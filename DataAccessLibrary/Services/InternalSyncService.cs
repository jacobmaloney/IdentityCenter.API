using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using Microsoft.Data.SqlClient;
using Dapper;
using DataAccessLibrary.Data;
using DataAccessLibrary.Models;
using DataAccessLibrary.Repositories;

namespace DataAccessLibrary.Services;

/// <summary>
/// Service for internal sync operations between Objects and Identities.
/// All operations use Dapper for high performance.
/// Designed for on-demand execution from InternalSyncCenter UI.
/// </summary>
public interface IInternalSyncService
{
    /// <summary>Get statistics for the Internal Sync Center dashboard</summary>
    Task<InternalSyncStats> GetStatsAsync(CancellationToken cancellationToken = default);

    /// <summary>Match Objects to existing Identities by the specified strategy</summary>
    Task<InternalSyncResult> RunObjectToIdentityMatchAsync(
        MatchingStrategy strategy,
        bool createNewIdentities,
        bool updateExistingIdentities,
        IProgress<InternalSyncProgress>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>Resolve manager relationships at Object and Identity level</summary>
    Task<InternalSyncResult> RunManagerResolutionAsync(
        IProgress<InternalSyncProgress>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>Run all internal sync operations in sequence</summary>
    Task<InternalSyncResult> RunAllAsync(
        MatchingStrategy strategy,
        bool createNewIdentities,
        bool updateExistingIdentities,
        IProgress<InternalSyncProgress>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>Execute an internal sync project by ID using its configured settings</summary>
    Task<InternalSyncResult> ExecuteProjectAsync(
        Guid projectId,
        IProgress<InternalSyncProgress>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Repair orphaned identities by linking them back to their source objects.
    /// Finds identities with no linked objects and attempts to match them by email, username, or name.
    /// </summary>
    Task<InternalSyncResult> RepairOrphanedIdentitiesAsync(
        IProgress<InternalSyncProgress>? progress = null,
        CancellationToken cancellationToken = default);
}

public class InternalSyncService : IInternalSyncService
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<InternalSyncService> _logger;
    private readonly IInternalSyncStepExecutor _stepExecutor;
    private readonly ISyncExecutionRepository _syncRepository;
    private readonly string _connectionString;

    public InternalSyncService(
        IConfiguration configuration,
        ILogger<InternalSyncService> logger,
        IInternalSyncStepExecutor stepExecutor,
        ISyncExecutionRepository syncRepository)
    {
        _configuration = configuration;
        _logger = logger;
        _stepExecutor = stepExecutor;
        _syncRepository = syncRepository;
        _connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("No database connection string configured");
    }

    public async Task<InternalSyncStats> GetStatsAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        var stats = new InternalSyncStats();

        // Get counts using a single round-trip
        const string sql = @"
            SELECT
                (SELECT COUNT(*) FROM Objects WHERE IdentityId IS NULL AND ObjectClass = 'user') AS UnmatchedObjects,
                (SELECT COUNT(*) FROM Objects WHERE IdentityId IS NOT NULL) AS MatchedObjects,
                (SELECT COUNT(*) FROM Identities) AS TotalIdentities,
                (SELECT COUNT(*) FROM Objects WHERE ManagerSourceId IS NOT NULL AND ManagerObjectId IS NULL) AS UnresolvedManagerObjects,
                (SELECT COUNT(*) FROM Identities WHERE ManagerIdentityId IS NULL AND EXISTS (
                    SELECT 1 FROM Objects o WHERE o.IdentityId = Identities.Id AND o.ManagerObjectId IS NOT NULL
                )) AS UnresolvedManagerIdentities";

        var result = await connection.QuerySingleAsync<dynamic>(sql);

        stats.UnmatchedObjects = (int)result.UnmatchedObjects;
        stats.MatchedObjects = (int)result.MatchedObjects;
        stats.TotalIdentities = (int)result.TotalIdentities;
        stats.UnresolvedManagerObjects = (int)result.UnresolvedManagerObjects;
        stats.UnresolvedManagerIdentities = (int)result.UnresolvedManagerIdentities;

        // Get last run info using Dapper
        const string lastRunSql = @"
            SELECT TOP 1 Id, OperationType, StartedAt, CompletedAt, Matched, Created,
                   TotalProcessed, Skipped, Status, ErrorMessage
            FROM InternalSyncRuns
            ORDER BY CompletedAt DESC";

        var lastRun = await connection.QueryFirstOrDefaultAsync<InternalSyncRun>(lastRunSql);

        if (lastRun != null)
        {
            stats.LastRunAt = lastRun.CompletedAt;
            stats.LastRunMatched = lastRun.Matched;
            stats.LastRunCreated = lastRun.Created;
        }

        return stats;
    }

    public async Task<InternalSyncResult> RunObjectToIdentityMatchAsync(
        MatchingStrategy strategy,
        bool createNewIdentities,
        bool updateExistingIdentities,
        IProgress<InternalSyncProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var result = new InternalSyncResult { Operation = "ObjectToIdentityMatch" };
        var startTime = DateTime.UtcNow;

        _logger.LogInformation("Starting Object→Identity matching with strategy: {Strategy}, CreateNew: {Create}, Update: {Update}",
            strategy, createNewIdentities, updateExistingIdentities);

        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        try
        {
            // Get unmatched user objects
            const string getUnmatchedSql = @"
                SELECT Id, Email, Username, FirstName, LastName, DisplayName,
                       EmployeeId, Department, JobTitle, Phone, DN, ObjectClass,
                       SourceConnectionId, SourceUniqueId
                FROM Objects
                WHERE IdentityId IS NULL
                  AND ObjectClass = 'user'
                ORDER BY CreatedAt";

            var unmatchedObjects = (await connection.QueryAsync<ObjectDto>(getUnmatchedSql)).ToList();
            result.Total = unmatchedObjects.Count;

            if (result.Total == 0)
            {
                _logger.LogInformation("No unmatched objects found");
                result.Success = true;
                return result;
            }

            progress?.Report(new InternalSyncProgress
            {
                Phase = "Matching",
                Message = $"Processing {result.Total} unmatched objects...",
                Processed = 0,
                Total = result.Total
            });

            // Process in batches of 100
            var batchSize = 100;
            var processed = 0;

            foreach (var batch in unmatchedObjects.Chunk(batchSize))
            {
                cancellationToken.ThrowIfCancellationRequested();

                foreach (var obj in batch)
                {
                    var matchResult = await TryMatchObjectToIdentityAsync(connection, obj, strategy);

                    if (matchResult.IdentityId.HasValue)
                    {
                        // Link object to identity
                        await LinkObjectToIdentityAsync(connection, obj.Id, matchResult.IdentityId.Value);
                        result.Matched++;

                        if (updateExistingIdentities)
                        {
                            await UpdateIdentityFromObjectAsync(connection, matchResult.IdentityId.Value, obj);
                            result.Updated++;
                        }
                    }
                    else if (createNewIdentities)
                    {
                        // Create new identity from object
                        var newIdentityId = await CreateIdentityFromObjectAsync(connection, obj);
                        await LinkObjectToIdentityAsync(connection, obj.Id, newIdentityId);
                        result.Created++;
                    }
                    else
                    {
                        result.Skipped++;
                    }

                    processed++;
                }

                progress?.Report(new InternalSyncProgress
                {
                    Phase = "Matching",
                    Message = $"Processed {processed}/{result.Total}",
                    Processed = processed,
                    Total = result.Total,
                    Matched = result.Matched,
                    Created = result.Created,
                    Skipped = result.Skipped
                });
            }

            result.Success = true;
            result.Duration = DateTime.UtcNow - startTime;

            // Log the run
            await LogRunAsync(result, cancellationToken);

            _logger.LogInformation("Object→Identity matching completed: Matched={Matched}, Created={Created}, Skipped={Skipped}",
                result.Matched, result.Created, result.Skipped);
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.ErrorMessage = ex.Message;
            _logger.LogError(ex, "Object→Identity matching failed");
        }

        return result;
    }

    private async Task<(Guid? IdentityId, string? MatchMethod, int Confidence)> TryMatchObjectToIdentityAsync(
        SqlConnection connection, ObjectDto obj, MatchingStrategy strategy)
    {
        // Try matching strategies in order of confidence
        switch (strategy)
        {
            case MatchingStrategy.Email:
                return await TryMatchByEmailAsync(connection, obj);

            case MatchingStrategy.Username:
                return await TryMatchByUsernameAsync(connection, obj);

            case MatchingStrategy.EmployeeId:
                return await TryMatchByEmployeeIdAsync(connection, obj);

            case MatchingStrategy.Composite:
            default:
                // Try all strategies in order of confidence
                var emailMatch = await TryMatchByEmailAsync(connection, obj);
                if (emailMatch.IdentityId.HasValue) return emailMatch;

                var usernameMatch = await TryMatchByUsernameAsync(connection, obj);
                if (usernameMatch.IdentityId.HasValue) return usernameMatch;

                var empIdMatch = await TryMatchByEmployeeIdAsync(connection, obj);
                if (empIdMatch.IdentityId.HasValue) return empIdMatch;

                // Try name match as last resort
                return await TryMatchByNameAsync(connection, obj);
        }
    }

    private async Task<(Guid? IdentityId, string? MatchMethod, int Confidence)> TryMatchByEmailAsync(
        SqlConnection connection, ObjectDto obj)
    {
        if (string.IsNullOrWhiteSpace(obj.Email)) return (null, null, 0);

        const string sql = "SELECT Id FROM Identities WHERE LOWER(PrimaryEmail) = LOWER(@Email)";
        var identityId = await connection.QueryFirstOrDefaultAsync<Guid?>(sql, new { obj.Email });

        return identityId.HasValue ? (identityId, "Email", 95) : (null, null, 0);
    }

    private async Task<(Guid? IdentityId, string? MatchMethod, int Confidence)> TryMatchByUsernameAsync(
        SqlConnection connection, ObjectDto obj)
    {
        if (string.IsNullOrWhiteSpace(obj.Username)) return (null, null, 0);

        const string sql = "SELECT Id FROM Identities WHERE LOWER(Username) = LOWER(@Username)";
        var identityId = await connection.QueryFirstOrDefaultAsync<Guid?>(sql, new { obj.Username });

        return identityId.HasValue ? (identityId, "Username", 90) : (null, null, 0);
    }

    private async Task<(Guid? IdentityId, string? MatchMethod, int Confidence)> TryMatchByEmployeeIdAsync(
        SqlConnection connection, ObjectDto obj)
    {
        if (string.IsNullOrWhiteSpace(obj.EmployeeId)) return (null, null, 0);

        const string sql = "SELECT Id FROM Identities WHERE EmployeeId = @EmployeeId";
        var identityId = await connection.QueryFirstOrDefaultAsync<Guid?>(sql, new { obj.EmployeeId });

        return identityId.HasValue ? (identityId, "EmployeeId", 92) : (null, null, 0);
    }

    private async Task<(Guid? IdentityId, string? MatchMethod, int Confidence)> TryMatchByNameAsync(
        SqlConnection connection, ObjectDto obj)
    {
        if (string.IsNullOrWhiteSpace(obj.FirstName) || string.IsNullOrWhiteSpace(obj.LastName))
            return (null, null, 0);

        const string sql = @"
            SELECT Id FROM Identities
            WHERE LOWER(FirstName) = LOWER(@FirstName)
              AND LOWER(LastName) = LOWER(@LastName)
              AND (Department IS NULL OR Department = @Department OR @Department IS NULL)";

        var identityId = await connection.QueryFirstOrDefaultAsync<Guid?>(sql, new
        {
            obj.FirstName,
            obj.LastName,
            obj.Department
        });

        return identityId.HasValue ? (identityId, "Name", 75) : (null, null, 0);
    }

    private async Task LinkObjectToIdentityAsync(SqlConnection connection, Guid objectId, Guid identityId)
    {
        const string sql = @"
            UPDATE Objects
            SET IdentityId = @IdentityId, ModifiedAt = GETUTCDATE()
            WHERE Id = @ObjectId";

        await connection.ExecuteAsync(sql, new { ObjectId = objectId, IdentityId = identityId });
    }

    private async Task<Guid> CreateIdentityFromObjectAsync(SqlConnection connection, ObjectDto obj)
    {
        var newId = Guid.NewGuid();

        const string sql = @"
            INSERT INTO Identities (Id, Email, Username, FirstName, LastName, DisplayName,
                                    EmployeeId, Department, JobTitle, Phone, Status, CreatedAt, ModifiedAt)
            VALUES (@Id, @Email, @Username, @FirstName, @LastName, @DisplayName,
                    @EmployeeId, @Department, @JobTitle, @Phone, 'Active', GETUTCDATE(), GETUTCDATE())";

        await connection.ExecuteAsync(sql, new
        {
            Id = newId,
            obj.Email,
            obj.Username,
            obj.FirstName,
            obj.LastName,
            DisplayName = obj.DisplayName ?? $"{obj.FirstName} {obj.LastName}".Trim(),
            obj.EmployeeId,
            obj.Department,
            obj.JobTitle,
            obj.Phone
        });

        return newId;
    }

    private async Task UpdateIdentityFromObjectAsync(SqlConnection connection, Guid identityId, ObjectDto obj)
    {
        const string sql = @"
            UPDATE Identities
            SET Email = COALESCE(@Email, Email),
                Username = COALESCE(@Username, Username),
                FirstName = COALESCE(@FirstName, FirstName),
                LastName = COALESCE(@LastName, LastName),
                DisplayName = COALESCE(@DisplayName, DisplayName),
                Department = COALESCE(@Department, Department),
                JobTitle = COALESCE(@JobTitle, JobTitle),
                Phone = COALESCE(@Phone, Phone),
                ModifiedAt = GETUTCDATE()
            WHERE Id = @IdentityId";

        await connection.ExecuteAsync(sql, new
        {
            IdentityId = identityId,
            obj.Email,
            obj.Username,
            obj.FirstName,
            obj.LastName,
            obj.DisplayName,
            obj.Department,
            obj.JobTitle,
            obj.Phone
        });
    }

    public async Task<InternalSyncResult> RunManagerResolutionAsync(
        IProgress<InternalSyncProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var result = new InternalSyncResult { Operation = "ManagerResolution" };
        var startTime = DateTime.UtcNow;

        _logger.LogInformation("Starting manager resolution");

        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        try
        {
            progress?.Report(new InternalSyncProgress
            {
                Phase = "ManagerResolution",
                Message = "Resolving Object manager IDs..."
            });

            // Step 1: Resolve ManagerObjectId from ManagerSourceId (DN)
            const string resolveManagerObjectIdSql = @"
                UPDATE o
                SET o.ManagerObjectId = manager.Id,
                    o.ModifiedAt = GETUTCDATE()
                FROM Objects o
                INNER JOIN Objects manager ON manager.DN = o.ManagerSourceId
                    AND manager.SourceConnectionId = o.SourceConnectionId
                WHERE o.ManagerSourceId IS NOT NULL
                  AND o.ManagerObjectId IS NULL
                  AND manager.Id IS NOT NULL";

            var objectManagersResolved = await connection.ExecuteAsync(resolveManagerObjectIdSql);
            result.Matched += objectManagersResolved;

            progress?.Report(new InternalSyncProgress
            {
                Phase = "ManagerResolution",
                Message = $"Resolved {objectManagersResolved} object managers. Resolving Identity managers...",
                Processed = objectManagersResolved
            });

            // Step 2: Assign ManagerIdentityId from ManagerObjectId
            const string assignIdentityManagerSql = @"
                UPDATE i
                SET i.ManagerIdentityId = managerIdentity.Id,
                    i.ModifiedAt = GETUTCDATE()
                FROM Identities i
                INNER JOIN Objects authObject ON authObject.IdentityId = i.Id AND authObject.IsAuthoritative = 1
                INNER JOIN Objects managerObject ON managerObject.Id = authObject.ManagerObjectId
                INNER JOIN Identities managerIdentity ON managerIdentity.Id = managerObject.IdentityId
                WHERE i.ManagerIdentityId IS NULL
                  AND managerIdentity.Id IS NOT NULL";

            var identityManagersAssigned = await connection.ExecuteAsync(assignIdentityManagerSql);
            result.Updated += identityManagersAssigned;

            progress?.Report(new InternalSyncProgress
            {
                Phase = "ManagerResolution",
                Message = $"Assigned {identityManagersAssigned} identity managers. Clearing orphans...",
                Processed = objectManagersResolved + identityManagersAssigned
            });

            // Step 3: Clear orphaned manager assignments
            const string clearOrphansSql = @"
                UPDATE i
                SET i.ManagerIdentityId = NULL,
                    i.ModifiedAt = GETUTCDATE()
                FROM Identities i
                INNER JOIN Objects authObject ON authObject.IdentityId = i.Id AND authObject.IsAuthoritative = 1
                WHERE i.ManagerIdentityId IS NOT NULL
                  AND authObject.ManagerObjectId IS NULL";

            var orphansCleared = await connection.ExecuteAsync(clearOrphansSql);
            result.Skipped = orphansCleared;

            result.Total = objectManagersResolved + identityManagersAssigned + orphansCleared;
            result.Success = true;
            result.Duration = DateTime.UtcNow - startTime;

            progress?.Report(new InternalSyncProgress
            {
                Phase = "ManagerResolution",
                Message = $"Complete: {objectManagersResolved} object managers, {identityManagersAssigned} identity managers, {orphansCleared} orphans cleared",
                Processed = result.Total,
                Total = result.Total,
                Complete = true
            });

            _logger.LogInformation("Manager resolution completed: ObjectManagers={ObjMgr}, IdentityManagers={IdMgr}, OrphansCleared={Orphans}",
                objectManagersResolved, identityManagersAssigned, orphansCleared);
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.ErrorMessage = ex.Message;
            _logger.LogError(ex, "Manager resolution failed");
        }

        return result;
    }

    public async Task<InternalSyncResult> RunAllAsync(
        MatchingStrategy strategy,
        bool createNewIdentities,
        bool updateExistingIdentities,
        IProgress<InternalSyncProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var combinedResult = new InternalSyncResult { Operation = "RunAll" };
        var startTime = DateTime.UtcNow;

        _logger.LogInformation("Starting full internal sync cycle");

        try
        {
            // Phase 1: Object to Identity matching
            progress?.Report(new InternalSyncProgress { Phase = "Matching", Message = "Phase 1/2: Object→Identity Matching" });
            var matchResult = await RunObjectToIdentityMatchAsync(strategy, createNewIdentities, updateExistingIdentities, progress, cancellationToken);
            combinedResult.Matched += matchResult.Matched;
            combinedResult.Created += matchResult.Created;
            combinedResult.Updated += matchResult.Updated;
            combinedResult.Skipped += matchResult.Skipped;

            if (!matchResult.Success)
            {
                combinedResult.Success = false;
                combinedResult.ErrorMessage = $"Matching failed: {matchResult.ErrorMessage}";
                return combinedResult;
            }

            // Phase 2: Manager resolution
            progress?.Report(new InternalSyncProgress { Phase = "ManagerResolution", Message = "Phase 2/2: Manager Resolution" });
            var managerResult = await RunManagerResolutionAsync(progress, cancellationToken);
            combinedResult.Matched += managerResult.Matched;
            combinedResult.Updated += managerResult.Updated;

            if (!managerResult.Success)
            {
                combinedResult.Success = false;
                combinedResult.ErrorMessage = $"Manager resolution failed: {managerResult.ErrorMessage}";
                return combinedResult;
            }

            combinedResult.Success = true;
            combinedResult.Duration = DateTime.UtcNow - startTime;
            combinedResult.Total = combinedResult.Matched + combinedResult.Created + combinedResult.Updated + combinedResult.Skipped;

            progress?.Report(new InternalSyncProgress
            {
                Phase = "Complete",
                Message = $"All phases complete in {combinedResult.Duration?.TotalSeconds ?? 0:F1}s",
                Complete = true
            });

            _logger.LogInformation("Full internal sync completed in {Duration}ms: Matched={Matched}, Created={Created}, Updated={Updated}",
                combinedResult.Duration?.TotalMilliseconds ?? 0, combinedResult.Matched, combinedResult.Created, combinedResult.Updated);
        }
        catch (Exception ex)
        {
            combinedResult.Success = false;
            combinedResult.ErrorMessage = ex.Message;
            combinedResult.Duration = DateTime.UtcNow - startTime;
            _logger.LogError(ex, "Full internal sync failed");
        }

        return combinedResult;
    }

    private async Task LogRunAsync(InternalSyncResult result, CancellationToken cancellationToken)
    {
        try
        {
            await using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);

            var run = new InternalSyncRun
            {
                Id = Guid.NewGuid(),
                OperationType = result.Operation,
                StartedAt = DateTime.UtcNow - (result.Duration ?? TimeSpan.Zero),
                CompletedAt = DateTime.UtcNow,
                Matched = result.Matched,
                Created = result.Created,
                TotalProcessed = result.Total,
                Skipped = result.Skipped,
                Status = result.Success ? "Completed" : "Failed",
                ErrorMessage = result.ErrorMessage
            };

            const string insertSql = @"
                INSERT INTO InternalSyncRuns (Id, OperationType, StartedAt, CompletedAt, Matched, Created,
                                              TotalProcessed, Skipped, Status, ErrorMessage)
                VALUES (@Id, @OperationType, @StartedAt, @CompletedAt, @Matched, @Created,
                        @TotalProcessed, @Skipped, @Status, @ErrorMessage)";

            await connection.ExecuteAsync(insertSql, run);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to log internal sync run");
        }
    }

    public async Task<InternalSyncResult> ExecuteProjectAsync(
        Guid projectId,
        IProgress<InternalSyncProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var result = new InternalSyncResult { Operation = "ExecuteProject" };
        var startTime = DateTime.UtcNow;
        SyncProjectRun? run = null;

        try
        {
            // Load project from database
            await using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);

            var project = await connection.QuerySingleOrDefaultAsync<ProjectDto>(
                @"SELECT Id, Name, ProjectType, IdentityMatchingStrategy, IsEnabled
                  FROM SyncProjects WHERE Id = @ProjectId",
                new { ProjectId = projectId });

            if (project == null)
            {
                result.Success = false;
                result.ErrorMessage = $"Project {projectId} not found";
                return result;
            }

            if (!project.IsEnabled)
            {
                result.Success = false;
                result.ErrorMessage = $"Project '{project.Name}' is disabled";
                return result;
            }

            // Check if project has configured steps
            var steps = await LoadProjectStepsAsync(connection, projectId);
            var totalSteps = steps.Count(s => s.IsEnabled);

            _logger.LogInformation("Executing internal project '{Name}' ({Type}) with {Steps} steps",
                project.Name, project.ProjectType, totalSteps);

            // ============================================================
            // CRITICAL: Create run record BEFORE execution (like external syncs)
            // This enables "View Progress" button and run tracking
            // ============================================================
            run = new SyncProjectRun
            {
                Id = Guid.NewGuid(),
                SyncProjectId = projectId,
                TriggerType = "Manual",
                StartedAt = startTime,
                Status = "Running",
                TotalSteps = totalSteps,
                CompletedSteps = 0,
                ProgressPercentage = 0,
                CurrentStep = "Initializing..."
            };

            await _syncRepository.CreateSyncProjectRunAsync(run, cancellationToken);
            await _syncRepository.UpdateRunProgressAsync(run.Id, currentStepName: "Initializing...", cancellationToken: cancellationToken);

            // Mark project as running
            const string updateProjectRunningSql = @"
                UPDATE SyncProjects
                SET IsRunning = 1, LastRunAt = @LastRunAt
                WHERE Id = @ProjectId";
            await connection.ExecuteAsync(updateProjectRunningSql, new { ProjectId = projectId, LastRunAt = startTime });

            _logger.LogInformation("Created run {RunId} for project '{Name}'", run.Id, project.Name);

            try
            {
                if (steps.Any())
                {
                    // Step-based execution with run tracking
                    result = await ExecuteStepBasedProjectWithRunAsync(
                        connection, project, steps, run, progress, cancellationToken);
                }
                else
                {
                    // LEGACY: Fallback to old behavior for projects without steps
                    result = await ExecuteLegacyProjectAsync(
                        project, progress, cancellationToken);
                }

                result.Duration = DateTime.UtcNow - startTime;

                // Update run with final results
                run.Status = result.Success ? "Completed" : "Failed";
                run.CompletedAt = DateTime.UtcNow;
                run.TotalObjectsProcessed = result.Total;
                run.TotalObjectsCreated = result.Created;
                run.TotalObjectsUpdated = result.Updated;
                run.TotalObjectsDeleted = 0;
                run.TotalPersonsCreated = result.Created;
                run.TotalErrors = run.FailedSteps > 0 ? run.FailedSteps : 0;
                run.ProgressPercentage = 100;
                run.DurationSeconds = (int)(result.Duration?.TotalSeconds ?? 0);
                run.ErrorMessage = result.ErrorMessage;

                await _syncRepository.UpdateSyncProjectRunStatusAsync(
                    run.Id, run.Status, run.CompletedAt, run.DurationSeconds, run.ErrorMessage,
                    run.TotalObjectsProcessed, run.TotalObjectsCreated, run.TotalObjectsUpdated,
                    run.TotalObjectsDeleted, run.TotalPersonsCreated, run.TotalErrors,
                    run.CompletedSteps, run.ProgressPercentage, cancellationToken);

                progress?.Report(new InternalSyncProgress
                {
                    Phase = "Complete",
                    Message = $"Completed: {result.Matched} matched, {result.Created} created, {result.Skipped} skipped",
                    Matched = result.Matched,
                    Created = result.Created,
                    Skipped = result.Skipped,
                    Complete = true
                });

                _logger.LogInformation("Internal project '{Name}' completed in {Duration}ms: Matched={Matched}, Created={Created}",
                    project.Name, result.Duration?.TotalMilliseconds ?? 0, result.Matched, result.Created);
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.ErrorMessage = ex.Message;
                result.Duration = DateTime.UtcNow - startTime;

                // Update run with error
                run.Status = "Failed";
                run.CompletedAt = DateTime.UtcNow;
                run.ErrorMessage = ex.Message;
                run.DurationSeconds = (int)(result.Duration?.TotalSeconds ?? 0);

                const string updateRunFailedSql = @"
                    UPDATE SyncProjectRuns
                    SET Status = @Status, CompletedAt = @CompletedAt, ErrorMessage = @ErrorMessage, DurationSeconds = @DurationSeconds
                    WHERE Id = @Id";
                await connection.ExecuteAsync(updateRunFailedSql, run);

                _logger.LogError(ex, "Internal project {ProjectId} failed", projectId);
            }
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.ErrorMessage = ex.Message;
            result.Duration = DateTime.UtcNow - startTime;
            _logger.LogError(ex, "Internal project {ProjectId} failed during setup", projectId);
        }
        finally
        {
            // ALWAYS reset IsRunning flag and save run status
            try
            {
                await using var connection = new SqlConnection(_connectionString);
                await connection.OpenAsync(cancellationToken);

                const string updateProjectFinalSql = @"
                    UPDATE SyncProjects
                    SET IsRunning = 0,
                        TotalExecutions = TotalExecutions + 1,
                        SuccessfulExecutions = SuccessfulExecutions + @SuccessIncrement
                    WHERE Id = @ProjectId";
                await connection.ExecuteAsync(updateProjectFinalSql, new { ProjectId = projectId, SuccessIncrement = result.Success ? 1 : 0 });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to reset project running state");
            }
        }

        return result;
    }

