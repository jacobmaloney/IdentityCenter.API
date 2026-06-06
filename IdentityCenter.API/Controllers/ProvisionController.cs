using Dapper;
using DataAccessLibrary.ControlPlane;
using IdentityCenter.API.Models;
using IdentityCenter.API.Services;
using Logging;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;

namespace IdentityCenter.API.Controllers;

/// <summary>
/// SaaS tenant provisioning. THIS IS THE MOST SECURITY-SENSITIVE ENDPOINT IN THE PRODUCT — an
/// authenticated call here CREATES A DATABASE and runs the full V001..V135 schema. It is therefore
/// gated to the control-plane admin scope ONLY (<c>AdminPolicy</c> ⇒ claim scope=admin). A tenant /
/// agent key (scope=agent) can never reach it — authorization yields 403.
///
/// Flow is staged-async: POST creates the registry row + enqueues a background job + returns 202 with a
/// status URL; the caller (Travis's site) polls GET /api/provision/{id} for Provisioning → Active/Failed.
/// The generated admin credential is returned EXACTLY ONCE, on the first status read after Active.
/// </summary>
[ApiController]
[Route("api/provision")]
[Authorize(Policy = "AdminPolicy")]
public sealed class ProvisionController : ControllerBase
{
    /// <summary>Plan applied when the caller does not specify one. The default SaaS entry plan.</summary>
    private const string DefaultPlan = "Trial";

    /// <summary>Config key for the trial length in days. Default <see cref="DefaultTrialLengthDays"/>.</summary>
    private const string TrialLengthConfigKey = "SaaS:TrialLengthDays";
    private const int DefaultTrialLengthDays = 14;

    private readonly ITenantRegistryRepository _registry;
    private readonly ProvisioningQueue _queue;
    private readonly OneTimeCredentialVault _vault;
    private readonly Microsoft.Extensions.Configuration.IConfiguration _configuration;
    private readonly IGlobalLogger _logger;

    public ProvisionController(
        ITenantRegistryRepository registry,
        ProvisioningQueue queue,
        OneTimeCredentialVault vault,
        Microsoft.Extensions.Configuration.IConfiguration configuration,
        IGlobalLogger logger)
    {
        _registry = registry;
        _queue = queue;
        _vault = vault;
        _configuration = configuration;
        _logger = logger;
    }

    /// <summary>
    /// Resolves the trial length (days) from config, clamped to a sane range. A misconfigured or
    /// non-positive value falls back to the default rather than producing an already-expired trial.
    /// </summary>
    private int ResolveTrialLengthDays()
    {
        var days = _configuration.GetValue<int?>(TrialLengthConfigKey) ?? DefaultTrialLengthDays;
        if (days <= 0) days = DefaultTrialLengthDays;
        if (days > 3650) days = 3650; // 10y guard against absurd config
        return days;
    }

