using Dapper;
using DataAccessLibrary.Models;
using Logging;
using Microsoft.Extensions.Configuration;

namespace DataAccessLibrary.Repositories;

/// <summary>
/// Dapper repository for ownership suggestion queries.
/// Joins ObjectGroupMemberships → Objects for department/manager analysis.
/// </summary>
public class OwnershipSuggestionRepository : DapperRepositoryBase, IOwnershipSuggestionRepository
{
    public OwnershipSuggestionRepository(IConfiguration configuration, IGlobalLogger logger)
        : base(configuration, logger)
    {
    }

    public async Task<List<GroupMemberOrgData>> GetGroupMembersWithOrgDataAsync(Guid groupId, CancellationToken ct = default)
    {
        return await ExecuteAsync(async connection =>
        {
            const string sql = @"
                SELECT
                    o.Id AS ObjectId,
                    o.DisplayName,
                    o.ManagerObjectId,
                    o.Department,
                    (SELECT COUNT(*) FROM Objects dr WHERE dr.ManagerObjectId = o.Id AND dr.IsActive = 1) AS DirectReportCount
                FROM ObjectGroupMemberships ogm
                INNER JOIN Objects o ON o.Id = ogm.ObjectId
                WHERE ogm.GroupId = @GroupId
                    AND o.ObjectClass = 'user'
                    AND o.IsActive = 1";

            var results = await connection.QueryAsync<GroupMemberOrgData>(sql, new { GroupId = groupId });
            return results.AsList();
        }, ct);
    }

    public async Task<List<OrphanedGroupSummary>> GetOrphanedGroupSummariesAsync(CancellationToken ct = default)
    {
        return await ExecuteAsync(async connection =>
        {
            const string sql = @"
                SELECT
                    g.Id AS GroupId,
                    g.DisplayName AS GroupName,
                    (SELECT COUNT(*) FROM ObjectGroupMemberships ogm WHERE ogm.GroupId = g.Id) AS MemberCount
                FROM Objects g
                WHERE g.ObjectClass = 'group'
                    AND g.IsActive = 1
                    AND g.OwnerIdentityId IS NULL
                ORDER BY MemberCount DESC";

            var results = await connection.QueryAsync<OrphanedGroupSummary>(sql);
            return results.AsList();
        }, ct);
    }
}
