using Dapper;
using DataAccessLibrary.Models;
using Logging;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using System.Text.Json;

namespace DataAccessLibrary.Repositories;

/// <summary>
/// Dapper-based repository for identity integrity score persistence.
/// </summary>
public class IntegrityScoreRepository : DapperRepositoryBase, IIntegrityScoreRepository
{
    public IntegrityScoreRepository(IConfiguration configuration, IGlobalLogger logger)
        : base(configuration, logger)
    {
    }

    public async Task UpdateIdentityIntegrityAsync(Guid identityId, decimal score, string level, string factorsJson, CancellationToken cancellationToken = default)
    {
        const string sql = @"
            UPDATE [Identities]
            SET [IntegrityScore] = @Score,
                [IntegrityLevel] = @Level,
                [IntegrityFactors] = @FactorsJson,
                [IntegrityLastCalculatedAt] = SYSUTCDATETIME()
            WHERE [Id] = @IdentityId";

        await ExecuteNonQueryAsync(async connection =>
        {
            await connection.ExecuteAsync(new CommandDefinition(
                sql,
                new { IdentityId = identityId, Score = score, Level = level, FactorsJson = factorsJson },
                cancellationToken: cancellationToken));
        }, cancellationToken);
    }

    public async Task BulkUpdateIntegrityScoresAsync(IEnumerable<IntegrityResult> results, CancellationToken cancellationToken = default)
    {
        await ExecuteNonQueryAsync(async connection =>
        {
            using var transaction = connection.BeginTransaction();
            try
            {
                const string updateSql = @"
                    UPDATE [Identities]
                    SET [IntegrityScore] = @Score,
                        [IntegrityLevel] = @Level,
                        [IntegrityFactors] = @FactorsJson,
                        [IntegrityLastCalculatedAt] = SYSUTCDATETIME()
                    WHERE [Id] = @IdentityId";

                const string historySql = @"
                    INSERT INTO [IdentityIntegrityHistory] ([Id], [IdentityId], [IntegrityScore], [IntegrityLevel], [FactorBreakdown], [CalculatedAt])
                    VALUES (NEWID(), @IdentityId, @Score, @Level, @FactorsJson, SYSUTCDATETIME())";

                foreach (var result in results)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var factorsJson = JsonSerializer.Serialize(result.Factors);
                    var param = new { IdentityId = result.IdentityId, Score = result.Score, Level = result.Level, FactorsJson = factorsJson };

                    await connection.ExecuteAsync(new CommandDefinition(updateSql, param, transaction, cancellationToken: cancellationToken));
                    await connection.ExecuteAsync(new CommandDefinition(historySql, param, transaction, cancellationToken: cancellationToken));
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

    public async Task InsertIntegrityHistoryAsync(Guid identityId, decimal score, string level, string? factorsJson, CancellationToken cancellationToken = default)
    {
        const string sql = @"
            INSERT INTO [IdentityIntegrityHistory] ([Id], [IdentityId], [IntegrityScore], [IntegrityLevel], [FactorBreakdown], [CalculatedAt])
            VALUES (NEWID(), @IdentityId, @Score, @Level, @FactorsJson, SYSUTCDATETIME())";

        await ExecuteNonQueryAsync(async connection =>
        {
            await connection.ExecuteAsync(new CommandDefinition(
                sql,
                new { IdentityId = identityId, Score = score, Level = level, FactorsJson = factorsJson },
                cancellationToken: cancellationToken));
        }, cancellationToken);
    }

    public async Task<List<IntegrityHistoryPoint>> GetIntegrityHistoryAsync(Guid identityId, int days = 30, CancellationToken cancellationToken = default)
    {
        const string sql = @"
            SELECT [Id], [IdentityId], [IntegrityScore], [IntegrityLevel], [FactorBreakdown], [CalculatedAt]
            FROM [IdentityIntegrityHistory]
            WHERE [IdentityId] = @IdentityId
              AND [CalculatedAt] >= DATEADD(DAY, -@Days, SYSUTCDATETIME())
            ORDER BY [CalculatedAt] DESC";

        return await ExecuteAsync(async connection =>
        {
            var results = await connection.QueryAsync<IntegrityHistoryPoint>(new CommandDefinition(
                sql,
                new { IdentityId = identityId, Days = days },
                cancellationToken: cancellationToken));
            return results.ToList();
        }, cancellationToken);
    }

    public async Task<IntegritySummary> GetOrganizationIntegritySummaryAsync(CancellationToken cancellationToken = default)
    {
        const string sql = @"
            SELECT
                ISNULL(AVG([IntegrityScore]), 0) AS AverageScore,
                COUNT(*) AS TotalIdentities,
                SUM(CASE WHEN [IntegrityLevel] = 'Excellent' THEN 1 ELSE 0 END) AS ExcellentCount,
                SUM(CASE WHEN [IntegrityLevel] = 'High' THEN 1 ELSE 0 END) AS HighCount,
                SUM(CASE WHEN [IntegrityLevel] = 'Medium' THEN 1 ELSE 0 END) AS MediumCount,
                SUM(CASE WHEN [IntegrityLevel] = 'Low' THEN 1 ELSE 0 END) AS LowCount,
                SUM(CASE WHEN [IntegrityLevel] = 'Critical' THEN 1 ELSE 0 END) AS CriticalCount
            FROM [Identities]
            WHERE [IsActive] = 1
              AND [IntegrityScore] IS NOT NULL";

        return await ExecuteAsync(async connection =>
        {
            var result = await connection.QuerySingleOrDefaultAsync<IntegritySummary>(new CommandDefinition(
                sql, cancellationToken: cancellationToken));

            if (result != null)
            {
                result.AverageLevel = IntegrityResult.ScoreToLevel(result.AverageScore);
                result.CalculatedAt = DateTime.UtcNow;
            }

            return result ?? new IntegritySummary { CalculatedAt = DateTime.UtcNow };
        }, cancellationToken);
    }

    public async Task<Guid> InsertGovernanceActionAsync(GovernanceActionRecord action, CancellationToken cancellationToken = default)
    {
        const string sql = @"
            DECLARE @NewId UNIQUEIDENTIFIER = NEWID();
            INSERT INTO [GovernanceActions]
                ([Id], [IdentityId], [ObjectId], [GroupId], [ActionType], [TriggerSource],
                 [PreviousState], [NewState], [Reason], [ConfidenceScore], [PerformedBy], [PerformedAt])
            VALUES
                (@NewId, @IdentityId, @ObjectId, @GroupId, @ActionType, @TriggerSource,
                 @PreviousState, @NewState, @Reason, @ConfidenceScore, @PerformedBy, SYSUTCDATETIME());
            SELECT @NewId;";

        return await ExecuteAsync(async connection =>
        {
            return await connection.QuerySingleAsync<Guid>(new CommandDefinition(
                sql,
                new
                {
                    action.IdentityId,
                    action.ObjectId,
                    action.GroupId,
                    action.ActionType,
                    action.TriggerSource,
                    action.PreviousState,
                    action.NewState,
                    action.Reason,
                    action.ConfidenceScore,
                    action.PerformedBy
                },
                cancellationToken: cancellationToken));
        }, cancellationToken);
    }

    public async Task<List<GovernanceActionRecord>> GetGovernanceActionsAsync(Guid? identityId = null, int take = 50, CancellationToken cancellationToken = default)
    {
        var sql = @"
            SELECT TOP (@Take)
                [Id], [IdentityId], [ObjectId], [GroupId], [ActionType], [TriggerSource],
                [PreviousState], [NewState], [Reason], [ConfidenceScore],
                [PerformedBy], [PerformedAt], [RevertedAt], [RevertedBy]
            FROM [GovernanceActions]";

        if (identityId.HasValue)
            sql += " WHERE [IdentityId] = @IdentityId";

        sql += " ORDER BY [PerformedAt] DESC";

        return await ExecuteAsync(async connection =>
        {
            var results = await connection.QueryAsync<GovernanceActionRecord>(new CommandDefinition(
                sql,
                new { Take = take, IdentityId = identityId },
                cancellationToken: cancellationToken));
            return results.ToList();
        }, cancellationToken);
    }

    public async Task<List<Guid>> GetAllActiveIdentityIdsAsync(CancellationToken cancellationToken = default)
    {
        const string sql = "SELECT [Id] FROM [Identities] WHERE [IsActive] = 1";

        return await ExecuteAsync(async connection =>
        {
            var results = await connection.QueryAsync<Guid>(new CommandDefinition(sql, cancellationToken: cancellationToken));
            return results.ToList();
        }, cancellationToken);
    }

    public async Task<decimal> GetAverageIntegrityScoreAsync(CancellationToken cancellationToken = default)
    {
        const string sql = @"
            SELECT ISNULL(AVG([IntegrityScore]), 0)
            FROM [Identities]
            WHERE [IsActive] = 1 AND [IntegrityScore] IS NOT NULL";

        return await ExecuteAsync(async connection =>
        {
            return await connection.QuerySingleAsync<decimal>(new CommandDefinition(sql, cancellationToken: cancellationToken));
        }, cancellationToken);
    }

    public async Task<List<IntegrityHistoryPoint>> GetOrganizationIntegrityTrendAsync(int days = 30, CancellationToken cancellationToken = default)
    {
        const string sql = @"
            SELECT
                NEWID() AS [Id],
                CAST('00000000-0000-0000-0000-000000000000' AS UNIQUEIDENTIFIER) AS [IdentityId],
                AVG([IntegrityScore]) AS [IntegrityScore],
                'Organization' AS [IntegrityLevel],
                NULL AS [FactorBreakdown],
                CAST([CalculatedAt] AS DATE) AS [CalculatedAt]
            FROM [IdentityIntegrityHistory]
            WHERE [CalculatedAt] >= DATEADD(DAY, -@Days, SYSUTCDATETIME())
            GROUP BY CAST([CalculatedAt] AS DATE)
            ORDER BY CAST([CalculatedAt] AS DATE)";

        return await ExecuteAsync(async connection =>
        {
            var results = await connection.QueryAsync<IntegrityHistoryPoint>(new CommandDefinition(
                sql,
                new { Days = days },
                cancellationToken: cancellationToken));
            return results.ToList();
        }, cancellationToken);
    }
}
