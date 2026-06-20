using Dapper;
using DataAccessLibrary.Models;
using Logging;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;

namespace DataAccessLibrary.Repositories;

/// <summary>
/// Dapper-based implementation of <see cref="ICloudActivityRepository"/>.
/// Handles SignInLogs, SignInSummaries, M365UsageReports, AppRoleAssignments, EnterpriseApps.
/// </summary>
public class CloudActivityRepository : ICloudActivityRepository
{
    private readonly string _defaultConnectionString;
    private readonly IGlobalLogger _logger;

    public CloudActivityRepository(IConfiguration configuration, IGlobalLogger logger)
    {
        _defaultConnectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    // Resolved PER CALL via the ambient tenant accessor so a tenant-scoped API
    // request hits ONLY its own DB; falls back to DefaultConnection for the
    // in-process orchestrator / admin (no resolver installed). Matches the
    // sign-in-log ingest path's tenant scoping exactly.
    private string _connectionString =>
        DataAccessLibrary.ControlPlane.TenantConnectionAccessor.Current?.Resolve() ?? _defaultConnectionString;

    private SqlConnection CreateConnection() => new SqlConnection(_connectionString);

    // ─────────────────────────────────────────────────────────────────────────
    // Sign-In Logs
    // ─────────────────────────────────────────────────────────────────────────

    public async Task<int> BulkUpsertSignInLogsAsync(IEnumerable<SignInLog> logs, CancellationToken ct = default)
    {
        const string sql = @"
            INSERT INTO SignInLogs
                (Id, SourceConnectionId, ObjectId, SignInId, SignInDateTime,
                 AppDisplayName, AppId, ClientAppUsed, DeviceDetail, IpAddress,
                 Location, Status, ErrorCode, RiskLevel, RiskState,
                 ConditionalAccessStatus, IsInteractive, ResourceDisplayName, ResourceId, CreatedAt)
            SELECT
                @Id, @SourceConnectionId, @ObjectId, @SignInId, @SignInDateTime,
                @AppDisplayName, @AppId, @ClientAppUsed, @DeviceDetail, @IpAddress,
                @Location, @Status, @ErrorCode, @RiskLevel, @RiskState,
                @ConditionalAccessStatus, @IsInteractive, @ResourceDisplayName, @ResourceId, @CreatedAt
            WHERE NOT EXISTS (
                SELECT 1 FROM SignInLogs
                WHERE SignInId = @SignInId AND @SignInId IS NOT NULL
            );";

        int inserted = 0;

        using var conn = CreateConnection();
        await conn.OpenAsync(ct);

        foreach (var log in logs)
        {
            if (ct.IsCancellationRequested) break;

            var affected = await conn.ExecuteAsync(sql, new
            {
                log.Id,
                log.SourceConnectionId,
                log.ObjectId,
                log.SignInId,
                log.SignInDateTime,
                log.AppDisplayName,
                log.AppId,
                log.ClientAppUsed,
                log.DeviceDetail,
                log.IpAddress,
                log.Location,
                log.Status,
                log.ErrorCode,
                log.RiskLevel,
                log.RiskState,
                log.ConditionalAccessStatus,
                log.IsInteractive,
                log.ResourceDisplayName,
                log.ResourceId,
                log.CreatedAt
            });

            inserted += affected;
        }

        _logger.LogInformation(
            "CloudActivityRepository.BulkUpsertSignInLogsAsync: Inserted {Inserted} sign-in log records",
            inserted);

        return inserted;
    }

    public async Task UpsertSignInSummaryAsync(SignInSummary summary, CancellationToken ct = default)
    {
        const string sql = @"
            MERGE SignInSummaries AS tgt
            USING (SELECT @ObjectId AS ObjectId, @AppDisplayName AS AppDisplayName, @SummaryDate AS SummaryDate) AS src
            ON tgt.ObjectId = src.ObjectId
               AND tgt.AppDisplayName = src.AppDisplayName
               AND tgt.SummaryDate = src.SummaryDate
            WHEN MATCHED THEN
                UPDATE SET
                    SuccessCount = @SuccessCount,
                    FailureCount = @FailureCount,
                    InteractiveCount = @InteractiveCount,
                    NonInteractiveCount = @NonInteractiveCount,
                    UniqueLocations = @UniqueLocations
            WHEN NOT MATCHED THEN
                INSERT (Id, ObjectId, SourceConnectionId, AppDisplayName, SummaryDate,
                        SuccessCount, FailureCount, InteractiveCount, NonInteractiveCount, UniqueLocations)
                VALUES (@Id, @ObjectId, @SourceConnectionId, @AppDisplayName, @SummaryDate,
                        @SuccessCount, @FailureCount, @InteractiveCount, @NonInteractiveCount, @UniqueLocations);";

        using var conn = CreateConnection();
        await conn.OpenAsync(ct);
        await conn.ExecuteAsync(sql, new
        {
            summary.Id,
            summary.ObjectId,
            summary.SourceConnectionId,
            summary.AppDisplayName,
            summary.SummaryDate,
            summary.SuccessCount,
            summary.FailureCount,
            summary.InteractiveCount,
            summary.NonInteractiveCount,
            summary.UniqueLocations
        });
    }

    public async Task<DateTime?> GetLatestSignInDateAsync(Guid connectionId, CancellationToken ct = default)
    {
        const string sql = @"
            SELECT MAX(SignInDateTime)
            FROM SignInLogs
            WHERE SourceConnectionId = @ConnectionId;";

        using var conn = CreateConnection();
        await conn.OpenAsync(ct);
        return await conn.QuerySingleOrDefaultAsync<DateTime?>(sql, new { ConnectionId = connectionId });
    }

    public async Task<(List<SignInLog> Items, int Total)> GetSignInLogsForObjectAsync(
        Guid objectId,
        int page = 1,
        int pageSize = 25,
        CancellationToken ct = default)
    {
        const string countSql = "SELECT COUNT(*) FROM SignInLogs WHERE ObjectId = @ObjectId;";
        const string dataSql = @"
            SELECT * FROM SignInLogs
            WHERE ObjectId = @ObjectId
            ORDER BY SignInDateTime DESC
            OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY;";

        using var conn = CreateConnection();
        await conn.OpenAsync(ct);
        var total = await conn.QuerySingleAsync<int>(countSql, new { ObjectId = objectId });
        var offset = (page - 1) * pageSize;
        var items = (await conn.QueryAsync<SignInLog>(dataSql, new { ObjectId = objectId, Offset = offset, PageSize = pageSize })).ToList();
        return (items, total);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // M365 Usage Reports
    // ─────────────────────────────────────────────────────────────────────────

    public async Task<int> BulkUpsertUsageReportsAsync(IEnumerable<M365UsageReport> reports, CancellationToken ct = default)
    {
        const string sql = @"
            MERGE M365UsageReports AS tgt
            USING (SELECT @ObjectId AS ObjectId, @ReportRefreshDate AS ReportRefreshDate) AS src
            ON tgt.ObjectId = src.ObjectId AND tgt.ReportRefreshDate = src.ReportRefreshDate
            WHEN MATCHED THEN
                UPDATE SET
                    SourceConnectionId = @SourceConnectionId,
                    UserPrincipalName = @UserPrincipalName,
                    DisplayName = @DisplayName,
                    HasExchangeLicense = @HasExchangeLicense,
                    HasOneDriveLicense = @HasOneDriveLicense,
                    HasSharePointLicense = @HasSharePointLicense,
                    HasTeamsLicense = @HasTeamsLicense,
                    HasYammerLicense = @HasYammerLicense,
                    ExchangeLastActivityDate = @ExchangeLastActivityDate,
                    OneDriveLastActivityDate = @OneDriveLastActivityDate,
                    SharePointLastActivityDate = @SharePointLastActivityDate,
                    TeamsLastActivityDate = @TeamsLastActivityDate,
                    YammerLastActivityDate = @YammerLastActivityDate,
                    ExchangeMailSent = @ExchangeMailSent,
                    ExchangeMailReceived = @ExchangeMailReceived,
                    OneDriveFilesViewed = @OneDriveFilesViewed,
                    OneDriveFilesSynced = @OneDriveFilesSynced,
                    OneDriveStorageUsedBytes = @OneDriveStorageUsedBytes,
                    OneDriveStorageAllocatedBytes = @OneDriveStorageAllocatedBytes,
                    MailboxStorageUsedBytes = @MailboxStorageUsedBytes,
                    MailboxQuotaBytes = @MailboxQuotaBytes,
                    SharePointFilesViewed = @SharePointFilesViewed,
                    SharePointFilesShared = @SharePointFilesShared,
                    TeamsChatMessages = @TeamsChatMessages,
                    TeamsCallCount = @TeamsCallCount,
                    TeamsMeetingCount = @TeamsMeetingCount,
                    AssignedProducts = @AssignedProducts,
                    LastSyncedAt = @LastSyncedAt
            WHEN NOT MATCHED THEN
                INSERT (Id, SourceConnectionId, ObjectId, ReportRefreshDate,
                        UserPrincipalName, DisplayName,
                        HasExchangeLicense, HasOneDriveLicense, HasSharePointLicense,
                        HasTeamsLicense, HasYammerLicense,
                        ExchangeLastActivityDate, OneDriveLastActivityDate,
                        SharePointLastActivityDate, TeamsLastActivityDate, YammerLastActivityDate,
                        ExchangeMailSent, ExchangeMailReceived,
                        OneDriveFilesViewed, OneDriveFilesSynced,
                        OneDriveStorageUsedBytes, OneDriveStorageAllocatedBytes,
                        MailboxStorageUsedBytes, MailboxQuotaBytes,
                        SharePointFilesViewed, SharePointFilesShared,
                        TeamsChatMessages, TeamsCallCount, TeamsMeetingCount,
                        AssignedProducts, LastSyncedAt)
                VALUES (@Id, @SourceConnectionId, @ObjectId, @ReportRefreshDate,
                        @UserPrincipalName, @DisplayName,
                        @HasExchangeLicense, @HasOneDriveLicense, @HasSharePointLicense,
                        @HasTeamsLicense, @HasYammerLicense,
                        @ExchangeLastActivityDate, @OneDriveLastActivityDate,
                        @SharePointLastActivityDate, @TeamsLastActivityDate, @YammerLastActivityDate,
                        @ExchangeMailSent, @ExchangeMailReceived,
                        @OneDriveFilesViewed, @OneDriveFilesSynced,
                        @OneDriveStorageUsedBytes, @OneDriveStorageAllocatedBytes,
                        @MailboxStorageUsedBytes, @MailboxQuotaBytes,
                        @SharePointFilesViewed, @SharePointFilesShared,
                        @TeamsChatMessages, @TeamsCallCount, @TeamsMeetingCount,
                        @AssignedProducts, @LastSyncedAt);";

        int count = 0;

        using var conn = CreateConnection();
        await conn.OpenAsync(ct);

        foreach (var report in reports)
        {
            if (ct.IsCancellationRequested) break;

            await conn.ExecuteAsync(sql, new
            {
                report.Id,
                report.SourceConnectionId,
                report.ObjectId,
                report.ReportRefreshDate,
                report.UserPrincipalName,
                report.DisplayName,
                report.HasExchangeLicense,
                report.HasOneDriveLicense,
                report.HasSharePointLicense,
                report.HasTeamsLicense,
                report.HasYammerLicense,
                report.ExchangeLastActivityDate,
                report.OneDriveLastActivityDate,
                report.SharePointLastActivityDate,
                report.TeamsLastActivityDate,
                report.YammerLastActivityDate,
                report.ExchangeMailSent,
                report.ExchangeMailReceived,
                report.OneDriveFilesViewed,
                report.OneDriveFilesSynced,
                report.OneDriveStorageUsedBytes,
                report.OneDriveStorageAllocatedBytes,
                report.MailboxStorageUsedBytes,
                report.MailboxQuotaBytes,
                report.SharePointFilesViewed,
                report.SharePointFilesShared,
                report.TeamsChatMessages,
                report.TeamsCallCount,
                report.TeamsMeetingCount,
                report.AssignedProducts,
                report.LastSyncedAt
            });

            count++;
        }

        _logger.LogInformation(
            "CloudActivityRepository.BulkUpsertUsageReportsAsync: Upserted {Count} M365 usage report records",
            count);

        return count;
    }

    public async Task<M365UsageReport?> GetLatestUsageReportForObjectAsync(Guid objectId, CancellationToken ct = default)
    {
        const string sql = @"
            SELECT TOP 1 *
            FROM M365UsageReports
            WHERE ObjectId = @ObjectId
            ORDER BY ReportRefreshDate DESC;";

        using var conn = CreateConnection();
        await conn.OpenAsync(ct);
        return await conn.QueryFirstOrDefaultAsync<M365UsageReport>(sql, new { ObjectId = objectId });
    }

    // ─────────────────────────────────────────────────────────────────────────
    // App Role Assignments
    // ─────────────────────────────────────────────────────────────────────────

    public async Task<int> BulkUpsertAppRoleAssignmentsAsync(IEnumerable<AppRoleAssignment> assignments, CancellationToken ct = default)
    {
        const string sql = @"
            INSERT INTO AppRoleAssignments
                (Id, SourceConnectionId, AppRoleAssignmentId,
                 PrincipalId, PrincipalObjectId, PrincipalType, PrincipalDisplayName,
                 ResourceId, ResourceObjectId, ResourceDisplayName,
                 AppRoleId, AppRoleName, CreatedDateTime, IsActive, LastSyncedAt)
            SELECT
                @Id, @SourceConnectionId, @AppRoleAssignmentId,
                @PrincipalId, @PrincipalObjectId, @PrincipalType, @PrincipalDisplayName,
                @ResourceId, @ResourceObjectId, @ResourceDisplayName,
                @AppRoleId, @AppRoleName, @CreatedDateTime, @IsActive, @LastSyncedAt
            WHERE NOT EXISTS (
                SELECT 1 FROM AppRoleAssignments
                WHERE SourceConnectionId = @SourceConnectionId
                  AND AppRoleAssignmentId = @AppRoleAssignmentId
                  AND @AppRoleAssignmentId IS NOT NULL
            );";

        int inserted = 0;

        using var conn = CreateConnection();
        await conn.OpenAsync(ct);

        foreach (var assignment in assignments)
        {
            if (ct.IsCancellationRequested) break;

            var affected = await conn.ExecuteAsync(sql, new
            {
                assignment.Id,
                assignment.SourceConnectionId,
                assignment.AppRoleAssignmentId,
                assignment.PrincipalId,
                assignment.PrincipalObjectId,
                assignment.PrincipalType,
                assignment.PrincipalDisplayName,
                assignment.ResourceId,
                assignment.ResourceObjectId,
                assignment.ResourceDisplayName,
                assignment.AppRoleId,
                assignment.AppRoleName,
                assignment.CreatedDateTime,
                assignment.IsActive,
                assignment.LastSyncedAt
            });

            inserted += affected;
        }

        _logger.LogInformation(
            "CloudActivityRepository.BulkUpsertAppRoleAssignmentsAsync: Inserted {Inserted} app role assignment records",
            inserted);

        return inserted;
    }

    public async Task<int> BulkUpsertEnterpriseAppsAsync(IEnumerable<EnterpriseApp> apps, CancellationToken ct = default)
    {
        const string sql = @"
            MERGE EnterpriseApps AS tgt
            USING (SELECT @SourceConnectionId AS SourceConnectionId, @ServicePrincipalId AS ServicePrincipalId) AS src
            ON tgt.SourceConnectionId = src.SourceConnectionId
               AND tgt.ServicePrincipalId = src.ServicePrincipalId
            WHEN MATCHED THEN
                UPDATE SET
                    ObjectId = @ObjectId,
                    AppId = @AppId,
                    DisplayName = @DisplayName,
                    ServicePrincipalType = @ServicePrincipalType,
                    SignInAudience = @SignInAudience,
                    Homepage = @Homepage,
                    LogoUrl = @LogoUrl,
                    TotalAssignments = @TotalAssignments,
                    UserAssignments = @UserAssignments,
                    GroupAssignments = @GroupAssignments,
                    IsEnabled = @IsEnabled,
                    LastSyncedAt = @LastSyncedAt
            WHEN NOT MATCHED THEN
                INSERT (Id, SourceConnectionId, ServicePrincipalId, ObjectId, AppId,
                        DisplayName, ServicePrincipalType, SignInAudience, Homepage, LogoUrl,
                        TotalAssignments, UserAssignments, GroupAssignments,
                        IsEnabled, LastSyncedAt)
                VALUES (@Id, @SourceConnectionId, @ServicePrincipalId, @ObjectId, @AppId,
                        @DisplayName, @ServicePrincipalType, @SignInAudience, @Homepage, @LogoUrl,
                        @TotalAssignments, @UserAssignments, @GroupAssignments,
                        @IsEnabled, @LastSyncedAt);";

        int count = 0;

        using var conn = CreateConnection();
        await conn.OpenAsync(ct);

        foreach (var app in apps)
        {
            if (ct.IsCancellationRequested) break;

            await conn.ExecuteAsync(sql, new
            {
                app.Id,
                app.SourceConnectionId,
                app.ServicePrincipalId,
                app.ObjectId,
                app.AppId,
                app.DisplayName,
                app.ServicePrincipalType,
                app.SignInAudience,
                app.Homepage,
                app.LogoUrl,
                app.TotalAssignments,
                app.UserAssignments,
                app.GroupAssignments,
                app.IsEnabled,
                app.LastSyncedAt
            });

            count++;
        }

        _logger.LogInformation(
            "CloudActivityRepository.BulkUpsertEnterpriseAppsAsync: Upserted {Count} enterprise app records",
            count);

        return count;
    }

    public async Task<int> DeactivateStaleAppRoleAssignmentsAsync(Guid connectionId, DateTime syncedBefore, CancellationToken ct = default)
    {
        const string sql = @"
            UPDATE AppRoleAssignments
            SET IsActive = 0, LastSyncedAt = GETUTCDATE()
            WHERE SourceConnectionId = @ConnectionId
              AND IsActive = 1
              AND LastSyncedAt < @SyncedBefore;";

        using var conn = CreateConnection();
        await conn.OpenAsync(ct);
        var affected = await conn.ExecuteAsync(sql, new { ConnectionId = connectionId, SyncedBefore = syncedBefore });

        if (affected > 0)
            _logger.LogInformation(
                "CloudActivityRepository.DeactivateStaleAppRoleAssignmentsAsync: Deactivated {Count} stale assignments for connection {ConnectionId}",
                affected, connectionId);

        return affected;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Resolution Helpers
    // ─────────────────────────────────────────────────────────────────────────

    public async Task<Dictionary<string, Guid>> ResolveEntraUserIdsAsync(
        Guid connectionId,
        IEnumerable<string> entraUserIds,
        CancellationToken ct = default)
    {
        const string sql = @"
            SELECT SourceUniqueId, Id
            FROM Objects
            WHERE SourceConnectionId = @ConnectionId
              AND SourceUniqueId IN @UserIds;";

        var allIds = entraUserIds.ToList();
        var result = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);

        if (!allIds.Any())
            return result;

        using var conn = CreateConnection();
        await conn.OpenAsync(ct);

        foreach (var batch in ChunkList(allIds, 2000))
        {
            var rows = await conn.QueryAsync<(string SourceUniqueId, Guid Id)>(sql,
                new { ConnectionId = connectionId, UserIds = batch });

            foreach (var row in rows)
            {
                result.TryAdd(row.SourceUniqueId, row.Id);
            }
        }

        _logger.LogInformation(
            "CloudActivityRepository.ResolveEntraUserIdsAsync: Resolved {Resolved}/{Total} for connection {ConnectionId}",
            result.Count, allIds.Count, connectionId);

        return result;
    }

    public async Task<Dictionary<string, Guid>> ResolveByUPNAsync(
        Guid connectionId,
        IEnumerable<string> upns,
        CancellationToken ct = default)
    {
        const string sql = @"
            SELECT UserPrincipalName, Id
            FROM Objects
            WHERE SourceConnectionId = @ConnectionId
              AND UserPrincipalName IN @UPNs;";

        var allUpns = upns.ToList();
        var result = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);

        if (!allUpns.Any())
            return result;

        using var conn = CreateConnection();
        await conn.OpenAsync(ct);

        foreach (var batch in ChunkList(allUpns, 2000))
        {
            var rows = await conn.QueryAsync<(string UserPrincipalName, Guid Id)>(sql,
                new { ConnectionId = connectionId, UPNs = batch });

            foreach (var row in rows)
            {
                result.TryAdd(row.UserPrincipalName, row.Id);
            }
        }

        _logger.LogInformation(
            "CloudActivityRepository.ResolveByUPNAsync: Resolved {Resolved}/{Total} UPNs for connection {ConnectionId}",
            result.Count, allUpns.Count, connectionId);

        return result;
    }

    public async Task<Dictionary<string, Guid>> ResolveEntraObjectIdsAsync(
        Guid connectionId,
        IEnumerable<string> entraObjectIds,
        CancellationToken ct = default)
    {
        // Same lookup as ResolveEntraUserIdsAsync — SourceUniqueId stores Entra object ID for all types
        return await ResolveEntraUserIdsAsync(connectionId, entraObjectIds, ct);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────────────

    private static List<List<T>> ChunkList<T>(List<T> source, int chunkSize)
    {
        var chunks = new List<List<T>>();
        for (int i = 0; i < source.Count; i += chunkSize)
        {
            chunks.Add(source.GetRange(i, Math.Min(chunkSize, source.Count - i)));
        }
        return chunks;
    }
}
