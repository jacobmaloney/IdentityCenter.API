using Dapper;
using DataAccessLibrary.Models;
using Logging;
using Microsoft.Extensions.Configuration;

namespace DataAccessLibrary.Repositories;

public class RiskDataRepository : DapperRepositoryBase, IRiskDataRepository
{
    public RiskDataRepository(IConfiguration configuration, IGlobalLogger logger)
        : base(configuration, logger) { }

    public async Task<RiskUserInfo?> GetUserInfoAsync(Guid userId)
    {
        const string sql = @"
            SELECT
                Id AS UserId,
                DisplayName AS UserName,
                DisplayName,
                Department
            FROM Identities
            WHERE Id = @UserId";

        return await ExecuteAsync(async connection =>
            await connection.QueryFirstOrDefaultAsync<RiskUserInfo>(sql, new { UserId = userId }));
    }

    public async Task<int> GetGroupCountAsync(Guid userId)
    {
        const string sql = @"
            SELECT COUNT(*)
            FROM Objects o
            INNER JOIN ObjectGroupMemberships ogm ON o.Id = ogm.ObjectId AND ogm.RemovedAt IS NULL
            WHERE o.IdentityId = @UserId";

        return await ExecuteAsync(async connection =>
            await connection.ExecuteScalarAsync<int>(sql, new { UserId = userId }));
    }

    public async Task<int> GetAdminGroupCountAsync(Guid userId)
    {
        const string sql = @"
            SELECT COUNT(*)
            FROM Objects o
            INNER JOIN ObjectGroupMemberships ogm ON o.Id = ogm.ObjectId AND ogm.RemovedAt IS NULL
            INNER JOIN Objects g ON ogm.GroupId = g.Id
            WHERE o.IdentityId = @UserId
                AND (g.DisplayName LIKE '%Admin%' OR g.DisplayName LIKE '%Administrators%')";

        return await ExecuteAsync(async connection =>
            await connection.ExecuteScalarAsync<int>(sql, new { UserId = userId }));
    }

    public async Task<DateTime?> GetLastLoginAsync(Guid userId)
    {
        const string sql = @"
            SELECT TOP 1 TRY_CAST(oa.AttributeValue AS DATETIME2)
            FROM Objects o
            INNER JOIN ObjectAttributes oa ON o.Id = oa.ObjectId
            WHERE o.IdentityId = @UserId AND oa.AttributeName = 'lastLogonTimestamp'";

        return await ExecuteAsync(async connection =>
            await connection.QueryFirstOrDefaultAsync<DateTime?>(sql, new { UserId = userId }));
    }

    public async Task<List<ViolationCount>> GetOpenViolationsAsync(Guid userId)
    {
        const string sql = @"
            SELECT Severity, COUNT(*) AS Count
            FROM CompliancePolicyViolations
            WHERE EntityId = @UserId AND Status = 'Open'
            GROUP BY Severity";

        return await ExecuteAsync(async connection =>
        {
            var results = await connection.QueryAsync<ViolationCount>(sql, new { UserId = userId });
            return results.ToList();
        });
    }

    public async Task<List<Guid>> GetHighRiskCandidateIdsAsync(double threshold)
    {
        const string sql = @"
            SELECT DISTINCT p.Id
            FROM Identities p
            LEFT JOIN Objects o ON o.IdentityId = p.Id
            LEFT JOIN ObjectGroupMemberships ogm ON o.Id = ogm.ObjectId AND ogm.RemovedAt IS NULL
            LEFT JOIN Objects g ON ogm.GroupId = g.Id
            WHERE p.IsActive = 1
                AND (
                    p.RiskScore >= @Threshold
                    OR g.DisplayName LIKE '%Admin%'
                    OR EXISTS (
                        SELECT 1 FROM CompliancePolicyViolations v
                        WHERE v.EntityId = p.Id AND v.Status = 'Open'
                    )
                )";

        return await ExecuteAsync(async connection =>
        {
            var results = await connection.QueryAsync<Guid>(sql, new { Threshold = threshold });
            return results.ToList();
        });
    }

    public async Task<int> GetActiveUserCountAsync()
    {
        const string sql = "SELECT COUNT(*) FROM Identities WHERE IsActive = 1";

        return await ExecuteAsync(async connection =>
            await connection.ExecuteScalarAsync<int>(sql));
    }

    public async Task<List<RiskDistributionItem>> GetRiskDistributionAsync()
    {
        const string sql = @"
            SELECT
                CASE
                    WHEN ISNULL(RiskScore, 0) * 100 >= 85 THEN 'Critical'
                    WHEN ISNULL(RiskScore, 0) * 100 >= 60 THEN 'High'
                    WHEN ISNULL(RiskScore, 0) * 100 >= 30 THEN 'Medium'
                    ELSE 'Low'
                END AS RiskLevel,
                COUNT(*) AS Count
            FROM Identities
            WHERE IsActive = 1
            GROUP BY
                CASE
                    WHEN ISNULL(RiskScore, 0) * 100 >= 85 THEN 'Critical'
                    WHEN ISNULL(RiskScore, 0) * 100 >= 60 THEN 'High'
                    WHEN ISNULL(RiskScore, 0) * 100 >= 30 THEN 'Medium'
                    ELSE 'Low'
                END";

        return await ExecuteAsync(async connection =>
        {
            var results = await connection.QueryAsync<RiskDistributionItem>(sql);
            return results.ToList();
        });
    }

    public async Task<double> GetAverageRiskScoreAsync()
    {
        const string sql = "SELECT AVG(ISNULL(RiskScore, 0)) * 100 FROM Identities WHERE IsActive = 1";

        return await ExecuteAsync(async connection =>
            await connection.ExecuteScalarAsync<double>(sql));
    }

    public async Task<bool> HasRiskScoreHistoryTableAsync()
    {
        const string sql = "SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'RiskScoreHistory'";

        return await ExecuteAsync(async connection =>
            await connection.ExecuteScalarAsync<int>(sql) > 0);
    }

    public async Task<List<RiskTrendDataPoint>> GetRiskTrendHistoryAsync(int days)
    {
        const string sql = @"
            SELECT
                CAST(RecordedAt AS DATE) AS Date,
                AVG(RiskScore) AS OverallRiskScore,
                COUNT(DISTINCT CASE WHEN EntityType = 'Anomaly' THEN EntityId END) AS AnomalyCount,
                COUNT(DISTINCT CASE WHEN RiskScore >= 70 THEN EntityId END) AS HighRiskUserCount,
                0 AS ViolationCount
            FROM RiskScoreHistory
            WHERE RecordedAt > DATEADD(day, -@Days, GETUTCDATE())
                AND EntityType = 'User'
            GROUP BY CAST(RecordedAt AS DATE)
            ORDER BY Date";

        return await ExecuteAsync(async connection =>
        {
            var results = await connection.QueryAsync<RiskTrendDataPoint>(sql, new { Days = days });
            return results.ToList();
        });
    }
}
