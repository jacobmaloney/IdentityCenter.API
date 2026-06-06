namespace DataAccessLibrary.Repositories;

/// <summary>
/// Forecast Slice D telemetry. Records per-pool, per-horizon predictions on the daily
/// snapshot job and backfills actual outcomes once the horizon date is reached so a
/// rolling MAE can be compared to training-time MAE for drift detection.
/// </summary>
public interface IMLPredictionLogService
{
    /// <summary>
    /// Insert one prediction row. Idempotent — duplicate (ModelName, EntityId, HorizonDays,
    /// PredictedDate) silently no-ops so the snapshot job is safe to re-run.
    /// </summary>
    Task LogPredictionAsync(MLPredictionLogEntry entry, CancellationToken ct = default);

    /// <summary>
    /// For all rows whose horizon date has passed and ActualValue is still NULL, look up the
    /// matching LicenseUsageSnapshot and write the realized utilization. Headline rows
    /// (HorizonDays IS NULL) are intentionally excluded — they're kept indefinitely for trend
    /// reporting but never backfilled.
    /// </summary>
    Task<int> BackfillActualsAsync(string modelName, CancellationToken ct = default);

    /// <summary>
    /// Compute rolling 7-day MAE across all backfilled predictions for the model, plus a
    /// per-entity breakdown so the drift job can surface the worst pools.
    /// </summary>
    Task<DriftStats?> ComputeRolling7DayMaeAsync(string modelName, CancellationToken ct = default);

    /// <summary>
    /// Drop never-backfilled rows older than the TTL. Only rows with HorizonDays &lt;= 90
    /// are eligible — headline (NULL) rows live indefinitely.
    /// </summary>
    Task<int> CleanupOrphanRowsAsync(string modelName, int olderThanDays = 90, CancellationToken ct = default);
}

public record MLPredictionLogEntry(
    string ModelName,
    string? ModelVersion,
    Guid EntityId,
    string EntityType,
    DateTime PredictedAt,
    double PredictionValue,
    int? HorizonDays,
    string? FeatureSnapshotJson);

public record DriftStats(
    double Rolling7dMae,
    int Sample7dCount,
    Dictionary<Guid, double> PerEntityMae);
