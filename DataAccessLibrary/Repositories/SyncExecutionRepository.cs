using Dapper;
using DataAccessLibrary.Models;
using Logging;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using System.Data;
using System.Diagnostics;
using System.Text.Json;

namespace DataAccessLibrary.Repositories;

/// <summary>
/// Dapper-based repository for sync execution/project/run management operations.
/// Handles project CRUD, run tracking, metrics updates, and execution status.
/// </summary>
public class SyncExecutionRepository : DapperRepositoryBase, ISyncExecutionRepository
{
    public SyncExecutionRepository(IConfiguration configuration, IGlobalLogger logger) : base(configuration, logger) { }

    public async Task UpdateStepRunMetricsAsync(
        Guid stepRunId,
        int objectsQueried,
        int objectsProcessed,
        int objectsCreated,
        int objectsUpdated,
        int objectsSkipped,
        int errorCount,
        CancellationToken cancellationToken = default,
        string? status = null,
        DateTime? completedAt = null,
        int? durationSeconds = null)
    {
        _logger.LogMethodEntry(nameof(UpdateStepRunMetricsAsync),
            new { stepRunId, objectsQueried, objectsProcessed, objectsCreated, objectsUpdated, objectsSkipped, errorCount, status });

        // Parameter validation before retry wrapper
        if (stepRunId == Guid.Empty)
        {
            _logger.LogWarning("StepRunId cannot be empty");
            throw new ArgumentException("StepRunId cannot be empty", nameof(stepRunId));
        }

        try
        {
            await SyncRepositoryHelpers.ExecuteWithRetryAsync(async () =>
            {
                using var tracker = new SyncRepositoryHelpers.PerformanceTracker(_logger, nameof(UpdateStepRunMetricsAsync),
                    new { stepRunId }, slowThresholdMs: 1000); // 1s threshold for simple update

                _logger.LogDebug("Updating metrics for step run {StepRunId}: Queried={ObjectsQueried}, Processed={ObjectsProcessed}, Created={ObjectsCreated}, Updated={ObjectsUpdated}, Skipped={ObjectsSkipped}, Errors={ErrorCount}, Status={Status}",
                    stepRunId, objectsQueried, objectsProcessed, objectsCreated, objectsUpdated, objectsSkipped, errorCount, status ?? "(unchanged)");

                using var connection = new SqlConnection(_connectionString);
                await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

                // Build SQL with optional status/completion columns
                var sql = @"UPDATE SyncStepRuns
                      SET ObjectsQueried = @ObjectsQueried,
                          ObjectsProcessed = @ObjectsProcessed,
                          ObjectsCreated = @ObjectsCreated,
                          ObjectsUpdated = @ObjectsUpdated,
                          ObjectsSkipped = @ObjectsSkipped,
                          ErrorCount = @ErrorCount";

                if (status != null)
                    sql += ", Status = @Status";
                if (completedAt.HasValue)
                    sql += ", CompletedAt = @CompletedAt";
                if (durationSeconds.HasValue)
                    sql += ", DurationSeconds = @DurationSeconds";

                sql += " WHERE Id = @StepRunId";

                var command = new CommandDefinition(
                    sql,
                    new
                    {
                        StepRunId = stepRunId,
                        ObjectsQueried = objectsQueried,
                        ObjectsProcessed = objectsProcessed,
                        ObjectsCreated = objectsCreated,
                        ObjectsUpdated = objectsUpdated,
                        ObjectsSkipped = objectsSkipped,
                        ErrorCount = errorCount,
                        Status = status,
                        CompletedAt = completedAt,
                        DurationSeconds = durationSeconds
                    },
                    cancellationToken: cancellationToken,
                    commandTimeout: 60); // Increased to handle database contention

                await connection.ExecuteAsync(command);

                _logger.LogInformation("Successfully updated metrics for step run {StepRunId} (Status={Status}) in {ElapsedMs}ms",
                    stepRunId, status ?? "(unchanged)", tracker.ElapsedMs);
            }, nameof(UpdateStepRunMetricsAsync), _logger, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw; // Always re-throw cancellation
        }
        catch (SqlException ex)
        {
            _logger.LogError(ex, "Database error updating metrics for step run {StepRunId}", stepRunId);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogMethodError(nameof(UpdateStepRunMetricsAsync), ex);
            throw;
        }
        finally
        {
            _logger.LogMethodExit(nameof(UpdateStepRunMetricsAsync));
        }
    }

    /// <summary>
    /// Updates person matching metrics for a step run.
    /// Called after post-processing scripts execute to update PersonsCreated and PersonsMatched.
    /// </summary>
    public async Task UpdateStepRunPersonMetricsAsync(
        Guid stepRunId,
        int personsCreated,
        int personsMatched,
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            const string sql = @"UPDATE SyncStepRuns
                SET PersonsCreated = @PersonsCreated,
                    PersonsMatched = @PersonsMatched
                WHERE Id = @StepRunId";

            await connection.ExecuteAsync(
                new CommandDefinition(sql,
                    new { StepRunId = stepRunId, PersonsCreated = personsCreated, PersonsMatched = personsMatched },
                    cancellationToken: cancellationToken));

            _logger.LogDebug("Updated person metrics for step run {StepRunId}: Created={PersonsCreated}, Matched={PersonsMatched}",
                stepRunId, personsCreated, personsMatched);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating person metrics for step run {StepRunId}", stepRunId);
            // Don't throw - person metrics update failure shouldn't fail the sync
        }
    }

