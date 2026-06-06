using Dapper;
using Logging;
using Microsoft.Extensions.Configuration;

namespace DataAccessLibrary.Repositories;

/// <summary>
/// Dapper-based repository for ML model metadata.
/// </summary>
public class MLModelRepository : DapperRepositoryBase, IMLModelRepository
{
    public MLModelRepository(IConfiguration configuration, IGlobalLogger logger)
        : base(configuration, logger)
    {
    }

    public async Task<Guid> InsertModelMetadataAsync(string modelName, int version, int sampleCount,
        double? accuracy, double? rSquared, double? rmse, string? modelFilePath,
        string? trainingParameters, CancellationToken cancellationToken = default)
    {
        const string sql = @"
            DECLARE @NewId UNIQUEIDENTIFIER = NEWID();
            INSERT INTO [MLModelMetadata]
                ([Id], [ModelName], [ModelVersion], [TrainingSampleCount], [Accuracy], [RSquared], [RMSE],
                 [ModelFilePath], [IsActive], [TrainingParameters], [TrainedAt])
            VALUES
                (@NewId, @ModelName, @Version, @SampleCount, @Accuracy, @RSquared, @RMSE,
                 @ModelFilePath, 0, @TrainingParameters, SYSUTCDATETIME());
            SELECT @NewId;";

        return await ExecuteAsync(async connection =>
        {
            return await connection.QuerySingleAsync<Guid>(new CommandDefinition(
                sql,
                new { ModelName = modelName, Version = version, SampleCount = sampleCount, Accuracy = accuracy, RSquared = rSquared, RMSE = rmse, ModelFilePath = modelFilePath, TrainingParameters = trainingParameters },
                cancellationToken: cancellationToken));
        }, cancellationToken);
    }

    public async Task ActivateModelAsync(Guid modelId, string modelName, CancellationToken cancellationToken = default)
    {
        const string sql = @"
            UPDATE [MLModelMetadata] SET [IsActive] = 0 WHERE [ModelName] = @ModelName AND [IsActive] = 1;
            UPDATE [MLModelMetadata] SET [IsActive] = 1 WHERE [Id] = @ModelId;";

        await ExecuteNonQueryAsync(async connection =>
        {
            await connection.ExecuteAsync(new CommandDefinition(sql, new { ModelId = modelId, ModelName = modelName }, cancellationToken: cancellationToken));
        }, cancellationToken);
    }

    public async Task<MLModelMetadataRecord?> GetActiveModelAsync(string modelName, CancellationToken cancellationToken = default)
    {
        const string sql = @"
            SELECT [Id], [ModelName], [ModelVersion], [TrainedAt], [TrainingSampleCount],
                   [Accuracy], [RSquared], [RMSE], [ModelFilePath], [IsActive], [TrainingParameters]
            FROM [MLModelMetadata]
            WHERE [ModelName] = @ModelName AND [IsActive] = 1";

        return await ExecuteAsync(async connection =>
        {
            return await connection.QuerySingleOrDefaultAsync<MLModelMetadataRecord>(
                new CommandDefinition(sql, new { ModelName = modelName }, cancellationToken: cancellationToken));
        }, cancellationToken);
    }

    public async Task<List<MLModelMetadataRecord>> GetModelHistoryAsync(string modelName, int take = 10, CancellationToken cancellationToken = default)
    {
        const string sql = @"
            SELECT TOP (@Take)
                [Id], [ModelName], [ModelVersion], [TrainedAt], [TrainingSampleCount],
                [Accuracy], [RSquared], [RMSE], [ModelFilePath], [IsActive], [TrainingParameters]
            FROM [MLModelMetadata]
            WHERE [ModelName] = @ModelName
            ORDER BY [TrainedAt] DESC";

        return await ExecuteAsync(async connection =>
        {
            var results = await connection.QueryAsync<MLModelMetadataRecord>(
                new CommandDefinition(sql, new { ModelName = modelName, Take = take }, cancellationToken: cancellationToken));
            return results.ToList();
        }, cancellationToken);
    }

    public async Task<int> GetNextVersionAsync(string modelName, CancellationToken cancellationToken = default)
    {
        const string sql = "SELECT ISNULL(MAX([ModelVersion]), 0) + 1 FROM [MLModelMetadata] WHERE [ModelName] = @ModelName";

        return await ExecuteAsync(async connection =>
        {
            return await connection.QuerySingleAsync<int>(
                new CommandDefinition(sql, new { ModelName = modelName }, cancellationToken: cancellationToken));
        }, cancellationToken);
    }

    public async Task<List<MLModelMetadataRecord>> GetAllActiveModelsAsync(CancellationToken cancellationToken = default)
    {
        const string sql = @"
            SELECT [Id], [ModelName], [ModelVersion], [TrainedAt], [TrainingSampleCount],
                   [Accuracy], [RSquared], [RMSE], [ModelFilePath], [IsActive], [TrainingParameters]
            FROM [MLModelMetadata]
            WHERE [IsActive] = 1
            ORDER BY [ModelName]";

        return await ExecuteAsync(async connection =>
        {
            var results = await connection.QueryAsync<MLModelMetadataRecord>(
                new CommandDefinition(sql, cancellationToken: cancellationToken));
            return results.ToList();
        }, cancellationToken);
    }
}
