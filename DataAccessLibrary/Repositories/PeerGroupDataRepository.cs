using Dapper;
using DataAccessLibrary.Models;
using Logging;
using Microsoft.Extensions.Configuration;

namespace DataAccessLibrary.Repositories;

public class PeerGroupDataRepository : DapperRepositoryBase, IPeerGroupDataRepository
{
    public PeerGroupDataRepository(IConfiguration configuration, IGlobalLogger logger)
        : base(configuration, logger) { }

    public async Task<PeerUserInfo?> GetUserInfoAsync(Guid userId)
    {
        const string sql = @"
            SELECT
                Id AS UserId,
                DisplayName AS UserName,
                DisplayName,
                Department,
                JobTitle AS Title
            FROM Identities
            WHERE Id = @UserId";

        return await ExecuteAsync(async connection =>
            await connection.QueryFirstOrDefaultAsync<PeerUserInfo>(sql, new { UserId = userId }));
    }

    public async Task<List<PeerMetrics>> GetPeersByDepartmentAndTitleAsync(string department, string title)
    {
        const string sql = @"
            SELECT
                p.Id AS UserId,
                COUNT(DISTINCT gm.GroupId) AS GroupCount,
                COUNT(DISTINCT CASE WHEN g.DisplayName LIKE '%Admin%' THEN gm.GroupId END) AS AdminGroupCount,
                ISNULL(p.RiskScore, 0) AS RiskScore
            FROM Identities p
            LEFT JOIN Objects o ON o.IdentityId = p.Id
            LEFT JOIN ObjectGroupMemberships gm ON o.Id = gm.ObjectId AND gm.RemovedAt IS NULL
            LEFT JOIN Objects g ON gm.GroupId = g.Id
            WHERE p.IsActive = 1
                AND p.Department = @Department
                AND p.JobTitle = @Title
            GROUP BY p.Id, p.RiskScore";

        return await ExecuteAsync(async connection =>
        {
            var results = await connection.QueryAsync<PeerMetrics>(sql, new { Department = department, Title = title });
            return results.ToList();
        });
    }

    public async Task<List<PeerMetrics>> GetPeersByDepartmentAsync(string department)
    {
        const string sql = @"
            SELECT
                p.Id AS UserId,
                COUNT(DISTINCT gm.GroupId) AS GroupCount,
                COUNT(DISTINCT CASE WHEN g.DisplayName LIKE '%Admin%' THEN gm.GroupId END) AS AdminGroupCount,
                ISNULL(p.RiskScore, 0) AS RiskScore
            FROM Identities p
            LEFT JOIN Objects o ON o.IdentityId = p.Id
            LEFT JOIN ObjectGroupMemberships gm ON o.Id = gm.ObjectId AND gm.RemovedAt IS NULL
            LEFT JOIN Objects g ON gm.GroupId = g.Id
            WHERE p.IsActive = 1
                AND p.Department = @Department
            GROUP BY p.Id, p.RiskScore";

        return await ExecuteAsync(async connection =>
        {
            var results = await connection.QueryAsync<PeerMetrics>(sql, new { Department = department });
            return results.ToList();
        });
    }

    public async Task<PeerMetrics?> GetUserMetricsAsync(Guid userId)
    {
        const string sql = @"
            SELECT
                p.Id AS UserId,
                COUNT(DISTINCT gm.GroupId) AS GroupCount,
                COUNT(DISTINCT CASE WHEN g.DisplayName LIKE '%Admin%' THEN gm.GroupId END) AS AdminGroupCount,
                ISNULL(p.RiskScore, 0) AS RiskScore
            FROM Identities p
            LEFT JOIN Objects o ON o.IdentityId = p.Id
            LEFT JOIN ObjectGroupMemberships gm ON o.Id = gm.ObjectId AND gm.RemovedAt IS NULL
            LEFT JOIN Objects g ON gm.GroupId = g.Id
            WHERE p.Id = @UserId
            GROUP BY p.Id, p.RiskScore";

        return await ExecuteAsync(async connection =>
            await connection.QueryFirstOrDefaultAsync<PeerMetrics>(sql, new { UserId = userId }));
    }

    public async Task<List<Guid>> GetAllActiveUserIdsAsync()
    {
        const string sql = "SELECT Id FROM Identities WHERE IsActive = 1";

        return await ExecuteAsync(async connection =>
        {
            var results = await connection.QueryAsync<Guid>(sql);
            return results.ToList();
        });
    }
}
