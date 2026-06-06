using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace DataAccessLibrary.Services
{
    /// <summary>
    /// Service for logging and retrieving audit trail of changes to directory objects
    /// </summary>
    public interface IAuditLogService
    {
        /// <summary>
        /// Log a change to a directory object
        /// </summary>
        Task LogChangeAsync(ChangeAuditEntry entry);

        /// <summary>
        /// Log multiple changes in a batch (for bulk operations)
        /// </summary>
        Task LogChangesAsync(IEnumerable<ChangeAuditEntry> entries);

        /// <summary>
        /// Get change history for a specific object
        /// </summary>
        Task<List<ChangeAuditEntry>> GetObjectHistoryAsync(Guid objectId, int limit = 50);

        /// <summary>
        /// Get change history for a specific person (includes all linked objects)
        /// </summary>
        Task<List<ChangeAuditEntry>> GetPersonHistoryAsync(Guid personId, int limit = 50);

        /// <summary>
        /// Get all changes made by a specific user
        /// </summary>
        Task<List<ChangeAuditEntry>> GetUserActivityAsync(string userId, int limit = 50);

        /// <summary>
        /// Get recent changes across all objects
        /// </summary>
        Task<List<ChangeAuditEntry>> GetRecentChangesAsync(int limit = 100);

        /// <summary>
        /// Search change history with filters
        /// </summary>
        Task<List<ChangeAuditEntry>> SearchHistoryAsync(ChangeAuditSearchCriteria criteria);
    }

    /// <summary>
    /// Represents a single change audit entry - Who, What, When, Why
    /// </summary>
    public class ChangeAuditEntry
    {
        public long Id { get; set; }

        // WHEN - Timestamp of the change
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;

        // WHO - Identity of the person making the change
        public string? UserId { get; set; }
        public string? UserDisplayName { get; set; }
        public string? UserEmail { get; set; }
        public string? IpAddress { get; set; }

        // WHAT - Details of the change
        public ChangeOperationType OperationType { get; set; }
        public string? EntityType { get; set; }  // Object, Person, Group, Identity
        public Guid? EntityId { get; set; }
        public string? EntityDisplayName { get; set; }
        public string? PropertyName { get; set; }  // Specific property changed (e.g., "displayName", "email")
        public string? OldValue { get; set; }
        public string? NewValue { get; set; }

        // For group membership changes
        public Guid? RelatedEntityId { get; set; }
        public string? RelatedEntityName { get; set; }

        // WHY - Reason for the change (optional, for approval workflows)
        public string? Reason { get; set; }
        public string? TicketNumber { get; set; }  // ServiceNow, Jira, etc.
        public Guid? ApprovedBy { get; set; }
        public string? ApproverName { get; set; }

        // Result
        public bool Success { get; set; } = true;
        public string? ErrorMessage { get; set; }

        // Correlation for related changes
        public Guid? CorrelationId { get; set; }
        public string? Source { get; set; }  // "ChatUI", "SyncEngine", "API", etc.

        // WHO-on-behalf — when a system/automated actor performs a write authorized
        // by a human reviewer, UserId stays "system" and these capture the human.
        public string? OnBehalfOfUserId { get; set; }
        public string? OnBehalfOfDisplayName { get; set; }
    }

    /// <summary>
    /// Types of operations that can be audited
    /// </summary>
    public enum ChangeOperationType
    {
        // Entity CRUD
        Create,
        Update,
        Delete,

        // Account status
        Enable,
        Disable,
        PasswordReset,
        Unlock,

        // Group membership
        AddToGroup,
        RemoveFromGroup,

        // Tagging
        AddTag,
        RemoveTag,

        // Identity linking
        LinkIdentity,
        UnlinkIdentity,

        // Sync operations
        Sync,
        SyncProjectCreated,
        SyncProjectUpdated,
        SyncProjectDeleted,
        SyncExecutionStarted,
        SyncExecutionCompleted,
        SyncExecutionFailed,

        // Scheduled job operations
        JobScheduled,
        JobUnscheduled,
        JobExecutionStarted,
        JobExecutionCompleted,
        JobExecutionFailed,
        JobExecutionCancelled,

        // Report operations
        ReportCreated,
        ReportUpdated,
        ReportDeleted,
        ReportExecuted,
        ReportScheduled,

        // Policy operations
        PolicyCreated,
        PolicyUpdated,
        PolicyDeleted,
        PolicyEvaluated,
        PolicyViolationDetected,
        PolicyViolationResolved,

        // Access review operations
        CampaignCreated,
        CampaignStarted,
        CampaignCompleted,
        CampaignCancelled,
        ReviewAssigned,
        ReviewDecisionMade,
        ReviewEscalated,
        ReviewReminderSent,

        // Configuration changes
        SettingChanged,
        ConnectionCreated,
        ConnectionUpdated,
        ConnectionDeleted,
        ConnectionTested,

        // User/Role management
        UserCreated,
        UserUpdated,
        UserDeleted,
        RoleAssigned,
        RoleRemoved,

        // Login/Security
        LoginSuccess,
        LoginFailed,
        LogoutSuccess,
        SessionExpired,

        // System maintenance
        MaintenanceStarted,
        MaintenanceCompleted,
        IndexRebuilt,
        DataCleanedUp,
        BackupCreated,
        BackupRestored
    }

    /// <summary>
    /// Search criteria for filtering audit history
    /// </summary>
    public class ChangeAuditSearchCriteria
    {
        public Guid? EntityId { get; set; }
        public string? EntityType { get; set; }
        public string? UserId { get; set; }
        public ChangeOperationType? OperationType { get; set; }
        public DateTime? FromDate { get; set; }
        public DateTime? ToDate { get; set; }
        public string? PropertyName { get; set; }
        public bool? SuccessOnly { get; set; }
        public string? Source { get; set; }
        public int Limit { get; set; } = 100;
        public int Offset { get; set; } = 0;
    }
}
