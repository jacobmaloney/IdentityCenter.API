using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace DataAccessLibrary.Models;

// ============================================================================
// EVENT-DRIVEN WORKFLOW ORCHESTRATION MODELS
// Design: Lieutenant Barclay with Crew Coordination
// Purpose: Enable policy violations, lifecycle events, manual actions, and
//          scheduled triggers to initiate workflows - NO SCRIPTING REQUIRED
// ============================================================================

#region Enumerations

/// <summary>
/// Types of events that can trigger workflows.
/// Comprehensive coverage of identity lifecycle and compliance scenarios.
/// </summary>
public enum WorkflowEventType
{
    // === Policy Events ===
    /// <summary>Compliance policy found a violation</summary>
    PolicyViolationDetected = 100,
    /// <summary>Violation was remediated</summary>
    PolicyViolationResolved = 101,
    /// <summary>Violation escalated due to timeout</summary>
    PolicyViolationEscalated = 102,

    // === Object Lifecycle Events ===
    /// <summary>New object synced from directory</summary>
    ObjectCreated = 200,
    /// <summary>Object attributes changed</summary>
    ObjectModified = 201,
    /// <summary>Object removed/soft-deleted</summary>
    ObjectDeleted = 202,
    /// <summary>Object moved to different OU</summary>
    ObjectMoved = 203,
    /// <summary>Account was enabled</summary>
    ObjectEnabled = 204,
    /// <summary>Account was disabled</summary>
    ObjectDisabled = 205,
    /// <summary>Password was changed</summary>
    ObjectPasswordChanged = 206,
    /// <summary>Password expiring soon</summary>
    ObjectPasswordExpiring = 207,

    // === Identity Lifecycle Events ===
    /// <summary>New identity (person) created</summary>
    IdentityCreated = 300,
    /// <summary>Identity attributes changed</summary>
    IdentityModified = 301,
    /// <summary>Two identities merged</summary>
    IdentityMerged = 302,
    /// <summary>Identity marked inactive</summary>
    IdentityDeactivated = 303,
    /// <summary>Identity fully terminated</summary>
    IdentityTerminated = 304,

    // === Group Events ===
    /// <summary>Member added to group</summary>
    GroupMemberAdded = 400,
    /// <summary>Member removed from group</summary>
    GroupMemberRemoved = 401,
    /// <summary>Group owner/manager changed</summary>
    GroupOwnerChanged = 402,
    /// <summary>New group created</summary>
    GroupCreated = 403,
    /// <summary>Group deleted</summary>
    GroupDeleted = 404,

    // === Access Review Events ===
    /// <summary>Review campaign started</summary>
    AccessReviewStarted = 500,
    /// <summary>Review campaign completed</summary>
    AccessReviewCompleted = 501,
    /// <summary>Individual item approved</summary>
    AccessReviewItemApproved = 502,
    /// <summary>Individual item denied</summary>
    AccessReviewItemDenied = 503,
    /// <summary>Item expired without decision</summary>
    AccessReviewItemExpired = 504,

    // === Workflow Events ===
    /// <summary>Workflow step approved</summary>
    WorkflowApproved = 600,
    /// <summary>Workflow step denied</summary>
    WorkflowDenied = 601,
    /// <summary>Workflow escalated</summary>
    WorkflowEscalated = 602,
    /// <summary>Workflow finished</summary>
    WorkflowCompleted = 603,
    /// <summary>Workflow timed out</summary>
    WorkflowTimedOut = 604,

    // === Scheduled Events ===
    /// <summary>Time-based trigger fired</summary>
    ScheduledTrigger = 700,

    // === Manual Events ===
    /// <summary>Manual button click</summary>
    ManualTrigger = 800,

    // === Sync Events ===
    /// <summary>Sync project finished</summary>
    SyncProjectCompleted = 900,
    /// <summary>Sync project failed</summary>
    SyncProjectFailed = 901,
    /// <summary>Individual step completed</summary>
    SyncStepCompleted = 902,
    /// <summary>Individual step failed</summary>
    SyncStepFailed = 903
}

