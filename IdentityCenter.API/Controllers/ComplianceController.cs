using DataAccessLibrary.Repositories;
using Logging;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace IdentityCenter.API.Controllers;

/// <summary>
/// Public API for compliance / policy violations.
/// </summary>
[ApiController]
[Route("api/compliance")]
[Authorize(Policy = "TenantDataPolicy")]
public class ComplianceController : ControllerBase
{
    private readonly IComplianceQueryRepository _complianceRepo;
    private readonly IGlobalLogger _logger;

    public ComplianceController(IComplianceQueryRepository complianceRepo, IGlobalLogger logger)
    {
        _complianceRepo = complianceRepo;
        _logger = logger;
    }

    /// <summary>
    /// Paged list of policy violations. Filters: severity, status.
    /// </summary>
    /// <remarks>
    /// Each row returns a flat shape — id, type, severity, status, objectName,
    /// detectedAt, description — that matches the public-API contract.
    /// </remarks>
    [HttpGet("violations")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetViolations(
        [FromQuery] string? severity = null,
        [FromQuery] string? status = "Open",
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50)
    {
        if (page < 1) page = 1;
        if (pageSize < 1) pageSize = 50;
        if (pageSize > 500) pageSize = 500;

        try
        {
            var (items, total) = await _complianceRepo.GetViolationsPagedAsync(
                status: status, severity: severity, page: page, pageSize: pageSize);

            var data = items.Select(v => new
            {
                id = v.Id,
                type = v.CompliancePolicy?.Name ?? "Unknown Policy",
                severity = v.Severity,
                status = v.Status,
                objectName = v.EntityDisplayName ?? v.Entity?.DisplayName,
                detectedAt = v.DetectedAt,
                description = v.Description
            });

            return Ok(new
            {
                data,
                page,
                pageSize,
                total
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to query violations (severity={Severity}, status={Status})", severity, status);
            return StatusCode(500, new { error = "Failed to query violations" });
        }
    }
}
