using Dapper;
using DataAccessLibrary.Models;
using Logging;
using Microsoft.Extensions.Configuration;

namespace DataAccessLibrary.Repositories;

public class IntelligenceDataRepository : DapperRepositoryBase, IIntelligenceDataRepository
{
    private readonly DataAccessLibrary.Services.IObjectWriteBackService? _writeBackService;
    private readonly DataAccessLibrary.Services.IDirectoryWriteService? _directoryWriteService;

    public IntelligenceDataRepository(
        IConfiguration configuration,
        IGlobalLogger logger,
        DataAccessLibrary.Services.IObjectWriteBackService? writeBackService = null,
        DataAccessLibrary.Services.IDirectoryWriteService? directoryWriteService = null)
        : base(configuration, logger)
    {
        _writeBackService = writeBackService;
        _directoryWriteService = directoryWriteService;
    }

    public async Task<List<AdminAccountRecord>> GetAdminAccountsAsync()
    {
        const string sql = @"
            SELECT DISTINCT
                o.Id,
                o.Id as ObjectGuid,
                o.DisplayName,
                o.FirstName,
                o.LastName,
                o.Email,
                o.Username,
                TRY_CONVERT(DATETIME, lastLogon.AttributeValue) as LastLogon,
                o.IsActive,
                o.ObjectClass,
                DATEDIFF(DAY, COALESCE(TRY_CONVERT(DATETIME, lastLogon.AttributeValue), o.FirstSyncedAt), GETUTCDATE()) as DaysSinceLastLogin,
                dc.Name as DirectorySource
            FROM Objects o
            LEFT JOIN DirectoryConnections dc ON o.SourceConnectionId = dc.Id
            LEFT JOIN ObjectAttributes lastLogon ON o.Id = lastLogon.ObjectId AND lastLogon.AttributeName = 'lastLogonTimestamp'
            INNER JOIN ObjectGroupMemberships ogm ON o.Id = ogm.ObjectId
            INNER JOIN Objects g ON ogm.GroupId = g.Id
            WHERE o.ObjectClass = 'user'
                AND o.IsActive = 1
                AND (
                    g.DisplayName LIKE '%admin%'
                    OR g.DisplayName LIKE '%Domain Admins%'
                    OR g.DisplayName LIKE '%Enterprise Admins%'
                    OR g.DisplayName LIKE '%Administrators%'
                    OR g.DisplayName LIKE '%Schema Admins%'
                )
            ORDER BY DaysSinceLastLogin DESC";

        return await ExecuteAsync(async connection =>
        {
            var results = await connection.QueryAsync<AdminAccountRecord>(sql);
            return results.ToList();
        });
    }

    public async Task<AdminStatsRecord> GetAdminStatsAsync()
    {
        const string sql = @"
            SELECT
                COUNT(DISTINCT o.Id) as TotalAdmins,
                COUNT(DISTINCT CASE WHEN DATEDIFF(DAY, COALESCE(TRY_CONVERT(DATETIME, lastLogon.AttributeValue), o.FirstSyncedAt), GETUTCDATE()) >= 90 THEN o.Id END) as InactiveAdmins,
                0 as HighRiskAdmins,
                0 as CriticalAdmins
            FROM Objects o
            LEFT JOIN ObjectAttributes lastLogon ON o.Id = lastLogon.ObjectId AND lastLogon.AttributeName = 'lastLogonTimestamp'
            INNER JOIN ObjectGroupMemberships ogm ON o.Id = ogm.ObjectId
            INNER JOIN Objects g ON ogm.GroupId = g.Id
            WHERE o.ObjectClass = 'user'
                AND o.IsActive = 1
                AND (
                    g.DisplayName LIKE '%admin%'
                    OR g.DisplayName LIKE '%Domain Admins%'
                    OR g.DisplayName LIKE '%Enterprise Admins%'
                    OR g.DisplayName LIKE '%Administrators%'
                )";

        return await ExecuteAsync(async connection =>
            await connection.QueryFirstAsync<AdminStatsRecord>(sql));
    }

    public async Task<List<InactiveAccountRecord>> GetInactiveAccountsAsync(int inactiveDaysThreshold)
    {
        const string sql = @"
            SELECT
                io.Id,
                io.Id as ObjectGuid,
                io.DisplayName,
                io.FirstName,
                io.LastName,
                io.Email,
                io.Username,
                TRY_CONVERT(DATETIME, lastLogon.AttributeValue) as LastLogon,
                io.IsActive,
                io.ObjectClass,
                DATEDIFF(DAY, COALESCE(TRY_CONVERT(DATETIME, lastLogon.AttributeValue), io.FirstSyncedAt), GETUTCDATE()) as DaysSinceLastLogin,
                dc.Name as DirectorySource
            FROM Objects io
            LEFT JOIN DirectoryConnections dc ON io.SourceConnectionId = dc.Id
            LEFT JOIN ObjectAttributes lastLogon ON io.Id = lastLogon.ObjectId AND lastLogon.AttributeName = 'lastLogonTimestamp'
            WHERE io.ObjectClass = 'user'
                AND io.IsActive = 1
                AND (
                    lastLogon.AttributeValue IS NULL
                    OR DATEDIFF(DAY, TRY_CONVERT(DATETIME, lastLogon.AttributeValue), GETUTCDATE()) >= @Threshold
                )
            ORDER BY DaysSinceLastLogin DESC";

        return await ExecuteAsync(async connection =>
        {
            var results = await connection.QueryAsync<InactiveAccountRecord>(sql, new { Threshold = inactiveDaysThreshold });
            return results.ToList();
        });
    }

    public async Task<InactivityStatsRecord> GetInactivityStatsAsync()
    {
        const string sql = @"
            SELECT
                COUNT(CASE WHEN DATEDIFF(DAY, COALESCE(TRY_CONVERT(DATETIME, lastLogon.AttributeValue), o.FirstSyncedAt), GETUTCDATE()) >= 90 THEN 1 END) as Inactive90Days,
                COUNT(CASE WHEN DATEDIFF(DAY, COALESCE(TRY_CONVERT(DATETIME, lastLogon.AttributeValue), o.FirstSyncedAt), GETUTCDATE()) >= 180 THEN 1 END) as Inactive180Days,
                COUNT(CASE WHEN DATEDIFF(DAY, COALESCE(TRY_CONVERT(DATETIME, lastLogon.AttributeValue), o.FirstSyncedAt), GETUTCDATE()) >= 365 THEN 1 END) as Inactive365Days,
                COUNT(CASE WHEN lastLogon.AttributeValue IS NULL THEN 1 END) as NeverLoggedIn,
                COUNT(*) as TotalActiveUsers
            FROM Objects o
            LEFT JOIN ObjectAttributes lastLogon ON o.Id = lastLogon.ObjectId AND lastLogon.AttributeName = 'lastLogonTimestamp'
            WHERE o.ObjectClass = 'user' AND o.IsActive = 1";

        return await ExecuteAsync(async connection =>
            await connection.QueryFirstAsync<InactivityStatsRecord>(sql));
    }

    public async Task<List<GroupInfoRecord>> GetAllGroupsWithMetadataAsync(CancellationToken cancellationToken = default)
    {
        const string sql = @"
            SELECT
                g.Id as GroupId,
                g.DisplayName as GroupName,
                descAttr.AttributeValue as Description,
                g.DN as DistinguishedName,
                COUNT(DISTINCT ogm.ObjectId) as MemberCount,
                g.FirstSyncedAt as CreatedDate,
                g.LastSyncedAt as ModifiedDate,
                managedBy.AttributeValue as ManagedBy,
                dc.Name as DirectorySource
            FROM Objects g
            LEFT JOIN ObjectGroupMemberships ogm ON g.Id = ogm.GroupId
            LEFT JOIN ObjectAttributes managedBy ON g.Id = managedBy.ObjectId AND managedBy.AttributeName = 'managedBy'
            LEFT JOIN ObjectAttributes descAttr ON g.Id = descAttr.ObjectId AND descAttr.AttributeName = 'description'
            LEFT JOIN DirectoryConnections dc ON g.SourceConnectionId = dc.Id
            WHERE g.ObjectClass = 'group'
                AND g.IsActive = 1
            GROUP BY
                g.Id, g.DisplayName, descAttr.AttributeValue, g.DN,
                g.FirstSyncedAt, g.LastSyncedAt, managedBy.AttributeValue, dc.Name
            ORDER BY g.DisplayName";

        return await ExecuteAsync(async connection =>
        {
            var results = await connection.QueryAsync<GroupInfoRecord>(
                new CommandDefinition(sql, commandTimeout: 60, cancellationToken: cancellationToken));
            return results.ToList();
        });
    }

    public async Task<int> GetOrphanedMemberCountAsync(Guid groupId, CancellationToken cancellationToken = default)
    {
        const string sql = @"
            SELECT COUNT(*)
            FROM ObjectGroupMemberships ogm
            INNER JOIN Objects o ON ogm.ObjectId = o.Id
            WHERE ogm.GroupId = @GroupId
                AND o.ObjectClass = 'user'
                AND o.IsActive = 0";

        return await ExecuteAsync(async connection =>
            await connection.ExecuteScalarAsync<int>(
                new CommandDefinition(sql, new { GroupId = groupId }, commandTimeout: 30, cancellationToken: cancellationToken)));
    }

    public async Task<List<RedundantGroupRecord>> GetRedundantGroupsAsync(CancellationToken cancellationToken = default)
    {
        // SQL Server 2016 compatible version (no STRING_AGG)
        // Uses CHECKSUM_AGG on member IDs to create a signature for comparison
        const string sql = @"
            WITH GroupMemberSignatures AS (
                SELECT
                    g.Id as GroupId,
                    g.DisplayName as GroupName,
                    COUNT(DISTINCT ogm.ObjectId) as MemberCount,
                    -- Use CHECKSUM_AGG as a signature (works in SQL Server 2016)
                    CHECKSUM_AGG(CHECKSUM(ogm.ObjectId)) as MemberSignature
                FROM Objects g
                LEFT JOIN ObjectGroupMemberships ogm ON g.Id = ogm.GroupId
                WHERE g.ObjectClass = 'group'
                    AND g.IsActive = 1
                GROUP BY g.Id, g.DisplayName
            )
            SELECT
                gms1.GroupId,
                gms1.GroupName,
                gms1.MemberCount,
                gms2.GroupName as RedundantWith
            FROM GroupMemberSignatures gms1
            INNER JOIN GroupMemberSignatures gms2
                ON gms1.MemberSignature = gms2.MemberSignature
                AND gms1.MemberCount = gms2.MemberCount
                AND gms1.GroupId < gms2.GroupId
                AND gms1.MemberCount > 0
            ORDER BY gms1.GroupName";

        return await ExecuteAsync(async connection =>
        {
            var results = await connection.QueryAsync<RedundantGroupRecord>(
                new CommandDefinition(sql, commandTimeout: 60, cancellationToken: cancellationToken));
            return results.ToList();
        });
    }

