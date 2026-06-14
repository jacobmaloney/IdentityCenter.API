namespace DataAccessLibrary.Repositories;

using DataAccessLibrary.Models;

/// <summary>
/// Data access contract for cloud activity monitoring tables:
/// SignInLogs, SignInSummaries, M365UsageReports, AppRoleAssignments, EnterpriseApps.
/// </summary>
public interface ICloudActivityRepository
{
    // ── Sign-In Logs ────────────────────────────────────────────────────────

    /// <summary>
    /// Inserts sign-in log records, skipping duplicates by SignInId.
    /// Returns the number of rows actually inserted.
    /// </summary>
    Task<int> BulkUpsertSignInLogsAsync(IEnumerable<SignInLog> logs, CancellationToken ct = default);

    /// <summary>
    /// Upserts a daily sign-in summary on the unique key (ObjectId, AppDisplayName, SummaryDate).
    /// </summary>
    Task UpsertSignInSummaryAsync(SignInSummary summary, CancellationToken ct = default);

    /// <summary>
    /// Returns the latest SignInDateTime for a connection, used for incremental sync.
    /// Returns null if no sign-in logs exist for the connection.
    /// </summary>
    Task<DateTime?> GetLatestSignInDateAsync(Guid connectionId, CancellationToken ct = default);

    /// <summary>
    /// Returns paged sign-in logs for a specific object, ordered by most recent first.
    /// </summary>
    Task<(List<SignInLog> Items, int Total)> GetSignInLogsForObjectAsync(
        Guid objectId,
        int page = 1,
        int pageSize = 25,
        CancellationToken ct = default);

    // ── M365 Usage Reports ──────────────────────────────────────────────────

    /// <summary>
    /// Upserts M365 usage report rows on the unique key (ObjectId, ReportRefreshDate).
    /// Returns the number of rows inserted or updated.
    /// </summary>
    Task<int> BulkUpsertUsageReportsAsync(IEnumerable<M365UsageReport> reports, CancellationToken ct = default);

    /// <summary>
    /// Returns the most recent M365 usage report row for a single object (by ReportRefreshDate),
    /// or null if none exists. Used by the Entra Manage pane's OneDrive card.
    /// </summary>
    Task<M365UsageReport?> GetLatestUsageReportForObjectAsync(Guid objectId, CancellationToken ct = default);

    // ── App Role Assignments ────────────────────────────────────────────────

    /// <summary>
    /// Inserts app role assignment records, skipping duplicates by AppRoleAssignmentId.
    /// Returns the number of rows inserted.
    /// </summary>
    Task<int> BulkUpsertAppRoleAssignmentsAsync(IEnumerable<AppRoleAssignment> assignments, CancellationToken ct = default);

    /// <summary>
    /// Upserts enterprise app records on the unique key (SourceConnectionId, ServicePrincipalId).
    /// Returns the number of rows inserted or updated.
    /// </summary>
    Task<int> BulkUpsertEnterpriseAppsAsync(IEnumerable<EnterpriseApp> apps, CancellationToken ct = default);

    /// <summary>
    /// Marks app role assignments as inactive if they were not refreshed during the current sync.
    /// Returns the number of deactivated rows.
    /// </summary>
    Task<int> DeactivateStaleAppRoleAssignmentsAsync(Guid connectionId, DateTime syncedBefore, CancellationToken ct = default);

    // ── Resolution Helpers ──────────────────────────────────────────────────

    /// <summary>
    /// Maps Entra user IDs (SourceUniqueId) to internal Object IDs for a given connection.
    /// </summary>
    Task<Dictionary<string, Guid>> ResolveEntraUserIdsAsync(Guid connectionId, IEnumerable<string> entraUserIds, CancellationToken ct = default);

    /// <summary>
    /// Maps User Principal Names to internal Object IDs for a given connection.
    /// </summary>
    Task<Dictionary<string, Guid>> ResolveByUPNAsync(Guid connectionId, IEnumerable<string> upns, CancellationToken ct = default);

    /// <summary>
    /// Maps Entra object IDs (SourceUniqueId) to internal Object IDs for a given connection.
    /// Alias for ResolveEntraUserIdsAsync with clearer semantics for non-user objects.
    /// </summary>
    Task<Dictionary<string, Guid>> ResolveEntraObjectIdsAsync(Guid connectionId, IEnumerable<string> entraObjectIds, CancellationToken ct = default);
}
