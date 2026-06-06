namespace DataAccessLibrary.Repositories;

public interface IMLModelConfigRepository
{
    Task<List<MLModelConfig>> GetAllAsync();
    Task<MLModelConfig?> GetByNameAsync(string modelName);
    Task UpdateScheduleAsync(Guid id, string cronSchedule);
    Task UpdateTargetServerAsync(Guid id, Guid? targetServerId);
    Task UpdateEnabledAsync(Guid id, bool isEnabled);
    Task UpdateLastTrainedAsync(Guid id, DateTime trainedAt, int durationSeconds, int sampleCount, double? accuracy, double? rSquared);
    Task UpdateScoreHistogramAsync(Guid id, string histogramJson);
    Task SetChampionAsync(Guid id, string modelVersion);
    Task UpdateAutoScoreAsync(Guid id, bool autoScore);
}

public class MLModelConfig
{
    public Guid Id { get; set; }
    public string ModelName { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string? Description { get; set; }
    public bool IsEnabled { get; set; } = true;
    public string CronSchedule { get; set; } = "0 0 2 ? * SUN";
    public Guid? TargetServerId { get; set; }
    public DateTime? LastTrainedAt { get; set; }
    public int? LastTrainedDuration { get; set; }
    public int? LastSampleCount { get; set; }
    public double? LastAccuracy { get; set; }
    public double? LastRSquared { get; set; }
    public bool AutoScoreAfterTraining { get; set; } = true;
    public int MinimumSamples { get; set; } = 30;
    public bool IsChampion { get; set; } = true;
    public string? ChampionModelVersion { get; set; }
    public double? ChallengerAccuracy { get; set; }
    public double PromotionThreshold { get; set; } = 0.02;
    public string? LastScoreHistogramJson { get; set; }
    public string? PreviousScoreHistogramJson { get; set; }
    public DateTime? LastDriftCheckAt { get; set; }
    public double DriftAlertThreshold { get; set; } = 0.15;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
