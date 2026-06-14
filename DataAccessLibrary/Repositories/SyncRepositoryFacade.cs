using DataAccessLibrary.Models;

namespace DataAccessLibrary.Repositories;

/// <summary>
/// Facade implementing ISyncRepository by delegating to the 4 focused repositories.
/// Provides backward compatibility for consumers that inject ISyncRepository.
/// </summary>
public class SyncRepositoryFacade : ISyncRepository
{
    private readonly ISyncObjectRepository _objectRepo;
    private readonly ISyncExecutionRepository _executionRepo;
    private readonly ISyncRelationshipRepository _relationshipRepo;
    private readonly ISyncScriptRepository _scriptRepo;

    public SyncRepositoryFacade(
        ISyncObjectRepository objectRepo,
        ISyncExecutionRepository executionRepo,
        ISyncRelationshipRepository relationshipRepo,
        ISyncScriptRepository scriptRepo)
    {
        _objectRepo = objectRepo;
        _executionRepo = executionRepo;
        _relationshipRepo = relationshipRepo;
        _scriptRepo = scriptRepo;
    }

    // ============================================
    // ISyncObjectRepository delegations
    // ============================================

    public Task<ObjectWithAttributes?> FindObjectBySourceUniqueIdAsync(Guid sourceConnectionId, string sourceUniqueId, CancellationToken cancellationToken = default)
        => _objectRepo.FindObjectBySourceUniqueIdAsync(sourceConnectionId, sourceUniqueId, cancellationToken);

    public Task<Dictionary<string, ObjectWithAttributes>> BulkLoadExistingObjectsAsync(Guid sourceConnectionId, CancellationToken cancellationToken = default)
        => _objectRepo.BulkLoadExistingObjectsAsync(sourceConnectionId, cancellationToken);

    public Task<IdentityLookupCache> BulkLoadIdentitiesAsync(CancellationToken cancellationToken = default)
        => _objectRepo.BulkLoadIdentitiesAsync(cancellationToken);

    public Task<UpsertResult> UpsertObjectWithAttributesAsync(IdentityObject identityObject, List<ObjectAttribute> attributes, CancellationToken cancellationToken = default)
        => _objectRepo.UpsertObjectWithAttributesAsync(identityObject, attributes, cancellationToken);

    public Task<int> BulkInsertAuditLogsAsync(List<SyncAuditLog> auditLogs, CancellationToken cancellationToken = default)
        => _objectRepo.BulkInsertAuditLogsAsync(auditLogs, cancellationToken);

    public Task<BulkUpsertResult> BulkUpsertObjectsAsync(List<(IdentityObject identityObject, List<ObjectAttribute> attributes)> objectsWithAttributes, CancellationToken cancellationToken = default)
        => _objectRepo.BulkUpsertObjectsAsync(objectsWithAttributes, cancellationToken);

    public Task<BulkUpsertResult> FastBulkUpsertObjectsAsync(List<(IdentityObject identityObject, List<ObjectAttribute> attributes)> objectsWithAttributes, CancellationToken cancellationToken = default, Func<int, int, Task>? onProgress = null)
        => _objectRepo.FastBulkUpsertObjectsAsync(objectsWithAttributes, cancellationToken, onProgress);

    public Task<BulkUpsertResult> TrueBulkUpsertObjectsAsync(List<(IdentityObject identityObject, List<ObjectAttribute> attributes)> objectsWithAttributes, CancellationToken cancellationToken = default)
        => _objectRepo.TrueBulkUpsertObjectsAsync(objectsWithAttributes, cancellationToken);

    public Task<BulkUpsertResult> BulkUpsertGroupsAsync(List<(Group group, List<GroupAttribute> attributes)> groupsWithAttributes, CancellationToken cancellationToken = default)
        => _objectRepo.BulkUpsertGroupsAsync(groupsWithAttributes, cancellationToken);

    public Task<IdentityObject?> FindObjectByEmailAsync(string email, CancellationToken cancellationToken = default)
        => _objectRepo.FindObjectByEmailAsync(email, cancellationToken);

    public Task<Guid> CreateIdentityAsync(Identity identity, CancellationToken cancellationToken = default)
        => _objectRepo.CreateIdentityAsync(identity, cancellationToken);

