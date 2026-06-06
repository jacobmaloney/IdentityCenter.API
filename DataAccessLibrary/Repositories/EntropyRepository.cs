using Dapper;
using DataAccessLibrary.Models;
using Logging;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;

namespace DataAccessLibrary.Repositories;

/// <summary>
/// Dapper-based repository for entropy snapshots and drift tracking.
/// </summary>
public class EntropyRepository : DapperRepositoryBase, IEntropyRepository
{
    public EntropyRepository(IConfiguration configuration, IGlobalLogger logger)
        : base(configuration, logger)
    {
    }

    public async Task InsertEntropySnapshotAsync(string snapshotType, decimal score, string? componentsJson, CancellationToken cancellationToken = default)
    {
        const string sql = @"
            INSERT INTO [EntropySnapshots] ([Id], [SnapshotType], [Score], [Components], [CalculatedAt])
            VALUES (NEWID(), @SnapshotType, @Score, @Components, SYSUTCDATETIME())";

        await ExecuteNonQueryAsync(async connection =>
        {
            await connection.ExecuteAsync(new CommandDefinition(
                sql,
                new { SnapshotType = snapshotType, Score = score, Components = componentsJson },
                cancellationToken: cancellationToken));
        }, cancellationToken);
    }

    public async Task<List<EntropyModels.EntropySnapshot>> GetLatestSnapshotsAsync(CancellationToken cancellationToken = default)
    {
        const string sql = @"
            WITH LatestPerType AS (
                SELECT *, ROW_NUMBER() OVER (PARTITION BY [SnapshotType] ORDER BY [CalculatedAt] DESC) AS rn
                FROM [EntropySnapshots]
            )
            SELECT [Id], [SnapshotType], [Score], [Components], [CalculatedAt]
            FROM LatestPerType
            WHERE rn = 1";

        return await ExecuteAsync(async connection =>
        {
            var results = await connection.QueryAsync<EntropyModels.EntropySnapshot>(
                new CommandDefinition(sql, cancellationToken: cancellationToken));
            return results.ToList();
        }, cancellationToken);
    }

    public async Task<List<EntropyModels.EntropySnapshot>> GetEntropyTrendAsync(string snapshotType, int days = 30, CancellationToken cancellationToken = default)
    {
        const string sql = @"
            SELECT [Id], [SnapshotType], [Score], [Components], [CalculatedAt]
            FROM [EntropySnapshots]
            WHERE [SnapshotType] = @SnapshotType
              AND [CalculatedAt] >= DATEADD(DAY, -@Days, SYSUTCDATETIME())
            ORDER BY [CalculatedAt]";

        return await ExecuteAsync(async connection =>
        {
            var results = await connection.QueryAsync<EntropyModels.EntropySnapshot>(
                new CommandDefinition(sql, new { SnapshotType = snapshotType, Days = days }, cancellationToken: cancellationToken));
            return results.ToList();
        }, cancellationToken);
    }

    public async Task InsertDriftRecordAsync(Guid identityId, string driftType, decimal magnitude, string? previousValue, string? currentValue, CancellationToken cancellationToken = default)
    {
        const string sql = @"
            INSERT INTO [IdentityDriftRecords] ([Id], [IdentityId], [DriftType], [DriftMagnitude], [PreviousValue], [CurrentValue], [DetectedAt])
            VALUES (NEWID(), @IdentityId, @DriftType, @Magnitude, @PreviousValue, @CurrentValue, SYSUTCDATETIME())";

        await ExecuteNonQueryAsync(async connection =>
        {
            await connection.ExecuteAsync(new CommandDefinition(
                sql,
                new { IdentityId = identityId, DriftType = driftType, Magnitude = magnitude, PreviousValue = previousValue, CurrentValue = currentValue },
                cancellationToken: cancellationToken));
        }, cancellationToken);
    }

