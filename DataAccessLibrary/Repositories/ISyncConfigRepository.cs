using DataAccessLibrary.Models;

namespace DataAccessLibrary.Repositories;

public interface ISyncConfigRepository
{
    // Full tree loading (replaces 3-level Include chain)
    Task<SyncProject?> GetSyncProjectWithFullTreeAsync(Guid projectId);

    // Sync Workflows
    Task<List<SyncWorkflow>> GetSyncWorkflowsForProjectAsync(Guid projectId);
    Task<SyncWorkflow?> GetSyncWorkflowAsync(Guid id);
    Task<Guid> CreateSyncWorkflowAsync(SyncWorkflow workflow);
    Task UpdateSyncWorkflowAsync(SyncWorkflow workflow);
    Task DeleteSyncWorkflowAsync(Guid id);

    // Sync Steps
    Task<List<SyncStep>> GetSyncStepsForWorkflowAsync(Guid workflowId);
    Task<SyncStep?> GetSyncStepAsync(Guid id);
    Task<Guid> CreateSyncStepAsync(SyncStep step);
    Task UpdateSyncStepAsync(SyncStep step);
    Task DeleteSyncStepAsync(Guid id);

    // Attribute Mappings
    Task<List<AttributeMapping>> GetAttributeMappingsForStepAsync(Guid stepId);
    Task<Guid> CreateAttributeMappingAsync(AttributeMapping mapping);
    Task UpdateAttributeMappingAsync(AttributeMapping mapping);
    Task DeleteAttributeMappingAsync(Guid id);
    Task DeleteAttributeMappingsForStepAsync(Guid stepId);

    // Sync Project CRUD (extends IAdminRepository coverage)
    Task<Guid> CreateSyncProjectAsync(SyncProject project);
    Task UpdateSyncProjectAsync(SyncProject project);
    Task DeleteSyncProjectAsync(Guid id);

    // Internal Sync Steps
    Task<List<InternalSyncStep>> GetInternalSyncStepsAsync(Guid projectId);
    Task CreateInternalSyncStepAsync(InternalSyncStep step);
    Task CreateInternalSyncStepMappingAsync(InternalSyncStepMapping mapping);

    // Sync Project enable/disable
    Task ToggleSyncProjectEnabledAsync(Guid projectId, bool isEnabled);

    // Cascade deletion (fast raw SQL)
    Task DeleteWorkflowsCascadeAsync(List<Guid> workflowIds);
    Task DeleteStepsCascadeAsync(List<Guid> stepIds);

    // Sync Project Chains
    Task<List<SyncProjectChain>> GetProjectChainsAsync(Guid sourceProjectId);
    Task DeleteProjectChainsAsync(List<Guid> chainIds);
    Task CreateProjectChainAsync(SyncProjectChain chain);

    // Workflow Tags
    Task<List<WorkflowTag>> GetWorkflowTagsAsync(Guid workflowId);
    Task DeleteWorkflowTagAsync(Guid workflowTagId);
    Task CreateWorkflowTagAsync(WorkflowTag tag);

    // Schedule updates
    Task UpdateProjectScheduleAsync(Guid projectId, DateTime? nextScheduledRunAt);
    Task ClearProjectScheduleAsync(Guid projectId);

    // Internal Sync Step operations
    Task UpdateInternalSyncStepEnabledAsync(Guid id, bool isEnabled);
    Task DeleteInternalSyncStepWithMappingsAsync(Guid id);
    Task UpdateInternalSyncStepAsync(InternalSyncStep step);
    Task DeleteInternalSyncStepMappingsAsync(Guid stepId);
    Task BulkUpdateInternalSyncStepScopesAsync(List<Guid> stepIds, string? tagFilter, Guid? sourceConnectionId);

    // Sync Projects query by type
    Task<List<SyncProject>> GetSyncProjectsByTypeAsync(params string[] projectTypes);
    Task<List<SyncProjectChain>> GetEnabledProjectChainsAsync(Guid sourceProjectId);

    // Sync Step scope/filter updates
    Task UpdateSyncStepFilterAndScopesAsync(Guid stepId, string? ldapFilter, string? searchBase, string? excludedSearchBases);
}
