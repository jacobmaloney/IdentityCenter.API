using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;

namespace DataAccessLibrary.Repositories;

public class MLModelConfigRepository : IMLModelConfigRepository
{
    private readonly string _connectionString;

    public MLModelConfigRepository(IConfiguration config)
    {
        _connectionString = config.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("DefaultConnection missing");
    }

    private SqlConnection CreateConnection() => new(_connectionString);

    public async Task<List<MLModelConfig>> GetAllAsync()
    {
        using var conn = CreateConnection();
        await conn.OpenAsync();
        var rows = await conn.QueryAsync<MLModelConfig>("SELECT * FROM MLModelConfig ORDER BY ModelName");
        return rows.ToList();
    }

    public async Task<MLModelConfig?> GetByNameAsync(string modelName)
    {
        using var conn = CreateConnection();
        await conn.OpenAsync();
        return await conn.QuerySingleOrDefaultAsync<MLModelConfig>(
            "SELECT * FROM MLModelConfig WHERE ModelName = @ModelName",
            new { ModelName = modelName });
    }

    public async Task UpdateScheduleAsync(Guid id, string cronSchedule)
    {
        using var conn = CreateConnection();
        await conn.OpenAsync();
        await conn.ExecuteAsync("UPDATE MLModelConfig SET CronSchedule = @Cron WHERE Id = @Id",
            new { Id = id, Cron = cronSchedule });
    }

    public async Task UpdateTargetServerAsync(Guid id, Guid? targetServerId)
    {
        using var conn = CreateConnection();
        await conn.OpenAsync();
        await conn.ExecuteAsync("UPDATE MLModelConfig SET TargetServerId = @ServerId WHERE Id = @Id",
            new { Id = id, ServerId = targetServerId });
    }

    public async Task UpdateEnabledAsync(Guid id, bool isEnabled)
    {
        using var conn = CreateConnection();
        await conn.OpenAsync();
        await conn.ExecuteAsync("UPDATE MLModelConfig SET IsEnabled = @Enabled WHERE Id = @Id",
            new { Id = id, Enabled = isEnabled });
    }

    public async Task UpdateLastTrainedAsync(Guid id, DateTime trainedAt, int durationSeconds, int sampleCount, double? accuracy, double? rSquared)
    {
        using var conn = CreateConnection();
        await conn.OpenAsync();
        await conn.ExecuteAsync(@"
            UPDATE MLModelConfig SET
                LastTrainedAt = @TrainedAt,
                LastTrainedDuration = @Duration,
                LastSampleCount = @Samples,
                LastAccuracy = @Accuracy,
                LastRSquared = @RSquared
            WHERE Id = @Id",
            new { Id = id, TrainedAt = trainedAt, Duration = durationSeconds, Samples = sampleCount, Accuracy = accuracy, RSquared = rSquared });
    }

    public async Task UpdateScoreHistogramAsync(Guid id, string histogramJson)
    {
        using var conn = CreateConnection();
        await conn.OpenAsync();
        await conn.ExecuteAsync(@"
            UPDATE MLModelConfig SET
                PreviousScoreHistogramJson = LastScoreHistogramJson,
                LastScoreHistogramJson = @Histogram,
                LastDriftCheckAt = GETUTCDATE()
            WHERE Id = @Id",
            new { Id = id, Histogram = histogramJson });
    }

    public async Task SetChampionAsync(Guid id, string modelVersion)
    {
        using var conn = CreateConnection();
        await conn.OpenAsync();
        await conn.ExecuteAsync(@"
            UPDATE MLModelConfig SET
                IsChampion = 1,
                ChampionModelVersion = @Version
            WHERE Id = @Id",
            new { Id = id, Version = modelVersion });
    }

    public async Task UpdateAutoScoreAsync(Guid id, bool autoScore)
    {
        using var conn = CreateConnection();
        await conn.OpenAsync();
        await conn.ExecuteAsync("UPDATE MLModelConfig SET AutoScoreAfterTraining = @Auto WHERE Id = @Id",
            new { Id = id, Auto = autoScore });
    }
}
