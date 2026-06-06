using Dapper;
using DataAccessLibrary.Models;
using DataAccessLibrary.Services;
using Logging;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;

namespace DataAccessLibrary.Repositories;

/// <summary>
/// Repository for managing bulk issue snapshots.
/// Uses Dapper for all database operations.
/// </summary>
public class BulkIssueSnapshotRepository : DapperRepositoryBase, IBulkIssueSnapshotRepository
{
    public BulkIssueSnapshotRepository(IConfiguration configuration, IGlobalLogger logger)
        : base(configuration, logger)
    {
    }

    public async Task<List<BulkIssueSnapshot>> GetLatestSnapshotsAsync(CancellationToken ct = default)
    {
        return await ExecuteAsync(async connection =>
        {
            // Get the most recent snapshot for each issue type
            var results = await connection.QueryAsync<BulkIssueSnapshot>(new CommandDefinition(@"
                WITH LatestSnapshots AS (
                    SELECT *,
                           ROW_NUMBER() OVER (PARTITION BY IssueId ORDER BY SnapshotDate DESC) as RowNum
                    FROM BulkIssueSnapshots
                )
                SELECT Id, IssueId, IssueTitle, Category, AffectedCount, FixableCount,
                       ChangeFromPrevious, ChangePercentage, SnapshotDate, SnapshotType,
                       NotificationSent, Metadata
                FROM LatestSnapshots
                WHERE RowNum = 1
                ORDER BY Category, IssueId",
                commandTimeout: 30,
                cancellationToken: ct));

            return results.ToList();
        }, ct);
    }

    public async Task<List<BulkIssueSnapshot>> GetSnapshotHistoryAsync(string issueId, int days = 30, CancellationToken ct = default)
    {
        return await ExecuteAsync(async connection =>
        {
            var results = await connection.QueryAsync<BulkIssueSnapshot>(new CommandDefinition(@"
                SELECT Id, IssueId, IssueTitle, Category, AffectedCount, FixableCount,
                       ChangeFromPrevious, ChangePercentage, SnapshotDate, SnapshotType,
                       NotificationSent, Metadata
                FROM BulkIssueSnapshots
                WHERE IssueId = @IssueId
                  AND SnapshotDate >= DATEADD(DAY, -@Days, GETUTCDATE())
                ORDER BY SnapshotDate DESC",
                new { IssueId = issueId, Days = days },
                commandTimeout: 30,
                cancellationToken: ct));

            return results.ToList();
        }, ct);
    }

    public async Task<List<BulkIssueSnapshot>> GetSnapshotsInRangeAsync(DateTime startDate, DateTime endDate, CancellationToken ct = default)
    {
        return await ExecuteAsync(async connection =>
        {
            var results = await connection.QueryAsync<BulkIssueSnapshot>(new CommandDefinition(@"
                SELECT Id, IssueId, IssueTitle, Category, AffectedCount, FixableCount,
                       ChangeFromPrevious, ChangePercentage, SnapshotDate, SnapshotType,
                       NotificationSent, Metadata
                FROM BulkIssueSnapshots
                WHERE SnapshotDate >= @StartDate AND SnapshotDate <= @EndDate
                ORDER BY SnapshotDate, IssueId",
                new { StartDate = startDate, EndDate = endDate },
                commandTimeout: 60,
                cancellationToken: ct));

            return results.ToList();
        }, ct);
    }

    public async Task SaveSnapshotsAsync(List<BulkIssueSnapshot> snapshots, CancellationToken ct = default)
    {
        if (!snapshots.Any())
            return;

        await ExecuteNonQueryAsync(async connection =>
        {
            // Bulk insert using table-valued parameter or multiple inserts
            foreach (var snapshot in snapshots)
            {
                snapshot.Id = Guid.NewGuid();
                await connection.ExecuteAsync(new CommandDefinition(@"
                    INSERT INTO BulkIssueSnapshots
                        (Id, IssueId, IssueTitle, Category, AffectedCount, FixableCount,
                         ChangeFromPrevious, ChangePercentage, SnapshotDate, SnapshotType,
                         NotificationSent, Metadata)
                    VALUES
                        (@Id, @IssueId, @IssueTitle, @Category, @AffectedCount, @FixableCount,
                         @ChangeFromPrevious, @ChangePercentage, @SnapshotDate, @SnapshotType,
                         @NotificationSent, @Metadata)",
                    snapshot,
                    commandTimeout: 30,
                    cancellationToken: ct));
            }
        }, ct);

        _logger.LogInformation("Saved {Count} bulk issue snapshots", snapshots.Count);
    }

