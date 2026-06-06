using Dapper;
using DataAccessLibrary.Models;
using Logging;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;

namespace DataAccessLibrary.Repositories;

/// <summary>
/// Dapper repository for ML prediction results (batch-persist-then-query pattern).
/// </summary>
public class MLPredictionResultRepository : DapperRepositoryBase, IMLPredictionResultRepository
{
    public MLPredictionResultRepository(IConfiguration configuration, IGlobalLogger logger)
        : base(configuration, logger) { }

    public async Task UpsertPredictionAsync(Guid identityId, string modelName, float predictedValue,
        bool? predictedLabel, float? confidence, CancellationToken ct = default)
    {
        await ExecuteNonQueryAsync(async connection =>
        {
            await connection.ExecuteAsync(new CommandDefinition(@"
                MERGE MLPredictionResults AS target
                USING (SELECT @IdentityId AS IdentityId, @ModelName AS ModelName) AS source
                ON target.IdentityId = source.IdentityId AND target.ModelName = source.ModelName
                WHEN MATCHED THEN
                    UPDATE SET PredictedValue = @PredictedValue, PredictedLabel = @PredictedLabel,
                               Confidence = @Confidence, ScoredAt = SYSUTCDATETIME()
                WHEN NOT MATCHED THEN
                    INSERT (IdentityId, ModelName, PredictedValue, PredictedLabel, Confidence)
                    VALUES (@IdentityId, @ModelName, @PredictedValue, @PredictedLabel, @Confidence);",
                new { IdentityId = identityId, ModelName = modelName, PredictedValue = predictedValue,
                      PredictedLabel = predictedLabel, Confidence = confidence },
                cancellationToken: ct));
        }, ct);
    }

    public async Task UpsertPredictionsBulkAsync(List<MLPredictionResultRecord> records, CancellationToken ct = default)
    {
        if (records.Count == 0) return;

        const int batchSize = 100;

        await ExecuteNonQueryAsync(async connection =>
        {
            for (int i = 0; i < records.Count; i += batchSize)
            {
                ct.ThrowIfCancellationRequested();

                var batch = records.Skip(i).Take(batchSize).ToList();

                // Build multi-row VALUES for a true batch MERGE
                var parameters = new DynamicParameters();
                var valuesClauses = new List<string>();
                for (int j = 0; j < batch.Count; j++)
                {
                    var r = batch[j];
                    parameters.Add($"Id{j}", r.IdentityId);
                    parameters.Add($"Mn{j}", r.ModelName);
                    parameters.Add($"Pv{j}", r.PredictedValue);
                    parameters.Add($"Pl{j}", r.PredictedLabel);
                    parameters.Add($"Co{j}", r.Confidence);
                    valuesClauses.Add($"(@Id{j}, @Mn{j}, @Pv{j}, @Pl{j}, @Co{j})");
                }

                var sql = $@"
                    MERGE MLPredictionResults AS target
                    USING (VALUES {string.Join(", ", valuesClauses)})
                        AS source (IdentityId, ModelName, PredictedValue, PredictedLabel, Confidence)
                    ON target.IdentityId = source.IdentityId AND target.ModelName = source.ModelName
                    WHEN MATCHED THEN
                        UPDATE SET PredictedValue = source.PredictedValue, PredictedLabel = source.PredictedLabel,
                                   Confidence = source.Confidence, ScoredAt = SYSUTCDATETIME()
                    WHEN NOT MATCHED THEN
                        INSERT (IdentityId, ModelName, PredictedValue, PredictedLabel, Confidence)
                        VALUES (source.IdentityId, source.ModelName, source.PredictedValue, source.PredictedLabel, source.Confidence);";

                await connection.ExecuteAsync(new CommandDefinition(sql, parameters, cancellationToken: ct));
            }
        }, ct);
    }

    public async Task<MLPredictionResultRecord?> GetPredictionAsync(Guid identityId, string modelName, CancellationToken ct = default)
    {
        return await ExecuteAsync(async connection =>
        {
            return await connection.QueryFirstOrDefaultAsync<MLPredictionResultRecord>(new CommandDefinition(@"
                SELECT Id, IdentityId, ModelName, PredictedValue, PredictedLabel, Confidence, ScoredAt
                FROM MLPredictionResults
                WHERE IdentityId = @IdentityId AND ModelName = @ModelName",
                new { IdentityId = identityId, ModelName = modelName },
                cancellationToken: ct));
        }, ct);
    }

