using DataAccessLibrary.Models;
using DataAccessLibrary.Repositories;
using Microsoft.Extensions.Logging;

namespace DataAccessLibrary.Services;

/// <summary>
/// Service for managing organizational structure views and custom folders
/// Wraps the repository with business logic and validation
/// </summary>
public class OrganizationService : IOrganizationService
{
    private readonly IOrganizationRepository _repository;
    private readonly ILogger<OrganizationService> _logger;

    public OrganizationService(
        IOrganizationRepository repository,
        ILogger<OrganizationService> logger)
    {
        _repository = repository;
        _logger = logger;
    }

    // ====================================================================
    // STATISTICS & OVERVIEW
    // ====================================================================

    public async Task<OrganizationStats> GetStatsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            return await _repository.GetOrganizationStatsAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting organization statistics");
            throw;
        }
    }

    public async Task<ManagerDiagnosticInfo> GetManagerDiagnosticsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            return await _repository.GetManagerDiagnosticsAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting manager diagnostics");
            return new ManagerDiagnosticInfo { ErrorMessage = ex.Message };
        }
    }

    // ====================================================================
    // DEPARTMENT VIEW
    // ====================================================================

    public async Task<List<OrgNodeDto>> GetDepartmentsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            return await _repository.GetDepartmentsAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting departments");
            throw;
        }
    }

    public async Task<List<Identity>> GetDepartmentMembersAsync(string department, int page = 1, int pageSize = 50, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(department))
            throw new ArgumentException("Department name is required", nameof(department));

        try
        {
            return await _repository.GetUsersInDepartmentAsync(department, page, pageSize, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting members for department {Department}", department);
            throw;
        }
    }

    public async Task<int> GetDepartmentMemberCountAsync(string department, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(department))
            throw new ArgumentException("Department name is required", nameof(department));

        try
        {
            return await _repository.GetDepartmentUserCountAsync(department, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting member count for department {Department}", department);
            throw;
        }
    }

    // ====================================================================
    // DIVISION VIEW
    // ====================================================================

    public async Task<List<OrgNodeDto>> GetDivisionsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            return await _repository.GetDivisionsAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting divisions");
            throw;
        }
    }

    public async Task<List<string>> GetDepartmentsInDivisionAsync(string division, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(division))
            throw new ArgumentException("Division name is required", nameof(division));

        try
        {
            return await _repository.GetDepartmentsInDivisionAsync(division, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting departments for division {Division}", division);
            throw;
        }
    }

    // ====================================================================
    // MANAGER HIERARCHY VIEW
    // ====================================================================

    public async Task<List<OrgNodeDto>> GetTopManagersAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            return await _repository.GetTopManagersAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting top managers");
            throw;
        }
    }

    public async Task<List<OrgNodeDto>> GetAllManagersAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            return await _repository.GetAllManagersAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting all managers");
            throw;
        }
    }

    public async Task<List<Identity>> GetDirectReportsAsync(Guid managerIdentityId, CancellationToken cancellationToken = default)
    {
        if (managerIdentityId == Guid.Empty)
            throw new ArgumentException("Manager identity ID is required", nameof(managerIdentityId));

        try
        {
            return await _repository.GetDirectReportsAsync(managerIdentityId, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting direct reports for manager {ManagerId}", managerIdentityId);
            throw;
        }
    }

    public async Task<List<Identity>> GetOrphanedUsersAsync(int page = 1, int pageSize = 50, CancellationToken cancellationToken = default)
    {
        try
        {
            return await _repository.GetUsersWithoutManagerAsync(page, pageSize, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting orphaned users");
            throw;
        }
    }

    public async Task<int> GetOrphanedUsersCountAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            return await _repository.GetUsersWithoutManagerCountAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting orphaned users count");
            throw;
        }
    }

    // ====================================================================
    // CUSTOM FOLDER MANAGEMENT
    // ====================================================================

    public async Task<List<OrganizationalFolder>> GetFoldersAsync(Guid? parentId = null, CancellationToken cancellationToken = default)
    {
        try
        {
            return await _repository.GetCustomFoldersAsync(parentId, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting folders (parentId: {ParentId})", parentId);
            throw;
        }
    }

    public async Task<OrganizationalFolder?> GetFolderByIdAsync(Guid folderId, CancellationToken cancellationToken = default)
    {
        if (folderId == Guid.Empty)
            throw new ArgumentException("Folder ID is required", nameof(folderId));

        try
        {
            return await _repository.GetFolderByIdAsync(folderId, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting folder {FolderId}", folderId);
            throw;
        }
    }

    public async Task<OrganizationalFolder> CreateFolderAsync(string name, string folderType, string? description = null, Guid? parentId = null, string? createdBy = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Folder name is required", nameof(name));
        if (string.IsNullOrWhiteSpace(folderType))
            throw new ArgumentException("Folder type is required", nameof(folderType));

        // Validate folder type
        var validTypes = new[] { FolderTypes.Custom, FolderTypes.Team, FolderTypes.Project, FolderTypes.Department, FolderTypes.Division, FolderTypes.Manager };
        if (!validTypes.Contains(folderType))
        {
            _logger.LogWarning("Invalid folder type {FolderType} specified, defaulting to Custom", folderType);
            folderType = FolderTypes.Custom;
        }

        try
        {
            var folder = new OrganizationalFolder
            {
                Name = name.Trim(),
                Description = description?.Trim(),
                FolderType = folderType,
                ParentId = parentId,
                CreatedBy = createdBy,
                IsSystem = false,
                IsActive = true
            };

            var created = await _repository.CreateFolderAsync(folder, cancellationToken);
            _logger.LogInformation("Created folder {FolderName} (ID: {FolderId}) of type {FolderType}", created.Name, created.Id, created.FolderType);
            return created;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating folder {FolderName}", name);
            throw;
        }
    }

    public async Task<OrganizationalFolder> GetOrCreateDynamicFolderAsync(string name, string folderType, string? queryKey = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Folder name is required", nameof(name));
        if (string.IsNullOrWhiteSpace(folderType))
            throw new ArgumentException("Folder type is required", nameof(folderType));

        try
        {
            // Check if system folder already exists
            var existing = await _repository.GetSystemFolderByNameAndTypeAsync(name, folderType, cancellationToken);
            if (existing != null)
            {
                _logger.LogDebug("Found existing system folder {FolderName} ({FolderType})", name, folderType);
                return existing;
            }

            // Create new system folder for dynamic view
            var folder = new OrganizationalFolder
            {
                Name = name.Trim(),
                Description = $"Auto-created system folder for {folderType}: {name}",
                FolderType = folderType,
                QueryFilter = queryKey, // Store the query key for future reference
                IsSystem = true,
                IsActive = true
            };

            var created = await _repository.CreateFolderAsync(folder, cancellationToken);
            _logger.LogInformation("Created system folder {FolderName} (ID: {FolderId}) of type {FolderType}", created.Name, created.Id, created.FolderType);
            return created;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting or creating dynamic folder {FolderName}", name);
            throw;
        }
    }

    public async Task<OrganizationalFolder> UpdateFolderAsync(OrganizationalFolder folder, CancellationToken cancellationToken = default)
    {
        if (folder == null)
            throw new ArgumentNullException(nameof(folder));
        if (folder.Id == Guid.Empty)
            throw new ArgumentException("Folder ID is required", nameof(folder));

        // Prevent modifying system folders
        var existing = await _repository.GetFolderByIdAsync(folder.Id, cancellationToken);
        if (existing?.IsSystem == true)
        {
            throw new InvalidOperationException("Cannot modify system-generated folders");
        }

        try
        {
            folder.ModifiedAt = DateTime.UtcNow;
            var updated = await _repository.UpdateFolderAsync(folder, cancellationToken);
            _logger.LogInformation("Updated folder {FolderId}", folder.Id);
            return updated;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating folder {FolderId}", folder.Id);
            throw;
        }
    }

    public async Task<bool> DeleteFolderAsync(Guid folderId, CancellationToken cancellationToken = default)
    {
        if (folderId == Guid.Empty)
            throw new ArgumentException("Folder ID is required", nameof(folderId));

        // Prevent deleting system folders
        var existing = await _repository.GetFolderByIdAsync(folderId, cancellationToken);
        if (existing?.IsSystem == true)
        {
            throw new InvalidOperationException("Cannot delete system-generated folders");
        }

        try
        {
            var result = await _repository.DeleteFolderAsync(folderId, cancellationToken);
            if (result)
            {
                _logger.LogInformation("Deleted folder {FolderId}", folderId);
            }
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting folder {FolderId}", folderId);
            throw;
        }
    }

    // ====================================================================
    // FOLDER MEMBER MANAGEMENT
    // ====================================================================

    public async Task<List<Identity>> GetFolderMembersAsync(Guid folderId, int page = 1, int pageSize = 50, CancellationToken cancellationToken = default)
    {
        if (folderId == Guid.Empty)
            throw new ArgumentException("Folder ID is required", nameof(folderId));

        try
        {
            return await _repository.GetFolderMembersAsync(folderId, page, pageSize, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting members for folder {FolderId}", folderId);
            throw;
        }
    }

    public async Task<int> GetFolderMemberCountAsync(Guid folderId, CancellationToken cancellationToken = default)
    {
        if (folderId == Guid.Empty)
            throw new ArgumentException("Folder ID is required", nameof(folderId));

        try
        {
            return await _repository.GetFolderMemberCountAsync(folderId, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting member count for folder {FolderId}", folderId);
            throw;
        }
    }

    public async Task<bool> AddMemberToFolderAsync(Guid folderId, Guid identityId, string? addedBy = null, string? notes = null, CancellationToken cancellationToken = default)
    {
        if (folderId == Guid.Empty)
            throw new ArgumentException("Folder ID is required", nameof(folderId));
        if (identityId == Guid.Empty)
            throw new ArgumentException("Identity ID is required", nameof(identityId));

        try
        {
            var result = await _repository.AddMemberToFolderAsync(folderId, identityId, addedBy, notes, cancellationToken);
            if (result)
            {
                _logger.LogInformation("Added identity {IdentityId} to folder {FolderId}", identityId, folderId);
            }
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error adding identity {IdentityId} to folder {FolderId}", identityId, folderId);
            throw;
        }
    }

    public async Task<bool> RemoveMemberFromFolderAsync(Guid folderId, Guid identityId, CancellationToken cancellationToken = default)
    {
        if (folderId == Guid.Empty)
            throw new ArgumentException("Folder ID is required", nameof(folderId));
        if (identityId == Guid.Empty)
            throw new ArgumentException("Identity ID is required", nameof(identityId));

        try
        {
            var result = await _repository.RemoveMemberFromFolderAsync(folderId, identityId, cancellationToken);
            if (result)
            {
                _logger.LogInformation("Removed identity {IdentityId} from folder {FolderId}", identityId, folderId);
            }
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error removing identity {IdentityId} from folder {FolderId}", identityId, folderId);
            throw;
        }
    }

    // ====================================================================
    // FOLDER POLICY MANAGEMENT
    // ====================================================================

    public async Task<List<OrganizationalFolderPolicy>> GetFolderPoliciesAsync(Guid folderId, CancellationToken cancellationToken = default)
    {
        if (folderId == Guid.Empty)
            throw new ArgumentException("Folder ID is required", nameof(folderId));

        try
        {
            return await _repository.GetFolderPoliciesAsync(folderId, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting policies for folder {FolderId}", folderId);
            throw;
        }
    }

    public async Task<bool> AttachPolicyAsync(Guid folderId, Guid policyId, string? appliedBy = null, bool inheritToChildren = true, CancellationToken cancellationToken = default)
    {
        if (folderId == Guid.Empty)
            throw new ArgumentException("Folder ID is required", nameof(folderId));
        if (policyId == Guid.Empty)
            throw new ArgumentException("Policy ID is required", nameof(policyId));

        try
        {
            var result = await _repository.AttachPolicyToFolderAsync(folderId, policyId, appliedBy, inheritToChildren, cancellationToken);
            if (result)
            {
                _logger.LogInformation("Attached policy {PolicyId} to folder {FolderId}", policyId, folderId);
            }
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error attaching policy {PolicyId} to folder {FolderId}", policyId, folderId);
            throw;
        }
    }

    public async Task<bool> RemovePolicyAsync(Guid folderId, Guid policyId, CancellationToken cancellationToken = default)
    {
        if (folderId == Guid.Empty)
            throw new ArgumentException("Folder ID is required", nameof(folderId));
        if (policyId == Guid.Empty)
            throw new ArgumentException("Policy ID is required", nameof(policyId));

        try
        {
            var result = await _repository.RemovePolicyFromFolderAsync(folderId, policyId, cancellationToken);
            if (result)
            {
                _logger.LogInformation("Removed policy {PolicyId} from folder {FolderId}", policyId, folderId);
            }
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error removing policy {PolicyId} from folder {FolderId}", policyId, folderId);
            throw;
        }
    }

    // ====================================================================
    // SEARCH
    // ====================================================================

    public async Task<List<Identity>> SearchAsync(string searchTerm, int maxResults = 50, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(searchTerm))
            return new List<Identity>();

        try
        {
            return await _repository.SearchOrganizationAsync(searchTerm.Trim(), maxResults, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error searching organization for term {SearchTerm}", searchTerm);
            throw;
        }
    }
}
