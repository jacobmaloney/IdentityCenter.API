using System.Security.Claims;
using System.Text.Encodings.Web;
using DataAccessLibrary.ControlPlane;
using DataAccessLibrary.Repositories;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace IdentityCenter.API.Authentication;

public class ApiKeyAuthenticationOptions : AuthenticationSchemeOptions
{
    public string HeaderName { get; set; } = "X-API-Key";
}

/// <summary>
/// X-API-Key authentication with TWO key authorities, tried in order:
///
///  1. CONTROL-PLANE keys (multi-tenant SaaS). If a ControlPlane connection string is configured, the
///     presented key is validated against the control-plane <c>TenantApiKeys</c> store. On a match we
///     resolve {TenantId, Scope}, populate the request-scoped <see cref="ITenantContext"/>, and INSTALL
///     the per-request <see cref="ITenantConnectionResolver"/> into the ambient
///     <see cref="TenantConnectionAccessor"/> so all downstream data access hits ONLY that tenant's DB.
///     Claims: scope=admin|tenant (drives [Authorize] policies) and tenant_id (audit only — NEVER read
///     back to choose a DB; the connection is driven by ITenantContext, set from the validated row).
///
///  2. LEGACY single-tenant keys. If there is no control plane, or the key did not match the control
///     plane, we fall back to the EXISTING <see cref="IApiKeyRepository"/> validation against the
///     IC DefaultConnection database — unchanged from before SaaS. No tenant context is set, so the
///     connection resolver falls back to DefaultConnection. This keeps the current IC.API / WebPortal
///     deployment working exactly as it did.
///
/// BACKWARD COMPAT STATEMENT: multi-tenant resolution engages ONLY for control-plane-issued keys. Any
/// key that the control plane does not recognise (including every key minted by today's single-tenant
/// IC) flows to path (2) and behaves identically to before. The legacy path can never set a tenant
/// context, so it cannot be abused to reach a tenant DB.
/// </summary>
public class ApiKeyAuthenticationHandler : AuthenticationHandler<ApiKeyAuthenticationOptions>
{
    private readonly IApiKeyRepository _apiKeyRepository;
    private readonly ITenantApiKeyRepository _tenantApiKeyRepository;
    private readonly ITenantContext _tenantContext;
    private readonly ITenantConnectionResolver _tenantConnectionResolver;
    private readonly IConfiguration _configuration;
    private readonly ILogger<ApiKeyAuthenticationHandler> _logger;

    public ApiKeyAuthenticationHandler(
        IOptionsMonitor<ApiKeyAuthenticationOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        IApiKeyRepository apiKeyRepository,
        ITenantApiKeyRepository tenantApiKeyRepository,
        ITenantContext tenantContext,
        ITenantConnectionResolver tenantConnectionResolver,
        IConfiguration configuration)
        : base(options, logger, encoder)
    {
        _apiKeyRepository = apiKeyRepository;
        _tenantApiKeyRepository = tenantApiKeyRepository;
        _tenantContext = tenantContext;
        _tenantConnectionResolver = tenantConnectionResolver;
        _configuration = configuration;
        _logger = logger.CreateLogger<ApiKeyAuthenticationHandler>();
    }

