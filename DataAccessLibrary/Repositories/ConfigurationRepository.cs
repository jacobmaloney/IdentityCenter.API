using Dapper;
using DataAccessLibrary.Models;
using Logging;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;

namespace DataAccessLibrary.Repositories;

public class ConfigurationRepository : DapperRepositoryBase, IConfigurationRepository
{
    public ConfigurationRepository(IConfiguration configuration, IGlobalLogger logger) : base(configuration, logger) { }

    private SqlConnection CreateConnection() => new SqlConnection(_connectionString);

    #region System Configuration

    public async Task<SystemConfiguration?> GetSystemConfigurationAsync()
    {
        using var conn = CreateConnection();
        return await conn.QueryFirstOrDefaultAsync<SystemConfiguration>(
            "SELECT TOP 1 * FROM SystemConfigurations ORDER BY Id").ConfigureAwait(false);
    }

    public async Task UpsertSystemConfigurationAsync(SystemConfiguration config)
    {
        using var conn = CreateConnection();
        config.ModifiedAt = DateTime.UtcNow;

        var exists = await conn.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM SystemConfigurations WHERE Id = @Id", new { config.Id }).ConfigureAwait(false);

        if (exists > 0)
        {
            await conn.ExecuteAsync(@"
                UPDATE SystemConfigurations SET
                    AllowSelfRegistration = @AllowSelfRegistration,
                    RequireEmailConfirmation = @RequireEmailConfirmation,
                    AllowExternalLogins = @AllowExternalLogins,
                    MinimumPasswordLength = @MinimumPasswordLength,
                    RequireDigit = @RequireDigit,
                    RequireLowercase = @RequireLowercase,
                    RequireUppercase = @RequireUppercase,
                    RequireNonAlphanumeric = @RequireNonAlphanumeric,
                    MaxFailedAccessAttempts = @MaxFailedAccessAttempts,
                    LockoutDurationMinutes = @LockoutDurationMinutes,
                    SessionTimeoutMinutes = @SessionTimeoutMinutes,
                    SlidingExpiration = @SlidingExpiration,
                    EnableAuditLogging = @EnableAuditLogging,
                    AuditRetentionDays = @AuditRetentionDays,
                    PortalUrl = @PortalUrl,
                    PortalDisplayName = @PortalDisplayName,
                    AdminNotificationEmail = @AdminNotificationEmail,
                    EnablePolicyNotifications = @EnablePolicyNotifications,
                    EnableSyncNotifications = @EnableSyncNotifications,
                    EnableEscalationNotifications = @EnableEscalationNotifications,
                    ChatLlmEnabled = @ChatLlmEnabled,
                    ChatLlmProvider = @ChatLlmProvider,
                    ChatLlmEndpoint = @ChatLlmEndpoint,
                    ChatLlmApiKey = @ChatLlmApiKey,
                    ChatLlmModel = @ChatLlmModel,
                    ChatLlmMaxTokens = @ChatLlmMaxTokens,
                    ChatLlmTemperature = @ChatLlmTemperature,
                    ChatLlmTimeoutSeconds = @ChatLlmTimeoutSeconds,
                    ComplianceEscalationSettings = @ComplianceEscalationSettings,
                    NotificationIntegrationSettings = @NotificationIntegrationSettings,
                    ModifiedAt = @ModifiedAt,
                    ModifiedBy = @ModifiedBy
                WHERE Id = @Id", config).ConfigureAwait(false);
        }
        else
        {
            config.CreatedAt = DateTime.UtcNow;
            await conn.ExecuteAsync(@"
                INSERT INTO SystemConfigurations (Id, AllowSelfRegistration, RequireEmailConfirmation, AllowExternalLogins,
                    MinimumPasswordLength, RequireDigit, RequireLowercase, RequireUppercase, RequireNonAlphanumeric,
                    MaxFailedAccessAttempts, LockoutDurationMinutes, SessionTimeoutMinutes, SlidingExpiration,
                    EnableAuditLogging, AuditRetentionDays, PortalUrl, PortalDisplayName,
                    AdminNotificationEmail, EnablePolicyNotifications, EnableSyncNotifications, EnableEscalationNotifications,
                    ChatLlmEnabled, ChatLlmProvider, ChatLlmEndpoint, ChatLlmApiKey, ChatLlmModel,
                    ChatLlmMaxTokens, ChatLlmTemperature, ChatLlmTimeoutSeconds,
                    ComplianceEscalationSettings, NotificationIntegrationSettings,
                    CreatedAt, ModifiedAt, ModifiedBy)
                VALUES (@Id, @AllowSelfRegistration, @RequireEmailConfirmation, @AllowExternalLogins,
                    @MinimumPasswordLength, @RequireDigit, @RequireLowercase, @RequireUppercase, @RequireNonAlphanumeric,
                    @MaxFailedAccessAttempts, @LockoutDurationMinutes, @SessionTimeoutMinutes, @SlidingExpiration,
                    @EnableAuditLogging, @AuditRetentionDays, @PortalUrl, @PortalDisplayName,
                    @AdminNotificationEmail, @EnablePolicyNotifications, @EnableSyncNotifications, @EnableEscalationNotifications,
                    @ChatLlmEnabled, @ChatLlmProvider, @ChatLlmEndpoint, @ChatLlmApiKey, @ChatLlmModel,
                    @ChatLlmMaxTokens, @ChatLlmTemperature, @ChatLlmTimeoutSeconds,
                    @ComplianceEscalationSettings, @NotificationIntegrationSettings,
                    @CreatedAt, @ModifiedAt, @ModifiedBy)", config).ConfigureAwait(false);
        }
    }

    #endregion

    #region Settings

    public async Task<List<Setting>> GetSettingsAsync()
    {
        using var conn = CreateConnection();
        var result = await conn.QueryAsync<Setting>(
            "SELECT * FROM Settings ORDER BY Category, [Key]").ConfigureAwait(false);
        return result.ToList();
    }

    public async Task<Setting?> GetSettingAsync(string key)
    {
        using var conn = CreateConnection();
        return await conn.QueryFirstOrDefaultAsync<Setting>(
            "SELECT * FROM Settings WHERE [Key] = @Key", new { Key = key }).ConfigureAwait(false);
    }

    public async Task<Setting?> GetSettingByCategoryAndKeyAsync(string category, string key)
    {
        using var conn = CreateConnection();
        return await conn.QueryFirstOrDefaultAsync<Setting>(
            "SELECT * FROM Settings WHERE Category = @Category AND [Key] = @Key",
            new { Category = category, Key = key }).ConfigureAwait(false);
    }

    public async Task UpsertSettingAsync(string category, string key, string value, string? dataType = null, bool isEncrypted = false)
    {
        using var conn = CreateConnection();
        await conn.ExecuteAsync(@"
            MERGE Settings AS target
            USING (SELECT @Category AS Category, @Key AS [Key]) AS source
            ON target.Category = source.Category AND target.[Key] = source.[Key]
            WHEN MATCHED THEN
                UPDATE SET Value = @Value, DataType = @DataType, IsEncrypted = @IsEncrypted, ModifiedAt = GETUTCDATE()
            WHEN NOT MATCHED THEN
                INSERT (Category, [Key], Value, DataType, IsEncrypted, ModifiedAt)
                VALUES (@Category, @Key, @Value, @DataType, @IsEncrypted, GETUTCDATE());",
            new { Category = category, Key = key, Value = value, DataType = dataType, IsEncrypted = isEncrypted }).ConfigureAwait(false);
    }

    #endregion

    #region Maintenance Settings

    public async Task<MaintenanceSettings?> GetMaintenanceSettingsAsync()
    {
        using var conn = CreateConnection();
        return await conn.QueryFirstOrDefaultAsync<MaintenanceSettings>(
            "SELECT TOP 1 * FROM MaintenanceSettings ORDER BY Id").ConfigureAwait(false);
    }

    public async Task UpsertMaintenanceSettingsAsync(MaintenanceSettings settings)
    {
        using var conn = CreateConnection();
        settings.ModifiedAt = DateTime.UtcNow;

        var exists = await conn.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM MaintenanceSettings WHERE Id = @Id", new { settings.Id }).ConfigureAwait(false);

        if (exists > 0)
        {
            await conn.ExecuteAsync(@"
                UPDATE MaintenanceSettings SET
                    SyncLogRetentionDays = @SyncLogRetentionDays,
                    ChangeLogRetentionDays = @ChangeLogRetentionDays,
                    ChangeLogRetentionMode = @ChangeLogRetentionMode,
                    ChangeLogMaxRecordCount = @ChangeLogMaxRecordCount,
                    ChangeLogMaxSizeMB = @ChangeLogMaxSizeMB,
                    SystemLogRetentionDays = @SystemLogRetentionDays,
                    JobHistoryRetentionDays = @JobHistoryRetentionDays,
                    NotificationLogRetentionDays = @NotificationLogRetentionDays,
                    EnableIndexMaintenance = @EnableIndexMaintenance,
                    IndexReorganizeThreshold = @IndexReorganizeThreshold,
                    IndexRebuildThreshold = @IndexRebuildThreshold,
                    EnableStatisticsUpdate = @EnableStatisticsUpdate,
                    StatisticsUpdateThreshold = @StatisticsUpdateThreshold,
                    EnableSessionCleanup = @EnableSessionCleanup,
                    ExpiredSessionRetentionDays = @ExpiredSessionRetentionDays,
                    EnableOrphanedDataCleanup = @EnableOrphanedDataCleanup,
                    OrphanedDataRetentionDays = @OrphanedDataRetentionDays,
                    EnableTempFileCleanup = @EnableTempFileCleanup,
                    TempFileRetentionDays = @TempFileRetentionDays,
                    LogCleanupSchedule = @LogCleanupSchedule,
                    IndexMaintenanceSchedule = @IndexMaintenanceSchedule,
                    StatisticsUpdateSchedule = @StatisticsUpdateSchedule,
                    SessionCleanupSchedule = @SessionCleanupSchedule,
                    OrphanedDataCleanupSchedule = @OrphanedDataCleanupSchedule,
                    LogCleanupEnabled = @LogCleanupEnabled,
                    IndexMaintenanceEnabled = @IndexMaintenanceEnabled,
                    StatisticsUpdateEnabled = @StatisticsUpdateEnabled,
                    SessionCleanupEnabled = @SessionCleanupEnabled,
                    OrphanedDataCleanupEnabled = @OrphanedDataCleanupEnabled,
                    LastLogCleanupRun = @LastLogCleanupRun,
                    LastIndexMaintenanceRun = @LastIndexMaintenanceRun,
                    LastStatisticsUpdateRun = @LastStatisticsUpdateRun,
                    LastSessionCleanupRun = @LastSessionCleanupRun,
                    LastOrphanedDataCleanupRun = @LastOrphanedDataCleanupRun,
                    ModifiedAt = @ModifiedAt,
                    ModifiedBy = @ModifiedBy
                WHERE Id = @Id", settings).ConfigureAwait(false);
        }
        else
        {
            settings.CreatedAt = DateTime.UtcNow;
            await conn.ExecuteAsync(@"
                INSERT INTO MaintenanceSettings (Id, SyncLogRetentionDays, ChangeLogRetentionDays, ChangeLogRetentionMode,
                    ChangeLogMaxRecordCount, ChangeLogMaxSizeMB, SystemLogRetentionDays, JobHistoryRetentionDays,
                    NotificationLogRetentionDays, EnableIndexMaintenance, IndexReorganizeThreshold, IndexRebuildThreshold,
                    EnableStatisticsUpdate, StatisticsUpdateThreshold, EnableSessionCleanup, ExpiredSessionRetentionDays,
                    EnableOrphanedDataCleanup, OrphanedDataRetentionDays, EnableTempFileCleanup, TempFileRetentionDays,
                    LogCleanupSchedule, IndexMaintenanceSchedule, StatisticsUpdateSchedule, SessionCleanupSchedule,
                    OrphanedDataCleanupSchedule, LogCleanupEnabled, IndexMaintenanceEnabled, StatisticsUpdateEnabled,
                    SessionCleanupEnabled, OrphanedDataCleanupEnabled,
                    LastLogCleanupRun, LastIndexMaintenanceRun, LastStatisticsUpdateRun, LastSessionCleanupRun,
                    LastOrphanedDataCleanupRun, CreatedAt, ModifiedAt, ModifiedBy)
                VALUES (@Id, @SyncLogRetentionDays, @ChangeLogRetentionDays, @ChangeLogRetentionMode,
                    @ChangeLogMaxRecordCount, @ChangeLogMaxSizeMB, @SystemLogRetentionDays, @JobHistoryRetentionDays,
                    @NotificationLogRetentionDays, @EnableIndexMaintenance, @IndexReorganizeThreshold, @IndexRebuildThreshold,
                    @EnableStatisticsUpdate, @StatisticsUpdateThreshold, @EnableSessionCleanup, @ExpiredSessionRetentionDays,
                    @EnableOrphanedDataCleanup, @OrphanedDataRetentionDays, @EnableTempFileCleanup, @TempFileRetentionDays,
                    @LogCleanupSchedule, @IndexMaintenanceSchedule, @StatisticsUpdateSchedule, @SessionCleanupSchedule,
                    @OrphanedDataCleanupSchedule, @LogCleanupEnabled, @IndexMaintenanceEnabled, @StatisticsUpdateEnabled,
                    @SessionCleanupEnabled, @OrphanedDataCleanupEnabled,
                    @LastLogCleanupRun, @LastIndexMaintenanceRun, @LastStatisticsUpdateRun, @LastSessionCleanupRun,
                    @LastOrphanedDataCleanupRun, @CreatedAt, @ModifiedAt, @ModifiedBy)", settings).ConfigureAwait(false);
        }
    }

    #endregion
}
