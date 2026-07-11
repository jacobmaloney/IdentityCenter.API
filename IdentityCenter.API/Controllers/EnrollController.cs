using DataAccessLibrary.ControlPlane;
using DataAccessLibrary.Repositories;
using IdentityCenter.API.Authentication;
using IdentityCenter.API.Services.Enroll;
using Logging;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace IdentityCenter.API.Controllers;

/// <summary>Body of POST /api/agent/enroll. A malformed instanceId fails model binding → 400.</summary>
public sealed class AgentEnrollRequest
{
    public string? EnrollCode { get; set; }
    public Guid InstanceId { get; set; }
    public string? Name { get; set; }
    public string? Version { get; set; }
}

/// <summary>
/// POST /api/agent/enroll — anonymous one-shot Conduit enrollment (SaaS Day 4).
///
/// The ENROLL CODE is the whole authorization: minted by a tenant admin in the portal
/// (/admin/download-conduit), single-use, 15-minute TTL, hashed at rest, consumed atomically.
/// Presenting a live code proves tenant-admin intent, which is why enrollment ACTIVATES the agent
/// (unlike the June-14 anonymous bulk-push auto-register path, which stays IsActive=0 pending
/// manual admin activation — that path has no proof of admin intent).
///
/// SECURITY POSTURE:
///   - Uniform failure: unknown, expired, and already-used codes — and codes whose tenant is not
///     Active — all return the SAME 403 {"error":"invalid_or_expired_code"}. No oracle
///     distinguishes the cases (no tenant/suspension enumeration).
///   - Atomic consume: TryConsumeAsync is a single UPDATE…OUTPUT; two racing callers can never
///     both enroll on one code.
///   - Rate limited per resolved client IP (Enroll:MaxPerIpPerHour, default 10) inside the global
///     anonymous limiter in RateLimitingMiddleware.
///   - Key split: the response carries a per-agent key (Scope=Agent → heartbeat/commands only;
///     DENIED on /api/objects/* by TenantDataPolicy) and a tenant sync key (Scope=Tenant → data
///     endpoints). One credential can never do both jobs.
/// </summary>
[ApiController]
[Route("api/agent")]
public sealed class EnrollController : ControllerBase
{
    private const int MaxEnrollCodeLength = 128;
    private const int MaxNameLength = 100;
    private const int MaxVersionLength = 64;

    private readonly IEnrollCodeRepository _enrollCodes;
    private readonly ITenantRegistryRepository _tenantRegistry;
    private readonly ITenantApiKeyRepository _tenantKeys;
    private readonly IAgentRegistryRepository _agents;
    private readonly IControlPlaneAuditRepository _audit;
    private readonly EnrollRateLimiter _rateLimiter;
    private readonly IConfiguration _configuration;
    private readonly IGlobalLogger _logger;

    public EnrollController(
        IEnrollCodeRepository enrollCodes,
        ITenantRegistryRepository tenantRegistry,
        ITenantApiKeyRepository tenantKeys,
        IAgentRegistryRepository agents,
        IControlPlaneAuditRepository audit,
        EnrollRateLimiter rateLimiter,
        IConfiguration configuration,
        IGlobalLogger logger)
    {
        _enrollCodes = enrollCodes;
        _tenantRegistry = tenantRegistry;
        _tenantKeys = tenantKeys;
        _agents = agents;
        _audit = audit;
        _rateLimiter = rateLimiter;
        _configuration = configuration;
        _logger = logger;
    }

