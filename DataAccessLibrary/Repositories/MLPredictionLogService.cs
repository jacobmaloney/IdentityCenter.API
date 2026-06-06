using Dapper;
using Logging;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;

namespace DataAccessLibrary.Repositories;

public class MLPredictionLogService : IMLPredictionLogService
{
    private readonly string _connectionString;
    private readonly IGlobalLogger _logger;

    public MLPredictionLogService(IConfiguration config, IGlobalLogger logger)
    {
        _connectionString = config.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("DefaultConnection missing");
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    private SqlConnection CreateConnection() => new(_connectionString);

    public async Task LogPredictionAsync(MLPredictionLogEntry entry, CancellationToken ct = default)
    {
        // Idempotent insert: WHERE NOT EXISTS prevents the snapshot job re-run from raising
        // a unique-key violation on (ModelName, EntityId, HorizonDays, PredictedDate).
        const string sql = @"
            INSERT INTO MLPredictionLog
                (Id, ModelName, ModelVersion, EntityId, EntityType, PredictedAt,
                 PredictionValue, FeatureSnapshotJson, HorizonDays)
            SELECT NEWID(), @ModelName, @ModelVersion, @EntityId, @EntityType, @PredictedAt,
                   @PredictionValue, @FeatureSnapshotJson, @HorizonDays
            WHERE NOT EXISTS (
                SELECT 1 FROM MLPredictionLog
                WHERE ModelName = @ModelName
                  AND EntityId = @EntityId
                  AND ((HorizonDays IS NULL AND @HorizonDays IS NULL) OR HorizonDays = @HorizonDays)
                  AND PredictedDate = CAST(@PredictedAt AS DATE)
            );";

        try
        {
            using var conn = CreateConnection();
            await conn.OpenAsync(ct);
            await conn.ExecuteAsync(new CommandDefinition(sql, new
            {
                entry.ModelName,
                entry.ModelVersion,
                entry.EntityId,
                entry.EntityType,
                entry.PredictedAt,
                entry.PredictionValue,
                entry.FeatureSnapshotJson,
                entry.HorizonDays
            }, cancellationToken: ct));
        }
        catch (Exception ex)
        {
            var horizonStr = entry.HorizonDays.HasValue ? entry.HorizonDays.Value.ToString() : "headline";
            _logger.LogWarning(ex, "MLPredictionLogService.LogPredictionAsync failed for {Model}/{Entity}/{Horizon}",
                entry.ModelName, entry.EntityId, horizonStr);
        }
    }

    public async Task<int> BackfillActualsAsync(string modelName, CancellationToken ct = default)
    {
        // Match each pending prediction to the LicenseUsageSnapshot whose SnapshotDate equals
        // PredictedDate + HorizonDays. Realized utilization is consumed/total * 100. Headline
        // rows (HorizonDays IS NULL) are excluded by design — they're audit/trend-only.
        const string sql = @"
            UPDATE p SET
                p.ActualValue = (CAST(s.ConsumedUnits AS FLOAT) / NULLIF(s.TotalUnits, 0)) * 100.0,
                p.ActualMeasuredAt = GETUTCDATE()
            FROM MLPredictionLog p
            INNER JOIN LicenseUsageSnapshots s
                ON s.LicensePoolId = p.EntityId
               AND s.SnapshotDate = CAST(DATEADD(day, p.HorizonDays, p.PredictedAt) AS DATE)
            WHERE p.ModelName = @ModelName
              AND p.ActualValue IS NULL
              AND p.HorizonDays IS NOT NULL
              AND DATEADD(day, p.HorizonDays, p.PredictedAt) <= GETUTCDATE()
              AND s.TotalUnits > 0;";

        try
        {
            using var conn = CreateConnection();
            await conn.OpenAsync(ct);
            var rows = await conn.ExecuteAsync(new CommandDefinition(sql,
                new { ModelName = modelName }, cancellationToken: ct));
            return rows;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "MLPredictionLogService.BackfillActualsAsync failed for {Model}", modelName);
            return 0;
        }
    }

    public async Task<DriftStats?> ComputeRolling7DayMaeAsync(string modelName, CancellationToken ct = default)
    {
        const string aggregateSql = @"
            SELECT
                AVG(ABS(PredictionValue - ActualValue)) AS Mae,
                COUNT(*)                                AS Sample
            FROM MLPredictionLog
            WHERE ModelName = @ModelName
              AND ActualValue IS NOT NULL
              AND ActualMeasuredAt >= DATEADD(day, -7, GETUTCDATE());";

        const string perEntitySql = @"
            SELECT EntityId, AVG(ABS(PredictionValue - ActualValue)) AS Mae
            FROM MLPredictionLog
            WHERE ModelName = @ModelName
              AND ActualValue IS NOT NULL
              AND ActualMeasuredAt >= DATEADD(day, -7, GETUTCDATE())
            GROUP BY EntityId
            ORDER BY Mae DESC;";

        try
        {
            using var conn = CreateConnection();
            await conn.OpenAsync(ct);

            var aggregate = await conn.QuerySingleOrDefaultAsync<(double? Mae, int Sample)>(
                new CommandDefinition(aggregateSql, new { ModelName = modelName }, cancellationToken: ct));

            if (aggregate.Sample == 0 || aggregate.Mae == null)
            {
                return new DriftStats(0, 0, new Dictionary<Guid, double>());
            }

            var perEntity = await conn.QueryAsync<(Guid EntityId, double Mae)>(
                new CommandDefinition(perEntitySql, new { ModelName = modelName }, cancellationToken: ct));

            return new DriftStats(
                Rolling7dMae: aggregate.Mae.Value,
                Sample7dCount: aggregate.Sample,
                PerEntityMae: perEntity.ToDictionary(r => r.EntityId, r => r.Mae));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "MLPredictionLogService.ComputeRolling7DayMaeAsync failed for {Model}", modelName);
            return null;
        }
    }

    public async Task<int> CleanupOrphanRowsAsync(string modelName, int olderThanDays = 90, CancellationToken ct = default)
    {
        // HorizonDays <= 90 evaluates false when HorizonDays IS NULL, so headline rows are
        // automatically retained. Only horizon rows that never got an actual are eligible.
        const string sql = @"
            DELETE FROM MLPredictionLog
            WHERE ModelName = @ModelName
              AND ActualValue IS NULL
              AND HorizonDays <= 90
              AND PredictedAt < DATEADD(day, -@OlderThanDays, GETUTCDATE());";

        try
        {
            using var conn = CreateConnection();
            await conn.OpenAsync(ct);
            return await conn.ExecuteAsync(new CommandDefinition(sql,
                new { ModelName = modelName, OlderThanDays = olderThanDays }, cancellationToken: ct));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "MLPredictionLogService.CleanupOrphanRowsAsync failed for {Model}", modelName);
            return 0;
        }
    }
}
