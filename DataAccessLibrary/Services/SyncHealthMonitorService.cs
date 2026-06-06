using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using Microsoft.Data.SqlClient;
using Dapper;

namespace DataAccessLibrary.Services;

/// <summary>
/// Background service that monitors sync health and automatically recovers from hung/orphaned syncs.
/// - Cleans up orphaned IsRunning flags on startup
/// - Detects and auto-cancels syncs stuck for > X minutes
/// - Provides self-healing for the sync system
/// </summary>
public class SyncHealthMonitorService : BackgroundService
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<SyncHealthMonitorService> _logger;

    // Configuration - staggered to avoid connection pool stampede on startup
    private const int STARTUP_DELAY_SECONDS = 60;
    private const int CHECK_INTERVAL_SECONDS = 60;
    private const int STEP_STUCK_THRESHOLD_MINUTES = 10; // Step-level stuck detection (increased for large syncs)

    public SyncHealthMonitorService(
        IConfiguration configuration,
        ILogger<SyncHealthMonitorService> logger)
    {
        _configuration = configuration;
        _logger = logger;
    }

    private string GetConnectionString()
    {
        return _configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Wait briefly for app to fully start
        await Task.Delay(TimeSpan.FromSeconds(STARTUP_DELAY_SECONDS), stoppingToken);

        // Check if database exists before proceeding
        try
        {
            var connectionString = GetConnectionString();
            await using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync(stoppingToken);
        }
        catch
        {
            _logger.LogDebug("Database not ready - skipping sync health monitor");
            return;
        }

        try
        {
            // 1. STARTUP CLEANUP: Fix any orphaned syncs from previous crashes
            await CleanupOrphanedSyncsAsync(stoppingToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during startup sync cleanup");
        }

        // 2. CONTINUOUS MONITORING: Check for stuck syncs periodically
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(CHECK_INTERVAL_SECONDS), stoppingToken);
                await DetectAndRecoverStuckSyncsAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in sync health monitor loop");
            }
        }
    }

    /// <summary>
    /// Cleans up any syncs marked as "Running" that were orphaned by a previous crash.
    /// Called once on startup.
    /// </summary>
    private async Task CleanupOrphanedSyncsAsync(CancellationToken ct)
    {
        var connectionString = GetConnectionString();
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(ct);

        // Find projects stuck in IsRunning state
        var orphanedProjects = await connection.QueryAsync<(Guid Id, string Name, DateTime? LastRunAt)>(@"
            SELECT Id, Name, LastRunAt
            FROM SyncProjects
            WHERE IsRunning = 1", commandTimeout: 30);

        var orphanedList = orphanedProjects.ToList();
        if (!orphanedList.Any())
        {
            _logger.LogDebug("No orphaned sync projects found on startup");
            return;
        }

        _logger.LogWarning("STARTUP CLEANUP: Found {Count} orphaned sync projects marked as running", orphanedList.Count);

        foreach (var project in orphanedList)
        {
            _logger.LogWarning("  - Cleaning up: {Name} (LastRun: {LastRun})",
                project.Name, project.LastRunAt?.ToString("g") ?? "Never");

            // Mark any running runs as failed
            await connection.ExecuteAsync(@"
                UPDATE SyncProjectRuns
                SET Status = 'Failed',
                    CompletedAt = GETUTCDATE(),
                    ErrorMessage = 'Sync was interrupted by application restart. Cleaned up automatically.'
                WHERE SyncProjectId = @ProjectId AND Status = 'Running'",
                new { ProjectId = project.Id }, commandTimeout: 30);

            // Mark any running step runs as failed
            await connection.ExecuteAsync(@"
                UPDATE ssr
                SET ssr.Status = 'Failed',
                    ssr.CompletedAt = GETUTCDATE(),
                    ssr.ErrorMessage = 'Sync was interrupted by application restart.'
                FROM SyncStepRuns ssr
                JOIN SyncProjectRuns spr ON ssr.SyncProjectRunId = spr.Id
                WHERE spr.SyncProjectId = @ProjectId AND ssr.Status = 'Running'",
                new { ProjectId = project.Id }, commandTimeout: 30);

            // Release the project lock
            await connection.ExecuteAsync(@"
                UPDATE SyncProjects SET IsRunning = 0 WHERE Id = @ProjectId",
                new { ProjectId = project.Id }, commandTimeout: 30);
        }

        _logger.LogInformation("Startup cleanup complete: {Count} orphaned syncs recovered", orphanedList.Count);
    }

    /// <summary>
    /// Detects syncs that are stuck (no progress for X minutes) and marks them as failed.
    /// Called periodically.
    /// DISABLED: Was killing legitimate syncs. Re-enable when fixed.
    /// </summary>
    private async Task DetectAndRecoverStuckSyncsAsync(CancellationToken ct)
    {
        // DISABLED - was killing legitimate syncs
        return;

        var connectionString = GetConnectionString();
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(ct);

        // Find step runs that have been running too long with no progress
        var stuckSteps = await connection.QueryAsync<StuckStepInfo>(@"
            SELECT
                ssr.Id AS StepRunId,
                ssr.StepName,
                ssr.StartedAt,
                ssr.ObjectsProcessed,
                ssr.ObjectsQueried,
                spr.Id AS RunId,
                spr.SyncProjectId AS ProjectId,
                sp.Name AS ProjectName,
                DATEDIFF(MINUTE, ssr.StartedAt, GETUTCDATE()) AS MinutesRunning
            FROM SyncStepRuns ssr
            JOIN SyncProjectRuns spr ON ssr.SyncProjectRunId = spr.Id
            JOIN SyncProjects sp ON spr.SyncProjectId = sp.Id
            WHERE ssr.Status = 'Running'
              AND DATEDIFF(MINUTE, ssr.StartedAt, GETUTCDATE()) > @ThresholdMinutes",
            new { ThresholdMinutes = STEP_STUCK_THRESHOLD_MINUTES }, commandTimeout: 30);

        var stuckList = stuckSteps.ToList();
        if (!stuckList.Any()) return;

        foreach (var stuck in stuckList)
        {
            // Check if this step has made ANY progress
            // ObjectsQueried == 0 means no data returned yet
            // ObjectsQueried == -1 means step is processing local data (sentinel value, don't flag as stuck)
            if (stuck.ObjectsQueried == 0 && stuck.ObjectsProcessed == 0)
            {
                _logger.LogWarning(
                    "STUCK SYNC DETECTED: Project '{Project}' step '{Step}' running for {Minutes}min with 0 objects queried. Auto-recovering.",
                    stuck.ProjectName, stuck.StepName, stuck.MinutesRunning);

                // Mark step as failed
                await connection.ExecuteAsync(@"
                    UPDATE SyncStepRuns
                    SET Status = 'Failed',
                        CompletedAt = GETUTCDATE(),
                        ErrorMessage = 'Auto-cancelled: Step stuck for ' + CAST(@Minutes AS VARCHAR) + ' minutes with no progress. Check logs for details.'
                    WHERE Id = @StepRunId",
                    new { StepRunId = stuck.StepRunId, Minutes = stuck.MinutesRunning }, commandTimeout: 30);

                // Mark run as failed
                await connection.ExecuteAsync(@"
                    UPDATE SyncProjectRuns
                    SET Status = 'Failed',
                        CompletedAt = GETUTCDATE(),
                        ErrorMessage = 'Auto-cancelled: Sync stuck for ' + CAST(@Minutes AS VARCHAR) + ' minutes. Step ''' + @StepName + ''' had no progress.'
                    WHERE Id = @RunId AND Status = 'Running'",
                    new { RunId = stuck.RunId, Minutes = stuck.MinutesRunning, StepName = stuck.StepName }, commandTimeout: 30);

                // Release project lock
                await connection.ExecuteAsync(@"
                    UPDATE SyncProjects SET IsRunning = 0 WHERE Id = @ProjectId",
                    new { ProjectId = stuck.ProjectId }, commandTimeout: 30);

                _logger.LogInformation("Auto-recovered stuck sync: {Project}", stuck.ProjectName);
            }
            else
            {
                // Step has made some progress - might be processing a large dataset
                _logger.LogDebug(
                    "Sync '{Project}' step '{Step}' running for {Minutes}min but has progress (Queried:{Queried}, Processed:{Processed})",
                    stuck.ProjectName, stuck.StepName, stuck.MinutesRunning, stuck.ObjectsQueried, stuck.ObjectsProcessed);
            }
        }
    }

    private class StuckStepInfo
    {
        public Guid StepRunId { get; set; }
        public string StepName { get; set; } = "";
        public DateTime StartedAt { get; set; }
        public int ObjectsProcessed { get; set; }
        public int ObjectsQueried { get; set; }
        public Guid RunId { get; set; }
        public Guid ProjectId { get; set; }
        public string ProjectName { get; set; } = "";
        public int MinutesRunning { get; set; }
    }
}