    /// <summary>
    /// Execute step-based project with run tracking for progress display.
    /// </summary>
    private async Task<InternalSyncResult> ExecuteStepBasedProjectWithRunAsync(
        SqlConnection connection,
        ProjectDto project,
        List<InternalSyncStep> steps,
        SyncProjectRun run,
        IProgress<InternalSyncProgress>? progress,
        CancellationToken cancellationToken)
    {
        var result = new InternalSyncResult { Operation = "ExecuteProject", Success = true };
        var completedSteps = 0;
        var enabledSteps = steps.Where(s => s.IsEnabled).OrderBy(s => s.ExecutionOrder).ToList();

        foreach (var step in enabledSteps)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Create SyncStepRun record for this step
            // Note: SyncStepId is null for internal sync (uses InternalSyncSteps table, not SyncSteps)
            var stepRun = new SyncStepRun
            {
                Id = Guid.NewGuid(),
                SyncProjectRunId = run.Id,
                SyncStepId = null, // Internal sync uses InternalSyncSteps, not SyncSteps
                StepName = step.Name,
                ObjectClass = step.ObjectClassFilter ?? "user",
                StartedAt = DateTime.UtcNow,
                Status = "Running"
            };

            await _syncRepository.CreateSyncStepRunAsync(stepRun, cancellationToken);

            // Update run progress
            run.CurrentStep = step.Name;
            run.ProgressPercentage = enabledSteps.Count > 0
                ? (completedSteps * 100) / enabledSteps.Count
                : 0;

            await _syncRepository.UpdateRunProgressAsync(run.Id,
                progressPercentage: run.ProgressPercentage,
                currentStepName: run.CurrentStep,
                cancellationToken: cancellationToken);

            progress?.Report(new InternalSyncProgress
            {
                Phase = step.Name,
                Message = $"Executing step {completedSteps + 1} of {enabledSteps.Count}: {step.Name}",
                Total = enabledSteps.Count,
                Processed = completedSteps
            });

            try
            {
                var stepStartTime = DateTime.UtcNow;
                var stepResult = await _stepExecutor.ExecuteStepAsync(
                    step, connection, stepRun.Id, progress, cancellationToken);

                result.Matched += stepResult.Matched;
                result.Created += stepResult.Created;
                result.Updated += stepResult.Updated;
                result.Skipped += stepResult.Skipped;
                result.Total += stepResult.Processed;

                completedSteps++;
                run.CompletedSteps = completedSteps;

                // Update step run with results
                stepRun.CompletedAt = DateTime.UtcNow;
                stepRun.Status = stepResult.Success ? "Completed" : "Failed";
                stepRun.ObjectsQueried = stepResult.Found;  // Source count (Found)
                stepRun.ObjectsProcessed = stepResult.Processed;
                stepRun.PersonsMatched = stepResult.Matched;
                stepRun.ObjectsCreated = stepResult.Created;
                stepRun.PersonsCreated = stepResult.Created;
                stepRun.ObjectsUpdated = stepResult.Updated;
                stepRun.ObjectsSkipped = stepResult.Skipped;
                stepRun.ErrorCount = stepResult.Errors;
                stepRun.DurationSeconds = (int)(stepRun.CompletedAt.Value - stepStartTime).TotalSeconds;
                stepRun.ErrorMessage = stepResult.ErrorMessage;

                await _syncRepository.UpdateStepRunMetricsAsync(
                    stepRun.Id, stepRun.ObjectsQueried, stepRun.ObjectsProcessed,
                    stepRun.ObjectsCreated, stepRun.ObjectsUpdated, stepRun.ObjectsSkipped,
                    stepRun.ErrorCount, cancellationToken,
                    status: stepRun.Status, completedAt: stepRun.CompletedAt,
                    durationSeconds: stepRun.DurationSeconds);

                await _syncRepository.UpdateStepRunPersonMetricsAsync(
                    stepRun.Id, stepRun.PersonsCreated, stepRun.PersonsMatched, cancellationToken);

                // Persist audit logs from the step
                if (stepResult.AuditLogs.Count > 0)
                {
                    foreach (var log in stepResult.AuditLogs)
                    {
                        log.SyncStepRunId = stepRun.Id;
                        if (log.Id == Guid.Empty) log.Id = Guid.NewGuid();
                    }

                    const string insertAuditSql = @"
                        INSERT INTO SyncAuditLogs (Id, SyncStepRunId, ObjectId, OperationType,
                            ObjectDisplayName, SourceUniqueId, Email, Username, UserPrincipalName,
                            ChangeDetails, ChangeCount, ErrorMessage, ProcessingTimeMs, Timestamp)
                        VALUES (@Id, @SyncStepRunId, @ObjectId, @OperationType,
                            @ObjectDisplayName, @SourceUniqueId, @Email, @Username, @UserPrincipalName,
                            @ChangeDetails, @ChangeCount, @ErrorMessage, @ProcessingTimeMs, @Timestamp)";

                    foreach (var batch in stepResult.AuditLogs.Chunk(500))
                    {
                        await connection.ExecuteAsync(insertAuditSql, batch, commandTimeout: 120);
                    }

                    _logger.LogInformation("Persisted {Count} audit log entries for step '{StepName}'",
                        stepResult.AuditLogs.Count, step.Name);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Step '{StepName}' failed", step.Name);

                // Update step run with failure
                stepRun.CompletedAt = DateTime.UtcNow;
                stepRun.Status = "Failed";
                stepRun.ErrorMessage = ex.Message;
                stepRun.DurationSeconds = (int)(stepRun.CompletedAt.Value - stepRun.StartedAt).TotalSeconds;

                const string updateStepRunFailedSql = @"
                    UPDATE SyncStepRuns
                    SET CompletedAt = @CompletedAt, Status = @Status, ErrorMessage = @ErrorMessage, DurationSeconds = @DurationSeconds
                    WHERE Id = @Id";
                await connection.ExecuteAsync(updateStepRunFailedSql, stepRun);

                if (!step.ContinueOnError)
                {
                    result.Success = false;
                    result.ErrorMessage = $"Step '{step.Name}' failed: {ex.Message}";
                    run.FailedSteps++;

                    const string updateRunFailedStepsSql = @"
                        UPDATE SyncProjectRuns SET FailedSteps = @FailedSteps WHERE Id = @Id";
                    await connection.ExecuteAsync(updateRunFailedStepsSql, new { run.FailedSteps, run.Id });

                    break;
                }
                run.FailedSteps++;

                const string updateRunFailedSteps2Sql = @"
                    UPDATE SyncProjectRuns SET FailedSteps = @FailedSteps WHERE Id = @Id";
                await connection.ExecuteAsync(updateRunFailedSteps2Sql, new { run.FailedSteps, run.Id });
            }
        }

        return result;
    }