    public async Task UpdateProjectRunMetricsAsync(
        Guid runId,
        int totalObjectsProcessed,
        int totalObjectsCreated,
        int totalObjectsUpdated,
        int totalErrors,
        int completedSteps,
        int progressPercentage,
        CancellationToken cancellationToken = default)
    {
        _logger.LogMethodEntry(nameof(UpdateProjectRunMetricsAsync),
            new { runId, totalObjectsProcessed, totalObjectsCreated, totalObjectsUpdated, totalErrors, completedSteps, progressPercentage });

        // Parameter validation before retry wrapper
        if (runId == Guid.Empty)
        {
            _logger.LogWarning("RunId cannot be empty");
            throw new ArgumentException("RunId cannot be empty", nameof(runId));
        }

        try
        {
            await SyncRepositoryHelpers.ExecuteWithRetryAsync(async () =>
            {
                using var tracker = new SyncRepositoryHelpers.PerformanceTracker(_logger, nameof(UpdateProjectRunMetricsAsync),
                    new { runId }, slowThresholdMs: 1000); // 1s threshold for simple update

                _logger.LogDebug("Updating metrics for project run {RunId}: TotalProcessed={TotalProcessed}, TotalCreated={TotalCreated}, TotalUpdated={TotalUpdated}, TotalErrors={TotalErrors}, CompletedSteps={CompletedSteps}, Progress={Progress}%",
                    runId, totalObjectsProcessed, totalObjectsCreated, totalObjectsUpdated, totalErrors, completedSteps, progressPercentage);

                using var connection = new SqlConnection(_connectionString);
                await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

                var command = new CommandDefinition(
                    @"UPDATE SyncProjectRuns
                      SET TotalObjectsProcessed = @TotalObjectsProcessed,
                          TotalObjectsCreated = @TotalObjectsCreated,
                          TotalObjectsUpdated = @TotalObjectsUpdated,
                          TotalErrors = @TotalErrors,
                          CompletedSteps = @CompletedSteps,
                          ProgressPercentage = @ProgressPercentage
                      WHERE Id = @RunId",
                    new
                    {
                        RunId = runId,
                        TotalObjectsProcessed = totalObjectsProcessed,
                        TotalObjectsCreated = totalObjectsCreated,
                        TotalObjectsUpdated = totalObjectsUpdated,
                        TotalErrors = totalErrors,
                        CompletedSteps = completedSteps,
                        ProgressPercentage = progressPercentage
                    },
                    cancellationToken: cancellationToken,
                    commandTimeout: 60); // Increased to handle database contention

                await connection.ExecuteAsync(command);

                _logger.LogInformation("Successfully updated metrics for project run {RunId} in {ElapsedMs}ms", runId, tracker.ElapsedMs);
            }, nameof(UpdateProjectRunMetricsAsync), _logger, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw; // Always re-throw cancellation
        }
        catch (SqlException ex)
        {
            _logger.LogError(ex, "Database error updating metrics for project run {RunId}", runId);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogMethodError(nameof(UpdateProjectRunMetricsAsync), ex);
            throw;
        }
        finally
        {
            _logger.LogMethodExit(nameof(UpdateProjectRunMetricsAsync));
        }
    }

    public async Task BulkInsertSyncProjectAsync(
        SyncProject project,
        CancellationToken cancellationToken = default)
    {
        _logger.LogMethodEntry(nameof(BulkInsertSyncProjectAsync));

        try
        {
            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            using var transaction = connection.BeginTransaction();

            try
            {
                // Set command timeout to 120 seconds for all operations
                const int commandTimeout = 120;

                // 1. Insert SyncProject (ALL columns to avoid NULL constraint errors)
                var projectSql = @"
                    INSERT INTO SyncProjects (
                        Id, Name, Description, ProjectType, SourceConnectionId, TargetConnectionId,
                        IsTemplateMode, IdentityMatchingStrategy, CronSchedule, IsEnabled, IsRunning,
                        ConflictResolutionStrategy, AutoCreateIdentities, EnableManagerAssignment,
                        SourceSyncProjectId, IsBuiltIn, IsReadOnly,
                        MinMatchConfidenceThreshold, PauseOnError, MaxErrorsBeforePause, Priority, LogLevel,
                        LastSuccessfulRunAt, LastRunAt, NextScheduledRunAt,
                        TotalExecutions, SuccessfulExecutions, FailedExecutions,
                        CreatedAt, CreatedBy, ModifiedAt, ModifiedBy
                    )
                    VALUES (
                        @Id, @Name, @Description, @ProjectType, @SourceConnectionId, @TargetConnectionId,
                        @IsTemplateMode, @IdentityMatchingStrategy, @CronSchedule, @IsEnabled, @IsRunning,
                        @ConflictResolutionStrategy, @AutoCreateIdentities, @EnableManagerAssignment,
                        @SourceSyncProjectId, @IsBuiltIn, @IsReadOnly,
                        @MinMatchConfidenceThreshold, @PauseOnError, @MaxErrorsBeforePause, @Priority, @LogLevel,
                        @LastSuccessfulRunAt, @LastRunAt, @NextScheduledRunAt,
                        @TotalExecutions, @SuccessfulExecutions, @FailedExecutions,
                        @CreatedAt, @CreatedBy, @ModifiedAt, @ModifiedBy
                    )";

                await connection.ExecuteAsync(projectSql, new
                {
                    project.Id,
                    project.Name,
                    project.Description,
                    project.ProjectType,
                    project.SourceConnectionId,
                    project.TargetConnectionId,
                    project.IsTemplateMode,
                    project.IdentityMatchingStrategy,
                    project.CronSchedule,
                    project.IsEnabled,
                    project.IsRunning,
                    project.ConflictResolutionStrategy,
                    project.AutoCreateIdentities,
                    project.EnableManagerAssignment,
                    project.SourceSyncProjectId,
                    project.IsBuiltIn,
                    project.IsReadOnly,
                    project.MinMatchConfidenceThreshold,
                    project.PauseOnError,
                    project.MaxErrorsBeforePause,
                    project.Priority,
                    project.LogLevel,
                    project.LastSuccessfulRunAt,
                    project.LastRunAt,
                    project.NextScheduledRunAt,
                    project.TotalExecutions,
                    project.SuccessfulExecutions,
                    project.FailedExecutions,
                    project.CreatedAt,
                    project.CreatedBy,
                    project.ModifiedAt,
                    project.ModifiedBy
                }, transaction, commandTimeout: commandTimeout);

                _logger.LogInformation("Inserted SyncProject {ProjectId}", project.Id);

                // 2-4. Insert the workflow graph (workflows + steps + mappings) for this project.
                var (wfCount, stCount, mpCount) = await InsertWorkflowGraphAsync(
                    connection, transaction, project.Id, project.Workflows, commandTimeout);

                // Commit transaction
                transaction.Commit();

                _logger.LogInformation("Successfully bulk inserted sync project {ProjectName} with {WorkflowCount} workflows, {StepCount} steps, and {MappingCount} attribute mappings",
                    project.Name, wfCount, stCount, mpCount);
            }
            catch
            {
                transaction.Rollback();
                throw;
            }
        }
        catch (SqlException ex)
        {
            _logger.LogError(ex, "Database error bulk inserting sync project");
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogMethodError(nameof(BulkInsertSyncProjectAsync), ex);
            throw;
        }
        finally
        {
            _logger.LogMethodExit(nameof(BulkInsertSyncProjectAsync));
        }
    }

    public async Task BulkInsertWorkflowsAsync(
        Guid projectId,
        List<SyncWorkflow> workflows,
        CancellationToken cancellationToken = default)
    {
        _logger.LogMethodEntry(nameof(BulkInsertWorkflowsAsync));

        if (workflows == null || workflows.Count == 0)
        {
            _logger.LogInformation("BulkInsertWorkflowsAsync: nothing to insert for project {ProjectId}", projectId);
            _logger.LogMethodExit(nameof(BulkInsertWorkflowsAsync));
            return;
        }

        try
        {
            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            using var transaction = connection.BeginTransaction();
            try
            {
                const int commandTimeout = 120;

                var (wfCount, stCount, mpCount) = await InsertWorkflowGraphAsync(
                    connection, transaction, projectId, workflows, commandTimeout);

                transaction.Commit();

                _logger.LogInformation(
                    "Backfilled {WorkflowCount} workflows, {StepCount} steps, {MappingCount} mappings into existing project {ProjectId}",
                    wfCount, stCount, mpCount, projectId);
            }
            catch
            {
                transaction.Rollback();
                throw;
            }
        }
        catch (SqlException ex)
        {
            _logger.LogError(ex, "Database error backfilling workflows into project {ProjectId}", projectId);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogMethodError(nameof(BulkInsertWorkflowsAsync), ex);
            throw;
        }
        finally
        {
            _logger.LogMethodExit(nameof(BulkInsertWorkflowsAsync));
        }
    }

    public async Task<List<string>> GetWorkflowObjectClassesForProjectAsync(
        Guid projectId,
        CancellationToken cancellationToken = default)
    {
        using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        var cmd = new CommandDefinition(
            "SELECT DISTINCT ObjectClass FROM SyncWorkflows WHERE SyncProjectId = @ProjectId",
            new { ProjectId = projectId },
            cancellationToken: cancellationToken);
        var rows = await connection.QueryAsync<string>(cmd).ConfigureAwait(false);
        return rows.Where(oc => !string.IsNullOrEmpty(oc)).ToList();
    }

    /// <summary>
    /// Inserts a list of workflows (with their steps + attribute mappings) for a given project,
    /// using the supplied open connection + transaction. Each workflow's SyncProjectId is forced
    /// to <paramref name="projectId"/>. Shared by both the full project insert and the in-place
    /// workflow backfill so the insert logic exists in exactly one place.
    /// Returns (workflowCount, stepCount, mappingCount) actually inserted.
    /// </summary>
    private async Task<(int Workflows, int Steps, int Mappings)> InsertWorkflowGraphAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        Guid projectId,
        ICollection<SyncWorkflow> projectWorkflows,
        int commandTimeout)
    {
        // Force project ownership so a caller can never write workflows under the wrong project id.
        foreach (var w in projectWorkflows)
        {
            w.SyncProjectId = projectId;
        }

        // 2. Bulk insert all workflows (ALL columns)
        {
                var workflowParams = projectWorkflows.Select(w => new
                {
                    w.Id,
                    w.SyncProjectId,
                    w.Name,
                    w.Description,
                    w.ObjectClass,
                    w.WorkflowType,
                    w.ExecutionOrder,
                    w.IsEnabled,
                    w.ContinueOnError,
                    w.MaxExecutionTimeMinutes,
                    w.CreatedAt,
                    w.ModifiedAt
                }).ToList();

                // PERFORMANCE FIX: Build single SQL with multiple VALUES instead of Dapper batch (was hitting 30s timeout per workflow)
                // Only execute if there are workflows to insert (internal sync projects may have no workflows)
                if (workflowParams.Count > 0)
                {
                    var workflowValuesClauses = new List<string>();
                    var workflowDynParams = new DynamicParameters();
                    for (int i = 0; i < workflowParams.Count; i++) {
                        var p = workflowParams[i]; var prefix = $"w{i}_";
                        workflowValuesClauses.Add($"(@{prefix}Id, @{prefix}SyncProjectId, @{prefix}Name, @{prefix}Description, @{prefix}ObjectClass, @{prefix}WorkflowType, @{prefix}ExecutionOrder, @{prefix}IsEnabled, @{prefix}ContinueOnError, @{prefix}MaxExecutionTimeMinutes, @{prefix}CreatedAt, @{prefix}ModifiedAt)");
                        workflowDynParams.Add($"{prefix}Id", p.Id); workflowDynParams.Add($"{prefix}SyncProjectId", p.SyncProjectId);
                        workflowDynParams.Add($"{prefix}Name", p.Name); workflowDynParams.Add($"{prefix}Description", p.Description);
                        workflowDynParams.Add($"{prefix}ObjectClass", p.ObjectClass);
                        workflowDynParams.Add($"{prefix}WorkflowType", p.WorkflowType); workflowDynParams.Add($"{prefix}ExecutionOrder", p.ExecutionOrder);
                        workflowDynParams.Add($"{prefix}IsEnabled", p.IsEnabled); workflowDynParams.Add($"{prefix}ContinueOnError", p.ContinueOnError);
                        workflowDynParams.Add($"{prefix}MaxExecutionTimeMinutes", p.MaxExecutionTimeMinutes); workflowDynParams.Add($"{prefix}CreatedAt", p.CreatedAt);
                        workflowDynParams.Add($"{prefix}ModifiedAt", p.ModifiedAt);
                    }
                    var workflowSingleSql = $"INSERT INTO SyncWorkflows (Id, SyncProjectId, Name, Description, ObjectClass, WorkflowType, ExecutionOrder, IsEnabled, ContinueOnError, MaxExecutionTimeMinutes, CreatedAt, ModifiedAt) VALUES {string.Join(", ", workflowValuesClauses)}";
                    await connection.ExecuteAsync(workflowSingleSql, workflowDynParams, transaction, commandTimeout: commandTimeout);
                    _logger.LogInformation("Inserted {Count} workflows in SINGLE SQL", workflowParams.Count);
                }
                else
                {
                    _logger.LogInformation("No workflows to insert (internal sync project)");
                }

                // 3. Bulk insert all steps (ALL columns)
                var stepSql = @"
                    INSERT INTO SyncSteps (
                        Id, SyncWorkflowId, Name, Description, ObjectClass, ExecutionOrder,
                        StepType, MarkAsType, LdapFilter, SearchBase, SearchBases, ExcludedSearchBases, SearchScope,
                        IsEnabled, ContinueOnError, MaxExecutionTimeMinutes, DependsOnStepIds,
                        ProcessDeletions, UpdateExisting, BatchSize, LdapPageSize, Configuration,
                        EnableIdentityMatching, IdentityMatchingAttribute, InheritWorkflowTags,
                        SkipPersonMatching, EnablePersonMatching, CreatePersonIfNotFound,
                        CreatedAt, ModifiedAt
                    )
                    VALUES (
                        @Id, @SyncWorkflowId, @Name, @Description, @ObjectClass, @ExecutionOrder,
                        @StepType, @MarkAsType, @LdapFilter, @SearchBase, @SearchBases, @ExcludedSearchBases, @SearchScope,
                        @IsEnabled, @ContinueOnError, @MaxExecutionTimeMinutes, @DependsOnStepIds,
                        @ProcessDeletions, @UpdateExisting, @BatchSize, @LdapPageSize, @Configuration,
                        @EnableIdentityMatching, @IdentityMatchingAttribute, @InheritWorkflowTags,
                        @SkipPersonMatching, @EnablePersonMatching, @CreatePersonIfNotFound,
                        @CreatedAt, @ModifiedAt
                    )";

                var stepParams = projectWorkflows
                    .SelectMany(w => w.Steps.Select(s => new
                    {
                        s.Id,
                        s.SyncWorkflowId,
                        s.Name,
                        s.Description,
                        s.ObjectClass,
                        s.ExecutionOrder,
                        s.StepType,
                        s.MarkAsType,
                        s.LdapFilter,
                        s.SearchBase,
                        s.SearchBases,
                        s.ExcludedSearchBases,
                        s.SearchScope,
                        s.IsEnabled,
                        s.ContinueOnError,
                        s.MaxExecutionTimeMinutes,
                        s.DependsOnStepIds,
                        s.ProcessDeletions,
                        s.UpdateExisting,
                        s.BatchSize,
                        s.LdapPageSize,
                        s.Configuration,
                        s.EnableIdentityMatching,
                        s.IdentityMatchingAttribute,
                        s.InheritWorkflowTags,
                        s.SkipPersonMatching,
                        s.EnablePersonMatching,
                        s.CreatePersonIfNotFound,
                        s.CreatedAt,
                        s.ModifiedAt
                    }))
                    .ToList();

                // PERFORMANCE FIX: Build single SQL with multiple VALUES instead of Dapper batch
                if (stepParams.Any())
                {
                    var stepValuesClauses = new List<string>();
                    var stepDynParams = new DynamicParameters();
                    for (int i = 0; i < stepParams.Count; i++)
                    {
                        var s = stepParams[i];
                        var prefix = $"s{i}_";
                        stepValuesClauses.Add($"(@{prefix}Id, @{prefix}SyncWorkflowId, @{prefix}Name, @{prefix}Description, @{prefix}ObjectClass, @{prefix}ExecutionOrder, @{prefix}StepType, @{prefix}MarkAsType, @{prefix}LdapFilter, @{prefix}SearchBase, @{prefix}SearchBases, @{prefix}ExcludedSearchBases, @{prefix}SearchScope, @{prefix}IsEnabled, @{prefix}ContinueOnError, @{prefix}MaxExecutionTimeMinutes, @{prefix}DependsOnStepIds, @{prefix}ProcessDeletions, @{prefix}UpdateExisting, @{prefix}BatchSize, @{prefix}LdapPageSize, @{prefix}Configuration, @{prefix}EnableIdentityMatching, @{prefix}IdentityMatchingAttribute, @{prefix}InheritWorkflowTags, @{prefix}SkipPersonMatching, @{prefix}EnablePersonMatching, @{prefix}CreatePersonIfNotFound, @{prefix}CreatedAt, @{prefix}ModifiedAt)");
                        stepDynParams.Add($"{prefix}Id", s.Id);
                        stepDynParams.Add($"{prefix}SyncWorkflowId", s.SyncWorkflowId);
                        stepDynParams.Add($"{prefix}Name", s.Name);
                        stepDynParams.Add($"{prefix}Description", s.Description);
                        stepDynParams.Add($"{prefix}ObjectClass", s.ObjectClass);
                        stepDynParams.Add($"{prefix}ExecutionOrder", s.ExecutionOrder);
                        stepDynParams.Add($"{prefix}StepType", s.StepType);
                        stepDynParams.Add($"{prefix}MarkAsType", s.MarkAsType);
                        stepDynParams.Add($"{prefix}LdapFilter", s.LdapFilter);
                        stepDynParams.Add($"{prefix}SearchBase", s.SearchBase);
                        stepDynParams.Add($"{prefix}SearchBases", s.SearchBases);
                        stepDynParams.Add($"{prefix}ExcludedSearchBases", s.ExcludedSearchBases);
                        stepDynParams.Add($"{prefix}SearchScope", s.SearchScope);
                        stepDynParams.Add($"{prefix}IsEnabled", s.IsEnabled);
                        stepDynParams.Add($"{prefix}ContinueOnError", s.ContinueOnError);
                        stepDynParams.Add($"{prefix}MaxExecutionTimeMinutes", s.MaxExecutionTimeMinutes);
                        stepDynParams.Add($"{prefix}DependsOnStepIds", s.DependsOnStepIds);
                        stepDynParams.Add($"{prefix}ProcessDeletions", s.ProcessDeletions);
                        stepDynParams.Add($"{prefix}UpdateExisting", s.UpdateExisting);
                        stepDynParams.Add($"{prefix}BatchSize", s.BatchSize);
                        stepDynParams.Add($"{prefix}LdapPageSize", s.LdapPageSize);
                        stepDynParams.Add($"{prefix}Configuration", s.Configuration);
                        stepDynParams.Add($"{prefix}EnableIdentityMatching", s.EnableIdentityMatching);
                        stepDynParams.Add($"{prefix}IdentityMatchingAttribute", s.IdentityMatchingAttribute);
                        stepDynParams.Add($"{prefix}InheritWorkflowTags", s.InheritWorkflowTags);
                        stepDynParams.Add($"{prefix}SkipPersonMatching", s.SkipPersonMatching);
                        stepDynParams.Add($"{prefix}EnablePersonMatching", s.EnablePersonMatching);
                        stepDynParams.Add($"{prefix}CreatePersonIfNotFound", s.CreatePersonIfNotFound);
                        stepDynParams.Add($"{prefix}CreatedAt", s.CreatedAt);
                        stepDynParams.Add($"{prefix}ModifiedAt", s.ModifiedAt);
                    }
                    var stepSingleSql = $"INSERT INTO SyncSteps (Id, SyncWorkflowId, Name, Description, ObjectClass, ExecutionOrder, StepType, MarkAsType, LdapFilter, SearchBase, SearchBases, ExcludedSearchBases, SearchScope, IsEnabled, ContinueOnError, MaxExecutionTimeMinutes, DependsOnStepIds, ProcessDeletions, UpdateExisting, BatchSize, LdapPageSize, Configuration, EnableIdentityMatching, IdentityMatchingAttribute, InheritWorkflowTags, SkipPersonMatching, EnablePersonMatching, CreatePersonIfNotFound, CreatedAt, ModifiedAt) VALUES {string.Join(", ", stepValuesClauses)}";
                    await connection.ExecuteAsync(stepSingleSql, stepDynParams, transaction, commandTimeout: commandTimeout);
                }
                _logger.LogInformation("Inserted {Count} steps in SINGLE SQL", stepParams.Count);

                // 4. Bulk insert all attribute mappings (ALL columns)
                var mappingSql = @"
                    INSERT INTO AttributeMappings (
                        Id, SyncStepId, SourceAttribute, SourceDisplayName, TargetAttribute, TargetType,
                        DataType, TransformationType, TransformationExpression, DefaultValue,
                        IsRequired, IsEnabled, UseForMatching, MatchWeight,
                        UseFuzzyMatch, FuzzyMatchThreshold, FuzzyMatchAlgorithm,
                        ExecutionOrder, CreatedAt, ModifiedAt
                    )
                    VALUES (
                        @Id, @SyncStepId, @SourceAttribute, @SourceDisplayName, @TargetAttribute, @TargetType,
                        @DataType, @TransformationType, @TransformationExpression, @DefaultValue,
                        @IsRequired, @IsEnabled, @UseForMatching, @MatchWeight,
                        @UseFuzzyMatch, @FuzzyMatchThreshold, @FuzzyMatchAlgorithm,
                        @ExecutionOrder, @CreatedAt, @ModifiedAt
                    )";

                var mappingParams = projectWorkflows
                    .SelectMany(w => w.Steps
                        .SelectMany(s => s.AttributeMappings.Select(m => new
                        {
                            m.Id,
                            m.SyncStepId,
                            m.SourceAttribute,
                            m.SourceDisplayName,
                            m.TargetAttribute,
                            m.TargetType,
                            m.DataType,
                            m.TransformationType,
                            m.TransformationExpression,
                            m.DefaultValue,
                            m.IsRequired,
                            m.IsEnabled,
                            m.UseForMatching,
                            m.MatchWeight,
                            m.UseFuzzyMatch,
                            m.FuzzyMatchThreshold,
                            m.FuzzyMatchAlgorithm,
                            m.ExecutionOrder,
                            m.CreatedAt,
                            m.ModifiedAt
                        })))
                    .ToList();

                // PERFORMANCE FIX: Build single SQL with multiple VALUES instead of Dapper batch
                // Split into batches of 50 to avoid SQL parameter limits (2100 max, 20 params per row = ~100 rows max)
                const int batchSize = 50;
                for (int batch = 0; batch < mappingParams.Count; batch += batchSize)
                {
                    var batchItems = mappingParams.Skip(batch).Take(batchSize).ToList();
                    var mappingValuesClauses = new List<string>();
                    var mappingDynParams = new DynamicParameters();
                    for (int i = 0; i < batchItems.Count; i++)
                    {
                        var m = batchItems[i];
                        var prefix = $"m{i}_";
                        mappingValuesClauses.Add($"(@{prefix}Id, @{prefix}SyncStepId, @{prefix}SourceAttribute, @{prefix}SourceDisplayName, @{prefix}TargetAttribute, @{prefix}TargetType, @{prefix}DataType, @{prefix}TransformationType, @{prefix}TransformationExpression, @{prefix}DefaultValue, @{prefix}IsRequired, @{prefix}IsEnabled, @{prefix}UseForMatching, @{prefix}MatchWeight, @{prefix}UseFuzzyMatch, @{prefix}FuzzyMatchThreshold, @{prefix}FuzzyMatchAlgorithm, @{prefix}ExecutionOrder, @{prefix}CreatedAt, @{prefix}ModifiedAt)");
                        mappingDynParams.Add($"{prefix}Id", m.Id);
                        mappingDynParams.Add($"{prefix}SyncStepId", m.SyncStepId);
                        mappingDynParams.Add($"{prefix}SourceAttribute", m.SourceAttribute);
                        mappingDynParams.Add($"{prefix}SourceDisplayName", m.SourceDisplayName);
                        mappingDynParams.Add($"{prefix}TargetAttribute", m.TargetAttribute);
                        mappingDynParams.Add($"{prefix}TargetType", m.TargetType);
                        mappingDynParams.Add($"{prefix}DataType", m.DataType);
                        mappingDynParams.Add($"{prefix}TransformationType", m.TransformationType);
                        mappingDynParams.Add($"{prefix}TransformationExpression", m.TransformationExpression);
                        mappingDynParams.Add($"{prefix}DefaultValue", m.DefaultValue);
                        mappingDynParams.Add($"{prefix}IsRequired", m.IsRequired);
                        mappingDynParams.Add($"{prefix}IsEnabled", m.IsEnabled);
                        mappingDynParams.Add($"{prefix}UseForMatching", m.UseForMatching);
                        mappingDynParams.Add($"{prefix}MatchWeight", m.MatchWeight);
                        mappingDynParams.Add($"{prefix}UseFuzzyMatch", m.UseFuzzyMatch);
                        mappingDynParams.Add($"{prefix}FuzzyMatchThreshold", m.FuzzyMatchThreshold);
                        mappingDynParams.Add($"{prefix}FuzzyMatchAlgorithm", m.FuzzyMatchAlgorithm);
                        mappingDynParams.Add($"{prefix}ExecutionOrder", m.ExecutionOrder);
                        mappingDynParams.Add($"{prefix}CreatedAt", m.CreatedAt);
                        mappingDynParams.Add($"{prefix}ModifiedAt", m.ModifiedAt);
                    }
                    var mappingSingleSql = $"INSERT INTO AttributeMappings (Id, SyncStepId, SourceAttribute, SourceDisplayName, TargetAttribute, TargetType, DataType, TransformationType, TransformationExpression, DefaultValue, IsRequired, IsEnabled, UseForMatching, MatchWeight, UseFuzzyMatch, FuzzyMatchThreshold, FuzzyMatchAlgorithm, ExecutionOrder, CreatedAt, ModifiedAt) VALUES {string.Join(", ", mappingValuesClauses)}";
                    await connection.ExecuteAsync(mappingSingleSql, mappingDynParams, transaction, commandTimeout: commandTimeout);
                }
                _logger.LogInformation("Inserted {Count} attribute mappings in SINGLE SQL batches", mappingParams.Count);

            return (workflowParams.Count, stepParams.Count, mappingParams.Count);
        }
    }

