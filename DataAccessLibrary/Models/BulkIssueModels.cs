using System;
using System.ComponentModel.DataAnnotations;

namespace DataAccessLibrary.Models;

/// <summary>
/// Represents a snapshot of bulk issue counts at a point in time.
/// Used for trend analysis and proactive detection of new issues.
/// </summary>
public class BulkIssueSnapshot
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>
    /// The bulk issue type ID (e.g., "missing-manager", "empty-groups")
    /// </summary>
    [Required]
    [MaxLength(100)]
    public string IssueId { get; set; } = string.Empty;

    /// <summary>
    /// Human-readable issue title
    /// </summary>
    [MaxLength(200)]
    public string? IssueTitle { get; set; }

    /// <summary>
    /// Issue category (People, Groups, Accounts)
    /// </summary>
    [MaxLength(50)]
    public string? Category { get; set; }

    /// <summary>
    /// Number of affected items at snapshot time
    /// </summary>
    public int AffectedCount { get; set; }

    /// <summary>
    /// Number of items with available auto-fix suggestions
    /// </summary>
    public int FixableCount { get; set; }

    /// <summary>
    /// Change from previous snapshot (positive = increase, negative = decrease)
    /// </summary>
    public int ChangeFromPrevious { get; set; }

    /// <summary>
    /// Percentage change from previous snapshot
    /// </summary>
    public double ChangePercentage { get; set; }

    /// <summary>
    /// When this snapshot was taken
    /// </summary>
    public DateTime SnapshotDate { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Type of snapshot: Daily, Weekly, OnDemand
    /// </summary>
    [MaxLength(20)]
    public string SnapshotType { get; set; } = "Daily";

    /// <summary>
    /// Whether a notification was sent for this snapshot
    /// </summary>
    public bool NotificationSent { get; set; }

    /// <summary>
    /// Additional metadata as JSON (e.g., department breakdown)
    /// </summary>
    public string? Metadata { get; set; }
}

/// <summary>
/// Aggregated summary of bulk issue changes for notifications
/// </summary>
public class BulkIssueSummary
{
    /// <summary>
    /// Total issues detected across all categories
    /// </summary>
    public int TotalIssueTypes { get; set; }

    /// <summary>
    /// Total affected items across all issues
    /// </summary>
    public int TotalAffectedItems { get; set; }

    /// <summary>
    /// Number of issue types that increased
    /// </summary>
    public int IssuesIncreased { get; set; }

    /// <summary>
    /// Number of issue types that decreased
    /// </summary>
    public int IssuesDecreased { get; set; }

    /// <summary>
    /// Number of new issue types detected
    /// </summary>
    public int NewIssues { get; set; }

    /// <summary>
    /// Number of issue types that were resolved (count went to 0)
    /// </summary>
    public int ResolvedIssues { get; set; }

    /// <summary>
    /// Net change in total affected items
    /// </summary>
    public int NetChange { get; set; }

    /// <summary>
    /// Most significant changes (top 5 by absolute change)
    /// </summary>
    public List<BulkIssueChange> SignificantChanges { get; set; } = new();

    /// <summary>
    /// Timestamp of this summary
    /// </summary>
    public DateTime GeneratedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Period start date
    /// </summary>
    public DateTime PeriodStart { get; set; }

    /// <summary>
    /// Period end date
    /// </summary>
    public DateTime PeriodEnd { get; set; }
}

/// <summary>
/// Represents a significant change in a bulk issue
/// </summary>
public class BulkIssueChange
{
    public string IssueId { get; set; } = string.Empty;
    public string IssueTitle { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public int PreviousCount { get; set; }
    public int CurrentCount { get; set; }
    public int Change { get; set; }
    public double ChangePercentage { get; set; }
    public ChangeType ChangeType { get; set; }
}

/// <summary>
/// Type of change detected
/// </summary>
public enum ChangeType
{
    /// <summary>New issue detected (previously 0)</summary>
    New,

    /// <summary>Issue count increased</summary>
    Increased,

    /// <summary>Issue count decreased</summary>
    Decreased,

    /// <summary>Issue resolved (count went to 0)</summary>
    Resolved,

    /// <summary>No change</summary>
    NoChange
}

/// <summary>
/// Configuration for bulk issue monitoring
/// </summary>
public class BulkIssueMonitorSettings
{
    /// <summary>
    /// Cron expression for when to run the monitor (default: daily at 6 AM)
    /// </summary>
    public string CronSchedule { get; set; } = "0 0 6 * * ?";

    /// <summary>
    /// Minimum change count to trigger notification
    /// </summary>
    public int MinChangeThreshold { get; set; } = 5;

    /// <summary>
    /// Minimum percentage change to trigger notification
    /// </summary>
    public double MinChangePercentage { get; set; } = 10.0;

    /// <summary>
    /// Whether to send notifications for new issues
    /// </summary>
    public bool NotifyOnNewIssues { get; set; } = true;

    /// <summary>
    /// Whether to send notifications for resolved issues
    /// </summary>
    public bool NotifyOnResolvedIssues { get; set; } = true;

    /// <summary>
    /// Whether to include proactive suggestions in chat
    /// </summary>
    public bool EnableProactiveSuggestions { get; set; } = true;