    /// <summary>
    /// Load configured steps for a project.
    /// </summary>
    private async Task<List<InternalSyncStep>> LoadProjectStepsAsync(
        SqlConnection connection, Guid projectId)
    {
        const string sql = @"
            SELECT s.Id, s.SyncProjectId, s.Name, s.Description, s.ExecutionOrder,
                   s.Direction, s.StepType, s.ObjectClassFilter, s.IsEnabled,
                   s.ContinueOnError, s.Configuration, s.SourceConnectionId, s.TagFilter
            FROM InternalSyncSteps s
            WHERE s.SyncProjectId = @ProjectId AND s.IsEnabled = 1
            ORDER BY s.ExecutionOrder";

        var steps = (await connection.QueryAsync<InternalSyncStep>(sql, new { ProjectId = projectId })).ToList();

        // Load mappings for each step
        foreach (var step in steps)
        {
            const string mappingSql = @"
                SELECT Id, InternalSyncStepId, SourceField, TargetField,
                       OverwriteExisting, IsRequired, DefaultValue, Transformation,
                       MappingOrder, IsEnabled
                FROM InternalSyncStepMappings
                WHERE InternalSyncStepId = @StepId AND IsEnabled = 1
                ORDER BY MappingOrder";

            var mappings = (await connection.QueryAsync<InternalSyncStepMapping>(mappingSql, new { StepId = step.Id })).ToList();
            step.Mappings = mappings;
        }

        return steps;
    }