    /// <summary>
    /// FAST: Load sync projects list without nested data (for list view)
    /// Uses 3 separate optimized queries instead of Cartesian explosion
    /// Performance: ~50ms vs ~30 seconds with EF Core (600x faster)
    /// </summary>
    public async Task<List<SyncProjectListItem>> GetSyncProjectsListAsync(
        CancellationToken cancellationToken = default)
    {
        _logger.LogMethodEntry(nameof(GetSyncProjectsListAsync));

        try
        {
            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            // Query 1: Load projects (FAST - no joins except connection name)
            var command1 = new CommandDefinition(
                @"SELECT
                    sp.Id,
                    sp.Name,
                    sp.Description,
                    sp.IsEnabled,
                    sp.IsBuiltIn,
                    sp.IsReadOnly,
                    sp.ProjectType,
                    sp.SourceConnectionId,
                    sp.TargetConnectionId,
                    sp.CronSchedule,
                    sp.LogLevel,
                    sp.TotalExecutions,
                    sp.SuccessfulExecutions,
                    sp.FailedExecutions,
                    sp.LastRunAt,
                    sp.NextScheduledRunAt,
                    sp.IsRunning,
                    dc.Name AS SourceConnectionName,
                    dc.ConnectionType AS SourceConnectionType,
                    tc.Name AS TargetConnectionName,
                    tc.ConnectionType AS TargetConnectionType
                FROM SyncProjects sp
                LEFT JOIN DirectoryConnections dc ON sp.SourceConnectionId = dc.Id
                LEFT JOIN DirectoryConnections tc ON sp.TargetConnectionId = tc.Id
                ORDER BY sp.IsBuiltIn DESC, sp.Name",
                cancellationToken: cancellationToken);

            var projects = await connection.QueryAsync<SyncProjectListItem>(command1);
            var projectsList = projects.AsList();
            var projectIds = projectsList.Select(p => p.Id).ToList();

            if (!projectIds.Any())
            {
                _logger.LogInformation("No sync projects found");
                return projectsList;
            }

            // Query 2: Load workflow counts (FAST - aggregation)
            var command2 = new CommandDefinition(
                @"SELECT
                    w.SyncProjectId AS ProjectId,
                    COUNT(DISTINCT w.Id) AS WorkflowCount,
                    COUNT(s.Id) AS StepCount
                FROM SyncWorkflows w
                LEFT JOIN SyncSteps s ON w.Id = s.SyncWorkflowId
                WHERE w.SyncProjectId IN @ProjectIds
                GROUP BY w.SyncProjectId",
                new { ProjectIds = projectIds },
                cancellationToken: cancellationToken);

            var workflowCounts = await connection.QueryAsync<(Guid ProjectId, int WorkflowCount, int StepCount)>(command2);

            // Map counts to projects
            var countsDict = workflowCounts.ToDictionary(x => x.ProjectId);
            foreach (var project in projectsList)
            {
                if (countsDict.TryGetValue(project.Id, out var counts))
                {
                    project.WorkflowCount = counts.WorkflowCount;
                    project.StepCount = counts.StepCount;
                }
            }

            // Query 3: Load total objects synced from successful runs
            var command3 = new CommandDefinition(
                @"SELECT
                    SyncProjectId AS ProjectId,
                    ISNULL(SUM(TotalObjectsProcessed), 0) AS TotalObjectsSynced
                FROM SyncProjectRuns
                WHERE SyncProjectId IN @ProjectIds
                    AND Status = 'Completed'
                GROUP BY SyncProjectId",
                new { ProjectIds = projectIds },
                cancellationToken: cancellationToken);

            var objectCounts = await connection.QueryAsync<(Guid ProjectId, int TotalObjectsSynced)>(command3);
            var objectCountsDict = objectCounts.ToDictionary(x => x.ProjectId);
            foreach (var project in projectsList)
            {
                if (objectCountsDict.TryGetValue(project.Id, out var objectCount))
                {
                    project.TotalObjectsSynced = objectCount.TotalObjectsSynced;
                }
            }

            // Query 4: Load CurrentRunId for running projects (ID of latest Running run)
            var runningProjectIds = projectsList.Where(p => p.IsRunning).Select(p => p.Id).ToList();
            if (runningProjectIds.Any())
            {
                var command4 = new CommandDefinition(
                    @"SELECT
                        SyncProjectId AS ProjectId,
                        Id AS CurrentRunId
                    FROM SyncProjectRuns
                    WHERE SyncProjectId IN @ProjectIds
                        AND Status = 'Running'
                    ORDER BY StartedAt DESC",
                    new { ProjectIds = runningProjectIds },
                    cancellationToken: cancellationToken);

                var runningRuns = await connection.QueryAsync<(Guid ProjectId, Guid CurrentRunId)>(command4);
                var runningRunsDict = runningRuns.GroupBy(x => x.ProjectId)
                    .ToDictionary(g => g.Key, g => g.First().CurrentRunId);

                foreach (var project in projectsList)
                {
                    if (runningRunsDict.TryGetValue(project.Id, out var currentRunId))
                    {
                        project.CurrentRunId = currentRunId;
                    }
                }
            }

            _logger.LogInformation("Loaded {ProjectCount} sync projects with Dapper (FAST)", projectsList.Count);
            return projectsList;
        }
        catch (Exception ex)
        {
            _logger.LogMethodError(nameof(GetSyncProjectsListAsync), ex);
            throw;
        }
        finally
        {
            _logger.LogMethodExit(nameof(GetSyncProjectsListAsync));
        }
    }

