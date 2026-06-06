using DataAccessLibrary.Models;

namespace DataAccessLibrary.Repositories;

/// <summary>
/// Repository interface for delegation access template management.
/// Handles CRUD for AccessTemplates, TemplatePermissions, ManagedScopes,
/// DelegationAssignments, and DelegationScopeComposites.
/// </summary>
public interface IDelegationRepository
{
    // =========================================================================
    // AccessTemplate CRUD
    // =========================================================================

    /// <summary>Gets all access templates, ordered by name.</summary>
    Task<List<AccessTemplate>> GetAllTemplatesAsync();

    /// <summary>Gets a single access template by ID. Returns null if not found.</summary>
    Task<AccessTemplate?> GetTemplateByIdAsync(Guid id);

    /// <summary>
    /// Gets a template with its permissions loaded in a single round-trip using
    /// QueryMultipleAsync. Returns null if not found.
    /// </summary>
    Task<AccessTemplate?> GetTemplateWithPermissionsAsync(Guid id);

    /// <summary>Inserts a new access template. Returns the inserted template's ID.</summary>
    Task<Guid> CreateTemplateAsync(AccessTemplate template);

    /// <summary>Updates an existing access template's name, description, and IsActive flag.</summary>
    Task UpdateTemplateAsync(AccessTemplate template);

    /// <summary>Soft-deletes a template by setting IsActive = 0.</summary>
    Task DeleteTemplateAsync(Guid id);

    // =========================================================================
    // TemplatePermission CRUD
    // =========================================================================

    /// <summary>Gets all permissions for the given template.</summary>
    Task<List<TemplatePermission>> GetPermissionsForTemplateAsync(Guid templateId);

    /// <summary>
    /// Replaces all permissions for a template inside a transaction:
    /// DELETE existing rows then INSERT the new set.
    /// </summary>
    Task SetPermissionsAsync(Guid templateId, List<TemplatePermission> permissions);

    // =========================================================================
    // ManagedScope CRUD
    // =========================================================================

    /// <summary>Gets all managed scopes, ordered by name.</summary>
    Task<List<ManagedScope>> GetAllScopesAsync();

    /// <summary>Gets a single managed scope by ID. Returns null if not found.</summary>
    Task<ManagedScope?> GetScopeByIdAsync(Guid id);

    /// <summary>Inserts a new managed scope. Returns the inserted scope's ID.</summary>
    Task<Guid> CreateScopeAsync(ManagedScope scope);

    /// <summary>Updates an existing managed scope's name, description, ScopeType, ScopeDefinition, and IsActive flag.</summary>
    Task UpdateScopeAsync(ManagedScope scope);

    /// <summary>Soft-deletes a managed scope by setting IsActive = 0.</summary>
    Task DeleteScopeAsync(Guid id);

    // =========================================================================
    // DelegationAssignment CRUD
    // =========================================================================

    /// <summary>Gets all delegation assignments with their joined TemplateName and ScopeName.</summary>
    Task<List<DelegationAssignment>> GetAllAssignmentsAsync();

    /// <summary>Gets a single delegation assignment by ID. Returns null if not found.</summary>
    Task<DelegationAssignment?> GetAssignmentByIdAsync(Guid id);

    /// <summary>
    /// Gets all active assignments for the specified role IDs.
    /// Filters: PrincipalType = 'Role', PrincipalId IN @RoleIds, IsActive = 1,
    /// and (ExpiresAt IS NULL OR ExpiresAt &gt; GETUTCDATE()).
    /// </summary>
    Task<List<DelegationAssignment>> GetActiveAssignmentsForRolesAsync(IEnumerable<string> roleIds);

    /// <summary>Inserts a new delegation assignment. Returns the inserted assignment's ID.</summary>
    Task<Guid> CreateAssignmentAsync(DelegationAssignment assignment);

    /// <summary>Updates an existing delegation assignment's fields.</summary>
    Task UpdateAssignmentAsync(DelegationAssignment assignment);

    /// <summary>Soft-deletes a delegation assignment by setting IsActive = 0.</summary>
    Task DeleteAssignmentAsync(Guid id);

    /// <summary>
    /// Sets IsActive = 0 for all assignments where ExpiresAt &lt; GETUTCDATE().
    /// Returns the number of rows affected.
    /// </summary>
    Task<int> DeactivateExpiredAssignmentsAsync(CancellationToken ct = default);

    // =========================================================================
    // DelegationScopeComposite CRUD
    // =========================================================================

    /// <summary>Gets all scope composites for the given assignment.</summary>
    Task<List<DelegationScopeComposite>> GetCompositesForAssignmentAsync(Guid assignmentId);

    /// <summary>
    /// Replaces all scope composites for an assignment inside a transaction:
    /// DELETE existing rows then INSERT the new set.
    /// </summary>
    Task SetCompositesAsync(Guid assignmentId, List<Guid> scopeIds);

    // =========================================================================
    // Group-based principal queries
    // =========================================================================

    /// <summary>
    /// Gets all active assignments where PrincipalType = 'Group' and PrincipalId is in
    /// the provided list of group identifiers (SIDs, DNs, or SAMAccountNames).
    /// Filters: IsActive = 1 and (ExpiresAt IS NULL OR ExpiresAt > GETUTCDATE()).
    /// </summary>
    Task<List<DelegationAssignment>> GetActiveAssignmentsForGroupsAsync(List<string> groupIds, CancellationToken ct = default);

    // =========================================================================
    // Batch queries (used by DelegationScopeService to avoid N+1)
    // =========================================================================

    /// <summary>
    /// Loads multiple access templates with their permissions in a single round-trip.
    /// Templates not found are simply absent from the returned list.
    /// </summary>
    Task<List<AccessTemplate>> GetTemplatesWithPermissionsBatchAsync(List<Guid> templateIds, CancellationToken ct = default);

    /// <summary>
    /// Loads multiple managed scopes by ID in a single round-trip.
    /// Scopes not found are simply absent from the returned list.
    /// </summary>
    Task<List<ManagedScope>> GetScopesBatchAsync(List<Guid> scopeIds, CancellationToken ct = default);

    // =========================================================================
    // System-level queries
    // =========================================================================

    /// <summary>
    /// Returns true if there is at least one active delegation assignment in the database.
    /// Used to short-circuit the delegation system when no assignments have been configured.
    /// </summary>
    Task<bool> AnyAssignmentsExistAsync();
}
