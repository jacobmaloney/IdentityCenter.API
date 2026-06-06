using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Data.SqlClient;
using Dapper;
using DataAccessLibrary.Models;
using DataAccessLibrary.Repositories;

namespace DataAccessLibrary.Services;

/// <summary>
/// Background service that processes post-sync tasks asynchronously.
/// Enables "lightning fast" sync by deferring expensive operations like person matching
/// and manager assignment to background processing after sync completes.
/// </summary>
public class PostSyncTaskService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<PostSyncTaskService> _logger;
    private readonly IConfiguration _configuration;
    private readonly TimeSpan _pollInterval = TimeSpan.FromSeconds(30);

    public PostSyncTaskService(
        IServiceProvider serviceProvider,
        ILogger<PostSyncTaskService> logger,
        IConfiguration configuration)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
        _configuration = configuration;
    }

    private string GetConnectionString()
    {
        return _configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("⚡ PostSyncTaskService started - ready to process background tasks");

        // Stagger startup to avoid connection pool stampede
        await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessPendingTasksAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing post-sync tasks: {Message}", ex.Message);
            }

            // Wait before checking for more tasks
            await Task.Delay(_pollInterval, stoppingToken);
        }

        _logger.LogInformation("⚡ PostSyncTaskService stopped");
    }

    private bool _schemaReady = false;
    private DateTime _lastSchemaCheck = DateTime.MinValue;

    private async Task ProcessPendingTasksAsync(CancellationToken cancellationToken)
    {
        // ✅ FIX: Check if schema is ready before querying
        // This prevents error spam during initial setup when migrations haven't been applied yet
        if (!_schemaReady)
        {
            // Only check schema every 30 seconds to avoid excessive database calls
            if ((DateTime.UtcNow - _lastSchemaCheck).TotalSeconds < 30)
            {
                return;
            }
            _lastSchemaCheck = DateTime.UtcNow;

            try
            {
                await using var checkConnection = new SqlConnection(GetConnectionString());
                await checkConnection.OpenAsync(cancellationToken);

                // Try a simple query to verify schema is ready
                await checkConnection.ExecuteScalarAsync<int>(
                    "SELECT TOP 1 1 FROM PostSyncTasks");

                _schemaReady = true;
                _logger.LogInformation("✅ PostSyncTaskService: Database schema is ready");
            }
            catch (SqlException sqlEx) when (sqlEx.Number == 208) // Invalid object name
            {
                _logger.LogDebug("⏳ PostSyncTaskService: Schema not ready yet (table doesn't exist). Will retry...");
                return;
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "⏳ PostSyncTaskService: Schema check failed. Will retry...");
                return;
            }
        }

        // ✅ FIX: Add connection resilience - transient DB errors should not crash service
        const int maxRetries = 3;
        int retryDelayMs = 1000;

        for (int attempt = 1; attempt <= maxRetries; attempt++)
        {
            try
            {
                await using var connection = new SqlConnection(GetConnectionString());
                await connection.OpenAsync(cancellationToken);

                // Query for pending tasks, ordered by priority (lower number = higher priority)
                const string selectPendingSql = @"
                    SELECT TOP 5
                        Id, SyncProjectRunId, TaskType, Status, Priority,
                        ObjectsTotal, ObjectsProcessed, CreatedAt, StartedAt,
                        CompletedAt, DurationSeconds, ErrorMessage
                    FROM PostSyncTasks
                    WHERE Status = 'Pending'
                    ORDER BY Priority, CreatedAt";

                var pendingTasks = (await connection.QueryAsync<PostSyncTask>(selectPendingSql)).ToList();

                if (pendingTasks.Count == 0)
                {
                    return;  // No tasks to process
                }

                _logger.LogInformation("⚡ Processing {Count} pending post-sync tasks", pendingTasks.Count);

                foreach (var task in pendingTasks)
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        break;
                    }

                    await ProcessTaskAsync(task, cancellationToken);
                }

                return; // Success - exit retry loop
            }
            catch (SqlException sqlEx) when (sqlEx.Number == 208) // Invalid object name
            {
                // Schema was marked ready but table was dropped or schema changed - reset and wait
                _schemaReady = false;
                _logger.LogWarning("⚠️ PostSyncTaskService: Schema appears to have changed. Resetting schema check...");
                return;
            }
            catch (SqlException sqlEx) when (attempt < maxRetries)
            {
                // Transient SQL error - retry with exponential backoff
                _logger.LogWarning(sqlEx,
                    "⚠️ Database connection error (attempt {Attempt}/{MaxRetries}): {Message}. Retrying in {Delay}ms...",
                    attempt, maxRetries, sqlEx.Message, retryDelayMs);

                await Task.Delay(retryDelayMs, CancellationToken.None);
                retryDelayMs *= 2; // Exponential backoff
            }
            catch (SqlException sqlEx)
            {
                // Final attempt failed - log and continue service (will retry on next poll)
                _logger.LogError(sqlEx,
                    "❌ Database connection failed after {MaxRetries} attempts: {Message}. Service will continue and retry on next poll cycle.",
                    maxRetries, sqlEx.Message);
                return;
            }
        }
    }

    private async Task ProcessTaskAsync(PostSyncTask task, CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(GetConnectionString());
        await connection.OpenAsync(cancellationToken);

        // Re-fetch the task to ensure we have the latest state
        const string selectTaskSql = @"
            SELECT Id, SyncProjectRunId, TaskType, Status, Priority,
                   ObjectsTotal, ObjectsProcessed, CreatedAt, StartedAt,
                   CompletedAt, DurationSeconds, ErrorMessage
            FROM PostSyncTasks
            WHERE Id = @Id";

        var trackedTask = await connection.QueryFirstOrDefaultAsync<PostSyncTask>(selectTaskSql, new { task.Id });
        if (trackedTask == null)
        {
            _logger.LogError("❌ PostSyncTask {TaskId} not found when trying to process", task.Id);
            return;
        }

        try
        {
            // Mark task as running
            trackedTask.Status = "Running";
            trackedTask.StartedAt = DateTime.UtcNow;

            const string updateRunningStatusSql = @"
                UPDATE PostSyncTasks
                SET Status = @Status, StartedAt = @StartedAt
                WHERE Id = @Id";
            await connection.ExecuteAsync(updateRunningStatusSql, new
            {
                trackedTask.Status,
                trackedTask.StartedAt,
                trackedTask.Id
            });

            _logger.LogInformation("⚡ Processing {TaskType} task {TaskId} for run {RunId}",
                trackedTask.TaskType, trackedTask.Id, trackedTask.SyncProjectRunId);

            // Route to appropriate handler based on task type
            // NOTE: All relationship tasks now handled by IdentityLinkerJob background service
            // These are legacy task types - mark as completed immediately
            switch (trackedTask.TaskType)
            {
                case "ComputerOwnerAssignment":
                    await ProcessComputerOwnerAssignmentTaskAsync(connection, trackedTask, cancellationToken);
                    break;

                case "PersonMatching":
                case "IdentityManagerAssignment":
                case "ManagerAssignment":
                case "GroupOwnerAssignment":
                    // Legacy task types - mark as completed (now handled by IdentityLinkerJob)
                    _logger.LogInformation("⚠️ Legacy task type {TaskType} - marking as completed (handled by IdentityLinkerJob)",
                        trackedTask.TaskType);
                    trackedTask.Status = "Completed";
                    trackedTask.ObjectsProcessed = trackedTask.ObjectsTotal ?? 0;
                    break;

                default:
                    throw new InvalidOperationException($"Unknown task type: {trackedTask.TaskType}");
            }

            // Mark task as completed
            trackedTask.Status = "Completed";
            trackedTask.CompletedAt = DateTime.UtcNow;
            trackedTask.DurationSeconds = (int)(trackedTask.CompletedAt.Value - trackedTask.StartedAt!.Value).TotalSeconds;

            _logger.LogInformation("✅ Completed {TaskType} task {TaskId} - processed {Count} objects in {Duration}s",
                trackedTask.TaskType, trackedTask.Id, trackedTask.ObjectsProcessed, trackedTask.DurationSeconds);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Failed to process {TaskType} task {TaskId}: {Message}",
                trackedTask.TaskType, trackedTask.Id, ex.Message);

            trackedTask.Status = "Failed";
            trackedTask.ErrorMessage = ex.Message;
            trackedTask.CompletedAt = DateTime.UtcNow;
            trackedTask.DurationSeconds = trackedTask.StartedAt.HasValue
                ? (int)(trackedTask.CompletedAt.Value - trackedTask.StartedAt.Value).TotalSeconds
                : 0;
        }

        // Save final status
        const string updateFinalStatusSql = @"
            UPDATE PostSyncTasks
            SET Status = @Status,
                ObjectsTotal = @ObjectsTotal,
                ObjectsProcessed = @ObjectsProcessed,
                CompletedAt = @CompletedAt,
                DurationSeconds = @DurationSeconds,
                ErrorMessage = @ErrorMessage
            WHERE Id = @Id";

        await connection.ExecuteAsync(updateFinalStatusSql, new
        {
            trackedTask.Status,
            trackedTask.ObjectsTotal,
            trackedTask.ObjectsProcessed,
            trackedTask.CompletedAt,
            trackedTask.DurationSeconds,
            trackedTask.ErrorMessage,
            trackedTask.Id
        });
    }

    // NOTE: ProcessPersonMatchingTaskAsync removed - PersonMatching now handled by dedicated sync project types
    // Existing method kept for reference: ProcessManagerAssignmentTaskAsync handles object-level manager resolution

    /// <summary>
    /// Process ManagerAssignment task: Resolve manager relationships for all objects.
    /// This task should run AFTER PersonMatching completes (Priority=100).
    /// </summary>
    private async Task ProcessManagerAssignmentTaskAsync(
        SqlConnection connection,
        PostSyncTask task,
        CancellationToken cancellationToken)
    {
        using var scope = _serviceProvider.CreateScope();
        var syncRepository = scope.ServiceProvider.GetRequiredService<ISyncRelationshipRepository>();

        _logger.LogInformation("⚡ ManagerAssignment: Finding objects with manager attribute for run {RunId}",
            task.SyncProjectRunId);

        // Get all objects from this sync run with manager attribute
        var objectsNeedingManagers = await syncRepository.GetObjectsWithManagerAttributeAsync(
            task.SyncProjectRunId, cancellationToken);

        task.ObjectsTotal = objectsNeedingManagers.Count;

        const string updateTotalSql = "UPDATE PostSyncTasks SET ObjectsTotal = @ObjectsTotal WHERE Id = @Id";
        await connection.ExecuteAsync(updateTotalSql, new { task.ObjectsTotal, task.Id });

        _logger.LogInformation("⚡ ManagerAssignment: Found {Count} objects with manager attribute", objectsNeedingManagers.Count);

        foreach (var obj in objectsNeedingManagers)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                break;
            }

            try
            {
                // Get manager DN from attributes
                var managerDN = obj.Attributes.FirstOrDefault(a => a.AttributeName == "manager")?.AttributeValue;
                if (string.IsNullOrEmpty(managerDN))
                {
                    task.ObjectsProcessed++;
                    continue;
                }

                // 🔍 Find manager object by DN (not GUID!)
                var managerObjectWithAttrs = await syncRepository.FindObjectByDNAsync(
                    obj.Object.SourceConnectionId, managerDN, cancellationToken);

                if (managerObjectWithAttrs != null)
                {
                    // ✅ Update object with ManagerObjectId (foreign key to manager's Object record)
                    await syncRepository.UpdateObjectManagerIdAsync(
                        obj.Object.Id, managerObjectWithAttrs.Object.Id, cancellationToken);

                    _logger.LogDebug("⚡ ManagerAssignment: Set manager for {Object} -> {Manager}",
                        obj.Object.DisplayName, managerObjectWithAttrs.Object.DisplayName);
                }
                else
                {
                    _logger.LogDebug("⚠️ ManagerAssignment: Manager not found for {Object} (DN: {DN})",
                        obj.Object.DisplayName, managerDN);
                }

                task.ObjectsProcessed++;

                if (task.ObjectsProcessed % 100 == 0)
                {
                    const string updateProgressSql = "UPDATE PostSyncTasks SET ObjectsProcessed = @ObjectsProcessed WHERE Id = @Id";
                    await connection.ExecuteAsync(updateProgressSql, new { task.ObjectsProcessed, task.Id });

                    _logger.LogDebug("⚡ ManagerAssignment: Processed {Count}/{Total} objects",
                        task.ObjectsProcessed, task.ObjectsTotal);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "⚠️ ManagerAssignment: Failed for object {ObjectId}: {Message}",
                    obj.Object.Id, ex.Message);
            }
        }

        _logger.LogInformation("⚡ ManagerAssignment: Completed - processed {Count}/{Total} objects",
            task.ObjectsProcessed, task.ObjectsTotal);
    }

    /// <summary>
    /// Process GroupOwnerAssignment task: Resolve group owner (managedBy) relationships.
    /// </summary>
    private async Task ProcessGroupOwnerAssignmentTaskAsync(
        SqlConnection connection,
        PostSyncTask task,
        CancellationToken cancellationToken)
    {
        using var scope = _serviceProvider.CreateScope();
        var syncRepository = scope.ServiceProvider.GetRequiredService<ISyncRelationshipRepository>();

        _logger.LogInformation("⚡ GroupOwnerAssignment: Finding groups with managedBy attribute for run {RunId}",
            task.SyncProjectRunId);

        // Get all groups from this sync run with managedBy attribute
        var groupsNeedingOwners = await syncRepository.GetGroupsWithOwnerAttributeAsync(
            task.SyncProjectRunId, cancellationToken);

        task.ObjectsTotal = groupsNeedingOwners.Count;

        const string updateTotalSql = "UPDATE PostSyncTasks SET ObjectsTotal = @ObjectsTotal WHERE Id = @Id";
        await connection.ExecuteAsync(updateTotalSql, new { task.ObjectsTotal, task.Id });

        _logger.LogInformation("⚡ GroupOwnerAssignment: Found {Count} groups with managedBy attribute", groupsNeedingOwners.Count);

        foreach (var group in groupsNeedingOwners)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                break;
            }

            try
            {
                // Get owner DN from attributes (managedBy in AD)
                var ownerDN = group.Attributes.FirstOrDefault(a => a.AttributeName == "managedBy")?.AttributeValue;
                if (string.IsNullOrEmpty(ownerDN))
                {
                    task.ObjectsProcessed++;
                    continue;
                }

                // 🔍 Find owner object by DN (not GUID!)
                var ownerObjectWithAttrs = await syncRepository.FindObjectByDNAsync(
                    group.Group.SourceConnectionId, ownerDN, cancellationToken);

                if (ownerObjectWithAttrs != null && ownerObjectWithAttrs.Object.IdentityId.HasValue)
                {
                    // ✅ Update group with OwnerId (foreign key to owner's Identity/Person record)
                    await syncRepository.UpdateGroupOwnerIdAsync(
                        group.Group.Id, ownerObjectWithAttrs.Object.IdentityId.Value, cancellationToken);

                    _logger.LogDebug("⚡ GroupOwnerAssignment: Set owner for group {Group} -> {Owner}",
                        group.Group.Name, ownerObjectWithAttrs.Object.DisplayName);
                }
                else if (ownerObjectWithAttrs != null)
                {
                    _logger.LogDebug("⚠️ GroupOwnerAssignment: Owner object found but has no IdentityId yet for group {Group} (DN: {DN})",
                        group.Group.Name, ownerDN);
                }
                else
                {
                    _logger.LogDebug("⚠️ GroupOwnerAssignment: Owner not found for group {Group} (DN: {DN})",
                        group.Group.Name, ownerDN);
                }

                task.ObjectsProcessed++;

                if (task.ObjectsProcessed % 100 == 0)
                {
                    const string updateProgressSql = "UPDATE PostSyncTasks SET ObjectsProcessed = @ObjectsProcessed WHERE Id = @Id";
                    await connection.ExecuteAsync(updateProgressSql, new { task.ObjectsProcessed, task.Id });
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "⚠️ GroupOwnerAssignment: Failed for group {GroupId}: {Message}",
                    group.Group.Id, ex.Message);
            }
        }

        _logger.LogInformation("⚡ GroupOwnerAssignment: Completed - processed {Count}/{Total} groups",
            task.ObjectsProcessed, task.ObjectsTotal);
    }

    // NOTE: ProcessIdentityManagerAssignmentTaskAsync removed - Identity manager assignment
    // now handled by IdentityLinkerJob.AssignIdentityManagerIdsAsync() and future PersonMatch sync projects

    /// <summary>
    /// Process ComputerOwnerAssignment task: Resolve computer owner relationships.
    /// </summary>
    private async Task ProcessComputerOwnerAssignmentTaskAsync(
        SqlConnection connection,
        PostSyncTask task,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("⚡ ComputerOwnerAssignment: Task type not yet implemented");

        // TODO: Implement computer owner assignment logic
        // Similar to manager assignment but for computer objects

        task.ObjectsTotal = 0;
        task.ObjectsProcessed = 0;

        const string updateSql = @"
            UPDATE PostSyncTasks
            SET ObjectsTotal = @ObjectsTotal, ObjectsProcessed = @ObjectsProcessed
            WHERE Id = @Id";
        await connection.ExecuteAsync(updateSql, new { task.ObjectsTotal, task.ObjectsProcessed, task.Id });
    }
}
