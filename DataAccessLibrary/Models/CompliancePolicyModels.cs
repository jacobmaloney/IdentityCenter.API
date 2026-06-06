using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace DataAccessLibrary.Models;

/// <summary>
/// Compliance policy definition model for attestation and review
/// Defines rules and actions for identity governance and compliance frameworks (SOX, HIPAA, GDPR, etc.)
/// Separate from AccessPolicy which is for access request workflows
/// </summary>
public class CompliancePolicy
{
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required]
    [MaxLength(200)]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// User-friendly display name for the policy
    /// </summary>
    [MaxLength(500)]
    public string? DisplayName { get; set; }

    [MaxLength(1000)]
    public string? Description { get; set; }

    /// <summary>
    /// Policy category: Compliance, Risk, Lifecycle, Governance
    /// </summary>
    [Required]
    [MaxLength(50)]
    public string Category { get; set; } = "Compliance";

    /// <summary>
    /// Policy type for categorizing policy behavior:
    /// - Detection: Detects conditions and reports violations
    /// - Enforcement: Automatically enforces rules
    /// - Notification: Sends notifications based on conditions
    /// - Remediation: Triggers remediation actions
    /// </summary>
    [MaxLength(50)]
    public string PolicyType { get; set; } = "Detection";

    /// <summary>
    /// Target entity type this policy evaluates against:
    /// - Object: AD accounts, filtered by ObjectClass (user, computer, group, etc.)
    /// - Identity: People (consolidated view across all linked AD accounts)
    /// </summary>
    [MaxLength(50)]
    public string TargetEntityType { get; set; } = "Object";

    /// <summary>
    /// Severity level: 1=Critical, 2=High, 3=Medium, 4=Low, 5=Info
    /// </summary>
    public int Severity { get; set; } = 3;

    /// <summary>
    /// Priority for policy hierarchy (higher number = more severe, takes precedence)
    /// Used to prevent multiple policies from triggering for the same user
    /// Example: 365-day inactive (Priority 5) suppresses 45, 90, 120, 180-day policies
    /// </summary>
    public int Priority { get; set; } = 0;

    /// <summary>
    /// Whether this policy is currently active
    /// New policies default to false - must be explicitly enabled
    /// </summary>
    public bool IsActive { get; set; } = false;

    /// <summary>
    /// Whether this is a built-in system policy (cannot be deleted, protected from modification)
    /// </summary>
    public bool IsBuiltIn { get; set; } = false;

    /// <summary>
    /// Evaluation frequency in hours (e.g., 24 for daily, 168 for weekly)
    /// </summary>
    public int EvaluationFrequencyHours { get; set; } = 24;

    /// <summary>
    /// When this policy was last evaluated
    /// </summary>
    public DateTime? LastEvaluationDate { get; set; }

    /// <summary>
    /// When this policy should be evaluated next
    /// </summary>
    public DateTime? NextEvaluationDate { get; set; }

    /// <summary>
    /// Compliance framework this policy supports (SOX, HIPAA, GDPR, etc.)
    /// </summary>
    [MaxLength(100)]
    public string? ComplianceFramework { get; set; }

    /// <summary>
    /// Number of identities currently in scope for this policy
    /// </summary>
    public int CurrentScope { get; set; } = 0;

    /// <summary>
    /// Number of violations found in last evaluation
    /// </summary>
    public int LastViolationCount { get; set; } = 0;

    /// <summary>
    /// Number of actions executed in last evaluation
    /// </summary>
    public int LastActionCount { get; set; } = 0;

    /// <summary>
    /// Whether this policy is currently being executed
    /// </summary>
    public bool IsRunning { get; set; } = false;

    /// <summary>
    /// When this policy was last executed
    /// </summary>
    public DateTime? LastRunAt { get; set; }

    /// <summary>
    /// Total number of times this policy has been executed
    /// </summary>
    public int TotalExecutions { get; set; } = 0;

    // ============================================
    // ENFORCEMENT MODE CONFIGURATION
    // ============================================

    /// <summary>
    /// Enforcement mode determines how violations are processed:
    /// - DetectionOnly: Violations logged, no actions of any kind
    /// - Monitor: Notifications and reviews only, no account changes
    /// - Measured: All actions throttled to ProcessingLimitPerRun per execution
    /// - Hard: Full enforcement - all violations processed immediately, no limits
    /// </summary>
    [MaxLength(20)]
    public string EnforcementMode { get; set; } = "Monitor";

    /// <summary>
    /// Maximum number of actions to execute per policy run when in Measured mode.
    /// Resets at the start of each execution.
    /// </summary>
    public int? ProcessingLimitPerRun { get; set; } = 10;

    /// <summary>
    /// Number of actions executed so far in the current run.
    /// Reset at the start of each policy execution.
    /// </summary>
    public int ProcessedThisRun { get; set; } = 0;

    /// <summary>
    /// Number of actions executed today (resets daily).
    /// Used for daily rate limiting in Measured enforcement mode.
    /// </summary>
    public int DailyProcessedCount { get; set; } = 0;

    /// <summary>
    /// Timestamp when the current run started (used to track execution context).
    /// </summary>
    public DateTime? CurrentRunStartedAt { get; set; }

    // ============================================
    // NOTIFICATION REMINDER CONFIGURATION
    // ============================================

    /// <summary>
    /// Days to wait before sending the first notification (0 = immediate on detection)
    /// </summary>
    public int FirstReminderDelayDays { get; set; } = 0;

    /// <summary>
    /// Days between subsequent reminder notifications
    /// </summary>
    public int ReminderIntervalDays { get; set; } = 5;

    /// <summary>
    /// Maximum number of reminder notifications to send (null = unlimited)
    /// </summary>
    public int? MaxReminderCount { get; set; } = 3;

    /// <summary>
    /// Whether to suppress notifications for already-notified violations
    /// When true, only sends notifications according to reminder schedule
    /// When false, sends notification every time violation is detected
    /// </summary>
    public bool EnableReminderSchedule { get; set; } = true;

    // ============================================
    // SLA CONFIGURATION
    // ============================================

    /// <summary>
    /// SLA target hours for Critical severity violations (default: 4 hours)
    /// </summary>
    public int SlaCriticalHours { get; set; } = 4;

    /// <summary>
    /// SLA target hours for High severity violations (default: 24 hours)
    /// </summary>
    public int SlaHighHours { get; set; } = 24;

    /// <summary>
    /// SLA target hours for Medium severity violations (default: 72 hours)
    /// </summary>
    public int SlaMediumHours { get; set; } = 72;

    /// <summary>
    /// SLA target hours for Low severity violations (default: 168 hours / 7 days)
    /// </summary>
    public int SlaLowHours { get; set; } = 168;

    // ============================================
    // EMAIL TEMPLATE CONFIGURATION
    // ============================================

    /// <summary>
    /// Email template to use when assigning access reviews created by this policy.
    /// Inherited by standing campaigns created for this policy.
    /// </summary>
    public Guid? AssignmentEmailTemplateId { get; set; }

    /// <summary>
    /// Email template to use for review reminders on campaigns created by this policy.
    /// Inherited by standing campaigns created for this policy.
    /// </summary>
    public Guid? ReminderEmailTemplateId { get; set; }

    /// <summary>
    /// Scope: Directory connection IDs to apply this policy to (JSON array of GUIDs)
    /// If null or empty, applies to all connections
    /// </summary>
    [MaxLength(2000)]
    public string? ScopeConnectionIds { get; set; }

    /// <summary>
    /// Scope: Tags to filter objects (JSON array of strings)
    /// If null or empty, applies to all objects
    /// </summary>
    [MaxLength(2000)]
    public string? ScopeTags { get; set; }

    /// <summary>
    /// Scope: LDAP-style attribute query to filter objects
    /// Example: "(department=IT)" or "(&(department=Sales)(title=Manager))"
    /// If null or empty, no attribute filtering applied
    /// </summary>
    [MaxLength(4000)]
    public string? ScopeAttributeQuery { get; set; }

    /// <summary>
    /// Scope: Group IDs to filter objects by membership (JSON array of GUIDs)
    /// If null or empty, no group filtering applied
    /// </summary>
    [MaxLength(4000)]
    public string? ScopeGroupIds { get; set; }

    /// <summary>
    /// Scope inheritance behavior when policy is linked to a framework:
    /// - "Inherit": Use framework's scope exclusively
    /// - "Override": Ignore framework scope, use policy scope only
    /// - "Combine": Merge framework and policy scopes (intersection)
    /// </summary>
    [MaxLength(20)]
    public string ScopeInheritance { get; set; } = "Inherit";

    /// <summary>
    /// When true, automatically removes violations for entities that fall out of scope
    /// when the policy runs on schedule. Preserves remediated/acknowledged violations.
    /// </summary>
    public bool RemoveOutOfScopeViolations { get; set; } = false;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [MaxLength(256)]
    public string? CreatedBy { get; set; }

    public DateTime? ModifiedAt { get; set; }

    [MaxLength(256)]
    public string? ModifiedBy { get; set; }

    /// <summary>
    /// Policy rules collection
    /// </summary>
    public virtual ICollection<CompliancePolicyRule> Rules { get; set; } = new List<CompliancePolicyRule>();

    /// <summary>
    /// Policy actions collection
    /// </summary>
    public virtual ICollection<CompliancePolicyAction> Actions { get; set; } = new List<CompliancePolicyAction>();

    /// <summary>
    /// Policy violations collection
    /// </summary>
    public virtual ICollection<CompliancePolicyViolation> Violations { get; set; } = new List<CompliancePolicyViolation>();

    /// <summary>
    /// Framework mappings - links this policy to compliance frameworks
    /// Enables bidirectional navigation: Policy -> Frameworks and Framework -> Policies
    /// </summary>
    public virtual ICollection<ComplianceFrameworkPolicyMapping> FrameworkMappings { get; set; } = new List<ComplianceFrameworkPolicyMapping>();
}

