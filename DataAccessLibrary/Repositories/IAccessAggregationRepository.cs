using DataAccessLibrary.Models;

namespace DataAccessLibrary.Repositories;

/// <summary>
/// Lightweight repository that consolidates the data needed for the
/// "Access" tab on Identity / Objects detail panels. Returns groups and
/// license entitlements in a single round-trip per identity (or per object)
/// so the lazy-loaded tab renders quickly.
/// </summary>
public interface IAccessAggregationRepository
{
    /// <summary>
    /// Returns all directory group memberships and license assignments for
    /// the given identity, by walking every linked Object on that identity.
    /// </summary>
    Task<IdentityAccessPayload> GetIdentityAccessAsync(Guid identityId, CancellationToken ct = default);

    /// <summary>
    /// Returns directory group memberships and license assignments for a
    /// single Object. Used by the Objects.razor Access tab.
    /// </summary>
    Task<IdentityAccessPayload> GetObjectAccessAsync(Guid objectId, CancellationToken ct = default);

    /// <summary>
    /// Returns the most recent completed access-review decision date for
    /// any item targeting this identity (or any of its linked Objects).
    /// Returns null if no decision is recorded.
    /// </summary>
    Task<DateTime?> GetLastReviewedDateAsync(Guid identityId, CancellationToken ct = default);

    /// <summary>
    /// Returns true if the supplied group name matches the privileged-group
    /// keyword set ("admin", "administrator", "privileged", "domain admin",
    /// "schema admin", "enterprise admin", "backup operators",
    /// "account operators"). Pure function — exposed for tests.
    /// </summary>
    bool IsPrivilegedGroupName(string? groupName);
}
