using Dapper;
using DataAccessLibrary.Models;
using Logging;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;

namespace DataAccessLibrary.Repositories;

/// <summary>
/// Repository implementation for organizational structure management.
/// Uses Dapper for all database access - queries from Identities (Persons) table
/// for department, manager, division data.
/// </summary>
public class OrganizationRepository : DapperRepositoryBase, IOrganizationRepository
{
    public OrganizationRepository(IConfiguration configuration, IGlobalLogger logger)
        : base(configuration, logger) { }

    #region Statistics

    public async Task<OrganizationStats> GetOrganizationStatsAsync(CancellationToken cancellationToken = default)
    {
        return await ExecuteAsync<OrganizationStats>(async conn =>
        {
            var stats = new OrganizationStats();

            try
            {
                // Get person/identity counts from Identities table
                // Note: Don't filter by IsActive - match what People page shows
                var identities = (await conn.QueryAsync<IdentityStatRow>(
                    @"SELECT Department, ManagerIdentityId FROM [Identities]")
                    .ConfigureAwait(false)).ToList();

                stats.TotalUsers = identities.Count;
                stats.UsersWithDepartment = identities.Count(i => !string.IsNullOrEmpty(i.Department));
                stats.UsersWithoutDepartment = identities.Count(i => string.IsNullOrEmpty(i.Department));
                stats.UsersWithManager = identities.Count(i => i.ManagerIdentityId != null);
                stats.UsersWithoutManager = identities.Count(i => i.ManagerIdentityId == null);

                // Get unique departments
                stats.TotalDepartments = identities
                    .Where(i => !string.IsNullOrEmpty(i.Department))
                    .Select(i => i.Department)
                    .Distinct()
                    .Count();

                // Get divisions (first part of department if using "/" separator)
                stats.TotalDivisions = identities
                    .Where(i => !string.IsNullOrEmpty(i.Department))
                    .Select(i => i.Department!.Split('/')[0].Trim())
                    .Distinct()
                    .Count();

                // Count managers (identities who have direct reports)
                var managerIds = identities
                    .Where(i => i.ManagerIdentityId != null)
                    .Select(i => i.ManagerIdentityId)
                    .Distinct()
                    .ToList();
                stats.TotalManagers = managerIds.Count;
            }
            catch (Exception ex)
            {
                _logger.LogError("Error querying identity statistics: {Message}", ex.Message);
            }

            // Custom folders count - wrap in try-catch as table may not exist yet
            try
            {
                stats.CustomFolders = await conn.ExecuteScalarAsync<int>(
                    @"SELECT COUNT(*) FROM [OrganizationalFolders]
                      WHERE FolderType = @FolderType AND IsActive = 1",
                    new { FolderType = FolderTypes.Custom }).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogWarning("OrganizationalFolders table may not exist yet: {Message}", ex.Message);
                stats.CustomFolders = 0;
            }

            return stats;
        }, cancellationToken).ConfigureAwait(false);
    }

