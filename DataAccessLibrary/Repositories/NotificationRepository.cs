using Dapper;
using DataAccessLibrary.Models;
using Logging;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;

namespace DataAccessLibrary.Repositories;

/// <summary>
/// Repository for admin notification persistence and monitoring queries.
/// </summary>
public class NotificationRepository : DapperRepositoryBase, INotificationRepository
{
    public NotificationRepository(IConfiguration configuration, IGlobalLogger logger)
        : base(configuration, logger)
    {
    }

    public async Task InsertNotificationAsync(AdminNotification notification, CancellationToken cancellationToken = default)
    {
        await ExecuteNonQueryAsync(async connection =>
        {
            notification.Id = Guid.NewGuid();
            await connection.ExecuteAsync(@"
                INSERT INTO AdminNotifications (Id, NotificationType, Category, Severity, Title, Message,
                    ActionUrl, ActionText, RelatedEntityId, RelatedEntityType, Source, CreatedAt, IsRead, IsDismissed)
                VALUES (@Id, @NotificationType, @Category, @Severity, @Title, @Message,
                    @ActionUrl, @ActionText, @RelatedEntityId, @RelatedEntityType, @Source, @CreatedAt, 0, 0)",
                notification);
        }, cancellationToken);
    }

    public async Task<List<FailedSyncInfo>> GetRecentFailedSyncsAsync(CancellationToken cancellationToken = default)
    {
        return await ExecuteAsync(async connection =>
        {
            var results = await connection.QueryAsync<FailedSyncInfo>(new CommandDefinition(@"
                SELECT spr.Id AS RunId, sp.Id AS ProjectId, sp.Name AS ProjectName, spr.CompletedAt, spr.ErrorMessage
                FROM SyncProjectRuns spr
                JOIN SyncProjects sp ON spr.SyncProjectId = sp.Id
                WHERE spr.Status = 'Failed'
                  AND spr.CompletedAt >= DATEADD(HOUR, -1, GETUTCDATE())
                  AND NOT EXISTS (
                      SELECT 1 FROM AdminNotifications an
                      WHERE an.RelatedEntityId = spr.Id
                        AND an.NotificationType = 'SyncFailed'
                  )
                ORDER BY spr.CompletedAt DESC",
                commandTimeout: 30,
                cancellationToken: cancellationToken));

            return results.ToList();
        }, cancellationToken);
    }

    public async Task<List<LongRunningSyncInfo>> GetLongRunningSyncsAsync(CancellationToken cancellationToken = default)
    {
        return await ExecuteAsync(async connection =>
        {
            var results = await connection.QueryAsync<LongRunningSyncInfo>(new CommandDefinition(@"
                SELECT sp.Id, sp.Name, sp.LastRunAt
                FROM SyncProjects sp
                WHERE sp.IsRunning = 1
                  AND sp.LastRunAt < DATEADD(MINUTE, -30, GETUTCDATE())
                  AND NOT EXISTS (
                      SELECT 1 FROM AdminNotifications an
                      WHERE an.RelatedEntityId = sp.Id
                        AND an.NotificationType = 'SyncStuck'
                        AND an.CreatedAt >= DATEADD(HOUR, -1, GETUTCDATE())
                  )",
                commandTimeout: 30,
                cancellationToken: cancellationToken));

            return results.ToList();
        }, cancellationToken);
    }

    public async Task<bool> IsSmtpConfiguredAsync(CancellationToken cancellationToken = default)
    {
        return await ExecuteAsync(async connection =>
        {
            var result = await connection.ExecuteScalarAsync<int>(new CommandDefinition(@"
                SELECT CASE WHEN EXISTS (
                    SELECT 1 FROM SMTPConfiguration WHERE IsActive = 1 AND Server IS NOT NULL AND Server != ''
                ) THEN 1 ELSE 0 END",
                commandTimeout: 30,
                cancellationToken: cancellationToken));

            return result == 1;
        }, cancellationToken);
    }

    public async Task<List<FailedConnectionInfo>> GetFailedConnectionsAsync(CancellationToken cancellationToken = default)
    {
        return await ExecuteAsync(async connection =>
        {
            var results = await connection.QueryAsync<FailedConnectionInfo>(new CommandDefinition(@"
                SELECT Id, Name, LastTestResult AS LastError
                FROM DirectoryConnections
                WHERE LastTestResult IS NOT NULL
                  AND LastTestResult <> 'Success'
                  AND NOT EXISTS (
                      SELECT 1 FROM AdminNotifications an
                      WHERE an.RelatedEntityId = DirectoryConnections.Id
                        AND an.NotificationType = 'ConnectionFailed'
                        AND an.CreatedAt >= DATEADD(HOUR, -6, GETUTCDATE())
                  )",
                commandTimeout: 30,
                cancellationToken: cancellationToken));

            return results.ToList();
        }, cancellationToken);
    }