/// <summary>
/// Individual rule within a compliance policy
/// Defines conditions that must be met for policy compliance
/// </summary>
public class CompliancePolicyRule
{
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required]
    public Guid CompliancePolicyId { get; set; }

    [Required]
    [MaxLength(200)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(1000)]
    public string? Description { get; set; }

    /// <summary>
    /// Rule type: LoginDormancy, RiskThreshold, PermissionCount, ManagerHierarchy, AccountStatus
    /// </summary>
    [Required]
    [MaxLength(50)]
    public string RuleType { get; set; } = string.Empty;

    /// <summary>
    /// Field name to evaluate (e.g., "LastSignInDate", "RiskScore", "PermissionCount")
    /// </summary>
    [Required]
    [MaxLength(100)]
    public string FieldName { get; set; } = string.Empty;

    /// <summary>
    /// Comparison operator: GreaterThan, LessThan, Equals, NotEquals, Contains, IsNull, Between
    /// </summary>
    [Required]
    [MaxLength(50)]
    public string Operator { get; set; } = "GreaterThan";

    /// <summary>
    /// Comparison value (e.g., "90" for 90 days, "0.8" for risk threshold)
    /// </summary>
    [MaxLength(500)]
    public string? ComparisonValue { get; set; }

    /// <summary>
    /// Days offset for relative date calculations (e.g., 90 for "90 days ago")
    /// </summary>
    public int? DaysOffset { get; set; }

    /// <summary>
    /// Rule weight for scoring (0.0 - 1.0)
    /// </summary>
    public decimal Weight { get; set; } = 1.0m;

    /// <summary>
    /// Sort order for rule evaluation
    /// </summary>
    public int SortOrder { get; set; } = 0;

    /// <summary>
    /// Whether this rule is active
    /// </summary>
    public bool IsActive { get; set; } = true;

    // ============================================
    // LOGICAL OPERATORS FOR RULE COMBINATION
    // ============================================

    /// <summary>
    /// Logical operator to combine with the NEXT rule in sequence: AND, OR
    /// Example: Rule1 AND Rule2 means BOTH must match
    /// Example: Rule1 OR Rule2 means EITHER can match
    /// The last rule's LogicalOperator is ignored (no next rule to combine with)
    /// </summary>
    [MaxLength(10)]
    public string LogicalOperator { get; set; } = "AND";

    /// <summary>
    /// Group ID for grouping rules together (rules with same GroupId are evaluated as a unit)
    /// Rules within a group are combined by their LogicalOperator
    /// Groups are then combined by the GroupOperator of the first rule in each group
    /// Null or 0 = ungrouped (evaluated individually)
    /// </summary>
    public int? RuleGroupId { get; set; }

    /// <summary>
    /// Display name for the rule group (only used on first rule of each group)
    /// </summary>
    [MaxLength(100)]
    public string? RuleGroupName { get; set; }

    /// <summary>
    /// Logical operator between this rule's GROUP and the next GROUP: AND, OR
    /// Only meaningful on the first rule of each group
    /// Example: Group1 OR Group2 means if EITHER group evaluates true, the policy triggers
    /// </summary>
    [MaxLength(10)]
    public string GroupOperator { get; set; } = "AND";

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [ForeignKey(nameof(CompliancePolicyId))]
    public virtual CompliancePolicy CompliancePolicy { get; set; } = null!;
}

