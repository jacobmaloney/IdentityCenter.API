using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace DataAccessLibrary.Models;

/// <summary>
/// Business roles that map to AD groups for automatic role assignment.
/// When a user logs in and is a member of the mapped AD group, they have this role.
/// Used for workflow approvers (CISO, Helpdesk, IT Admin, etc.)
/// </summary>
[Table("BusinessRoles")]
public class BusinessRole
{
    [Key]
    public Guid Id { get; set; }

    /// <summary>
    /// Role name used in workflows and policies (e.g., "CISO", "Helpdesk", "IT Administrator")
    /// </summary>
    [Required]
    [MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// User-friendly display name
    /// </summary>
    [MaxLength(200)]
    public string? DisplayName { get; set; }

    /// <summary>
    /// Description of the role's responsibilities
    /// </summary>
    [MaxLength(1000)]
    public string? Description { get; set; }

    /// <summary>
    /// Category for organizing roles (Executive, IT, Security, Compliance, Operations)
    /// </summary>
    [MaxLength(50)]
    public string? Category { get; set; }

    /// <summary>
    /// The AD group DN or name that grants this role
    /// When user is member of this group, they have this role
    /// </summary>
    [MaxLength(500)]
    public string? ADGroupDN { get; set; }

    /// <summary>
    /// The AD group's ObjectGuid (cached for faster lookups)
    /// </summary>
    public Guid? ADGroupObjectId { get; set; }

    /// <summary>
    /// Alternative: Link to an Objects table group record
    /// </summary>
    public Guid? LinkedGroupId { get; set; }

    /// <summary>
    /// Icon for UI display (Bootstrap Icons class)
    /// </summary>
    [MaxLength(50)]
    public string? Icon { get; set; }

    /// <summary>
    /// Color for UI display
    /// </summary>
    [MaxLength(20)]
    public string? Color { get; set; }

    /// <summary>
    /// Sort order in UI
    /// </summary>
    public int SortOrder { get; set; }

    /// <summary>
    /// Is this a system-defined role (cannot be deleted)
    /// </summary>
    public bool IsSystem { get; set; }

    /// <summary>
    /// Is this role active
    /// </summary>
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// Can this role approve access requests
    /// </summary>
    public bool CanApprove { get; set; } = true;

    /// <summary>
    /// Can this role be used as an escalation target
    /// </summary>
    public bool CanEscalate { get; set; } = true;

    /// <summary>
    /// Fallback email if no role holder found
    /// </summary>
    [MaxLength(200)]
    public string? FallbackEmail { get; set; }

    /// <summary>
    /// Enforcement mode: Monitor (report only), Measured (provision only), Hard (provision + deprovision)
    /// </summary>
    [MaxLength(20)]
    public string EnforcementMode { get; set; } = "Monitor";

    /// <summary>
    /// Whether this role has membership rules defined
    /// </summary>
    public bool HasMembershipRules { get; set; }

    /// <summary>
    /// When enforcement was last run for this role
    /// </summary>
    public DateTime? LastEnforcementAt { get; set; }

    // Audit fields
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string? CreatedBy { get; set; }
    public DateTime? ModifiedAt { get; set; }
    public string? ModifiedBy { get; set; }

    // Navigation property - links to IdentityObject (group synced from AD/Entra)
    [ForeignKey(nameof(LinkedGroupId))]
    public virtual IdentityObject? LinkedGroup { get; set; }
}

/// <summary>
/// Tracks which users currently hold which business roles (cached from AD group membership)
/// </summary>
[Table("BusinessRoleMembers")]
public class BusinessRoleMember
{
    [Key]
    public Guid Id { get; set; }

    public Guid BusinessRoleId { get; set; }

    /// <summary>
    /// The identity ID of the role holder
    /// </summary>
    public Guid IdentityId { get; set; }

    /// <summary>
    /// Cached display name for quick lookup
    /// </summary>
    [MaxLength(200)]
    public string? DisplayName { get; set; }

    /// <summary>
    /// Cached email for notifications
    /// </summary>
    [MaxLength(200)]
    public string? Email { get; set; }