/// <summary>
/// High-level trigger type categories for UI organization.
/// </summary>
public enum TriggerType
{
    /// <summary>Triggered when compliance policy detects violation</summary>
    PolicyViolation,
    /// <summary>Triggered on object create/modify/delete/move</summary>
    ObjectLifecycle,
    /// <summary>Triggered by manual "Remediate" button</summary>
    Manual,
    /// <summary>Time-based trigger using cron expression</summary>
    Scheduled,
    /// <summary>Triggered by access review events</summary>
    AccessReview,
    /// <summary>Triggered by workflow completion events</summary>
    WorkflowCompletion,
    /// <summary>Triggered by sync events</summary>
    SyncCompletion
}

/// <summary>
/// Types of conditions for trigger evaluation.
/// Visual builder will present these as dropdown options.
/// </summary>
public enum TriggerConditionType
{
    // === Object Attribute Conditions ===
    /// <summary>Check any object attribute</summary>
    ObjectAttribute,
    /// <summary>Filter by object class (User, Computer, etc.)</summary>
    ObjectClass,
    /// <summary>Filter by organizational unit</summary>
    ObjectOU,
    /// <summary>Filter by source directory connection</summary>
    ObjectConnection,
    /// <summary>Filter by assigned tags</summary>
    ObjectTags,

    // === Identity Conditions ===
    /// <summary>Check any identity attribute</summary>
    IdentityAttribute,
    /// <summary>Filter by department</summary>
    IdentityDepartment,
    /// <summary>Filter by job title</summary>
    IdentityJobTitle,
    /// <summary>Filter by manager</summary>
    IdentityManager,
    /// <summary>Filter by identity tags</summary>
    IdentityTags,

    // === Policy Conditions ===
    /// <summary>Filter by policy name</summary>
    PolicyName,
    /// <summary>Filter by policy category</summary>
    PolicyCategory,
    /// <summary>Filter by violation severity</summary>
    PolicySeverity,
    /// <summary>How long violation has been open</summary>
    ViolationAge,

    // === Group Conditions ===
    /// <summary>Filter by group name</summary>
    GroupName,
    /// <summary>Security, Distribution, etc.</summary>
    GroupType,
    /// <summary>Filter by sensitivity flag</summary>
    GroupSensitivity,
    /// <summary>Filter by compliance tags (SOX, HIPAA, etc.)</summary>
    GroupComplianceTags,

    // === Risk Conditions ===
    /// <summary>Numeric risk score threshold</summary>
    RiskScore,
    /// <summary>Risk level category</summary>
    RiskLevel,

    // === Change Conditions ===
    /// <summary>Which attribute changed</summary>
    ChangedAttribute,
    /// <summary>Previous value</summary>
    OldValue,
    /// <summary>New value</summary>
    NewValue,
    /// <summary>What caused the change</summary>
    ChangeSource,

    // === Time Conditions ===
    /// <summary>Only during certain hours</summary>
    TimeOfDay,
    /// <summary>Only on certain days</summary>
    DayOfWeek,
    /// <summary>During business hours only</summary>
    BusinessHours,

    // === Custom ===
    /// <summary>Advanced: Custom expression (for power users)</summary>
    CustomExpression
}

/// <summary>
/// Comparison operators for condition evaluation.
/// </summary>
public enum TriggerConditionOperator
{
    Equals,
    NotEquals,
    Contains,
    NotContains,
    StartsWith,
    EndsWith,
    GreaterThan,
    GreaterThanOrEqual,
    LessThan,
    LessThanOrEqual,
    In,
    NotIn,
    IsNull,
    IsNotNull,
    Between,
    Matches  // Regex
}