    public async Task<ManagerDiagnosticInfo> GetManagerDiagnosticsAsync(CancellationToken cancellationToken = default)
    {
        return await ExecuteAsync<ManagerDiagnosticInfo>(async conn =>
        {
            var result = new ManagerDiagnosticInfo();

            try
            {
                // Count totals
                result.TotalObjects = await conn.ExecuteScalarAsync<int>(
                    @"SELECT COUNT(*) FROM [Objects] WHERE ObjectClass = 'user'")
                    .ConfigureAwait(false);

                result.ObjectsWithManagerSourceId = await conn.ExecuteScalarAsync<int>(
                    @"SELECT COUNT(*) FROM [Objects] WHERE ObjectClass = 'user' AND ManagerSourceId IS NOT NULL")
                    .ConfigureAwait(false);

                result.ObjectsWithManagerObjectId = await conn.ExecuteScalarAsync<int>(
                    @"SELECT COUNT(*) FROM [Objects] WHERE ObjectClass = 'user' AND ManagerObjectId IS NOT NULL")
                    .ConfigureAwait(false);

                result.IdentitiesWithManagerId = await conn.ExecuteScalarAsync<int>(
                    @"SELECT COUNT(*) FROM [Identities] WHERE ManagerIdentityId IS NOT NULL")
                    .ConfigureAwait(false);

                // Get sample records with manager info
                var samples = (await conn.QueryAsync<ManagerSampleRecord>(
                    @"SELECT TOP 10
                        o.DisplayName AS DisplayName,
                        o.ManagerSourceId,
                        o.ManagerObjectId,
                        m.DisplayName AS ManagerName
                      FROM [Objects] o
                      LEFT JOIN [Objects] m ON o.ManagerObjectId = m.Id
                      WHERE o.ObjectClass = 'user'
                      ORDER BY o.DisplayName")
                    .ConfigureAwait(false)).ToList();

                result.SampleRecords = samples;
            }
            catch (Exception ex)
            {
                _logger.LogError("Error getting manager diagnostics: {Message}", ex.Message);
                result.ErrorMessage = ex.Message;
            }

            return result;
        }, cancellationToken).ConfigureAwait(false);
    }

    #endregion

    #region Department View

    public async Task<List<OrgNodeDto>> GetDepartmentsAsync(CancellationToken cancellationToken = default)
    {
        return await ExecuteAsync<List<OrgNodeDto>>(async conn =>
        {
            // Get all unique departments with their user counts from Identities table
            var departments = (await conn.QueryAsync<DepartmentCountRow>(
                @"SELECT Department, COUNT(*) AS [Count]
                  FROM [Identities]
                  WHERE Department IS NOT NULL AND Department <> ''
                  GROUP BY Department
                  ORDER BY Department")
                .ConfigureAwait(false)).ToList();

            return departments.Select(d => new OrgNodeDto
            {
                Id = Guid.NewGuid(), // Virtual ID for department
                Name = d.Department!,
                FolderType = FolderTypes.Department,
                IconClass = "fas fa-building",
                MemberCount = d.Count,
                IsSystem = true,
                HasChildren = false
            }).ToList();
        }, cancellationToken).ConfigureAwait(false);
    }

    public async Task<List<Identity>> GetUsersInDepartmentAsync(string department, int page = 1, int pageSize = 50, CancellationToken cancellationToken = default)
    {
        return await ExecuteAsync<List<Identity>>(async conn =>
        {
            var offset = (page - 1) * pageSize;

            var results = await conn.QueryAsync<Identity>(
                @"SELECT * FROM [Identities]
                  WHERE Department = @Department
                  ORDER BY DisplayName
                  OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY",
                new { Department = department, Offset = offset, PageSize = pageSize })
                .ConfigureAwait(false);

            return results.ToList();
        }, cancellationToken).ConfigureAwait(false);
    }

    public async Task<int> GetDepartmentUserCountAsync(string department, CancellationToken cancellationToken = default)
    {
        return await ExecuteAsync<int>(async conn =>
        {
            return await conn.ExecuteScalarAsync<int>(
                @"SELECT COUNT(*) FROM [Identities] WHERE Department = @Department",
                new { Department = department }).ConfigureAwait(false);
        }, cancellationToken).ConfigureAwait(false);
    }

    #endregion

    #region Division View

    public async Task<List<OrgNodeDto>> GetDivisionsAsync(CancellationToken cancellationToken = default)
    {
        return await ExecuteAsync<List<OrgNodeDto>>(async conn =>
        {
            // Get departments and parse divisions from Identities table
            var departments = (await conn.QueryAsync<string>(
                @"SELECT DISTINCT Department FROM [Identities]
                  WHERE Department IS NOT NULL AND Department <> ''")
                .ConfigureAwait(false)).ToList();

            // Group by division (first part before "/")
            var divisions = departments
                .Select(d => d!.Contains('/') ? d.Split('/')[0].Trim() : d)
                .GroupBy(d => d)
                .Select(g => new OrgNodeDto
                {
                    Id = Guid.NewGuid(),
                    Name = g.Key!,
                    FolderType = FolderTypes.Division,
                    IconClass = "fas fa-city",
                    ChildCount = g.Count(),
                    IsSystem = true,
                    HasChildren = true
                })
                .OrderBy(d => d.Name)
                .ToList();

            return divisions;
        }, cancellationToken).ConfigureAwait(false);
    }

    public async Task<List<string>> GetDepartmentsInDivisionAsync(string division, CancellationToken cancellationToken = default)
    {
        return await ExecuteAsync<List<string>>(async conn =>
        {
            var departments = (await conn.QueryAsync<string>(
                @"SELECT DISTINCT Department FROM [Identities]
                  WHERE Department IS NOT NULL AND Department <> ''")
                .ConfigureAwait(false)).ToList();

            return departments
                .Where(d => d!.StartsWith(division, StringComparison.OrdinalIgnoreCase) ||
                           d.Split('/')[0].Trim().Equals(division, StringComparison.OrdinalIgnoreCase))
                .Select(d => d!)
                .Distinct()
                .OrderBy(d => d)
                .ToList();
        }, cancellationToken).ConfigureAwait(false);
    }

    #endregion

    #region Manager Hierarchy

    public async Task<List<OrgNodeDto>> GetTopManagersAsync(CancellationToken cancellationToken = default)
    {
        return await ExecuteAsync<List<OrgNodeDto>>(async conn =>
        {
            try
            {
                // Find all manager identity IDs (people who have direct reports)
                var allManagerIds = (await conn.QueryAsync<Guid>(
                    @"SELECT DISTINCT ManagerIdentityId
                      FROM [Identities]
                      WHERE ManagerIdentityId IS NOT NULL")
                    .ConfigureAwait(false)).ToList();

                _logger.LogInformation("GetTopManagersAsync: Found {0} unique manager IDs", allManagerIds.Count);

                if (!allManagerIds.Any())
                {
                    _logger.LogWarning("GetTopManagersAsync: No manager IDs found - ManagerIdentityId not populated");
                    return new List<OrgNodeDto>();
                }

                // Find managers who don't have a manager themselves (top level)
                // Use in-memory filtering to avoid complex SQL translation issues
                var allManagers = (await conn.QueryAsync<ManagerRow>(
                    @"SELECT Id, DisplayName, JobTitle, PrimaryEmail, ManagerIdentityId
                      FROM [Identities]
                      WHERE Id IN @ManagerIds",
                    new { ManagerIds = allManagerIds })
                    .ConfigureAwait(false)).ToList();

                var topManagers = allManagers
                    .Where(i => i.ManagerIdentityId == null || !allManagerIds.Contains(i.ManagerIdentityId.Value))
                    .ToList();

                _logger.LogInformation("GetTopManagersAsync: Found {0} top managers", topManagers.Count);

                // Get direct report counts
                var reportCounts = (await conn.QueryAsync<ManagerReportCount>(
                    @"SELECT ManagerIdentityId AS ManagerId, COUNT(*) AS [Count]
                      FROM [Identities]
                      WHERE ManagerIdentityId IS NOT NULL
                      GROUP BY ManagerIdentityId")
                    .ConfigureAwait(false)).ToList();

                var countDict = reportCounts.ToDictionary(r => r.ManagerId!.Value, r => r.Count);

                return topManagers.Select(m => new OrgNodeDto
                {
                    Id = m.Id,
                    Name = m.DisplayName ?? "Unknown",
                    FolderType = FolderTypes.Manager,
                    IconClass = "fas fa-user-tie",
                    MemberCount = countDict.GetValueOrDefault(m.Id, 0),
                    ManagerName = m.DisplayName,
                    ManagerTitle = m.JobTitle,
                    ManagerEmail = m.PrimaryEmail,
                    ManagerIdentityId = m.Id,
                    IsSystem = true,
                    HasChildren = countDict.GetValueOrDefault(m.Id, 0) > 0
                }).OrderBy(m => m.Name).ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError("GetTopManagersAsync failed: {Message}", ex.Message);
                throw;
            }
        }, cancellationToken).ConfigureAwait(false);
    }

    public async Task<List<OrgNodeDto>> GetAllManagersAsync(CancellationToken cancellationToken = default)
    {
        return await ExecuteAsync<List<OrgNodeDto>>(async conn =>
        {
            try
            {
                var managers = (await conn.QueryAsync<ManagerRow>(
                    @"SELECT i.Id, i.DisplayName, i.JobTitle, i.PrimaryEmail, i.ManagerIdentityId,
                             COUNT(r.Id) AS DirectReportCount
                      FROM [Identities] i
                      INNER JOIN [Identities] r ON r.ManagerIdentityId = i.Id
                      GROUP BY i.Id, i.DisplayName, i.JobTitle, i.PrimaryEmail, i.ManagerIdentityId
                      ORDER BY COUNT(r.Id) DESC")
                    .ConfigureAwait(false)).ToList();

                return managers.Select(m => new OrgNodeDto
                {
                    Id = m.Id,
                    Name = m.DisplayName ?? "Unknown",
                    FolderType = FolderTypes.Manager,
                    IconClass = "fas fa-user-tie",
                    MemberCount = m.DirectReportCount,
                    ManagerName = m.DisplayName,
                    ManagerTitle = m.JobTitle,
                    ManagerEmail = m.PrimaryEmail,
                    ManagerIdentityId = m.Id,
                    IsSystem = true,
                    HasChildren = m.DirectReportCount > 0
                }).ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError("GetAllManagersAsync failed: {Message}", ex.Message);
                throw;
            }
        }, cancellationToken).ConfigureAwait(false);
    }

    public async Task<List<Identity>> GetDirectReportsAsync(Guid managerIdentityId, CancellationToken cancellationToken = default)
    {
        return await ExecuteAsync<List<Identity>>(async conn =>
        {
            var results = await conn.QueryAsync<Identity>(
                @"SELECT * FROM [Identities]
                  WHERE ManagerIdentityId = @ManagerIdentityId
                  ORDER BY DisplayName",
                new { ManagerIdentityId = managerIdentityId }).ConfigureAwait(false);

            return results.ToList();
        }, cancellationToken).ConfigureAwait(false);
    }

    public async Task<List<Identity>> GetUsersWithoutManagerAsync(int page = 1, int pageSize = 50, CancellationToken cancellationToken = default)
    {
        return await ExecuteAsync<List<Identity>>(async conn =>
        {
            var offset = (page - 1) * pageSize;

            var results = await conn.QueryAsync<Identity>(
                @"SELECT * FROM [Identities]
                  WHERE ManagerIdentityId IS NULL
                  ORDER BY DisplayName
                  OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY",
                new { Offset = offset, PageSize = pageSize }).ConfigureAwait(false);

            return results.ToList();
        }, cancellationToken).ConfigureAwait(false);
    }

    public async Task<int> GetUsersWithoutManagerCountAsync(CancellationToken cancellationToken = default)
    {
        return await ExecuteAsync<int>(async conn =>
        {
            return await conn.ExecuteScalarAsync<int>(
                @"SELECT COUNT(*) FROM [Identities] WHERE ManagerIdentityId IS NULL")
                .ConfigureAwait(false);
        }, cancellationToken).ConfigureAwait(false);
    }

    #endregion

    #region Custom Folders

    public async Task<List<OrganizationalFolder>> GetCustomFoldersAsync(Guid? parentId = null, CancellationToken cancellationToken = default)
    {
        return await ExecuteAsync<List<OrganizationalFolder>>(async conn =>
        {
            try
            {
                string sql;
                object parameters;

                if (parentId == null)
                {
                    sql = @"SELECT * FROM [OrganizationalFolders]
                            WHERE ParentId IS NULL AND IsActive = 1
                            ORDER BY SortOrder, Name";
                    parameters = new { };
                }
                else
                {
                    sql = @"SELECT * FROM [OrganizationalFolders]
                            WHERE ParentId = @ParentId AND IsActive = 1
                            ORDER BY SortOrder, Name";
                    parameters = new { ParentId = parentId };
                }

                var results = await conn.QueryAsync<OrganizationalFolder>(sql, parameters).ConfigureAwait(false);
                return results.ToList();
            }
            catch (Exception ex)
            {
                _logger.LogWarning("OrganizationalFolders table may not exist yet: {Message}", ex.Message);
                return new List<OrganizationalFolder>();
            }
        }, cancellationToken).ConfigureAwait(false);
    }

    public async Task<OrganizationalFolder?> GetFolderByIdAsync(Guid folderId, CancellationToken cancellationToken = default)
    {
        return await ExecuteAsync<OrganizationalFolder?>(async conn =>
        {
            try
            {
                // Use QueryMultipleAsync to load folder + children + members in one round trip
                using var multi = await conn.QueryMultipleAsync(
                    @"SELECT * FROM [OrganizationalFolders] WHERE Id = @FolderId;
                      SELECT * FROM [OrganizationalFolders] WHERE ParentId = @FolderId AND IsActive = 1;
                      SELECT * FROM [OrganizationalFolderMembers] WHERE FolderId = @FolderId AND IsActive = 1;",
                    new { FolderId = folderId }).ConfigureAwait(false);

                var folder = await multi.ReadFirstOrDefaultAsync<OrganizationalFolder>().ConfigureAwait(false);
                if (folder == null)
                    return null;

                var children = (await multi.ReadAsync<OrganizationalFolder>().ConfigureAwait(false)).ToList();
                var members = (await multi.ReadAsync<OrganizationalFolderMember>().ConfigureAwait(false)).ToList();

                folder.Children = children;
                folder.Members = members;

                return folder;
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Error loading folder by ID (table may not exist): {Message}", ex.Message);
                return null;
            }
        }, cancellationToken).ConfigureAwait(false);
    }

    public async Task<OrganizationalFolder?> GetSystemFolderByNameAndTypeAsync(string name, string folderType, CancellationToken cancellationToken = default)
    {
        return await ExecuteAsync<OrganizationalFolder?>(async conn =>
        {
            try
            {
                return await conn.QueryFirstOrDefaultAsync<OrganizationalFolder>(
                    @"SELECT * FROM [OrganizationalFolders]
                      WHERE Name = @Name AND FolderType = @FolderType AND IsSystem = 1",
                    new { Name = name, FolderType = folderType }).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Error loading system folder (table may not exist): {Message}", ex.Message);
                return null;
            }
        }, cancellationToken).ConfigureAwait(false);
    }

    public async Task<OrganizationalFolder> CreateFolderAsync(OrganizationalFolder folder, CancellationToken cancellationToken = default)
    {
        return await ExecuteAsync<OrganizationalFolder>(async conn =>
        {
            folder.CreatedAt = DateTime.UtcNow;
            if (folder.Id == Guid.Empty)
                folder.Id = Guid.NewGuid();

            await conn.ExecuteAsync(
                @"INSERT INTO [OrganizationalFolders]
                    (Id, Name, Description, ParentId, FolderType, QueryFilter, IconClass, SortOrder,
                     IsSystem, IsActive, ManagerIdentityId, MemberCount, MemberCountUpdatedAt,
                     CreatedAt, CreatedBy, ModifiedAt, ModifiedBy)
                  VALUES
                    (@Id, @Name, @Description, @ParentId, @FolderType, @QueryFilter, @IconClass, @SortOrder,
                     @IsSystem, @IsActive, @ManagerIdentityId, @MemberCount, @MemberCountUpdatedAt,
                     @CreatedAt, @CreatedBy, @ModifiedAt, @ModifiedBy)",
                folder).ConfigureAwait(false);

            _logger.LogInformation("Created organizational folder: {0} ({1})", folder.Name, folder.FolderType);
            return folder;
        }, cancellationToken).ConfigureAwait(false);
    }

    public async Task<OrganizationalFolder> UpdateFolderAsync(OrganizationalFolder folder, CancellationToken cancellationToken = default)
    {
        return await ExecuteAsync<OrganizationalFolder>(async conn =>
        {
            folder.ModifiedAt = DateTime.UtcNow;

            await conn.ExecuteAsync(
                @"UPDATE [OrganizationalFolders]
                  SET Name = @Name, Description = @Description, ParentId = @ParentId,
                      FolderType = @FolderType, QueryFilter = @QueryFilter, IconClass = @IconClass,
                      SortOrder = @SortOrder, IsSystem = @IsSystem, IsActive = @IsActive,
                      ManagerIdentityId = @ManagerIdentityId, MemberCount = @MemberCount,
                      MemberCountUpdatedAt = @MemberCountUpdatedAt, ModifiedAt = @ModifiedAt,
                      ModifiedBy = @ModifiedBy
                  WHERE Id = @Id",
                folder).ConfigureAwait(false);

            _logger.LogInformation("Updated organizational folder: {0}", folder.Name);
            return folder;
        }, cancellationToken).ConfigureAwait(false);
    }

    public async Task<bool> DeleteFolderAsync(Guid folderId, CancellationToken cancellationToken = default)
    {
        return await ExecuteAsync<bool>(async conn =>
        {
            // Check if folder exists and is not a system folder
            var folder = await conn.QueryFirstOrDefaultAsync<OrganizationalFolder>(
                @"SELECT * FROM [OrganizationalFolders] WHERE Id = @FolderId",
                new { FolderId = folderId }).ConfigureAwait(false);

            if (folder == null || folder.IsSystem)
                return false;

            // Soft delete
            await conn.ExecuteAsync(
                @"UPDATE [OrganizationalFolders]
                  SET IsActive = 0, ModifiedAt = @ModifiedAt
                  WHERE Id = @FolderId",
                new { FolderId = folderId, ModifiedAt = DateTime.UtcNow }).ConfigureAwait(false);

            _logger.LogInformation("Deleted organizational folder: {0}", folder.Name);
            return true;
        }, cancellationToken).ConfigureAwait(false);
    }

    #endregion

    #region Folder Members

    public async Task<List<Identity>> GetFolderMembersAsync(Guid folderId, int page = 1, int pageSize = 50, CancellationToken cancellationToken = default)
    {
        return await ExecuteAsync<List<Identity>>(async conn =>
        {
            var offset = (page - 1) * pageSize;

            // Get identity IDs from folder members, then load identities
            var identityIds = (await conn.QueryAsync<Guid>(
                @"SELECT IdentityId FROM [OrganizationalFolderMembers]
                  WHERE FolderId = @FolderId AND IsActive = 1
                  ORDER BY AddedAt
                  OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY",
                new { FolderId = folderId, Offset = offset, PageSize = pageSize })
                .ConfigureAwait(false)).ToList();

            if (!identityIds.Any())
                return new List<Identity>();

            var results = await conn.QueryAsync<Identity>(
                @"SELECT * FROM [Identities]
                  WHERE Id IN @IdentityIds
                  ORDER BY DisplayName",
                new { IdentityIds = identityIds }).ConfigureAwait(false);

            return results.ToList();
        }, cancellationToken).ConfigureAwait(false);
    }

    public async Task<int> GetFolderMemberCountAsync(Guid folderId, CancellationToken cancellationToken = default)
    {
        return await ExecuteAsync<int>(async conn =>
        {
            return await conn.ExecuteScalarAsync<int>(
                @"SELECT COUNT(*) FROM [OrganizationalFolderMembers]
                  WHERE FolderId = @FolderId AND IsActive = 1",
                new { FolderId = folderId }).ConfigureAwait(false);
        }, cancellationToken).ConfigureAwait(false);
    }

    public async Task<bool> AddMemberToFolderAsync(Guid folderId, Guid identityId, string? addedBy = null, string? notes = null, CancellationToken cancellationToken = default)
    {
        return await ExecuteAsync<bool>(async conn =>
        {
            // Check if already exists
            var exists = await conn.ExecuteScalarAsync<int>(
                @"SELECT COUNT(*) FROM [OrganizationalFolderMembers]
                  WHERE FolderId = @FolderId AND IdentityId = @IdentityId AND IsActive = 1",
                new { FolderId = folderId, IdentityId = identityId }).ConfigureAwait(false);

            if (exists > 0)
                return false;

            var member = new OrganizationalFolderMember
            {
                Id = Guid.NewGuid(),
                FolderId = folderId,
                IdentityId = identityId,
                AddedBy = addedBy,
                Notes = notes,
                AddedAt = DateTime.UtcNow,
                IsActive = true
            };

            await conn.ExecuteAsync(
                @"INSERT INTO [OrganizationalFolderMembers]
                    (Id, FolderId, IdentityId, AddedBy, Notes, AddedAt, ExpiresAt, IsActive)
                  VALUES
                    (@Id, @FolderId, @IdentityId, @AddedBy, @Notes, @AddedAt, @ExpiresAt, @IsActive)",
                member).ConfigureAwait(false);

            // Update folder member count
            var memberCount = await conn.ExecuteScalarAsync<int>(
                @"SELECT COUNT(*) FROM [OrganizationalFolderMembers]
                  WHERE FolderId = @FolderId AND IsActive = 1",
                new { FolderId = folderId }).ConfigureAwait(false);

            await conn.ExecuteAsync(
                @"UPDATE [OrganizationalFolders]
                  SET MemberCount = @MemberCount, MemberCountUpdatedAt = @UpdatedAt
                  WHERE Id = @FolderId",
                new { FolderId = folderId, MemberCount = memberCount, UpdatedAt = DateTime.UtcNow })
                .ConfigureAwait(false);

            return true;
        }, cancellationToken).ConfigureAwait(false);
    }

    public async Task<bool> RemoveMemberFromFolderAsync(Guid folderId, Guid identityId, CancellationToken cancellationToken = default)
    {
        return await ExecuteAsync<bool>(async conn =>
        {
            // Find the active membership
            var member = await conn.QueryFirstOrDefaultAsync<OrganizationalFolderMember>(
                @"SELECT * FROM [OrganizationalFolderMembers]
                  WHERE FolderId = @FolderId AND IdentityId = @IdentityId AND IsActive = 1",
                new { FolderId = folderId, IdentityId = identityId }).ConfigureAwait(false);

            if (member == null)
                return false;

            // Soft delete the membership
            await conn.ExecuteAsync(
                @"UPDATE [OrganizationalFolderMembers]
                  SET IsActive = 0
                  WHERE Id = @Id",
                new { member.Id }).ConfigureAwait(false);

            // Update folder member count
            var memberCount = await conn.ExecuteScalarAsync<int>(
                @"SELECT COUNT(*) FROM [OrganizationalFolderMembers]
                  WHERE FolderId = @FolderId AND IsActive = 1",
                new { FolderId = folderId }).ConfigureAwait(false);

            await conn.ExecuteAsync(
                @"UPDATE [OrganizationalFolders]
                  SET MemberCount = @MemberCount, MemberCountUpdatedAt = @UpdatedAt
                  WHERE Id = @FolderId",
                new { FolderId = folderId, MemberCount = memberCount, UpdatedAt = DateTime.UtcNow })
                .ConfigureAwait(false);

            return true;
        }, cancellationToken).ConfigureAwait(false);
    }

    #endregion

    #region Folder Policies

    public async Task<List<OrganizationalFolderPolicy>> GetFolderPoliciesAsync(Guid folderId, CancellationToken cancellationToken = default)
    {
        return await ExecuteAsync<List<OrganizationalFolderPolicy>>(async conn =>
        {
            // Use multi-mapping JOIN to populate the Policy navigation property
            var results = await conn.QueryAsync<OrganizationalFolderPolicy, CompliancePolicy, OrganizationalFolderPolicy>(
                @"SELECT fp.*, p.*
                  FROM [OrganizationalFolderPolicies] fp
                  INNER JOIN [CompliancePolicies] p ON fp.PolicyId = p.Id
                  WHERE fp.FolderId = @FolderId AND fp.IsActive = 1",
                (fp, p) =>
                {
                    fp.Policy = p;
                    return fp;
                },
                new { FolderId = folderId },
                splitOn: "Id").ConfigureAwait(false);

            return results.ToList();
        }, cancellationToken).ConfigureAwait(false);
    }

    public async Task<bool> AttachPolicyToFolderAsync(Guid folderId, Guid policyId, string? appliedBy = null, bool inheritToChildren = true, CancellationToken cancellationToken = default)
    {
        return await ExecuteAsync<bool>(async conn =>
        {
            // Check if already exists
            var exists = await conn.ExecuteScalarAsync<int>(
                @"SELECT COUNT(*) FROM [OrganizationalFolderPolicies]
                  WHERE FolderId = @FolderId AND PolicyId = @PolicyId AND IsActive = 1",
                new { FolderId = folderId, PolicyId = policyId }).ConfigureAwait(false);

            if (exists > 0)
                return false;

            var folderPolicy = new OrganizationalFolderPolicy
            {
                Id = Guid.NewGuid(),
                FolderId = folderId,
                PolicyId = policyId,
                AppliedBy = appliedBy,
                InheritToChildren = inheritToChildren,
                AppliedAt = DateTime.UtcNow,
                IsActive = true
            };

            await conn.ExecuteAsync(
                @"INSERT INTO [OrganizationalFolderPolicies]
                    (Id, FolderId, PolicyId, InheritToChildren, AppliedAt, AppliedBy, IsActive)
                  VALUES
                    (@Id, @FolderId, @PolicyId, @InheritToChildren, @AppliedAt, @AppliedBy, @IsActive)",
                folderPolicy).ConfigureAwait(false);

            return true;
        }, cancellationToken).ConfigureAwait(false);
    }

    public async Task<bool> RemovePolicyFromFolderAsync(Guid folderId, Guid policyId, CancellationToken cancellationToken = default)
    {
        return await ExecuteAsync<bool>(async conn =>
        {
            // Find the active policy attachment
            var policy = await conn.QueryFirstOrDefaultAsync<OrganizationalFolderPolicy>(
                @"SELECT * FROM [OrganizationalFolderPolicies]
                  WHERE FolderId = @FolderId AND PolicyId = @PolicyId AND IsActive = 1",
                new { FolderId = folderId, PolicyId = policyId }).ConfigureAwait(false);

            if (policy == null)
                return false;

            // Soft delete
            await conn.ExecuteAsync(
                @"UPDATE [OrganizationalFolderPolicies]
                  SET IsActive = 0
                  WHERE Id = @Id",
                new { policy.Id }).ConfigureAwait(false);

            return true;
        }, cancellationToken).ConfigureAwait(false);
    }

    #endregion

    #region Search

    public async Task<List<Identity>> SearchOrganizationAsync(string searchTerm, int maxResults = 50, CancellationToken cancellationToken = default)
    {
        return await ExecuteAsync<List<Identity>>(async conn =>
        {
            var results = await conn.QueryAsync<Identity>(
                @"SELECT TOP (@MaxResults) * FROM [Identities]
                  WHERE DisplayName LIKE '%' + @SearchTerm + '%'
                     OR PrimaryEmail LIKE '%' + @SearchTerm + '%'
                     OR Department LIKE '%' + @SearchTerm + '%'
                  ORDER BY DisplayName",
                new { SearchTerm = searchTerm, MaxResults = maxResults }).ConfigureAwait(false);

            return results.ToList();
        }, cancellationToken).ConfigureAwait(false);
    }

    #endregion

    #region Private DTOs for Dapper Queries

    /// <summary>
    /// Lightweight DTO for identity statistics query - avoids loading full Identity model.
    /// </summary>
    private class IdentityStatRow
    {
        public string? Department { get; set; }
        public Guid? ManagerIdentityId { get; set; }
    }

    /// <summary>
    /// DTO for department GROUP BY count results.
    /// </summary>
    private class DepartmentCountRow
    {
        public string? Department { get; set; }
        public int Count { get; set; }
    }

    /// <summary>
    /// DTO for manager hierarchy queries - avoids loading full Identity model.
    /// </summary>
    private class ManagerRow
    {
        public Guid Id { get; set; }
        public string? DisplayName { get; set; }
        public string? JobTitle { get; set; }
        public string? PrimaryEmail { get; set; }
        public Guid? ManagerIdentityId { get; set; }
        public int DirectReportCount { get; set; }
    }

    /// <summary>
    /// DTO for manager report count GROUP BY results.
    /// </summary>
    private class ManagerReportCount
    {
        public Guid? ManagerId { get; set; }
        public int Count { get; set; }
    }

    #endregion
}