    /// <summary>
    /// When the membership was last verified from AD
    /// </summary>
    public DateTime LastVerifiedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Is this a direct assignment (not from AD group)
    /// </summary>
    public bool IsDirectAssignment { get; set; }

    // Navigation properties
    [ForeignKey(nameof(BusinessRoleId))]
    public virtual BusinessRole? BusinessRole { get; set; }

    [ForeignKey(nameof(IdentityId))]
    public virtual Identity? Identity { get; set; }
}

/// <summary>
/// DTO for role list display
/// </summary>
public class BusinessRoleListItem
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? DisplayName { get; set; }
    public string? Description { get; set; }
    public string? Category { get; set; }
    public string? ADGroupDN { get; set; }
    public string? Icon { get; set; }
    public string? Color { get; set; }
    public bool IsSystem { get; set; }
    public bool IsActive { get; set; }
    public int MemberCount { get; set; }
    public int SortOrder { get; set; }
    public int EntitlementCount { get; set; }
    public int PolicyCount { get; set; }
    public int RuleCount { get; set; }
    public string EnforcementMode { get; set; } = "Monitor";
}

/// <summary>
/// DTO for role member display
/// </summary>
public class BusinessRoleMemberItem
{
    public Guid Id { get; set; }
    public Guid IdentityId { get; set; }
    public string? DisplayName { get; set; }
    public string? Email { get; set; }
    public string? Department { get; set; }
    public string? Title { get; set; }
    public bool IsDirectAssignment { get; set; }
    public DateTime LastVerifiedAt { get; set; }
    public bool IsActive { get; set; } = true;  // From linked Identity
}

/// <summary>
/// Categories for organizing business roles
/// </summary>
[Table("BusinessRoleCategories")]
public class BusinessRoleCategory
{
    [Key]
    public Guid Id { get; set; }

    /// <summary>
    /// Category name (e.g., "Executive", "Security", "IT")
    /// </summary>
    [Required]
    [MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Bootstrap icon class for the category
    /// </summary>
    [MaxLength(50)]
    public string? Icon { get; set; }

    /// <summary>
    /// Gradient start color (hex)
    /// </summary>
    [MaxLength(20)]
    public string? ColorStart { get; set; }

    /// <summary>
    /// Gradient end color (hex)
    /// </summary>
    [MaxLength(20)]
    public string? ColorEnd { get; set; }

    /// <summary>
    /// Sort order in UI
    /// </summary>
    public int SortOrder { get; set; }

    /// <summary>
    /// Is this a system category (cannot be deleted)
    /// </summary>
    public bool IsSystem { get; set; }

    // Audit fields
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string? CreatedBy { get; set; }
}

// =============================================
// Entitlement Management Entities
// =============================================

/// <summary>
/// AD groups (or other entitlements) that a business role provisions its members into.
/// </summary>
[Table("BusinessRoleEntitlements")]
public class BusinessRoleEntitlement
{
    [Key]
    public Guid Id { get; set; }
    public Guid BusinessRoleId { get; set; }

    [MaxLength(50)]
    public string EntitlementType { get; set; } = "ADGroup";

    public Guid TargetObjectId { get; set; }

    [MaxLength(1000)]
    public string? TargetDN { get; set; }

    [MaxLength(500)]
    public string? TargetDisplayName { get; set; }

    public bool IsAutoProvision { get; set; } = true;
    public bool IsAutoDeprovision { get; set; }
    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string? CreatedBy { get; set; }
    public DateTime? ModifiedAt { get; set; }
    public string? ModifiedBy { get; set; }

    [ForeignKey(nameof(BusinessRoleId))]
    public virtual BusinessRole? BusinessRole { get; set; }
}

/// <summary>
/// Links a business role to a compliance policy.
/// </summary>
[Table("BusinessRolePolicies")]
public class BusinessRolePolicy
{
    [Key]
    public Guid Id { get; set; }
    public Guid BusinessRoleId { get; set; }
    public Guid CompliancePolicyId { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string? CreatedBy { get; set; }

    [ForeignKey(nameof(BusinessRoleId))]
    public virtual BusinessRole? BusinessRole { get; set; }
}

/// <summary>
/// Attribute-based membership rule for auto-determining role holders.
/// Uses LogicalOperator/RuleGroupId/GroupOperator pattern (same as CompliancePolicyRule).
/// </summary>
[Table("BusinessRoleMembershipRules")]
public class BusinessRoleMembershipRule
{
    [Key]
    public Guid Id { get; set; }
    public Guid BusinessRoleId { get; set; }