    /// <summary>
    /// Execute project using configured steps.
    /// </summary>
    private async Task<InternalSyncResult> ExecuteStepBasedProjectAsync(
        SqlConnection connection,
        ProjectDto project,
        List<InternalSyncStep> steps,
        IProgress<InternalSyncProgress>? progress,
        CancellationToken cancellationToken)
    {
        var result = new InternalSyncResult { Operation = "ExecuteProject", Success = true };

        _logger.LogInformation("Executing step-based project '{Name}' with {StepCount} steps",
            project.Name, steps.Count);

        progress?.Report(new InternalSyncProgress
        {
            Phase = "Starting",
            Message = $"Starting {project.Name} with {steps.Count} steps"
        });

        var stepIndex = 0;
        foreach (var step in steps)
        {
            cancellationToken.ThrowIfCancellationRequested();
            stepIndex++;

            progress?.Report(new InternalSyncProgress
            {
                Phase = step.StepType,
                Message = $"Step {stepIndex}/{steps.Count}: {step.Name}"
            });

            _logger.LogDebug("Executing step {StepIndex}/{StepCount}: '{StepName}' ({StepType})",
                stepIndex, steps.Count, step.Name, step.StepType);

            var stepResult = await _stepExecutor.ExecuteStepAsync(step, connection, null, progress, cancellationToken);

            // Aggregate results
            result.Matched += stepResult.Matched;
            result.Created += stepResult.Created;
            result.Updated += stepResult.Updated;
            result.Skipped += stepResult.Skipped;
            result.Total += stepResult.Processed;

            if (!stepResult.Success)
            {
                _logger.LogWarning("Step '{StepName}' failed: {Error}", step.Name, stepResult.ErrorMessage);

                if (!step.ContinueOnError)
                {
                    result.Success = false;
                    result.ErrorMessage = $"Step '{step.Name}' failed: {stepResult.ErrorMessage}";
                    break;
                }
            }
        }

        return result;
    }

