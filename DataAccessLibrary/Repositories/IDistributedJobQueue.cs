using DataAccessLibrary.Models;

namespace DataAccessLibrary.Repositories;

/// <summary>
/// Enhanced job queue interface for distributed execution.
/// Extends the existing IJobQueueRepository with batch operations,
/// server-aware claiming, cooperative cancellation, and job reassignment.
///
/// The existing IJobQueueRepository methods remain functional and are used
/// by the current codebase. This interface adds the distributed-aware
/// operations that the execution server infrastructure requires.
/// </summary>
public interface IDistributedJobQueue : IJobQueueRepository
{
    // ========================================================================
    // BATCH OPERATIONS
    // ========================================================================

    /// <summary>
    /// Enqueues a batch of jobs in a single database round trip using a table-valued
    /// parameter. Supports millions of rows per call.
    ///
    /// Used by bulk operations like "re-evaluate all policies" or "sync all projects"
    /// where thousands of individual jobs need to be queued simultaneously.
    /// </summary>
    /// <param name="jobs">The batch of jobs to enqueue.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The number of jobs successfully enqueued.</returns>
    Task<int> EnqueueBatchAsync(IReadOnlyList<JobQueueEntry> jobs, CancellationToken cancellationToken = default);

    // ========================================================================
    // DISTRIBUTED CLAIMING
    // ========================================================================

    /// <summary>
    /// Claims up to N jobs for a specific execution server in a single atomic operation.
    /// Uses the usp_ClaimJobsForServer stored procedure with ROWLOCK + READPAST hints
    /// for deadlock-free concurrent claiming.
    ///
    /// Routing logic (handled by the stored procedure):
    ///   1. Jobs with TargetServerId matching this server are claimed first (server affinity).
    ///   2. Jobs with no TargetServerId are claimed if the server supports the job type.
    ///   3. Jobs targeted to a different server are never claimed.
    /// </summary>
    /// <param name="serverId">The execution server claiming the jobs.</param>
    /// <param name="supportedJobTypes">Job types this server can handle.</param>
    /// <param name="maxJobs">Maximum number of jobs to claim in this batch.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of claimed job entries (may be empty if no jobs available).</returns>
    Task<List<JobQueueEntry>> ClaimJobBatchAsync(
        Guid serverId,
        IReadOnlyList<string> supportedJobTypes,
        int maxJobs = 5,
        CancellationToken cancellationToken = default);

    // ========================================================================
    // COOPERATIVE CANCELLATION
    // ========================================================================

    /// <summary>
    /// Requests cancellation of a running job. Sets CancellationRequested=1 on the
    /// JobQueue row. The executing server must periodically check this flag and
    /// stop processing gracefully.
    ///
    /// This is cooperative cancellation, not preemptive. The job will continue
    /// running until the next cancellation checkpoint in the executing code.
    ///
    /// If the job is still Pending (not yet claimed), it is immediately set to Cancelled.
    /// If the job is Claimed or Processing, the cancellation flag is set.
    /// If the job is already completed, no action is taken.
    /// </summary>
    /// <param name="jobId">The job to cancel.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// The result of the cancellation request:
    /// - Cancelled: Job was Pending and immediately cancelled.
    /// - CancellationRequested: Job is running and the flag was set.
    /// - AlreadyCompleted: Job was already in a terminal state.
    /// - NotFound: Job does not exist.
    /// </returns>
    Task<JobCancellationResult> RequestCancellationAsync(Guid jobId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks whether cancellation has been requested for a specific job.
    /// Called by job execution code at periodic checkpoints.
    ///
    /// This is a lightweight query designed to be called frequently (every few seconds)
    /// without significant overhead.
    /// </summary>
    /// <param name="jobId">The job to check.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if cancellation has been requested.</returns>
    Task<bool> IsCancellationRequestedAsync(Guid jobId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all jobs claimed by a specific server that have cancellation requested.
    /// More efficient than checking individual jobs when a server has many active jobs.
    /// </summary>
    /// <param name="serverId">The server to check.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of job IDs with cancellation requested.</returns>
    Task<List<Guid>> GetCancelledJobIdsForServerAsync(Guid serverId, CancellationToken cancellationToken = default);

    // ========================================================================
    // JOB REASSIGNMENT
    // ========================================================================

    /// <summary>
    /// Reassigns a job from one server to another. The job is reset to Pending
    /// with the new TargetServerId set, so it will be claimed by the target server
    /// on its next poll cycle.
    ///
    /// If the job is currently Processing, the cancellation flag is set first
    /// and the reassignment happens after the server acknowledges the cancellation.
    /// </summary>
    /// <param name="jobId">The job to reassign.</param>
    /// <param name="targetServerId">
    /// The server to reassign to. Null to allow any server to claim it.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if the job was successfully reassigned.</returns>
    Task<bool> ReassignJobAsync(Guid jobId, Guid? targetServerId, CancellationToken cancellationToken = default);

    // ========================================================================
    // SERVER-SCOPED QUERIES
    // ========================================================================

    /// <summary>
    /// Gets all active (Claimed or Processing) jobs for a specific server.
    /// Used by the admin dashboard and server health monitoring.
    /// </summary>
    /// <param name="serverId">The server to query.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of active jobs for the server.</returns>
    Task<List<JobQueueEntry>> GetActiveJobsForServerAsync(Guid serverId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets job execution history for a specific server with pagination.
    /// </summary>
    /// <param name="serverId">The server to query.</param>
    /// <param name="skip">Number of records to skip.</param>
    /// <param name="take">Number of records to return.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of historical job executions for the server.</returns>
    Task<List<JobQueueEntry>> GetJobHistoryForServerAsync(
        Guid serverId,
        int skip = 0,
        int take = 50,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets queue statistics broken down by server.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Queue summary with per-server breakdowns.</returns>
    Task<DistributedQueueSummary> GetDistributedQueueSummaryAsync(CancellationToken cancellationToken = default);
}

// ============================================================================
// SUPPORTING TYPES
// ============================================================================

/// <summary>
/// Result of a job cancellation request.
/// </summary>
public enum JobCancellationResult
{
    /// <summary>Job was Pending and immediately cancelled.</summary>
    Cancelled,

    /// <summary>Job is running and the cancellation flag was set.</summary>
    CancellationRequested,

    /// <summary>Job was already in a terminal state (Completed, Failed, Cancelled).</summary>
    AlreadyCompleted,

    /// <summary>Job does not exist.</summary>
    NotFound
}

/// <summary>
/// Extended queue summary with per-server breakdowns for distributed monitoring.
/// </summary>
public class DistributedQueueSummary : JobQueueSummary
{
    /// <summary>
    /// Active job count per server (ServerId -> count).
    /// </summary>
    public Dictionary<Guid, int> ActiveJobsByServer { get; set; } = new();

    /// <summary>
    /// Server names for display (ServerId -> name).
    /// </summary>
    public Dictionary<Guid, string> ServerNames { get; set; } = new();

    /// <summary>
    /// Available capacity per server (ServerId -> remaining slots).
    /// </summary>
    public Dictionary<Guid, int> CapacityByServer { get; set; } = new();

    /// <summary>
    /// Jobs pending with a specific TargetServerId (targeted routing).
    /// </summary>
    public int PendingTargeted { get; set; }

    /// <summary>
    /// Jobs pending with no TargetServerId (available to any server).
    /// </summary>
    public int PendingUnassigned { get; set; }

    /// <summary>
    /// Jobs with CancellationRequested=1 that are still running.
    /// </summary>
    public int PendingCancellation { get; set; }
}