/// <summary>
/// Types of actions that can be executed by triggers.
/// NO SCRIPTING REQUIRED for these common scenarios!
/// </summary>
public enum TriggerActionType
{
    // === Notification Actions ===
    /// <summary>Send email notification</summary>
    SendEmail = 100,
    /// <summary>Send Teams notification</summary>
    SendTeamsMessage = 101,
    /// <summary>Send Slack notification (future)</summary>
    SendSlackMessage = 102,

    // === Workflow Actions ===
    /// <summary>Start approval workflow</summary>
    StartWorkflow = 200,
    /// <summary>Create access review campaign</summary>
    CreateAccessReview = 201,
    /// <summary>Add item to existing review</summary>
    CreateAccessReviewItem = 202,

    // === Directory Write-Back Actions ===
    /// <summary>Disable AD/Azure AD account</summary>
    DisableAccount = 300,
    /// <summary>Enable AD/Azure AD account</summary>
    EnableAccount = 301,
    /// <summary>Remove user from group</summary>
    RemoveFromGroup = 302,
    /// <summary>Add user to group</summary>
    AddToGroup = 303,
    /// <summary>Move object to different OU</summary>
    MoveToOU = 304,
    /// <summary>Set specific attribute value</summary>
    SetAttribute = 305,
    /// <summary>Force password reset</summary>
    ResetPassword = 306,

    // === M365 / Entra Offboarding Actions ===
    /// <summary>Revoke all active sign-in sessions</summary>
    RevokeActiveSessions = 307,
    /// <summary>Remove all M365 license assignments</summary>
    RemoveAllLicenses = 308,
    /// <summary>Set mail forwarding to manager or specified address</summary>
    SetMailForwarding = 309,
    /// <summary>Enable out-of-office auto-reply</summary>
    SetOutOfOffice = 310,
    /// <summary>Transfer ownership of all owned Teams to manager</summary>
    TransferTeamOwnership = 311,

    // === Internal Actions ===
    /// <summary>Update violation status</summary>
    UpdateViolationStatus = 400,
    /// <summary>Create audit entry</summary>
    CreateAuditLog = 401,
    /// <summary>Add/remove object tag</summary>
    SetObjectTag = 402,
    /// <summary>Add/remove identity tag</summary>
    SetIdentityTag = 403,
    /// <summary>Recalculate risk score</summary>
    UpdateRiskScore = 404,

    // === Escalation Actions ===
    /// <summary>Escalate to user's manager</summary>
    EscalateToManager = 500,
    /// <summary>Escalate to resource owner</summary>
    EscalateToOwner = 501,
    /// <summary>Escalate to specific role holder</summary>
    EscalateToRole = 502,

    // === Integration Actions ===
    /// <summary>Create ServiceNow incident (future)</summary>
    CreateServiceNowTicket = 600,
    /// <summary>Call external webhook</summary>
    CallWebhook = 601,
    /// <summary>Execute PowerShell script (admin only)</summary>
    ExecutePowerShell = 602,

    // === Wait/Delay Actions ===
    /// <summary>Wait for human approval</summary>
    WaitForApproval = 700,
    /// <summary>Wait specified time</summary>
    WaitForDuration = 701,
    /// <summary>Wait until condition met</summary>
    WaitForCondition = 702
}

/// <summary>
/// Status of trigger events in the queue.
/// </summary>
public enum TriggerEventStatus
{
    /// <summary>Event waiting to be processed</summary>
    Pending,
    /// <summary>Event currently being processed</summary>
    Processing,
    /// <summary>Event successfully processed</summary>
    Completed,
    /// <summary>Event processing failed</summary>
    Failed,
    /// <summary>Event was cancelled</summary>
    Cancelled,
    /// <summary>Event expired before processing</summary>
    Expired
}