    public Task UpdateObjectIdentityLinkAsync(Guid objectId, Guid identityId, CancellationToken cancellationToken = default)
        => _objectRepo.UpdateObjectIdentityLinkAsync(objectId, identityId, cancellationToken);

    public Task<Dictionary<string, GroupWithAttributes>> BulkLoadExistingGroupsAsync(Guid sourceConnectionId, CancellationToken cancellationToken = default)
        => _objectRepo.BulkLoadExistingGroupsAsync(sourceConnectionId, cancellationToken);

    public Task<UpsertResult> UpsertGroupWithAttributesAsync(Group group, List<GroupAttribute> attributes, CancellationToken cancellationToken = default)
        => _objectRepo.UpsertGroupWithAttributesAsync(group, attributes, cancellationToken);

    public Task<GroupWithAttributes?> FindGroupBySourceUniqueIdAsync(Guid sourceConnectionId, string sourceUniqueId, CancellationToken cancellationToken = default)
        => _objectRepo.FindGroupBySourceUniqueIdAsync(sourceConnectionId, sourceUniqueId, cancellationToken);

    public Task<List<ObjectWithAttributes>> GetUnmatchedObjectsFromRunAsync(Guid syncProjectRunId, CancellationToken cancellationToken = default)
        => _objectRepo.GetUnmatchedObjectsFromRunAsync(syncProjectRunId, cancellationToken);

    public Task<(int TotalSynced, int AlreadyMatched, int NeedingMatch)> GetUserObjectCountsFromRunAsync(Guid syncProjectRunId, CancellationToken cancellationToken = default)
        => _objectRepo.GetUserObjectCountsFromRunAsync(syncProjectRunId, cancellationToken);

    public Task<Dictionary<string, Guid>> GetObjectIdsBySourceUniqueIdsAsync(Guid sourceConnectionId, List<string> sourceUniqueIds, CancellationToken cancellationToken = default)
        => _objectRepo.GetObjectIdsBySourceUniqueIdsAsync(sourceConnectionId, sourceUniqueIds, cancellationToken);

    public Task<Dictionary<string, Guid>> GetObjectIdsByDistinguishedNamesAsync(Guid sourceConnectionId, List<string> distinguishedNames, CancellationToken cancellationToken = default)
        => _objectRepo.GetObjectIdsByDistinguishedNamesAsync(sourceConnectionId, distinguishedNames, cancellationToken);

    public Task<List<ObjectWithAttributes>> GetAllUnmatchedUserObjectsAsync(Guid sourceConnectionId, CancellationToken cancellationToken = default)
        => _objectRepo.GetAllUnmatchedUserObjectsAsync(sourceConnectionId, cancellationToken);

    public Task UpdateObjectIdentityIdAsync(Guid objectId, Guid personId, CancellationToken cancellationToken = default)
        => _objectRepo.UpdateObjectIdentityIdAsync(objectId, personId, cancellationToken);

    public Task<int> BulkUpsertObjectGroupMembershipsAsync(List<(Guid ObjectId, Guid GroupId, bool IsDirect, bool IsPrimary)> memberships, CancellationToken cancellationToken = default)
        => _objectRepo.BulkUpsertObjectGroupMembershipsAsync(memberships, cancellationToken);

    public Task<int> MarkRemovedObjectGroupMembershipsAsync(Guid objectId, List<Guid> currentGroupIds, CancellationToken cancellationToken = default)
        => _objectRepo.MarkRemovedObjectGroupMembershipsAsync(objectId, currentGroupIds, cancellationToken);

    public Task<int> BulkInsertIdentitiesAsync(List<Identity> identities, CancellationToken cancellationToken = default)
        => _objectRepo.BulkInsertIdentitiesAsync(identities, cancellationToken);

    public Task<int> BulkAssignTagToObjectsAsync(Guid tagId, List<Guid> objectIds, bool isInherited = true, CancellationToken cancellationToken = default)
        => _objectRepo.BulkAssignTagToObjectsAsync(tagId, objectIds, isInherited, cancellationToken);

