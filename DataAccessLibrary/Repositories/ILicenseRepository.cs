using DataAccessLibrary.Models;

namespace DataAccessLibrary.Repositories;

/// <summary>
/// Data access contract for the License Monitoring feature.
/// Schema managed by V056__LicenseMonitoring.sql.
/// </summary>
public interface ILicenseRepository
{
    // ── License Pools ────────────────────────────────────────────────────────

    /// <summary>
    /// Returns all active license pools. When <paramref name="connectionId"/> is
    /// provided, results are filtered to that Entra ID / directory connection.
    /// </summary>
    Task<List<LicensePool>> GetLicensePoolsAsync(
        Guid? connectionId = null,
        CancellationToken ct = default);

    /// <summary>Returns a single pool by primary key, or null if not found.</summary>
    Task<LicensePool?> GetLicensePoolAsync(Guid poolId, CancellationToken ct = default);

    /// <summary>
    /// Inserts or updates a license pool, matching on (SourceConnectionId, SkuId).
    /// Returns the pool Id (existing or newly generated).
    /// </summary>
    Task<Guid> UpsertLicensePoolAsync(LicensePool pool, CancellationToken ct = default);

    /// <summary>
    /// Creates a manual license pool for a non-discoverable software product
    /// (Okta, SailPoint, CyberArk, CrowdStrike, etc.). A synthetic
    /// DirectoryConnection of ConnectionType="Manual" is created the first time
    /// a given source label is used and reused thereafter. The resulting pool
    /// is stored with PoolType="Manual".
    /// </summary>
    Task<Guid> CreateManualLicensePoolAsync(
        string sourceLabel,
        string skuName,
        string? skuPartNumber,
        string? friendlyName,
        int totalUnits,
        int initialConsumedUnits,
        decimal? costPerUnitMonthly,
        string? billingPeriod,
        string? licenseType,
        string? notes,
        CancellationToken ct = default);

    /// <summary>
    /// Updates the owned (purchased) quantity for a pool.
    /// </summary>
    Task UpdatePoolOwnedUnitsAsync(Guid poolId, int totalUnits, CancellationToken ct = default);

    /// <summary>
    /// Updates pool-level policy thresholds and admin notes.
    /// </summary>
    Task UpdatePoolPolicyAsync(
        Guid poolId,
        int? minBufferPercent,
        int? maxUtilizationPercent,
        string? notes,
        CancellationToken ct = default);

    /// <summary>
    /// Updates what happens when a pool breaches its threshold.
    /// </summary>
    Task UpdatePoolBreachActionsAsync(LicensePool pool, CancellationToken ct = default);

    // ── License Assignments ──────────────────────────────────────────────────

    /// <summary>
    /// Returns assignments for the given pool. Pass <c>includeInactive = true</c>
    /// to include assignments where IsActive = 0.
    /// </summary>
    Task<List<LicenseAssignment>> GetLicenseAssignmentsAsync(
        Guid poolId,
        bool includeInactive = false,
        CancellationToken ct = default);

    /// <summary>
    /// Returns all license assignments for a specific user (Object).
    /// </summary>
    Task<List<LicenseAssignment>> GetAssignmentsForObjectAsync(
        Guid objectId,
        CancellationToken ct = default);

    /// <summary>
    /// Inserts or updates a license assignment, matching on (LicensePoolId, ObjectId).
    /// Returns the assignment Id (existing or newly generated).
    /// </summary>
    Task<Guid> UpsertLicenseAssignmentAsync(LicenseAssignment assignment, CancellationToken ct = default);

    // ── Waste Analysis ───────────────────────────────────────────────────────

    /// <summary>
    /// Returns per-user waste rows: active assignments whose LastUsedAt is older
    /// than <paramref name="inactiveDays"/> days (default 90), or where LastUsedAt
    /// is null and AssignedAt is older than the threshold.
    /// Results are ordered by estimated monthly cost descending.
    /// </summary>
    Task<List<LicenseWasteReport>> GetWastedLicensesAsync(
        int inactiveDays = 90,
        Guid? connectionId = null,
        CancellationToken ct = default);

    // ── Service Plans ────────────────────────────────────────────────────────

    /// <summary>Returns all service plans belonging to a pool.</summary>
    Task<List<LicenseServicePlan>> GetServicePlansAsync(
        Guid poolId,
        CancellationToken ct = default);

    /// <summary>
    /// Replaces all service plans for a pool in a single transaction.
    /// Deletes existing plans for the pool, then bulk-inserts the provided list.
    /// </summary>
    Task ReplaceServicePlansAsync(
        Guid poolId,
        IEnumerable<LicenseServicePlan> plans,
        CancellationToken ct = default);

