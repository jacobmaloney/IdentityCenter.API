using System.Collections.Concurrent;
using System.Text.Json;
using Dapper;
using DataAccessLibrary.Models;
using DataAccessLibrary.Repositories;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DataAccessLibrary.Services;

/// <summary>
/// BackgroundService that runs on every execution server (primary and remote workers).
///
/// Implements the core poll-claim-execute loop for distributed job processing:
///   1. Waits for IExecutionServerContext.IsReady.
///   2. Polls the JobQueue table for claimed jobs (via IDistributedJobQueue).
///   3. Executes each job by routing to the correct IJobTypeHandler.
///   4. Reports progress and completion back to the database.
///   5. Supports cooperative cancellation via CancellationRequested flag checks.
///   6. Supports drain mode: finishes active jobs but stops claiming new ones.
///
/// On the primary server this runs alongside Quartz (which creates JobQueue rows).
/// On remote workers this is the only service — no web UI, no scheduler.
/// </summary>
public class ExecutionServerJobRunner : BackgroundService, IExecutionServerJobRunner
{
    // ── DI dependencies ──────────────────────────────────────────────────────
    private readonly IExecutionServerContext _context;
    private readonly IServiceProvider _serviceProvider;
    private readonly ExecutionServerOptions _options;
    private readonly ILogger<ExecutionServerJobRunner> _logger;
    private readonly IConfiguration _configuration;

    // ── Active job tracking ──────────────────────────────────────────────────
    // Key: JobId. Value: CTS for the job, the running Task, and the job type string.
    private readonly ConcurrentDictionary<Guid, (CancellationTokenSource Cts, Task Task, string JobType)> _activeJobs = new();

    // ── Counters (Interlocked for lock-free thread safety) ───────────────────
    private long _totalJobsProcessed;
    private long _totalJobsFailed;

    // ── Drain flag ───────────────────────────────────────────────────────────
    private volatile bool _isDraining;

    // ── IJobTypeHandler lookup (built once, reused) ──────────────────────────
    // Populated lazily on first poll; stays null until the service is ready.
    private Dictionary<string, IJobTypeHandler>? _handlers;

    // ── Connection string (resolved once from IConfiguration) ───────────────
    private string? _connectionString;

    // ============================================================================
    // Constructor
    // ============================================================================

    public ExecutionServerJobRunner(
        IExecutionServerContext executionServerContext,
        IServiceProvider serviceProvider,
        IOptions<ExecutionServerOptions> options,
        ILogger<ExecutionServerJobRunner> logger,
        IConfiguration configuration)
    {
        _context = executionServerContext ?? throw new ArgumentNullException(nameof(executionServerContext));
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
    }

    // ============================================================================
    // IExecutionServerJobRunner implementation
    // ============================================================================

    /// <inheritdoc/>
    public int ActiveJobCount => _activeJobs.Count;

    /// <inheritdoc/>
    public IReadOnlyList<Guid> ActiveJobIds => _activeJobs.Keys.ToList();

    /// <inheritdoc/>
    public long TotalJobsProcessed => Interlocked.Read(ref _totalJobsProcessed);

    /// <inheritdoc/>
    public long TotalJobsFailed => Interlocked.Read(ref _totalJobsFailed);

    /// <inheritdoc/>
    public bool IsDraining => _isDraining;

    /// <inheritdoc/>
    public void EnterDrainMode()
    {
        _isDraining = true;
        _logger.LogInformation("ExecutionServerJobRunner: drain mode entered — no new jobs will be claimed");
    }

    /// <inheritdoc/>
    public void ExitDrainMode()
    {
        _isDraining = false;
        _logger.LogInformation("ExecutionServerJobRunner: drain mode exited — resuming normal polling");
    }

    /// <inheritdoc/>
    public bool RequestJobCancellation(Guid jobId)
    {
        if (_activeJobs.TryGetValue(jobId, out var entry))
        {
            _logger.LogInformation(
                "ExecutionServerJobRunner: cancellation requested for job {JobId} (type: {JobType})",
                jobId, entry.JobType);
            entry.Cts.Cancel();
            return true;
        }
        return false;
    }

    /// <inheritdoc/>
    public async Task<bool> WaitForActiveJobsAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        var activeTasks = _activeJobs.Values.Select(e => e.Task).ToArray();
        if (activeTasks.Length == 0)
            return true;

