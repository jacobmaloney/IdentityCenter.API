using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace DataAccessLibrary.Models;

/// <summary>
/// Represents an organizational folder/grouping for organizing people
/// Can be dynamic (query-based from AD) or static (manually created)
/// </summary>
public class OrganizationalFolder
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required]
    [MaxLength(200)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(1000)]
    public string? Description { get; set; }

    /// <summary>
    /// Parent folder for hierarchical structure
    /// </summary>
    public Guid? ParentId { get; set; }

    [ForeignKey(nameof(ParentId))]
    public virtual OrganizationalFolder? Parent { get; set; }

    /// <summary>
    /// Child folders
    /// </summary>
    public virtual ICollection<OrganizationalFolder> Children { get; set; } = new List<OrganizationalFolder>();

    /// <summary>
    /// Type of folder: Department, Division, Team, Manager, Custom
    /// </summary>
    [Required]
    [MaxLength(50)]
    public string FolderType { get; set; } = "Custom";

    /// <summary>
    /// For dynamic folders: JSON query definition to auto-populate members
    /// e.g., {"field": "Department", "operator": "equals", "value": "IT"}
    /// </summary>
    public string? QueryFilter { get; set; }

    /// <summary>
    /// Font Awesome or Bootstrap icon class
    /// </summary>
    [MaxLength(100)]
    public string? IconClass { get; set; }

    /// <summary>
    /// Display order within parent
    /// </summary>
    public int SortOrder { get; set; }

    /// <summary>
    /// True for system-generated folders (from AD sync)
    /// </summary>
    public bool IsSystem { get; set; }

    /// <summary>
    /// True if folder is active/visible
    /// </summary>
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// For manager-based folders, the manager's identity ID
    /// </summary>
    public Guid? ManagerIdentityId { get; set; }

    /// <summary>
    /// Cached member count for performance
    /// </summary>
    public int MemberCount { get; set; }

    /// <summary>
    /// When member count was last calculated
    /// </summary>
    public DateTime? MemberCountUpdatedAt { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [MaxLength(200)]
    public string? CreatedBy { get; set; }

    public DateTime? ModifiedAt { get; set; }

    [MaxLength(200)]
    public string? ModifiedBy { get; set; }

    /// <summary>
    /// Manual folder members (for static folders)
    /// </summary>
    public virtual ICollection<OrganizationalFolderMember> Members { get; set; } = new List<OrganizationalFolderMember>();

    /// <summary>
    /// Policies attached to this folder
    /// </summary>
    public virtual ICollection<OrganizationalFolderPolicy> Policies { get; set; } = new List<OrganizationalFolderPolicy>();
}

/// <summary>
/// Folder type constants
/// </summary>
public static class FolderTypes
{
    public const string Department = "Department";
    public const string Division = "Division";
    public const string Team = "Team";
    public const string Manager = "Manager";
    public const string Custom = "Custom";
    public const string Project = "Project";
    public const string FieldValue = "FieldValue";
}

/// <summary>
/// Manual membership assignment for static folders
/// </summary>
public class OrganizationalFolderMember
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required]
    public Guid FolderId { get; set; }

    [ForeignKey(nameof(FolderId))]
    public virtual OrganizationalFolder Folder { get; set; } = null!;

    /// <summary>
    /// The identity (person) assigned to this folder
    /// </summary>
    [Required]
    public Guid IdentityId { get; set; }

    [ForeignKey(nameof(IdentityId))]
    public virtual Identity Identity { get; set; } = null!;

    public DateTime AddedAt { get; set; } = DateTime.UtcNow;

    [MaxLength(200)]
    public string? AddedBy { get; set; }

    [MaxLength(500)]
    public string? Notes { get; set; }

    /// <summary>
    /// For temporary assignments
    /// </summary>
    public DateTime? ExpiresAt { get; set; }

    public bool IsActive { get; set; } = true;
}

/// <summary>
/// Policy attachment to organizational folders
/// </summary>
public class OrganizationalFolderPolicy
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required]
    public Guid FolderId { get; set; }

    [ForeignKey(nameof(FolderId))]
    public virtual OrganizationalFolder Folder { get; set; } = null!;

    [Required]
    public Guid PolicyId { get; set; }

    [ForeignKey(nameof(PolicyId))]
    public virtual CompliancePolicy Policy { get; set; } = null!;

    /// <summary>
    /// Whether this policy is inherited by child folders
    /// </summary>
    public bool InheritToChildren { get; set; } = true;

    public DateTime AppliedAt { get; set; } = DateTime.UtcNow;

    [MaxLength(200)]
    public string? AppliedBy { get; set; }

    public bool IsActive { get; set; } = true;
}

/// <summary>
/// DTO for displaying organizational structure
/// </summary>
public class OrgNodeDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string FolderType { get; set; } = string.Empty;
    public string? IconClass { get; set; }
    public int MemberCount { get; set; }
    public int ChildCount { get; set; }
    public Guid? ParentId { get; set; }
    public bool IsSystem { get; set; }
    public bool IsExpanded { get; set; }
    public bool HasChildren { get; set; }
    public List<OrgNodeDto> Children { get; set; } = new();

    // For manager hierarchy view
    public string? ManagerName { get; set; }
    public string? ManagerTitle { get; set; }
    public string? ManagerEmail { get; set; }
    public Guid? ManagerIdentityId { get; set; }
}

/// <summary>
/// Statistics for organizational overview
/// </summary>
public class OrganizationStats
{
    public int TotalDepartments { get; set; }
    public int TotalDivisions { get; set; }
    public int TotalUsers { get; set; }
    public int TotalManagers { get; set; }
    public int UsersWithManager { get; set; }
    public int UsersWithoutManager { get; set; }
    public int UsersWithDepartment { get; set; }
    public int UsersWithoutDepartment { get; set; }
    public double ManagerCoveragePercent => TotalUsers > 0 ? Math.Round((double)UsersWithManager / TotalUsers * 100, 1) : 0;
    public double DepartmentCoveragePercent => TotalUsers > 0 ? Math.Round((double)UsersWithDepartment / TotalUsers * 100, 1) : 0;
    public int CustomFolders { get; set; }
}

/// <summary>
/// DTO for field lookup values merged with identity usage counts
/// </summary>
public class FieldValueWithUsage
{
    public Guid? LookupId { get; set; }
    public string FieldName { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
    public bool IsManaged { get; set; }
    public int IdentityCount { get; set; }
}

/// <summary>
/// Query filter definition for dynamic folders
/// </summary>
public class FolderQueryFilter
{
    public string Field { get; set; } = string.Empty; // Department, Division, Manager, etc.
    public string Operator { get; set; } = "equals"; // equals, contains, startswith
    public string Value { get; set; } = string.Empty;
}

/// <summary>
/// Diagnostic info for troubleshooting manager resolution
/// </summary>
public class ManagerDiagnosticInfo
{
    public int TotalObjects { get; set; }
    public int ObjectsWithManagerSourceId { get; set; }
    public int ObjectsWithManagerObjectId { get; set; }
    public int IdentitiesWithManagerId { get; set; }
    public List<ManagerSampleRecord> SampleRecords { get; set; } = new();
    public string? ErrorMessage { get; set; }
}

public class ManagerSampleRecord
{
    public string DisplayName { get; set; } = "";
    public string? ManagerSourceId { get; set; }
    public Guid? ManagerObjectId { get; set; }
    public string? ManagerName { get; set; }
}