    private bool ControlPlaneConfigured =>
        !string.IsNullOrWhiteSpace(
            _configuration.GetConnectionString(ControlPlaneMigrationService.ConnectionStringName));

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(Options.HeaderName, out var apiKeyHeaderValues))
            return AuthenticateResult.NoResult();

        var providedApiKey = apiKeyHeaderValues.FirstOrDefault();
        if (string.IsNullOrWhiteSpace(providedApiKey))
            return AuthenticateResult.NoResult();

        try
        {
            // ── Path 1: control-plane (multi-tenant) keys ───────────────────────
            if (ControlPlaneConfigured)
            {
                var cp = await _tenantApiKeyRepository.ValidateAsync(providedApiKey, Context.RequestAborted);
                if (cp.IsValid)
                    return SucceedControlPlane(cp);

                // Not a control-plane key → fall through to the legacy authority. We do NOT fail here:
                // a legacy single-tenant key is still valid on a box that also has a control plane.
            }

            // ── Path 2: legacy single-tenant keys (existing behavior) ───────────
            var validationResult = await _apiKeyRepository.ValidateApiKeyAsync(providedApiKey, GetClientIpAddress());

            if (!validationResult.IsValid)
            {
                _logger.LogWarning("Invalid API key attempt from IP {IpAddress}: {Reason}",
                    GetClientIpAddress(), validationResult.FailureReason);
                return AuthenticateResult.Fail(validationResult.FailureReason ?? "Invalid API key");
            }

            // Legacy path: NO tenant context set → connection resolver falls back to DefaultConnection.
            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.NameIdentifier, validationResult.KeyId.ToString()),
                new Claim(ClaimTypes.Name, validationResult.KeyName ?? "API Key"),
                new Claim("key_type", validationResult.KeyType ?? "Unknown"),
            };

            if (!string.IsNullOrEmpty(validationResult.Scopes))
            {
                foreach (var scope in validationResult.Scopes.Split(',', StringSplitOptions.RemoveEmptyEntries))
                    claims.Add(new Claim("scope", scope.Trim()));
            }

            if (validationResult.AgentId.HasValue)
                claims.Add(new Claim("agent_id", validationResult.AgentId.Value.ToString()));

            if (!string.IsNullOrEmpty(validationResult.UserId))
                claims.Add(new Claim("user_id", validationResult.UserId));

            var identity = new ClaimsIdentity(claims, Scheme.Name);
            var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name);

            _logger.LogInformation("API key authenticated (legacy): {KeyName} ({KeyType}) from {IpAddress}",
                validationResult.KeyName, validationResult.KeyType, GetClientIpAddress());

            return AuthenticateResult.Success(ticket);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error validating API key");
            return AuthenticateResult.Fail("Error validating API key");
        }
    }

    /// <summary>
    /// Builds the principal for a validated control-plane key AND wires the per-request tenant routing:
    /// sets <see cref="ITenantContext"/> (the trust anchor for connection resolution) and installs the
    /// resolver into the ambient accessor so the data layer routes to this tenant's DB only.
    /// </summary>
    private AuthenticateResult SucceedControlPlane(TenantApiKeyValidationResult cp)
    {
        // Authorization scope claim. Control-plane Admin ⇒ "admin" (satisfies AdminPolicy). Tenant ⇒
        // "tenant" (satisfies TenantPolicy; deliberately does NOT satisfy AdminPolicy → 403 on /provision).
        var scopeClaim = cp.Scope == TenantApiKeyScope.Admin ? "admin" : "tenant";

        // NOTE (Day-6 fix): we deliberately do NOT set ITenantContext or TenantConnectionAccessor here.
        // This handler runs as an awaited SUBTREE of the authorization middleware, and an AsyncLocal set
        // inside an awaited callee does not flow back up to the caller — so any value set here would be
        // GONE before the controller runs (every tenant request then silently hit DefaultConnection). The
        // tenant routing is installed instead by TenantConnectionScopeMiddleware, which runs as an ANCESTOR
        // frame of the controller and reads the SAME trust anchor: the server-set claims below, derived
        // here from the validated key row. The key_type + tenant_id claims are the contract between the two.

        var claims = new List<Claim>
        {
            new Claim(ClaimTypes.NameIdentifier, cp.KeyId.ToString()),
            new Claim(ClaimTypes.Name, cp.Name ?? "Control-plane key"),
            new Claim("key_type", cp.Scope == TenantApiKeyScope.Admin ? "ControlPlaneAdmin" : "Tenant"),
            new Claim("scope", scopeClaim),
        };

        // tenant_id is AUDIT METADATA ONLY. It is never read back to select a database — the connection
        // is driven exclusively by ITenantContext (set above from the validated row). Emitting it as a
        // claim lets controllers/audit see who they served without reopening an IDOR vector.
        if (cp.TenantId is Guid tid)
            claims.Add(new Claim("tenant_id", tid.ToString()));

        var identity = new ClaimsIdentity(claims, Scheme.Name);
        var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name);

        _logger.LogInformation("API key authenticated (control-plane {Scope}) key {KeyId} tenant {TenantId} from {Ip}",
            cp.Scope, cp.KeyId, cp.TenantId, GetClientIpAddress());

        return AuthenticateResult.Success(ticket);
    }

    // X-Forwarded-For is honored only behind a configured trusted proxy
    // (Api:TrustedProxies); otherwise the socket address wins. A direct caller
    // could previously spoof its logged/stored IP with an arbitrary XFF header.
    private string GetClientIpAddress() => ClientIp.Resolve(Context, _configuration);
}