        _logger.LogInformation(
            "ExecutionServerJobRunner: waiting for {Count} active job(s) to complete (timeout: {Timeout}s)",
            activeTasks.Length, timeout.TotalSeconds);

        var completionTask = Task.WhenAll(activeTasks);
        var timeoutTask = Task.Delay(timeout, cancellationToken);

        var finished = await Task.WhenAny(completionTask, timeoutTask).ConfigureAwait(false);
        return finished == completionTask;
    }

    // ============================================================================
    // BackgroundService: StopAsync override for graceful drain
    // ============================================================================

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("ExecutionServerJobRunner: StopAsync called — entering drain mode and waiting for active jobs");

        // Stop claiming new jobs immediately.
        EnterDrainMode();

        // Wait for in-flight jobs to complete (up to GracefulShutdownTimeout).
        var allCompleted = await WaitForActiveJobsAsync(_options.GracefulShutdownTimeout, cancellationToken)
            .ConfigureAwait(false);

        if (!allCompleted)
        {
            _logger.LogWarning(
                "ExecutionServerJobRunner: graceful shutdown timeout reached — {Count} job(s) still running",
                _activeJobs.Count);

            // Cancel all remaining jobs hard.
            foreach (var (_, entry) in _activeJobs)
            {
                try { entry.Cts.Cancel(); } catch { /* ignore */ }
            }
        }

        // Mark the server offline in the DB.
        try
        {
            await _context.MarkOfflineAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "ExecutionServerJobRunner: failed to mark server offline during shutdown");
        }

        await base.StopAsync(cancellationToken).ConfigureAwait(false);
    }

    // ============================================================================
    // BackgroundService: ExecuteAsync — main poll-claim-execute loop
    // ============================================================================

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogDebug("ExecutionServerJobRunner: starting");

        // ── Phase 1: wait for server context to be ready ─────────────────────
        while (!_context.IsReady && !stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken).ConfigureAwait(false);
        }

        if (stoppingToken.IsCancellationRequested)
        {
            _logger.LogInformation("ExecutionServerJobRunner: cancelled before server was ready — exiting");
            return;
        }

        _logger.LogInformation(
            "ExecutionServerJobRunner: execution server '{ServerName}' ({ServerId}) is ready — " +
            "starting poll loop (interval: {Interval}s, maxConcurrent: {Max})",
            _context.ServerName, _context.ServerId,
            _options.PollInterval.TotalSeconds, _context.MaxConcurrentJobs);

        // Build the handler map once (all IJobTypeHandler registrations in DI).
        BuildHandlerMap();

        // Track when we last checked for DB-side cancellation requests so we can
        // space it out independently of the poll interval.
        var lastCancelCheckUtc = DateTime.MinValue;

        // ── Phase 2: main loop ────────────────────────────────────────────────
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // ── Step A: clean up completed tasks ─────────────────────────
                PruneCompletedJobs();

                // ── Step B: claim new jobs (unless draining or at capacity) ──
                if (!_isDraining && _activeJobs.Count < _context.MaxConcurrentJobs)
                {
                    var availableSlots = _context.MaxConcurrentJobs - _activeJobs.Count;
                    var batchSize = Math.Min(availableSlots, _options.MaxClaimBatchSize);

                    await ClaimAndStartJobsAsync(batchSize, stoppingToken).ConfigureAwait(false);
                }
                else if (_isDraining)
                {
                    _logger.LogDebug("ExecutionServerJobRunner: draining — skipping job claim");
                }
                else
                {
                    _logger.LogDebug(
                        "ExecutionServerJobRunner: at capacity ({Count}/{Max}) — skipping job claim",
                        _activeJobs.Count, _context.MaxConcurrentJobs);
                }

                // ── Step C: check for DB-side cancellation requests ───────────
                var now = DateTime.UtcNow;
                if ((now - lastCancelCheckUtc) >= _options.CancellationCheckInterval)
                {
                    lastCancelCheckUtc = now;
                    await CheckDbCancellationsAsync(stoppingToken).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "ExecutionServerJobRunner: poll cycle error — will retry after interval");
            }

            // ── Wait before next poll ─────────────────────────────────────────
            try
            {
                await Task.Delay(_options.PollInterval, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        _logger.LogInformation("ExecutionServerJobRunner: poll loop exiting");
    }

    // ============================================================================
    // Private: poll helpers
    // ============================================================================

    /// <summary>
    /// Claims up to <paramref name="batchSize"/> jobs and starts each one
    /// on a background Task tracked in _activeJobs.
    /// </summary>
    private async Task ClaimAndStartJobsAsync(int batchSize, CancellationToken stoppingToken)
    {
        List<JobQueueEntry> claimed;
        try
        {
            await using var scope = _serviceProvider.CreateAsyncScope();
            var queue = scope.ServiceProvider.GetRequiredService<IDistributedJobQueue>();

            claimed = await queue.ClaimJobBatchAsync(
                _context.ServerId,
                _context.SupportedJobTypes,
                batchSize,
                stoppingToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "ExecutionServerJobRunner: failed to claim job batch");
            return;
        }

        if (claimed.Count == 0)
        {
            _logger.LogDebug("ExecutionServerJobRunner: no jobs available");
            return;
        }

        _logger.LogInformation(
            "ExecutionServerJobRunner: claimed {Count} job(s) — starting execution",
            claimed.Count);

        foreach (var job in claimed)
        {
            // Create a linked CTS so we can cancel individual jobs independently.
            var cts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);

            var task = Task.Run(() => ExecuteJobAsync(job, cts.Token), stoppingToken);

            _activeJobs[job.Id] = (cts, task, job.JobType);

            _logger.LogInformation(
                "ExecutionServerJobRunner: started job {JobId} (type: {JobType}, name: {JobName})",
                job.Id, job.JobType, job.JobName);
        }
    }

    /// <summary>
    /// Removes entries for jobs whose Task has already completed (any status).
    /// </summary>
    private void PruneCompletedJobs()
    {
        foreach (var (jobId, entry) in _activeJobs)
        {
            if (entry.Task.IsCompleted)
            {
                _activeJobs.TryRemove(jobId, out _);
                entry.Cts.Dispose();
            }
        }
    }

    /// <summary>
    /// Queries the DB for jobs claimed by this server that have CancellationRequested=1,
    /// then triggers the corresponding CancellationTokenSource in _activeJobs.
    /// </summary>
    private async Task CheckDbCancellationsAsync(CancellationToken stoppingToken)
    {
        if (_activeJobs.IsEmpty)
            return;

        try
        {
            await using var scope = _serviceProvider.CreateAsyncScope();
            var queue = scope.ServiceProvider.GetRequiredService<IDistributedJobQueue>();

            var cancelledIds = await queue.GetCancelledJobIdsForServerAsync(
                _context.ServerId, stoppingToken).ConfigureAwait(false);

            foreach (var jobId in cancelledIds)
            {
                if (_activeJobs.TryGetValue(jobId, out var entry))
                {
                    _logger.LogInformation(
                        "ExecutionServerJobRunner: DB cancellation flag detected for job {JobId} (type: {JobType}) — cancelling",
                        jobId, entry.JobType);
                    entry.Cts.Cancel();
                }
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "ExecutionServerJobRunner: failed to check DB-side cancellation requests");
        }
    }

    // ============================================================================
    // Private: job execution
    // ============================================================================

    /// <summary>
    /// Executes a single claimed job end-to-end:
    ///   1. Updates status to 'Processing'.
    ///   2. Resolves and calls the appropriate IJobTypeHandler.
    ///   3. Updates status to Completed / Failed / Cancelled.
    ///   4. Removes itself from _activeJobs.
    /// </summary>
    private async Task ExecuteJobAsync(JobQueueEntry job, CancellationToken ct)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        _logger.LogInformation(
            "ExecutionServerJobRunner: executing job {JobId} (type: {JobType})",
            job.Id, job.JobType);

        try
        {
            // ── Mark Processing ───────────────────────────────────────────────
            await UpdateJobStatusAsync(job.Id, "Processing", startedAt: DateTime.UtcNow)
                .ConfigureAwait(false);

            // ── Resolve handler and execute ───────────────────────────────────
            // TODO: Add TimeoutMs column to JobQueue table (V055 migration) and wrap
            // job execution in a timeout: using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            // if (job.TimeoutMs > 0) cts.CancelAfter(job.TimeoutMs);
            await using var scope = _serviceProvider.CreateAsyncScope();

            var handler = ResolveHandler(job.JobType, scope.ServiceProvider);

            await handler.ExecuteAsync(job, scope.ServiceProvider, ct).ConfigureAwait(false);

            // ── Success ───────────────────────────────────────────────────────
            stopwatch.Stop();
            Interlocked.Increment(ref _totalJobsProcessed);

            await CompleteJobSuccessAsync(job.Id, (int)stopwatch.ElapsedMilliseconds)
                .ConfigureAwait(false);

            _logger.LogInformation(
                "ExecutionServerJobRunner: job {JobId} completed successfully in {Ms}ms",
                job.Id, stopwatch.ElapsedMilliseconds);
        }
        catch (OperationCanceledException)
        {
            stopwatch.Stop();
            _logger.LogInformation(
                "ExecutionServerJobRunner: job {JobId} was cancelled after {Ms}ms",
                job.Id, stopwatch.ElapsedMilliseconds);

            await CompleteJobCancelledAsync(job.Id, (int)stopwatch.ElapsedMilliseconds)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            Interlocked.Increment(ref _totalJobsFailed);

            _logger.LogError(ex,
                "ExecutionServerJobRunner: job {JobId} (type: {JobType}) failed after {Ms}ms",
                job.Id, job.JobType, stopwatch.ElapsedMilliseconds);

            await CompleteJobFailedAsync(job.Id, ex, (int)stopwatch.ElapsedMilliseconds, job.RetryAttempt)
                .ConfigureAwait(false);
        }
        finally
        {
            // Remove from active jobs dictionary.
            _activeJobs.TryRemove(job.Id, out var removed);
            removed.Cts?.Dispose();
        }
    }

    // ============================================================================
    // Private: handler resolution
    // ============================================================================

    /// <summary>
    /// Resolves the handler for the given job type.
    /// Falls back to <see cref="DefaultJobTypeHandler"/> if no specific handler is registered.
    /// </summary>
    private IJobTypeHandler ResolveHandler(string jobType, IServiceProvider scopedProvider)
    {
        // Try the pre-built map first (covers handlers registered with known job types).
        if (_handlers is not null && _handlers.TryGetValue(jobType, out var mappedHandler))
            return mappedHandler;

        // Try resolving from scoped provider (allows per-scope handler registration).
        var scopedHandlers = scopedProvider.GetServices<IJobTypeHandler>();
        foreach (var h in scopedHandlers)
        {
            if (string.Equals(h.JobType, jobType, StringComparison.OrdinalIgnoreCase))
                return h;
        }

        // Fall back to the default stub handler.
        return new DefaultJobTypeHandler(jobType, _logger);
    }

    /// <summary>
    /// Builds the handler lookup dictionary from all singleton/scoped IJobTypeHandler
    /// registrations visible at startup time.
    /// </summary>
    private void BuildHandlerMap()
    {
        try
        {
            // Use a temporary scope to collect transient/scoped handlers.
            using var scope = _serviceProvider.CreateScope();
            var all = scope.ServiceProvider.GetServices<IJobTypeHandler>();

            _handlers = all.ToDictionary(
                h => h.JobType,
                h => h,
                StringComparer.OrdinalIgnoreCase);

            _logger.LogInformation(
                "ExecutionServerJobRunner: registered {Count} job type handler(s): {Types}",
                _handlers.Count,
                string.Join(", ", _handlers.Keys));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "ExecutionServerJobRunner: could not build handler map — will use default handler for all job types");
            _handlers = new Dictionary<string, IJobTypeHandler>(StringComparer.OrdinalIgnoreCase);
        }
    }

    // ============================================================================
    // Private: DB status updates (raw Dapper — avoids IDistributedJobQueue scope overhead)
    // ============================================================================

    /// <summary>
    /// Returns the DefaultConnection connection string, resolved once and cached.
    /// Uses the same approach as other Dapper repositories in the project.
    /// </summary>
    private string GetConnectionString()
    {
        if (_connectionString is not null)
            return _connectionString;

        _connectionString = _configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException(
                "ExecutionServerJobRunner: 'DefaultConnection' connection string is not configured.");

        return _connectionString;
    }

    private async Task UpdateJobStatusAsync(Guid jobId, string status, DateTime? startedAt = null)
    {
        try
        {
            var cs = GetConnectionString();
            await using var conn = new SqlConnection(cs);

            await conn.ExecuteAsync(
                "UPDATE JobQueue SET Status = @Status, StartedAt = @StartedAt WHERE Id = @Id",
                new { Status = status, StartedAt = startedAt, Id = jobId }).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "ExecutionServerJobRunner: failed to set job {JobId} status to {Status}", jobId, status);
        }
    }

    private async Task CompleteJobSuccessAsync(Guid jobId, int durationMs)
    {
        try
        {
            var cs = GetConnectionString();
            await using var conn = new SqlConnection(cs);

            await conn.ExecuteAsync(
                @"UPDATE JobQueue
                  SET Status      = 'Completed',
                      CompletedAt = GETUTCDATE(),
                      DurationMs  = @DurationMs,
                      ProgressPercent = 100
                  WHERE Id = @Id",
                new { Id = jobId, DurationMs = durationMs }).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "ExecutionServerJobRunner: failed to mark job {JobId} as Completed", jobId);
        }
    }

    private async Task CompleteJobFailedAsync(Guid jobId, Exception ex, int durationMs, int currentRetryAttempt)
    {
        try
        {
            var cs = GetConnectionString();
            await using var conn = new SqlConnection(cs);

            var errorMessage = ex.Message.Length > 2000 ? ex.Message[..2000] : ex.Message;
            var exceptionJson = SerializeException(ex);

            await conn.ExecuteAsync(
                @"UPDATE JobQueue
                  SET Status               = 'Failed',
                      CompletedAt          = GETUTCDATE(),
                      DurationMs           = @DurationMs,
                      ErrorMessage         = @ErrorMessage,
                      ExceptionDetailsJson = @ExceptionDetailsJson,
                      RetryAttempt         = @RetryAttempt
                  WHERE Id = @Id",
                new
                {
                    Id = jobId,
                    DurationMs = durationMs,
                    ErrorMessage = errorMessage,
                    ExceptionDetailsJson = exceptionJson,
                    RetryAttempt = currentRetryAttempt + 1
                }).ConfigureAwait(false);
        }
        catch (Exception innerEx)
        {
            _logger.LogWarning(innerEx, "ExecutionServerJobRunner: failed to mark job {JobId} as Failed", jobId);
        }
    }

    private async Task CompleteJobCancelledAsync(Guid jobId, int durationMs)
    {
        try
        {
            var cs = GetConnectionString();
            await using var conn = new SqlConnection(cs);

            await conn.ExecuteAsync(
                @"UPDATE JobQueue
                  SET Status      = 'Cancelled',
                      CompletedAt = GETUTCDATE(),
                      DurationMs  = @DurationMs
                  WHERE Id = @Id",
                new { Id = jobId, DurationMs = durationMs }).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "ExecutionServerJobRunner: failed to mark job {JobId} as Cancelled", jobId);
        }
    }

    // ============================================================================
    // Private: helpers
    // ============================================================================

    private static string SerializeException(Exception ex)
    {
        try
        {
            var obj = new
            {
                Type = ex.GetType().FullName,
                ex.Message,
                ex.StackTrace,
                Inner = ex.InnerException?.Message
            };
            return JsonSerializer.Serialize(obj);
        }
        catch
        {
            return "{}";
        }
    }
}

// ============================================================================
// Default (stub) handler — used when no IJobTypeHandler is registered for a job type
// ============================================================================

/// <summary>
/// Fallback handler that logs a warning and completes successfully (no-op).
/// Used when a job type arrives for which no concrete IJobTypeHandler is registered.
/// This prevents Unknown job types from cycling through Failed/Retry forever during
/// initial rollout before all handlers are implemented.
/// </summary>
internal sealed class DefaultJobTypeHandler : IJobTypeHandler
{
    private readonly ILogger _logger;

    public DefaultJobTypeHandler(string jobType, ILogger logger)
    {
        JobType = jobType;
        _logger = logger;
    }

    public string JobType { get; }

    public Task ExecuteAsync(JobQueueEntry job, IServiceProvider scopedProvider, CancellationToken ct)
    {
        _logger.LogWarning(
            "ExecutionServerJobRunner: no handler registered for job type '{JobType}' (jobId: {JobId}) — " +
            "job will be marked Completed as a no-op. Register an IJobTypeHandler implementation to handle this type.",
            JobType, job.Id);

        return Task.CompletedTask;
    }
}