    public Task<int> BulkAssignTagToObjectsBySourceAsync(Guid tagId, Guid sourceConnectionId, List<string> sourceUniqueIds, bool isInherited = true, CancellationToken cancellationToken = default)
        => _objectRepo.BulkAssignTagToObjectsBySourceAsync(tagId, sourceConnectionId, sourceUniqueIds, isInherited, cancellationToken);

    public Task<List<ObjectWithAttributes>> GetUnlinkedObjectsAsync(string objectClass, int limit = 50, CancellationToken cancellationToken = default)
        => _objectRepo.GetUnlinkedObjectsAsync(objectClass, limit, cancellationToken);

    public Task<List<ObjectWithAttributes>> GetObjectsByIdsAsync(List<Guid> objectIds, CancellationToken cancellationToken = default)
        => _objectRepo.GetObjectsByIdsAsync(objectIds, cancellationToken);

    public Task<int> GetCountAsync(string tableName, string? whereClause = null, CancellationToken cancellationToken = default)
        => _objectRepo.GetCountAsync(tableName, whereClause, cancellationToken);

    public Task<DataStatisticsResult> GetDataStatisticsAsync(CancellationToken cancellationToken = default)
        => _objectRepo.GetDataStatisticsAsync(cancellationToken);

    // ============================================
    // ISyncExecutionRepository delegations
    // ============================================

    public Task UpdateStepRunMetricsAsync(Guid stepRunId, int objectsQueried, int objectsProcessed, int objectsCreated, int objectsUpdated, int objectsSkipped, int errorCount, CancellationToken cancellationToken = default, string? status = null, DateTime? completedAt = null, int? durationSeconds = null)
        => _executionRepo.UpdateStepRunMetricsAsync(stepRunId, objectsQueried, objectsProcessed, objectsCreated, objectsUpdated, objectsSkipped, errorCount, cancellationToken, status, completedAt, durationSeconds);

    public Task UpdateStepRunPersonMetricsAsync(Guid stepRunId, int personsCreated, int personsMatched, CancellationToken cancellationToken = default)
        => _executionRepo.UpdateStepRunPersonMetricsAsync(stepRunId, personsCreated, personsMatched, cancellationToken);

    public Task UpdateProjectRunMetricsAsync(Guid runId, int totalObjectsProcessed, int totalObjectsCreated, int totalObjectsUpdated, int totalErrors, int completedSteps, int progressPercentage, CancellationToken cancellationToken = default)
        => _executionRepo.UpdateProjectRunMetricsAsync(runId, totalObjectsProcessed, totalObjectsCreated, totalObjectsUpdated, totalErrors, completedSteps, progressPercentage, cancellationToken);

    public Task BulkInsertSyncProjectAsync(SyncProject project, CancellationToken cancellationToken = default)
        => _executionRepo.BulkInsertSyncProjectAsync(project, cancellationToken);

    public Task BulkInsertWorkflowsAsync(Guid projectId, List<SyncWorkflow> workflows, CancellationToken cancellationToken = default)
        => _executionRepo.BulkInsertWorkflowsAsync(projectId, workflows, cancellationToken);

    public Task<List<string>> GetWorkflowObjectClassesForProjectAsync(Guid projectId, CancellationToken cancellationToken = default)
        => _executionRepo.GetWorkflowObjectClassesForProjectAsync(projectId, cancellationToken);

    public Task<List<SyncProjectListItem>> GetSyncProjectsListAsync(CancellationToken cancellationToken = default)
        => _executionRepo.GetSyncProjectsListAsync(cancellationToken);

    public Task<SyncProjectDetails?> GetSyncProjectDetailsAsync(Guid projectId, CancellationToken cancellationToken = default)
        => _executionRepo.GetSyncProjectDetailsAsync(projectId, cancellationToken);

    public Task<List<SyncProjectRun>> GetSyncRunsForProjectAsync(Guid projectId, int limit = 50, CancellationToken cancellationToken = default)
        => _executionRepo.GetSyncRunsForProjectAsync(projectId, limit, cancellationToken);

    public Task<SyncProjectRun?> GetLatestRunForProjectAsync(Guid projectId, CancellationToken cancellationToken = default)
        => _executionRepo.GetLatestRunForProjectAsync(projectId, cancellationToken);