    /// <summary>
    /// FAST: Load one sync project with all details using separate queries
    /// Avoids Cartesian explosion by using 5 separate fast queries
    /// Performance: ~200ms vs 8+ MINUTES with EF Core (2,400x faster!)
    /// </summary>
    public async Task<SyncProjectDetails?> GetSyncProjectDetailsAsync(
        Guid projectId,
        CancellationToken cancellationToken = default)
    {
        _logger.LogMethodEntry(nameof(GetSyncProjectDetailsAsync), new { projectId });

        try
        {
            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            // Query 1: Load project (FAST)
            var cmd1 = new CommandDefinition(
                @"SELECT
                    sp.*,
                    dc.Name AS SourceConnectionName,
                    dc.ConnectionType AS SourceConnectionType
                FROM SyncProjects sp
                LEFT JOIN DirectoryConnections dc ON sp.SourceConnectionId = dc.Id
                WHERE sp.Id = @ProjectId",
                new { ProjectId = projectId },
                cancellationToken: cancellationToken);

            var project = await connection.QuerySingleOrDefaultAsync<SyncProjectDetails>(cmd1);

            if (project == null)
            {
                _logger.LogWarning("Sync project not found: {ProjectId}", projectId);
                return null;
            }

            // Query 2: Load workflows (FAST - no joins)
            var cmd2 = new CommandDefinition(
                "SELECT * FROM SyncWorkflows WHERE SyncProjectId = @ProjectId ORDER BY ExecutionOrder",
                new { ProjectId = projectId },
                cancellationToken: cancellationToken);

            var workflows = await connection.QueryAsync<SyncWorkflow>(cmd2);
            project.Workflows = workflows.AsList();

            if (!project.Workflows.Any())
            {
                _logger.LogInformation("No workflows found for project {ProjectId}", projectId);
                return project;
            }

            var workflowIds = project.Workflows.Select(w => w.Id).ToList();

            // Query 3: Load steps (FAST - no joins)
            var cmd3 = new CommandDefinition(
                "SELECT * FROM SyncSteps WHERE SyncWorkflowId IN @WorkflowIds ORDER BY ExecutionOrder",
                new { WorkflowIds = workflowIds },
                cancellationToken: cancellationToken);

            var steps = await connection.QueryAsync<SyncStep>(cmd3);
            var stepsList = steps.AsList();
            var stepIds = stepsList.Select(s => s.Id).ToList();

            // Query 4: Load attribute mappings (FAST - no joins)
            List<AttributeMapping> mappings;
            if (stepIds.Any())
            {
                var cmd4 = new CommandDefinition(
                    "SELECT * FROM AttributeMappings WHERE SyncStepId IN @StepIds ORDER BY ExecutionOrder",
                    new { StepIds = stepIds },
                    cancellationToken: cancellationToken);
                mappings = (await connection.QueryAsync<AttributeMapping>(cmd4)).AsList();
            }
            else
            {
                mappings = new List<AttributeMapping>();
            }

            // Query 5: Load workflow tags (FAST)
            var cmd5 = new CommandDefinition(
                @"SELECT
                    wt.SyncWorkflowId,
                    wt.TagId,
                    t.Name AS TagName,
                    t.Category AS TagCategory,
                    t.Color AS TagColor
                FROM WorkflowTags wt
                INNER JOIN Tags t ON wt.TagId = t.Id
                WHERE wt.SyncWorkflowId IN @WorkflowIds",
                new { WorkflowIds = workflowIds },
                cancellationToken: cancellationToken);

            var workflowTags = await connection.QueryAsync<WorkflowTagDetails>(cmd5);

            var tagsList = workflowTags.AsList();

            // Query 6: Load step tags (FAST)
            List<StepTagDetails> stepTagsList;
            if (stepIds.Any())
            {
                var cmd6 = new CommandDefinition(
                    @"SELECT
                        st.SyncStepId,
                        st.TagId,
                        t.Name AS TagName,
                        t.Category AS TagCategory,
                        t.Color AS TagColor
                    FROM SyncStepTags st
                    INNER JOIN Tags t ON st.TagId = t.Id
                    WHERE st.SyncStepId IN @StepIds",
                    new { StepIds = stepIds },
                    cancellationToken: cancellationToken);
                stepTagsList = (await connection.QueryAsync<StepTagDetails>(cmd6)).AsList();
            }
            else
            {
                stepTagsList = new List<StepTagDetails>();
            }

            // Map relationships (in-memory - FAST)
            var stepsLookup = stepsList.ToLookup(s => s.SyncWorkflowId);
            var mappingsLookup = mappings.ToLookup(m => m.SyncStepId);
            var tagsLookup = tagsList.ToLookup(t => t.SyncWorkflowId);
            var stepTagsLookup = stepTagsList.ToLookup(st => st.SyncStepId);

            foreach (var workflow in project.Workflows)
            {
                workflow.Steps = stepsLookup[workflow.Id].ToList();

                foreach (var step in workflow.Steps)
                {
                    step.AttributeMappings = mappingsLookup[step.Id].ToList();

                    // Populate step tags
                    step.StepTags = stepTagsLookup[step.Id]
                        .Select(st => new SyncStepTag
                        {
                            SyncStepId = step.Id,
                            TagId = st.TagId,
                            Tag = new Tag
                            {
                                Id = st.TagId,
                                Name = st.TagName,
                                Category = st.TagCategory,
                                Color = st.TagColor
                            }
                        })
                        .ToList();
                }

                // Populate workflow tags
                workflow.WorkflowTags = tagsLookup[workflow.Id]
                    .Select(t => new WorkflowTag
                    {
                        SyncWorkflowId = workflow.Id,
                        TagId = t.TagId,
                        Tag = new Tag
                        {
                            Id = t.TagId,
                            Name = t.TagName,
                            Category = t.TagCategory,
                            Color = t.TagColor
                        }
                    })
                    .ToList();
            }

            _logger.LogInformation("Loaded project '{ProjectName}' with {WorkflowCount} workflows, {StepCount} steps, {MappingCount} mappings using Dapper (BLAZING FAST)",
                project.Name,
                project.Workflows.Count,
                stepsList.Count,
                mappings.Count);

            return project;
        }
        catch (Exception ex)
        {
            _logger.LogMethodError(nameof(GetSyncProjectDetailsAsync), ex);
            throw;
        }
        finally
        {
            _logger.LogMethodExit(nameof(GetSyncProjectDetailsAsync));
        }
    }