    [HttpPost("enroll")]
    [AllowAnonymous]
    [RequestSizeLimit(16 * 1024)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> Enroll([FromBody] AgentEnrollRequest? request, CancellationToken ct)
    {
        // ── Strict payload validation (400) ─────────────────────────────────
        if (request is null || string.IsNullOrWhiteSpace(request.EnrollCode))
            return BadRequest(new { error = "enrollCode is required" });
        if (request.EnrollCode.Length > MaxEnrollCodeLength)
            return BadRequest(new { error = "enrollCode is too long" });
        if (request.InstanceId == Guid.Empty)
            return BadRequest(new { error = "instanceId is required and must be a non-empty GUID" });
        if (request.Name is { Length: > MaxNameLength })
            return BadRequest(new { error = $"name must be {MaxNameLength} characters or fewer" });
        if (request.Version is { Length: > MaxVersionLength })
            return BadRequest(new { error = $"version must be {MaxVersionLength} characters or fewer" });

        var clientIp = ClientIp.Resolve(HttpContext, _configuration);

        // ── Per-IP rate limit (429) ─────────────────────────────────────────
        if (!_rateLimiter.TryAcquire(clientIp, out var retryAfterSeconds))
        {
            Response.Headers["Retry-After"] = retryAfterSeconds.ToString();
            return StatusCode(StatusCodes.Status429TooManyRequests, new { error = "Too many enrollment attempts. Try again later." });
        }

        // ── Atomic single-use consume; uniform 403 for every failure mode ───
        var tenantId = await _enrollCodes.TryConsumeAsync(request.EnrollCode, request.InstanceId, ct);
        if (tenantId is null)
        {
            _logger.LogWarning("Agent enroll rejected (bad/expired/used code) from {Ip}", clientIp);
            // No tenant/slug on an unknown-code reject — there is nothing verified to attribute it to.
            await _audit.TryWriteAsync("agent-enroll", "EnrollRejected", null, null, clientIp,
                $"reason=invalid_or_expired_code; instanceId={request.InstanceId}", ct);
            return InvalidCode();
        }

        var tenant = await _tenantRegistry.GetByIdAsync(tenantId.Value, ct);
        if (tenant is null || tenant.Status != TenantStatus.Active ||
            string.IsNullOrWhiteSpace(tenant.IcDbConnectionString))
        {
            // Same uniform response as an invalid code — a consumed code against a suspended /
            // half-provisioned tenant must not leak that tenant's state.
            _logger.LogWarning("Agent enroll rejected (tenant {TenantId} not servable) from {Ip}", tenantId, clientIp);
            await _audit.TryWriteAsync("agent-enroll", "EnrollRejected", tenantId, null, clientIp,
                $"reason=tenant_not_servable; instanceId={request.InstanceId}", ct);
            return InvalidCode();
        }

        var agentName = SanitizeName(request.Name) ?? $"Conduit-{N8(request.InstanceId)}";
        var n8 = N8(request.InstanceId);
        Guid? mintedAgentKeyId = null;
        Guid? mintedSyncKeyId = null;
        var activatedByUs = false;

        try
        {
            // ── Create/activate the agent in the TENANT DB ──────────────────
            // IAgentRegistryRepository is ambient-routed (DapperRepositoryBase); this anonymous
            // request has no tenant claims, so we install the tenant's decrypted connection
            // explicitly for just this block. ACTIVATION RATIONALE: the enroll code IS the
            // tenant-admin authorization (admin minted it in the tenant UI, single-use, 15-min
            // TTL), so enrollment activates — unlike the June-14 anonymous auto-register path,
            // which stays IsActive=0.
            //
            // COLLISION GUARD (Worf pass-2 M1): enrollment requires the instanceId to be UNKNOWN
            // or INACTIVE in this tenant. An id already owned by a LIVE agent is rejected — a code
            // holder must not be able to claim a running agent's identity and mint a second live
            // credential for it (the claim channel's READPAST is first-wins, so a duplicate would
            // intercept commands). Re-enrolling an inactive/deactivated agent stays allowed: that
            // is the lost-credentials recovery path (admin deactivates the agent, mints a fresh
            // code, re-enrolls).
            bool activeCollision;
            TenantConnectionAccessor.Current = new FixedConnectionResolver(tenant.IcDbConnectionString!);
            try
            {
                var existingAgent = await _agents.GetByIdAsync(request.InstanceId);
                activeCollision = existingAgent is { IsActive: true };
                if (!activeCollision)
                {
                    await _agents.CreateOrGetWithIdAsync(request.InstanceId, agentName, location: null,
                        capabilities: null, active: false);
                    await _agents.SetActiveAsync(request.InstanceId, true);
                    activatedByUs = true;
                }
            }
            finally
            {
                TenantConnectionAccessor.Current = null;
            }

            if (activeCollision)
            {
                // Uniform 403 body — the response must not reveal that the id exists (no oracle);
                // the audit trail carries the real reason for the operator.
                _logger.LogWarning("Agent enroll rejected (instance {InstanceId} is already an ACTIVE agent in tenant {Slug}) from {Ip}",
                    request.InstanceId, tenant.Slug, clientIp);
                await _audit.TryWriteAsync("agent-enroll", "EnrollRejected", tenant.Id, tenant.Slug, clientIp,
                    $"reason=active_instance_collision; instanceId={request.InstanceId}", ct);
                return InvalidCode();
            }

            // ── Mint the credential pair (control plane) ────────────────────
            // Re-enroll hygiene (Worf pass-2 LOW-1 + HIGH-1): retire EVERY prior live credential
            // for this (tenant, agent) first — the agent identity key(s) AND the bound sync
            // key(s), so a reinstall never orphans a live data-plane credential. The legacy-name
            // sweep also catches sync keys minted before AgentId binding existed (their mint name
            // is deterministic per instance).
            var syncKeyName = $"conduit-sync-{n8}";
            var priorRevoked = await _tenantKeys.RevokeKeysForAgentAsync(
                tenant.Id, request.InstanceId, syncKeyName, ct);
            if (priorRevoked > 0)
                _logger.LogInformation("Enroll: revoked {Count} prior key(s) (agent + sync) for instance {InstanceId} tenant {Slug}",
                    priorRevoked, request.InstanceId, tenant.Slug);

            var (agentKeyId, agentApiKey) = await _tenantKeys.CreateAgentAsync(
                tenant.Id, request.InstanceId, $"conduit-agent-{n8}", ct);
            mintedAgentKeyId = agentKeyId;
            // Sync key is Scope=Tenant (data endpoints) but BOUND to the instance via AgentId so
            // the next re-enroll / deactivate revokes it (HIGH-1). Binding is lineage, not
            // identity — ValidateAsync never surfaces AgentId for Tenant scope, so the claim set
            // is unchanged.
            var (syncKeyId, syncApiKey) = await _tenantKeys.CreateTenantKeyForAgentAsync(
                tenant.Id, request.InstanceId, syncKeyName, ct);
            mintedSyncKeyId = syncKeyId;

            await _audit.TryWriteAsync("agent-enroll", "AgentEnrolled", tenant.Id, tenant.Slug, clientIp,
                $"instanceId={request.InstanceId}; agent={agentName}; version={SanitizeName(request.Version)}", ct);
            await _audit.TryWriteAsync("agent-enroll", "EnrollKeysMinted", tenant.Id, tenant.Slug, clientIp,
                $"agentKeyId={agentKeyId}; syncKeyId={syncKeyId}; instanceId={request.InstanceId}", ct);

            _logger.LogInformation("Agent {InstanceId} enrolled to tenant {Slug} from {Ip}",
                request.InstanceId, tenant.Slug, clientIp);

            var baseUrl = _configuration["Enroll:PublicBaseUrl"];
            if (string.IsNullOrWhiteSpace(baseUrl))
                baseUrl = $"{Request.Scheme}://{Request.Host}";

            return Ok(new
            {
                baseUrl,
                tenantSlug = tenant.Slug,
                agentId = request.InstanceId,
                agentApiKey,
                syncApiKey
            });
        }
        catch (Exception ex)
        {
            // The code is already consumed — retry requires a FRESH code (single-use is the
            // security property; we never un-consume). Best-effort cleanup so a mid-flight failure
            // cannot leave an ACTIVE keyless agent row (it would skew agent targeting) or an
            // orphaned credential.
            _logger.LogError(ex, "Agent enroll failed mid-flight for instance {InstanceId} tenant {Slug} — cleaning up",
                request.InstanceId, tenant.Slug);
            try
            {
                if (mintedAgentKeyId is Guid ak) await _tenantKeys.RevokeAsync(ak, CancellationToken.None);
                if (mintedSyncKeyId is Guid sk) await _tenantKeys.RevokeAsync(sk, CancellationToken.None);
                // Deactivate ONLY an agent WE activated this request — never a pre-existing active
                // agent (e.g. when the collision lookup itself threw mid-flight).
                if (activatedByUs)
                {
                    TenantConnectionAccessor.Current = new FixedConnectionResolver(tenant.IcDbConnectionString!);
                    await _agents.SetActiveAsync(request.InstanceId, false);
                }
            }
            catch (Exception cleanupEx)
            {
                _logger.LogError(cleanupEx, "Enroll cleanup failed for instance {InstanceId} tenant {Slug}",
                    request.InstanceId, tenant.Slug);
            }
            finally
            {
                TenantConnectionAccessor.Current = null;
            }
            return StatusCode(StatusCodes.Status500InternalServerError, new { error = "enrollment_failed" });
        }
    }

    /// <summary>The ONE failure response for unknown/expired/used codes and unservable tenants.</summary>
    private ObjectResult InvalidCode() =>
        StatusCode(StatusCodes.Status403Forbidden, new { error = "invalid_or_expired_code" });

    private static string N8(Guid id) => id.ToString("N")[..8];

    /// <summary>Trims and strips control characters. Returns null when nothing printable remains.</summary>
    private static string? SanitizeName(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var cleaned = new string(raw.Trim().Where(c => !char.IsControl(c)).ToArray());
        return cleaned.Length == 0 ? null : cleaned;
    }
}