/// <summary>
/// Status of trigger executions.
/// </summary>
public enum TriggerExecutionStatus
{
    /// <summary>Execution is running</summary>
    Running,
    /// <summary>Execution completed successfully</summary>
    Completed,
    /// <summary>Execution failed</summary>
    Failed,
    /// <summary>Execution was cancelled</summary>
    Cancelled,
    /// <summary>Execution timed out</summary>
    TimedOut,
    /// <summary>Execution waiting for approval</summary>
    WaitingForApproval
}

/// <summary>
/// Status of individual action logs.
/// </summary>
public enum TriggerActionLogStatus
{
    /// <summary>Action waiting to execute</summary>
    Pending,
    /// <summary>Action currently executing</summary>
    Running,
    /// <summary>Action completed successfully</summary>
    Completed,
    /// <summary>Action failed</summary>
    Failed,
    /// <summary>Action was skipped</summary>
    Skipped
}

#endregion

#region Core Models

/// <summary>
/// Main trigger configuration entity.
/// Defines when and how workflows should be triggered.
/// </summary>
public class WorkflowTrigger
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>
    /// Human-readable trigger name
    /// </summary>
    [Required]
    [MaxLength(200)]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Detailed description of what this trigger does
    /// </summary>
    [MaxLength(2000)]
    public string? Description { get; set; }

    /// <summary>
    /// Category for organization: Compliance, Lifecycle, Security, Custom
    /// </summary>
    [MaxLength(100)]
    public string? Category { get; set; }

    /// <summary>
    /// High-level trigger type determining which events activate this trigger
    /// </summary>
    [Required]
    [MaxLength(50)]
    public string TriggerType { get; set; } = Models.TriggerType.PolicyViolation.ToString();

    /// <summary>
    /// Specific event types this trigger responds to (JSON array of WorkflowEventType)
    /// </summary>
    public string? EventTypes { get; set; }

    /// <summary>
    /// Additional event source configuration (JSON)
    /// </summary>
    public string? EventSourceConfig { get; set; }

    /// <summary>
    /// Optional reference to approval workflow to start
    /// If null, uses inline actions only
    /// </summary>
    public Guid? WorkflowId { get; set; }

    /// <summary>
    /// Cron expression for scheduled triggers (Quartz format)
    /// </summary>
    [MaxLength(100)]
    public string? CronExpression { get; set; }

    /// <summary>
    /// Next scheduled execution time
    /// </summary>
    public DateTime? NextScheduledRun { get; set; }

    /// <summary>
    /// Last scheduled execution time
    /// </summary>
    public DateTime? LastScheduledRun { get; set; }

    /// <summary>
    /// Whether this trigger is active
    /// </summary>
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// Whether this is a built-in system trigger (cannot be deleted)
    /// </summary>
    public bool IsSystem { get; set; } = false;

    /// <summary>
    /// Priority level (lower number = higher priority)
    /// </summary>
    public int Priority { get; set; } = 100;

    /// <summary>
    /// Cooldown period in minutes to prevent trigger spam
    /// 0 = no cooldown
    /// </summary>
    public int CooldownMinutes { get; set; } = 0;

    /// <summary>
    /// Whether to run in test mode (log only, no actions)
    /// </summary>
    public bool TestMode { get; set; } = false;

    /// <summary>
    /// Total number of times this trigger has fired
    /// </summary>
    public int TriggerCount { get; set; } = 0;

    /// <summary>
    /// When this trigger last fired
    /// </summary>
    public DateTime? LastTriggeredAt { get; set; }

    /// <summary>
    /// Number of successful executions
    /// </summary>
    public int SuccessCount { get; set; } = 0;

    /// <summary>
    /// Number of failed executions
    /// </summary>
    public int FailureCount { get; set; } = 0;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [MaxLength(256)]
    public string? CreatedBy { get; set; }

    public DateTime? ModifiedAt { get; set; }

    [MaxLength(256)]
    public string? ModifiedBy { get; set; }

    // Navigation properties
    // Note: WorkflowId links to ApprovalWorkflow in AccessReview.Models, but we can't reference
    // that assembly from DataAccessLibrary due to dependency direction. Use ID-based lookup instead.

    public virtual ICollection<TriggerCondition> Conditions { get; set; } = new List<TriggerCondition>();
    public virtual ICollection<TriggerAction> Actions { get; set; } = new List<TriggerAction>();
    public virtual ICollection<TriggerExecution> Executions { get; set; } = new List<TriggerExecution>();
}