/// <summary>
/// Automated action to execute when compliance policy is violated
/// </summary>
public class CompliancePolicyAction
{
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required]
    public Guid CompliancePolicyId { get; set; }

    [Required]
    [MaxLength(200)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(1000)]
    public string? Description { get; set; }

    /// <summary>
    /// Action type: CreateAccessReview, TriggerWorkflow, SendNotification, DisableAccount,
    /// RemovePermissions, EscalateToManager, CreateServiceTicket, LogViolation
    /// </summary>
    [Required]
    [MaxLength(50)]
    public string ActionType { get; set; } = "LogViolation";

    /// <summary>
    /// Execution timing: Immediate, AfterDelay, OnEscalation
    /// </summary>
    [MaxLength(50)]
    public string ExecutionTiming { get; set; } = "Immediate";

    /// <summary>
    /// Delay in minutes before execution (if ExecutionTiming = AfterDelay)
    /// </summary>
    public int? DelayMinutes { get; set; }

    /// <summary>
    /// Whether this action requires approval before execution
    /// </summary>
    public bool RequiresApproval { get; set; } = false;

    /// <summary>
    /// Maximum number of times this action can execute per entity
    /// </summary>
    public int? MaxExecutions { get; set; }

    /// <summary>
    /// Priority: 1=High, 2=Normal, 3=Low
    /// </summary>
    public int Priority { get; set; } = 2;

    /// <summary>
    /// JSON configuration for action-specific parameters
    /// </summary>
    public string? Configuration { get; set; }

    /// <summary>
    /// Whether this action is active
    /// </summary>
    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [ForeignKey(nameof(CompliancePolicyId))]
    public virtual CompliancePolicy CompliancePolicy { get; set; } = null!;
}

