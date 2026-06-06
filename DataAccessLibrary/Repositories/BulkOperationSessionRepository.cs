using Dapper;
using DataAccessLibrary.Models;
using DataAccessLibrary.Services;
using Logging;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;

namespace DataAccessLibrary.Repositories;

/// <summary>
/// Repository for managing bulk operation sessions and changes.
/// Uses Dapper for all database operations.
/// Supports rollback functionality by tracking all changes made during bulk operations.
/// </summary>
public class BulkOperationSessionRepository : DapperRepositoryBase, IBulkOperationSessionRepository
{
    // Rollback time limit in hours (default: 24 hours)
    private const int RollbackTimeLimitHours = 24;

    public BulkOperationSessionRepository(IConfiguration configuration, IGlobalLogger logger)
        : base(configuration, logger)
    {
    }

    // ============================================================================
    // SESSION MANAGEMENT
    // ============================================================================

    public async Task<Guid> CreateSessionAsync(BulkOperationSession session, CancellationToken ct = default)
    {
        return await ExecuteAsync(async connection =>
        {
            await connection.ExecuteAsync(new CommandDefinition(@"
                INSERT INTO BulkOperationSessions (
                    Id, IssueId, IssueTitle, UserId, UserDisplayName,
                    ExecutedAt, ItemCount, SuccessCount, FailedCount, Status,
                    DepartmentFilter, OuFilter, Notes
                ) VALUES (
                    @Id, @IssueId, @IssueTitle, @UserId, @UserDisplayName,
                    @ExecutedAt, @ItemCount, @SuccessCount, @FailedCount, @Status,
                    @DepartmentFilter, @OuFilter, @Notes
                )",
                session,
                commandTimeout: 30,
                cancellationToken: ct));

            return session.Id;
        }, ct);
    }

    public async Task<BulkOperationSession?> GetSessionAsync(Guid sessionId, bool includeChanges = true, CancellationToken ct = default)
    {
        return await ExecuteAsync(async connection =>
        {
            var session = await connection.QuerySingleOrDefaultAsync<BulkOperationSession>(new CommandDefinition(@"
                SELECT Id, IssueId, IssueTitle, UserId, UserDisplayName,
                       ExecutedAt, ItemCount, SuccessCount, FailedCount, Status,
                       DepartmentFilter, OuFilter, LastModifiedAt, RolledBackBy, RolledBackAt, Notes
                FROM BulkOperationSessions
                WHERE Id = @SessionId",
                new { SessionId = sessionId },
                commandTimeout: 30,
                cancellationToken: ct));

            if (session != null && includeChanges)
            {
                session.Changes = await GetSessionChangesAsync(sessionId, ct);
            }

            return session;
        }, ct);
    }

    public async Task<BulkOperationSession?> GetLastSessionForUserAsync(string userId, CancellationToken ct = default)
    {
        return await ExecuteAsync(async connection =>
        {
            var session = await connection.QuerySingleOrDefaultAsync<BulkOperationSession>(new CommandDefinition(@"
                SELECT TOP 1
                    Id, IssueId, IssueTitle, UserId, UserDisplayName,
                    ExecutedAt, ItemCount, SuccessCount, FailedCount, Status,
                    DepartmentFilter, OuFilter, LastModifiedAt, RolledBackBy, RolledBackAt, Notes
                FROM BulkOperationSessions
                WHERE UserId = @UserId
                ORDER BY ExecutedAt DESC",
                new { UserId = userId },
                commandTimeout: 30,
                cancellationToken: ct));

            if (session != null)
            {
                session.Changes = await GetSessionChangesAsync(session.Id, ct);
            }

            return session;
        }, ct);
    }