    public async Task<GroupStatsRecord> GetGroupStatsAsync(CancellationToken cancellationToken = default)
    {
        const string sql = @"
            SELECT
                COUNT(*) as TotalGroups,
                COUNT(CASE WHEN MemberCount = 0 THEN 1 END) as EmptyGroups,
                COUNT(CASE WHEN MemberCount = 1 THEN 1 END) as SingleMemberGroups,
                COUNT(CASE WHEN DATEDIFF(DAY, g.LastSyncedAt, GETUTCDATE()) >= 365 THEN 1 END) as StaleGroups,
                COUNT(CASE WHEN managedBy.AttributeValue IS NULL THEN 1 END) as GroupsWithNoManager
            FROM Objects g
            LEFT JOIN (
                SELECT GroupId, COUNT(*) as MemberCount
                FROM ObjectGroupMemberships
                GROUP BY GroupId
            ) memberCounts ON g.Id = memberCounts.GroupId
            LEFT JOIN ObjectAttributes managedBy ON g.Id = managedBy.ObjectId AND managedBy.AttributeName = 'managedBy'
            WHERE g.ObjectClass = 'group'
                AND g.IsActive = 1";

        return await ExecuteAsync(async connection =>
            await connection.QueryFirstAsync<GroupStatsRecord>(
                new CommandDefinition(sql, commandTimeout: 30, cancellationToken: cancellationToken)));
    }

    public async Task<List<OrgUserRecord>> GetAllUsersWithOrgDataAsync(CancellationToken cancellationToken = default)
    {
        const string sql = @"
            SELECT
                COALESCE(i.Id, u.Id) as UserId,
                u.Id as ObjectId,
                u.Username as UserName,
                COALESCE(i.DisplayName, u.DisplayName) as DisplayName,
                COALESCE(i.PrimaryEmail, u.Email) as Email,
                dept.AttributeValue as Department,
                title.AttributeValue as Title,
                u.IsActive,
                dc.Name as DirectorySource,
                i.ManagerIdentityId as ManagerId,
                managerIdentity.DisplayName as ManagerName,
                managerIdentity.IsActive as ManagerIsActive,
                (SELECT COUNT(*)
                 FROM Identities directReport
                 WHERE directReport.ManagerIdentityId = i.Id
                     AND directReport.IsActive = 1) as DirectReportCount
            FROM Objects u
            LEFT JOIN Identities i ON u.IdentityId = i.Id
            LEFT JOIN Identities managerIdentity ON i.ManagerIdentityId = managerIdentity.Id
            LEFT JOIN ObjectAttributes dept ON u.Id = dept.ObjectId AND dept.AttributeName = 'department'
            LEFT JOIN ObjectAttributes title ON u.Id = title.ObjectId AND title.AttributeName = 'title'
            LEFT JOIN DirectoryConnections dc ON u.SourceConnectionId = dc.Id
            WHERE u.ObjectClass = 'user'
                AND u.IsActive = 1
            ORDER BY COALESCE(i.DisplayName, u.DisplayName)";

        return await ExecuteAsync(async connection =>
        {
            var results = await connection.QueryAsync<OrgUserRecord>(
                new CommandDefinition(sql, commandTimeout: 60, cancellationToken: cancellationToken));
            return results.ToList();
        });
    }

    public async Task<List<CircularChainRecord>> GetCircularManagerChainsAsync(CancellationToken cancellationToken = default)
    {
        const string sql = @"
            WITH ManagerHierarchy AS (
                SELECT
                    u.Id as UserId,
                    u.ManagerObjectId as ManagerId,
                    CAST(u.Id AS NVARCHAR(MAX)) as HierarchyPath,
                    0 as Level
                FROM Objects u
                WHERE u.ObjectClass = 'user'
                    AND u.IsActive = 1
                    AND u.ManagerObjectId IS NOT NULL

                UNION ALL

                SELECT
                    mh.UserId,
                    mgr.ManagerObjectId as ManagerId,
                    CAST(mh.HierarchyPath + ',' + CAST(mgr.Id AS NVARCHAR(36)) AS NVARCHAR(MAX)),
                    mh.Level + 1
                FROM ManagerHierarchy mh
                INNER JOIN Objects mgr
                    ON mh.ManagerId = mgr.Id
                    AND mgr.ObjectClass = 'user'
                WHERE mh.Level < 20
                    AND mh.HierarchyPath NOT LIKE '%' + CAST(mgr.Id AS NVARCHAR(36)) + '%'
                    AND mgr.ManagerObjectId IS NOT NULL
            )
            SELECT DISTINCT
                mh.UserId,
                u.DisplayName,
                mh.ManagerId,
                mgr.DisplayName as ManagerName,
                mh.Level as ChainLength
            FROM ManagerHierarchy mh
            INNER JOIN Objects u ON mh.UserId = u.Id
            LEFT JOIN Objects mgr ON mh.ManagerId = mgr.Id
            WHERE mh.UserId = mh.ManagerId
                OR mh.HierarchyPath LIKE '%' + CAST(mh.UserId AS NVARCHAR(36)) + '%,' + CAST(mh.ManagerId AS NVARCHAR(36)) + '%'
            ORDER BY mh.Level DESC";

        return await ExecuteAsync(async connection =>
        {
            var results = await connection.QueryAsync<CircularChainRecord>(
                new CommandDefinition(sql, commandTimeout: 60, cancellationToken: cancellationToken));
            return results.ToList();
        });
    }

    public async Task<OrgStatsRecord> GetOrganizationStatsAsync(CancellationToken cancellationToken = default)
    {
        const string sql = @"
            SELECT
                COUNT(*) as TotalUsers,
                COUNT(CASE WHEN u.ManagerObjectId IS NOT NULL THEN 1 END) as UsersWithManager,
                COUNT(CASE WHEN u.ManagerObjectId IS NULL THEN 1 END) as UsersWithoutManager,
                COUNT(CASE WHEN dept.AttributeValue IS NOT NULL THEN 1 END) as UsersWithDepartment,
                COUNT(CASE WHEN title.AttributeValue IS NOT NULL THEN 1 END) as UsersWithTitle,
                COUNT(DISTINCT u.ManagerObjectId) as TotalManagers
            FROM Objects u
            LEFT JOIN ObjectAttributes dept ON u.Id = dept.ObjectId AND dept.AttributeName = 'department'
            LEFT JOIN ObjectAttributes title ON u.Id = title.ObjectId AND title.AttributeName = 'title'
            WHERE u.ObjectClass = 'user'
                AND u.IsActive = 1";

        return await ExecuteAsync(async connection =>
            await connection.QueryFirstAsync<OrgStatsRecord>(
                new CommandDefinition(sql, commandTimeout: 30, cancellationToken: cancellationToken)));
    }

    public async Task<double> GetAverageDirectReportsAsync(CancellationToken cancellationToken = default)
    {
        const string sql = @"
            SELECT AVG(CAST(DirectReportCount AS FLOAT)) as AverageDirectReports
            FROM (
                SELECT COUNT(*) as DirectReportCount
                FROM Objects
                WHERE ManagerObjectId IS NOT NULL
                    AND ObjectClass = 'user'
                    AND IsActive = 1
                GROUP BY ManagerObjectId
            ) subquery";

        return await ExecuteAsync(async connection =>
            await connection.ExecuteScalarAsync<double?>(
                new CommandDefinition(sql, commandTimeout: 30, cancellationToken: cancellationToken)) ?? 0);
    }

    public async Task<List<ManagerHierarchyRecord>> GetManagerHierarchyAsync(CancellationToken cancellationToken = default)
    {
        const string sql = @"
            SELECT
                mgrObj.Id as ManagerId,
                mgrObj.Username as ManagerName,
                mgrObj.DisplayName as ManagerDisplayName,
                dept.AttributeValue as Department,
                COUNT(DISTINCT u.Id) as DirectReportCount
            FROM Objects mgrObj
            LEFT JOIN Objects u
                ON u.ManagerObjectId = mgrObj.Id
                AND u.ObjectClass = 'user'
                AND u.IsActive = 1
            LEFT JOIN ObjectAttributes dept ON mgrObj.Id = dept.ObjectId AND dept.AttributeName = 'department'
            WHERE mgrObj.ObjectClass = 'user'
                AND mgrObj.IsActive = 1
            GROUP BY
                mgrObj.Id, mgrObj.Username, mgrObj.DisplayName, dept.AttributeValue
            HAVING COUNT(DISTINCT u.Id) > 0
            ORDER BY DirectReportCount DESC";

        return await ExecuteAsync(async connection =>
        {
            var results = await connection.QueryAsync<ManagerHierarchyRecord>(
                new CommandDefinition(sql, commandTimeout: 60, cancellationToken: cancellationToken));
            return results.ToList();
        });
    }

    // ========== BULK ISSUE DETECTION - PEOPLE/IDENTITY ==========