    /// <summary>
    /// Execute project using legacy hardcoded logic (backward compatibility).
    /// </summary>
    private async Task<InternalSyncResult> ExecuteLegacyProjectAsync(
        ProjectDto project,
        IProgress<InternalSyncProgress>? progress,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Executing legacy project '{Name}' (no steps configured)", project.Name);

        // Parse matching strategy
        var strategy = project.IdentityMatchingStrategy?.ToLower() switch
        {
            "email" => MatchingStrategy.Email,
            "username" => MatchingStrategy.Username,
            "employeeid" => MatchingStrategy.EmployeeId,
            "upn" => MatchingStrategy.Email,
            "composite" => MatchingStrategy.Composite,
            _ => MatchingStrategy.Email
        };

        // Determine behavior based on project type
        bool createNewIdentities = project.ProjectType == "PersonCreate";
        bool updateExistingIdentities = true;

        progress?.Report(new InternalSyncProgress
        {
            Phase = "Starting",
            Message = $"Starting legacy {project.ProjectType}: {project.Name}"
        });

        // Run the matching operation
        return await RunObjectToIdentityMatchAsync(
            strategy,
            createNewIdentities,
            updateExistingIdentities,
            progress,
            cancellationToken);
    }

    /// <summary>
    /// Log run with project reference.
    /// </summary>
    private async Task LogRunWithProjectAsync(Guid projectId, InternalSyncResult result, CancellationToken cancellationToken)
    {
        try
        {
            await using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);

            // Use SyncProjectRun (same as external syncs) for unified history view
            var run = new SyncProjectRun
            {
                Id = Guid.NewGuid(),
                SyncProjectId = projectId,
                TriggerType = "Manual",
                StartedAt = DateTime.UtcNow - (result.Duration ?? TimeSpan.Zero),
                CompletedAt = DateTime.UtcNow,
                Status = result.Success ? "Completed" : "Failed",
                TotalObjectsProcessed = result.Total,
                TotalObjectsUpdated = result.Updated,
                TotalPersonsCreated = result.Created,
                TotalErrors = 0,
                ErrorMessage = result.ErrorMessage,
                DurationSeconds = (int?)(result.Duration?.TotalSeconds),
                // Store internal sync specifics in the execution log
                ExecutionLog = System.Text.Json.JsonSerializer.Serialize(new
                {
                    Operation = result.Operation,
                    Matched = result.Matched,
                    Created = result.Created,
                    Skipped = result.Skipped,
                    Duration = result.Duration?.TotalMilliseconds
                })
            };

            const string insertRunSql = @"
                INSERT INTO SyncProjectRuns (Id, SyncProjectId, TriggerType, StartedAt, CompletedAt, Status,
                                            TotalSteps, CompletedSteps, FailedSteps, SkippedSteps,
                                            TotalObjectsProcessed, TotalObjectsCreated, TotalObjectsUpdated, TotalObjectsDeleted,
                                            TotalErrors, TotalPersonsCreated, ProgressPercentage,
                                            ErrorMessage, DurationSeconds, ExecutionLog)
                VALUES (@Id, @SyncProjectId, @TriggerType, @StartedAt, @CompletedAt, @Status,
                        @TotalSteps, @CompletedSteps, @FailedSteps, @SkippedSteps,
                        @TotalObjectsProcessed, @TotalObjectsCreated, @TotalObjectsUpdated, @TotalObjectsDeleted,
                        @TotalErrors, @TotalPersonsCreated, @ProgressPercentage,
                        @ErrorMessage, @DurationSeconds, @ExecutionLog)";
            await connection.ExecuteAsync(insertRunSql, run);

            // Update project stats
            await UpdateProjectStatsAsync(projectId, result.Success, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to log internal sync run");
        }
    }

