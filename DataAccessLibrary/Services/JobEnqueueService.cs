using DataAccessLibrary.Models;
using DataAccessLibrary.Repositories;
using Logging;

namespace DataAccessLibrary.Services;

/// <summary>
/// Enqueues jobs to the distributed JobQueue table via stored procedures.
/// WebPortal and Quartz triggers call this to enqueue work.
/// Workers poll and execute via ExecutionServerJobRunner.
/// </summary>
public class JobEnqueueService : IJobEnqueueService
{
    private readonly IDistributedJobQueue _queue;
    private readonly IGlobalLogger _logger;

    public JobEnqueueService(IDistributedJobQueue queue, IGlobalLogger logger)
    {
        _queue = queue;
        _logger = logger;
    }

    public async Task EnqueueAsync(
        string jobType,
        string jobName,
        Guid? relatedEntityId = null,
        string? payloadJson = null,
        Guid? targetServerId = null,
        CancellationToken ct = default)
    {
        var entry = new JobQueueEntry
        {
            Id = Guid.NewGuid(),
            JobType = jobType,
            JobName = jobName,
            RelatedEntityId = relatedEntityId,
            PayloadJson = payloadJson,
            TargetServerId = targetServerId,
            Status = "Pending",
            Priority = 5,
            MaxRetries = 3,
            RetryAttempt = 0,
            CreatedAt = DateTime.UtcNow,
            Ready2Execute = true
        };

        await _queue.EnqueueBatchAsync(new[] { entry }, ct);

        _logger.LogInformation("JobEnqueueService: enqueued {JobType} '{JobName}' (entity: {EntityId})",
            jobType, jobName, relatedEntityId?.ToString() ?? "none");
    }
}
