namespace DataAccessLibrary.Models;

/// <summary>
/// Models for Auto-Governance and Quarantine (Phase 5).
/// </summary>
public static class GovernanceModels
{
    /// <summary>
    /// A configurable governance policy that triggers automated actions.
    /// </summary>
    public class GovernancePolicy
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? Description { get; set; }
        public bool IsEnabled { get; set; }
        public int Priority { get; set; } = 100;
        public string? TriggerConditions { get; set; }
        public string ActionType { get; set; } = "Notify"; // Notify, Quarantine, Disable, FlagForReview
        public string? ActionConfig { get; set; }
        public bool RequiresApproval { get; set; } = true;
        public decimal ConfidenceThreshold { get; set; } = 80m;
        public int MaxActionsPerRun { get; set; } = 50;
        public int CooldownHours { get; set; } = 24;
        public bool ExcludeAdminAccounts { get; set; } = true;
        public DateTime CreatedAt { get; set; }
        public DateTime? ModifiedAt { get; set; }
        public string? CreatedBy { get; set; }
    }

    /// <summary>
    /// Result of evaluating an identity against governance policies.
    /// </summary>
    public class GovernanceEvaluation
    {
        public Guid IdentityId { get; set; }
        public Guid PolicyId { get; set; }
        public string PolicyName { get; set; } = string.Empty;
        public string ActionType { get; set; } = string.Empty;
        public decimal ConfidenceScore { get; set; }
        public string Reason { get; set; } = string.Empty;
        public bool RequiresApproval { get; set; }
    }

    /// <summary>
    /// Record of a quarantine action on an identity/object.
    /// </summary>
    public class QuarantineRecord
    {
        public Guid Id { get; set; }
        public Guid? IdentityId { get; set; }
        public Guid? ObjectId { get; set; }
        public Guid? GovernancePolicyId { get; set; }
        public string QuarantineType { get; set; } = "Soft"; // Soft or Hard
        public string? PreviousOU { get; set; }
        public string? QuarantineOU { get; set; }
        public bool? PreviousEnabled { get; set; }
        public string? RemovedGroupIds { get; set; }
        public string? Reason { get; set; }
        public DateTime QuarantinedAt { get; set; }
        public string? QuarantinedBy { get; set; }
        public DateTime? ExpiresAt { get; set; }
        public DateTime? ReleasedAt { get; set; }
        public string? ReleasedBy { get; set; }
        public string? ReleaseReason { get; set; }
        public bool IsActive { get; set; } = true;

        /// <summary>
        /// Display name of the identity (populated for display queries).
        /// </summary>
        public string? DisplayName { get; set; }
    }

    /// <summary>
    /// Trigger condition types for governance policies.
    /// </summary>
    public static class TriggerTypes
    {
        public const string IntegrityBelow = "IntegrityBelow";
        public const string RiskAbove = "RiskAbove";
        public const string InactiveDays = "InactiveDays";
        public const string ExcessiveGroups = "ExcessiveGroups";
        public const string DriftAbove = "DriftAbove";
        public const string ViolationCount = "ViolationCount";
    }

    /// <summary>
    /// Action types for governance policies.
    /// </summary>
    public static class ActionTypes
    {
        public const string Notify = "Notify";
        public const string Quarantine = "Quarantine";
        public const string Disable = "Disable";
        public const string FlagForReview = "FlagForReview";
    }
}