    /// <summary>
    /// Provision a new tenant (DB-per-tenant). Returns 202 + a status URL. Body: slug, displayName,
    /// region?, plan?, adminEmail?.
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> Provision([FromBody] ProvisionTenantRequest request, CancellationToken cancellationToken)
    {
        if (request is null)
            return BadRequest(new { error = "Request body is required." });

        if (string.IsNullOrWhiteSpace(request.DisplayName))
            return BadRequest(new { error = "displayName is required." });

        // (a) Validate + normalize the slug. Invalid → 400. This is the gate that keeps unsafe input
        //     out of any DB name; ToDatabaseName later re-validates as defense in depth.
        string slug;
        try
        {
            slug = TenantSlug.Normalize(request.Slug);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }

        // (a) Reject duplicate slug → 409.
        if (await _registry.SlugExistsAsync(slug, cancellationToken))
            return Conflict(new { error = $"A tenant with slug '{slug}' already exists." });

        // Resolve plan (default Trial) and, for a Trial plan, compute the trial expiry now so it is
        // recorded immediately and visible even while the tenant is still Provisioning. RECORDING ONLY:
        // nothing enforces this expiry yet (suspend-on-expiry / convert-to-paid is a tracked follow-up).
        var plan = string.IsNullOrWhiteSpace(request.Plan) ? DefaultPlan : request.Plan.Trim();
        DateTime? trialExpiresAt = plan.Equals(DefaultPlan, StringComparison.OrdinalIgnoreCase)
            ? DateTime.UtcNow.AddDays(ResolveTrialLengthDays())
            : null;

        // (b) Create the registry row in Provisioning. Secrets (conn string) are filled in by the
        //     background job; we do NOT compute or store them on the request thread.
        TenantRecord created;
        try
        {
            created = await _registry.CreateAsync(new TenantRecord
            {
                Slug = slug,
                DisplayName = request.DisplayName.Trim(),
                Region = string.IsNullOrWhiteSpace(request.Region) ? null : request.Region.Trim(),
                Plan = plan,
                TrialExpiresAt = trialExpiresAt,
                Status = TenantStatus.Provisioning
            }, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Provision: failed to create registry row for slug {Slug}", slug);
            return StatusCode(500, new { error = "Failed to create tenant record." });
        }

        // Enqueue the background provisioning job (catalog create + V001..V135 + admin seed). The queue
        // enforces an in-flight cap so a flood (e.g. a leaked admin key) cannot launch unbounded
        // concurrent CREATE DATABASE + migration storms. On capacity-exceeded we DELETE the just-created
        // registry row (it would otherwise orphan as a Provisioning row that never gets worked) and return
        // 429 so the caller can retry with backoff.
        var enqueued = _queue.TryEnqueue(created.Id, request.AdminEmail);
        if (enqueued == ProvisioningQueue.EnqueueResult.CapacityExceeded)
        {
            try { await _registry.DeleteAsync(created.Id, cancellationToken); }
            catch (Exception delEx)
            {
                _logger.LogError(delEx, "Provision: failed to roll back registry row {TenantId} after capacity rejection", created.Id);
            }
            _logger.LogWarning("Provision: tenant {Slug} rejected — provisioning queue at capacity", slug);
            Response.Headers["Retry-After"] = "120";
            return StatusCode(StatusCodes.Status429TooManyRequests, new
            {
                error = "Provisioning capacity reached. Too many tenants are being provisioned right now; retry shortly."
            });
        }

        _logger.LogInformation("Provision: tenant {Slug} ({TenantId}) accepted; provisioning queued", slug, created.Id);

        var statusUrl = $"{Request.Scheme}://{Request.Host}/api/provision/{created.Id}";
        Response.Headers.Location = statusUrl;
        return Accepted(statusUrl, new ProvisionAcceptedResponse
        {
            TenantId = created.Id,
            Slug = slug,
            Status = TenantStatus.Provisioning.ToString(),
            StatusUrl = statusUrl,
            Message = "Tenant provisioning started. Poll the status URL until status is Active or Failed."
        });
    }

    /// <summary>
    /// Status of a provisioning job. On the FIRST read after the tenant reaches Active, the response
    /// includes the one-time access bundle (admin email + generated password); thereafter it does not.
    /// </summary>
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetStatus(Guid id, CancellationToken cancellationToken)
    {
        var tenant = await _registry.GetByIdAsync(id, cancellationToken);
        if (tenant is null)
            return NotFound(new { error = "Tenant not found." });

        var response = new ProvisionStatusResponse
        {
            TenantId = tenant.Id,
            Slug = tenant.Slug,
            DisplayName = tenant.DisplayName,
            Status = tenant.Status.ToString(),
            Region = tenant.Region,
            Plan = tenant.Plan,
            TrialExpiresAt = tenant.TrialExpiresAt,
            CreatedAt = tenant.CreatedAt,
            ModifiedAt = tenant.ModifiedAt
        };

        if (tenant.Status == TenantStatus.Active)
        {
            // Report the applied schema version (best-effort; never fail the status read on this).
            response.SchemaVersion = await TryReadSchemaVersionAsync(tenant.Id, tenant.IcDbConnectionString, cancellationToken);

            // Surface the one-time credential exactly once. Subsequent reads get null.
            response.AccessBundle = _vault.TakeOnce(tenant.Id);
        }
        else if (tenant.Status == TenantStatus.Failed)
        {
            response.Error = "Provisioning failed. See server logs for the sanitized cause; the partial database (if any) was left for diagnosis.";
        }

        return Ok(response);
    }

    /// <summary>
    /// Reads MAX(Version) from the tenant DB's __SchemaVersion table using the decrypted tenant
    /// connection string. Returns null on any error — this is informational only and must never break
    /// the status read.
    /// </summary>
    private async Task<int?> TryReadSchemaVersionAsync(Guid id, string? tenantConnectionString, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(tenantConnectionString))
            return null;
        try
        {
            await using var conn = new SqlConnection(tenantConnectionString);
            await conn.OpenAsync(cancellationToken);
            var exists = await conn.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = '__SchemaVersion'");
            if (exists == 0) return null;
            return await conn.ExecuteScalarAsync<int?>("SELECT ISNULL(MAX(Version), 0) FROM __SchemaVersion");
        }
        catch
        {
            // Intentionally swallow without logging the exception: a SQL error message can echo the
            // connection string (which contains the master credential). Schema version is informational
            // only; a null here just omits it from the status response.
            _logger.LogDebug("Provision status: schema version unavailable for tenant {TenantId}", id);
            return null;
        }
    }
}