/// <summary>
/// Compliance framework definition (SOX, HIPAA, GDPR, ISO27001, PCI DSS, NIST, etc.)
/// </summary>
public class ComplianceFramework
{
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required]
    [MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    [Required]
    [MaxLength(50)]
    public string Code { get; set; } = string.Empty; // SOX, HIPAA, GDPR, ISO27001, etc.

    [MaxLength(2000)]
    public string? Description { get; set; }

    /// <summary>
    /// Framework category: Regulatory, Security, Privacy, Industry
    /// </summary>
    [Required]
    [MaxLength(50)]
    public string Category { get; set; } = "Regulatory";

    /// <summary>
    /// Issuing authority (e.g., "US Congress", "EU Commission", "ISO")
    /// </summary>
    [MaxLength(200)]
    public string? Authority { get; set; }

    /// <summary>
    /// Jurisdiction (e.g., "United States", "European Union", "Global")
    /// </summary>
    [MaxLength(100)]
    public string? Jurisdiction { get; set; }

    /// <summary>
    /// Industry focus (e.g., "Healthcare", "Financial Services", "All")
    /// </summary>
    [MaxLength(100)]
    public string? Industry { get; set; }

    /// <summary>
    /// Framework version (e.g., "2020", "1.1")
    /// </summary>
    [MaxLength(50)]
    public string? Version { get; set; }

    /// <summary>
    /// When this framework was published/enacted
    /// </summary>
    public DateTime? PublishedDate { get; set; }

    /// <summary>
    /// Whether this framework is currently active/applicable
    /// </summary>
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// Whether this is a built-in system framework (cannot be deleted, protected from modification)
    /// </summary>
    public bool IsBuiltIn { get; set; } = false;

    /// <summary>
    /// Overall compliance score (0-100)
    /// </summary>
    public decimal ComplianceScore { get; set; } = 0m;

    /// <summary>
    /// Total number of requirements in this framework
    /// </summary>
    public int TotalRequirements { get; set; } = 0;

    /// <summary>
    /// Number of controls/policies implemented
    /// </summary>
    public int ImplementedControls { get; set; } = 0;

    /// <summary>
    /// Last assessment date
    /// </summary>
    public DateTime? LastAssessmentDate { get; set; }

    /// <summary>
    /// Color for UI display (hex code, e.g., "#2196f3")
    /// </summary>
    [MaxLength(20)]
    public string Color { get; set; } = "#6b7280";

    /// <summary>
    /// Icon name for UI display
    /// </summary>
    [MaxLength(50)]
    public string? Icon { get; set; }

    // ============================================
    // SCOPE DEFINITION - Inherited by all policies
    // ============================================

    /// <summary>
    /// Scope: Directory connection IDs to apply this framework to (JSON array of GUIDs)
    /// All policies linked to this framework will inherit this scope
    /// If null or empty, applies to all connections
    /// </summary>
    [MaxLength(2000)]
    public string? ScopeConnectionIds { get; set; }

    /// <summary>
    /// Scope: Tags to filter objects (JSON array of GUIDs)
    /// All policies linked to this framework will inherit this scope
    /// If null or empty, applies to all objects
    /// </summary>
    [MaxLength(2000)]
    public string? ScopeTags { get; set; }

    /// <summary>
    /// Scope: Attribute query criteria (JSON array of criteria objects)
    /// Each criterion has: Field, Operator, Value
    /// All policies linked to this framework will inherit this scope
    /// If null or empty, no attribute filtering applied
    /// </summary>
    [MaxLength(4000)]
    public string? ScopeAttributeQuery { get; set; }

    /// <summary>
    /// Scope: Group IDs to filter objects by membership (JSON array of GUIDs)
    /// All policies linked to this framework will inherit this scope
    /// If null or empty, no group filtering applied
    /// </summary>
    [MaxLength(4000)]
    public string? ScopeGroupIds { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [MaxLength(256)]
    public string? CreatedBy { get; set; }

    public DateTime? ModifiedAt { get; set; }

    [MaxLength(256)]
    public string? ModifiedBy { get; set; }

    /// <summary>
    /// Policy mappings for this framework
    /// </summary>
    public virtual ICollection<ComplianceFrameworkPolicyMapping> PolicyMappings { get; set; } = new List<ComplianceFrameworkPolicyMapping>();
}

/// <summary>
/// Maps compliance policies to framework requirements
/// </summary>
public class ComplianceFrameworkPolicyMapping
{
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required]
    public Guid FrameworkId { get; set; }

    [Required]
    public Guid CompliancePolicyId { get; set; }

    /// <summary>
    /// Framework requirement identifier (e.g., "SOX-404", "HIPAA-164.308")
    /// </summary>
    [MaxLength(100)]
    public string? RequirementId { get; set; }

    /// <summary>
    /// Requirement description
    /// </summary>
    [MaxLength(2000)]
    public string? RequirementDescription { get; set; }

    /// <summary>
    /// Compliance status: Compliant, Partial, NonCompliant, NotAssessed
    /// </summary>
    [MaxLength(50)]
    public string ComplianceStatus { get; set; } = "NotAssessed";

    /// <summary>
    /// Coverage percentage (0-100)
    /// </summary>
    public decimal CoveragePercentage { get; set; } = 0m;

    /// <summary>
    /// Evidence of compliance
    /// </summary>
    public string? Evidence { get; set; }

    /// <summary>
    /// Last validation date
    /// </summary>
    public DateTime? LastValidated { get; set; }

    /// <summary>
    /// Gap description (if not fully compliant)
    /// </summary>
    public string? GapDescription { get; set; }

    /// <summary>
    /// Display order for policies within this framework
    /// </summary>
    public int SortOrder { get; set; } = 0;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [ForeignKey(nameof(FrameworkId))]
    public virtual ComplianceFramework Framework { get; set; } = null!;

    [ForeignKey(nameof(CompliancePolicyId))]
    public virtual CompliancePolicy CompliancePolicy { get; set; } = null!;
}

