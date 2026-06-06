using DataAccessLibrary.Models;

namespace DataAccessLibrary.Services;

/// <summary>
/// Repository for managing bulk issue snapshots used for trend analysis
/// and proactive detection of organization-wide data quality issues.
/// </summary>
public interface IBulkIssueSnapshotRepository
{
    /// <summary>
    /// Get the latest snapshot for each issue type
    /// </summary>
    Task<List<BulkIssueSnapshot>> GetLatestSnapshotsAsync(CancellationToken ct = default);

    /// <summary>
    /// Get snapshots for a specific issue over time
    /// </summary>
    Task<List<BulkIssueSnapshot>> GetSnapshotHistoryAsync(string issueId, int days = 30, CancellationToken ct = default);

    /// <summary>
    /// Get all snapshots for a date range (for trend analysis)
    /// </summary>
    Task<List<BulkIssueSnapshot>> GetSnapshotsInRangeAsync(DateTime startDate, DateTime endDate, CancellationToken ct = default);

    /// <summary>
    /// Save multiple snapshots (batch insert)
    /// </summary>
    Task SaveSnapshotsAsync(List<BulkIssueSnapshot> snapshots, CancellationToken ct = default);

    /// <summary>
    /// Get the latest summary of bulk issues (for proactive suggestions)
    /// </summary>
    Task<BulkIssueSummary?> GetLatestSummaryAsync(CancellationToken ct = default);

    /// <summary>
    /// Get significant changes since a date (for chat proactive suggestions)
    /// </summary>
    Task<List<BulkIssueChange>> GetRecentChangesAsync(DateTime since, CancellationToken ct = default);

    /// <summary>
    /// Cleanup snapshots older than specified days
    /// </summary>
    Task CleanupOldSnapshotsAsync(int retentionDays, CancellationToken ct = default);

    /// <summary>
    /// Check if there's a recent snapshot (to avoid duplicate runs)
    /// </summary>
    Task<bool> HasRecentSnapshotAsync(int withinHours = 20, CancellationToken ct = default);
}
