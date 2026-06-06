using Dapper;
using DataAccessLibrary.Models;
using Logging;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;

namespace DataAccessLibrary.Repositories;

public class SyncConfigRepository : DapperRepositoryBase, ISyncConfigRepository
{
    public SyncConfigRepository(IConfiguration configuration, IGlobalLogger logger) : base(configuration, logger) { }

    private SqlConnection CreateConnection() => new SqlConnection(_connectionString);

    #region Full Tree Loading

    public async Task<SyncProject?> GetSyncProjectWithFullTreeAsync(Guid projectId)
    {
        using var conn = CreateConnection();
        await conn.OpenAsync().ConfigureAwait(false);

        // Load all levels in a single round-trip using QueryMultiple
        using var multi = await conn.QueryMultipleAsync(@"
            SELECT * FROM SyncProjects WHERE Id = @ProjectId;
            SELECT * FROM SyncWorkflows WHERE SyncProjectId = @ProjectId ORDER BY ExecutionOrder;
            SELECT s.* FROM SyncSteps s
                INNER JOIN SyncWorkflows w ON s.SyncWorkflowId = w.Id
                WHERE w.SyncProjectId = @ProjectId ORDER BY s.ExecutionOrder;
            SELECT am.* FROM AttributeMappings am
                INNER JOIN SyncSteps s ON am.SyncStepId = s.Id
                INNER JOIN SyncWorkflows w ON s.SyncWorkflowId = w.Id
                WHERE w.SyncProjectId = @ProjectId ORDER BY am.SyncStepId;",
            new { ProjectId = projectId }).ConfigureAwait(false);

        var project = await multi.ReadFirstOrDefaultAsync<SyncProject>().ConfigureAwait(false);
        if (project == null) return null;

        var workflows = (await multi.ReadAsync<SyncWorkflow>().ConfigureAwait(false)).ToList();
        var steps = (await multi.ReadAsync<SyncStep>().ConfigureAwait(false)).ToList();
        var mappings = (await multi.ReadAsync<AttributeMapping>().ConfigureAwait(false)).ToList();

        // Stitch the tree together
        var stepsLookup = steps.ToLookup(s => s.SyncWorkflowId);
        var mappingsLookup = mappings.ToLookup(m => m.SyncStepId);

        foreach (var workflow in workflows)
        {
            workflow.Steps = stepsLookup[workflow.Id].ToList();
            foreach (var step in workflow.Steps)
            {
                step.AttributeMappings = mappingsLookup[step.Id].ToList();
            }
        }
        project.Workflows = workflows;

        return project;
    }

    #endregion

    #region Sync Workflows

    public async Task<List<SyncWorkflow>> GetSyncWorkflowsForProjectAsync(Guid projectId)
    {
        using var conn = CreateConnection();
        var result = await conn.QueryAsync<SyncWorkflow>(
            "SELECT * FROM SyncWorkflows WHERE SyncProjectId = @ProjectId ORDER BY ExecutionOrder",
            new { ProjectId = projectId }).ConfigureAwait(false);
        return result.ToList();
    }

    public async Task<SyncWorkflow?> GetSyncWorkflowAsync(Guid id)
    {
        using var conn = CreateConnection();
        return await conn.QueryFirstOrDefaultAsync<SyncWorkflow>(
            "SELECT * FROM SyncWorkflows WHERE Id = @Id", new { Id = id }).ConfigureAwait(false);
    }

    public async Task<Guid> CreateSyncWorkflowAsync(SyncWorkflow workflow)
    {
        using var conn = CreateConnection();
        workflow.Id = workflow.Id == Guid.Empty ? Guid.NewGuid() : workflow.Id;
        workflow.CreatedAt = DateTime.UtcNow;

        await conn.ExecuteAsync(@"
            INSERT INTO SyncWorkflows (Id, SyncProjectId, Name, Description, ObjectClass, WorkflowType,
                ExecutionOrder, IsEnabled, ContinueOnError, MaxExecutionTimeMinutes, CreatedAt)
            VALUES (@Id, @SyncProjectId, @Name, @Description, @ObjectClass, @WorkflowType,
                @ExecutionOrder, @IsEnabled, @ContinueOnError, @MaxExecutionTimeMinutes, @CreatedAt)",
            workflow).ConfigureAwait(false);

        return workflow.Id;
    }