    public async Task<List<PersonWithIssueRecord>> GetPeopleWithoutManagersAsync(CancellationToken cancellationToken = default)
    {
        const string sql = @"
            SELECT
                i.Id,
                i.DisplayName,
                i.PrimaryEmail as Email,
                dept.AttributeValue as Department,
                title.AttributeValue as JobTitle,
                suggestedMgr.Id as SuggestedManagerId,
                suggestedMgr.DisplayName as SuggestedManagerName
            FROM Identities i
            LEFT JOIN Objects o ON o.IdentityId = i.Id
            LEFT JOIN ObjectAttributes dept ON o.Id = dept.ObjectId AND dept.AttributeName = 'department'
            LEFT JOIN ObjectAttributes title ON o.Id = title.ObjectId AND title.AttributeName = 'title'
            -- Suggest a manager from same department who has direct reports
            OUTER APPLY (
                SELECT TOP 1 m.Id, m.DisplayName
                FROM Identities m
                INNER JOIN Objects mo ON mo.IdentityId = m.Id
                INNER JOIN ObjectAttributes mDept ON mo.Id = mDept.ObjectId AND mDept.AttributeName = 'department'
                WHERE mDept.AttributeValue = dept.AttributeValue
                    AND m.Id != i.Id
                    AND m.IsActive = 1
                    AND EXISTS (SELECT 1 FROM Identities dr WHERE dr.ManagerIdentityId = m.Id)
                ORDER BY (SELECT COUNT(*) FROM Identities dr WHERE dr.ManagerIdentityId = m.Id) DESC
            ) suggestedMgr
            WHERE i.IsActive = 1
                AND i.ManagerIdentityId IS NULL
            ORDER BY i.DisplayName";

        return await ExecuteAsync(async connection =>
        {
            var results = await connection.QueryAsync<PersonWithIssueRecord>(
                new CommandDefinition(sql, commandTimeout: 60, cancellationToken: cancellationToken));
            return results.ToList();
        });
    }

    public async Task<List<PersonWithIssueRecord>> GetPeopleWithDisabledManagersAsync(CancellationToken cancellationToken = default)
    {
        const string sql = @"
            SELECT
                i.Id,
                i.DisplayName,
                i.PrimaryEmail as Email,
                dept.AttributeValue as Department,
                title.AttributeValue as JobTitle,
                -- Suggest the disabled manager's manager if available
                COALESCE(grandMgr.Id, suggestedMgr.Id) as SuggestedManagerId,
                COALESCE(grandMgr.DisplayName, suggestedMgr.DisplayName) as SuggestedManagerName,
                mgr.DisplayName as IssueDetail
            FROM Identities i
            INNER JOIN Identities mgr ON i.ManagerIdentityId = mgr.Id AND mgr.IsActive = 0
            LEFT JOIN Identities grandMgr ON mgr.ManagerIdentityId = grandMgr.Id AND grandMgr.IsActive = 1
            LEFT JOIN Objects o ON o.IdentityId = i.Id
            LEFT JOIN ObjectAttributes dept ON o.Id = dept.ObjectId AND dept.AttributeName = 'department'
            LEFT JOIN ObjectAttributes title ON o.Id = title.ObjectId AND title.AttributeName = 'title'
            -- Fallback: suggest manager from same department
            OUTER APPLY (
                SELECT TOP 1 m.Id, m.DisplayName
                FROM Identities m
                INNER JOIN Objects mo ON mo.IdentityId = m.Id
                INNER JOIN ObjectAttributes mDept ON mo.Id = mDept.ObjectId AND mDept.AttributeName = 'department'
                WHERE mDept.AttributeValue = dept.AttributeValue
                    AND m.Id != i.Id
                    AND m.IsActive = 1
                    AND EXISTS (SELECT 1 FROM Identities dr WHERE dr.ManagerIdentityId = m.Id)
            ) suggestedMgr
            WHERE i.IsActive = 1
            ORDER BY i.DisplayName";

        return await ExecuteAsync(async connection =>
        {
            var results = await connection.QueryAsync<PersonWithIssueRecord>(
                new CommandDefinition(sql, commandTimeout: 60, cancellationToken: cancellationToken));
            return results.ToList();
        });
    }

    public async Task<List<PersonWithIssueRecord>> GetPeopleWithMissingDepartmentAsync(CancellationToken cancellationToken = default)
    {
        const string sql = @"
            SELECT
                i.Id,
                i.DisplayName,
                i.PrimaryEmail as Email,
                NULL as Department,
                title.AttributeValue as JobTitle,
                NULL as SuggestedManagerId,
                NULL as SuggestedManagerName
            FROM Identities i
            LEFT JOIN Objects o ON o.IdentityId = i.Id
            LEFT JOIN ObjectAttributes dept ON o.Id = dept.ObjectId AND dept.AttributeName = 'department'
            LEFT JOIN ObjectAttributes title ON o.Id = title.ObjectId AND title.AttributeName = 'title'
            WHERE i.IsActive = 1
                AND (dept.AttributeValue IS NULL OR dept.AttributeValue = '')
            ORDER BY i.DisplayName";

        return await ExecuteAsync(async connection =>
        {
            var results = await connection.QueryAsync<PersonWithIssueRecord>(
                new CommandDefinition(sql, commandTimeout: 60, cancellationToken: cancellationToken));
            return results.ToList();
        });
    }

    public async Task<List<PersonWithIssueRecord>> GetPeopleWithMissingJobTitleAsync(CancellationToken cancellationToken = default)
    {
        const string sql = @"
            SELECT
                i.Id,
                i.DisplayName,
                i.PrimaryEmail as Email,
                dept.AttributeValue as Department,
                NULL as JobTitle,
                NULL as SuggestedManagerId,
                NULL as SuggestedManagerName
            FROM Identities i
            LEFT JOIN Objects o ON o.IdentityId = i.Id
            LEFT JOIN ObjectAttributes dept ON o.Id = dept.ObjectId AND dept.AttributeName = 'department'
            LEFT JOIN ObjectAttributes title ON o.Id = title.ObjectId AND title.AttributeName = 'title'
            WHERE i.IsActive = 1
                AND (title.AttributeValue IS NULL OR title.AttributeValue = '')
            ORDER BY i.DisplayName";

        return await ExecuteAsync(async connection =>
        {
            var results = await connection.QueryAsync<PersonWithIssueRecord>(
                new CommandDefinition(sql, commandTimeout: 60, cancellationToken: cancellationToken));
            return results.ToList();
        });
    }

    public async Task<List<PersonWithIssueRecord>> GetPeopleNeverLoggedInAsync(CancellationToken cancellationToken = default)
    {
        // Query Objects directly (not Identities) so demo/seeded data without person-matching still surfaces.
        const string sql = @"
            SELECT TOP 500
                o.Id,
                COALESCE(o.DisplayName, o.Username, o.CN) as DisplayName,
                o.Email as Email,
                dept.AttributeValue as Department,
                title.AttributeValue as JobTitle,
                CAST(NULL AS UNIQUEIDENTIFIER) as SuggestedManagerId,
                NULL as SuggestedManagerName,
                FORMAT(o.FirstSyncedAt, 'yyyy-MM-dd') as IssueDetail
            FROM Objects o
            LEFT JOIN ObjectAttributes lastLogon ON o.Id = lastLogon.ObjectId AND lastLogon.AttributeName = 'lastLogonTimestamp'
            LEFT JOIN ObjectAttributes dept ON o.Id = dept.ObjectId AND dept.AttributeName = 'department'
            LEFT JOIN ObjectAttributes title ON o.Id = title.ObjectId AND title.AttributeName = 'title'
            WHERE o.ObjectClass = 'user'
                AND o.IsActive = 1
                AND lastLogon.AttributeValue IS NULL
            ORDER BY o.FirstSyncedAt DESC";

        return await ExecuteAsync(async connection =>
        {
            var results = await connection.QueryAsync<PersonWithIssueRecord>(
                new CommandDefinition(sql, commandTimeout: 120, cancellationToken: cancellationToken));
            return results.ToList();
        });
    }

    // ========== BULK ISSUE DETECTION - GROUPS ==========

    public async Task<List<GroupWithIssueRecord>> GetGroupsWithoutOwnersAsync(CancellationToken cancellationToken = default)
    {
        // Checks both the managedBy attribute (AD) and OwnerIdentityId column (application-level ownership)
        const string sql = @"
            SELECT
                g.Id,
                g.DisplayName as Name,
                COUNT(DISTINCT ogm.ObjectId) as MemberCount,
                g.LastSyncedAt as LastModified,
                -- Suggest owner from top member by direct reports
                topMember.IdentityId as SuggestedOwnerId,
                topMember.DisplayName as SuggestedOwnerName
            FROM Objects g
            LEFT JOIN ObjectGroupMemberships ogm ON g.Id = ogm.GroupId
            LEFT JOIN ObjectAttributes managedBy ON g.Id = managedBy.ObjectId AND managedBy.AttributeName = 'managedBy'
            -- Find potential owner: top member with most direct reports
            OUTER APPLY (
                SELECT TOP 1 i.Id as IdentityId, i.DisplayName
                FROM ObjectGroupMemberships m
                INNER JOIN Objects o ON m.ObjectId = o.Id AND o.ObjectClass = 'user' AND o.IsActive = 1
                INNER JOIN Identities i ON o.IdentityId = i.Id AND i.IsActive = 1
                WHERE m.GroupId = g.Id
                ORDER BY (SELECT COUNT(*) FROM Identities dr WHERE dr.ManagerIdentityId = i.Id) DESC
            ) topMember
            WHERE g.ObjectClass = 'group'
                AND g.IsActive = 1
                AND (managedBy.AttributeValue IS NULL OR managedBy.AttributeValue = '')
                AND g.OwnerIdentityId IS NULL
            GROUP BY g.Id, g.DisplayName, g.LastSyncedAt, topMember.IdentityId, topMember.DisplayName
            ORDER BY COUNT(DISTINCT ogm.ObjectId) DESC";

        return await ExecuteAsync(async connection =>
        {
            var results = await connection.QueryAsync<GroupWithIssueRecord>(
                new CommandDefinition(sql, commandTimeout: 60, cancellationToken: cancellationToken));
            return results.ToList();
        });
    }

    public async Task<List<GroupWithIssueRecord>> GetEmptyGroupsAsync(CancellationToken cancellationToken = default)
    {
        const string sql = @"
            SELECT
                g.Id,
                g.DisplayName as Name,
                0 as MemberCount,
                g.LastSyncedAt as LastModified,
                NULL as SuggestedOwnerId,
                NULL as SuggestedOwnerName,
                DATEDIFF(DAY, g.FirstSyncedAt, GETUTCDATE()) as IssueDetail
            FROM Objects g
            LEFT JOIN ObjectGroupMemberships ogm ON g.Id = ogm.GroupId
            WHERE g.ObjectClass = 'group'
                AND g.IsActive = 1
            GROUP BY g.Id, g.DisplayName, g.LastSyncedAt, g.FirstSyncedAt
            HAVING COUNT(ogm.ObjectId) = 0
            ORDER BY g.DisplayName";

        return await ExecuteAsync(async connection =>
        {
            var results = await connection.QueryAsync<GroupWithIssueRecord>(
                new CommandDefinition(sql, commandTimeout: 60, cancellationToken: cancellationToken));
            return results.ToList();
        });
    }