    private async Task UpdateProjectStatsAsync(Guid projectId, bool success, CancellationToken cancellationToken)
    {
        try
        {
            await using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);

            const string updateSql = @"
                UPDATE SyncProjects
                SET LastRunAt = @LastRunAt,
                    TotalExecutions = TotalExecutions + 1,
                    SuccessfulExecutions = SuccessfulExecutions + @SuccessIncrement
                WHERE Id = @ProjectId";

            await connection.ExecuteAsync(updateSql, new
            {
                ProjectId = projectId,
                LastRunAt = DateTime.UtcNow,
                SuccessIncrement = success ? 1 : 0
            });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to update project stats");
        }
    }

    // DTO for project data
    private class ProjectDto
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? ProjectType { get; set; }
        public string? IdentityMatchingStrategy { get; set; }
        public bool IsEnabled { get; set; }
    }

    // DTO for object data
    private class ObjectDto
    {
        public Guid Id { get; set; }
        public string? Email { get; set; }
        public string? Username { get; set; }
        public string? FirstName { get; set; }
        public string? LastName { get; set; }
        public string? DisplayName { get; set; }
        public string? EmployeeId { get; set; }
        public string? Department { get; set; }
        public string? JobTitle { get; set; }
        public string? Phone { get; set; }
        public string? DN { get; set; }
        public string? ObjectClass { get; set; }
        public Guid SourceConnectionId { get; set; }
        public string? SourceUniqueId { get; set; }
    }

    /// <summary>
    /// Repair orphaned identities by linking them back to their source objects.
    /// </summary>
    public async Task<InternalSyncResult> RepairOrphanedIdentitiesAsync(
        IProgress<InternalSyncProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var result = new InternalSyncResult
        {
            Operation = "RepairOrphanedIdentities",
            Success = true
        };

        var startTime = DateTime.UtcNow;

        try
        {
            await using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);

            progress?.Report(new InternalSyncProgress
            {
                Phase = "Repair",
                Message = "Finding orphaned identities..."
            });

            // Find identities that have no linked objects
            const string findOrphansSql = @"
                SELECT i.Id, i.PrimaryEmail, i.Username, i.FirstName, i.LastName, i.DisplayName
                FROM Identities i
                WHERE i.IsActive = 1
                  AND NOT EXISTS (SELECT 1 FROM Objects o WHERE o.IdentityId = i.Id)";

            var orphanedIdentities = (await connection.QueryAsync<OrphanedIdentityDto>(findOrphansSql)).ToList();

            result.Total = orphanedIdentities.Count;

            if (orphanedIdentities.Count == 0)
            {
                _logger.LogInformation("No orphaned identities found");
                progress?.Report(new InternalSyncProgress
                {
                    Phase = "Repair",
                    Message = "No orphaned identities found",
                    Complete = true
                });
                result.Duration = DateTime.UtcNow - startTime;
                return result;
            }

            _logger.LogInformation("Found {Count} orphaned identities to repair", orphanedIdentities.Count);

            progress?.Report(new InternalSyncProgress
            {
                Phase = "Repair",
                Message = $"Repairing {orphanedIdentities.Count} orphaned identities...",
                Total = orphanedIdentities.Count
            });

            var repaired = 0;
            var skipped = 0;

            foreach (var identity in orphanedIdentities)
            {
                cancellationToken.ThrowIfCancellationRequested();

                // Try to find a matching unlinked object
                Guid? matchedObjectId = null;

                // Strategy 1: Match by email (highest confidence)
                if (!string.IsNullOrWhiteSpace(identity.PrimaryEmail))
                {
                    matchedObjectId = await connection.QueryFirstOrDefaultAsync<Guid?>(
                        @"SELECT TOP 1 Id FROM Objects
                          WHERE IdentityId IS NULL
                            AND LOWER(Email) = LOWER(@Email)",
                        new { Email = identity.PrimaryEmail });
                }

                // Strategy 2: Match by username
                if (!matchedObjectId.HasValue && !string.IsNullOrWhiteSpace(identity.Username))
                {
                    matchedObjectId = await connection.QueryFirstOrDefaultAsync<Guid?>(
                        @"SELECT TOP 1 Id FROM Objects
                          WHERE IdentityId IS NULL
                            AND LOWER(Username) = LOWER(@Username)",
                        new { identity.Username });
                }

                // Strategy 3: Match by first name + last name
                if (!matchedObjectId.HasValue &&
                    !string.IsNullOrWhiteSpace(identity.FirstName) &&
                    !string.IsNullOrWhiteSpace(identity.LastName))
                {
                    matchedObjectId = await connection.QueryFirstOrDefaultAsync<Guid?>(
                        @"SELECT TOP 1 Id FROM Objects
                          WHERE IdentityId IS NULL
                            AND LOWER(FirstName) = LOWER(@FirstName)
                            AND LOWER(LastName) = LOWER(@LastName)",
                        new { identity.FirstName, identity.LastName });
                }

                if (matchedObjectId.HasValue)
                {
                    // Link the object to the identity
                    await connection.ExecuteAsync(
                        @"UPDATE Objects
                          SET IdentityId = @IdentityId,
                              MatchConfidence = 90,
                              MatchMethod = 'Repaired',
                              LastSeenAt = GETUTCDATE()
                          WHERE Id = @ObjectId",
                        new { IdentityId = identity.Id, ObjectId = matchedObjectId.Value });

                    repaired++;
                    _logger.LogInformation("Repaired identity {IdentityId} - linked to object {ObjectId}",
                        identity.Id, matchedObjectId.Value);
                }
                else
                {
                    skipped++;
                    _logger.LogDebug("Could not find matching object for identity {IdentityId} ({Email})",
                        identity.Id, identity.PrimaryEmail ?? identity.DisplayName);
                }

                if ((repaired + skipped) % 10 == 0)
                {
                    progress?.Report(new InternalSyncProgress
                    {
                        Phase = "Repair",
                        Message = $"Repaired {repaired}, skipped {skipped}...",
                        Processed = repaired + skipped,
                        Total = orphanedIdentities.Count,
                        Matched = repaired,
                        Skipped = skipped
                    });
                }
            }

            result.Matched = repaired;
            result.Skipped = skipped;
            result.Duration = DateTime.UtcNow - startTime;

            _logger.LogInformation("Repair completed: {Repaired} linked, {Skipped} skipped",
                repaired, skipped);

            progress?.Report(new InternalSyncProgress
            {
                Phase = "Repair",
                Message = $"Repair complete: {repaired} linked, {skipped} could not be matched",
                Processed = orphanedIdentities.Count,
                Total = orphanedIdentities.Count,
                Matched = repaired,
                Skipped = skipped,
                Complete = true
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to repair orphaned identities");
            result.Success = false;
            result.ErrorMessage = ex.Message;
            result.Duration = DateTime.UtcNow - startTime;
        }

        return result;
    }

    private class OrphanedIdentityDto
    {
        public Guid Id { get; set; }
        public string? PrimaryEmail { get; set; }
        public string? Username { get; set; }
        public string? FirstName { get; set; }
        public string? LastName { get; set; }
        public string? DisplayName { get; set; }
    }
}