/// <summary>
/// Condition that must be met for trigger to activate.
/// Supports AND/OR grouping for complex logic.
/// </summary>
public class TriggerCondition
{
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required]
    public Guid TriggerId { get; set; }

    /// <summary>
    /// Type of condition (from TriggerConditionType enum)
    /// </summary>
    [Required]
    [MaxLength(100)]
    public string ConditionType { get; set; } = TriggerConditionType.PolicySeverity.ToString();

    /// <summary>
    /// Field name to evaluate (for attribute-based conditions)
    /// </summary>
    [MaxLength(200)]
    public string? FieldName { get; set; }

    /// <summary>
    /// Comparison operator
    /// </summary>
    [Required]
    [MaxLength(50)]
    public string Operator { get; set; } = TriggerConditionOperator.Equals.ToString();

    /// <summary>
    /// Value to compare against
    /// </summary>
    [MaxLength(2000)]
    public string? Value { get; set; }

    /// <summary>
    /// Data type of the value: String, Number, Boolean, DateTime, List
    /// </summary>
    [MaxLength(50)]
    public string ValueType { get; set; } = "String";

    /// <summary>
    /// Logical grouping for complex conditions
    /// Format: "Group1:AND" or "Group2:OR"
    /// </summary>
    [MaxLength(50)]
    public string LogicalGroup { get; set; } = "AND";

    /// <summary>
    /// Order within the logical group
    /// </summary>
    public int GroupOrder { get; set; } = 0;

    /// <summary>
    /// Whether this condition is active
    /// </summary>
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// Sort order for evaluation
    /// </summary>
    public int SortOrder { get; set; } = 0;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [ForeignKey(nameof(TriggerId))]
    public virtual WorkflowTrigger Trigger { get; set; } = null!;
}

/// <summary>
/// Action to execute when trigger fires.
/// Actions execute in order with retry and timeout support.
/// </summary>
public class TriggerAction
{
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required]
    public Guid TriggerId { get; set; }

    /// <summary>
    /// Type of action (from TriggerActionType enum)
    /// </summary>
    [Required]
    [MaxLength(100)]
    public string ActionType { get; set; } = TriggerActionType.SendEmail.ToString();

    /// <summary>
    /// Display name for this action instance
    /// </summary>
    [MaxLength(200)]
    public string? ActionName { get; set; }

    /// <summary>
    /// JSON configuration specific to this action type
    /// </summary>
    public string? ActionConfig { get; set; }

    /// <summary>
    /// Execution order (lower = earlier)
    /// </summary>
    public int ExecutionOrder { get; set; } = 0;

    /// <summary>
    /// Whether to run this action in background
    /// </summary>
    public bool IsAsync { get; set; } = false;

    /// <summary>
    /// Whether to continue with next action if this one fails
    /// </summary>
    public bool ContinueOnError { get; set; } = true;

    /// <summary>
    /// Delay before executing this action (minutes)
    /// </summary>
    public int DelayMinutes { get; set; } = 0;

    /// <summary>
    /// Maximum execution time (minutes)
    /// </summary>
    public int TimeoutMinutes { get; set; } = 60;

    /// <summary>
    /// Maximum retry attempts on failure
    /// </summary>
    public int MaxRetries { get; set; } = 3;

    /// <summary>
    /// Delay between retries (seconds)
    /// </summary>
    public int RetryDelaySeconds { get; set; } = 60;

    /// <summary>
    /// Whether this action is active
    /// </summary>
    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [ForeignKey(nameof(TriggerId))]
    public virtual WorkflowTrigger Trigger { get; set; } = null!;

    public virtual ICollection<TriggerActionLog> ActionLogs { get; set; } = new List<TriggerActionLog>();
}