/// <summary>
/// Record of a compliance policy violation
/// </summary>
public class CompliancePolicyViolation
{
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required]
    public Guid CompliancePolicyId { get; set; }

    [Required]
    public Guid EntityId { get; set; } // Identity or Object ID

    /// <summary>
    /// Entity type: Identity or Object
    /// </summary>
    [MaxLength(50)]
    public string? EntityType { get; set; } = "Identity";

    /// <summary>
    /// Cached display name of the entity (for display when Entity navigation is null)
    /// </summary>
    [MaxLength(500)]
    public string? EntityDisplayName { get; set; }

    /// <summary>
    /// Violation severity: Critical, High, Medium, Low
    /// </summary>
    [MaxLength(50)]
    public string Severity { get; set; } = "Medium";

    /// <summary>
    /// Violation status: Open, Acknowledged, Remediated, Closed, Ignored
    /// </summary>
    [MaxLength(50)]
    public string Status { get; set; } = "Open";

    /// <summary>
    /// Violation score (0-100)
    /// </summary>
    public decimal ViolationScore { get; set; } = 50m;

    /// <summary>
    /// Detailed violation message
    /// </summary>
    public string? Message { get; set; }

    /// <summary>
    /// Violation description (for reports)
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Display name alias for EntityDisplayName (for report compatibility)
    /// </summary>
    [MaxLength(500)]
    public string? DisplayName { get; set; }

    /// <summary>
    /// When the violation was detected
    /// </summary>
    public DateTime DetectedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// When the violation was acknowledged
    /// </summary>
    public DateTime? AcknowledgedAt { get; set; }

    /// <summary>
    /// Who acknowledged the violation
    /// </summary>
    public Guid? AcknowledgedBy { get; set; }

    /// <summary>
    /// When the violation was remediated
    /// </summary>
    public DateTime? RemediatedAt { get; set; }

    /// <summary>
    /// Who remediated the violation
    /// </summary>
    public Guid? RemediatedBy { get; set; }

    /// <summary>
    /// Remediation notes
    /// </summary>
    public string? RemediationNotes { get; set; }

    /// <summary>
    /// When the violation was closed
    /// </summary>
    public DateTime? ClosedAt { get; set; }

    /// <summary>
    /// Whether automated actions were taken
    /// </summary>
    public bool ActionsExecuted { get; set; } = false;

    /// <summary>
    /// Number of actions executed
    /// </summary>
    public int ActionCount { get; set; } = 0;

    // ============================================
    // NOTIFICATION TRACKING
    // ============================================

    /// <summary>
    /// When the first notification was sent for this violation
    /// </summary>
    public DateTime? FirstNotificationSentAt { get; set; }

    /// <summary>
    /// When the last notification was sent for this violation
    /// </summary>
    public DateTime? LastNotificationSentAt { get; set; }

    /// <summary>
    /// Number of notifications sent for this violation
    /// </summary>
    public int NotificationCount { get; set; } = 0;

    /// <summary>
    /// When the next reminder notification should be sent (calculated from policy settings)
    /// </summary>
    public DateTime? NextReminderAt { get; set; }

    // Audit
    public DateTime? ModifiedAt { get; set; }

    [MaxLength(256)]
    public string? ModifiedBy { get; set; }

    [ForeignKey(nameof(CompliancePolicyId))]
    public virtual CompliancePolicy CompliancePolicy { get; set; } = null!;

    /// <summary>
    /// Navigation property for Identity entities (when EntityType="Identity")
    /// Note: This is nullable because EntityId can also point to Objects table
    /// Do not use .Include(v => v.Entity) - use EntityDisplayName instead
    /// </summary>
    public virtual Identity? Entity { get; set; }
}

