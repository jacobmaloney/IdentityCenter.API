using Dapper;
using DataAccessLibrary.Models;
using DataAccessLibrary.Repositories;
using Logging;
using Microsoft.Extensions.Configuration;

namespace DataAccessLibrary.Services;

public class EntraLicenseComplianceEngine : IEntraLicenseComplianceEngine
{
    private readonly ISqlLicenseRepository _violationRepo;
    private readonly ILicenseRepository _licenseRepo;
    private readonly IGlobalLogger _logger;
    private readonly string _connectionString;

    public EntraLicenseComplianceEngine(
        ISqlLicenseRepository violationRepo,
        ILicenseRepository licenseRepo,
        IGlobalLogger logger,
        IConfiguration config)
    {
        _violationRepo = violationRepo;
        _licenseRepo = licenseRepo;
        _logger = logger;
        _connectionString = config.GetConnectionString("DefaultConnection") ?? "";
    }

    public async Task EvaluateAllPoolsAsync(CancellationToken ct = default)
    {
        _logger.LogInformation("EntraLicenseComplianceEngine: Starting Entra license compliance evaluation");

        var pools = await _licenseRepo.GetLicensePoolsAsync(ct: ct);
        var entraPools = pools.Where(p => p.PoolType == "Synced" && p.IsActive).ToList();

        var existing = await _violationRepo.GetViolationsAsync(unresolvedOnly: true, sourceType: "Entra");

        // Get waste data for high-waste detection
        var wastedLicenses = await _licenseRepo.GetWastedLicensesAsync(inactiveDays: 90, ct: ct);
        var wasteByPool = wastedLicenses.GroupBy(w => w.LicensePoolId)
            .ToDictionary(g => g.Key, g => g.ToList());

        // Get disabled users with active license assignments
        int disabledWithLicenses = 0;
        try
        {
            using var conn = new Microsoft.Data.SqlClient.SqlConnection(_connectionString);
            await conn.OpenAsync(ct);
            disabledWithLicenses = await conn.ExecuteScalarAsync<int>(@"
                SELECT COUNT(DISTINCT la.ObjectId)
                FROM LicenseAssignments la
                INNER JOIN Objects o ON CAST(o.Id AS NVARCHAR(100)) = la.ObjectId
                WHERE o.IsEnabled = 0
                  AND o.DeletedAt IS NULL
                  AND la.IsActive = 1");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "EntraLicenseComplianceEngine: Failed to query disabled user licenses");
        }

        foreach (var pool in entraPools)
        {
            if (ct.IsCancellationRequested) break;

            var violations = new List<LicenseComplianceViolation>();
            var poolName = pool.FriendlyName ?? pool.SkuName;

            // Rule 1: Over-Allocated — consumed exceeds total
            if (pool.TotalUnits > 0 && pool.ConsumedUnits > pool.TotalUnits)
            {
                var excess = pool.ConsumedUnits - pool.TotalUnits;
                violations.Add(new LicenseComplianceViolation
                {
                    SourceType = "Entra",
                    LicensePoolId = pool.Id,
                    ViolationType = "OverAllocated",
                    Severity = "Critical",
                    Title = poolName + ": Over-allocated by " + excess.ToString("N0") + " licenses",
                    Detail = pool.ConsumedUnits.ToString("N0") + " licenses assigned but only " +
                             pool.TotalUnits.ToString("N0") + " owned in tenant. " +
                             "Purchase additional licenses or remove assignments to restore compliance."
                });
            }

            // Rule 2: High Waste — >20% of consumed licenses are wasted (inactive 90+ days)
            if (pool.ConsumedUnits > 10 && wasteByPool.TryGetValue(pool.Id, out var poolWaste))
            {
                var wastedCount = poolWaste.Count;
                var wastePct = (double)wastedCount / pool.ConsumedUnits * 100;
                var monthlyCost = poolWaste.Sum(w => w.EstimatedMonthlyCost ?? 0);

                if (wastePct >= 20)
                {
                    violations.Add(new LicenseComplianceViolation
                    {
                        SourceType = "Entra",
                        LicensePoolId = pool.Id,
                        ViolationType = "HighWaste",
                        Severity = "Warning",
                        Title = poolName + ": " + wastePct.ToString("F0") + "% waste (" + wastedCount.ToString("N0") + " licenses inactive 90+ days)",
                        Detail = wastedCount.ToString("N0") + " of " + pool.ConsumedUnits.ToString("N0") +
                                 " assigned licenses have not been used in 90+ days. " +
                                 "Estimated monthly waste: " + monthlyCost.ToString("$#,##0") + ". " +
                                 "Review and remove unused assignments to reduce spend."
                    });
                }
            }

            // Rule 3: Approaching Capacity — less than 5% available
            if (pool.TotalUnits > 20)
            {
                var availPct = (double)pool.AvailableUnits / pool.TotalUnits * 100;
                if (availPct > 0 && availPct < 5)
                {
                    violations.Add(new LicenseComplianceViolation
                    {
                        SourceType = "Entra",
                        LicensePoolId = pool.Id,
                        ViolationType = "ApproachingCapacity",
                        Severity = "Warning",
                        Title = poolName + ": Only " + pool.AvailableUnits.ToString("N0") + " licenses remaining (" + availPct.ToString("F0") + "%)",
                        Detail = "This pool has " + pool.AvailableUnits.ToString("N0") + " of " +
                                 pool.TotalUnits.ToString("N0") + " licenses available. " +
                                 "New user onboarding may be blocked soon. Purchase additional licenses or reclaim unused ones."
                    });
                }
            }

            // Persist new violations (avoid duplicates)
            foreach (var v in violations)
            {
                bool alreadyExists = existing.Any(e =>
                    e.LicensePoolId == v.LicensePoolId &&
                    e.ViolationType == v.ViolationType &&
                    !e.IsResolved);
                if (!alreadyExists)
                {
                    v.DetectedAt = DateTime.UtcNow;
                    await _violationRepo.CreateViolationAsync(v);
                }
            }

            // Auto-resolve violations that no longer apply
            var poolExisting = existing.Where(e => e.LicensePoolId == pool.Id && !e.IsResolved).ToList();
            var activeViolationTypes = violations.Select(v => v.ViolationType).ToHashSet();
            foreach (var old in poolExisting)
            {
                if (!activeViolationTypes.Contains(old.ViolationType))
                {
                    await _violationRepo.ResolveViolationAsync(old.Id, "System", "Automatically resolved — condition no longer applies");
                }
            }
        }

        // Rule 4: Disabled Users with Active Licenses (cross-pool, reported once)
        if (disabledWithLicenses > 0)
        {
            bool alreadyExists = existing.Any(e =>
                e.ViolationType == "DisabledUserLicense" && !e.IsResolved);
            if (!alreadyExists)
            {
                await _violationRepo.CreateViolationAsync(new LicenseComplianceViolation
                {
                    SourceType = "Entra",
                    ViolationType = "DisabledUserLicense",
                    Severity = "Critical",
                    Title = disabledWithLicenses.ToString("N0") + " disabled user(s) still have active license assignments",
                    Detail = "These users are disabled in the directory but still consume paid licenses. " +
                             "Review and remove their license assignments to stop unnecessary spend. " +
                             "Create an access review campaign to certify these assignments.",
                    DetectedAt = DateTime.UtcNow
                });
            }
        }
        else
        {
            // Auto-resolve if no longer applicable
            var disabledViolation = existing.FirstOrDefault(e =>
                e.ViolationType == "DisabledUserLicense" && !e.IsResolved);
            if (disabledViolation != null)
            {
                await _violationRepo.ResolveViolationAsync(disabledViolation.Id, "System",
                    "Automatically resolved — no disabled users with active licenses");
            }
        }

        _logger.LogInformation("EntraLicenseComplianceEngine: Evaluated {Count} Entra pools, {Disabled} disabled users with licenses",
            entraPools.Count, disabledWithLicenses);
    }

    public async Task<List<LicenseComplianceViolation>> GetPendingViolationsAsync()
        => await _violationRepo.GetViolationsAsync(unresolvedOnly: true, sourceType: "Entra");
}