/// <summary>
/// Durable event queue entry.
/// Events are persisted for at-least-once delivery guarantee.
/// </summary>
public class TriggerEvent
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>
    /// Type of event (from WorkflowEventType enum)
    /// </summary>
    [Required]
    [MaxLength(100)]
    public string EventType { get; set; } = string.Empty;

    /// <summary>
    /// Source that generated this event
    /// </summary>
    [Required]
    [MaxLength(200)]
    public string EventSource { get; set; } = string.Empty;

    /// <summary>
    /// Full event data as JSON
    /// </summary>
    [Required]
    public string EventData { get; set; } = "{}";

    /// <summary>
    /// Type of target entity
    /// </summary>
    [MaxLength(100)]
    public string? TargetEntityType { get; set; }

    /// <summary>
    /// ID of target entity
    /// </summary>
    public Guid? TargetEntityId { get; set; }

    /// <summary>
    /// Processing status
    /// </summary>
    [Required]
    [MaxLength(50)]
    public string Status { get; set; } = TriggerEventStatus.Pending.ToString();

    /// <summary>
    /// Number of processing attempts
    /// </summary>
    public int ProcessingAttempts { get; set; } = 0;

    /// <summary>
    /// Last processing attempt time
    /// </summary>
    public DateTime? LastAttemptAt { get; set; }

    /// <summary>
    /// When processing completed
    /// </summary>
    public DateTime? ProcessedAt { get; set; }

    /// <summary>
    /// Error message if processing failed
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// Idempotency key to prevent duplicate processing
    /// </summary>
    [MaxLength(500)]
    public string? IdempotencyKey { get; set; }

    /// <summary>
    /// When the event occurred
    /// </summary>
    public DateTime OccurredAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// When the event expires (if not processed)
    /// </summary>
    public DateTime? ExpiresAt { get; set; }

    /// <summary>
    /// Correlation ID for related events
    /// </summary>
    public Guid? CorrelationId { get; set; }

    /// <summary>
    /// ID of the event that caused this event
    /// </summary>
    public Guid? CausationId { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public virtual ICollection<TriggerExecution> Executions { get; set; } = new List<TriggerExecution>();
}

/// <summary>
/// Execution record for a trigger firing.
/// Tracks full history with action results.
/// </summary>
public class TriggerExecution
{
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required]
    public Guid TriggerId { get; set; }

    /// <summary>
    /// Event that caused this execution (null for manual/scheduled)
    /// </summary>
    public Guid? EventId { get; set; }

    /// <summary>
    /// Workflow instance started by this trigger (if applicable)
    /// </summary>
    public Guid? WorkflowInstanceId { get; set; }

    /// <summary>
    /// Type of target entity
    /// </summary>
    [MaxLength(100)]
    public string? TargetEntityType { get; set; }

    /// <summary>
    /// ID of target entity
    /// </summary>
    public Guid? TargetEntityId { get; set; }

    /// <summary>
    /// Execution status
    /// </summary>
    [Required]
    [MaxLength(50)]
    public string Status { get; set; } = TriggerExecutionStatus.Running.ToString();

    public DateTime StartedAt { get; set; } = DateTime.UtcNow;

    public DateTime? CompletedAt { get; set; }

    /// <summary>
    /// Duration in milliseconds
    /// </summary>
    public long? DurationMs { get; set; }

    /// <summary>
    /// Number of actions executed
    /// </summary>
    public int ActionsExecuted { get; set; } = 0;

    /// <summary>
    /// Number of actions that failed
    /// </summary>
    public int ActionsFailed { get; set; } = 0;

    /// <summary>
    /// Summary of what happened (JSON)
    /// </summary>
    public string? ResultSummary { get; set; }

    /// <summary>
    /// Error message if execution failed
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// Snapshot of event data at execution time
    /// </summary>
    public string? EventDataSnapshot { get; set; }

    /// <summary>
    /// Snapshot of trigger config at execution time
    /// </summary>
    public string? TriggerConfigSnapshot { get; set; }

    /// <summary>
    /// Who/what triggered this execution
    /// </summary>
    [MaxLength(256)]
    public string? TriggeredBy { get; set; }

    [ForeignKey(nameof(TriggerId))]
    public virtual WorkflowTrigger Trigger { get; set; } = null!;

    [ForeignKey(nameof(EventId))]
    public virtual TriggerEvent? Event { get; set; }

    public virtual ICollection<TriggerActionLog> ActionLogs { get; set; } = new List<TriggerActionLog>();
}