/// <summary>Matching strategy for Object to Identity matching</summary>
public enum MatchingStrategy
{
    /// <summary>Match by email address (highest confidence)</summary>
    Email,
    /// <summary>Match by username/sAMAccountName</summary>
    Username,
    /// <summary>Match by employee ID</summary>
    EmployeeId,
    /// <summary>Try all strategies in order of confidence</summary>
    Composite
}

/// <summary>Statistics for Internal Sync Center dashboard</summary>
public class InternalSyncStats
{
    public int UnmatchedObjects { get; set; }
    public int MatchedObjects { get; set; }
    public int TotalIdentities { get; set; }
    public int UnresolvedManagerObjects { get; set; }
    public int UnresolvedManagerIdentities { get; set; }
    public DateTime? LastRunAt { get; set; }
    public int LastRunMatched { get; set; }
    public int LastRunCreated { get; set; }
}

/// <summary>Result of an internal sync operation</summary>
public class InternalSyncResult
{
    public string Operation { get; set; } = string.Empty;
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    public int Total { get; set; }
    public int Matched { get; set; }
    public int Created { get; set; }
    public int Updated { get; set; }
    public int Skipped { get; set; }
    public TimeSpan? Duration { get; set; }
}

/// <summary>Progress report for internal sync operations</summary>
public class InternalSyncProgress
{
    public string Phase { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public int Processed { get; set; }
    public int Total { get; set; }
    public int Matched { get; set; }
    public int Created { get; set; }
    public int Skipped { get; set; }
    public bool Complete { get; set; }
    public double PercentComplete => Total > 0 ? (double)Processed / Total * 100 : 0;
}
