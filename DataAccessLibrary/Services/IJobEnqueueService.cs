using DataAccessLibrary.Models;

namespace DataAccessLibrary.Services;

/// <summary>
/// Service for enqueuing jobs to the distributed job queue.
/// Used by Quartz triggers on the WebPortal to enqueue work
/// instead of executing it directly. Workers then pick up
/// and execute the jobs via IJobTypeHandler implementations.
/// </summary>
public interface IJobEnqueueService
{
    /// <summary>
    /// Enqueue a single job to the distributed queue.
    /// </summary>
    /// <param name="jobType">Job type matching an IJobTypeHandler.JobType</param>
    /// <param name="jobName">Human-readable name for the job</param>
    /// <param name="relatedEntityId">Optional entity ID (e.g. SyncProjectId, PolicyId)</param>
    /// <param name="payloadJson">Optional JSON payload with job-specific parameters</param>
    /// <param name="targetServerId">Optional: route to specific server</param>
    /// <param name="ct">Cancellation token</param>
    Task EnqueueAsync(
        string jobType,
        string jobName,
        Guid? relatedEntityId = null,
        string? payloadJson = null,
        Guid? targetServerId = null,
        CancellationToken ct = default);
}