/// <summary>
/// Result of a compliance policy evaluation (not stored in database, used for processing)
/// </summary>
public class CompliancePolicyEvaluationResult
{
    public Guid PolicyId { get; set; }
    public Guid EntityId { get; set; }
    public string Status { get; set; } = "Compliant";
    public bool HasViolations { get; set; }
    public decimal ViolationScore { get; set; }
    public string? Message { get; set; }
    public DateTime EvaluatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Represents a framework applied to a specific scope (connection, department, application).
/// When a framework is assigned, all its active policies execute against this scope.
/// This transforms frameworks from passive containers to active drivers of policy execution.
/// </summary>
public class FrameworkAssignment
{
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required]
    public Guid FrameworkId { get; set; }

    // ============================================
    // SCOPE DEFINITION - What the framework applies to
    // ============================================

    /// <summary>
    /// Specific connection to apply framework to (e.g., a single AD domain)
    /// </summary>
    public Guid? ConnectionId { get; set; }

    /// <summary>
    /// Department to apply framework to - all identities in this department
    /// </summary>
    public Guid? DepartmentId { get; set; }

    /// <summary>
    /// Application to apply framework to - all identities with access to this app
    /// </summary>
    public Guid? ApplicationId { get; set; }

    /// <summary>
    /// Custom scope expression for advanced filtering (e.g., "RiskLevel = 'High'", "Department LIKE 'Finance%'")
    /// Allows flexible targeting without predefined scope types
    /// </summary>
    public string? ScopeExpression { get; set; }

    /// <summary>
    /// Scope inheritance behavior when combining framework and policy scopes:
    /// - "Inherit": Use this assignment's scope, policies inherit it
    /// - "Override": Policies ignore this scope, use their own
    /// - "Combine": Intersection of assignment scope and policy scope
    /// </summary>
    [MaxLength(20)]
    public string ScopeInheritance { get; set; } = "Inherit";

    // ============================================
    // STATUS AND LIFECYCLE
    // ============================================

    /// <summary>
    /// Whether this framework assignment is currently active
    /// </summary>
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// When this assignment was activated (policies started executing)
    /// </summary>
    public DateTime? ActivatedAt { get; set; }

    /// <summary>
    /// When this assignment was deactivated (policies stopped executing)
    /// </summary>
    public DateTime? DeactivatedAt { get; set; }

    /// <summary>
    /// Reason for deactivation (for audit purposes)
    /// </summary>
    [MaxLength(1000)]
    public string? DeactivationReason { get; set; }

    // ============================================
    // COMPLIANCE TRACKING - Auto-calculated
    // ============================================

    /// <summary>
    /// Overall compliance score for this assignment (0-100)
    /// Calculated as: (PassingPolicies / TotalPolicies) * 100
    /// </summary>
    public decimal ComplianceScore { get; set; } = 0m;

