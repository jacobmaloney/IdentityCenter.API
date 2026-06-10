using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Authentication.OAuth;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.SqlClient;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Dapper;
using DataAccessLibrary.Models;
using DataAccessLibrary.Repositories;
using Common.Encryption;
using System.Text.Json;

namespace IdentityCenter.API.Authentication;

/// <summary>
/// Dynamic external identity-provider registration, ported from the IdentityCenter WebPortal
/// (WebPortal/Services/DynamicAuthenticationService.cs). Reads the SAME IdentityProviders table
/// the portal's configuration page writes (shared database), decrypts client secrets with the
/// SAME DataProtection keyring, and registers one authentication scheme per enabled provider —
/// so an IDP configured once in the portal works on this API's admin login too.
///
/// STARTUP-TIME registration, exactly like the WebPortal: providers are read from the database
/// once while the service collection is being built. A provider added or changed in the portal
/// AFTER this service starts requires a service RESTART to appear here. (The portal has the
/// same property — this is IC's behavior, mirrored deliberately.)
///
/// Deliberate divergences from the WebPortal copy (each scoped to this API's deployment shape;
/// nothing in the portal is touched):
///   1. Correlation/nonce cookies use SecurePolicy=SameAsRequest + SameSite=Lax instead of the
///      framework default (None+Secure-only). This service runs on plain HTTP inside the lab
///      LAN; Secure-only correlation cookies would silently never be stored and every callback
///      would fail. Lax is safe here because the providers use response_type=code, whose
///      callback arrives as a top-level GET (Lax cookies are sent). On an HTTPS deployment the
///      cookies upgrade to Secure automatically.
///   2. AzureAD providers with a multi-tenant authority (common/organizations/consumers) are
///      REFUSED registration on this admin surface — any Azure AD account in any tenant could
///      otherwise satisfy the issuer check (account-takeover shape). The portal accepts what
///      the configuration page wrote; this surface requires an explicit tenant GUID.
///   3. SaveTokens=false (portal uses true). The admin UI never uses the provider tokens, and
///      not persisting access tokens into the auth cookie is strictly safer on plain HTTP.
///   4. SignInScheme is set explicitly to the Identity external cookie so the registration is
///      independent of this app's DefaultScheme (which is the X-API-Key scheme, not Identity).
/// </summary>
public static class DynamicAuthenticationExtensions
{
    public static async Task AddDynamicProvidersAsync(
        this AuthenticationBuilder authBuilder,
        IServiceProvider serviceProvider,
        ILogger logger)
    {
        logger.LogInformation("Starting dynamic authentication provider registration");

        try
        {
            using var scope = serviceProvider.CreateScope();
            var adminRepository = scope.ServiceProvider.GetRequiredService<IAdminRepository>();
            var encryptionService = scope.ServiceProvider.GetRequiredService<IEncryptionService>();
            var configuration = scope.ServiceProvider.GetRequiredService<IConfiguration>();

            // Check if the database schema is ready before querying IdentityProviders.
            // Prevents errors when the database exists but IC's migrations haven't run yet.
            if (!await IsDatabaseSchemaReadyAsync(configuration, logger))
            {
                logger.LogInformation("Database schema not ready - skipping dynamic authentication provider registration");
                return;
            }

            var allProviders = await adminRepository.GetIdentityProvidersAsync();
            var providers = allProviders.Where(p => p.IsEnabled).ToList();

            logger.LogInformation("Found {Count} enabled identity providers in database", providers.Count);

            foreach (var provider in providers)
            {
                try
                {
                    logger.LogDebug("Registering provider: {Name} (Type: {Type})", provider.Name, provider.Type);

                    switch (provider.Type)
                    {
                        case "AzureAD":
                            RegisterAzureAD(authBuilder, provider, encryptionService, logger);
                            break;

                        case "OIDC":
                            RegisterOIDC(authBuilder, provider, encryptionService, logger);
                            break;

                        case "OAuth":
                            RegisterOAuth(authBuilder, provider, encryptionService, logger);
                            break;

                        case "Local":
                            logger.LogDebug("Skipping Local provider (handled by ASP.NET Core Identity)");
                            break;

                        default:
                            logger.LogWarning("Unknown provider type: {Type} for provider {Name}", provider.Type, provider.Name);
                            break;
                    }
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Failed to register provider: {Name}", provider.Name);
                }
            }

            logger.LogInformation("Dynamic authentication provider registration completed");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error during dynamic authentication provider registration");
            throw;
        }
    }