    public async Task<List<MLPredictionResultRecord>> GetPredictionsByModelAsync(string modelName, int maxResults = 10000, CancellationToken ct = default)
    {
        return await ExecuteAsync(async connection =>
        {
            var results = await connection.QueryAsync<MLPredictionResultRecord>(new CommandDefinition(@"
                SELECT TOP (@MaxResults) Id, IdentityId, ModelName, PredictedValue, PredictedLabel, Confidence, ScoredAt
                FROM MLPredictionResults
                WHERE ModelName = @ModelName
                ORDER BY PredictedValue DESC",
                new { ModelName = modelName, MaxResults = maxResults },
                cancellationToken: ct));
            return results.AsList();
        }, ct);
    }

    public async Task<int> GetCountByModelAndLabelAsync(string modelName, bool label, CancellationToken ct = default)
    {
        return await ExecuteAsync(async connection =>
        {
            return await connection.ExecuteScalarAsync<int>(new CommandDefinition(@"
                SELECT COUNT(*)
                FROM MLPredictionResults
                WHERE ModelName = @ModelName AND PredictedLabel = @Label",
                new { ModelName = modelName, Label = label },
                cancellationToken: ct));
        }, ct);
    }

    public async Task<int> GetCountByModelAboveThresholdAsync(string modelName, float threshold, CancellationToken ct = default)
    {
        return await ExecuteAsync(async connection =>
        {
            return await connection.ExecuteScalarAsync<int>(new CommandDefinition(@"
                SELECT COUNT(*)
                FROM MLPredictionResults
                WHERE ModelName = @ModelName AND PredictedValue >= @Threshold",
                new { ModelName = modelName, Threshold = threshold },
                cancellationToken: ct));
        }, ct);
    }

    public async Task<List<MLPredictionResultRecord>> GetTopByModelAsync(string modelName, int take, CancellationToken ct = default)
    {
        return await ExecuteAsync(async connection =>
        {
            var results = await connection.QueryAsync<MLPredictionResultRecord>(new CommandDefinition(@"
                SELECT TOP (@Take) Id, IdentityId, ModelName, PredictedValue, PredictedLabel, Confidence, ScoredAt
                FROM MLPredictionResults
                WHERE ModelName = @ModelName
                ORDER BY PredictedValue DESC",
                new { ModelName = modelName, Take = take },
                cancellationToken: ct));
            return results.AsList();
        }, ct);
    }

    public async Task<DateTime?> GetLastScoredAtAsync(CancellationToken ct = default)
    {
        return await ExecuteAsync(async connection =>
        {
            return await connection.ExecuteScalarAsync<DateTime?>(new CommandDefinition(@"
                SELECT MAX(ScoredAt) FROM MLPredictionResults",
                cancellationToken: ct));
        }, ct);
    }

    public async Task<List<MLPredictionResultRecord>> GetForIdentityAsync(Guid identityId, CancellationToken ct = default)
    {
        return await ExecuteAsync(async connection =>
        {
            var results = await connection.QueryAsync<MLPredictionResultRecord>(new CommandDefinition(@"
                SELECT * FROM MLPredictionResults WHERE IdentityId = @IdentityId ORDER BY ModelName",
                new { IdentityId = identityId }, cancellationToken: ct));
            return results.AsList();
        }, ct);
    }

    public async Task<List<MLPredictionResultRecord>> GetForObjectAsync(Guid objectId, CancellationToken ct = default)
    {
        return await ExecuteAsync(async connection =>
        {
            var results = await connection.QueryAsync<MLPredictionResultRecord>(new CommandDefinition(@"
                SELECT pr.* FROM MLPredictionResults pr
                INNER JOIN Objects o ON o.IdentityId = pr.IdentityId
                WHERE o.Id = @ObjectId ORDER BY pr.ModelName",
                new { ObjectId = objectId }, cancellationToken: ct));
            return results.AsList();
        }, ct);
    }

    public async Task<List<float>> GetAllScoresForModelAsync(string modelName, CancellationToken ct = default)
    {
        return await ExecuteAsync(async connection =>
        {
            var results = await connection.QueryAsync<float>(new CommandDefinition(@"
                SELECT PredictedValue FROM MLPredictionResults WHERE ModelName = @ModelName",
                new { ModelName = modelName }, cancellationToken: ct));
            return results.AsList();
        }, ct);
    }
}
