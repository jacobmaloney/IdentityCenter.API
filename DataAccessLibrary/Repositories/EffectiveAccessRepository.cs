using Dapper;
using DataAccessLibrary.Models;
using Logging;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;

namespace DataAccessLibrary.Repositories;

/// <summary>
/// Dapper-based repository for effective access materialization and blast radius.
/// </summary>
public class EffectiveAccessRepository : DapperRepositoryBase, IEffectiveAccessRepository
{
    public EffectiveAccessRepository(IConfiguration configuration, IGlobalLogger logger)
        : base(configuration, logger)
    {
    }

    public async Task<List<EffectiveAccessModels.DirectMembership>> GetAllDirectMembershipsAsync(CancellationToken cancellationToken = default)
    {
        const string sql = @"
            SELECT ogm.[ObjectId], ogm.[GroupId], ogm.[Id] AS MembershipId
            FROM [ObjectGroupMemberships] ogm
            INNER JOIN [Objects] o ON ogm.[ObjectId] = o.[Id]
            WHERE o.[IsActive] = 1";

        return await ExecuteAsync(async connection =>
        {
            var results = await connection.QueryAsync<EffectiveAccessModels.DirectMembership>(
                new CommandDefinition(sql, cancellationToken: cancellationToken));
            return results.ToList();
        }, cancellationToken);
    }

    public async Task<List<EffectiveAccessModels.GroupNesting>> GetAllGroupNestingsAsync(CancellationToken cancellationToken = default)
    {
        // Groups that are members of other groups (member object is itself a group)
        const string sql = @"
            SELECT ogm.[ObjectId] AS ChildGroupId, ogm.[GroupId] AS ParentGroupId
            FROM [ObjectGroupMemberships] ogm
            INNER JOIN [Objects] o ON ogm.[ObjectId] = o.[Id]
            WHERE o.[ObjectClass] = 'group' AND o.[IsActive] = 1";

        return await ExecuteAsync(async connection =>
        {
            var results = await connection.QueryAsync<EffectiveAccessModels.GroupNesting>(
                new CommandDefinition(sql, cancellationToken: cancellationToken));
            return results.ToList();
        }, cancellationToken);
    }

