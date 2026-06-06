using DataAccessLibrary.Models;

namespace DataAccessLibrary.Repositories;

/// <summary>
/// Repository for entropy snapshots and drift tracking.
/// </summary>
public interface IEntropyRepository
{
    /// <summary>
    /// Inserts an entropy snapshot.
    /// </summary>
    Task InsertEntropySnapshotAsync(string snapshotType, decimal score, string? componentsJson, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets latest entropy snapshots by type.
    /// </summary>
    Task<List<EntropyModels.EntropySnapshot>> GetLatestSnapshotsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets entropy trend data for a specific type.
    /// </summary>
    Task<List<EntropyModels.EntropySnapshot>> GetEntropyTrendAsync(string snapshotType, int days = 30, CancellationToken cancellationToken = default);

    /// <summary>
    /// Inserts a drift record for an identity.
    /// </summary>
    Task InsertDriftRecordAsync(Guid identityId, string driftType, decimal magnitude, string? previousValue, string? currentValue, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets unacknowledged drift records, ordered by magnitude descending.
    /// </summary>
    Task<List<EntropyModels.DriftRecord>> GetTopDriftersAsync(int top = 20, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets drift records for a specific identity.
    /// </summary>
    Task<List<EntropyModels.DriftRecord>> GetDriftRecordsForIdentityAsync(Guid identityId, int days = 30, CancellationToken cancellationToken = default);

    /// <summary>
    /// Acknowledges a drift record.
    /// </summary>
    Task AcknowledgeDriftAsync(Guid driftRecordId, string acknowledgedBy, CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates identity drift score and sync baselines.
    /// </summary>
    Task UpdateIdentityDriftDataAsync(Guid identityId, decimal driftScore, int groupCount, decimal riskScore, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets identity drift baselines (group count + risk score at last sync).
    /// </summary>
    Task<List<EntropyModels.IdentityDriftBaseline>> GetAllIdentityDriftBaselinesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the count of unacknowledged drift records.
    /// </summary>
    Task<int> GetUnacknowledgedDriftCountAsync(CancellationToken cancellationToken = default);
}