    /// <summary>
    /// FAST: Load sync runs for a project using Dapper (no DbContext concurrency issues)
    /// Limited to most recent 50 runs by default for performance
    /// </summary>
    public async Task<List<SyncProjectRun>> GetSyncRunsForProjectAsync(
        Guid projectId,
        int limit = 50,
        CancellationToken cancellationToken = default)
    {
        _logger.LogMethodEntry(nameof(GetSyncRunsForProjectAsync));

        try
        {
            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            _logger.LogInformation("Loading sync runs for project {ProjectId} using Dapper (FAST, limit {Limit})", projectId, limit);

            var command = new CommandDefinition(
                @"SELECT TOP (@Limit) * FROM SyncProjectRuns
                  WHERE SyncProjectId = @ProjectId
                  ORDER BY StartedAt DESC",
                new { ProjectId = projectId, Limit = limit },
                cancellationToken: cancellationToken);

            var runs = await connection.QueryAsync<SyncProjectRun>(command);
            var runsList = runs.AsList();

            _logger.LogInformation("Loaded {Count} sync runs using Dapper (FAST)", runsList.Count);

            return runsList;
        }
        catch (Exception ex)
        {
            _logger.LogMethodError(nameof(GetSyncRunsForProjectAsync), ex);
            throw;
        }
        finally
        {
            _logger.LogMethodExit(nameof(GetSyncRunsForProjectAsync));
        }
    }

