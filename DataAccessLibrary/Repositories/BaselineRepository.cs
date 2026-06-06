using Dapper;
using DataAccessLibrary.Models;
using Logging;
using Microsoft.Extensions.Configuration;

namespace DataAccessLibrary.Repositories;

/// <summary>
/// Dapper-based repository for Golden Image Baselines.
/// </summary>
public class BaselineRepository : DapperRepositoryBase, IBaselineRepository
{
    public BaselineRepository(IConfiguration configuration, IGlobalLogger logger)
        : base(configuration, logger)
    {
    }

    public async Task<Guid> CaptureBaselineAsync(BaselineModels.GoldenImageBaseline baseline, CancellationToken cancellationToken = default)
    {
        // Deactivate any existing active baseline for this entity first
        const string deactivateSql = @"
            UPDATE [GoldenImageBaselines]
            SET [IsActive] = 0
            WHERE [EntityType] = @EntityType AND [EntityId] = @EntityId AND [IsActive] = 1";

        const string insertSql = @"
            DECLARE @NewId UNIQUEIDENTIFIER = NEWID();
            INSERT INTO [GoldenImageBaselines]
                ([Id], [EntityType], [EntityId], [BaselineData], [GroupMemberships],
                 [IntegrityScoreAtBaseline], [RiskScoreAtBaseline], [CapturedAt], [CapturedBy], [IsActive], [Notes])
            VALUES
                (@NewId, @EntityType, @EntityId, @BaselineData, @GroupMemberships,
                 @IntegrityScoreAtBaseline, @RiskScoreAtBaseline, SYSUTCDATETIME(), @CapturedBy, 1, @Notes);
            SELECT @NewId;";

        return await ExecuteAsync(async connection =>
        {
            await connection.ExecuteAsync(new CommandDefinition(
                deactivateSql,
                new { baseline.EntityType, baseline.EntityId },
                cancellationToken: cancellationToken));

            return await connection.QuerySingleAsync<Guid>(new CommandDefinition(
                insertSql,
                new
                {
                    baseline.EntityType, baseline.EntityId, baseline.BaselineData,
                    baseline.GroupMemberships, baseline.IntegrityScoreAtBaseline,
                    baseline.RiskScoreAtBaseline, baseline.CapturedBy, baseline.Notes
                },
                cancellationToken: cancellationToken));
        }, cancellationToken);
    }

    public async Task<BaselineModels.GoldenImageBaseline?> GetActiveBaselineAsync(string entityType, Guid entityId, CancellationToken cancellationToken = default)
    {
        const string sql = @"
            SELECT * FROM [GoldenImageBaselines]
            WHERE [EntityType] = @EntityType AND [EntityId] = @EntityId AND [IsActive] = 1";

        return await ExecuteAsync(async connection =>
        {
            return await connection.QuerySingleOrDefaultAsync<BaselineModels.GoldenImageBaseline>(
                new CommandDefinition(sql, new { EntityType = entityType, EntityId = entityId }, cancellationToken: cancellationToken));
        }, cancellationToken);
    }

    public async Task<List<BaselineModels.GoldenImageBaseline>> GetAllActiveBaselinesAsync(string? entityType = null, CancellationToken cancellationToken = default)
    {
        var sql = "SELECT gb.*, i.[DisplayName] FROM [GoldenImageBaselines] gb LEFT JOIN [Identities] i ON gb.[EntityId] = i.[Id] WHERE gb.[IsActive] = 1";
        if (!string.IsNullOrEmpty(entityType))
            sql += " AND gb.[EntityType] = @EntityType";
        sql += " ORDER BY gb.[CapturedAt] DESC";

        return await ExecuteAsync(async connection =>
        {
            var results = await connection.QueryAsync<BaselineModels.GoldenImageBaseline>(
                new CommandDefinition(sql, new { EntityType = entityType }, cancellationToken: cancellationToken));
            return results.ToList();
        }, cancellationToken);
    }

    public async Task DeactivateBaselineAsync(Guid baselineId, CancellationToken cancellationToken = default)
    {
        const string sql = "UPDATE [GoldenImageBaselines] SET [IsActive] = 0 WHERE [Id] = @Id";

        await ExecuteNonQueryAsync(async connection =>
        {
            await connection.ExecuteAsync(new CommandDefinition(sql, new { Id = baselineId }, cancellationToken: cancellationToken));
        }, cancellationToken);
    }

    public async Task<int> GetBaselineCountAsync(CancellationToken cancellationToken = default)
    {
        const string sql = "SELECT COUNT(*) FROM [GoldenImageBaselines] WHERE [IsActive] = 1";

        return await ExecuteAsync(async connection =>
        {
            return await connection.QuerySingleAsync<int>(new CommandDefinition(sql, cancellationToken: cancellationToken));
        }, cancellationToken);
    }
}