    [Required]
    [MaxLength(100)]
    public string FieldName { get; set; } = "JobTitle";

    [MaxLength(50)]
    public string Operator { get; set; } = "Equals";

    [MaxLength(500)]
    public string? Value { get; set; }

    [MaxLength(10)]
    public string LogicalOperator { get; set; } = "AND";

    public int RuleGroupId { get; set; }

    [MaxLength(10)]
    public string GroupOperator { get; set; } = "AND";

    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string? CreatedBy { get; set; }
    public DateTime? ModifiedAt { get; set; }
    public string? ModifiedBy { get; set; }

    [ForeignKey(nameof(BusinessRoleId))]
    public virtual BusinessRole? BusinessRole { get; set; }
}

/// <summary>
/// Audit log entry for provisioning actions (no FKs for write performance).
/// </summary>
[Table("BusinessRoleProvisioningLog")]
public class BusinessRoleProvisioningLogEntry
{
    [Key]
    public Guid Id { get; set; }
    public Guid BusinessRoleId { get; set; }
    public Guid? EntitlementId { get; set; }
    public Guid? IdentityId { get; set; }
    public Guid? ObjectId { get; set; }

    [MaxLength(50)]
    public string Action { get; set; } = string.Empty;

    [MaxLength(1000)]
    public string? TargetDN { get; set; }

    [MaxLength(500)]
    public string? TargetDisplayName { get; set; }

    public bool Success { get; set; } = true;

    [MaxLength(2000)]
    public string? ErrorMessage { get; set; }

    [MaxLength(200)]
    public string? ExecutedBy { get; set; }

    public DateTime ExecutedAt { get; set; } = DateTime.UtcNow;
}

// =============================================
// Entitlement Management DTOs
// =============================================

public class BusinessRoleEntitlementItem
{
    public Guid Id { get; set; }
    public Guid BusinessRoleId { get; set; }
    public string EntitlementType { get; set; } = "ADGroup";
    public Guid TargetObjectId { get; set; }
    public string? TargetDN { get; set; }
    public string? TargetDisplayName { get; set; }
    public bool IsAutoProvision { get; set; }
    public bool IsAutoDeprovision { get; set; }
    public bool IsActive { get; set; }
}

public class BusinessRolePolicyItem
{
    public Guid Id { get; set; }
    public Guid BusinessRoleId { get; set; }
    public Guid CompliancePolicyId { get; set; }
    public string? PolicyName { get; set; }
    public string? PolicyCategory { get; set; }
    public string? PolicySeverity { get; set; }
    public bool IsActive { get; set; }
}

public class BusinessRoleMembershipRuleItem
{
    public Guid Id { get; set; }
    public Guid BusinessRoleId { get; set; }
    public string FieldName { get; set; } = "JobTitle";
    public string Operator { get; set; } = "Equals";
    public string? Value { get; set; }
    public string LogicalOperator { get; set; } = "AND";
    public int RuleGroupId { get; set; }
    public string GroupOperator { get; set; } = "AND";
    public int SortOrder { get; set; }
    public bool IsActive { get; set; }
}

public class MembershipRulePreviewResult
{
    public int MatchCount { get; set; }
    public int CurrentMemberCount { get; set; }
    public int ToAddCount { get; set; }
    public int ToRemoveCount { get; set; }
    public List<BusinessRoleMemberItem> SampleMatches { get; set; } = new();
}

public class EntitlementEnforcementResult
{
    public Guid RoleId { get; set; }
    public string RoleName { get; set; } = string.Empty;
    public int EntitlementsProcessed { get; set; }
    public int MembersProvisioned { get; set; }
    public int MembersDeprovisioned { get; set; }
    public int Errors { get; set; }
    public List<string> ErrorMessages { get; set; } = new();
}