/// <summary>
/// Detailed log of individual action execution within a trigger.
/// </summary>
public class TriggerActionLog
{
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required]
    public Guid ExecutionId { get; set; }

    [Required]
    public Guid ActionId { get; set; }

    /// <summary>
    /// Type of action executed
    /// </summary>
    [Required]
    [MaxLength(100)]
    public string ActionType { get; set; } = string.Empty;

    /// <summary>
    /// Display name of the action
    /// </summary>
    [MaxLength(200)]
    public string? ActionName { get; set; }

    /// <summary>
    /// Execution status
    /// </summary>
    [Required]
    [MaxLength(50)]
    public string Status { get; set; } = TriggerActionLogStatus.Pending.ToString();

    public DateTime? StartedAt { get; set; }

    public DateTime? CompletedAt { get; set; }

    /// <summary>
    /// Duration in milliseconds
    /// </summary>
    public long? DurationMs { get; set; }

    /// <summary>
    /// Input data passed to action (JSON)
    /// </summary>
    public string? InputData { get; set; }

    /// <summary>
    /// Output/result from action (JSON)
    /// </summary>
    public string? OutputData { get; set; }

    /// <summary>
    /// Error message if action failed
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// Current retry attempt number
    /// </summary>
    public int AttemptNumber { get; set; } = 1;

    /// <summary>
    /// Whether this action will be retried
    /// </summary>
    public bool WillRetry { get; set; } = false;

    /// <summary>
    /// Next retry time (if WillRetry is true)
    /// </summary>
    public DateTime? NextRetryAt { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [ForeignKey(nameof(ExecutionId))]
    public virtual TriggerExecution Execution { get; set; } = null!;

    [ForeignKey(nameof(ActionId))]
    public virtual TriggerAction Action { get; set; } = null!;
}

#endregion

#region Template Models

/// <summary>
/// Built-in trigger template for easy configuration.
/// Users can create triggers from templates without understanding all options.
/// </summary>
public class WorkflowTriggerTemplate
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>
    /// Template name
    /// </summary>
    [Required]
    [MaxLength(200)]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Template description
    /// </summary>
    [MaxLength(2000)]
    public string? Description { get; set; }

    /// <summary>
    /// Category: Compliance, Security, Lifecycle, Custom
    /// </summary>
    [MaxLength(100)]
    public string? Category { get; set; }

    /// <summary>
    /// Icon class for UI display
    /// </summary>
    [MaxLength(100)]
    public string? Icon { get; set; }

    /// <summary>
    /// Color for UI display
    /// </summary>
    [MaxLength(50)]
    public string? Color { get; set; }

    /// <summary>
    /// Whether this is a built-in system template
    /// </summary>
    public bool IsSystem { get; set; } = false;

    /// <summary>
    /// Full trigger configuration as JSON
    /// </summary>
    [Required]
    public string TemplateJson { get; set; } = "{}";

    /// <summary>
    /// Number of times this template has been used
    /// </summary>
    public int UsageCount { get; set; } = 0;

    /// <summary>
    /// Sort order for display
    /// </summary>
    public int SortOrder { get; set; } = 0;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [MaxLength(256)]
    public string? CreatedBy { get; set; }

    public DateTime? ModifiedAt { get; set; }
}

