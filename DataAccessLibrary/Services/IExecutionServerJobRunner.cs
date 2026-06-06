namespace DataAccessLibrary.Services;

/// <summary>
/// BackgroundService that runs on every execution server (primary and remote workers).
/// Implements the core poll-claim-execute loop for distributed job processing.
///
/// On the primary server, this runs alongside the Quartz scheduler. Quartz handles
/// scheduling (creating JobQueue rows on cron triggers), while this service handles
/// the actual execution of those queued jobs.
///
/// On remote workers, this is the primary service -- there is no Quartz scheduler,
/// no web UI, just this background service polling for and executing jobs.
///
/// Lifecycle:
///   1. StartAsync: Waits for IExecutionServerContext to be initialized.
///   2. ExecuteAsync: Runs the poll-claim-execute loop until cancellation.
///   3. StopAsync: Enters drain mode, waits for active jobs to complete, marks server offline.
/// </summary>
public interface IExecutionServerJobRunner
{
    /// <summary>
    /// The number of jobs currently being executed by this server instance.
    /// Thread-safe; updated atomically as jobs start and complete.
    /// </summary>
    int ActiveJobCount { get; }

    /// <summary>
    /// The IDs of jobs currently being executed.
    /// Used for heartbeat reporting and orphan detection.
    /// </summary>
    IReadOnlyList<Guid> ActiveJobIds { get; }

    /// <summary>
    /// Total number of jobs processed since this server started.
    /// </summary>
    long TotalJobsProcessed { get; }

    /// <summary>
    /// Total number of jobs that failed since this server started.
    /// </summary>
    long TotalJobsFailed { get; }

    /// <summary>
    /// Whether the job runner is currently in drain mode
    /// (finishing active jobs but not claiming new ones).
    /// </summary>
    bool IsDraining { get; }

    /// <summary>
    /// Enters drain mode. The poll loop stops claiming new jobs.
    /// Active jobs continue to completion.
    /// </summary>
    void EnterDrainMode();

    /// <summary>
    /// Exits drain mode and resumes normal polling.
    /// </summary>
    void ExitDrainMode();

    /// <summary>
    /// Requests cancellation of a specific job running on this server.
    /// Triggers the CancellationToken associated with that job's execution.
    /// </summary>
    /// <param name="jobId">The job to cancel.</param>
    /// <returns>True if the job was found and cancellation was requested.</returns>
    bool RequestJobCancellation(Guid jobId);

    /// <summary>
    /// Waits for all active jobs to complete, with a timeout.
    /// Used during graceful shutdown.
    /// </summary>
    /// <param name="timeout">Maximum time to wait for jobs to complete.</param>
    /// <param name="cancellationToken">External cancellation token.</param>
    /// <returns>True if all jobs completed within the timeout.</returns>
    Task<bool> WaitForActiveJobsAsync(TimeSpan timeout, CancellationToken cancellationToken = default);
}

/// <summary>
/// Configuration for the execution server job runner.
/// Bound from appsettings.json section "ExecutionServer".
/// </summary>
public class ExecutionServerOptions
{
    public const string SectionName = "ExecutionServer";

    /// <summary>
    /// How often to poll the database for new jobs (default: 5 seconds).
    /// Shorter intervals mean faster job pickup but more database load.
    /// </summary>
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Maximum jobs to claim per poll cycle (default: 3).
    /// Set to the available capacity (MaxConcurrentJobs - ActiveJobCount).
    /// </summary>
    public int MaxClaimBatchSize { get; set; } = 3;

    /// <summary>
    /// How often to send heartbeats (default: 30 seconds).
    /// Must be less than the orphan detection threshold.
    /// </summary>
    public TimeSpan HeartbeatInterval { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// How long to wait for active jobs during graceful shutdown (default: 5 minutes).
    /// After this timeout, the server shuts down even if jobs are still running.
    /// </summary>
    public TimeSpan GracefulShutdownTimeout { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// How often to check for cancellation-requested jobs (default: 5 seconds).
    /// </summary>
    public TimeSpan CancellationCheckInterval { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// How often the primary server checks for orphaned jobs (default: 60 seconds).
    /// Only applies to the primary server.
    /// </summary>
    public TimeSpan OrphanDetectionInterval { get; set; } = TimeSpan.FromSeconds(60);

    /// <summary>
    /// Heartbeat timeout before a server is considered dead (default: 10 minutes).
    /// Must be significantly longer than HeartbeatInterval to avoid false positives.
    /// </summary>
    public int HeartbeatTimeoutMinutes { get; set; } = 10;
}
