using Dapper;
using DataAccessLibrary.Models;
using Logging;
using Microsoft.Extensions.Configuration;

namespace DataAccessLibrary.Repositories;

public class AnomalyDataRepository : DapperRepositoryBase, IAnomalyDataRepository
{
    public AnomalyDataRepository(IConfiguration configuration, IGlobalLogger logger)
        : base(configuration, logger) { }

    public async Task<List<AnomalyUserRecord>> GetDormantAccountsAwakenedAsync(int dormantDays)
    {
        const string sql = @"
            SELECT
                p.Id AS UserId,
                p.UserPrincipalName AS UserName,
                p.DisplayName,
                p.Department,
                oa_last.AttributeValue AS LastSignIn,
                oa_prev.AttributeValue AS PreviousSignIn
            FROM Identities p
            LEFT JOIN ObjectAttributes oa_last ON p.Id = oa_last.ObjectId
                AND oa_last.AttributeName = 'lastLogonTimestamp'
            LEFT JOIN ObjectAttributes oa_prev ON p.Id = oa_prev.ObjectId
                AND oa_prev.AttributeName = 'previousLastLogon'
            WHERE p.IsActive = 1
                AND oa_last.AttributeValue IS NOT NULL
                AND TRY_CAST(oa_last.AttributeValue AS DATETIME2) > DATEADD(day, -7, GETUTCDATE())
                AND (
                    oa_prev.AttributeValue IS NULL
                    OR TRY_CAST(oa_prev.AttributeValue AS DATETIME2) < DATEADD(day, -@DormantDays, GETUTCDATE())
                )";

        return await ExecuteAsync(async connection =>
        {
            var results = await connection.QueryAsync<AnomalyUserRecord>(sql, new { DormantDays = dormantDays });
            return results.ToList();
        });
    }

    public async Task<List<PrivilegeEscalationRecord>> GetRecentPrivilegeEscalationsAsync()
    {
        const string sql = @"
            SELECT
                o.Id AS UserId,
                o.UserPrincipalName AS UserName,
                o.DisplayName,
                o.Department,
                g.DisplayName AS GroupName,
                ogm.AddedAt
            FROM ObjectGroupMemberships ogm
            INNER JOIN Objects o ON ogm.ObjectId = o.Id
            INNER JOIN Objects g ON ogm.GroupId = g.Id
            WHERE ogm.AddedAt > DATEADD(day, -7, GETUTCDATE())
                AND (
                    g.DisplayName LIKE '%Admin%'
                    OR g.DisplayName LIKE '%Administrators%'
                    OR g.DisplayName LIKE '%Domain Admins%'
                    OR g.DisplayName LIKE '%Enterprise Admins%'
                    OR g.DisplayName LIKE '%Privileged%'
                )
                AND o.IsActive = 1";

        return await ExecuteAsync(async connection =>
        {
            var results = await connection.QueryAsync<PrivilegeEscalationRecord>(sql);
            return results.ToList();
        });
    }

    public async Task<List<SuddenGroupChangeRecord>> GetSuddenGroupChangesAsync(int threshold)
    {
        const string sql = @"
            SELECT
                o.Id AS UserId,
                o.UserPrincipalName AS UserName,
                o.DisplayName,
                o.Department,
                COUNT(*) AS NewGroupCount
            FROM ObjectGroupMemberships ogm
            INNER JOIN Objects o ON ogm.ObjectId = o.Id
            WHERE ogm.AddedAt > DATEADD(day, -1, GETUTCDATE())
                AND o.IsActive = 1
            GROUP BY o.Id, o.UserPrincipalName, o.DisplayName, o.Department
            HAVING COUNT(*) >= @Threshold";

        return await ExecuteAsync(async connection =>
        {
            var results = await connection.QueryAsync<SuddenGroupChangeRecord>(sql, new { Threshold = threshold });
            return results.ToList();
        });
    }

    public async Task<List<DisabledAccountActivityRecord>> GetDisabledAccountsWithActivityAsync()
    {
        const string sql = @"
            SELECT
                p.Id AS UserId,
                p.UserPrincipalName AS UserName,
                p.DisplayName,
                p.Department,
                oa.AttributeValue AS LastActivity
            FROM Identities p
            LEFT JOIN ObjectAttributes oa ON p.Id = oa.ObjectId
                AND oa.AttributeName = 'lastLogonTimestamp'
            WHERE p.IsActive = 0
                AND oa.AttributeValue IS NOT NULL
                AND TRY_CAST(oa.AttributeValue AS DATETIME2) > DATEADD(day, -7, GETUTCDATE())";

        return await ExecuteAsync(async connection =>
        {
            var results = await connection.QueryAsync<DisabledAccountActivityRecord>(sql);
            return results.ToList();
        });
    }
}
