using DataAccessLibrary.Models;

namespace DataAccessLibrary.Repositories;

/// <summary>
/// Manager/owner resolution, person matching, and identity manager operations.
/// </summary>
public interface ISyncRelationshipRepository
{
    Task<Identity?> FindIdentityByEmailAsync(string email, CancellationToken cancellationToken = default);

    Task<List<Identity>> FindIdentitiesByNameAsync(
        string firstName, string lastName, CancellationToken cancellationToken = default);

    Task<Identity?> FindIdentityByIdAsync(Guid identityId, CancellationToken cancellationToken = default);

    Task<Identity?> FindIdentityByEmployeeIdAsync(string employeeId, CancellationToken cancellationToken = default);

    Task<Identity?> FindIdentityByUsernameAsync(string username, CancellationToken cancellationToken = default);

    Task<Identity?> FindIdentityByUPNAsync(string upn, CancellationToken cancellationToken = default);

    Task<Identity?> FindIdentityByDisplayNameAsync(string displayName, CancellationToken cancellationToken = default);

    Task<List<ObjectWithAttributes>> GetObjectsWithManagerAttributeAsync(
        Guid syncProjectRunId, CancellationToken cancellationToken = default);

    Task UpdateObjectManagerIdAsync(Guid objectId, Guid managerObjectId, CancellationToken cancellationToken = default);

    Task<int> BulkUpdateManagerIdsAsync(
        List<(Guid ObjectId, Guid ManagerObjectId)> updates, CancellationToken cancellationToken = default);

    Task<int> ResolveManagerRelationshipsAsync(
        Guid sourceConnectionId, CancellationToken cancellationToken = default);

    Task<(int TotalWithManagerDN, int AlreadyResolved, int NeedingResolution)> GetManagerResolutionStatsAsync(
        Guid sourceConnectionId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets objects needing manager resolution for audit logging purposes.
    /// Returns object details including which ones were resolved after the update.
    /// </summary>
    Task<List<ManagerResolutionAuditItem>> GetManagerResolutionDetailsAsync(
        Guid sourceConnectionId, CancellationToken cancellationToken = default);

    Task<int> ResolveGroupOwnerRelationshipsAsync(
        Guid sourceConnectionId, CancellationToken cancellationToken = default);

    Task<ObjectWithAttributes?> FindObjectByDNAsync(
        Guid sourceConnectionId, string distinguishedName, CancellationToken cancellationToken = default);

    Task UpdateGroupOwnerIdAsync(Guid groupId, Guid ownerId, CancellationToken cancellationToken = default);

    Task<List<GroupWithAttributes>> GetGroupsWithOwnerAttributeAsync(
        Guid syncProjectRunId, CancellationToken cancellationToken = default);

    Task<List<IdentityManagerInfo>> GetIdentitiesNeedingManagerAssignmentAsync(
        CancellationToken cancellationToken = default);

    Task UpdateIdentityManagerIdAsync(
        Guid identityId, Guid? managerIdentityId, CancellationToken cancellationToken = default);

    Task<int> BulkUpdateIdentityManagerIdsAsync(
        List<(Guid IdentityId, Guid ManagerIdentityId)> updates, CancellationToken cancellationToken = default);
}