    public Task<List<SyncProjectRun>> GetRecentSyncRunsAsync(int limit = 50, CancellationToken cancellationToken = default)
        => _executionRepo.GetRecentSyncRunsAsync(limit, cancellationToken);

    public Task<List<SyncProjectRun>> GetRunningSyncProjectRunsAsync(CancellationToken cancellationToken = default)
        => _executionRepo.GetRunningSyncProjectRunsAsync(cancellationToken);

    public Task<SyncRunDetailsData?> GetSyncRunDetailsAsync(Guid runId, CancellationToken cancellationToken = default)
        => _executionRepo.GetSyncRunDetailsAsync(runId, cancellationToken);

    public Task UpdateProjectStatusAsync(Guid projectId, bool? isRunning = null, DateTime? lastRunAt = null, int? totalExecutions = null, int? successfulExecutions = null, int? failedExecutions = null, CancellationToken cancellationToken = default)
        => _executionRepo.UpdateProjectStatusAsync(projectId, isRunning, lastRunAt, totalExecutions, successfulExecutions, failedExecutions, cancellationToken);

    public Task UpdateRunProgressAsync(Guid runId, int? completedSteps = null, int? progressPercentage = null, string? currentStepName = null, string? status = null, DateTime? completedAt = null, string? errorMessage = null, CancellationToken cancellationToken = default)
        => _executionRepo.UpdateRunProgressAsync(runId, completedSteps, progressPercentage, currentStepName, status, completedAt, errorMessage, cancellationToken);

    public Task<Guid> CreateSyncExecutionAsync(Guid directoryConnectionId, DateTime startedAt, string status = "Running", CancellationToken cancellationToken = default)
        => _executionRepo.CreateSyncExecutionAsync(directoryConnectionId, startedAt, status, cancellationToken);

    public Task UpdateSyncExecutionAsync(Guid executionId, string? status = null, DateTime? completedAt = null, int? identitiesAdded = null, int? identitiesUpdated = null, int? identitiesDeleted = null, int? groupsAdded = null, int? groupsUpdated = null, int? groupsDeleted = null, int? membershipsAdded = null, int? membershipsRemoved = null, int? personsCreated = null, int? personsUpdated = null, string? errorMessage = null, string? executionLog = null, CancellationToken cancellationToken = default)
        => _executionRepo.UpdateSyncExecutionAsync(executionId, status, completedAt, identitiesAdded, identitiesUpdated, identitiesDeleted, groupsAdded, groupsUpdated, groupsDeleted, membershipsAdded, membershipsRemoved, personsCreated, personsUpdated, errorMessage, executionLog, cancellationToken);

    public Task DeleteSyncProjectAsync(Guid projectId, CancellationToken cancellationToken = default)
        => _executionRepo.DeleteSyncProjectAsync(projectId, cancellationToken);

    public Task<Guid> CreateSyncProjectRunAsync(SyncProjectRun run, CancellationToken cancellationToken = default)
        => _executionRepo.CreateSyncProjectRunAsync(run, cancellationToken);

    public Task<Guid> CreateSyncStepRunAsync(SyncStepRun stepRun, CancellationToken cancellationToken = default)
        => _executionRepo.CreateSyncStepRunAsync(stepRun, cancellationToken);

    public Task UpdateSyncProjectRunStatusAsync(Guid runId, string status, DateTime? completedAt, int? durationSeconds, string? errorMessage, int totalObjectsProcessed, int totalObjectsCreated, int totalObjectsUpdated, int totalObjectsDeleted, int totalPersonsCreated, int totalErrors, int completedSteps, int progressPercentage, CancellationToken cancellationToken = default)
        => _executionRepo.UpdateSyncProjectRunStatusAsync(runId, status, completedAt, durationSeconds, errorMessage, totalObjectsProcessed, totalObjectsCreated, totalObjectsUpdated, totalObjectsDeleted, totalPersonsCreated, totalErrors, completedSteps, progressPercentage, cancellationToken);