    /// <summary>
    /// Lightweight existence probe for the IdentityProviders table (same as the WebPortal's),
    /// so a box pointed at an un-migrated database still boots with local login only.
    /// </summary>
    private static async Task<bool> IsDatabaseSchemaReadyAsync(IConfiguration configuration, ILogger logger)
    {
        try
        {
            var connectionString = configuration.GetConnectionString("DefaultConnection");
            if (string.IsNullOrEmpty(connectionString))
            {
                logger.LogDebug("No connection string configured - schema not ready");
                return false;
            }

            await using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync();
            await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM IdentityProviders");

            logger.LogDebug("Database schema is ready - IdentityProviders table exists");
            return true;
        }
        catch (SqlException ex) when (ex.Number == 208)
        {
            logger.LogDebug("IdentityProviders table does not exist - schema not ready");
            return false;
        }
        catch (SqlException ex)
        {
            logger.LogWarning(ex, "SQL error checking database schema readiness (Error {ErrorNumber})", ex.Number);
            return false;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not connect to database to check schema readiness");
            return false;
        }
    }

    private static Dictionary<string, string> GetClaimMappings(IdentityProvider provider, ILogger logger)
    {
        var claimMappings = new Dictionary<string, string>();

        try
        {
            var configDoc = JsonDocument.Parse(provider.Configuration);
            if (configDoc.RootElement.TryGetProperty("ClaimMappings", out var mappingsElement) &&
                mappingsElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var mapping in mappingsElement.EnumerateArray())
                {
                    if (mapping.TryGetProperty("ExternalClaim", out var externalClaim) &&
                        mapping.TryGetProperty("InternalProperty", out var internalProperty))
                    {
                        var external = externalClaim.GetString();
                        var internal_ = internalProperty.GetString();

                        if (!string.IsNullOrWhiteSpace(external) && !string.IsNullOrWhiteSpace(internal_))
                        {
                            claimMappings[external] = internal_;
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to parse claim mappings for provider {Name}", provider.Name);
        }

        return claimMappings;
    }

    private static string? DecryptSecret(IEncryptionService encryptionService, string? encryptedValue, string providerName, ILogger logger)
    {
        if (string.IsNullOrEmpty(encryptedValue))
            return null;

        try
        {
            // Same keyring + application name as the WebPortal (C:\ProgramData\IdentityCenter\Keys),
            // so a secret the portal's configuration page encrypted decrypts here unchanged.
            return encryptionService.DecryptAsync(encryptedValue).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not decrypt secret for provider {Name}, using as plain text", providerName);
            return encryptedValue;
        }
    }

    /// <summary>
    /// Divergence #1: make the OIDC round trip workable on the plain-HTTP LAN deployment.
    /// Two coupled changes (verified live — either alone is insufficient):
    ///   - Correlation/nonce cookies: SecurePolicy=SameAsRequest + SameSite=Lax instead of the
    ///     framework default (SameSite=None + Secure-only). Secure-only cookies never get
    ///     stored over http, so every callback would fail with a correlation error.
    ///   - ResponseMode=query instead of the handler default form_post. Lax cookies accompany
    ///     top-level GET navigations only — a cross-site form POST callback would arrive
    ///     WITHOUT the correlation cookie even with the relaxed policy above. With
    ///     response_type=code the query callback carries only {code, state} (no tokens), and
    ///     the code is single-use + PKCE-bound + requires the client secret to redeem.
    /// Both upgrade cleanly on an HTTPS deployment (cookies become Secure automatically).
    /// </summary>
    private static void ApplyHttpFriendlyCookiePolicy(OpenIdConnectOptions options)
    {
        options.ResponseMode = OpenIdConnectResponseMode.Query;
        options.CorrelationCookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        options.CorrelationCookie.SameSite = SameSiteMode.Lax;
        options.NonceCookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        options.NonceCookie.SameSite = SameSiteMode.Lax;
    }

    private static void RegisterAzureAD(AuthenticationBuilder authBuilder, IdentityProvider provider, IEncryptionService encryptionService, ILogger logger)
    {
        var configDoc = JsonDocument.Parse(provider.Configuration);
        var root = configDoc.RootElement;

        var clientId = root.TryGetProperty("ClientId", out var cidEl) ? cidEl.GetString() : null;
        var clientSecretRaw = root.TryGetProperty("ClientSecret", out var csEl) ? csEl.GetString() : null;

        var clientSecret = DecryptSecret(encryptionService, clientSecretRaw, provider.Name, logger);

        var tenantId = root.TryGetProperty("TenantId", out var tidEl) ? tidEl.GetString() : null;
        var callbackPath = root.TryGetProperty("CallbackPath", out var cbEl) ? cbEl.GetString() : null;
        if (string.IsNullOrWhiteSpace(callbackPath)) callbackPath = "/signin-oidc";

        if (string.IsNullOrEmpty(clientId) || string.IsNullOrEmpty(clientSecret) || string.IsNullOrEmpty(tenantId))
        {
            logger.LogError("Azure AD provider {Name} is missing required configuration. ClientId: {HasClientId}, ClientSecret: {HasSecret}, TenantId: {HasTenantId}",
                provider.Name,
                !string.IsNullOrEmpty(clientId),
                !string.IsNullOrEmpty(clientSecret),
                !string.IsNullOrEmpty(tenantId));
            return;
        }

        // Divergence #2: tenant pinning. On this ADMIN surface a multi-tenant authority means any
        // Azure AD account anywhere passes issuer validation — refuse it and say why.
        if (tenantId is "common" or "organizations" or "consumers")
        {
            logger.LogError(
                "Azure AD provider {Name} uses multi-tenant authority '{TenantId}'. " +
                "The API admin surface requires an explicit tenant GUID; provider not registered.",
                provider.Name, tenantId);
            return;
        }

        authBuilder.AddOpenIdConnect(provider.Name, provider.Name, options =>
        {
            options.SignInScheme = IdentityConstants.ExternalScheme; // divergence #4 (explicit; DefaultScheme here is ApiKey)
            options.ClientId = clientId;
            options.ClientSecret = clientSecret;
            options.Authority = $"https://login.microsoftonline.com/{tenantId}/v2.0";
            options.ResponseType = "code";
            options.CallbackPath = callbackPath;
            options.SignedOutCallbackPath = "/signout-callback-oidc";
            options.SaveTokens = false; // divergence #3 (portal: true) — tokens are never used by the admin UI
            options.GetClaimsFromUserInfoEndpoint = true;

            ApplyHttpFriendlyCookiePolicy(options);

            var claimMappings = GetClaimMappings(provider, logger);
            if (claimMappings.Any())
            {
                options.ClaimActions.Clear();
                foreach (var mapping in claimMappings)
                {
                    options.ClaimActions.MapJsonKey(mapping.Value, mapping.Key);
                }
            }

            options.TokenValidationParameters.NameClaimType = "name";
            options.TokenValidationParameters.RoleClaimType = "roles";

            // Same scope set as the portal so the one app registration serves both surfaces.
            options.Scope.Clear();
            options.Scope.Add("openid");
            options.Scope.Add("profile");
            options.Scope.Add("email");
            options.Scope.Add("GroupMember.Read.All");
        });

        logger.LogInformation("Registered Azure AD provider: {Name}", provider.Name);
    }

    private static void RegisterOIDC(AuthenticationBuilder authBuilder, IdentityProvider provider, IEncryptionService encryptionService, ILogger logger)
    {
        var configDoc = JsonDocument.Parse(provider.Configuration);
        var root = configDoc.RootElement;

        var clientId = root.TryGetProperty("ClientId", out var cidEl) ? cidEl.GetString() : null;
        var clientSecret = DecryptSecret(encryptionService, root.TryGetProperty("ClientSecret", out var csEl) ? csEl.GetString() : null, provider.Name, logger);
        var authority = root.TryGetProperty("Authority", out var authEl) ? authEl.GetString() : null;
        var callbackPath = root.TryGetProperty("CallbackPath", out var cbEl) ? cbEl.GetString() : null;
        if (string.IsNullOrWhiteSpace(callbackPath)) callbackPath = "/signin-oidc";

        if (string.IsNullOrEmpty(clientId) || string.IsNullOrEmpty(clientSecret) || string.IsNullOrEmpty(authority))
        {
            logger.LogError("OIDC provider {Name} is missing required configuration", provider.Name);
            return;
        }

        authBuilder.AddOpenIdConnect(provider.Name, provider.Name, options =>
        {
            options.SignInScheme = IdentityConstants.ExternalScheme; // divergence #4
            options.ClientId = clientId;
            options.ClientSecret = clientSecret;
            options.Authority = authority;
            options.ResponseType = "code";
            options.CallbackPath = callbackPath;
            options.SignedOutCallbackPath = "/signout-callback-oidc";
            options.SaveTokens = false; // divergence #3
            options.GetClaimsFromUserInfoEndpoint = true;

            ApplyHttpFriendlyCookiePolicy(options);

            var claimMappings = GetClaimMappings(provider, logger);
            if (claimMappings.Any())
            {
                options.ClaimActions.Clear();
                foreach (var mapping in claimMappings)
                {
                    options.ClaimActions.MapJsonKey(mapping.Value, mapping.Key);
                }
            }

            options.TokenValidationParameters.NameClaimType = "name";
            options.TokenValidationParameters.RoleClaimType = "roles";

            options.Scope.Clear();
            options.Scope.Add("openid");
            options.Scope.Add("profile");
            options.Scope.Add("email");
        });

        logger.LogInformation("Registered OIDC provider: {Name}", provider.Name);
    }

    private static void RegisterOAuth(AuthenticationBuilder authBuilder, IdentityProvider provider, IEncryptionService encryptionService, ILogger logger)
    {
        var configDoc = JsonDocument.Parse(provider.Configuration);
        var root = configDoc.RootElement;

        var clientId = root.TryGetProperty("ClientId", out var cidEl) ? cidEl.GetString() : null;
        var clientSecret = DecryptSecret(encryptionService, root.TryGetProperty("ClientSecret", out var csEl) ? csEl.GetString() : null, provider.Name, logger);
        var authorizationEndpoint = root.TryGetProperty("AuthorizationEndpoint", out var aeEl) ? aeEl.GetString() : null;
        var tokenEndpoint = root.TryGetProperty("TokenEndpoint", out var teEl) ? teEl.GetString() : null;
        var userInformationEndpoint = root.TryGetProperty("UserInformationEndpoint", out var uiEl) ? uiEl.GetString() : null;

        if (string.IsNullOrEmpty(clientId) || string.IsNullOrEmpty(clientSecret) ||
            string.IsNullOrEmpty(authorizationEndpoint) || string.IsNullOrEmpty(tokenEndpoint))
        {
            logger.LogError("OAuth provider {Name} is missing required configuration", provider.Name);
            return;
        }

        authBuilder.AddOAuth(provider.Name, provider.Name, options =>
        {
            options.SignInScheme = IdentityConstants.ExternalScheme; // divergence #4
            options.ClientId = clientId;
            options.ClientSecret = clientSecret;
            options.AuthorizationEndpoint = authorizationEndpoint;
            options.TokenEndpoint = tokenEndpoint;
            options.CallbackPath = "/signin-oauth";
            options.SaveTokens = false; // divergence #3

            options.CorrelationCookie.SecurePolicy = CookieSecurePolicy.SameAsRequest; // divergence #1
            options.CorrelationCookie.SameSite = SameSiteMode.Lax;

            if (!string.IsNullOrEmpty(userInformationEndpoint))
            {
                options.UserInformationEndpoint = userInformationEndpoint;
            }

            options.Scope.Add("openid");
            options.Scope.Add("profile");
            options.Scope.Add("email");
        });

        logger.LogInformation("Registered OAuth provider: {Name}", provider.Name);
    }
}