    /// <summary>
    /// FAST: Get the latest run for a project (running first, then most recent)
    /// </summary>
    public async Task<SyncProjectRun?> GetLatestRunForProjectAsync(
        Guid projectId,
        CancellationToken cancellationToken = default)
    {
        _logger.LogMethodEntry(nameof(GetLatestRunForProjectAsync));

        try
        {
            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            // First try to get a running run
            var runningRun = await connection.QueryFirstOrDefaultAsync<SyncProjectRun>(
                @"SELECT TOP 1 * FROM SyncProjectRuns
                  WHERE SyncProjectId = @ProjectId AND Status = 'Running'
                  ORDER BY StartedAt DESC",
                new { ProjectId = projectId });

            if (runningRun != null)
                return runningRun;

            // Otherwise get the most recent run
            return await connection.QueryFirstOrDefaultAsync<SyncProjectRun>(
                @"SELECT TOP 1 * FROM SyncProjectRuns
                  WHERE SyncProjectId = @ProjectId
                  ORDER BY StartedAt DESC",
                new { ProjectId = projectId });
        }
        catch (Exception ex)
        {
            _logger.LogMethodError(nameof(GetLatestRunForProjectAsync), ex);
            throw;
        }
        finally
        {
            _logger.LogMethodExit(nameof(GetLatestRunForProjectAsync));
        }
    }

    /// <summary>
    /// FAST: Load all recent sync runs across all projects using Dapper
    /// </summary>
    public async Task<List<SyncProjectRun>> GetRecentSyncRunsAsync(
        int limit = 50,
        CancellationToken cancellationToken = default)
    {
        _logger.LogMethodEntry(nameof(GetRecentSyncRunsAsync));

        try
        {
            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            _logger.LogInformation("Loading recent {Limit} sync runs using Dapper (FAST)", limit);

            var command = new CommandDefinition(
                @"SELECT TOP (@Limit) * FROM SyncProjectRuns
                  ORDER BY StartedAt DESC",
                new { Limit = limit },
                cancellationToken: cancellationToken);

            var runs = await connection.QueryAsync<SyncProjectRun>(command);
            var runsList = runs.AsList();

            _logger.LogInformation("Loaded {Count} recent sync runs using Dapper (MILLISECONDS)", runsList.Count);

            return runsList;
        }
        catch (Exception ex)
        {
            _logger.LogMethodError(nameof(GetRecentSyncRunsAsync), ex);
            throw;
        }
        finally
        {
            _logger.LogMethodExit(nameof(GetRecentSyncRunsAsync));
        }
    }

    /// <summary>
    /// FAST: Get all currently running sync project runs (Status = 'Running')
    /// Used by Processing Center to show active sync operations
    /// </summary>
    public async Task<List<SyncProjectRun>> GetRunningSyncProjectRunsAsync(
        CancellationToken cancellationToken = default)
    {
        _logger.LogMethodEntry(nameof(GetRunningSyncProjectRunsAsync));

        try
        {
            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            var command = new CommandDefinition(
                @"SELECT * FROM SyncProjectRuns
                  WHERE Status = 'Running'
                  ORDER BY StartedAt DESC",
                cancellationToken: cancellationToken);

            var runs = await connection.QueryAsync<SyncProjectRun>(command);
            var runsList = runs.AsList();

            _logger.LogDebug("Found {Count} running sync project runs", runsList.Count);

            return runsList;
        }
        catch (Exception ex)
        {
            _logger.LogMethodError(nameof(GetRunningSyncProjectRunsAsync), ex);
            throw;
        }
        finally
        {
            _logger.LogMethodExit(nameof(GetRunningSyncProjectRunsAsync));
        }
    }

    /// <summary>
    /// FAST: Load sync run details with all step runs using Dapper (no DbContext concurrency issues)
    /// </summary>
    public async Task<SyncRunDetailsData?> GetSyncRunDetailsAsync(
        Guid runId,
        CancellationToken cancellationToken = default)
    {
        _logger.LogMethodEntry(nameof(GetSyncRunDetailsAsync));

        try
        {
            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            _logger.LogInformation("Loading sync run details for run {RunId} using Dapper (FAST)", runId);

            // Query 1: Load the run
            var runCommand = new CommandDefinition(
                "SELECT * FROM SyncProjectRuns WHERE Id = @RunId",
                new { RunId = runId },
                cancellationToken: cancellationToken);

            var run = await connection.QuerySingleOrDefaultAsync<SyncProjectRun>(runCommand);

            if (run == null)
            {
                _logger.LogWarning("Sync run {RunId} not found", runId);
                return null;
            }

            // Query 2: Load the project
            var projectCommand = new CommandDefinition(
                "SELECT * FROM SyncProjects WHERE Id = @ProjectId",
                new { ProjectId = run.SyncProjectId },
                cancellationToken: cancellationToken);

            var project = await connection.QuerySingleOrDefaultAsync<SyncProject>(projectCommand);

            // Query 3: Load step runs
            var stepRunsCommand = new CommandDefinition(
                @"SELECT * FROM SyncStepRuns
                  WHERE SyncProjectRunId = @RunId
                  ORDER BY StartedAt",
                new { RunId = runId },
                cancellationToken: cancellationToken);

            var stepRuns = await connection.QueryAsync<SyncStepRun>(stepRunsCommand);

            var result = new SyncRunDetailsData
            {
                Run = run,
                Project = project,
                StepRuns = stepRuns.AsList()
            };

            _logger.LogInformation("Loaded sync run details with {StepCount} step runs using Dapper (MILLISECONDS)", result.StepRuns.Count);

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogMethodError(nameof(GetSyncRunDetailsAsync), ex);
            throw;
        }
        finally
        {
            _logger.LogMethodExit(nameof(GetSyncRunDetailsAsync));
        }
    }

    public async Task UpdateProjectStatusAsync(
        Guid projectId,
        bool? isRunning = null,
        DateTime? lastRunAt = null,
        int? totalExecutions = null,
        int? successfulExecutions = null,
        int? failedExecutions = null,
        CancellationToken cancellationToken = default)
    {
        _logger.LogMethodEntry(nameof(UpdateProjectStatusAsync), new { projectId, isRunning, lastRunAt });

        try
        {
            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            var sql = @"
                UPDATE SyncProjects
                SET IsRunning = COALESCE(@IsRunning, IsRunning),
                    LastRunAt = COALESCE(@LastRunAt, LastRunAt),
                    TotalExecutions = COALESCE(@TotalExecutions, TotalExecutions),
                    SuccessfulExecutions = COALESCE(@SuccessfulExecutions, SuccessfulExecutions),
                    FailedExecutions = COALESCE(@FailedExecutions, FailedExecutions)
                WHERE Id = @ProjectId";

            var command = new CommandDefinition(
                sql,
                new { ProjectId = projectId, IsRunning = isRunning, LastRunAt = lastRunAt,
                      TotalExecutions = totalExecutions, SuccessfulExecutions = successfulExecutions,
                      FailedExecutions = failedExecutions },
                commandTimeout: 30,
                cancellationToken: cancellationToken);

            await connection.ExecuteAsync(command);

            _logger.LogDebug("Updated project {ProjectId} status: IsRunning={IsRunning}, LastRunAt={LastRunAt}",
                projectId, isRunning, lastRunAt);
        }
        catch (Exception ex)
        {
            _logger.LogMethodError(nameof(UpdateProjectStatusAsync), ex);
            throw;
        }
        finally
        {
            _logger.LogMethodExit(nameof(UpdateProjectStatusAsync));
        }
    }

    /// <summary>
    /// Updates sync run progress and metadata using Dapper (eliminates EF SaveChangesAsync overhead).
    /// </summary>
    public async Task UpdateRunProgressAsync(
        Guid runId,
        int? completedSteps = null,
        int? progressPercentage = null,
        string? currentStepName = null,
        string? status = null,
        DateTime? completedAt = null,
        string? errorMessage = null,
        CancellationToken cancellationToken = default)
    {
        _logger.LogMethodEntry(nameof(UpdateRunProgressAsync), new { runId, completedSteps, progressPercentage });

        try
        {
            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            var sql = @"
                UPDATE SyncProjectRuns
                SET CompletedSteps = COALESCE(@CompletedSteps, CompletedSteps),
                    ProgressPercentage = COALESCE(@ProgressPercentage, ProgressPercentage),
                    CurrentStep = COALESCE(@CurrentStepName, CurrentStep),
                    Status = COALESCE(@Status, Status),
                    CompletedAt = COALESCE(@CompletedAt, CompletedAt),
                    ErrorMessage = COALESCE(@ErrorMessage, ErrorMessage)
                WHERE Id = @RunId";

            var command = new CommandDefinition(
                sql,
                new { RunId = runId, CompletedSteps = completedSteps, ProgressPercentage = progressPercentage,
                      CurrentStepName = currentStepName, Status = status, CompletedAt = completedAt,
                      ErrorMessage = errorMessage },
                commandTimeout: 60,  // Increased from 30s to handle database contention
                cancellationToken: cancellationToken);

            await connection.ExecuteAsync(command);

            _logger.LogDebug("Updated run {RunId} progress: {CompletedSteps} steps, {ProgressPercentage}%",
                runId, completedSteps, progressPercentage);
        }
        catch (Exception ex)
        {
            _logger.LogMethodError(nameof(UpdateRunProgressAsync), ex);
            throw;
        }
        finally
        {
            _logger.LogMethodExit(nameof(UpdateRunProgressAsync));
        }
    }