    public async Task UpdateSyncWorkflowAsync(SyncWorkflow workflow)
    {
        using var conn = CreateConnection();
        workflow.ModifiedAt = DateTime.UtcNow;
        await conn.ExecuteAsync(@"
            UPDATE SyncWorkflows SET
                Name = @Name, Description = @Description, ObjectClass = @ObjectClass,
                WorkflowType = @WorkflowType, ExecutionOrder = @ExecutionOrder,
                IsEnabled = @IsEnabled, ContinueOnError = @ContinueOnError,
                MaxExecutionTimeMinutes = @MaxExecutionTimeMinutes, ModifiedAt = @ModifiedAt
            WHERE Id = @Id", workflow).ConfigureAwait(false);
    }

    public async Task DeleteSyncWorkflowAsync(Guid id)
    {
        using var conn = CreateConnection();
        // Cascade: delete mappings -> steps -> workflow
        await conn.ExecuteAsync(@"
            DELETE am FROM AttributeMappings am
            INNER JOIN SyncSteps s ON am.SyncStepId = s.Id
            WHERE s.SyncWorkflowId = @Id", new { Id = id }).ConfigureAwait(false);
        await conn.ExecuteAsync("DELETE FROM SyncSteps WHERE SyncWorkflowId = @Id", new { Id = id }).ConfigureAwait(false);
        await conn.ExecuteAsync("DELETE FROM SyncWorkflows WHERE Id = @Id", new { Id = id }).ConfigureAwait(false);
    }

    #endregion

    #region Sync Steps

    public async Task<List<SyncStep>> GetSyncStepsForWorkflowAsync(Guid workflowId)
    {
        using var conn = CreateConnection();
        var result = await conn.QueryAsync<SyncStep>(
            "SELECT * FROM SyncSteps WHERE SyncWorkflowId = @WorkflowId ORDER BY ExecutionOrder",
            new { WorkflowId = workflowId }).ConfigureAwait(false);
        return result.ToList();
    }

    public async Task<SyncStep?> GetSyncStepAsync(Guid id)
    {
        using var conn = CreateConnection();
        return await conn.QueryFirstOrDefaultAsync<SyncStep>(
            "SELECT * FROM SyncSteps WHERE Id = @Id", new { Id = id }).ConfigureAwait(false);
    }

    public async Task<Guid> CreateSyncStepAsync(SyncStep step)
    {
        using var conn = CreateConnection();
        step.Id = step.Id == Guid.Empty ? Guid.NewGuid() : step.Id;
        step.CreatedAt = DateTime.UtcNow;

        await conn.ExecuteAsync(@"
            INSERT INTO SyncSteps (Id, SyncWorkflowId, Name, Description, ExecutionOrder, ObjectClass,
                StepType, IsEnabled, ContinueOnError, Configuration, CreatedAt)
            VALUES (@Id, @SyncWorkflowId, @Name, @Description, @ExecutionOrder, @ObjectClass,
                @StepType, @IsEnabled, @ContinueOnError, @Configuration, @CreatedAt)",
            step).ConfigureAwait(false);

        return step.Id;
    }

    public async Task UpdateSyncStepAsync(SyncStep step)
    {
        using var conn = CreateConnection();
        step.ModifiedAt = DateTime.UtcNow;
        await conn.ExecuteAsync(@"
            UPDATE SyncSteps SET
                Name = @Name, Description = @Description, ExecutionOrder = @ExecutionOrder,
                ObjectClass = @ObjectClass, StepType = @StepType,
                IsEnabled = @IsEnabled, ContinueOnError = @ContinueOnError,
                Configuration = @Configuration, ModifiedAt = @ModifiedAt
            WHERE Id = @Id", step).ConfigureAwait(false);
    }

    public async Task DeleteSyncStepAsync(Guid id)
    {
        using var conn = CreateConnection();
        await conn.ExecuteAsync("DELETE FROM AttributeMappings WHERE SyncStepId = @Id", new { Id = id }).ConfigureAwait(false);
        await conn.ExecuteAsync("DELETE FROM SyncSteps WHERE Id = @Id", new { Id = id }).ConfigureAwait(false);
    }

    #endregion

    #region Attribute Mappings

    public async Task<List<AttributeMapping>> GetAttributeMappingsForStepAsync(Guid stepId)
    {
        using var conn = CreateConnection();
        var result = await conn.QueryAsync<AttributeMapping>(
            "SELECT * FROM AttributeMappings WHERE SyncStepId = @StepId ORDER BY SourceAttribute",
            new { StepId = stepId }).ConfigureAwait(false);
        return result.ToList();
    }

    public async Task<Guid> CreateAttributeMappingAsync(AttributeMapping mapping)
    {
        using var conn = CreateConnection();
        mapping.Id = mapping.Id == Guid.Empty ? Guid.NewGuid() : mapping.Id;
        mapping.CreatedAt = DateTime.UtcNow;

        await conn.ExecuteAsync(@"
            INSERT INTO AttributeMappings (Id, SyncStepId, SourceAttribute, SourceDisplayName, DataType, TargetType, TargetAttribute,
                TransformationType, TransformationExpression, DefaultValue, IsRequired, UseForMatching, MatchWeight,
                UseFuzzyMatch, FuzzyMatchThreshold, FuzzyMatchAlgorithm, ExecutionOrder, IsEnabled, CreatedAt)
            VALUES (@Id, @SyncStepId, @SourceAttribute, @SourceDisplayName, @DataType, @TargetType, @TargetAttribute,
                @TransformationType, @TransformationExpression, @DefaultValue, @IsRequired, @UseForMatching, @MatchWeight,
                @UseFuzzyMatch, @FuzzyMatchThreshold, @FuzzyMatchAlgorithm, @ExecutionOrder, @IsEnabled, @CreatedAt)",
            mapping).ConfigureAwait(false);

        return mapping.Id;
    }

    public async Task UpdateAttributeMappingAsync(AttributeMapping mapping)
    {
        using var conn = CreateConnection();
        await conn.ExecuteAsync(@"
            UPDATE AttributeMappings SET
                SourceAttribute = @SourceAttribute, SourceDisplayName = @SourceDisplayName,
                DataType = @DataType, TargetType = @TargetType, TargetAttribute = @TargetAttribute,
                TransformationType = @TransformationType, TransformationExpression = @TransformationExpression,
                DefaultValue = @DefaultValue, IsRequired = @IsRequired, IsEnabled = @IsEnabled,
                UseForMatching = @UseForMatching, MatchWeight = @MatchWeight,
                ExecutionOrder = @ExecutionOrder
            WHERE Id = @Id", mapping).ConfigureAwait(false);
    }

    public async Task DeleteAttributeMappingAsync(Guid id)
    {
        using var conn = CreateConnection();
        await conn.ExecuteAsync("DELETE FROM AttributeMappings WHERE Id = @Id", new { Id = id }).ConfigureAwait(false);
    }

    public async Task DeleteAttributeMappingsForStepAsync(Guid stepId)
    {
        using var conn = CreateConnection();
        await conn.ExecuteAsync("DELETE FROM AttributeMappings WHERE SyncStepId = @StepId", new { StepId = stepId }).ConfigureAwait(false);
    }

    #endregion

    #region Sync Project CRUD

    public async Task<Guid> CreateSyncProjectAsync(SyncProject project)
    {
        using var conn = CreateConnection();
        project.Id = project.Id == Guid.Empty ? Guid.NewGuid() : project.Id;
        project.CreatedAt = DateTime.UtcNow;

        await conn.ExecuteAsync(@"
            INSERT INTO SyncProjects (Id, Name, Description, SourceConnectionId, TargetConnectionId,
                SyncDirection, IsTemplateMode, ProjectType, ConflictResolutionStrategy,
                AutoCreateIdentities, EnableManagerAssignment, MinMatchConfidenceThreshold,
                Priority, LogLevel, CronSchedule, IsEnabled, IsRunning, NextScheduledRunAt,
                LastRunAt, SourceSyncProjectId, CreatedAt)
            VALUES (@Id, @Name, @Description, @SourceConnectionId, @TargetConnectionId,
                @SyncDirection, @IsTemplateMode, @ProjectType, @ConflictResolutionStrategy,
                @AutoCreateIdentities, @EnableManagerAssignment, @MinMatchConfidenceThreshold,
                @Priority, @LogLevel, @CronSchedule, @IsEnabled, @IsRunning, @NextScheduledRunAt,
                @LastRunAt, @SourceSyncProjectId, @CreatedAt)",
            project).ConfigureAwait(false);

        return project.Id;
    }

    public async Task UpdateSyncProjectAsync(SyncProject project)
    {
        using var conn = CreateConnection();
        project.ModifiedAt = DateTime.UtcNow;
        await conn.ExecuteAsync(@"
            UPDATE SyncProjects SET
                Name = @Name, Description = @Description,
                SourceConnectionId = @SourceConnectionId, TargetConnectionId = @TargetConnectionId,
                SyncDirection = @SyncDirection, CronSchedule = @CronSchedule,
                IsEnabled = @IsEnabled, NextScheduledRunAt = @NextScheduledRunAt,
                ModifiedAt = @ModifiedAt
            WHERE Id = @Id", project).ConfigureAwait(false);
    }

    public async Task DeleteSyncProjectAsync(Guid id)
    {
        using var conn = CreateConnection();
        await conn.OpenAsync().ConfigureAwait(false);
        using var tx = conn.BeginTransaction();

        // Delete in dependency order
        await conn.ExecuteAsync(@"
            DELETE am FROM AttributeMappings am
            INNER JOIN SyncSteps s ON am.SyncStepId = s.Id
            INNER JOIN SyncWorkflows w ON s.SyncWorkflowId = w.Id
            WHERE w.SyncProjectId = @Id", new { Id = id }, tx).ConfigureAwait(false);

        await conn.ExecuteAsync(@"
            DELETE s FROM SyncSteps s
            INNER JOIN SyncWorkflows w ON s.SyncWorkflowId = w.Id
            WHERE w.SyncProjectId = @Id", new { Id = id }, tx).ConfigureAwait(false);

        await conn.ExecuteAsync("DELETE FROM SyncWorkflows WHERE SyncProjectId = @Id", new { Id = id }, tx).ConfigureAwait(false);
        await conn.ExecuteAsync("DELETE FROM SyncProjectRuns WHERE SyncProjectId = @Id", new { Id = id }, tx).ConfigureAwait(false);
        await conn.ExecuteAsync("DELETE FROM SyncProjects WHERE Id = @Id", new { Id = id }, tx).ConfigureAwait(false);

        tx.Commit();
    }

    #endregion

    #region Internal Sync Steps

    public async Task<List<InternalSyncStep>> GetInternalSyncStepsAsync(Guid projectId)
    {
        using var conn = CreateConnection();
        var steps = (await conn.QueryAsync<InternalSyncStep>(@"
            SELECT * FROM InternalSyncSteps WHERE SyncProjectId = @ProjectId ORDER BY ExecutionOrder",
            new { ProjectId = projectId }).ConfigureAwait(false)).ToList();

        if (steps.Any())
        {
            var stepIds = steps.Select(s => s.Id).ToList();
            var mappings = (await conn.QueryAsync<InternalSyncStepMapping>(@"
                SELECT * FROM InternalSyncStepMappings WHERE InternalSyncStepId IN @StepIds",
                new { StepIds = stepIds }).ConfigureAwait(false)).ToList();

            foreach (var step in steps)
            {
                step.Mappings = mappings.Where(m => m.InternalSyncStepId == step.Id).ToList();
            }
        }

        return steps;
    }

    public async Task CreateInternalSyncStepAsync(InternalSyncStep step)
    {
        using var conn = CreateConnection();
        await conn.ExecuteAsync(@"
            INSERT INTO InternalSyncSteps (Id, SyncProjectId, Name, Description, ExecutionOrder, Direction, StepType,
                ObjectClassFilter, IsEnabled, ContinueOnError, Configuration, SourceConnectionId, CreatedAt, ModifiedAt)
            VALUES (@Id, @SyncProjectId, @Name, @Description, @ExecutionOrder, @Direction, @StepType,
                @ObjectClassFilter, @IsEnabled, @ContinueOnError, @Configuration, @SourceConnectionId, @CreatedAt, @ModifiedAt)",
            step).ConfigureAwait(false);
    }

    public async Task CreateInternalSyncStepMappingAsync(InternalSyncStepMapping mapping)
    {
        using var conn = CreateConnection();
        await conn.ExecuteAsync(@"
            INSERT INTO InternalSyncStepMappings (Id, InternalSyncStepId, SourceField, TargetField, OverwriteExisting, IsRequired, DefaultValue, Transformation, MappingOrder, IsEnabled)
            VALUES (@Id, @InternalSyncStepId, @SourceField, @TargetField, @OverwriteExisting, @IsRequired, @DefaultValue, @Transformation, @MappingOrder, @IsEnabled)",
            mapping).ConfigureAwait(false);
    }

    public async Task ToggleSyncProjectEnabledAsync(Guid projectId, bool isEnabled)
    {
        using var conn = CreateConnection();
        await conn.ExecuteAsync(
            "UPDATE SyncProjects SET IsEnabled = @IsEnabled WHERE Id = @Id",
            new { Id = projectId, IsEnabled = isEnabled }).ConfigureAwait(false);
    }

    #endregion

    #region Cascade Deletion

    public async Task DeleteWorkflowsCascadeAsync(List<Guid> workflowIds)
    {
        if (!workflowIds.Any()) return;
        using var conn = CreateConnection();
        await conn.OpenAsync().ConfigureAwait(false);
        using var transaction = conn.BeginTransaction();

        var ids = workflowIds.ToArray();

        // Delete in correct dependency order using parameterized queries
        await conn.ExecuteAsync(
            "DELETE FROM SyncScriptExecutions WHERE SyncStepRunId IN (SELECT Id FROM SyncStepRuns WHERE SyncStepId IN (SELECT Id FROM SyncSteps WHERE SyncWorkflowId IN @Ids))",
            new { Ids = ids }, transaction).ConfigureAwait(false);
        await conn.ExecuteAsync(
            "DELETE FROM SyncAuditLogs WHERE SyncStepRunId IN (SELECT Id FROM SyncStepRuns WHERE SyncStepId IN (SELECT Id FROM SyncSteps WHERE SyncWorkflowId IN @Ids))",
            new { Ids = ids }, transaction).ConfigureAwait(false);
        await conn.ExecuteAsync(
            "DELETE FROM SyncStepRuns WHERE SyncStepId IN (SELECT Id FROM SyncSteps WHERE SyncWorkflowId IN @Ids)",
            new { Ids = ids }, transaction).ConfigureAwait(false);
        await conn.ExecuteAsync(
            "DELETE FROM SyncStepScripts WHERE SyncStepId IN (SELECT Id FROM SyncSteps WHERE SyncWorkflowId IN @Ids)",
            new { Ids = ids }, transaction).ConfigureAwait(false);
        await conn.ExecuteAsync(
            "DELETE FROM SyncStepTags WHERE SyncStepId IN (SELECT Id FROM SyncSteps WHERE SyncWorkflowId IN @Ids)",
            new { Ids = ids }, transaction).ConfigureAwait(false);
        await conn.ExecuteAsync(
            "DELETE FROM AttributeMappings WHERE SyncStepId IN (SELECT Id FROM SyncSteps WHERE SyncWorkflowId IN @Ids)",
            new { Ids = ids }, transaction).ConfigureAwait(false);
        await conn.ExecuteAsync(
            "DELETE FROM SyncSteps WHERE SyncWorkflowId IN @Ids",
            new { Ids = ids }, transaction).ConfigureAwait(false);
        await conn.ExecuteAsync(
            "DELETE FROM WorkflowTags WHERE SyncWorkflowId IN @Ids",
            new { Ids = ids }, transaction).ConfigureAwait(false);
        await conn.ExecuteAsync(
            "DELETE FROM SyncWorkflows WHERE Id IN @Ids",
            new { Ids = ids }, transaction).ConfigureAwait(false);

        transaction.Commit();
    }

    public async Task DeleteStepsCascadeAsync(List<Guid> stepIds)
    {
        if (!stepIds.Any()) return;
        using var conn = CreateConnection();
        await conn.OpenAsync().ConfigureAwait(false);
        using var transaction = conn.BeginTransaction();

        var ids = stepIds.ToArray();

        await conn.ExecuteAsync(
            "DELETE FROM SyncScriptExecutions WHERE SyncStepRunId IN (SELECT Id FROM SyncStepRuns WHERE SyncStepId IN @Ids)",
            new { Ids = ids }, transaction).ConfigureAwait(false);
        await conn.ExecuteAsync(
            "DELETE FROM SyncAuditLogs WHERE SyncStepRunId IN (SELECT Id FROM SyncStepRuns WHERE SyncStepId IN @Ids)",
            new { Ids = ids }, transaction).ConfigureAwait(false);
        await conn.ExecuteAsync(
            "DELETE FROM SyncStepRuns WHERE SyncStepId IN @Ids",
            new { Ids = ids }, transaction).ConfigureAwait(false);
        await conn.ExecuteAsync(
            "DELETE FROM SyncStepScripts WHERE SyncStepId IN @Ids",
            new { Ids = ids }, transaction).ConfigureAwait(false);
        await conn.ExecuteAsync(
            "DELETE FROM SyncStepTags WHERE SyncStepId IN @Ids",
            new { Ids = ids }, transaction).ConfigureAwait(false);
        await conn.ExecuteAsync(
            "DELETE FROM AttributeMappings WHERE SyncStepId IN @Ids",
            new { Ids = ids }, transaction).ConfigureAwait(false);
        await conn.ExecuteAsync(
            "DELETE FROM SyncSteps WHERE Id IN @Ids",
            new { Ids = ids }, transaction).ConfigureAwait(false);

        transaction.Commit();
    }

    #endregion

    #region Sync Project Chains

    public async Task<List<SyncProjectChain>> GetProjectChainsAsync(Guid sourceProjectId)
    {
        using var conn = CreateConnection();
        var result = await conn.QueryAsync<SyncProjectChain>(
            "SELECT * FROM SyncProjectChains WHERE SourceProjectId = @SourceProjectId ORDER BY ExecutionOrder",
            new { SourceProjectId = sourceProjectId }).ConfigureAwait(false);
        return result.ToList();
    }

    public async Task DeleteProjectChainsAsync(List<Guid> chainIds)
    {
        if (!chainIds.Any()) return;
        using var conn = CreateConnection();
        await conn.ExecuteAsync(
            "DELETE FROM SyncProjectChains WHERE Id IN @Ids",
            new { Ids = chainIds }).ConfigureAwait(false);
    }

    public async Task CreateProjectChainAsync(SyncProjectChain chain)
    {
        using var conn = CreateConnection();
        await conn.ExecuteAsync(@"
            INSERT INTO SyncProjectChains (Id, SourceProjectId, TargetProjectId, ExecutionOrder, TriggerCondition, IsEnabled, DelaySeconds, CreatedAt)
            VALUES (@Id, @SourceProjectId, @TargetProjectId, @ExecutionOrder, @TriggerCondition, @IsEnabled, @DelaySeconds, @CreatedAt)",
            chain).ConfigureAwait(false);
    }

    #endregion

    #region Workflow Tags

    public async Task<List<WorkflowTag>> GetWorkflowTagsAsync(Guid workflowId)
    {
        using var conn = CreateConnection();
        var result = await conn.QueryAsync<WorkflowTag>(
            "SELECT * FROM WorkflowTags WHERE SyncWorkflowId = @WorkflowId",
            new { WorkflowId = workflowId }).ConfigureAwait(false);
        return result.ToList();
    }

    public async Task DeleteWorkflowTagAsync(Guid workflowTagId)
    {
        using var conn = CreateConnection();
        await conn.ExecuteAsync(
            "DELETE FROM WorkflowTags WHERE Id = @Id",
            new { Id = workflowTagId }).ConfigureAwait(false);
    }

    public async Task CreateWorkflowTagAsync(WorkflowTag tag)
    {
        using var conn = CreateConnection();
        await conn.ExecuteAsync(@"
            INSERT INTO WorkflowTags (Id, SyncWorkflowId, TagId, CreatedAt, CreatedBy)
            VALUES (@Id, @SyncWorkflowId, @TagId, @CreatedAt, @CreatedBy)",
            tag).ConfigureAwait(false);
    }

    #endregion

    #region Schedule Updates

    public async Task UpdateProjectScheduleAsync(Guid projectId, DateTime? nextScheduledRunAt)
    {
        using var conn = CreateConnection();
        await conn.ExecuteAsync(
            "UPDATE SyncProjects SET NextScheduledRunAt = @NextScheduledRunAt WHERE Id = @Id",
            new { Id = projectId, NextScheduledRunAt = nextScheduledRunAt }).ConfigureAwait(false);
    }

    public async Task ClearProjectScheduleAsync(Guid projectId)
    {
        using var conn = CreateConnection();
        await conn.ExecuteAsync(
            "UPDATE SyncProjects SET NextScheduledRunAt = NULL, CronSchedule = NULL WHERE Id = @Id",
            new { Id = projectId }).ConfigureAwait(false);
    }

    #endregion

    #region Internal Sync Step Operations

    public async Task UpdateInternalSyncStepEnabledAsync(Guid id, bool isEnabled)
    {
        using var conn = CreateConnection();
        await conn.ExecuteAsync(
            "UPDATE InternalSyncSteps SET IsEnabled = @IsEnabled, ModifiedAt = @ModifiedAt WHERE Id = @Id",
            new { Id = id, IsEnabled = isEnabled, ModifiedAt = DateTime.UtcNow }).ConfigureAwait(false);
    }

    public async Task DeleteInternalSyncStepWithMappingsAsync(Guid id)
    {
        using var conn = CreateConnection();
        await conn.ExecuteAsync("DELETE FROM InternalSyncStepMappings WHERE InternalSyncStepId = @Id", new { Id = id }).ConfigureAwait(false);
        await conn.ExecuteAsync("DELETE FROM InternalSyncSteps WHERE Id = @Id", new { Id = id }).ConfigureAwait(false);
    }

    public async Task UpdateInternalSyncStepAsync(InternalSyncStep step)
    {
        using var conn = CreateConnection();
        await conn.ExecuteAsync(@"
            UPDATE InternalSyncSteps SET
                Name = @Name, Description = @Description, ExecutionOrder = @ExecutionOrder,
                ObjectClassFilter = @ObjectClassFilter, Direction = @Direction,
                SourceConnectionId = @SourceConnectionId, TagFilter = @TagFilter,
                IsEnabled = @IsEnabled, ContinueOnError = @ContinueOnError, ModifiedAt = @ModifiedAt
            WHERE Id = @Id",
            new
            {
                step.Id,
                step.Name,
                step.Description,
                step.ExecutionOrder,
                step.ObjectClassFilter,
                step.Direction,
                step.SourceConnectionId,
                step.TagFilter,
                step.IsEnabled,
                step.ContinueOnError,
                ModifiedAt = DateTime.UtcNow
            }).ConfigureAwait(false);
    }

    public async Task DeleteInternalSyncStepMappingsAsync(Guid stepId)
    {
        using var conn = CreateConnection();
        await conn.ExecuteAsync(
            "DELETE FROM InternalSyncStepMappings WHERE InternalSyncStepId = @StepId",
            new { StepId = stepId }).ConfigureAwait(false);
    }

    public async Task BulkUpdateInternalSyncStepScopesAsync(List<Guid> stepIds, string? tagFilter, Guid? sourceConnectionId)
    {
        if (!stepIds.Any()) return;
        using var conn = CreateConnection();

        var setClauses = new List<string>();
        var p = new DynamicParameters();
        p.Add("Ids", stepIds);
        p.Add("ModifiedAt", DateTime.UtcNow);

        if (tagFilter != null)
        {
            setClauses.Add("TagFilter = @TagFilter");
            p.Add("TagFilter", tagFilter);
        }
        if (sourceConnectionId.HasValue)
        {
            setClauses.Add("SourceConnectionId = @SourceConnectionId");
            p.Add("SourceConnectionId", sourceConnectionId.Value);
        }

        if (!setClauses.Any()) return;
        setClauses.Add("ModifiedAt = @ModifiedAt");

        var sql = $"UPDATE InternalSyncSteps SET {string.Join(", ", setClauses)} WHERE Id IN @Ids";
        await conn.ExecuteAsync(sql, p).ConfigureAwait(false);
    }

    #endregion

    #region Sync Projects Query

    public async Task<List<SyncProject>> GetSyncProjectsByTypeAsync(params string[] projectTypes)
    {
        using var conn = CreateConnection();
        var result = await conn.QueryAsync<SyncProject>(
            "SELECT * FROM SyncProjects WHERE ProjectType IN @Types AND IsEnabled = 1 ORDER BY Name",
            new { Types = projectTypes }).ConfigureAwait(false);
        return result.ToList();
    }

    public async Task<List<SyncProjectChain>> GetEnabledProjectChainsAsync(Guid sourceProjectId)
    {
        using var conn = CreateConnection();
        var result = await conn.QueryAsync<SyncProjectChain>(
            "SELECT * FROM SyncProjectChains WHERE SourceProjectId = @SourceProjectId AND IsEnabled = 1 ORDER BY ExecutionOrder",
            new { SourceProjectId = sourceProjectId }).ConfigureAwait(false);
        return result.ToList();
    }

    #endregion

    #region Sync Step Scope/Filter Updates

    public async Task UpdateSyncStepFilterAndScopesAsync(Guid stepId, string? ldapFilter, string? searchBase, string? excludedSearchBases)
    {
        using var conn = CreateConnection();
        await conn.ExecuteAsync(@"
            UPDATE SyncSteps SET LdapFilter = @LdapFilter, SearchBase = @SearchBase,
                ExcludedSearchBases = @ExcludedSearchBases, ModifiedAt = @ModifiedAt
            WHERE Id = @Id",
            new { Id = stepId, LdapFilter = ldapFilter, SearchBase = searchBase,
                ExcludedSearchBases = excludedSearchBases, ModifiedAt = DateTime.UtcNow }).ConfigureAwait(false);
    }

    #endregion
}
