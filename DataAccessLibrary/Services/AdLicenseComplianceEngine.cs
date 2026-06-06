using DataAccessLibrary.Models;
using DataAccessLibrary.Repositories;
using Logging;

namespace DataAccessLibrary.Services;

public class AdLicenseComplianceEngine : IAdLicenseComplianceEngine
{
    private readonly ISqlLicenseRepository _repo;
    private readonly ILicenseRepository _licenseRepo;
    private readonly IGlobalLogger _logger;

    public AdLicenseComplianceEngine(ISqlLicenseRepository repo, ILicenseRepository licenseRepo, IGlobalLogger logger)
    {
        _repo = repo;
        _licenseRepo = licenseRepo;
        _logger = logger;
    }

    public async Task EvaluateAllPoolsAsync(CancellationToken ct = default)
    {
        _logger.LogInformation("AdLicenseComplianceEngine: Starting AD CAL compliance evaluation");

        var pools = await _licenseRepo.GetLicensePoolsAsync();
        // Match AutoCount pools OR any pool with an AutoCountObjectClass set (covers pools created before PoolType column existed)
        var adPools = pools.Where(p => p.IsActive &&
            (p.PoolType == "AutoCount" || !string.IsNullOrEmpty(p.AutoCountObjectClass))).ToList();

        _logger.LogInformation("AdLicenseComplianceEngine: Found {Total} total pools, {AD} AD/AutoCount pools to evaluate",
            pools.Count, adPools.Count);

        var existing = await _repo.GetViolationsAsync(unresolvedOnly: true, sourceType: "AD");

        foreach (var pool in adPools)
        {
            if (ct.IsCancellationRequested) break;

            var violations = new List<LicenseComplianceViolation>();
            var poolName = pool.FriendlyName ?? pool.SkuName;

            _logger.LogInformation("AdLicenseComplianceEngine: Evaluating pool '{PoolName}' — PoolType={PoolType}, Owned={Owned}, Consumed={Consumed}",
                poolName, pool.PoolType ?? "(null)", pool.TotalUnits, pool.ConsumedUnits);

            // Rule 1: Under-Licensed — tracked pool where consumed > owned
            if (pool.TotalUnits > 0 && pool.ConsumedUnits > pool.TotalUnits)
            {
                var deficit = pool.ConsumedUnits - pool.TotalUnits;
                violations.Add(new LicenseComplianceViolation
                {
                    SourceType = "AD",
                    LicensePoolId = pool.Id,
                    ViolationType = "UnderLicensed",
                    Severity = "Critical",
                    Title = poolName + ": Under-licensed by " + deficit.ToString("N0"),
                    Detail = "This pool has " + pool.ConsumedUnits.ToString("N0") + " in use but only " +
                             pool.TotalUnits.ToString("N0") + " owned. Purchase " + deficit.ToString("N0") +
                             " additional licenses to achieve compliance."
                });
            }

            // Rule 2: Untracked Pool — consumed > 0 but never configured owned quantity
            if (pool.TotalUnits == 0 && pool.ConsumedUnits > 0)
            {
                violations.Add(new LicenseComplianceViolation
                {
                    SourceType = "AD",
                    LicensePoolId = pool.Id,
                    ViolationType = "UntrackedPool",
                    Severity = "Warning",
                    Title = poolName + ": " + pool.ConsumedUnits.ToString("N0") + " in use but not tracked",
                    Detail = "This pool shows " + pool.ConsumedUnits.ToString("N0") +
                             " consumed licenses but has no owned quantity configured. " +
                             "Set the owned quantity to enable compliance tracking."
                });
            }

            // Rule 3: High Utilization — pool >90% utilized (approaching capacity)
            if (pool.TotalUnits > 0)
            {
                var utilPct = (double)pool.ConsumedUnits / pool.TotalUnits * 100;
                if (utilPct >= 90 && utilPct <= 100)
                {
                    var remaining = pool.TotalUnits - pool.ConsumedUnits;
                    violations.Add(new LicenseComplianceViolation
                    {
                        SourceType = "AD",
                        LicensePoolId = pool.Id,
                        ViolationType = "HighUtilization",
                        Severity = "Warning",
                        Title = poolName + ": " + utilPct.ToString("F0") + "% utilized — only " + remaining.ToString("N0") + " remaining",
                        Detail = "This pool is approaching capacity (" + pool.ConsumedUnits.ToString("N0") +
                                 " of " + pool.TotalUnits.ToString("N0") + "). " +
                                 "Consider purchasing additional licenses before availability reaches zero."
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
                    await _repo.CreateViolationAsync(v);
                }
            }

            // Auto-resolve violations that no longer apply
            var poolExisting = existing.Where(e => e.LicensePoolId == pool.Id && !e.IsResolved).ToList();
            var activeViolationTypes = violations.Select(v => v.ViolationType).ToHashSet();
            foreach (var old in poolExisting)
            {
                if (!activeViolationTypes.Contains(old.ViolationType))
                {
                    await _repo.ResolveViolationAsync(old.Id, "System", "Automatically resolved — condition no longer applies");
                }
            }
        }

        _logger.LogInformation("AdLicenseComplianceEngine: Evaluated {Count} AD pools", adPools.Count);
    }

    public async Task<List<LicenseComplianceViolation>> GetPendingViolationsAsync()
        => await _repo.GetViolationsAsync(unresolvedOnly: true, sourceType: "AD");
}
