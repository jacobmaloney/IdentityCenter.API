using DataAccessLibrary.Models;

namespace DataAccessLibrary.Repositories;

/// <summary>
/// Repository for effective access materialization and blast radius data.
/// </summary>
public interface IEffectiveAccessRepository
{
    /// <summary>
    /// Gets all active direct group memberships (object -> group).
    /// </summary>
    Task<List<EffectiveAccessModels.DirectMembership>> GetAllDirectMembershipsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all group-to-group nesting relationships.
    /// </summary>
    Task<List<EffectiveAccessModels.GroupNesting>> GetAllGroupNestingsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Truncates and rebuilds effective access entries in a transaction.
    /// </summary>
    Task RebuildEffectiveAccessAsync(IEnumerable<EffectiveAccessModels.EffectiveAccessEntry> entries, CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates blast radius data for all groups.
    /// </summary>
    Task UpsertGroupBlastRadiiAsync(IEnumerable<EffectiveAccessModels.GroupBlastRadiusRecord> records, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets effective access entries for a specific object.
    /// </summary>
    Task<List<EffectiveAccessModels.EffectiveAccessEntry>> GetEffectiveAccessForObjectAsync(Guid objectId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets top groups by blast radius score.
    /// </summary>
    Task<List<EffectiveAccessModels.GroupBlastRadiusRecord>> GetTopBlastRadiusGroupsAsync(int top = 20, CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates identity-level effective access summaries.
    /// </summary>
    Task UpdateIdentityEffectiveAccessAsync(Guid identityId, int effectiveGroupCount, int effectiveAdminGroupCount, int maxDepth, CancellationToken cancellationToken = default);
}
