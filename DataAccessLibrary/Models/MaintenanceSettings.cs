using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace DataAccessLibrary.Models
{
    /// <summary>
    /// Configuration for automated maintenance jobs including log cleanup,
    /// database optimization, and data retention policies.
    /// </summary>
    public class MaintenanceSettings
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.None)] // Singleton - no identity column
        public int Id { get; set; } = 1; // Singleton record

        // ==========================================
        // LOG RETENTION SETTINGS (in days, 0 = keep forever)
        // ==========================================

        /// <summary>
        /// How long to keep sync audit logs (SyncAuditLog table)
        /// </summary>
        public int SyncLogRetentionDays { get; set; } = 30;

        /// <summary>
        /// How long to keep change audit logs (ChangeAuditLog table)
        /// </summary>
        public int ChangeLogRetentionDays { get; set; } = 365;

        /// <summary>
        /// Retention mode for change audit logs (ByDays, ByRecordCount, or BySize)
        /// </summary>
        public AuditLogRetentionMode ChangeLogRetentionMode { get; set; } = AuditLogRetentionMode.ByDays;

        /// <summary>
        /// Maximum number of change audit log records to keep (when using ByRecordCount mode)
        /// </summary>
        public int? ChangeLogMaxRecordCount { get; set; }

        /// <summary>
        /// Maximum size in MB for change audit logs table (when using BySize mode)
        /// </summary>
        public int? ChangeLogMaxSizeMB { get; set; }

        /// <summary>
        /// How long to keep system audit logs (AuditLog table)
        /// </summary>
        public int SystemLogRetentionDays { get; set; } = 90;

        /// <summary>
        /// How long to keep job execution history (JobExecutionHistory table)
        /// </summary>
        public int JobHistoryRetentionDays { get; set; } = 30;

        /// <summary>
        /// How long to keep notification logs
        /// </summary>
        public int NotificationLogRetentionDays { get; set; } = 60;

        // ==========================================
        // DATABASE MAINTENANCE SETTINGS
        // ==========================================

        /// <summary>
        /// Enable automatic index maintenance
        /// </summary>
        public bool EnableIndexMaintenance { get; set; } = true;

        /// <summary>
        /// Fragmentation percentage threshold to trigger index REORGANIZE (default 10%)
        /// </summary>
        public int IndexReorganizeThreshold { get; set; } = 10;

        /// <summary>
        /// Fragmentation percentage threshold to trigger index REBUILD (default 30%)
        /// </summary>
        public int IndexRebuildThreshold { get; set; } = 30;

        /// <summary>
        /// Enable automatic statistics updates
        /// </summary>
        public bool EnableStatisticsUpdate { get; set; } = true;

        /// <summary>
        /// Minimum rows changed percentage to trigger statistics update
        /// </summary>
        public int StatisticsUpdateThreshold { get; set; } = 20;

        // ==========================================
        // SESSION & ORPHAN CLEANUP SETTINGS
        // ==========================================

        /// <summary>
        /// Enable cleanup of expired user sessions
        /// </summary>
        public bool EnableSessionCleanup { get; set; } = true;

        /// <summary>
        /// Days after which to delete expired sessions
        /// </summary>
        public int ExpiredSessionRetentionDays { get; set; } = 7;

        /// <summary>
        /// Enable cleanup of orphaned sync staging data
        /// </summary>
        public bool EnableOrphanedDataCleanup { get; set; } = true;

        /// <summary>
        /// Days after which to delete orphaned staging records
        /// </summary>
        public int OrphanedDataRetentionDays { get; set; } = 14;

        /// <summary>
        /// Enable cleanup of temporary export/import files
        /// </summary>
        public bool EnableTempFileCleanup { get; set; } = true;

        /// <summary>
        /// Days after which to delete temporary files
        /// </summary>
        public int TempFileRetentionDays { get; set; } = 7;

        // ==========================================
        // JOB SCHEDULES (Cron expressions)
        // ==========================================

        /// <summary>
        /// Schedule for log cleanup job (default: daily at 2 AM)
        /// </summary>
        public string LogCleanupSchedule { get; set; } = "0 0 2 * * ?";

        /// <summary>
        /// Schedule for database index maintenance (default: Sunday at 3 AM)
        /// </summary>
        public string IndexMaintenanceSchedule { get; set; } = "0 0 3 ? * SUN";

        /// <summary>
        /// Schedule for statistics update (default: daily at 3:30 AM)
        /// </summary>
        public string StatisticsUpdateSchedule { get; set; } = "0 30 3 * * ?";

        /// <summary>
        /// Schedule for session cleanup (default: every 6 hours)
        /// </summary>
        public string SessionCleanupSchedule { get; set; } = "0 0 */6 * * ?";

        /// <summary>
        /// Schedule for orphaned data cleanup (default: daily at 4 AM)
        /// </summary>
        public string OrphanedDataCleanupSchedule { get; set; } = "0 0 4 * * ?";

        // ==========================================
        // JOB ENABLED FLAGS
        // ==========================================

        /// <summary>
        /// Enable/disable the log cleanup job
        /// </summary>
        public bool LogCleanupEnabled { get; set; } = true;

        /// <summary>
        /// Enable/disable the index maintenance job
        /// </summary>
        public bool IndexMaintenanceEnabled { get; set; } = true;

        /// <summary>
        /// Enable/disable the statistics update job
        /// </summary>
        public bool StatisticsUpdateEnabled { get; set; } = true;

        /// <summary>
        /// Enable/disable the session cleanup job
        /// </summary>
        public bool SessionCleanupEnabled { get; set; } = true;

        /// <summary>
        /// Enable/disable the orphaned data cleanup job
        /// </summary>
        public bool OrphanedDataCleanupEnabled { get; set; } = true;

        // ==========================================
        // LAST RUN TRACKING
        // ==========================================

        public DateTime? LastLogCleanupRun { get; set; }
        public DateTime? LastIndexMaintenanceRun { get; set; }
        public DateTime? LastStatisticsUpdateRun { get; set; }
        public DateTime? LastSessionCleanupRun { get; set; }
        public DateTime? LastOrphanedDataCleanupRun { get; set; }

        // ==========================================
        // AUDIT COLUMNS
        // ==========================================

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? ModifiedAt { get; set; }
        public string ModifiedBy { get; set; } = string.Empty;
    }
}
