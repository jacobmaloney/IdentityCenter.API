using DataAccessLibrary.Repositories;
using Logging;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace IdentityCenter.API.Controllers;

/// <summary>
/// Public API for canned reports — currently the M365 license-waste roll-up.
/// </summary>
[ApiController]
[Route("api/reports")]
[Authorize(Policy = "TenantDataPolicy")]
public class ReportsController : ControllerBase
{
    private readonly ILicenseRepository _licenseRepo;
    private readonly IGlobalLogger _logger;

    public ReportsController(ILicenseRepository licenseRepo, IGlobalLogger logger)
    {
        _licenseRepo = licenseRepo;
        _logger = logger;
    }

    /// <summary>
    /// License waste report: per-user assignments whose LastUsedAt is older than
    /// <paramref name="inactiveDays"/> days, with totals.
    /// </summary>
    [HttpGet("license-waste")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetLicenseWaste([FromQuery] int inactiveDays = 90, CancellationToken ct = default)
    {
        if (inactiveDays < 0) inactiveDays = 0;

        try
        {
            var rows = await _licenseRepo.GetWastedLicensesAsync(inactiveDays: inactiveDays, ct: ct);

            var data = rows.Select(r => new
            {
                objectId = r.ObjectId,
                displayName = r.UserDisplayName ?? r.Username,
                email = r.UserPrincipalName,
                poolName = r.SkuName, // poolName presented to API consumers as the SKU label
                skuName = r.SkuName,
                lastActiveDate = r.LastUsedAt,
                inactiveDays = r.DaysInactive,
                monthlyWaste = r.EstimatedMonthlyCost ?? 0m,
                assignmentSource = r.AssignmentSource,
                recommendation = r.RecommendationType
            }).ToList();

            var totalWastedSeats = data.Count;
            var estimatedMonthlyCost = data.Sum(d => d.monthlyWaste);

            return Ok(new
            {
                data,
                summary = new
                {
                    totalWastedSeats,
                    estimatedMonthlyCost,
                    inactiveDays
                }
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to query license waste report (inactiveDays={Days})", inactiveDays);
            return StatusCode(500, new { error = "Failed to query license waste report" });
        }
    }
}