    public async Task<List<EntropyModels.DriftRecord>> GetTopDriftersAsync(int top = 20, CancellationToken cancellationToken = default)
    {
        const string sql = @"
            SELECT TOP (@Top)
                dr.[Id], dr.[IdentityId], dr.[DriftType], dr.[DriftMagnitude], dr.[PreviousValue],
                dr.[CurrentValue], dr.[DetectedAt], dr.[IsAcknowledged], dr.[AcknowledgedBy], dr.[AcknowledgedAt],
                i.[DisplayName]
            FROM [IdentityDriftRecords] dr
            INNER JOIN [Identities] i ON dr.[IdentityId] = i.[Id]
            WHERE dr.[IsAcknowledged] = 0
            ORDER BY dr.[DriftMagnitude] DESC, dr.[DetectedAt] DESC";

        return await ExecuteAsync(async connection =>
        {
            var results = await connection.QueryAsync<EntropyModels.DriftRecord>(
                new CommandDefinition(sql, new { Top = top }, cancellationToken: cancellationToken));
            return results.ToList();
        }, cancellationToken);
    }

    public async Task<List<EntropyModels.DriftRecord>> GetDriftRecordsForIdentityAsync(Guid identityId, int days = 30, CancellationToken cancellationToken = default)
    {
        const string sql = @"
            SELECT [Id], [IdentityId], [DriftType], [DriftMagnitude], [PreviousValue],
                   [CurrentValue], [DetectedAt], [IsAcknowledged], [AcknowledgedBy], [AcknowledgedAt]
            FROM [IdentityDriftRecords]
            WHERE [IdentityId] = @IdentityId
              AND [DetectedAt] >= DATEADD(DAY, -@Days, SYSUTCDATETIME())
            ORDER BY [DetectedAt] DESC";

        return await ExecuteAsync(async connection =>
        {
            var results = await connection.QueryAsync<EntropyModels.DriftRecord>(
                new CommandDefinition(sql, new { IdentityId = identityId, Days = days }, cancellationToken: cancellationToken));
            return results.ToList();
        }, cancellationToken);
    }

    public async Task AcknowledgeDriftAsync(Guid driftRecordId, string acknowledgedBy, CancellationToken cancellationToken = default)
    {
        const string sql = @"
            UPDATE [IdentityDriftRecords]
            SET [IsAcknowledged] = 1,
                [AcknowledgedBy] = @AcknowledgedBy,
                [AcknowledgedAt] = SYSUTCDATETIME()
            WHERE [Id] = @Id";

        await ExecuteNonQueryAsync(async connection =>
        {
            await connection.ExecuteAsync(new CommandDefinition(
                sql,
                new { Id = driftRecordId, AcknowledgedBy = acknowledgedBy },
                cancellationToken: cancellationToken));
        }, cancellationToken);
    }

    public async Task UpdateIdentityDriftDataAsync(Guid identityId, decimal driftScore, int groupCount, decimal riskScore, CancellationToken cancellationToken = default)
    {
        const string sql = @"
            UPDATE [Identities]
            SET [DriftScore] = @DriftScore,
                [LastDriftCalculatedAt] = SYSUTCDATETIME(),
                [GroupCountAtLastSync] = @GroupCount,
                [RiskScoreAtLastSync] = @RiskScore
            WHERE [Id] = @IdentityId";

        await ExecuteNonQueryAsync(async connection =>
        {
            await connection.ExecuteAsync(new CommandDefinition(
                sql,
                new { IdentityId = identityId, DriftScore = driftScore, GroupCount = groupCount, RiskScore = riskScore },
                cancellationToken: cancellationToken));
        }, cancellationToken);
    }

    public async Task<List<EntropyModels.IdentityDriftBaseline>> GetAllIdentityDriftBaselinesAsync(CancellationToken cancellationToken = default)
    {
        const string sql = @"
            SELECT [Id] AS IdentityId, [GroupCountAtLastSync], [RiskScoreAtLastSync], [RiskScore]
            FROM [Identities]
            WHERE [IsActive] = 1";

        return await ExecuteAsync(async connection =>
        {
            var results = await connection.QueryAsync<EntropyModels.IdentityDriftBaseline>(
                new CommandDefinition(sql, cancellationToken: cancellationToken));
            return results.ToList();
        }, cancellationToken);
    }

    public async Task<int> GetUnacknowledgedDriftCountAsync(CancellationToken cancellationToken = default)
    {
        const string sql = "SELECT COUNT(*) FROM [IdentityDriftRecords] WHERE [IsAcknowledged] = 0";

        return await ExecuteAsync(async connection =>
        {
            return await connection.QuerySingleAsync<int>(new CommandDefinition(sql, cancellationToken: cancellationToken));
        }, cancellationToken);
    }
}
