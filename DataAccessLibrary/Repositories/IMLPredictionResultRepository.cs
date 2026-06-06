using DataAccessLibrary.Models;

namespace DataAccessLibrary.Repositories;

/// <summary>
/// Repository for pre-computed ML prediction results.
/// </summary>
public interface IMLPredictionResultRepository
{
    Task UpsertPredictionAsync(Guid identityId, string modelName, float predictedValue,
        bool? predictedLabel, float? confidence, CancellationToken ct = default);

    Task UpsertPredictionsBulkAsync(List<MLPredictionResultRecord> records, CancellationToken ct = default);

    Task<MLPredictionResultRecord?> GetPredictionAsync(Guid identityId, string modelName, CancellationToken ct = default);

    Task<List<MLPredictionResultRecord>> GetPredictionsByModelAsync(string modelName, int maxResults = 10000, CancellationToken ct = default);

    Task<int> GetCountByModelAndLabelAsync(string modelName, bool label, CancellationToken ct = default);

    Task<int> GetCountByModelAboveThresholdAsync(string modelName, float threshold, CancellationToken ct = default);

    Task<List<MLPredictionResultRecord>> GetTopByModelAsync(string modelName, int take, CancellationToken ct = default);

    Task<DateTime?> GetLastScoredAtAsync(CancellationToken ct = default);

    /// <summary>Get all ML predictions for a given identity (across all models).</summary>
    Task<List<MLPredictionResultRecord>> GetForIdentityAsync(Guid identityId, CancellationToken ct = default);

    /// <summary>Get all ML predictions for a given object (looks up via IdentityId link).</summary>
    Task<List<MLPredictionResultRecord>> GetForObjectAsync(Guid objectId, CancellationToken ct = default);

    /// <summary>Get all predicted values for a model (for histogram computation).</summary>
    Task<List<float>> GetAllScoresForModelAsync(string modelName, CancellationToken ct = default);
}