    public async Task<List<BulkOperationHistoryItem>> GetRecentSessionsAsync(
        string? userId = null,
        int limit = 20,
        CancellationToken ct = default)
    {
        return await ExecuteAsync(async connection =>
        {
            var sql = @"
                SELECT TOP (@Limit)
                    s.Id AS SessionId,
                    s.IssueId,
                    s.IssueTitle,
                    s.UserId,
                    s.UserDisplayName,
                    s.ExecutedAt,
                    s.ItemCount,
                    s.SuccessCount,
                    s.FailedCount,
                    s.Status,
                    s.DepartmentFilter,
                    CASE
                        WHEN s.Status IN ('FullyRolledBack', 'Failed') THEN 0
                        WHEN s.ExecutedAt < DATEADD(HOUR, -@RollbackHours, GETUTCDATE()) THEN 0
                        ELSE 1
                    END AS CanRollback
                FROM BulkOperationSessions s
                WHERE (@UserId IS NULL OR s.UserId = @UserId)
                ORDER BY s.ExecutedAt DESC";

            var results = await connection.QueryAsync<BulkOperationHistoryItem>(new CommandDefinition(
                sql,
                new { Limit = limit, UserId = userId, RollbackHours = RollbackTimeLimitHours },
                commandTimeout: 30,
                cancellationToken: ct));

            var items = results.ToList();
            foreach (var item in items)
            {
                item.TimeAgo = GetTimeAgo(item.ExecutedAt);
            }

            return items;
        }, ct);
    }

    public async Task<List<BulkOperationHistoryItem>> GetSessionsByIssueAsync(
        string issueId,
        int limit = 10,
        CancellationToken ct = default)
    {
        return await ExecuteAsync(async connection =>
        {
            var results = await connection.QueryAsync<BulkOperationHistoryItem>(new CommandDefinition(@"
                SELECT TOP (@Limit)
                    s.Id AS SessionId,
                    s.IssueId,
                    s.IssueTitle,
                    s.UserId,
                    s.UserDisplayName,
                    s.ExecutedAt,
                    s.ItemCount,
                    s.SuccessCount,
                    s.FailedCount,
                    s.Status,
                    s.DepartmentFilter,
                    CASE
                        WHEN s.Status IN ('FullyRolledBack', 'Failed') THEN 0
                        WHEN s.ExecutedAt < DATEADD(HOUR, -@RollbackHours, GETUTCDATE()) THEN 0
                        ELSE 1
                    END AS CanRollback
                FROM BulkOperationSessions s
                WHERE s.IssueId = @IssueId
                ORDER BY s.ExecutedAt DESC",
                new { IssueId = issueId, Limit = limit, RollbackHours = RollbackTimeLimitHours },
                commandTimeout: 30,
                cancellationToken: ct));

            var items = results.ToList();
            foreach (var item in items)
            {
                item.TimeAgo = GetTimeAgo(item.ExecutedAt);
            }

            return items;
        }, ct);
    }

    public async Task UpdateSessionStatusAsync(
        Guid sessionId,
        string status,
        string? rolledBackBy = null,
        CancellationToken ct = default)
    {
        await ExecuteAsync(async connection =>
        {
            await connection.ExecuteAsync(new CommandDefinition(@"
                UPDATE BulkOperationSessions
                SET Status = @Status,
                    LastModifiedAt = GETUTCDATE(),
                    RolledBackBy = COALESCE(@RolledBackBy, RolledBackBy),
                    RolledBackAt = CASE WHEN @RolledBackBy IS NOT NULL THEN GETUTCDATE() ELSE RolledBackAt END
                WHERE Id = @SessionId",
                new { SessionId = sessionId, Status = status, RolledBackBy = rolledBackBy },
                commandTimeout: 30,
                cancellationToken: ct));

            return true;
        }, ct);
    }

    // ============================================================================
    // CHANGE TRACKING
    // ============================================================================

