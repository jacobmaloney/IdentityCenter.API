using DataAccessLibrary.Models;

namespace DataAccessLibrary.Repositories;

/// <summary>
/// Repository interface for organizational structure management
/// </summary>
public interface IOrganizationRepository
{
    // Statistics
    Task<OrganizationStats> GetOrganizationStatsAsync(CancellationToken cancellationToken = default);
    Task<ManagerDiagnosticInfo> GetManagerDiagnosticsAsync(CancellationToken cancellationToken = default);

    // Department View
    Task<List<OrgNodeDto>> GetDepartmentsAsync(CancellationToken cancellationToken = default);
    Task<List<Identity>> GetUsersInDepartmentAsync(string department, int page = 1, int pageSize = 50, CancellationToken cancellationToken = default);
    Task<int> GetDepartmentUserCountAsync(string department, CancellationToken cancellationToken = default);

    // Division View
    Task<List<OrgNodeDto>> GetDivisionsAsync(CancellationToken cancellationToken = default);
    Task<List<string>> GetDepartmentsInDivisionAsync(string division, CancellationToken cancellationToken = default);

    // Manager Hierarchy View
    Task<List<OrgNodeDto>> GetTopManagersAsync(CancellationToken cancellationToken = default);
    Task<List<OrgNodeDto>> GetAllManagersAsync(CancellationToken cancellationToken = default);
    Task<List<Identity>> GetDirectReportsAsync(Guid managerIdentityId, CancellationToken cancellationToken = default);
    Task<List<Identity>> GetUsersWithoutManagerAsync(int page = 1, int pageSize = 50, CancellationToken cancellationToken = default);
    Task<int> GetUsersWithoutManagerCountAsync(CancellationToken cancellationToken = default);

    // Custom Folders
    Task<List<OrganizationalFolder>> GetCustomFoldersAsync(Guid? parentId = null, CancellationToken cancellationToken = default);
    Task<OrganizationalFolder?> GetFolderByIdAsync(Guid folderId, CancellationToken cancellationToken = default);
    Task<OrganizationalFolder?> GetSystemFolderByNameAndTypeAsync(string name, string folderType, CancellationToken cancellationToken = default);
    Task<OrganizationalFolder> CreateFolderAsync(OrganizationalFolder folder, CancellationToken cancellationToken = default);
    Task<OrganizationalFolder> UpdateFolderAsync(OrganizationalFolder folder, CancellationToken cancellationToken = default);
    Task<bool> DeleteFolderAsync(Guid folderId, CancellationToken cancellationToken = default);

    // Folder Members (for custom/static folders)
    Task<List<Identity>> GetFolderMembersAsync(Guid folderId, int page = 1, int pageSize = 50, CancellationToken cancellationToken = default);
    Task<int> GetFolderMemberCountAsync(Guid folderId, CancellationToken cancellationToken = default);
    Task<bool> AddMemberToFolderAsync(Guid folderId, Guid identityId, string? addedBy = null, string? notes = null, CancellationToken cancellationToken = default);
    Task<bool> RemoveMemberFromFolderAsync(Guid folderId, Guid identityId, CancellationToken cancellationToken = default);

    // Folder Policies
    Task<List<OrganizationalFolderPolicy>> GetFolderPoliciesAsync(Guid folderId, CancellationToken cancellationToken = default);
    Task<bool> AttachPolicyToFolderAsync(Guid folderId, Guid policyId, string? appliedBy = null, bool inheritToChildren = true, CancellationToken cancellationToken = default);
    Task<bool> RemovePolicyFromFolderAsync(Guid folderId, Guid policyId, CancellationToken cancellationToken = default);

    // Search
    Task<List<Identity>> SearchOrganizationAsync(string searchTerm, int maxResults = 50, CancellationToken cancellationToken = default);
}