    public Task UpdateSyncProjectExecutionStatusAsync(Guid projectId, bool isRunning, bool success, CancellationToken cancellationToken = default)
        => _executionRepo.UpdateSyncProjectExecutionStatusAsync(projectId, isRunning, success, cancellationToken);

    public Task SynchronizeStepTagsAsync(Guid stepId, List<Guid> tagIds, CancellationToken cancellationToken = default)
        => _executionRepo.SynchronizeStepTagsAsync(stepId, tagIds, cancellationToken);

    public Task InsertStepTagsAsync(Guid stepId, List<Guid> tagIds, CancellationToken cancellationToken = default)
        => _executionRepo.InsertStepTagsAsync(stepId, tagIds, cancellationToken);

    // ============================================
    // ISyncRelationshipRepository delegations
    // ============================================

    public Task<Identity?> FindIdentityByEmailAsync(string email, CancellationToken cancellationToken = default)
        => _relationshipRepo.FindIdentityByEmailAsync(email, cancellationToken);

    public Task<List<Identity>> FindIdentitiesByNameAsync(string firstName, string lastName, CancellationToken cancellationToken = default)
        => _relationshipRepo.FindIdentitiesByNameAsync(firstName, lastName, cancellationToken);

    public Task<Identity?> FindIdentityByIdAsync(Guid identityId, CancellationToken cancellationToken = default)
        => _relationshipRepo.FindIdentityByIdAsync(identityId, cancellationToken);

    public Task<Identity?> FindIdentityByEmployeeIdAsync(string employeeId, CancellationToken cancellationToken = default)
        => _relationshipRepo.FindIdentityByEmployeeIdAsync(employeeId, cancellationToken);

    public Task<Identity?> FindIdentityByUsernameAsync(string username, CancellationToken cancellationToken = default)
        => _relationshipRepo.FindIdentityByUsernameAsync(username, cancellationToken);

    public Task<Identity?> FindIdentityByUPNAsync(string upn, CancellationToken cancellationToken = default)
        => _relationshipRepo.FindIdentityByUPNAsync(upn, cancellationToken);

    public Task<Identity?> FindIdentityByDisplayNameAsync(string displayName, CancellationToken cancellationToken = default)
        => _relationshipRepo.FindIdentityByDisplayNameAsync(displayName, cancellationToken);

    public Task<List<ObjectWithAttributes>> GetObjectsWithManagerAttributeAsync(Guid syncProjectRunId, CancellationToken cancellationToken = default)
        => _relationshipRepo.GetObjectsWithManagerAttributeAsync(syncProjectRunId, cancellationToken);

    public Task UpdateObjectManagerIdAsync(Guid objectId, Guid managerObjectId, CancellationToken cancellationToken = default)
        => _relationshipRepo.UpdateObjectManagerIdAsync(objectId, managerObjectId, cancellationToken);

    public Task<int> BulkUpdateManagerIdsAsync(List<(Guid ObjectId, Guid ManagerObjectId)> updates, CancellationToken cancellationToken = default)
        => _relationshipRepo.BulkUpdateManagerIdsAsync(updates, cancellationToken);

    public Task<int> ResolveManagerRelationshipsAsync(Guid sourceConnectionId, CancellationToken cancellationToken = default)
        => _relationshipRepo.ResolveManagerRelationshipsAsync(sourceConnectionId, cancellationToken);

    public Task<(int TotalWithManagerDN, int AlreadyResolved, int NeedingResolution)> GetManagerResolutionStatsAsync(Guid sourceConnectionId, CancellationToken cancellationToken = default)
        => _relationshipRepo.GetManagerResolutionStatsAsync(sourceConnectionId, cancellationToken);

    public Task<List<ManagerResolutionAuditItem>> GetManagerResolutionDetailsAsync(Guid sourceConnectionId, CancellationToken cancellationToken = default)
        => _relationshipRepo.GetManagerResolutionDetailsAsync(sourceConnectionId, cancellationToken);

    public Task<int> ResolveGroupOwnerRelationshipsAsync(Guid sourceConnectionId, CancellationToken cancellationToken = default)
        => _relationshipRepo.ResolveGroupOwnerRelationshipsAsync(sourceConnectionId, cancellationToken);