    /// <summary>
    /// Creates a new sync execution record using Dapper (eliminates EF SaveChangesAsync overhead).
    /// </summary>
    public async Task<Guid> CreateSyncExecutionAsync(
        Guid directoryConnectionId,
        DateTime startedAt,
        string status = "Running",
        CancellationToken cancellationToken = default)
    {
        _logger.LogMethodEntry(nameof(CreateSyncExecutionAsync), new { directoryConnectionId, startedAt });

        try
        {
            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            var sql = @"
                INSERT INTO SyncExecutions (Id, DirectoryConnectionId, StartedAt, Status)
                VALUES (@Id, @DirectoryConnectionId, @StartedAt, @Status);
                SELECT @Id;";

            var id = Guid.NewGuid();

            var command = new CommandDefinition(
                sql,
                new { Id = id, DirectoryConnectionId = directoryConnectionId, StartedAt = startedAt, Status = status },
                commandTimeout: 30,
                cancellationToken: cancellationToken);

            await connection.ExecuteAsync(command);

            _logger.LogInformation("Created sync execution {ExecutionId} for connection {ConnectionId}",
                id, directoryConnectionId);

            return id;
        }
        catch (Exception ex)
        {
            _logger.LogMethodError(nameof(CreateSyncExecutionAsync), ex);
            throw;
        }
        finally
        {
            _logger.LogMethodExit(nameof(CreateSyncExecutionAsync));
        }
    }

    /// <summary>
    /// Updates sync execution results using Dapper (eliminates EF SaveChangesAsync overhead).
    /// </summary>
    public async Task UpdateSyncExecutionAsync(
        Guid executionId,
        string? status = null,
        DateTime? completedAt = null,
        int? identitiesAdded = null,
        int? identitiesUpdated = null,
        int? identitiesDeleted = null,
        int? groupsAdded = null,
        int? groupsUpdated = null,
        int? groupsDeleted = null,
        int? membershipsAdded = null,
        int? membershipsRemoved = null,
        int? personsCreated = null,
        int? personsUpdated = null,
        string? errorMessage = null,
        string? executionLog = null,
        CancellationToken cancellationToken = default)
    {
        _logger.LogMethodEntry(nameof(UpdateSyncExecutionAsync), new { executionId, status });

        try
        {
            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            var sql = @"
                UPDATE SyncExecutions
                SET Status = COALESCE(@Status, Status),
                    CompletedAt = COALESCE(@CompletedAt, CompletedAt),
                    IdentitiesAdded = COALESCE(@IdentitiesAdded, IdentitiesAdded),
                    IdentitiesUpdated = COALESCE(@IdentitiesUpdated, IdentitiesUpdated),
                    IdentitiesDeleted = COALESCE(@IdentitiesDeleted, IdentitiesDeleted),
                    GroupsAdded = COALESCE(@GroupsAdded, GroupsAdded),
                    GroupsUpdated = COALESCE(@GroupsUpdated, GroupsUpdated),
                    GroupsDeleted = COALESCE(@GroupsDeleted, GroupsDeleted),
                    MembershipsAdded = COALESCE(@MembershipsAdded, MembershipsAdded),
                    MembershipsRemoved = COALESCE(@MembershipsRemoved, MembershipsRemoved),
                    PersonsCreated = COALESCE(@PersonsCreated, PersonsCreated),
                    PersonsUpdated = COALESCE(@PersonsUpdated, PersonsUpdated),
                    ErrorMessage = COALESCE(@ErrorMessage, ErrorMessage),
                    ExecutionLog = COALESCE(@ExecutionLog, ExecutionLog)
                WHERE Id = @ExecutionId";

            var command = new CommandDefinition(
                sql,
                new {
                    ExecutionId = executionId, Status = status, CompletedAt = completedAt,
                    IdentitiesAdded = identitiesAdded, IdentitiesUpdated = identitiesUpdated, IdentitiesDeleted = identitiesDeleted,
                    GroupsAdded = groupsAdded, GroupsUpdated = groupsUpdated, GroupsDeleted = groupsDeleted,
                    MembershipsAdded = membershipsAdded, MembershipsRemoved = membershipsRemoved,
                    PersonsCreated = personsCreated, PersonsUpdated = personsUpdated,
                    ErrorMessage = errorMessage, ExecutionLog = executionLog
                },
                commandTimeout: 30,
                cancellationToken: cancellationToken);

            await connection.ExecuteAsync(command);

            _logger.LogInformation("Updated sync execution {ExecutionId}: Status={Status}, Identities(+{Added} ~{Updated} -{Deleted})",
                executionId, status, identitiesAdded, identitiesUpdated, identitiesDeleted);
        }
        catch (Exception ex)
        {
            _logger.LogMethodError(nameof(UpdateSyncExecutionAsync), ex);
            throw;
        }
        finally
        {
            _logger.LogMethodExit(nameof(UpdateSyncExecutionAsync));
        }
    }