    public async Task RebuildEffectiveAccessAsync(IEnumerable<EffectiveAccessModels.EffectiveAccessEntry> entries, CancellationToken cancellationToken = default)
    {
        await ExecuteNonQueryAsync(async connection =>
        {
            using var transaction = connection.BeginTransaction();
            try
            {
                // Truncate existing entries
                await connection.ExecuteAsync(
                    new CommandDefinition("TRUNCATE TABLE [EffectiveAccessEntries]", transaction: transaction, cancellationToken: cancellationToken));

                // Bulk insert new entries
                const string insertSql = @"
                    INSERT INTO [EffectiveAccessEntries]
                        ([Id], [ObjectId], [GroupId], [AccessPath], [Depth], [IsDirect], [SourceMembershipId], [MaterializedAt])
                    VALUES
                        (NEWID(), @ObjectId, @GroupId, @AccessPath, @Depth, @IsDirect, @SourceMembershipId, SYSUTCDATETIME())";

                foreach (var entry in entries)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    await connection.ExecuteAsync(new CommandDefinition(
                        insertSql,
                        new { entry.ObjectId, entry.GroupId, entry.AccessPath, entry.Depth, entry.IsDirect, entry.SourceMembershipId },
                        transaction,
                        cancellationToken: cancellationToken));
                }

                transaction.Commit();
            }
            catch
            {
                transaction.Rollback();
                throw;
            }
        }, cancellationToken);
    }

    public async Task UpsertGroupBlastRadiiAsync(IEnumerable<EffectiveAccessModels.GroupBlastRadiusRecord> records, CancellationToken cancellationToken = default)
    {
        const string sql = @"
            MERGE [GroupBlastRadius] AS target
            USING (SELECT @GroupId AS GroupId) AS source
            ON target.[GroupId] = source.[GroupId]
            WHEN MATCHED THEN
                UPDATE SET
                    [DirectMemberCount] = @DirectMemberCount,
                    [EffectiveMemberCount] = @EffectiveMemberCount,
                    [MaxDepth] = @MaxDepth,
                    [NestedGroupCount] = @NestedGroupCount,
                    [BlastRadiusScore] = @BlastRadiusScore,
                    [CalculatedAt] = SYSUTCDATETIME()
            WHEN NOT MATCHED THEN
                INSERT ([GroupId], [DirectMemberCount], [EffectiveMemberCount], [MaxDepth], [NestedGroupCount], [BlastRadiusScore], [CalculatedAt])
                VALUES (@GroupId, @DirectMemberCount, @EffectiveMemberCount, @MaxDepth, @NestedGroupCount, @BlastRadiusScore, SYSUTCDATETIME());";

        await ExecuteNonQueryAsync(async connection =>
        {
            foreach (var record in records)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await connection.ExecuteAsync(new CommandDefinition(
                    sql,
                    new { record.GroupId, record.DirectMemberCount, record.EffectiveMemberCount, record.MaxDepth, record.NestedGroupCount, record.BlastRadiusScore },
                    cancellationToken: cancellationToken));
            }
        }, cancellationToken);
    }

    public async Task<List<EffectiveAccessModels.EffectiveAccessEntry>> GetEffectiveAccessForObjectAsync(Guid objectId, CancellationToken cancellationToken = default)
    {
        const string sql = @"
            SELECT ea.[Id], ea.[ObjectId], ea.[GroupId], ea.[AccessPath], ea.[Depth], ea.[IsDirect],
                   ea.[SourceMembershipId], ea.[MaterializedAt],
                   o.[DisplayName] AS GroupName
            FROM [EffectiveAccessEntries] ea
            LEFT JOIN [Objects] o ON ea.[GroupId] = o.[Id]
            WHERE ea.[ObjectId] = @ObjectId
            ORDER BY ea.[Depth], o.[DisplayName]";

        return await ExecuteAsync(async connection =>
        {
            var results = await connection.QueryAsync<EffectiveAccessModels.EffectiveAccessEntry>(
                new CommandDefinition(sql, new { ObjectId = objectId }, cancellationToken: cancellationToken));
            return results.ToList();
        }, cancellationToken);
    }

    public async Task<List<EffectiveAccessModels.GroupBlastRadiusRecord>> GetTopBlastRadiusGroupsAsync(int top = 20, CancellationToken cancellationToken = default)
    {
        const string sql = @"
            SELECT TOP (@Top)
                gbr.[GroupId], gbr.[DirectMemberCount], gbr.[EffectiveMemberCount],
                gbr.[MaxDepth], gbr.[NestedGroupCount], gbr.[BlastRadiusScore], gbr.[CalculatedAt],
                o.[DisplayName] AS GroupName
            FROM [GroupBlastRadius] gbr
            LEFT JOIN [Objects] o ON gbr.[GroupId] = o.[Id]
            ORDER BY gbr.[BlastRadiusScore] DESC";

        return await ExecuteAsync(async connection =>
        {
            var results = await connection.QueryAsync<EffectiveAccessModels.GroupBlastRadiusRecord>(
                new CommandDefinition(sql, new { Top = top }, cancellationToken: cancellationToken));
            return results.ToList();
        }, cancellationToken);
    }

    public async Task UpdateIdentityEffectiveAccessAsync(Guid identityId, int effectiveGroupCount, int effectiveAdminGroupCount, int maxDepth, CancellationToken cancellationToken = default)
    {
        const string sql = @"
            UPDATE [Identities]
            SET [EffectiveGroupCount] = @EffectiveGroupCount,
                [EffectiveAdminGroupCount] = @EffectiveAdminGroupCount,
                [MaxAccessDepth] = @MaxDepth,
                [EffectiveAccessLastCalculatedAt] = SYSUTCDATETIME()
            WHERE [Id] = @IdentityId";

        await ExecuteNonQueryAsync(async connection =>
        {
            await connection.ExecuteAsync(new CommandDefinition(
                sql,
                new { IdentityId = identityId, EffectiveGroupCount = effectiveGroupCount, EffectiveAdminGroupCount = effectiveAdminGroupCount, MaxDepth = maxDepth },
                cancellationToken: cancellationToken));
        }, cancellationToken);
    }
}
