using Common.Encryption;
using DataAccessLibrary.Repositories;
using Logging;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace IdentityCenter.API.Controllers;

/// <summary>
/// Conduit self-registration endpoint. When an operator creates an
/// IdentityCenter-type Connected System inside Conduit (entering THIS IC's base
/// URL + an IC API key), Conduit calls here once to register itself back into
/// IC — so the IC↔Conduit PULL seam (sync-run history etc., consumed by
/// <c>WebPortal.Services.Conduit.ConduitClient</c>) lights up with no manual
/// paste on the IC side.
///
/// PER-TENANT WRITE (SaaS Day 4): authentication is the standard X-API-Key. The
/// presented key's tenant context — installed by <c>ApiKeyAuthenticationHandler</c>
/// into the ambient <c>TenantConnectionAccessor</c> — decides WHICH tenant's
/// Settings table this writes to. The body NEVER carries a tenant id, so a
/// tenant's Conduit can only ever register into THAT tenant. The single-tenant /
/// legacy key path writes DefaultConnection, exactly as before SaaS.
///
/// We write the SAME Settings rows (<c>Conduit</c> / <c>BaseUrl</c> /
/// <c>ApiToken</c>) that <c>ConduitConnectionSettings</c> uses in WebPortal —
/// replicated here with <see cref="IEncryptionService"/> + the
/// <see cref="IConfigurationRepository"/> Settings seam so the API takes NO
/// WebPortal dependency. The Conduit token is encrypted at rest and never logged
/// or echoed back.
/// </summary>
[ApiController]
[Route("api/conduit")]
[Authorize(Policy = "TenantDataPolicy")]
public sealed class ConduitController : ControllerBase
{
    // Mirror ConduitConnectionSettings (WebPortal) so the existing IC pull client
    // reads what we write here with no further change.
    private const string SettingsCategory = "Conduit";
    private const string KeyBaseUrl = "BaseUrl";
    private const string KeyToken = "ApiToken";

    private readonly IConfigurationRepository _config;
    private readonly IEncryptionService _encryption;
    private readonly IGlobalLogger _logger;

    public ConduitController(
        IConfigurationRepository config,
        IEncryptionService encryption,
        IGlobalLogger logger)
    {
        _config = config;
        _encryption = encryption;
        _logger = logger;
    }

    public sealed record RegisterConduitRequest(string? ConduitBaseUrl, string? ConduitToken);

    /// <summary>
    /// Idempotent registration. Re-registering overwrites the stored URL + token
    /// (the operator may have re-saved the connection in Conduit, rotating the
    /// auto-reg token). Returns a small confirmation — never the secret.
    /// </summary>
    [HttpPost("register")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Register([FromBody] RegisterConduitRequest request)
    {
        if (request is null)
            return BadRequest(new { error = "Request body is required." });

        if (string.IsNullOrWhiteSpace(request.ConduitBaseUrl))
            return BadRequest(new { error = "conduitBaseUrl is required." });

        if (string.IsNullOrWhiteSpace(request.ConduitToken))
            return BadRequest(new { error = "conduitToken is required." });

        // Validate + normalize the URL. We store this so IC's ConduitClient will
        // later CALL it — so it must be a sane absolute http/https URL. Reject
        // anything else rather than persist a value that becomes an SSRF/foot-gun
        // when the pull client dereferences it.
        if (!TryNormalizeHttpUrl(request.ConduitBaseUrl, out var normalizedUrl))
            return BadRequest(new { error = "conduitBaseUrl must be an absolute http(s) URL." });

        var token = request.ConduitToken.Trim();

        try
        {
            // Tenant routing is implicit: ConfigurationRepository : DapperRepositoryBase
            // resolves its connection per-call from TenantConnectionAccessor.Current,
            // which the auth handler set from the validated key. So these two writes
            // land in the CALLING key's tenant DB (or DefaultConnection for legacy).
            await _config.UpsertSettingAsync(SettingsCategory, KeyBaseUrl, normalizedUrl);

            var cipher = await _encryption.EncryptAsync(token);
            await _config.UpsertSettingAsync(
                SettingsCategory, KeyToken, cipher, dataType: "string", isEncrypted: true);

            // Audit-grade breadcrumb. tenant_id is the AUDIT claim only (set by the
            // handler from the validated row) — never read back to choose a DB. No
            // secret is logged.
            var tenantId = User.FindFirst("tenant_id")?.Value ?? "default";
            _logger.LogInformation(
                "Conduit self-registered for tenant {TenantId}: base URL {BaseUrl} stored, token stored encrypted.",
                tenantId, normalizedUrl);

            return Ok(new
            {
                registered = true,
                conduitBaseUrl = normalizedUrl,
                message = "Conduit registered. IdentityCenter will now pull sync history from this Conduit."
            });
        }
        catch (Exception ex)
        {
            // Don't leak the token or internal detail to the caller.
            _logger.LogError("Conduit registration failed while persisting settings.", ex);
            return StatusCode(StatusCodes.Status500InternalServerError,
                new { error = "Failed to persist Conduit registration." });
        }
    }

    /// <summary>
    /// Accepts only absolute http/https URLs. Returns the scheme+authority+path
    /// with any trailing slash trimmed (matching ConduitConnectionSettings'
    /// normalization), so the stored value is exactly what the pull client needs.
    /// </summary>
    private static bool TryNormalizeHttpUrl(string? value, out string normalized)
    {
        normalized = string.Empty;
        if (string.IsNullOrWhiteSpace(value)) return false;

        if (!Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri))
            return false;

        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            return false;

        normalized = uri.GetLeftPart(UriPartial.Path).TrimEnd('/');
        return !string.IsNullOrEmpty(normalized);
    }
}