    public async Task RecordChangesAsync(Guid sessionId, List<BulkOperationChange> changes, CancellationToken ct = default)
    {
        if (!changes.Any()) return;

        await ExecuteAsync(async connection =>
        {
            // Set session ID for all changes
            foreach (var change in changes)
            {
                change.SessionId = sessionId;
            }

            // Batch insert using Dapper
            await connection.ExecuteAsync(new CommandDefinition(@"
                INSERT INTO BulkOperationChanges (
                    Id, SessionId, EntityId, EntityType, EntityName,
                    PropertyName, OldValue, NewValue, IsRolledBack, Metadata
                ) VALUES (
                    @Id, @SessionId, @EntityId, @EntityType, @EntityName,
                    @PropertyName, @OldValue, @NewValue, @IsRolledBack, @Metadata
                )",
                changes,
                commandTimeout: 120,
                cancellationToken: ct));

            return true;
        }, ct);
    }

    public async Task<List<BulkOperationChange>> GetSessionChangesAsync(Guid sessionId, CancellationToken ct = default)
    {
        return await ExecuteAsync(async connection =>
        {
            var results = await connection.QueryAsync<BulkOperationChange>(new CommandDefinition(@"
                SELECT Id, SessionId, EntityId, EntityType, EntityName,
                       PropertyName, OldValue, NewValue, IsRolledBack, RolledBackAt, RollbackError, Metadata
                FROM BulkOperationChanges
                WHERE SessionId = @SessionId
                ORDER BY Id",
                new { SessionId = sessionId },
                commandTimeout: 60,
                cancellationToken: ct));

            return results.ToList();
        }, ct);
    }

    public async Task<List<BulkOperationChange>> GetRollbackableChangesAsync(Guid sessionId, CancellationToken ct = default)
    {
        return await ExecuteAsync(async connection =>
        {
            var results = await connection.QueryAsync<BulkOperationChange>(new CommandDefinition(@"
                SELECT Id, SessionId, EntityId, EntityType, EntityName,
                       PropertyName, OldValue, NewValue, IsRolledBack, RolledBackAt, RollbackError, Metadata
                FROM BulkOperationChanges
                WHERE SessionId = @SessionId
                  AND IsRolledBack = 0
                ORDER BY Id",
                new { SessionId = sessionId },
                commandTimeout: 60,
                cancellationToken: ct));

            return results.ToList();
        }, ct);
    }

    public async Task MarkChangeRolledBackAsync(Guid changeId, string? error = null, CancellationToken ct = default)
    {
        await ExecuteAsync(async connection =>
        {
            await connection.ExecuteAsync(new CommandDefinition(@"
                UPDATE BulkOperationChanges
                SET IsRolledBack = 1,
                    RolledBackAt = GETUTCDATE(),
                    RollbackError = @Error
                WHERE Id = @ChangeId",
                new { ChangeId = changeId, Error = error },
                commandTimeout: 30,
                cancellationToken: ct));

            return true;
        }, ct);
    }

    public async Task MarkChangesRolledBackAsync(List<Guid> changeIds, CancellationToken ct = default)
    {
        if (!changeIds.Any()) return;

        await ExecuteAsync(async connection =>
        {
            await connection.ExecuteAsync(new CommandDefinition(@"
                UPDATE BulkOperationChanges
                SET IsRolledBack = 1,
                    RolledBackAt = GETUTCDATE()
                WHERE Id IN @ChangeIds",
                new { ChangeIds = changeIds },
                commandTimeout: 60,
                cancellationToken: ct));

            return true;
        }, ct);
    }

    // ============================================================================
    // ROLLBACK OPERATIONS
    // ============================================================================