    public async Task<BulkIssueSummary?> GetLatestSummaryAsync(CancellationToken ct = default)
    {
        return await ExecuteAsync(async connection =>
        {
            // Get latest snapshots and compute summary
            var latestSnapshots = await GetLatestSnapshotsAsync(ct);

            if (!latestSnapshots.Any())
                return null;

            var latestDate = latestSnapshots.Max(s => s.SnapshotDate);
            var previousDate = latestDate.AddDays(-1);

            // Get previous day's snapshots for comparison
            var previousSnapshots = await connection.QueryAsync<BulkIssueSnapshot>(new CommandDefinition(@"
                WITH PreviousSnapshots AS (
                    SELECT *,
                           ROW_NUMBER() OVER (PARTITION BY IssueId ORDER BY SnapshotDate DESC) as RowNum
                    FROM BulkIssueSnapshots
                    WHERE SnapshotDate < @LatestDate
                )
                SELECT Id, IssueId, IssueTitle, Category, AffectedCount, FixableCount,
                       ChangeFromPrevious, ChangePercentage, SnapshotDate, SnapshotType,
                       NotificationSent, Metadata
                FROM PreviousSnapshots
                WHERE RowNum = 1",
                new { LatestDate = latestDate.Date },
                commandTimeout: 30,
                cancellationToken: ct));

            var previousByIssue = previousSnapshots.ToDictionary(s => s.IssueId, s => s);

            var summary = new BulkIssueSummary
            {
                TotalIssueTypes = latestSnapshots.Count(s => s.AffectedCount > 0),
                TotalAffectedItems = latestSnapshots.Sum(s => s.AffectedCount),
                PeriodStart = previousDate,
                PeriodEnd = latestDate,
                GeneratedAt = DateTime.UtcNow
            };

            foreach (var current in latestSnapshots)
            {
                var previousCount = previousByIssue.TryGetValue(current.IssueId, out var prev) ? prev.AffectedCount : 0;
                var change = current.AffectedCount - previousCount;

                if (previousCount == 0 && current.AffectedCount > 0)
                    summary.NewIssues++;
                else if (previousCount > 0 && current.AffectedCount == 0)
                    summary.ResolvedIssues++;
                else if (change > 0)
                    summary.IssuesIncreased++;
                else if (change < 0)
                    summary.IssuesDecreased++;

                summary.NetChange += change;

                // Track significant changes
                if (Math.Abs(change) >= 5 || (previousCount > 0 && Math.Abs((double)change / previousCount) >= 0.1))
                {
                    summary.SignificantChanges.Add(new BulkIssueChange
                    {
                        IssueId = current.IssueId,
                        IssueTitle = current.IssueTitle ?? current.IssueId,
                        Category = current.Category ?? "Unknown",
                        PreviousCount = previousCount,
                        CurrentCount = current.AffectedCount,
                        Change = change,
                        ChangePercentage = previousCount > 0 ? (double)change / previousCount * 100 : 100,
                        ChangeType = DetermineChangeType(previousCount, current.AffectedCount)
                    });
                }
            }

            // Sort significant changes by absolute change
            summary.SignificantChanges = summary.SignificantChanges
                .OrderByDescending(c => Math.Abs(c.Change))
                .Take(10)
                .ToList();

            return summary;
        }, ct);
    }

    public async Task<List<BulkIssueChange>> GetRecentChangesAsync(DateTime since, CancellationToken ct = default)
    {
        return await ExecuteAsync(async connection =>
        {
            // Get snapshots with significant changes since the given date
            var results = await connection.QueryAsync<BulkIssueSnapshot>(new CommandDefinition(@"
                SELECT Id, IssueId, IssueTitle, Category, AffectedCount, FixableCount,
                       ChangeFromPrevious, ChangePercentage, SnapshotDate, SnapshotType,
                       NotificationSent, Metadata
                FROM BulkIssueSnapshots
                WHERE SnapshotDate >= @Since
                  AND (ABS(ChangeFromPrevious) >= 5 OR ABS(ChangePercentage) >= 10)
                ORDER BY SnapshotDate DESC, ABS(ChangeFromPrevious) DESC",
                new { Since = since },
                commandTimeout: 30,
                cancellationToken: ct));

            return results.Select(s => new BulkIssueChange
            {
                IssueId = s.IssueId,
                IssueTitle = s.IssueTitle ?? s.IssueId,
                Category = s.Category ?? "Unknown",
                PreviousCount = s.AffectedCount - s.ChangeFromPrevious,
                CurrentCount = s.AffectedCount,
                Change = s.ChangeFromPrevious,
                ChangePercentage = s.ChangePercentage,
                ChangeType = DetermineChangeType(s.AffectedCount - s.ChangeFromPrevious, s.AffectedCount)
            }).ToList();
        }, ct);
    }

    public async Task CleanupOldSnapshotsAsync(int retentionDays, CancellationToken ct = default)
    {
        await ExecuteNonQueryAsync(async connection =>
        {
            var deleted = await connection.ExecuteAsync(new CommandDefinition(@"
                DELETE FROM BulkIssueSnapshots
                WHERE SnapshotDate < DATEADD(DAY, -@RetentionDays, GETUTCDATE())",
                new { RetentionDays = retentionDays },
                commandTimeout: 60,
                cancellationToken: ct));

            if (deleted > 0)
            {
                _logger.LogInformation("Cleaned up {Count} old bulk issue snapshots (retention: {Days} days)",
                    deleted, retentionDays);
            }
        }, ct);
    }

    public async Task<bool> HasRecentSnapshotAsync(int withinHours = 20, CancellationToken ct = default)
    {
        return await ExecuteAsync(async connection =>
        {
            var count = await connection.ExecuteScalarAsync<int>(new CommandDefinition(@"
                SELECT COUNT(*)
                FROM BulkIssueSnapshots
                WHERE SnapshotDate >= DATEADD(HOUR, -@Hours, GETUTCDATE())",
                new { Hours = withinHours },
                commandTimeout: 30,
                cancellationToken: ct));

            return count > 0;
        }, ct);
    }

    private static ChangeType DetermineChangeType(int previousCount, int currentCount)
    {
        if (previousCount == 0 && currentCount > 0)
            return ChangeType.New;
        if (previousCount > 0 && currentCount == 0)
            return ChangeType.Resolved;
        if (currentCount > previousCount)
            return ChangeType.Increased;
        if (currentCount < previousCount)
            return ChangeType.Decreased;
        return ChangeType.NoChange;
    }
}