    // ── Usage Snapshots ──────────────────────────────────────────────────────

    /// <summary>
    /// Creates a daily usage snapshot for the given pool, calculating WastedUnits
    /// from the current state of LicenseAssignments with the supplied inactive-day
    /// threshold. If a snapshot for today already exists it is updated in place.
    /// </summary>
    /// <summary>
    /// Snapshots EVERY active LicensePool for today using the same MERGE semantics as
    /// CreateSnapshotAsync (one row per pool per day). Used by the daily LicenseSnapshotJob
    /// so manual pools and pools belonging to connections that don't sync also show up
    /// in trend charts. Returns the number of snapshots written/updated.
    /// </summary>
    Task<int> CreateSnapshotsForAllPoolsAsync(int inactiveDays = 90, CancellationToken ct = default);

    /// <summary>
    /// Generates plausible historical snapshots for every pool over the last N days
    /// (skipping days where a real snapshot already exists). The synthetic series is a
    /// gentle random walk that lands on each pool's current ConsumedUnits, so the
    /// charts read as "this is roughly what the data would look like if we'd been
    /// snapshotting all along". Demo / dev helper; not called from production paths.
    /// </summary>
    Task<int> SeedHistoricalSnapshotsAsync(int daysBack = 90, bool includeExhaustionScenarios = false, CancellationToken ct = default);

    Task<Guid> CreateSnapshotAsync(
        Guid poolId,
        int inactiveDays = 90,
        CancellationToken ct = default);

    /// <summary>
    /// Returns snapshots for a pool ordered by SnapshotDate descending.
    /// Use <paramref name="maxDays"/> to limit the look-back window (default 90).
    /// </summary>
    Task<List<LicenseUsageSnapshot>> GetSnapshotsAsync(
        Guid poolId,
        int maxDays = 90,
        CancellationToken ct = default);

    /// <summary>
    /// Returns the recent snapshot history for every pool in a single round-trip,
    /// keyed by LicensePoolId and ordered by SnapshotDate ascending. Used by the
    /// exhaustion forecaster to fit a linear regression across all pools without
    /// per-pool query overhead.
    /// </summary>
    Task<Dictionary<Guid, List<LicenseUsageSnapshot>>> GetAllRecentSnapshotsAsync(
        int maxDays = 60,
        CancellationToken ct = default);

    // ── Reclaim Support ──────────────────────────────────────────────────────

    /// <summary>
    /// Atomically decrement the local ConsumedUnits counter for a pool by the given delta.
    /// Used by reclaim flows (manual revoke + DryRun) to keep dashboard counts in sync.
    /// Will not drop below zero.
    /// </summary>
    Task DecrementConsumedUnitsAsync(Guid licensePoolId, int delta, CancellationToken ct = default);

    /// <summary>
    /// Mark a LicenseAssignments row inactive after a successful Live revoke so the
    /// user's license list and the next sync reflect the change. No-op if no active
    /// row exists for the (poolId, objectId) pair. DryRun callers should not invoke this.
    /// </summary>
    Task DeactivateAssignmentAsync(Guid licensePoolId, Guid objectId, CancellationToken ct = default);

    /// <summary>
    /// Insert a row into LicenseAssignmentEvents. Use the constants on
    /// <see cref="LicenseAssignmentEventTypes"/> for <paramref name="eventType"/>.
    /// </summary>
    Task WriteLicenseAssignmentEventAsync(
        Guid licensePoolId,
        Guid objectId,
        Guid? assignmentId,
        string eventType,
        string? actor,
        string? reason,
        string? metadataJson = null,
        CancellationToken ct = default);

    // ── Sync Support ────────────────────────────────────────────────────────

    /// <summary>
    /// Marks license assignments as inactive if they were not updated during the current sync.
    /// This handles license revocations in Entra ID.
    /// </summary>
    Task<int> DeactivateStaleAssignmentsAsync(Guid connectionId, DateTime syncedBefore, CancellationToken ct = default);

    /// <summary>
    /// Maps Entra user IDs (SourceUniqueId) to internal Object IDs for a given connection.
    /// </summary>
    Task<Dictionary<string, Guid>> ResolveEntraUserIdsAsync(Guid connectionId, IEnumerable<string> entraUserIds, CancellationToken ct = default);

    /// <summary>
    /// Gets all active license pool IDs keyed by SkuId for a connection.
    /// </summary>
    Task<Dictionary<string, Guid>> GetPoolIdsBySkuAsync(Guid connectionId, CancellationToken ct = default);