    public async Task<List<GroupWithIssueRecord>> GetSingleMemberGroupsAsync(CancellationToken cancellationToken = default)
    {
        const string sql = @"
            SELECT
                g.Id,
                g.DisplayName as Name,
                1 as MemberCount,
                g.LastSyncedAt as LastModified,
                NULL as SuggestedOwnerId,
                singleMember.DisplayName as SuggestedOwnerName
            FROM Objects g
            INNER JOIN ObjectGroupMemberships ogm ON g.Id = ogm.GroupId
            -- Get the single member's name
            OUTER APPLY (
                SELECT TOP 1 o.DisplayName
                FROM ObjectGroupMemberships m
                INNER JOIN Objects o ON m.ObjectId = o.Id
                WHERE m.GroupId = g.Id
            ) singleMember
            WHERE g.ObjectClass = 'group'
                AND g.IsActive = 1
            GROUP BY g.Id, g.DisplayName, g.LastSyncedAt, singleMember.DisplayName
            HAVING COUNT(ogm.ObjectId) = 1
            ORDER BY g.DisplayName";

        return await ExecuteAsync(async connection =>
        {
            var results = await connection.QueryAsync<GroupWithIssueRecord>(
                new CommandDefinition(sql, commandTimeout: 60, cancellationToken: cancellationToken));
            return results.ToList();
        });
    }

    public async Task<List<GroupWithIssueRecord>> GetStaleGroupsAsync(int staleDaysThreshold = 365, CancellationToken cancellationToken = default)
    {
        const string sql = @"
            SELECT
                g.Id,
                g.DisplayName as Name,
                COUNT(DISTINCT ogm.ObjectId) as MemberCount,
                g.LastSyncedAt as LastModified,
                NULL as SuggestedOwnerId,
                NULL as SuggestedOwnerName,
                CAST(DATEDIFF(DAY, g.LastSyncedAt, GETUTCDATE()) AS NVARCHAR(10)) + ' days' as IssueDetail
            FROM Objects g
            LEFT JOIN ObjectGroupMemberships ogm ON g.Id = ogm.GroupId
            WHERE g.ObjectClass = 'group'
                AND g.IsActive = 1
                AND DATEDIFF(DAY, g.LastSyncedAt, GETUTCDATE()) >= @Threshold
            GROUP BY g.Id, g.DisplayName, g.LastSyncedAt
            ORDER BY g.LastSyncedAt ASC";

        return await ExecuteAsync(async connection =>
        {
            var results = await connection.QueryAsync<GroupWithIssueRecord>(
                new CommandDefinition(sql, new { Threshold = staleDaysThreshold }, commandTimeout: 60, cancellationToken: cancellationToken));
            return results.ToList();
        });
    }

    public async Task<List<GroupWithIssueRecord>> GetGroupsWithOrphanedMembersAsync(CancellationToken cancellationToken = default)
    {
        const string sql = @"
            SELECT
                g.Id,
                g.DisplayName as Name,
                orphanedCount.OrphanedCount as MemberCount,
                g.LastSyncedAt as LastModified,
                NULL as SuggestedOwnerId,
                NULL as SuggestedOwnerName,
                CAST(orphanedCount.OrphanedCount AS NVARCHAR(10)) + ' disabled members' as IssueDetail
            FROM Objects g
            CROSS APPLY (
                SELECT COUNT(*) as OrphanedCount
                FROM ObjectGroupMemberships ogm
                INNER JOIN Objects o ON ogm.ObjectId = o.Id
                WHERE ogm.GroupId = g.Id
                    AND o.ObjectClass = 'user'
                    AND o.IsActive = 0
            ) orphanedCount
            WHERE g.ObjectClass = 'group'
                AND g.IsActive = 1
                AND orphanedCount.OrphanedCount > 0
            ORDER BY orphanedCount.OrphanedCount DESC";

        return await ExecuteAsync(async connection =>
        {
            var results = await connection.QueryAsync<GroupWithIssueRecord>(
                new CommandDefinition(sql, commandTimeout: 60, cancellationToken: cancellationToken));
            return results.ToList();
        });
    }

    // ========== BULK ISSUE DETECTION - ACCOUNTS/OBJECTS ==========

    public async Task<List<ObjectWithIssueRecord>> GetAccountsWithPasswordNeverExpiresAsync(int? take = null, CancellationToken cancellationToken = default)
    {
        var sql = $@"
            SELECT {(take.HasValue ? $"TOP {take.Value}" : "")}
                o.Id,
                o.DisplayName,
                o.Username,
                dc.Name as SourceType,
                'Password never expires flag set' as IssueDetail,
                i.Id as LinkedIdentityId
            FROM Objects o
            INNER JOIN ObjectAttributes uac ON o.Id = uac.ObjectId AND uac.AttributeName = 'userAccountControl'
            LEFT JOIN DirectoryConnections dc ON o.SourceConnectionId = dc.Id
            LEFT JOIN Identities i ON o.IdentityId = i.Id
            WHERE o.ObjectClass = 'user'
                AND o.IsActive = 1
                AND TRY_CONVERT(INT, uac.AttributeValue) & 65536 = 65536  -- DONT_EXPIRE_PASSWORD flag
            ORDER BY o.DisplayName";

        return await ExecuteAsync(async connection =>
        {
            var results = await connection.QueryAsync<ObjectWithIssueRecord>(
                new CommandDefinition(sql, commandTimeout: 60, cancellationToken: cancellationToken));
            return results.ToList();
        });
    }

    public async Task<List<ObjectWithIssueRecord>> GetKerberoastableAccountsAsync(CancellationToken cancellationToken = default)
    {
        const string sql = @"
            SELECT
                o.Id,
                o.DisplayName,
                o.Username,
                dc.Name as SourceType,
                spn.AttributeValue as IssueDetail,
                i.Id as LinkedIdentityId
            FROM Objects o
            INNER JOIN ObjectAttributes spn ON o.Id = spn.ObjectId AND spn.AttributeName = 'servicePrincipalName'
            LEFT JOIN DirectoryConnections dc ON o.SourceConnectionId = dc.Id
            LEFT JOIN Identities i ON o.IdentityId = i.Id
            WHERE o.ObjectClass = 'user'
                AND o.IsActive = 1
                AND spn.AttributeValue IS NOT NULL
                AND spn.AttributeValue != ''
            ORDER BY o.DisplayName";

        return await ExecuteAsync(async connection =>
        {
            var results = await connection.QueryAsync<ObjectWithIssueRecord>(
                new CommandDefinition(sql, commandTimeout: 60, cancellationToken: cancellationToken));
            return results.ToList();
        });
    }

    public async Task<List<ObjectWithIssueRecord>> GetUnconstrainedDelegationAccountsAsync(CancellationToken cancellationToken = default)
    {
        const string sql = @"
            SELECT
                o.Id,
                o.DisplayName,
                o.Username,
                dc.Name as SourceType,
                'Trusted for delegation (unconstrained)' as IssueDetail,
                i.Id as LinkedIdentityId
            FROM Objects o
            INNER JOIN ObjectAttributes uac ON o.Id = uac.ObjectId AND uac.AttributeName = 'userAccountControl'
            LEFT JOIN DirectoryConnections dc ON o.SourceConnectionId = dc.Id
            LEFT JOIN Identities i ON o.IdentityId = i.Id
            WHERE o.ObjectClass = 'user'
                AND o.IsActive = 1
                AND TRY_CONVERT(INT, uac.AttributeValue) & 524288 = 524288  -- TRUSTED_FOR_DELEGATION flag
            ORDER BY o.DisplayName";

        return await ExecuteAsync(async connection =>
        {
            var results = await connection.QueryAsync<ObjectWithIssueRecord>(
                new CommandDefinition(sql, commandTimeout: 60, cancellationToken: cancellationToken));
            return results.ToList();
        });
    }

    public async Task<List<ObjectWithIssueRecord>> GetPrivilegedAccountsWithoutSensitiveFlagAsync(CancellationToken cancellationToken = default)
    {
        const string sql = @"
            SELECT DISTINCT
                o.Id,
                o.DisplayName,
                o.Username,
                dc.Name as SourceType,
                'Privileged account without sensitive flag' as IssueDetail,
                i.Id as LinkedIdentityId
            FROM Objects o
            INNER JOIN ObjectGroupMemberships ogm ON o.Id = ogm.ObjectId
            INNER JOIN Objects g ON ogm.GroupId = g.Id
            INNER JOIN ObjectAttributes uac ON o.Id = uac.ObjectId AND uac.AttributeName = 'userAccountControl'
            LEFT JOIN DirectoryConnections dc ON o.SourceConnectionId = dc.Id
            LEFT JOIN Identities i ON o.IdentityId = i.Id
            WHERE o.ObjectClass = 'user'
                AND o.IsActive = 1
                AND (
                    g.DisplayName LIKE '%admin%'
                    OR g.DisplayName LIKE '%Domain Admins%'
                    OR g.DisplayName LIKE '%Enterprise Admins%'
                    OR g.DisplayName LIKE '%Schema Admins%'
                )
                AND TRY_CONVERT(INT, uac.AttributeValue) & 1048576 = 0  -- NOT_DELEGATED flag NOT set
            ORDER BY o.DisplayName";

        return await ExecuteAsync(async connection =>
        {
            var results = await connection.QueryAsync<ObjectWithIssueRecord>(
                new CommandDefinition(sql, commandTimeout: 60, cancellationToken: cancellationToken));
            return results.ToList();
        });
    }

    public async Task<List<ObjectWithIssueRecord>> GetOrphanedAccountsAsync(CancellationToken cancellationToken = default)
    {
        const string sql = @"
            SELECT
                o.Id,
                o.DisplayName,
                o.Username,
                dc.Name as SourceType,
                'No linked identity' as IssueDetail,
                NULL as LinkedIdentityId
            FROM Objects o
            LEFT JOIN DirectoryConnections dc ON o.SourceConnectionId = dc.Id
            LEFT JOIN Identities i ON o.IdentityId = i.Id
            WHERE o.ObjectClass = 'user'
                AND o.IsActive = 1
                AND i.Id IS NULL
            ORDER BY o.DisplayName";

        return await ExecuteAsync(async connection =>
        {
            var results = await connection.QueryAsync<ObjectWithIssueRecord>(
                new CommandDefinition(sql, commandTimeout: 60, cancellationToken: cancellationToken));
            return results.ToList();
        });
    }

