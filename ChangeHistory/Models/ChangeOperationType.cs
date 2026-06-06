namespace ChangeHistory.Models;

/// <summary>
/// Types of operations that can be audited.
/// Int values match the existing ChangeAuditLogs table data.
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