    // ── Dashboard ────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns a fully aggregated summary suitable for the dashboard header.
    /// Optionally scoped to a single directory connection.
    /// </summary>
    Task<LicenseDashboardSummary> GetDashboardSummaryAsync(
        Guid? connectionId = null,
        int inactiveDays = 90,
        CancellationToken ct = default);

    // ── Optimization Recommendations ─────────────────────────────────────────

    /// <summary>
    /// Returns optimization recommendations. Pass a <paramref name="status"/>
    /// value ("Pending", "Approved", "Applied", "Dismissed") to filter; null
    /// returns all statuses.
    /// </summary>
    Task<List<LicenseOptimizationRecommendation>> GetOptimizationRecommendationsAsync(
        string? status = "Pending",
        Guid? connectionId = null,
        CancellationToken ct = default);

    /// <summary>Returns a single recommendation by Id, or null if not found.</summary>
    Task<LicenseOptimizationRecommendation?> GetRecommendationAsync(
        Guid recommendationId,
        CancellationToken ct = default);

    /// <summary>Inserts a new optimization recommendation and returns its Id.</summary>
    Task<Guid> CreateRecommendationAsync(
        LicenseOptimizationRecommendation recommendation,
        CancellationToken ct = default);

    /// <summary>
    /// Marks a recommendation as Approved, recording the reviewer name and timestamp.
    /// </summary>
    Task ApproveRecommendationAsync(
        Guid recommendationId,
        string reviewerName,
        CancellationToken ct = default);

    /// <summary>
    /// Marks a recommendation as Dismissed, recording the reviewer name and timestamp.
    /// </summary>
    Task DismissRecommendationAsync(
        Guid recommendationId,
        string reviewerName,
        CancellationToken ct = default);

    /// <summary>
    /// Marks a recommendation as Applied, recording the timestamp.
    /// </summary>
    Task MarkRecommendationAppliedAsync(
        Guid recommendationId,
        CancellationToken ct = default);

    // ── V071: License Categories ─────────────────────────────────────────────

    /// <summary>Returns all active license categories sorted by SortOrder.</summary>
    Task<List<LicenseCategory>> GetCategoriesAsync(CancellationToken ct = default);

    /// <summary>Returns categories enriched with pool counts and total monthly spend.</summary>
    Task<List<LicenseCategory>> GetCategoriesWithStatsAsync(Guid? connectionId = null, CancellationToken ct = default);

    /// <summary>Creates a new custom category. Returns the new Id.</summary>
    Task<Guid> CreateCategoryAsync(LicenseCategory category, CancellationToken ct = default);

    /// <summary>Updates a category's name, description, color, icon, sort order.</summary>
    Task UpdateCategoryAsync(LicenseCategory category, CancellationToken ct = default);

    /// <summary>Soft-deletes a category (sets IsActive=0). Built-in categories cannot be deleted.</summary>
    Task DeleteCategoryAsync(Guid categoryId, CancellationToken ct = default);

    /// <summary>Assigns a pool to a category (or clears it if categoryId is null).</summary>
    Task AssignPoolToCategoryAsync(Guid poolId, Guid? categoryId, CancellationToken ct = default);

    /// <summary>Bulk-assign multiple pools to a single category.</summary>
    Task<int> BulkAssignPoolsToCategoryAsync(IEnumerable<Guid> poolIds, Guid? categoryId, CancellationToken ct = default);

    // ── CAL Auto-Attribution Candidates ──────────────────────────────────────

    /// <summary>
    /// Returns activity-based candidate CAL pools the given object is likely
    /// consuming without a formal LicenseAssignment row. Computed on-demand:
    /// for users/service accounts → User CAL pools driven by SignInSummary
    /// (30d window); for computers → Device CAL pools driven by sign-in
    /// activity / SqlServerPermissions presence (30d high+med, 90d low).
    /// Excludes pools the object is already assigned to and pools the user
    /// has dismissed via Settings(Category='LicenseManagement',
    /// Key='DismissedCandidate:{objectId}:{poolId}').
    /// Results are sorted High → Medium → Low confidence.
    /// </summary>
    Task<List<LicenseAttributionCandidate>> GetActivityBasedCandidatesAsync(
        Guid objectId,
        CancellationToken ct = default);

    // ── Enterprise Apps Overview (License Center wedge) ──────────────────────

    /// <summary>
    /// Aggregate summary of enterprise apps for the License Center overview card:
    /// totals, top-by-sign-in volume, dormant, and high-permission lists. Returns
    /// <see cref="EnterpriseAppSummary.Empty"/> if EnterpriseApps is empty or any
    /// dependency table (SignInLogs, oAuth2 grants) hasn't been synced yet.
    /// </summary>
    Task<EnterpriseAppSummary> GetEnterpriseAppSummaryAsync(CancellationToken ct = default);
}