    /// <summary>
    /// Days to retain snapshot history
    /// </summary>
    public int SnapshotRetentionDays { get; set; } = 90;
}

// ============================================================================
// PHASE 7: ROLLBACK SUPPORT - Session-based change tracking
// ============================================================================

/// <summary>
/// Represents a bulk operation session for rollback support.
/// Tracks all changes made during a bulk fix operation.
/// </summary>
public class BulkOperationSession
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>
    /// The bulk issue type that was fixed (e.g., "missing-manager")
    /// </summary>
    [Required]
    [MaxLength(100)]
    public string IssueId { get; set; } = string.Empty;

    /// <summary>
    /// Human-readable issue title
    /// </summary>
    [MaxLength(200)]
    public string IssueTitle { get; set; } = string.Empty;

    /// <summary>
    /// User who initiated the bulk operation
    /// </summary>
    [Required]
    [MaxLength(256)]
    public string UserId { get; set; } = string.Empty;

    /// <summary>
    /// User's display name for audit purposes
    /// </summary>
    [MaxLength(256)]
    public string? UserDisplayName { get; set; }

    /// <summary>
    /// When the operation was executed
    /// </summary>
    public DateTime ExecutedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Total number of items attempted
    /// </summary>
    public int ItemCount { get; set; }

    /// <summary>
    /// Number of successful changes
    /// </summary>
    public int SuccessCount { get; set; }

    /// <summary>
    /// Number of failed changes
    /// </summary>
    public int FailedCount { get; set; }

    /// <summary>
    /// Status of the operation: Completed, PartiallyRolledBack, FullyRolledBack, Failed
    /// </summary>
    [MaxLength(50)]
    public string Status { get; set; } = "Completed";

    /// <summary>
    /// Department filter applied (if any)
    /// </summary>
    [MaxLength(100)]
    public string? DepartmentFilter { get; set; }

    /// <summary>
    /// OU filter applied (if any)
    /// </summary>
    [MaxLength(500)]
    public string? OuFilter { get; set; }

    /// <summary>
    /// When the session was last modified (e.g., during rollback)
    /// </summary>
    public DateTime? LastModifiedAt { get; set; }

    /// <summary>
    /// User who performed the rollback (if any)
    /// </summary>
    [MaxLength(256)]
    public string? RolledBackBy { get; set; }

    /// <summary>
    /// When the rollback was performed (if any)
    /// </summary>
    public DateTime? RolledBackAt { get; set; }

    /// <summary>
    /// Notes or comments about the operation
    /// </summary>
    public string? Notes { get; set; }

    /// <summary>
    /// Individual changes made in this session (loaded separately)
    /// </summary>
    public List<BulkOperationChange> Changes { get; set; } = new();
}

/// <summary>
/// Represents a single change made during a bulk operation.
/// Stores before/after values for rollback capability.
/// </summary>
public class BulkOperationChange
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>
    /// Reference to the parent session
    /// </summary>
    public Guid SessionId { get; set; }

    /// <summary>
    /// The entity ID that was modified (IdentityObject, Identity, or Group)
    /// </summary>
    public Guid EntityId { get; set; }

    /// <summary>
    /// Type of entity: IdentityObject, Identity, Group
    /// </summary>
    [Required]
    [MaxLength(50)]
    public string EntityType { get; set; } = string.Empty;

    /// <summary>
    /// Display name of the entity for UI purposes
    /// </summary>
    [MaxLength(256)]
    public string? EntityName { get; set; }

    /// <summary>
    /// The property that was changed (e.g., "ManagerIdentityId", "IsEnabled")
    /// </summary>
    [Required]
    [MaxLength(100)]
    public string PropertyName { get; set; } = string.Empty;

    /// <summary>
    /// The old value before the change (stored as string, parsed based on property type)
    /// </summary>
    public string? OldValue { get; set; }

    /// <summary>
    /// The new value after the change
    /// </summary>
    public string? NewValue { get; set; }

    /// <summary>
    /// Whether this change has been rolled back
    /// </summary>
    public bool IsRolledBack { get; set; }

    /// <summary>
    /// When the rollback occurred (if any)
    /// </summary>
    public DateTime? RolledBackAt { get; set; }

    /// <summary>
    /// Error message if rollback failed
    /// </summary>
    public string? RollbackError { get; set; }

    /// <summary>
    /// Additional context as JSON (e.g., related entity info)
    /// </summary>
    public string? Metadata { get; set; }
}

/// <summary>
/// Session status enumeration for type safety
/// </summary>
public static class BulkOperationStatus
{
    public const string Completed = "Completed";
    public const string PartiallyRolledBack = "PartiallyRolledBack";
    public const string FullyRolledBack = "FullyRolledBack";
    public const string Failed = "Failed";
    public const string InProgress = "InProgress";
}

/// <summary>
/// Summary of a bulk operation for history display
/// </summary>
public class BulkOperationHistoryItem
{
    public Guid SessionId { get; set; }
    public string IssueId { get; set; } = string.Empty;
    public string IssueTitle { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public string? UserDisplayName { get; set; }
    public DateTime ExecutedAt { get; set; }
    public int ItemCount { get; set; }
    public int SuccessCount { get; set; }
    public int FailedCount { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? DepartmentFilter { get; set; }
    public bool CanRollback { get; set; }
    public string TimeAgo { get; set; } = string.Empty;
}

/// <summary>
/// Result of a rollback operation
/// </summary>
public class RollbackResult
{
    public Guid SessionId { get; set; }
    public int TotalChanges { get; set; }
    public int RolledBack { get; set; }
    public int AlreadyRolledBack { get; set; }
    public int Failed { get; set; }
    public List<string> Errors { get; set; } = new();
    public string Status { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
}
