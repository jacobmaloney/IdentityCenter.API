using DataAccessLibrary.Models;

namespace DataAccessLibrary.Services;

/// <summary>
/// Evaluates license pools against their configured thresholds and generates
/// breach records + notifications when limits are exceeded.
///
/// Thresholds checked:
/// - MinBufferPercent: alerts when AvailableUnits/TotalUnits < MinBufferPercent
/// - MaxUtilizationPercent: alerts when ConsumedUnits/TotalUnits > MaxUtilizationPercent
/// - DaysUntilExhaustion (from ML forecast): alerts when pool will exhaust within N days
/// </summary>
public interface ILicenseThresholdMonitorService
{
    /// <summary>
    /// Evaluate all active pools. Creates LicenseThresholdBreach records for
    /// any new breaches, sends AdminNotifications, resolves breaches that have
    /// recovered. Deduplicates: won't re-alert for an unresolved breach.
    /// </summary>
    /// <returns>Count of (newBreaches, resolvedBreaches) tuple.</returns>
    Task<(int newBreaches, int resolvedBreaches)> EvaluateAllPoolsAsync(CancellationToken ct = default);

    /// <summary>Evaluate a single pool. Returns true if a new breach was recorded.</summary>
    Task<bool> EvaluatePoolAsync(Guid poolId, CancellationToken ct = default);

    /// <summary>Get active (unresolved) breaches across all pools, newest first.</summary>
    Task<List<LicenseThresholdBreach>> GetActiveBreachesAsync(CancellationToken ct = default);

    /// <summary>Get all breaches for a pool, optionally including resolved ones.</summary>
    Task<List<LicenseThresholdBreach>> GetBreachesForPoolAsync(Guid poolId, bool includeResolved = true, CancellationToken ct = default);

    /// <summary>Manually resolve a breach with a reason.</summary>
    Task ResolveBreachAsync(Guid breachId, string reason, CancellationToken ct = default);
}