    /// <summary>
    /// When compliance was last evaluated for this assignment
    /// </summary>
    public DateTime? LastEvaluatedAt { get; set; }

    /// <summary>
    /// Total number of active policies in the framework
    /// </summary>
    public int TotalPolicies { get; set; } = 0;

    /// <summary>
    /// Number of policies currently passing (no violations)
    /// </summary>
    public int PassingPolicies { get; set; } = 0;

    /// <summary>
    /// Number of policies currently failing (have violations)
    /// </summary>
    public int FailingPolicies { get; set; } = 0;

    /// <summary>
    /// Total violation count across all policies in this assignment
    /// </summary>
    public int TotalViolations { get; set; } = 0;

    /// <summary>
    /// Number of critical violations requiring immediate attention
    /// </summary>
    public int CriticalViolations { get; set; } = 0;

    // ============================================
    // AUDIT FIELDS
    // ============================================

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [MaxLength(256)]
    public string CreatedBy { get; set; } = string.Empty;

    public DateTime? ModifiedAt { get; set; }

    [MaxLength(256)]
    public string? ModifiedBy { get; set; }

    // ============================================
    // NAVIGATION PROPERTIES
    // ============================================

    [ForeignKey(nameof(FrameworkId))]
    public virtual ComplianceFramework Framework { get; set; } = null!;

    [ForeignKey(nameof(ConnectionId))]
    public virtual DirectoryConnection? Connection { get; set; }

    /// <summary>
    /// Policy-level overrides for this specific assignment
    /// Allows disabling individual policies or changing enforcement modes
    /// </summary>
    public virtual ICollection<FrameworkAssignmentPolicyOverride> PolicyOverrides { get; set; } = new List<FrameworkAssignmentPolicyOverride>();
}

/// <summary>
/// Allows overriding specific policy settings when a framework is assigned to a scope.
/// Example: HIPAA framework applied, but disable one specific policy for this connection.
/// Provides granular control without modifying the global policy definition.
/// </summary>
public class FrameworkAssignmentPolicyOverride
{
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required]
    public Guid AssignmentId { get; set; }

    [Required]
    public Guid PolicyId { get; set; }

    /// <summary>
    /// Override the enabled state (null = use policy's default IsActive)
    /// Set to false to disable this specific policy for this assignment
    /// </summary>
    public bool? IsEnabled { get; set; }

    /// <summary>
    /// Override enforcement mode (null = use policy's default)
    /// Values: "DetectionOnly", "Monitor", "Measured", "Hard"
    /// </summary>
    [MaxLength(20)]
    public string? EnforcementMode { get; set; }

    /// <summary>
    /// JSON configuration to override policy-specific parameters
    /// Example: {"ProcessingLimitPerRun": 50, "ReminderIntervalDays": 3}
    /// </summary>
    public string? CustomParameters { get; set; }

    /// <summary>
    /// Business justification for this override (required for audit compliance)
    /// </summary>
    [MaxLength(2000)]
    public string? Justification { get; set; }

    /// <summary>
    /// When this override expires (null = permanent)
    /// After expiration, policy reverts to default behavior
    /// </summary>
    public DateTime? ExpiresAt { get; set; }

    // ============================================
    // AUDIT FIELDS
    // ============================================

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [MaxLength(256)]
    public string CreatedBy { get; set; } = string.Empty;

    public DateTime? ModifiedAt { get; set; }

    [MaxLength(256)]
    public string? ModifiedBy { get; set; }

    // ============================================
    // NAVIGATION PROPERTIES
    // ============================================

    [ForeignKey(nameof(AssignmentId))]
    public virtual FrameworkAssignment Assignment { get; set; } = null!;

    [ForeignKey(nameof(PolicyId))]
    public virtual CompliancePolicy Policy { get; set; } = null!;
}

/// <summary>
/// Result of a framework assignment evaluation (not stored in database)
/// Used to return comprehensive results from ExecuteFrameworkAssignmentAsync
/// </summary>
public class FrameworkEvaluationResult
{
    public Guid AssignmentId { get; set; }
    public string FrameworkName { get; set; } = string.Empty;
    public decimal ComplianceScore { get; set; }
    public int TotalPolicies { get; set; }
    public int PassingPolicies { get; set; }
    public int FailingPolicies { get; set; }
    public int TotalViolations { get; set; }
    public int NewViolations { get; set; }
    public int ResolvedViolations { get; set; }
    public DateTime EvaluatedAt { get; set; } = DateTime.UtcNow;
    public List<PolicyEvaluationSummary> PolicyResults { get; set; } = new();
    public bool Success { get; set; } = true;
    public string? ErrorMessage { get; set; }

