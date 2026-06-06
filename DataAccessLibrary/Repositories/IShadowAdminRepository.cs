using DataAccessLibrary.Models;

namespace DataAccessLibrary.Repositories;

/// <summary>
/// Repository for shadow admin detection — identifies users with inherited admin rights
/// through nested group memberships using the EffectiveAccessEntries table.
/// </summary>
public interface IShadowAdminRepository
{
    /// <summary>
    /// Gets users who reach admin groups only through nested memberships (not direct).
    /// </summary>
    Task<List<ShadowAdminRecord>> GetShadowAdminsAsync(CancellationToken ct = default);

    /// <summary>
    /// Gets the count of distinct shadow admin users.
    /// </summary>
    Task<int> GetShadowAdminCountAsync(CancellationToken ct = default);
}
