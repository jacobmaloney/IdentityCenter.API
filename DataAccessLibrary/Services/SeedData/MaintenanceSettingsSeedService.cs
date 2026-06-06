using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using Microsoft.Data.SqlClient;
using Dapper;
using DataAccessLibrary.Models;

namespace DataAccessLibrary.Services.SeedData;

/// <summary>
/// Seeds default maintenance settings for automated system maintenance jobs.
/// Ensures the singleton MaintenanceSettings record exists with sensible defaults.
/// </summary>
public class MaintenanceSettingsSeedService
{
    private readonly string _connectionString;
    private readonly ILogger<MaintenanceSettingsSeedService> _logger;

    public MaintenanceSettingsSeedService(
        IConfiguration configuration,
        ILogger<MaintenanceSettingsSeedService> logger)
    {
        _connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");
        _logger = logger;
    }

    /// <summary>
    /// Seeds the default maintenance settings if they don't exist.
    /// This ensures maintenance jobs can run immediately after deployment.
    /// </summary>
    public async Task SeedMaintenanceSettingsAsync()
    {
        _logger.LogInformation("Checking for existing maintenance settings...");

        try
        {
            await using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();

            // Check if settings already exist
            var existingSettings = await connection.QueryFirstOrDefaultAsync<MaintenanceSettings>(
                "SELECT TOP 1 * FROM MaintenanceSettings");

            if (existingSettings != null)
            {
                _logger.LogInformation("Maintenance settings already exist (Id: {Id}), skipping seed", existingSettings.Id);
                return;
            }

            _logger.LogInformation("Creating default maintenance settings...");

            var defaultSettings = new MaintenanceSettings
            {
                Id = 1,

                // Log Retention (days) - balanced between compliance and storage
                SyncLogRetentionDays = 30,      // Sync logs - keep 30 days
                ChangeLogRetentionDays = 365,   // Change audit - keep 1 year for compliance
                SystemLogRetentionDays = 90,    // System logs - keep 90 days
                JobHistoryRetentionDays = 30,   // Job history - keep 30 days
                NotificationLogRetentionDays = 60, // Notifications - keep 60 days

                // Database Maintenance
                EnableIndexMaintenance = true,
                IndexReorganizeThreshold = 10,  // Reorganize at 10% fragmentation
                IndexRebuildThreshold = 30,     // Rebuild at 30% fragmentation
                EnableStatisticsUpdate = true,
                StatisticsUpdateThreshold = 20, // Update when 20% rows changed

                // Cleanup Settings
                EnableSessionCleanup = true,
                ExpiredSessionRetentionDays = 7,
                EnableOrphanedDataCleanup = true,
                OrphanedDataRetentionDays = 14,
                EnableTempFileCleanup = true,
                TempFileRetentionDays = 7,

                // Job Schedules (Quartz cron format)
                LogCleanupSchedule = "0 0 2 * * ?",           // Daily at 2 AM
                IndexMaintenanceSchedule = "0 0 3 ? * SUN",   // Sunday at 3 AM
                StatisticsUpdateSchedule = "0 30 3 * * ?",    // Daily at 3:30 AM
                SessionCleanupSchedule = "0 0 */6 * * ?",     // Every 6 hours
                OrphanedDataCleanupSchedule = "0 0 4 * * ?",  // Daily at 4 AM

                // Job Enabled Flags - all enabled by default
                LogCleanupEnabled = true,
                IndexMaintenanceEnabled = true,
                StatisticsUpdateEnabled = true,
                SessionCleanupEnabled = true,
                OrphanedDataCleanupEnabled = true,

                // Audit
                CreatedAt = DateTime.UtcNow,
                ModifiedBy = "System"
            };

            const string insertSql = @"
                INSERT INTO MaintenanceSettings
                    (Id, SyncLogRetentionDays, ChangeLogRetentionDays, SystemLogRetentionDays,
                     JobHistoryRetentionDays, NotificationLogRetentionDays, EnableIndexMaintenance,
                     IndexReorganizeThreshold, IndexRebuildThreshold, EnableStatisticsUpdate,
                     StatisticsUpdateThreshold, EnableSessionCleanup, ExpiredSessionRetentionDays,
                     EnableOrphanedDataCleanup, OrphanedDataRetentionDays, EnableTempFileCleanup,
                     TempFileRetentionDays, LogCleanupSchedule, IndexMaintenanceSchedule,
                     StatisticsUpdateSchedule, SessionCleanupSchedule, OrphanedDataCleanupSchedule,
                     LogCleanupEnabled, IndexMaintenanceEnabled, StatisticsUpdateEnabled,
                     SessionCleanupEnabled, OrphanedDataCleanupEnabled, CreatedAt, ModifiedBy)
                VALUES
                    (@Id, @SyncLogRetentionDays, @ChangeLogRetentionDays, @SystemLogRetentionDays,
                     @JobHistoryRetentionDays, @NotificationLogRetentionDays, @EnableIndexMaintenance,
                     @IndexReorganizeThreshold, @IndexRebuildThreshold, @EnableStatisticsUpdate,
                     @StatisticsUpdateThreshold, @EnableSessionCleanup, @ExpiredSessionRetentionDays,
                     @EnableOrphanedDataCleanup, @OrphanedDataRetentionDays, @EnableTempFileCleanup,
                     @TempFileRetentionDays, @LogCleanupSchedule, @IndexMaintenanceSchedule,
                     @StatisticsUpdateSchedule, @SessionCleanupSchedule, @OrphanedDataCleanupSchedule,
                     @LogCleanupEnabled, @IndexMaintenanceEnabled, @StatisticsUpdateEnabled,
                     @SessionCleanupEnabled, @OrphanedDataCleanupEnabled, @CreatedAt, @ModifiedBy)";

            await connection.ExecuteAsync(insertSql, defaultSettings);

            _logger.LogInformation("Successfully created default maintenance settings with Id: {Id}", defaultSettings.Id);
            _logger.LogInformation("  - Log Cleanup: {Schedule} (enabled: {Enabled})", defaultSettings.LogCleanupSchedule, defaultSettings.LogCleanupEnabled);
            _logger.LogInformation("  - Index Maintenance: {Schedule} (enabled: {Enabled})", defaultSettings.IndexMaintenanceSchedule, defaultSettings.IndexMaintenanceEnabled);
            _logger.LogInformation("  - Statistics Update: {Schedule} (enabled: {Enabled})", defaultSettings.StatisticsUpdateSchedule, defaultSettings.StatisticsUpdateEnabled);
            _logger.LogInformation("  - Session Cleanup: {Schedule} (enabled: {Enabled})", defaultSettings.SessionCleanupSchedule, defaultSettings.SessionCleanupEnabled);
            _logger.LogInformation("  - Orphaned Data Cleanup: {Schedule} (enabled: {Enabled})", defaultSettings.OrphanedDataCleanupSchedule, defaultSettings.OrphanedDataCleanupEnabled);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error seeding maintenance settings");
            throw;
        }
    }
}