    public Task<ObjectWithAttributes?> FindObjectByDNAsync(Guid sourceConnectionId, string distinguishedName, CancellationToken cancellationToken = default)
        => _relationshipRepo.FindObjectByDNAsync(sourceConnectionId, distinguishedName, cancellationToken);

    public Task UpdateGroupOwnerIdAsync(Guid groupId, Guid ownerId, CancellationToken cancellationToken = default)
        => _relationshipRepo.UpdateGroupOwnerIdAsync(groupId, ownerId, cancellationToken);

    public Task<List<GroupWithAttributes>> GetGroupsWithOwnerAttributeAsync(Guid syncProjectRunId, CancellationToken cancellationToken = default)
        => _relationshipRepo.GetGroupsWithOwnerAttributeAsync(syncProjectRunId, cancellationToken);

    public Task<List<IdentityManagerInfo>> GetIdentitiesNeedingManagerAssignmentAsync(CancellationToken cancellationToken = default)
        => _relationshipRepo.GetIdentitiesNeedingManagerAssignmentAsync(cancellationToken);

    public Task UpdateIdentityManagerIdAsync(Guid identityId, Guid? managerIdentityId, CancellationToken cancellationToken = default)
        => _relationshipRepo.UpdateIdentityManagerIdAsync(identityId, managerIdentityId, cancellationToken);

    public Task<int> BulkUpdateIdentityManagerIdsAsync(List<(Guid IdentityId, Guid ManagerIdentityId)> updates, CancellationToken cancellationToken = default)
        => _relationshipRepo.BulkUpdateIdentityManagerIdsAsync(updates, cancellationToken);

    // ============================================
    // ISyncScriptRepository delegations
    // ============================================

    public Task<List<StepScriptInfo>> GetStepScriptsAsync(Guid syncStepId, string executionPhase, CancellationToken cancellationToken = default)
        => _scriptRepo.GetStepScriptsAsync(syncStepId, executionPhase, cancellationToken);

    public Task<SyncProcessingScript?> GetScriptByIdAsync(Guid scriptId, CancellationToken cancellationToken = default)
        => _scriptRepo.GetScriptByIdAsync(scriptId, cancellationToken);

    public Task<Guid> RecordScriptExecutionAsync(SyncScriptExecution execution, CancellationToken cancellationToken = default)
        => _scriptRepo.RecordScriptExecutionAsync(execution, cancellationToken);

    public Task UpdateScriptCompilationStatusAsync(Guid scriptId, string status, string? errorMessage, CancellationToken cancellationToken = default)
        => _scriptRepo.UpdateScriptCompilationStatusAsync(scriptId, status, errorMessage, cancellationToken);

    public Task<List<SyncProcessingScript>> GetAllScriptsAsync(CancellationToken cancellationToken = default)
        => _scriptRepo.GetAllScriptsAsync(cancellationToken);

    public Task<Guid> SaveScriptAsync(SyncProcessingScript script, CancellationToken cancellationToken = default)
        => _scriptRepo.SaveScriptAsync(script, cancellationToken);

    public Task<bool> DeleteScriptAsync(Guid scriptId, CancellationToken cancellationToken = default)
        => _scriptRepo.DeleteScriptAsync(scriptId, cancellationToken);

    public Task AssignScriptToStepAsync(Guid syncStepId, Guid scriptId, string executionPhase, int executionOrder, CancellationToken cancellationToken = default)
        => _scriptRepo.AssignScriptToStepAsync(syncStepId, scriptId, executionPhase, executionOrder, cancellationToken);

    public Task RemoveScriptFromStepAsync(Guid syncStepScriptId, CancellationToken cancellationToken = default)
        => _scriptRepo.RemoveScriptFromStepAsync(syncStepScriptId, cancellationToken);

    public Task AutoAssignPersonMatchingScriptAsync(Guid syncStepId, CancellationToken cancellationToken = default)
        => _scriptRepo.AutoAssignPersonMatchingScriptAsync(syncStepId, cancellationToken);

    public Task RemovePersonMatchingScriptAsync(Guid syncStepId, CancellationToken cancellationToken = default)
        => _scriptRepo.RemovePersonMatchingScriptAsync(syncStepId, cancellationToken);
}
