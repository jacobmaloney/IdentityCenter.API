using DataAccessLibrary.Models;

namespace DataAccessLibrary.Repositories;

/// <summary>
/// Project/Run CRUD, execution tracking, metrics, status, and stats.
/// </summary>
public interface ISyncExecutionRepository
{
    Task UpdateStepRunMetricsAsync(
        Guid stepRunId, int objectsQueried, int objectsProcessed, int objectsCreated,
        int objectsUpdated, int objectsSkipped, int errorCount,
        CancellationToken cancellationToken = default, string? status = null,
        DateTime? completedAt = null, int? durationSeconds = null);

    Task UpdateStepRunPersonMetricsAsync(
        Guid stepRunId, int personsCreated, int personsMatched, CancellationToken cancellationToken = default);

    Task UpdateProjectRunMetricsAsync(
        Guid runId, int totalObjectsProcessed, int totalObjectsCreated, int totalObjectsUpdated,
        int totalErrors, int completedSteps, int progressPercentage, CancellationToken cancellationToken = default);

    Task BulkInsertSyncProjectAsync(SyncProject project, CancellationToken cancellationToken = default);

    /// <summary>
    /// Bulk inserts workflows (with their steps + attribute mappings) INTO an existing sync project.
    /// Does NOT insert/modify the SyncProjects row. Each workflow's SyncProjectId is forced to
    /// <paramref name="projectId"/> before insert. All inserts run in a single transaction.
    /// </summary>
    Task BulkInsertWorkflowsAsync(Guid projectId, List<SyncWorkflow> workflows, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the distinct ObjectClass values of workflows already present in a sync project.
    /// Used by the in-place backfill path to avoid duplicating object classes.
    /// </summary>
    Task<List<string>> GetWorkflowObjectClassesForProjectAsync(Guid projectId, CancellationToken cancellationToken = default);

    Task<List<SyncProjectListItem>> GetSyncProjectsListAsync(CancellationToken cancellationToken = default);

    Task<SyncProjectDetails?> GetSyncProjectDetailsAsync(Guid projectId, CancellationToken cancellationToken = default);

    Task<List<SyncProjectRun>> GetSyncRunsForProjectAsync(
        Guid projectId, int limit = 50, CancellationToken cancellationToken = default);

    Task<SyncProjectRun?> GetLatestRunForProjectAsync(Guid projectId, CancellationToken cancellationToken = default);

    Task<List<SyncProjectRun>> GetRecentSyncRunsAsync(int limit = 50, CancellationToken cancellationToken = default);

    Task<List<SyncProjectRun>> GetRunningSyncProjectRunsAsync(CancellationToken cancellationToken = default);

    Task<SyncRunDetailsData?> GetSyncRunDetailsAsync(Guid runId, CancellationToken cancellationToken = default);

    Task UpdateProjectStatusAsync(
        Guid projectId, bool? isRunning = null, DateTime? lastRunAt = null,
        int? totalExecutions = null, int? successfulExecutions = null, int? failedExecutions = null,
        CancellationToken cancellationToken = default);

    Task UpdateRunProgressAsync(
        Guid runId, int? completedSteps = null, int? progressPercentage = null,
        string? currentStepName = null, string? status = null, DateTime? completedAt = null,
        string? errorMessage = null, CancellationToken cancellationToken = default);

    Task<Guid> CreateSyncExecutionAsync(
        Guid directoryConnectionId, DateTime startedAt, string status = "Running",
        CancellationToken cancellationToken = default);

    Task UpdateSyncExecutionAsync(
        Guid executionId, string? status = null, DateTime? completedAt = null,
        int? identitiesAdded = null, int? identitiesUpdated = null, int? identitiesDeleted = null,
        int? groupsAdded = null, int? groupsUpdated = null, int? groupsDeleted = null,
        int? membershipsAdded = null, int? membershipsRemoved = null,
        int? personsCreated = null, int? personsUpdated = null,
        string? errorMessage = null, string? executionLog = null,
        CancellationToken cancellationToken = default);

    Task DeleteSyncProjectAsync(Guid projectId, CancellationToken cancellationToken = default);

    Task<Guid> CreateSyncProjectRunAsync(SyncProjectRun run, CancellationToken cancellationToken = default);

    Task<Guid> CreateSyncStepRunAsync(SyncStepRun stepRun, CancellationToken cancellationToken = default);

    Task UpdateSyncProjectRunStatusAsync(
        Guid runId, string status, DateTime? completedAt, int? durationSeconds, string? errorMessage,
        int totalObjectsProcessed, int totalObjectsCreated, int totalObjectsUpdated, int totalObjectsDeleted,
        int totalPersonsCreated, int totalErrors, int completedSteps, int progressPercentage,
        CancellationToken cancellationToken = default);

    Task UpdateSyncProjectExecutionStatusAsync(
        Guid projectId, bool isRunning, bool success, CancellationToken cancellationToken = default);

    /// <summary>
    /// Synchronizes step tags - removes tags not in the list and adds new ones.
    /// </summary>
    Task SynchronizeStepTagsAsync(Guid stepId, List<Guid> tagIds, CancellationToken cancellationToken = default);

    /// <summary>
    /// Bulk inserts step tags for a newly created step.
    /// </summary>
    Task InsertStepTagsAsync(Guid stepId, List<Guid> tagIds, CancellationToken cancellationToken = default);
}