    public async Task<List<ObjectWithIssueRecord>> GetInactiveAccounts90DaysAsync(int? take = null, CancellationToken cancellationToken = default)
    {
        var sql = $@"
            SELECT {(take.HasValue ? $"TOP {take.Value}" : "")}
                o.Id,
                o.DisplayName,
                o.Username,
                dc.Name as SourceType,
                CAST(DATEDIFF(DAY, COALESCE(TRY_CONVERT(DATETIME, lastLogon.AttributeValue), o.FirstSyncedAt), GETUTCDATE()) AS NVARCHAR(10)) + ' days inactive' as IssueDetail,
                i.Id as LinkedIdentityId
            FROM Objects o
            LEFT JOIN ObjectAttributes lastLogon ON o.Id = lastLogon.ObjectId AND lastLogon.AttributeName = 'lastLogonTimestamp'
            LEFT JOIN DirectoryConnections dc ON o.SourceConnectionId = dc.Id
            LEFT JOIN Identities i ON o.IdentityId = i.Id
            WHERE o.ObjectClass = 'user'
                AND o.IsActive = 1
                AND DATEDIFF(DAY, COALESCE(TRY_CONVERT(DATETIME, lastLogon.AttributeValue), o.FirstSyncedAt), GETUTCDATE()) >= 90
                AND DATEDIFF(DAY, COALESCE(TRY_CONVERT(DATETIME, lastLogon.AttributeValue), o.FirstSyncedAt), GETUTCDATE()) < 365
            ORDER BY DATEDIFF(DAY, COALESCE(TRY_CONVERT(DATETIME, lastLogon.AttributeValue), o.FirstSyncedAt), GETUTCDATE()) DESC";

        return await ExecuteAsync(async connection =>
        {
            var results = await connection.QueryAsync<ObjectWithIssueRecord>(
                new CommandDefinition(sql, commandTimeout: 60, cancellationToken: cancellationToken));
            return results.ToList();
        });
    }

    public async Task<List<ObjectWithIssueRecord>> GetInactiveAccounts365DaysAsync(CancellationToken cancellationToken = default)
    {
        const string sql = @"
            SELECT
                o.Id,
                o.DisplayName,
                o.Username,
                dc.Name as SourceType,
                CAST(DATEDIFF(DAY, COALESCE(TRY_CONVERT(DATETIME, lastLogon.AttributeValue), o.FirstSyncedAt), GETUTCDATE()) AS NVARCHAR(10)) + ' days inactive' as IssueDetail,
                i.Id as LinkedIdentityId
            FROM Objects o
            LEFT JOIN ObjectAttributes lastLogon ON o.Id = lastLogon.ObjectId AND lastLogon.AttributeName = 'lastLogonTimestamp'
            LEFT JOIN DirectoryConnections dc ON o.SourceConnectionId = dc.Id
            LEFT JOIN Identities i ON o.IdentityId = i.Id
            WHERE o.ObjectClass = 'user'
                AND o.IsActive = 1
                AND DATEDIFF(DAY, COALESCE(TRY_CONVERT(DATETIME, lastLogon.AttributeValue), o.FirstSyncedAt), GETUTCDATE()) >= 365
            ORDER BY DATEDIFF(DAY, COALESCE(TRY_CONVERT(DATETIME, lastLogon.AttributeValue), o.FirstSyncedAt), GETUTCDATE()) DESC";

        return await ExecuteAsync(async connection =>
        {
            var results = await connection.QueryAsync<ObjectWithIssueRecord>(
                new CommandDefinition(sql, commandTimeout: 60, cancellationToken: cancellationToken));
            return results.ToList();
        });
    }

    // ========== BULK FIX OPERATIONS ==========

    public async Task<int> AssignManagerAsync(Guid userId, Guid managerId, CancellationToken cancellationToken = default)
    {
        const string sql = @"
            UPDATE Identities
            SET ManagerIdentityId = @ManagerId
            WHERE Id = @UserId";

        return await ExecuteAsync(async connection =>
            await connection.ExecuteAsync(
                new CommandDefinition(sql, new { UserId = userId, ManagerId = managerId }, commandTimeout: 30, cancellationToken: cancellationToken)));
    }

    public async Task<int> AssignGroupOwnerAsync(Guid groupId, Guid ownerId, CancellationToken cancellationToken = default)
    {
        const string sql = @"
            UPDATE Objects
            SET OwnerIdentityId = @OwnerId
            WHERE Id = @GroupId AND ObjectClass = 'group'";

        return await ExecuteAsync(async connection =>
            await connection.ExecuteAsync(
                new CommandDefinition(sql, new { GroupId = groupId, OwnerId = ownerId }, commandTimeout: 30, cancellationToken: cancellationToken)));
    }

    public async Task<int> AssignObjectManagerAsync(Guid objectId, Guid managerObjectId, CancellationToken cancellationToken = default)
    {
        if (_writeBackService != null)
        {
            // Look up the manager's DN for AD write-back
            var managerDn = await ExecuteAsync(async connection =>
                await connection.QueryFirstOrDefaultAsync<string>(
                    new CommandDefinition("SELECT DN FROM Objects WHERE Id = @Id",
                        new { Id = managerObjectId }, commandTimeout: 30, cancellationToken: cancellationToken)),
                cancellationToken);

            var caller = Services.WriteBackCallerContext.System("IntelligenceEngine");
            var result = await _writeBackService.SetObjectManagerAsync(objectId, managerDn, managerObjectId, "Intelligence", caller);
            return result.DatabaseUpdated ? 1 : 0;
        }

        // Fallback: DB-only
        const string sql = @"
            UPDATE Objects
            SET ManagerObjectId = @ManagerObjectId
            WHERE Id = @ObjectId";

        return await ExecuteAsync(async connection =>
            await connection.ExecuteAsync(
                new CommandDefinition(sql, new { ObjectId = objectId, ManagerObjectId = managerObjectId }, commandTimeout: 30, cancellationToken: cancellationToken)));
    }

    /// <summary>
    /// Syncs the ManagerObjectId on the person's authoritative Object to match the manager's authoritative Object.
    /// This prevents IdentityLinkerJob from clearing the ManagerIdentityId when it sees a mismatch.
    /// </summary>
    public async Task<int> SyncManagerToAuthoritativeObjectAsync(Guid personId, Guid managerIdentityId, CancellationToken cancellationToken = default)
    {
        // This query:
        // 1. Finds the person's authoritative Object
        // 2. Finds the manager's authoritative Object
        // 3. Sets the person's Object.ManagerObjectId to the manager's Object.Id
        const string sql = @"
            UPDATE personObj
            SET personObj.ManagerObjectId = managerObj.Id
            FROM Objects personObj
            INNER JOIN Objects managerObj ON managerObj.IdentityId = @ManagerIdentityId AND managerObj.IsAuthoritative = 1
            WHERE personObj.IdentityId = @PersonId
              AND personObj.IsAuthoritative = 1";

        return await ExecuteAsync(async connection =>
            await connection.ExecuteAsync(
                new CommandDefinition(sql, new { PersonId = personId, ManagerIdentityId = managerIdentityId }, commandTimeout: 30, cancellationToken: cancellationToken)));
    }

    // ============================================================================
    // PHASE 7: ROLLBACK SUPPORT - Methods with nullable parameters
    // ============================================================================

    /// <summary>
    /// Assigns a manager to a user with nullable support for rollback (clears manager if null)
    /// </summary>
    public async Task<int> AssignManagerAsync(Guid userId, Guid? managerId, CancellationToken cancellationToken = default)
    {
        const string sql = @"
            UPDATE Identities
            SET ManagerIdentityId = @ManagerId
            WHERE Id = @UserId";

        return await ExecuteAsync(async connection =>
            await connection.ExecuteAsync(
                new CommandDefinition(sql, new { UserId = userId, ManagerId = managerId }, commandTimeout: 30, cancellationToken: cancellationToken)));
    }

    /// <summary>
    /// Assigns an owner to a group with nullable support for rollback (clears owner if null)
    /// </summary>
    public async Task<int> AssignGroupOwnerAsync(Guid groupId, Guid? ownerId, CancellationToken cancellationToken = default)
    {
        const string sql = @"
            UPDATE Objects
            SET OwnerIdentityId = @OwnerId
            WHERE Id = @GroupId AND ObjectClass = 'group'";

        return await ExecuteAsync(async connection =>
            await connection.ExecuteAsync(
                new CommandDefinition(sql, new { GroupId = groupId, OwnerId = ownerId }, commandTimeout: 30, cancellationToken: cancellationToken)));
    }

    /// <summary>
    /// Sets the enabled status of an object with write-back to the target directory.
    /// </summary>
    public async Task<int> SetObjectEnabledAsync(Guid objectId, bool isEnabled, CancellationToken cancellationToken = default)
    {
        if (_writeBackService != null)
        {
            var caller = Services.WriteBackCallerContext.System("IntelligenceEngine");
            var result = await _writeBackService.SetObjectEnabledAsync(objectId, isEnabled, "Intelligence", caller);
            return result.DatabaseUpdated ? 1 : 0;
        }

        // Fallback: DB-only
        const string sql = @"
            UPDATE Objects
            SET IsActive = @IsEnabled
            WHERE Id = @ObjectId";

        return await ExecuteAsync(async connection =>
            await connection.ExecuteAsync(
                new CommandDefinition(sql, new { ObjectId = objectId, IsEnabled = isEnabled }, commandTimeout: 30, cancellationToken: cancellationToken)));
    }

    /// <summary>
    /// Gets the current manager for an identity (for change tracking)
    /// </summary>
    public async Task<Guid?> GetCurrentManagerAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        const string sql = @"
            SELECT ManagerIdentityId
            FROM Identities
            WHERE Id = @UserId";

