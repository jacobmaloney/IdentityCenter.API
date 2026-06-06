using DataAccessLibrary.Models;

namespace DataAccessLibrary.Services;

/// <summary>
/// Service for managing organizational structure views and custom folders
/// Supports Department, Division, and Manager hierarchy views
/// </summary>
public interface IOrganizationService
{
    // ====================================================================
    // STATISTICS & OVERVIEW
    // ====================================================================

    /// <summary>
    /// Gets comprehensive organization statistics
    /// </summary>
    Task<OrganizationStats> GetStatsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Diagnostic: Get sample manager data from Objects table
    /// </summary>
    Task<ManagerDiagnosticInfo> GetManagerDiagnosticsAsync(CancellationToken cancellationToken = default);

    // ====================================================================
    // DEPARTMENT VIEW
    // ====================================================================

    /// <summary>
    /// Gets all departments as tree nodes
    /// </summary>
    Task<List<OrgNodeDto>> GetDepartmentsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets users in a specific department with pagination
    /// </summary>
    Task<List<Identity>> GetDepartmentMembersAsync(string department, int page = 1, int pageSize = 50, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets total count of users in a department
    /// </summary>
    Task<int> GetDepartmentMemberCountAsync(string department, CancellationToken cancellationToken = default);

    // ====================================================================
    // DIVISION VIEW
    // ====================================================================

    /// <summary>
    /// Gets all divisions as tree nodes
    /// </summary>
    Task<List<OrgNodeDto>> GetDivisionsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets departments within a specific division
    /// </summary>
    Task<List<string>> GetDepartmentsInDivisionAsync(string division, CancellationToken cancellationToken = default);

    // ====================================================================
    // MANAGER HIERARCHY VIEW
    // ====================================================================

    /// <summary>
    /// Gets top-level managers (managers with no manager)
    /// </summary>
    Task<List<OrgNodeDto>> GetTopManagersAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all managers sorted by direct report count (descending)
    /// </summary>
    Task<List<OrgNodeDto>> GetAllManagersAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets direct reports for a manager
    /// </summary>
    Task<List<Identity>> GetDirectReportsAsync(Guid managerIdentityId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets users without a manager assigned
    /// </summary>
    Task<List<Identity>> GetOrphanedUsersAsync(int page = 1, int pageSize = 50, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets count of users without a manager
    /// </summary>
    Task<int> GetOrphanedUsersCountAsync(CancellationToken cancellationToken = default);

    // ====================================================================
    // CUSTOM FOLDER MANAGEMENT
    // ====================================================================

    /// <summary>
    /// Gets custom folders, optionally filtered by parent
    /// </summary>
    Task<List<OrganizationalFolder>> GetFoldersAsync(Guid? parentId = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a folder by ID
    /// </summary>
    Task<OrganizationalFolder?> GetFolderByIdAsync(Guid folderId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a new custom folder
    /// </summary>
    Task<OrganizationalFolder> CreateFolderAsync(string name, string folderType, string? description = null, Guid? parentId = null, string? createdBy = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets or creates a system folder for dynamic organizational views (departments, divisions, managers)
    /// Creates a folder with query filter if it doesn't exist, otherwise returns existing
    /// </summary>
    Task<OrganizationalFolder> GetOrCreateDynamicFolderAsync(string name, string folderType, string? queryKey = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates an existing folder
    /// </summary>
    Task<OrganizationalFolder> UpdateFolderAsync(OrganizationalFolder folder, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a custom folder
    /// </summary>
    Task<bool> DeleteFolderAsync(Guid folderId, CancellationToken cancellationToken = default);

    // ====================================================================
    // FOLDER MEMBER MANAGEMENT
    // ====================================================================

    /// <summary>
    /// Gets members of a custom folder with pagination
    /// </summary>
    Task<List<Identity>> GetFolderMembersAsync(Guid folderId, int page = 1, int pageSize = 50, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets count of members in a folder
    /// </summary>
    Task<int> GetFolderMemberCountAsync(Guid folderId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Adds an identity to a custom folder
    /// </summary>
    Task<bool> AddMemberToFolderAsync(Guid folderId, Guid identityId, string? addedBy = null, string? notes = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes an identity from a custom folder
    /// </summary>
    Task<bool> RemoveMemberFromFolderAsync(Guid folderId, Guid identityId, CancellationToken cancellationToken = default);

    // ====================================================================
    // FOLDER POLICY MANAGEMENT
    // ====================================================================

    /// <summary>
    /// Gets policies attached to a folder
    /// </summary>
    Task<List<OrganizationalFolderPolicy>> GetFolderPoliciesAsync(Guid folderId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Attaches a policy to a folder
    /// </summary>
    Task<bool> AttachPolicyAsync(Guid folderId, Guid policyId, string? appliedBy = null, bool inheritToChildren = true, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes a policy from a folder
    /// </summary>
    Task<bool> RemovePolicyAsync(Guid folderId, Guid policyId, CancellationToken cancellationToken = default);

    // ====================================================================
    // SEARCH
    // ====================================================================

    /// <summary>
    /// Searches organization for users by name, email, department, or title
    /// </summary>
    Task<List<Identity>> SearchAsync(string searchTerm, int maxResults = 50, CancellationToken cancellationToken = default);
}
