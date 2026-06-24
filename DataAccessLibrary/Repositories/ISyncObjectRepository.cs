using DataAccessLibrary.Models;

namespace DataAccessLibrary.Repositories;

/// <summary>
/// Object/Group/Identity CRUD, bulk load, memberships, tags, and audit operations.
/// </summary>
public interface ISyncObjectRepository
{
    Task<ObjectWithAttributes?> FindObjectBySourceUniqueIdAsync(
        Guid sourceConnectionId, string sourceUniqueId, CancellationToken cancellationToken = default);

    Task<Dictionary<string, ObjectWithAttributes>> BulkLoadExistingObjectsAsync(
        Guid sourceConnectionId, CancellationToken cancellationToken = default);

    Task<IdentityLookupCache> BulkLoadIdentitiesAsync(CancellationToken cancellationToken = default);

    Task<UpsertResult> UpsertObjectWithAttributesAsync(
        IdentityObject identityObject, List<ObjectAttribute> attributes, CancellationToken cancellationToken = default);

    Task<int> BulkInsertAuditLogsAsync(
        List<SyncAuditLog> auditLogs, CancellationToken cancellationToken = default);

    Task<BulkUpsertResult> BulkUpsertObjectsAsync(
        List<(IdentityObject identityObject, List<ObjectAttribute> attributes)> objectsWithAttributes,
        CancellationToken cancellationToken = default);

    Task<BulkUpsertResult> FastBulkUpsertObjectsAsync(
        List<(IdentityObject identityObject, List<ObjectAttribute> attributes)> objectsWithAttributes,
        CancellationToken cancellationToken = default, Func<int, int, Task>? onProgress = null);

    Task<BulkUpsertResult> TrueBulkUpsertObjectsAsync(
        List<(IdentityObject identityObject, List<ObjectAttribute> attributes)> objectsWithAttributes,
        CancellationToken cancellationToken = default);

    Task<BulkUpsertResult> BulkUpsertGroupsAsync(
        List<(Group group, List<GroupAttribute> attributes)> groupsWithAttributes,
        CancellationToken cancellationToken = default);

    Task<IdentityObject?> FindObjectByEmailAsync(string email, CancellationToken cancellationToken = default);

    Task<Guid> CreateIdentityAsync(Identity identity, CancellationToken cancellationToken = default);

    Task UpdateObjectIdentityLinkAsync(Guid objectId, Guid identityId, CancellationToken cancellationToken = default);

    Task<Dictionary<string, GroupWithAttributes>> BulkLoadExistingGroupsAsync(
        Guid sourceConnectionId, CancellationToken cancellationToken = default);

    Task<UpsertResult> UpsertGroupWithAttributesAsync(
        Group group, List<GroupAttribute> attributes, CancellationToken cancellationToken = default);

    Task<GroupWithAttributes?> FindGroupBySourceUniqueIdAsync(
        Guid sourceConnectionId, string sourceUniqueId, CancellationToken cancellationToken = default);

    Task<List<ObjectWithAttributes>> GetUnmatchedObjectsFromRunAsync(
        Guid syncProjectRunId, CancellationToken cancellationToken = default);

    Task<(int TotalSynced, int AlreadyMatched, int NeedingMatch)> GetUserObjectCountsFromRunAsync(
        Guid syncProjectRunId, CancellationToken cancellationToken = default);

    Task<Dictionary<string, Guid>> GetObjectIdsBySourceUniqueIdsAsync(
        Guid sourceConnectionId, List<string> sourceUniqueIds, CancellationToken cancellationToken = default);

    Task<Dictionary<string, Guid>> GetObjectIdsByDistinguishedNamesAsync(
        Guid sourceConnectionId, List<string> distinguishedNames, CancellationToken cancellationToken = default);

    Task<Dictionary<string, Guid>> GetObjectIdsByUserPrincipalNamesAsync(
        Guid sourceConnectionId, List<string> userPrincipalNames, CancellationToken cancellationToken = default);

    Task<List<ObjectWithAttributes>> GetAllUnmatchedUserObjectsAsync(
        Guid sourceConnectionId, CancellationToken cancellationToken = default);

    Task UpdateObjectIdentityIdAsync(Guid objectId, Guid personId, CancellationToken cancellationToken = default);

    Task<int> BulkUpsertObjectGroupMembershipsAsync(
        List<(Guid ObjectId, Guid GroupId, bool IsDirect, bool IsPrimary)> memberships,
        CancellationToken cancellationToken = default);

    Task<int> BulkInsertSignInLogsAsync(
        List<SignInLog> logs, CancellationToken cancellationToken = default);

    /// <summary>
    /// Upsert Entra license-assignment data pushed by Conduit. Upserts the org-level
    /// <c>LicensePools</c> SKU inventory (keyed on connection + SkuId) and the per-user
    /// <c>LicenseAssignments</c> (keyed on pool + ObjectId). Idempotent: re-posting the
    /// same data updates capacity/usage in place rather than duplicating. Returns the
    /// count of assignment rows upserted.
    /// </summary>
    Task<(int PoolsUpserted, int AssignmentsPersisted)> BulkUpsertLicenseAssignmentsAsync(
        Guid sourceConnectionId,
        List<LicensePoolUpsert> pools,
        List<LicenseAssignmentUpsert> assignments,
        CancellationToken cancellationToken = default);

    Task<int> MarkRemovedObjectGroupMembershipsAsync(
        Guid objectId, List<Guid> currentGroupIds, CancellationToken cancellationToken = default);

    Task<int> BulkInsertIdentitiesAsync(List<Identity> identities, CancellationToken cancellationToken = default);

    Task<int> BulkAssignTagToObjectsAsync(
        Guid tagId, List<Guid> objectIds, bool isInherited = true, CancellationToken cancellationToken = default);

    Task<int> BulkAssignTagToObjectsBySourceAsync(
        Guid tagId, Guid sourceConnectionId, List<string> sourceUniqueIds,
        bool isInherited = true, CancellationToken cancellationToken = default);

    Task<List<ObjectWithAttributes>> GetUnlinkedObjectsAsync(
        string objectClass, int limit = 50, CancellationToken cancellationToken = default);

    Task<List<ObjectWithAttributes>> GetObjectsByIdsAsync(
        List<Guid> objectIds, CancellationToken cancellationToken = default);

    Task<int> GetCountAsync(string tableName, string? whereClause = null, CancellationToken cancellationToken = default);

    Task<DataStatisticsResult> GetDataStatisticsAsync(CancellationToken cancellationToken = default);
}

/// <summary>One org-level license SKU pool to upsert (keyed on connection + SkuId).</summary>
public sealed record LicensePoolUpsert(
    string SkuId,
    string SkuName,
    string? SkuPartNumber,
    int TotalUnits,
    int ConsumedUnits,
    int WarningUnits,
    int SuspendedUnits);

/// <summary>One per-user license assignment to upsert. ObjectId is already resolved
/// (server-side, never client-trusted); SkuId links it to its pool.</summary>
public sealed record LicenseAssignmentUpsert(
    Guid ObjectId,
    string SkuId,
    DateTime? AssignedAt,
    string AssignmentSource);