#endregion

#region DTO Models

/// <summary>
/// Lightweight DTO for trigger list display
/// </summary>
public class WorkflowTriggerListItem
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? Category { get; set; }
    public string TriggerType { get; set; } = string.Empty;
    public string? EventTypes { get; set; }
    public bool IsActive { get; set; }
    public bool IsSystem { get; set; }
    public int Priority { get; set; }
    public int TriggerCount { get; set; }
    public DateTime? LastTriggeredAt { get; set; }
    public int SuccessCount { get; set; }
    public int FailureCount { get; set; }
    public int ConditionCount { get; set; }
    public int ActionCount { get; set; }
    public string? WorkflowName { get; set; }

    // Scheduled trigger fields
    public string? CronExpression { get; set; }
    public DateTime? NextScheduledRun { get; set; }
    public DateTime? LastScheduledRun { get; set; }
}

/// <summary>
/// Event data passed to trigger evaluation
/// </summary>
public class WorkflowEventData
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public WorkflowEventType EventType { get; set; }
    public string EventSource { get; set; } = string.Empty;
    public DateTime OccurredAt { get; set; } = DateTime.UtcNow;

    public string? TargetEntityType { get; set; }
    public Guid? TargetEntityId { get; set; }
    public string? TargetEntityName { get; set; }

    public Dictionary<string, object> Data { get; set; } = new();

    public Guid? CorrelationId { get; set; }
    public Guid? CausationId { get; set; }
    public string? TriggeredBy { get; set; }

    /// <summary>
    /// Helper to get typed data value
    /// </summary>
    public T? GetData<T>(string key)
    {
        if (Data.TryGetValue(key, out var value))
        {
            if (value is T typedValue) return typedValue;
            try
            {
                return (T)Convert.ChangeType(value, typeof(T));
            }
            catch
            {
                return default;
            }
        }
        return default;
    }
}

/// <summary>
/// Result of evaluating a trigger against an event
/// </summary>
public class TriggerEvaluationResult
{
    public Guid TriggerId { get; set; }
    public string TriggerName { get; set; } = string.Empty;
    public bool Matched { get; set; }
    public List<ConditionEvaluationResult> ConditionResults { get; set; } = new();
    public string? MatchReason { get; set; }
    public string? NoMatchReason { get; set; }
}

/// <summary>
/// Result of evaluating a single condition
/// </summary>
public class ConditionEvaluationResult
{
    public Guid ConditionId { get; set; }
    public string ConditionType { get; set; } = string.Empty;
    public bool Matched { get; set; }
    public string? ActualValue { get; set; }
    public string? ExpectedValue { get; set; }
    public string? EvaluationDetails { get; set; }
}

/// <summary>
/// Result of executing a trigger
/// </summary>
public class TriggerExecutionResult
{
    public Guid ExecutionId { get; set; }
    public Guid TriggerId { get; set; }
    public string TriggerName { get; set; } = string.Empty;
    public bool Success { get; set; }
    public string Status { get; set; } = string.Empty;

    public int ActionsExecuted { get; set; }
    public int ActionsFailed { get; set; }
    public List<ActionExecutionResult> ActionResults { get; set; } = new();

    public Guid? WorkflowInstanceId { get; set; }
    public string? ErrorMessage { get; set; }
    public TimeSpan Duration { get; set; }
}

/// <summary>
/// Result of executing a single action
/// </summary>
public class ActionExecutionResult
{
    public Guid ActionId { get; set; }
    public string ActionType { get; set; } = string.Empty;
    public string? ActionName { get; set; }
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    public object? Result { get; set; }
    public TimeSpan Duration { get; set; }
}

#endregion
