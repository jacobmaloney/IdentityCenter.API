namespace DataAccessLibrary.Repositories;

/// <summary>
/// Repository for ML model metadata persistence.
/// </summary>
public interface IMLModelRepository
{
    Task<Guid> InsertModelMetadataAsync(string modelName, int version, int sampleCount,
        double? accuracy, double? rSquared, double? rmse, string? modelFilePath,
        string? trainingParameters, CancellationToken cancellationToken = default);

    Task ActivateModelAsync(Guid modelId, string modelName, CancellationToken cancellationToken = default);

    Task<MLModelMetadataRecord?> GetActiveModelAsync(string modelName, CancellationToken cancellationToken = default);

    Task<List<MLModelMetadataRecord>> GetModelHistoryAsync(string modelName, int take = 10, CancellationToken cancellationToken = default);

    Task<int> GetNextVersionAsync(string modelName, CancellationToken cancellationToken = default);

    Task<List<MLModelMetadataRecord>> GetAllActiveModelsAsync(CancellationToken cancellationToken = default);
}

public class MLModelMetadataRecord
{
    public Guid Id { get; set; }
    public string ModelName { get; set; } = string.Empty;
    public int ModelVersion { get; set; }
    public DateTime TrainedAt { get; set; }
    public int TrainingSampleCount { get; set; }
    public double? Accuracy { get; set; }
    public double? RSquared { get; set; }
    public double? RMSE { get; set; }
    public string? ModelFilePath { get; set; }
    public bool IsActive { get; set; }
    public string? TrainingParameters { get; set; }
}