    public async Task<(bool CanRollback, string Reason)> CanRollbackSessionAsync(Guid sessionId, CancellationToken ct = default)
    {
        return await ExecuteAsync(async connection =>
        {
            var session = await connection.QuerySingleOrDefaultAsync<dynamic>(new CommandDefinition(@"
                SELECT Status, ExecutedAt,
                       DATEDIFF(HOUR, ExecutedAt, GETUTCDATE()) AS HoursAgo
                FROM BulkOperationSessions
                WHERE Id = @SessionId",
                new { SessionId = sessionId },
                commandTimeout: 30,
                cancellationToken: ct));

            if (session == null)
                return (false, "Session not found");

            if (session.Status == BulkOperationStatus.FullyRolledBack)
                return (false, "Session has already been fully rolled back");

            if (session.Status == BulkOperationStatus.Failed)
                return (false, "Session failed and cannot be rolled back");

            if (session.HoursAgo > RollbackTimeLimitHours)
                return (false, $"Session is older than {RollbackTimeLimitHours} hours and cannot be rolled back");

            // Check if there are any rollbackable changes
            var rollbackableCount = await connection.ExecuteScalarAsync<int>(new CommandDefinition(@"
                SELECT COUNT(*)
                FROM BulkOperationChanges
                WHERE SessionId = @SessionId AND IsRolledBack = 0",
                new { SessionId = sessionId },
                commandTimeout: 30,
                cancellationToken: ct));

            if (rollbackableCount == 0)
                return (false, "All changes have already been rolled back");

            return (true, $"{rollbackableCount} changes can be rolled back");
        }, ct);
    }

    public async Task<(int Total, int Rollbackable, int AlreadyRolledBack)> GetRollbackStatsAsync(
        Guid sessionId,
        CancellationToken ct = default)
    {
        return await ExecuteAsync(async connection =>
        {
            var stats = await connection.QuerySingleAsync<dynamic>(new CommandDefinition(@"
                SELECT
                    COUNT(*) AS Total,
                    SUM(CASE WHEN IsRolledBack = 0 THEN 1 ELSE 0 END) AS Rollbackable,
                    SUM(CASE WHEN IsRolledBack = 1 THEN 1 ELSE 0 END) AS AlreadyRolledBack
                FROM BulkOperationChanges
                WHERE SessionId = @SessionId",
                new { SessionId = sessionId },
                commandTimeout: 30,
                cancellationToken: ct));

            return ((int)stats.Total, (int)stats.Rollbackable, (int)stats.AlreadyRolledBack);
        }, ct);
    }

    // ============================================================================
    // CLEANUP
    // ============================================================================

    public async Task CleanupOldSessionsAsync(int retentionDays = 30, CancellationToken ct = default)
    {
        await ExecuteAsync(async connection =>
        {
            // First delete changes for old sessions
            await connection.ExecuteAsync(new CommandDefinition(@"
                DELETE FROM BulkOperationChanges
                WHERE SessionId IN (
                    SELECT Id FROM BulkOperationSessions
                    WHERE ExecutedAt < DATEADD(DAY, -@Days, GETUTCDATE())
                )",
                new { Days = retentionDays },
                commandTimeout: 120,
                cancellationToken: ct));

            // Then delete the sessions
            var deletedCount = await connection.ExecuteAsync(new CommandDefinition(@"
                DELETE FROM BulkOperationSessions
                WHERE ExecutedAt < DATEADD(DAY, -@Days, GETUTCDATE())",
                new { Days = retentionDays },
                commandTimeout: 60,
                cancellationToken: ct));

            _logger.LogInformation("Cleaned up {Count} bulk operation sessions older than {Days} days",
                deletedCount, retentionDays);

            return true;
        }, ct);
    }

    // ============================================================================
    // HELPERS
    // ============================================================================

    private static string GetTimeAgo(DateTime dateTime)
    {
        var timeSpan = DateTime.UtcNow - dateTime;

        if (timeSpan.TotalMinutes < 1)
            return "just now";
        if (timeSpan.TotalMinutes < 60)
            return $"{(int)timeSpan.TotalMinutes} min ago";
        if (timeSpan.TotalHours < 24)
            return $"{(int)timeSpan.TotalHours} hours ago";
        if (timeSpan.TotalDays < 7)
            return $"{(int)timeSpan.TotalDays} days ago";

        return dateTime.ToString("MMM d, yyyy");
    }
}
