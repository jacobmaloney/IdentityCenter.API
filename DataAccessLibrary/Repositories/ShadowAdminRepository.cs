using Dapper;
using DataAccessLibrary.Models;
using Logging;
using Microsoft.Extensions.Configuration;

namespace DataAccessLibrary.Repositories;

/// <summary>
/// Dapper repository for shadow admin detection.
/// Uses EffectiveAccessEntries to find users who reach admin groups only through nesting.
/// </summary>
public class ShadowAdminRepository : DapperRepositoryBase, IShadowAdminRepository
{
    private const string ShadowAdminCte = @"
        ;WITH AdminGroups AS (
            SELECT Id, DisplayName
            FROM Objects
            WHERE ObjectClass = 'group'
                AND IsActive = 1
                AND (
                    DisplayName LIKE '%Domain Admins%'
                    OR DisplayName LIKE '%Enterprise Admins%'
                    OR DisplayName LIKE '%Schema Admins%'
                    OR DisplayName LIKE '%Administrators%'
                    OR DisplayName LIKE '%Admin%'
                )
        ),
        IndirectAdminAccess AS (
            SELECT ea.ObjectId, ea.GroupId, ea.Depth, ea.AccessPath
            FROM EffectiveAccessEntries ea
            INNER JOIN AdminGroups ag ON ag.Id = ea.GroupId
            WHERE ea.IsDirect = 0 AND ea.Depth > 0
        ),
        DirectAdminAccess AS (
            SELECT ea.ObjectId, ea.GroupId
            FROM EffectiveAccessEntries ea
            INNER JOIN AdminGroups ag ON ag.Id = ea.GroupId
            WHERE ea.IsDirect = 1
        )";

    public ShadowAdminRepository(IConfiguration configuration, IGlobalLogger logger)
        : base(configuration, logger)
    {
    }

    public async Task<List<ShadowAdminRecord>> GetShadowAdminsAsync(CancellationToken ct = default)
    {
        return await ExecuteAsync(async connection =>
        {
            var sql = ShadowAdminCte + @"
                SELECT DISTINCT
                    o.Id AS ObjectId,
                    o.DisplayName,
                    o.Username,
                    o.Email,
                    ia.GroupId AS AdminGroupId,
                    ag.DisplayName AS AdminGroupName,
                    ia.Depth AS NestingDepth,
                    ia.AccessPath,
                    o.Department
                FROM IndirectAdminAccess ia
                INNER JOIN Objects o ON o.Id = ia.ObjectId
                INNER JOIN AdminGroups ag ON ag.Id = ia.GroupId
                LEFT JOIN DirectAdminAccess da ON da.ObjectId = ia.ObjectId AND da.GroupId = ia.GroupId
                WHERE da.ObjectId IS NULL
                    AND o.ObjectClass = 'user'
                    AND o.IsActive = 1
                ORDER BY ia.Depth DESC, o.DisplayName";

            var results = await connection.QueryAsync<ShadowAdminRecord>(sql);
            return results.AsList();
        }, ct);
    }

    public async Task<int> GetShadowAdminCountAsync(CancellationToken ct = default)
    {
        return await ExecuteAsync(async connection =>
        {
            var sql = ShadowAdminCte + @"
                SELECT COUNT(DISTINCT ia.ObjectId)
                FROM IndirectAdminAccess ia
                INNER JOIN Objects o ON o.Id = ia.ObjectId
                LEFT JOIN DirectAdminAccess da ON da.ObjectId = ia.ObjectId AND da.GroupId = ia.GroupId
                WHERE da.ObjectId IS NULL
                    AND o.ObjectClass = 'user'
                    AND o.IsActive = 1";

            return await connection.ExecuteScalarAsync<int>(sql);
        }, ct);
    }
}