        return await ExecuteAsync(async connection =>
            await connection.QuerySingleOrDefaultAsync<Guid?>(
                new CommandDefinition(sql, new { UserId = userId }, commandTimeout: 30, cancellationToken: cancellationToken)));
    }

    /// <summary>
    /// Gets the current owner for a group (for change tracking)
    /// </summary>
    public async Task<Guid?> GetCurrentGroupOwnerAsync(Guid groupId, CancellationToken cancellationToken = default)
    {
        const string sql = @"
            SELECT OwnerIdentityId
            FROM Groups
            WHERE Id = @GroupId";

        return await ExecuteAsync(async connection =>
            await connection.QuerySingleOrDefaultAsync<Guid?>(
                new CommandDefinition(sql, new { GroupId = groupId }, commandTimeout: 30, cancellationToken: cancellationToken)));
    }

    // ============================================================================
    // COUNT-ONLY QUERIES - Fast dashboard loading (no OUTER APPLY, no suggestions)
    // ============================================================================

    #region People Count Queries

    public async Task<int> GetPeopleWithoutManagersCountAsync(CancellationToken cancellationToken = default)
    {
        const string sql = @"
            SELECT COUNT(*)
            FROM Identities i
            WHERE i.IsActive = 1
                AND i.ManagerIdentityId IS NULL";

        return await ExecuteAsync(async connection =>
            await connection.ExecuteScalarAsync<int>(
                new CommandDefinition(sql, commandTimeout: 30, cancellationToken: cancellationToken)));
    }

    public async Task<int> GetPeopleWithDisabledManagersCountAsync(CancellationToken cancellationToken = default)
    {
        const string sql = @"
            SELECT COUNT(*)
            FROM Identities i
            INNER JOIN Identities mgr ON i.ManagerIdentityId = mgr.Id
            WHERE i.IsActive = 1
                AND mgr.IsActive = 0";

        return await ExecuteAsync(async connection =>
            await connection.ExecuteScalarAsync<int>(
                new CommandDefinition(sql, commandTimeout: 30, cancellationToken: cancellationToken)));
    }

    public async Task<int> GetCircularManagerChainsCountAsync(CancellationToken cancellationToken = default)
    {
        const string sql = @"
            WITH ManagerHierarchy AS (
                SELECT
                    u.Id as UserId,
                    u.ManagerObjectId as ManagerId,
                    CAST(u.Id AS NVARCHAR(MAX)) as HierarchyPath,
                    0 as Level
                FROM Objects u
                WHERE u.ObjectClass = 'user'
                    AND u.IsActive = 1
                    AND u.ManagerObjectId IS NOT NULL

                UNION ALL

                SELECT
                    mh.UserId,
                    mgr.ManagerObjectId as ManagerId,
                    CAST(mh.HierarchyPath + ',' + CAST(mgr.Id AS NVARCHAR(36)) AS NVARCHAR(MAX)),
                    mh.Level + 1
                FROM ManagerHierarchy mh
                INNER JOIN Objects mgr ON mh.ManagerId = mgr.Id AND mgr.ObjectClass = 'user'
                WHERE mh.Level < 20
                    AND mh.HierarchyPath NOT LIKE '%' + CAST(mgr.Id AS NVARCHAR(36)) + '%'
                    AND mgr.ManagerObjectId IS NOT NULL
            )
            SELECT COUNT(DISTINCT mh.UserId)
            FROM ManagerHierarchy mh
            WHERE mh.UserId = mh.ManagerId
                OR mh.HierarchyPath LIKE '%' + CAST(mh.UserId AS NVARCHAR(36)) + '%,' + CAST(mh.ManagerId AS NVARCHAR(36)) + '%'";

        return await ExecuteAsync(async connection =>
            await connection.ExecuteScalarAsync<int>(
                new CommandDefinition(sql, commandTimeout: 60, cancellationToken: cancellationToken)));
    }

    public async Task<int> GetPeopleWithMissingDepartmentCountAsync(CancellationToken cancellationToken = default)
    {
        const string sql = @"
            SELECT COUNT(*)
            FROM Identities i
            WHERE i.IsActive = 1
                AND (i.Department IS NULL OR i.Department = '')";

        return await ExecuteAsync(async connection =>
            await connection.ExecuteScalarAsync<int>(
                new CommandDefinition(sql, commandTimeout: 30, cancellationToken: cancellationToken)));
    }

    public async Task<int> GetPeopleWithMissingJobTitleCountAsync(CancellationToken cancellationToken = default)
    {
        const string sql = @"
            SELECT COUNT(*)
            FROM Identities i
            WHERE i.IsActive = 1
                AND (i.JobTitle IS NULL OR i.JobTitle = '')";

        return await ExecuteAsync(async connection =>
            await connection.ExecuteScalarAsync<int>(
                new CommandDefinition(sql, commandTimeout: 30, cancellationToken: cancellationToken)));
    }

    public async Task<int> GetPeopleNeverLoggedInCountAsync(CancellationToken cancellationToken = default)
    {
        const string sql = @"
            SELECT COUNT(*)
            FROM Objects o
            LEFT JOIN ObjectAttributes lastLogon ON o.Id = lastLogon.ObjectId AND lastLogon.AttributeName = 'lastLogonTimestamp'
            WHERE o.ObjectClass = 'user'
                AND o.IsActive = 1
                AND lastLogon.AttributeValue IS NULL";

        return await ExecuteAsync(async connection =>
            await connection.ExecuteScalarAsync<int>(
                new CommandDefinition(sql, commandTimeout: 30, cancellationToken: cancellationToken)));
    }

    #endregion

    #region Group Count Queries

    public async Task<int> GetGroupsWithoutOwnersCountAsync(CancellationToken cancellationToken = default)
    {
        const string sql = @"
            SELECT COUNT(*)
            FROM Objects g
            LEFT JOIN ObjectAttributes managedBy ON g.Id = managedBy.ObjectId AND managedBy.AttributeName = 'managedBy'
            WHERE g.ObjectClass = 'group'
                AND g.IsActive = 1
                AND (managedBy.AttributeValue IS NULL OR managedBy.AttributeValue = '')
                AND g.OwnerIdentityId IS NULL";

        return await ExecuteAsync(async connection =>
            await connection.ExecuteScalarAsync<int>(
                new CommandDefinition(sql, commandTimeout: 30, cancellationToken: cancellationToken)));
    }

    public async Task<int> GetEmptyGroupsCountAsync(CancellationToken cancellationToken = default)
    {
        const string sql = @"
            SELECT COUNT(*)
            FROM (
                SELECT g.Id
                FROM Objects g
                LEFT JOIN ObjectGroupMemberships ogm ON g.Id = ogm.GroupId
                WHERE g.ObjectClass = 'group'
                    AND g.IsActive = 1
                GROUP BY g.Id
                HAVING COUNT(ogm.ObjectId) = 0
            ) emptyGroups";

        return await ExecuteAsync(async connection =>
            await connection.ExecuteScalarAsync<int>(
                new CommandDefinition(sql, commandTimeout: 30, cancellationToken: cancellationToken)));
    }

    public async Task<int> GetSingleMemberGroupsCountAsync(CancellationToken cancellationToken = default)
    {
        const string sql = @"
            SELECT COUNT(*)
            FROM (
                SELECT g.Id
                FROM Objects g
                LEFT JOIN ObjectGroupMemberships ogm ON g.Id = ogm.GroupId
                WHERE g.ObjectClass = 'group'
                    AND g.IsActive = 1
                GROUP BY g.Id
                HAVING COUNT(ogm.ObjectId) = 1
            ) subquery";

        return await ExecuteAsync(async connection =>
            await connection.ExecuteScalarAsync<int>(
                new CommandDefinition(sql, commandTimeout: 30, cancellationToken: cancellationToken)));
    }

    public async Task<int> GetStaleGroupsCountAsync(int staleDaysThreshold = 365, CancellationToken cancellationToken = default)
    {
        const string sql = @"
            SELECT COUNT(*)
            FROM Objects g
            WHERE g.ObjectClass = 'group'
                AND g.IsActive = 1
                AND (g.LastSyncedAt IS NULL OR DATEDIFF(DAY, g.LastSyncedAt, GETUTCDATE()) > @StaleDays)";

        return await ExecuteAsync(async connection =>
            await connection.ExecuteScalarAsync<int>(
                new CommandDefinition(sql, new { StaleDays = staleDaysThreshold }, commandTimeout: 30, cancellationToken: cancellationToken)));
    }

    public async Task<int> GetGroupsWithOrphanedMembersCountAsync(CancellationToken cancellationToken = default)
    {
        const string sql = @"
            SELECT COUNT(DISTINCT g.Id)
            FROM Objects g
            INNER JOIN ObjectGroupMemberships ogm ON g.Id = ogm.GroupId
            INNER JOIN Objects o ON ogm.ObjectId = o.Id
            WHERE g.ObjectClass = 'group'
                AND g.IsActive = 1
                AND o.ObjectClass = 'user'
                AND o.IsActive = 0";

        return await ExecuteAsync(async connection =>
            await connection.ExecuteScalarAsync<int>(
                new CommandDefinition(sql, commandTimeout: 30, cancellationToken: cancellationToken)));
    }

    public async Task<int> GetRedundantGroupsCountAsync(CancellationToken cancellationToken = default)
    {
        const string sql = @"
            WITH GroupMemberSignatures AS (
                SELECT
                    g.Id as GroupId,
                    COUNT(DISTINCT ogm.ObjectId) as MemberCount,
                    CHECKSUM_AGG(CHECKSUM(ogm.ObjectId)) as MemberSignature
                FROM Objects g
                LEFT JOIN ObjectGroupMemberships ogm ON g.Id = ogm.GroupId
                WHERE g.ObjectClass = 'group'
                    AND g.IsActive = 1
                GROUP BY g.Id
            )
            SELECT COUNT(DISTINCT gms1.GroupId)
            FROM GroupMemberSignatures gms1
            INNER JOIN GroupMemberSignatures gms2
                ON gms1.MemberSignature = gms2.MemberSignature
                AND gms1.MemberCount = gms2.MemberCount
                AND gms1.GroupId < gms2.GroupId
                AND gms1.MemberCount > 0";

        return await ExecuteAsync(async connection =>
            await connection.ExecuteScalarAsync<int>(
                new CommandDefinition(sql, commandTimeout: 60, cancellationToken: cancellationToken)));
    }

    #endregion

    #region Account Count Queries

    public async Task<int> GetAccountsWithPasswordNeverExpiresCountAsync(CancellationToken cancellationToken = default)
    {
        const string sql = @"
            SELECT COUNT(*)
            FROM Objects o
            WHERE o.ObjectClass = 'user'
                AND o.IsActive = 1
                AND o.PasswordNeverExpires = 1";

        return await ExecuteAsync(async connection =>
            await connection.ExecuteScalarAsync<int>(
                new CommandDefinition(sql, commandTimeout: 30, cancellationToken: cancellationToken)));
    }

    public async Task<int> GetKerberoastableAccountsCountAsync(CancellationToken cancellationToken = default)
    {
        const string sql = @"
            SELECT COUNT(DISTINCT o.Id)
            FROM Objects o
            INNER JOIN ObjectAttributes spn ON o.Id = spn.ObjectId AND spn.AttributeName = 'servicePrincipalName'
            WHERE o.ObjectClass = 'user'
                AND o.IsActive = 1
                AND spn.AttributeValue IS NOT NULL
                AND spn.AttributeValue != ''";

        return await ExecuteAsync(async connection =>
            await connection.ExecuteScalarAsync<int>(
                new CommandDefinition(sql, commandTimeout: 30, cancellationToken: cancellationToken)));
    }

    public async Task<int> GetUnconstrainedDelegationAccountsCountAsync(CancellationToken cancellationToken = default)
    {
        const string sql = @"
            SELECT COUNT(*)
            FROM Objects o
            INNER JOIN ObjectAttributes uac ON o.Id = uac.ObjectId AND uac.AttributeName = 'userAccountControl'
            WHERE o.ObjectClass = 'user'
                AND o.IsActive = 1
                AND TRY_CONVERT(INT, uac.AttributeValue) & 524288 = 524288";

        return await ExecuteAsync(async connection =>
            await connection.ExecuteScalarAsync<int>(
                new CommandDefinition(sql, commandTimeout: 30, cancellationToken: cancellationToken)));
    }

    public async Task<int> GetPrivilegedAccountsWithoutSensitiveFlagCountAsync(CancellationToken cancellationToken = default)
    {
        const string sql = @"
            SELECT COUNT(DISTINCT o.Id)
            FROM Objects o
            INNER JOIN ObjectGroupMemberships ogm ON o.Id = ogm.ObjectId
            INNER JOIN Objects g ON ogm.GroupId = g.Id
            LEFT JOIN ObjectAttributes uac ON o.Id = uac.ObjectId AND uac.AttributeName = 'userAccountControl'
            WHERE o.ObjectClass = 'user'
                AND o.IsActive = 1
                AND (g.DisplayName LIKE '%Admin%' OR g.DisplayName LIKE '%Domain Admins%' OR g.DisplayName LIKE '%Enterprise Admins%' OR g.DisplayName LIKE '%Schema Admins%')
                AND (TRY_CONVERT(INT, uac.AttributeValue) & 1048576 = 0 OR uac.AttributeValue IS NULL)";

        return await ExecuteAsync(async connection =>
            await connection.ExecuteScalarAsync<int>(
                new CommandDefinition(sql, commandTimeout: 30, cancellationToken: cancellationToken)));
    }

    public async Task<int> GetOrphanedAccountsCountAsync(CancellationToken cancellationToken = default)
    {
        const string sql = @"
            SELECT COUNT(*)
            FROM Objects o
            WHERE o.ObjectClass = 'user'
                AND o.IsActive = 1
                AND o.IdentityId IS NULL";

        return await ExecuteAsync(async connection =>
            await connection.ExecuteScalarAsync<int>(
                new CommandDefinition(sql, commandTimeout: 30, cancellationToken: cancellationToken)));
    }

    public async Task<int> GetInactiveAccounts90DaysCountAsync(CancellationToken cancellationToken = default)
    {
        const string sql = @"
            SELECT COUNT(*)
            FROM Objects o
            LEFT JOIN ObjectAttributes lastLogon ON o.Id = lastLogon.ObjectId AND lastLogon.AttributeName = 'lastLogonTimestamp'
            WHERE o.ObjectClass = 'user'
                AND o.IsActive = 1
                AND lastLogon.AttributeValue IS NOT NULL
                AND DATEDIFF(DAY, TRY_CONVERT(DATETIME, lastLogon.AttributeValue), GETUTCDATE()) > 90";

        return await ExecuteAsync(async connection =>
            await connection.ExecuteScalarAsync<int>(
                new CommandDefinition(sql, commandTimeout: 30, cancellationToken: cancellationToken)));
    }

    public async Task<int> GetInactiveAccounts365DaysCountAsync(CancellationToken cancellationToken = default)
    {
        const string sql = @"
            SELECT COUNT(*)
            FROM Objects o
            LEFT JOIN ObjectAttributes lastLogon ON o.Id = lastLogon.ObjectId AND lastLogon.AttributeName = 'lastLogonTimestamp'
            WHERE o.ObjectClass = 'user'
                AND o.IsActive = 1
                AND lastLogon.AttributeValue IS NOT NULL
                AND DATEDIFF(DAY, TRY_CONVERT(DATETIME, lastLogon.AttributeValue), GETUTCDATE()) > 365";

        return await ExecuteAsync(async connection =>
            await connection.ExecuteScalarAsync<int>(
                new CommandDefinition(sql, commandTimeout: 30, cancellationToken: cancellationToken)));
    }

    #endregion

    #region Dashboard Insight Counts

    public async Task<DashboardInsightCounts> GetDashboardInsightCountsAsync(CancellationToken cancellationToken = default)
    {
        return await ExecuteAsync(async connection =>
        {
            const string sql = @"
                SELECT
                    (SELECT COUNT(*) FROM SyncProjectRuns WHERE Status = 'Failed' AND StartedAt >= DATEADD(DAY, -1, GETUTCDATE())) AS FailedSyncsLast24h,
                    (SELECT COUNT(*) FROM SyncProjects WHERE IsEnabled = 1 AND (LastRunAt IS NULL OR LastRunAt < DATEADD(DAY, -1, GETUTCDATE()))) AS StaleSyncProjects,
                    (SELECT COUNT(*) FROM Objects WHERE ObjectClass = 'user' AND IsActive = 1 AND LastSeenAt IS NOT NULL AND LastSeenAt < DATEADD(DAY, -90, GETUTCDATE())) AS StaleAccounts90Days,
                    (SELECT COUNT(*) FROM Objects g WHERE g.ObjectClass = 'group' AND NOT EXISTS (SELECT 1 FROM ObjectGroupMemberships gm WHERE gm.GroupId = g.Id)) AS EmptyGroups,
                    (SELECT COUNT(*) FROM Objects WHERE ObjectClass = 'user' AND IsActive = 1 AND (ManagerSourceId IS NULL OR ManagerSourceId = '')) AS UsersWithoutManagers,
                    (SELECT COUNT(*) FROM Objects WHERE ObjectClass = 'user' AND IsActive = 1 AND PasswordNeverExpires = 1) AS PasswordNeverExpires,
                    (SELECT COUNT(*) FROM CompliancePolicyViolations WHERE Status IN ('Open', 'Pending')) AS ActiveComplianceViolations,
                    (SELECT COUNT(*) FROM CompliancePolicyViolations WHERE Status IN ('Open', 'Pending') AND Severity = 'Critical') AS CriticalComplianceViolations,
                    (SELECT COUNT(*) FROM AccessReviewAssignments WHERE Status = 'Pending' AND DueDate < GETUTCDATE()) AS OverdueReviews,
                    (SELECT COUNT(*) FROM SyncAuditLogs WHERE OperationType = 'Error' AND Timestamp >= DATEADD(DAY, -1, GETUTCDATE())) AS RecentErrors24h,
                    (SELECT COUNT(*) FROM Objects WHERE ObjectClass = 'group' AND OwnerObjectId IS NULL) AS GroupsWithoutOwners,
                    (SELECT COUNT(*) FROM Objects WHERE ObjectClass = 'user' AND IsActive = 1 AND Username IS NOT NULL AND (Username LIKE 'svc[_]%' OR Username LIKE 'svc-%' OR Username LIKE 'service[_]%' OR Username LIKE 'service-%' OR Username LIKE '%[_]svc%' OR Username LIKE '%-svc%') AND (LastSeenAt IS NULL OR LastSeenAt < DATEADD(DAY, -90, GETUTCDATE()))) AS StaleServiceAccounts90Days,
                    (SELECT COUNT(*) FROM Objects WHERE ObjectClass = 'user' AND IsActive = 1 AND IsAdminSDHolder = 1 AND (LastSeenAt IS NULL OR LastSeenAt < DATEADD(DAY, -30, GETUTCDATE()))) AS StalePrivilegedAccounts30Days,
                    (SELECT COUNT(*) FROM Objects WHERE ObjectClass = 'user' AND IsActive = 1 AND IsAdminSDHolder = 1 AND ManagerObjectId IS NULL) AS PrivilegedAccountsWithoutManager,
                    (SELECT COUNT(*) FROM Objects WHERE ObjectClass = 'user' AND UserAccountControl IS NOT NULL AND (UserAccountControl & 16) = 16) AS LockedOutAccounts";

            return await connection.QuerySingleAsync<DashboardInsightCounts>(
                new CommandDefinition(sql, commandTimeout: 60, cancellationToken: cancellationToken));
        }, cancellationToken);
    }

    public async Task<int> BulkDisableStaleAccountsAsync(int inactiveDays = 90, CancellationToken cancellationToken = default)
    {
        if (_writeBackService != null)
        {
            // Query affected object IDs first, then disable each via write-back (DB + AD)
            var staleIds = await ExecuteAsync(async connection =>
                (await connection.QueryAsync<Guid>(
                    new CommandDefinition(@"
                        SELECT Id FROM Objects
                        WHERE ObjectClass = 'user' AND IsActive = 1 AND LastSeenAt IS NOT NULL
                          AND LastSeenAt < DATEADD(DAY, -@Days, GETUTCDATE())",
                        new { Days = inactiveDays },
                        commandTimeout: 60,
                        cancellationToken: cancellationToken))).ToList(),
                cancellationToken);

            var caller = Services.WriteBackCallerContext.System("BulkStaleAccountDisable");
            int disabledCount = 0;
            foreach (var objectId in staleIds)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var result = await _writeBackService.SetObjectEnabledAsync(objectId, false, "Intelligence", caller);
                if (result.DatabaseUpdated) disabledCount++;
            }
            return disabledCount;
        }

        // Fallback: DB-only
        return await ExecuteAsync(async connection =>
        {
            return await connection.ExecuteAsync(
                new CommandDefinition(@"
                    UPDATE Objects SET IsActive = 0, LastSyncedAt = GETUTCDATE()
                    WHERE ObjectClass = 'user' AND IsActive = 1 AND LastSeenAt IS NOT NULL
                      AND LastSeenAt < DATEADD(DAY, -@Days, GETUTCDATE())",
                    new { Days = inactiveDays },
                    commandTimeout: 60,
                    cancellationToken: cancellationToken));
        }, cancellationToken);
    }

    public async Task<int> BulkDeleteEmptyGroupsAsync(CancellationToken cancellationToken = default)
    {
        return await ExecuteAsync(async connection =>
        {
            return await connection.ExecuteAsync(
                new CommandDefinition(@"
                    DELETE FROM Objects
                    WHERE ObjectClass = 'group'
                      AND NOT EXISTS (SELECT 1 FROM ObjectGroupMemberships gm WHERE gm.GroupId = Objects.Id)",
                    commandTimeout: 60,
                    cancellationToken: cancellationToken));
        }, cancellationToken);
    }

    public async Task<int> BulkEnforcePasswordExpiryAsync(CancellationToken cancellationToken = default)
    {
        if (_directoryWriteService != null)
        {
            // Query affected object IDs, then clear DONT_EXPIRE_PASSWD flag in AD + DB for each
            const int ADS_UF_DONT_EXPIRE_PASSWD = 0x10000;
            var affectedIds = await ExecuteAsync(async connection =>
                (await connection.QueryAsync<Guid>(
                    new CommandDefinition(@"
                        SELECT Id FROM Objects
                        WHERE ObjectClass = 'user' AND IsActive = 1 AND PasswordNeverExpires = 1",
                        commandTimeout: 60,
                        cancellationToken: cancellationToken))).ToList(),
                cancellationToken);

            int enforcedCount = 0;
            foreach (var objectId in affectedIds)
            {
                cancellationToken.ThrowIfCancellationRequested();
                // SetUacFlagsAsync handles both AD UAC update and DB field sync
                var success = await _directoryWriteService.SetUacFlagsAsync(objectId, 0, ADS_UF_DONT_EXPIRE_PASSWD);
                if (success) enforcedCount++;
            }
            return enforcedCount;
        }

        // Fallback: DB-only
        return await ExecuteAsync(async connection =>
        {
            return await connection.ExecuteAsync(
                new CommandDefinition(@"
                    UPDATE Objects SET PasswordNeverExpires = 0, LastSyncedAt = GETUTCDATE()
                    WHERE ObjectClass = 'user' AND IsActive = 1 AND PasswordNeverExpires = 1",
                    commandTimeout: 60,
                    cancellationToken: cancellationToken));
        }, cancellationToken);
    }

    public async Task<int> GetEnabledSyncProjectCountAsync(CancellationToken cancellationToken = default)
    {
        return await ExecuteAsync(async connection =>
        {
            return await connection.ExecuteScalarAsync<int>(
                new CommandDefinition("SELECT COUNT(*) FROM SyncProjects WHERE IsEnabled = 1",
                    commandTimeout: 30,
                    cancellationToken: cancellationToken));
        }, cancellationToken);
    }

    #endregion

    public async Task<PersonWithIssueRecord?> GetIdentityDisplayInfoAsync(Guid identityId, CancellationToken cancellationToken = default)
    {
        return await ExecuteAsync(async connection =>
        {
            return await connection.QueryFirstOrDefaultAsync<PersonWithIssueRecord>(
                new CommandDefinition(@"
                    SELECT Id, DisplayName, Email, Department, JobTitle
                    FROM Identities
                    WHERE Id = @IdentityId",
                    new { IdentityId = identityId },
                    commandTimeout: 30,
                    cancellationToken: cancellationToken));
        }, cancellationToken);
    }

    public async Task<Dictionary<Guid, PersonWithIssueRecord>> GetIdentitiesDisplayInfoBatchAsync(List<Guid> identityIds, CancellationToken cancellationToken = default)
    {
        if (identityIds.Count == 0) return new Dictionary<Guid, PersonWithIssueRecord>();

        return await ExecuteAsync(async connection =>
        {
            // Dapper expands @Ids into parameterized IN list automatically
            var results = await connection.QueryAsync<PersonWithIssueRecord>(@"
                    SELECT Id, DisplayName, Email, Department, JobTitle
                    FROM Identities
                    WHERE Id IN @Ids",
                    new { Ids = identityIds });
            return results.ToDictionary(r => r.Id);
        }, cancellationToken);
    }

    // ========== ChatHub TopIssues helpers (entity-level rows) ==========

    public async Task<List<TopStaleAccountRow>> GetTopStaleAccountsAsync(
        int n, int days, ScopeFilter? scope = null, CancellationToken cancellationToken = default)
    {
        var p = new DynamicParameters();
        p.Add("@N", n);
        p.Add("@Days", days);
        var scopeFragment = BuildScopeFragment(scope, p, objectAlias: "o", includeTagJoin: true);

        var sql = $@"
            SELECT TOP (@N)
                o.Id           AS ObjectId,
                o.DisplayName  AS DisplayName,
                o.Email        AS Email,
                DATEDIFF(DAY, COALESCE(TRY_CONVERT(DATETIME, lastLogon.AttributeValue), o.FirstSyncedAt), GETUTCDATE()) AS DaysSinceLastLogin,
                dc.Name        AS ConnectionName
            FROM   Objects o
            LEFT JOIN ObjectAttributes lastLogon
                   ON o.Id = lastLogon.ObjectId AND lastLogon.AttributeName = 'lastLogonTimestamp'
            LEFT JOIN DirectoryConnections dc ON o.SourceConnectionId = dc.Id
            WHERE  o.ObjectClass = 'user'
               AND o.IsActive = 1
               AND DATEDIFF(DAY, COALESCE(TRY_CONVERT(DATETIME, lastLogon.AttributeValue), o.FirstSyncedAt), GETUTCDATE()) >= @Days
               {scopeFragment}
            ORDER BY DATEDIFF(DAY, COALESCE(TRY_CONVERT(DATETIME, lastLogon.AttributeValue), o.FirstSyncedAt), GETUTCDATE()) DESC";

        return await ExecuteAsync(async connection =>
        {
            var rows = await connection.QueryAsync<TopStaleAccountRow>(
                new CommandDefinition(sql, p, commandTimeout: 30, cancellationToken: cancellationToken));
            return rows.ToList();
        }, cancellationToken);
    }

    public async Task<List<TopOwnerlessGroupRow>> GetTopOwnerlessGroupsAsync(
        int n, ScopeFilter? scope = null, CancellationToken cancellationToken = default)
    {
        var p = new DynamicParameters();
        p.Add("@N", n);
        var scopeFragment = BuildScopeFragment(scope, p, objectAlias: "g", includeTagJoin: true);

        var sql = $@"
            SELECT TOP (@N)
                g.Id                                         AS ObjectId,
                COALESCE(g.DisplayName, g.CN)                AS DisplayName,
                COUNT(DISTINCT ogm.ObjectId)                 AS MemberCount,
                dc.Name                                      AS ConnectionName
            FROM   Objects g
            LEFT JOIN ObjectGroupMemberships ogm
                   ON g.Id = ogm.GroupId AND ogm.IsActive = 1
            LEFT JOIN ObjectAttributes managedBy
                   ON g.Id = managedBy.ObjectId AND managedBy.AttributeName = 'managedBy'
            LEFT JOIN DirectoryConnections dc ON g.SourceConnectionId = dc.Id
            WHERE  g.ObjectClass = 'group'
               AND g.IsActive = 1
               AND (managedBy.AttributeValue IS NULL OR managedBy.AttributeValue = '')
               AND g.OwnerIdentityId IS NULL
               {scopeFragment}
            GROUP BY g.Id, g.DisplayName, g.CN, dc.Name
            ORDER BY COUNT(DISTINCT ogm.ObjectId) DESC";

        return await ExecuteAsync(async connection =>
        {
            var rows = await connection.QueryAsync<TopOwnerlessGroupRow>(
                new CommandDefinition(sql, p, commandTimeout: 30, cancellationToken: cancellationToken));
            return rows.ToList();
        }, cancellationToken);
    }

    /// <summary>
    /// Builds a parameterized scope-filter fragment from a typed ScopeFilter.
    /// Each predicate references a known column on the Objects table and binds
    /// the value through DynamicParameters — no caller-supplied SQL is interpolated.
    /// </summary>
    private static string BuildScopeFragment(ScopeFilter? scope, DynamicParameters p, string objectAlias, bool includeTagJoin)
    {
        if (scope == null) return string.Empty;
        var parts = new List<string>();
        if (scope.ConnectionId is Guid cid)
        {
            parts.Add($"{objectAlias}.SourceConnectionId = @ScopeConnectionId");
            p.Add("ScopeConnectionId", cid);
        }
        if (!string.IsNullOrWhiteSpace(scope.ObjectClass))
        {
            parts.Add($"{objectAlias}.ObjectClass = @ScopeObjectClass");
            p.Add("ScopeObjectClass", scope.ObjectClass);
        }
        if (!string.IsNullOrWhiteSpace(scope.OuPath))
        {
            // Match objects whose DN ends with the OU path (e.g. "OU=Sales,DC=corp,DC=local").
            parts.Add($"{objectAlias}.DN LIKE @ScopeOuPath");
            p.Add("ScopeOuPath", "%" + scope.OuPath + "%");
        }
        if (!string.IsNullOrWhiteSpace(scope.Department))
        {
            parts.Add($"{objectAlias}.Department = @ScopeDepartment");
            p.Add("ScopeDepartment", scope.Department);
        }
        if (includeTagJoin && scope.TagId is Guid tagId)
        {
            parts.Add($"EXISTS (SELECT 1 FROM ObjectTags ot WHERE ot.ObjectId = {objectAlias}.Id AND ot.TagId = @ScopeTagId)");
            p.Add("ScopeTagId", tagId);
        }
        return parts.Count == 0 ? string.Empty : " AND " + string.Join(" AND ", parts);
    }
}