    public async Task<bool> HasRecentNotificationAsync(string notificationType, string? titlePattern, TimeSpan window, CancellationToken cancellationToken = default)
    {
        return await ExecuteAsync(async connection =>
        {
            var windowMinutes = (int)window.TotalMinutes;
            string sql;

            if (titlePattern != null)
            {
                sql = @"SELECT COUNT(*) FROM AdminNotifications
                        WHERE NotificationType = @NotificationType
                          AND Title LIKE @TitlePattern
                          AND CreatedAt >= DATEADD(MINUTE, -@WindowMinutes, GETUTCDATE())";
            }
            else
            {
                sql = @"SELECT COUNT(*) FROM AdminNotifications
                        WHERE NotificationType = @NotificationType
                          AND CreatedAt >= DATEADD(MINUTE, -@WindowMinutes, GETUTCDATE())";
            }

            var count = await connection.ExecuteScalarAsync<int>(new CommandDefinition(
                sql,
                new { NotificationType = notificationType, TitlePattern = titlePattern, WindowMinutes = windowMinutes },
                commandTimeout: 30,
                cancellationToken: cancellationToken));

            return count > 0;
        }, cancellationToken);
    }

    public async Task<bool> HasRecentEntityNotificationAsync(Guid entityId, string notificationType, TimeSpan window, CancellationToken cancellationToken = default)
    {
        return await ExecuteAsync(async connection =>
        {
            var windowMinutes = (int)window.TotalMinutes;
            var count = await connection.ExecuteScalarAsync<int>(new CommandDefinition(@"
                SELECT COUNT(*) FROM AdminNotifications
                WHERE RelatedEntityId = @EntityId
                  AND NotificationType = @NotificationType
                  AND CreatedAt >= DATEADD(MINUTE, -@WindowMinutes, GETUTCDATE())",
                new { EntityId = entityId, NotificationType = notificationType, WindowMinutes = windowMinutes },
                commandTimeout: 30,
                cancellationToken: cancellationToken));

            return count > 0;
        }, cancellationToken);
    }

    public async Task InsertMonitorNotificationAsync(
        string notificationType, string category, string severity,
        string title, string message, string? actionUrl, string? actionText,
        Guid? relatedEntityId, string? relatedEntityType, string source,
        CancellationToken cancellationToken = default)
    {
        await ExecuteNonQueryAsync(async connection =>
        {
            await connection.ExecuteAsync(new CommandDefinition(@"
                INSERT INTO AdminNotifications (
                    Id, NotificationType, Category, Severity, Title, Message,
                    ActionUrl, ActionText, RelatedEntityId, RelatedEntityType,
                    Source, CreatedAt, IsRead, IsDismissed
                ) VALUES (
                    @Id, @NotificationType, @Category, @Severity, @Title, @Message,
                    @ActionUrl, @ActionText, @RelatedEntityId, @RelatedEntityType,
                    @Source, GETUTCDATE(), 0, 0
                )",
                new
                {
                    Id = Guid.NewGuid(),
                    NotificationType = notificationType,
                    Category = category,
                    Severity = severity,
                    Title = title,
                    Message = message,
                    ActionUrl = actionUrl,
                    ActionText = actionText,
                    RelatedEntityId = relatedEntityId,
                    RelatedEntityType = relatedEntityType,
                    Source = source
                },
                commandTimeout: 30,
                cancellationToken: cancellationToken));
        }, cancellationToken);
    }

    public async Task<int> GetUnreadCountAsync(CancellationToken cancellationToken = default)
    {
        return await ExecuteAsync<int>(async connection =>
        {
            return await connection.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM AdminNotifications WHERE IsRead = 0 AND IsDismissed = 0");
        }, cancellationToken);
    }

    public async Task<(List<AdminNotification> Items, int TotalCount)> GetNotificationsPagedAsync(
        int skip = 0, int take = 50, CancellationToken cancellationToken = default)
    {
        return await ExecuteAsync<(List<AdminNotification>, int)>(async connection =>
        {
            var totalCount = await connection.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM AdminNotifications WHERE IsDismissed = 0");
            var items = (await connection.QueryAsync<AdminNotification>(
                @"SELECT * FROM AdminNotifications WHERE IsDismissed = 0
                  ORDER BY CreatedAt DESC
                  OFFSET @Skip ROWS FETCH NEXT @Take ROWS ONLY",
                new { Skip = skip, Take = take })).ToList();
            return (items, totalCount);
        }, cancellationToken);
    }

    public async Task MarkAsReadAsync(Guid notificationId, CancellationToken cancellationToken = default)
    {
        await ExecuteNonQueryAsync(async connection =>
        {
            await connection.ExecuteAsync(
                "UPDATE AdminNotifications SET IsRead = 1, ReadAt = @ReadAt WHERE Id = @Id",
                new { Id = notificationId, ReadAt = DateTime.UtcNow });
        }, cancellationToken);
    }
}
