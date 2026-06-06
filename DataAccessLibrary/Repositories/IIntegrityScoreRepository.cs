using DataAccessLibrary.Models;

namespace DataAccessLibrary.Repositories;

/// <summary>
/// Repository for identity integrity score persistence and history.
/// </summary>
public interface IIntegrityScoreRepository
{
    /// <summary>
    /// Updates a single identity's integrity score and related fields.
    /// </summary>
    Task UpdateIdentityIntegrityAsync(Guid identityId, decimal score, string level, string factorsJson, CancellationToken cancellationToken = default);

    /// <summary>
    /// Bulk updates integrity scores for many identities in a single operation.
    /// </summary>
    Task BulkUpdateIntegrityScoresAsync(IEnumerable<IntegrityResult> results, CancellationToken cancellationToken = default);

    /// <summary>
    /// Inserts a history record for trend tracking.
    /// </summary>
    Task InsertIntegrityHistoryAsync(Guid identityId, decimal score, string level, string? factorsJson, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets integrity history for a specific identity.
    /// </summary>
    Task<List<IntegrityHistoryPoint>> GetIntegrityHistoryAsync(Guid identityId, int days = 30, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets organization-wide integrity summary (distribution of levels).
    /// </summary>
    Task<IntegritySummary> GetOrganizationIntegritySummaryAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Inserts a governance action record.
    /// </summary>
    Task<Guid> InsertGovernanceActionAsync(GovernanceActionRecord action, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets governance actions, optionally filtered by identity.
    /// </summary>
    Task<List<GovernanceActionRecord>> GetGovernanceActionsAsync(Guid? identityId = null, int take = 50, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all active identity IDs for bulk processing.
    /// </summary>
    Task<List<Guid>> GetAllActiveIdentityIdsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the org-wide average integrity score for dashboard display.
    /// </summary>
    Task<decimal> GetAverageIntegrityScoreAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets integrity trend data for the organization (daily averages).
    /// </summary>
    Task<List<IntegrityHistoryPoint>> GetOrganizationIntegrityTrendAsync(int days = 30, CancellationToken cancellationToken = default);
}