    public async Task DeleteSyncProjectAsync(Guid projectId, CancellationToken cancellationToken = default)
    {
        _logger.LogMethodEntry(nameof(DeleteSyncProjectAsync), new { projectId });

        try
        {
            await using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            // Use a transaction for atomic deletion
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

            try
            {
                // Get all workflow IDs for this project
                var workflowIds = (await connection.QueryAsync<Guid>(
                    "SELECT Id FROM SyncWorkflows WHERE SyncProjectId = @ProjectId",
                    new { ProjectId = projectId },
                    transaction)).ToList();

                _logger.LogInformation("Deleting sync project {ProjectId} with {WorkflowCount} workflows",
                    projectId, workflowIds.Count);

                if (workflowIds.Any())
                {
                    // Delete in correct order to respect foreign key constraints
                    // Use parameterized queries with DataTable for IN clause
                    var workflowTable = new DataTable();
                    workflowTable.Columns.Add("Id", typeof(Guid));
                    foreach (var id in workflowIds)
                        workflowTable.Rows.Add(id);

                    // 1. Delete audit logs (depends on step runs)
                    await connection.ExecuteAsync(@"
                        DELETE FROM SyncAuditLogs
                        WHERE SyncStepRunId IN (
                            SELECT Id FROM SyncStepRuns
                            WHERE SyncStepId IN (
                                SELECT Id FROM SyncSteps
                                WHERE SyncWorkflowId IN (SELECT Id FROM SyncWorkflows WHERE SyncProjectId = @ProjectId)
                            )
                        )", new { ProjectId = projectId }, transaction, commandTimeout: 300);

                    // 2. Delete step runs
                    await connection.ExecuteAsync(@"
                        DELETE FROM SyncStepRuns
                        WHERE SyncStepId IN (
                            SELECT Id FROM SyncSteps
                            WHERE SyncWorkflowId IN (SELECT Id FROM SyncWorkflows WHERE SyncProjectId = @ProjectId)
                        )", new { ProjectId = projectId }, transaction, commandTimeout: 300);

                    // 3. Delete attribute mappings
                    await connection.ExecuteAsync(@"
                        DELETE FROM AttributeMappings
                        WHERE SyncStepId IN (
                            SELECT Id FROM SyncSteps
                            WHERE SyncWorkflowId IN (SELECT Id FROM SyncWorkflows WHERE SyncProjectId = @ProjectId)
                        )", new { ProjectId = projectId }, transaction, commandTimeout: 300);

                    // 4. Delete sync step tags
                    await connection.ExecuteAsync(@"
                        DELETE FROM SyncStepTags
                        WHERE SyncStepId IN (
                            SELECT Id FROM SyncSteps
                            WHERE SyncWorkflowId IN (SELECT Id FROM SyncWorkflows WHERE SyncProjectId = @ProjectId)
                        )", new { ProjectId = projectId }, transaction, commandTimeout: 300);

                    // 5. Delete steps
                    await connection.ExecuteAsync(@"
                        DELETE FROM SyncSteps
                        WHERE SyncWorkflowId IN (SELECT Id FROM SyncWorkflows WHERE SyncProjectId = @ProjectId)",
                        new { ProjectId = projectId }, transaction, commandTimeout: 300);

                    // 6. Delete workflow tags
                    await connection.ExecuteAsync(@"
                        DELETE FROM WorkflowTags
                        WHERE SyncWorkflowId IN (SELECT Id FROM SyncWorkflows WHERE SyncProjectId = @ProjectId)",
                        new { ProjectId = projectId }, transaction, commandTimeout: 300);

                    // 7. Delete workflows
                    await connection.ExecuteAsync(
                        "DELETE FROM SyncWorkflows WHERE SyncProjectId = @ProjectId",
                        new { ProjectId = projectId }, transaction, commandTimeout: 300);
                }

                // 8. Delete project chains (where this project is source or target)
                await connection.ExecuteAsync(
                    "DELETE FROM SyncProjectChains WHERE SourceProjectId = @ProjectId OR TargetProjectId = @ProjectId",
                    new { ProjectId = projectId }, transaction, commandTimeout: 300);

                // 9. Finally, delete the project itself
                await connection.ExecuteAsync(
                    "DELETE FROM SyncProjects WHERE Id = @ProjectId",
                    new { ProjectId = projectId }, transaction, commandTimeout: 300);

                await transaction.CommitAsync(cancellationToken);

                _logger.LogInformation("Successfully deleted sync project {ProjectId}", projectId);
            }
            catch
            {
                await transaction.RollbackAsync(cancellationToken);
                throw;
            }
        }
        catch (Exception ex)
        {
            _logger.LogMethodError(nameof(DeleteSyncProjectAsync), ex);
            throw;
        }
        finally
        {
            _logger.LogMethodExit(nameof(DeleteSyncProjectAsync));
        }
    }

    public async Task<Guid> CreateSyncProjectRunAsync(
        SyncProjectRun run,
        CancellationToken cancellationToken = default)
    {
        using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        const string sql = @"
            INSERT INTO SyncProjectRuns (
                Id, SyncProjectId, TriggerType, TriggeredBy, StartedAt, Status,
                TotalSteps, CompletedSteps, FailedSteps, SkippedSteps,
                ProgressPercentage, TotalObjectsProcessed, TotalObjectsCreated,
                TotalObjectsUpdated, TotalObjectsDeleted, TotalPersonsCreated, TotalErrors
            ) VALUES (
                @Id, @SyncProjectId, @TriggerType, @TriggeredBy, @StartedAt, @Status,
                @TotalSteps, @CompletedSteps, @FailedSteps, @SkippedSteps,
                @ProgressPercentage, @TotalObjectsProcessed, @TotalObjectsCreated,
                @TotalObjectsUpdated, @TotalObjectsDeleted, @TotalPersonsCreated, @TotalErrors
            )";

        await connection.ExecuteAsync(new CommandDefinition(sql, run, cancellationToken: cancellationToken));
        return run.Id;
    }

    /// <summary>
    /// Create a new sync step run record using Dapper.
    /// </summary>
    public async Task<Guid> CreateSyncStepRunAsync(
        SyncStepRun stepRun,
        CancellationToken cancellationToken = default)
    {
        using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        const string sql = @"
            INSERT INTO SyncStepRuns (
                Id, SyncProjectRunId, SyncStepId, StepName, ObjectClass, StartedAt, Status,
                ObjectsQueried, ObjectsProcessed, ObjectsCreated, ObjectsUpdated,
                ObjectsSkipped, ObjectsDeleted, ErrorCount, ExecutionLog,
                PersonsCreated, PersonsMatched, PersonMatchingSkipped
            ) VALUES (
                @Id, @SyncProjectRunId, @SyncStepId, @StepName, @ObjectClass, @StartedAt, @Status,
                @ObjectsQueried, @ObjectsProcessed, @ObjectsCreated, @ObjectsUpdated,
                @ObjectsSkipped, @ObjectsDeleted, @ErrorCount, @ExecutionLog,
                @PersonsCreated, @PersonsMatched, @PersonMatchingSkipped
            )";

        await connection.ExecuteAsync(new CommandDefinition(sql, stepRun, cancellationToken: cancellationToken));
        return stepRun.Id;
    }

    /// <summary>
    /// Update sync project run final status using Dapper.
    /// </summary>
    public async Task UpdateSyncProjectRunStatusAsync(
        Guid runId,
        string status,
        DateTime? completedAt,
        int? durationSeconds,
        string? errorMessage,
        int totalObjectsProcessed,
        int totalObjectsCreated,
        int totalObjectsUpdated,
        int totalObjectsDeleted,
        int totalPersonsCreated,
        int totalErrors,
        int completedSteps,
        int progressPercentage,
        CancellationToken cancellationToken = default)
    {
        using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        const string sql = @"
            UPDATE SyncProjectRuns SET
                Status = @Status,
                CompletedAt = @CompletedAt,
                DurationSeconds = @DurationSeconds,
                ErrorMessage = @ErrorMessage,
                TotalObjectsProcessed = @TotalObjectsProcessed,
                TotalObjectsCreated = @TotalObjectsCreated,
                TotalObjectsUpdated = @TotalObjectsUpdated,
                TotalObjectsDeleted = @TotalObjectsDeleted,
                TotalPersonsCreated = @TotalPersonsCreated,
                TotalErrors = @TotalErrors,
                CompletedSteps = @CompletedSteps,
                ProgressPercentage = @ProgressPercentage
            WHERE Id = @RunId";

        await connection.ExecuteAsync(new CommandDefinition(sql, new {
            RunId = runId,
            Status = status,
            CompletedAt = completedAt,
            DurationSeconds = durationSeconds,
            ErrorMessage = errorMessage,
            TotalObjectsProcessed = totalObjectsProcessed,
            TotalObjectsCreated = totalObjectsCreated,
            TotalObjectsUpdated = totalObjectsUpdated,
            TotalObjectsDeleted = totalObjectsDeleted,
            TotalPersonsCreated = totalPersonsCreated,
            TotalErrors = totalErrors,
            CompletedSteps = completedSteps,
            ProgressPercentage = progressPercentage
        }, cancellationToken: cancellationToken));
    }

    /// <summary>
    /// Update sync project IsRunning flag and execution counts using Dapper.
    /// </summary>
    public async Task UpdateSyncProjectExecutionStatusAsync(
        Guid projectId,
        bool isRunning,
        bool success,
        CancellationToken cancellationToken = default)
    {
        using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        var sql = @"
            UPDATE SyncProjects WITH (UPDLOCK)
            SET IsRunning = @IsRunning,
                TotalExecutions = TotalExecutions + 1,
                " + (success ? "SuccessfulExecutions = SuccessfulExecutions + 1, LastSuccessfulRunAt = GETUTCDATE()" : "FailedExecutions = FailedExecutions + 1") + @"
            WHERE Id = @ProjectId";

        await connection.ExecuteAsync(new CommandDefinition(sql, new {
            ProjectId = projectId,
            IsRunning = isRunning
        }, cancellationToken: cancellationToken));
    }

    /// <summary>
    /// Synchronizes step tags - removes tags not in the list and adds new ones using Dapper.
    /// </summary>
    public async Task SynchronizeStepTagsAsync(Guid stepId, List<Guid> tagIds, CancellationToken cancellationToken = default)
    {
        using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        // Get existing tag IDs for this step
        var existingTagIds = (await connection.QueryAsync<Guid>(
            "SELECT TagId FROM SyncStepTags WHERE SyncStepId = @StepId",
            new { StepId = stepId })).ToHashSet();

        var selectedTagIds = tagIds.ToHashSet();

        // Remove tags that are no longer selected
        var tagIdsToRemove = existingTagIds.Where(id => !selectedTagIds.Contains(id)).ToList();
        if (tagIdsToRemove.Any())
        {
            await connection.ExecuteAsync(
                "DELETE FROM SyncStepTags WHERE SyncStepId = @StepId AND TagId IN @TagIds",
                new { StepId = stepId, TagIds = tagIdsToRemove });
            _logger.LogInformation("Removed {Count} tags from step {StepId}", tagIdsToRemove.Count, stepId);
        }

        // Add new tags
        var tagIdsToAdd = selectedTagIds.Where(id => !existingTagIds.Contains(id)).ToList();
        if (tagIdsToAdd.Any())
        {
            var insertParams = tagIdsToAdd.Select(tagId => new
            {
                Id = Guid.NewGuid(),
                SyncStepId = stepId,
                TagId = tagId,
                CreatedAt = DateTime.UtcNow
            });

            await connection.ExecuteAsync(
                @"INSERT INTO SyncStepTags (Id, SyncStepId, TagId, CreatedAt)
                  VALUES (@Id, @SyncStepId, @TagId, @CreatedAt)",
                insertParams);
            _logger.LogInformation("Added {Count} tags to step {StepId}", tagIdsToAdd.Count, stepId);
        }
    }

    /// <summary>
    /// Bulk inserts step tags for a newly created step using Dapper.
    /// </summary>
    public async Task InsertStepTagsAsync(Guid stepId, List<Guid> tagIds, CancellationToken cancellationToken = default)
    {
        if (!tagIds.Any()) return;

        // Deduplicate tagIds to prevent duplicate inserts
        var uniqueTagIds = tagIds.Distinct().ToList();

        using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        // Check for existing tags and only insert new ones
        var existingTagIds = (await connection.QueryAsync<Guid>(
            "SELECT TagId FROM SyncStepTags WHERE SyncStepId = @StepId",
            new { StepId = stepId })).ToHashSet();

        var tagIdsToInsert = uniqueTagIds.Where(id => !existingTagIds.Contains(id)).ToList();
        if (!tagIdsToInsert.Any())
        {
            _logger.LogInformation("No new tags to insert for step {StepId} - all {Count} tags already exist", stepId, uniqueTagIds.Count);
            return;
        }

        var insertParams = tagIdsToInsert.Select(tagId => new
        {
            Id = Guid.NewGuid(),
            SyncStepId = stepId,
            TagId = tagId,
            CreatedAt = DateTime.UtcNow
        });

        await connection.ExecuteAsync(
            @"INSERT INTO SyncStepTags (Id, SyncStepId, TagId, CreatedAt)
              VALUES (@Id, @SyncStepId, @TagId, @CreatedAt)",
            insertParams);

        _logger.LogInformation("Inserted {Count} tags for new step {StepId}", tagIdsToInsert.Count, stepId);
    }
}