    public static FrameworkEvaluationResult NotFound(Guid assignmentId = default)
    {
        return new FrameworkEvaluationResult
        {
            AssignmentId = assignmentId,
            Success = false,
            ErrorMessage = "Framework assignment not found or inactive"
        };
    }
}

/// <summary>
/// Summary of a single policy evaluation within a framework
/// </summary>
public class PolicyEvaluationSummary
{
    public Guid PolicyId { get; set; }
    public string PolicyName { get; set; } = string.Empty;
    public bool IsCompliant { get; set; }
    public int ViolationCount { get; set; }
    public int NewViolations { get; set; }
    public int ResolvedViolations { get; set; }
    public string Severity { get; set; } = "Medium";
    public DateTime EvaluatedAt { get; set; }
}

/// <summary>
/// Preview of what will happen when applying a framework
/// Used by the Framework Setup Wizard to show impact before applying
/// </summary>
public class FrameworkApplicationPreview
{
    public Guid FrameworkId { get; set; }
    public string FrameworkName { get; set; } = string.Empty;
    public string FrameworkCode { get; set; } = string.Empty;
    public int TotalPolicies { get; set; }
    public int ActivePolicies { get; set; }
    public int AffectedIdentities { get; set; }
    public int AffectedConnections { get; set; }
    public List<PolicyPreviewItem> Policies { get; set; } = new();
    public List<string> Warnings { get; set; } = new();
    public List<string> Recommendations { get; set; } = new();
}

/// <summary>
/// Preview of a single policy within a framework application preview
/// </summary>
public class PolicyPreviewItem
{
    public Guid PolicyId { get; set; }
    public string PolicyName { get; set; } = string.Empty;
    public string PolicyType { get; set; } = string.Empty;
    public string EnforcementMode { get; set; } = "Monitor";
    public int Severity { get; set; }
    public bool IsActive { get; set; }
    public int EstimatedScope { get; set; }
    public string? RequirementId { get; set; }
}

/// <summary>
/// Summary of compliance status by framework for dashboard display
/// </summary>
public class FrameworkComplianceSummary
{
    public Guid FrameworkId { get; set; }
    public string FrameworkName { get; set; } = string.Empty;
    public string FrameworkCode { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string Color { get; set; } = "#6b7280";
    public int TotalAssignments { get; set; }
    public int ActiveAssignments { get; set; }
    public decimal AverageComplianceScore { get; set; }
    public int TotalViolations { get; set; }
    public int CriticalViolations { get; set; }
    public DateTime? LastEvaluatedAt { get; set; }
}

/// <summary>
/// Execution history for a compliance policy run
/// </summary>
public class CompliancePolicyExecution
{
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required]
    public Guid CompliancePolicyId { get; set; }

    /// <summary>
    /// Execution status: Running, Completed, Failed, Cancelled
    /// </summary>
    [Required]
    [MaxLength(50)]
    public string Status { get; set; } = "Running";

    /// <summary>
    /// When execution started
    /// </summary>
    public DateTime StartedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// When execution completed
    /// </summary>
    public DateTime? CompletedAt { get; set; }

    /// <summary>
    /// Execution duration in milliseconds
    /// </summary>
    public long? DurationMs { get; set; }

    /// <summary>
    /// Number of users evaluated
    /// </summary>
    public int UsersEvaluated { get; set; } = 0;

    /// <summary>
    /// Number of violations found
    /// </summary>
    public int ViolationsFound { get; set; } = 0;

    /// <summary>
    /// Number of actions executed
    /// </summary>
    public int ActionsExecuted { get; set; } = 0;

    /// <summary>
    /// Number of NEW violations found (didn't exist before)
    /// </summary>
    public int NewViolations { get; set; } = 0;

    /// <summary>
    /// Number of violations that already existed (skipped)
    /// </summary>
    public int SkippedViolations { get; set; } = 0;

    /// <summary>
    /// Number of violations that were resolved (no longer in violation)
    /// </summary>
    public int ResolvedViolations { get; set; } = 0;

    /// <summary>
    /// Error message if execution failed
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// Stack trace if execution failed
    /// </summary>
    public string? StackTrace { get; set; }

    /// <summary>
    /// How was this execution triggered: Manual, Scheduled, API
    /// </summary>
    [MaxLength(50)]
    public string TriggerType { get; set; } = "Manual";

    /// <summary>
    /// Who triggered this execution
    /// </summary>
    [MaxLength(256)]
    public string? TriggeredBy { get; set; }

    [ForeignKey(nameof(CompliancePolicyId))]
    public virtual CompliancePolicy CompliancePolicy { get; set; } = null!;
}
