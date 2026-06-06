namespace DataAccessLibrary.Models;

public class AccessTemplate
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool IsSystem { get; set; }
    public bool IsActive { get; set; } = true;
    public string? CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string? ModifiedBy { get; set; }
    public DateTime? ModifiedAt { get; set; }

    // Navigation (populated by service, not Dapper auto-map)
    public List<TemplatePermission> Permissions { get; set; } = new();
    public int AssignmentCount { get; set; } // computed: how many delegations use this template
}

public class TemplatePermission
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid AccessTemplateId { get; set; }
    public string PermissionType { get; set; } = string.Empty;  // ObjectType, Attribute, Action, Page, CatalogResource
    public string? ObjectClass { get; set; }                     // NULL = all types
    public string Target { get; set; } = string.Empty;          // depends on PermissionType
    public string AccessLevel { get; set; } = "Read";           // Read, Write, Execute, Deny
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class ManagedScope
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string ScopeType { get; set; } = string.Empty;       // OU, Query, Connection, ObjectType, QueryAdvanced, All
    public string ScopeDefinition { get; set; } = "{}";         // JSON
    public bool IsActive { get; set; } = true;
    public string? CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string? ModifiedBy { get; set; }
    public DateTime? ModifiedAt { get; set; }

    // Computed: how many delegations use this scope
    public int AssignmentCount { get; set; }
}

public class DelegationAssignment
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid AccessTemplateId { get; set; }
    public string PrincipalType { get; set; } = "Role";         // Role, User, Group
    public string PrincipalId { get; set; } = string.Empty;     // RoleId, UserId, or Group SID/DN
    public string? PrincipalName { get; set; }                  // Cached display name
    public Guid? ManagedScopeId { get; set; }                   // NULL = use composites or global
    public bool IsActive { get; set; } = true;
    public DateTime? ExpiresAt { get; set; }
    public string? CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string? ModifiedBy { get; set; }
    public DateTime? ModifiedAt { get; set; }

    // Navigation
    public string? TemplateName { get; set; }
    public string? ScopeName { get; set; }
    public bool IsExpired => ExpiresAt.HasValue && ExpiresAt.Value < DateTime.UtcNow;
}

public class DelegationScopeComposite
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid DelegationAssignmentId { get; set; }
    public Guid ManagedScopeId { get; set; }
}

// ============================================================================
// RUNTIME RESOLVED CONTEXT (cached per Blazor circuit)
// ============================================================================

/// <summary>
/// The fully resolved delegation context for the current user.
/// Built once per circuit from the user's roles + delegation assignments.
/// </summary>
public class UserDelegationContext
{
    public string UserId { get; set; } = string.Empty;
    public int AccessLevel { get; set; } = 1;
    public bool IsAdmin { get; set; }
    public bool HasAnyDelegation { get; set; }
    public bool DelegationSystemActive { get; set; } // true if ANY assignments exist in DB
    public List<ResolvedDelegation> Delegations { get; set; } = new();
    public DateTime ResolvedAt { get; set; } = DateTime.UtcNow;

    // Pre-computed for fast lookups
    public HashSet<string> AllowedObjectTypes { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> AllowedPages { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> DeniedActions { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, HashSet<string>> WritableAttributes { get; set; } = new(); // objectClass -> attributes
    public Dictionary<string, HashSet<string>> AllowedActions { get; set; } = new();     // objectClass -> actions
}

public class ResolvedDelegation
{
    public Guid AssignmentId { get; set; }
    public AccessTemplate Template { get; set; } = null!;
    public List<ManagedScope> Scopes { get; set; } = new();

    // Pre-built SQL fragment for this delegation's scope
    public string ScopeWhereClause { get; set; } = string.Empty;
    public Dictionary<string, object> ScopeParameters { get; set; } = new();
}

// ============================================================================
// SCOPE DEFINITION DTOs (deserialized from ManagedScope.ScopeDefinition JSON)
// ============================================================================

public class OUScopeDefinition
{
    public string DN { get; set; } = string.Empty;
    public bool IncludeChildren { get; set; } = true;
}

public class QueryScopeDefinition
{
    public string Field { get; set; } = string.Empty;       // Department, ObjectClass, etc.
    public string Operator { get; set; } = "Equals";        // Equals, Contains, StartsWith, In
    public string Value { get; set; } = string.Empty;       // Single value or comma-separated for In
}

public class ConnectionScopeDefinition
{
    public Guid ConnectionId { get; set; }
}

public class ObjectTypeScopeDefinition
{
    public string ObjectClass { get; set; } = string.Empty;
}

// ============================================================================
// UI SELECTION TRANSFER OBJECT — used by AttributeScopeBrowser component
// ============================================================================

/// <summary>
/// Carries a scope selection made in the attribute browser back to the parent page.
/// ScopeDefinitionJson is a ready-to-store JSON string matching the ManagedScope.ScopeDefinition format.
/// </summary>
public class ScopeSelection
{
    /// <summary>OU, Query, ObjectType, or Connection</summary>
    public string ScopeType { get; set; } = string.Empty;

    /// <summary>Human-readable label for what was selected</summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>Ready-to-store JSON that matches the ManagedScope.ScopeDefinition format</summary>
    public string ScopeDefinitionJson { get; set; } = "{}";
}

// ============================================================================
// DELEGATION REQUEST / APPROVAL WORKFLOW
// ============================================================================

/// <summary>
/// A request for a delegation assignment. Requires admin approval before activation.
/// </summary>
public class DelegationRequest
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid AccessTemplateId { get; set; }
    public string PrincipalType { get; set; } = "Role";
    public string PrincipalId { get; set; } = string.Empty;
    public string? PrincipalName { get; set; }
    public Guid? ManagedScopeId { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public string? Justification { get; set; }
    public string RequestedBy { get; set; } = string.Empty;
    public DateTime RequestedAt { get; set; } = DateTime.UtcNow;
    public string Status { get; set; } = "Pending"; // Pending, Approved, Rejected
    public string? ReviewedBy { get; set; }
    public DateTime? ReviewedAt { get; set; }
    public string? ReviewComments { get; set; }
    public Guid? CreatedAssignmentId { get; set; } // Set when approved and assignment created
}

// ============================================================================
// VIRTUAL CONTAINERS — give flat sources (Entra, SCIM, CSV) an OU-like hierarchy
// ============================================================================

/// <summary>
/// Virtual container for organizing flat identity sources (Entra ID, SCIM, CSV)
/// into a browsable hierarchy using attribute-based rules.
/// </summary>
public class VirtualContainer
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ConnectionId { get; set; }
    public string Name { get; set; } = string.Empty;
    public Guid? ParentId { get; set; }

    /// <summary>Attribute, ObjectClass, Static, or Rule</summary>
    public string ContainerType { get; set; } = "Attribute";

    /// <summary>For Attribute type: Department, Company, Office, EmployeeType, etc.</summary>
    public string? AttributeName { get; set; }

    /// <summary>For Attribute type: the specific value that matches objects into this container.</summary>
    public string? AttributeValue { get; set; }

    /// <summary>For Rule type: a SQL WHERE fragment applied against the Objects table.</summary>
    public string? RuleExpression { get; set; }

    /// <summary>Optional Font Awesome icon class (e.g., fas fa-building).</summary>
    public string? IconClass { get; set; }

    public int SortOrder { get; set; }
    public bool IsAutoGenerated { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ModifiedAt { get; set; }

    // Computed — not stored in DB, populated by repository
    public int ObjectCount { get; set; }
    public List<VirtualContainer> Children { get; set; } = new();
}
