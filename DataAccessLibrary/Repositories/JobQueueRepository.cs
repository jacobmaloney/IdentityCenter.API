using Dapper;
using DataAccessLibrary.Models;
using Logging;
using Microsoft.Extensions.Configuration;

namespace DataAccessLibrary.Repositories;

public class JobQueueRepository : DapperRepositoryBase, IJobQueueRepository
{
    public JobQueueRepository(IConfiguration configuration, IGlobalLogger logger)
        : base(configuration, logger) { }

    public async Task<JobQueueEntry?> ClaimNextJobAsync(Guid agentId, List<string> supportedJobTypes)
    {
        const string sql = @"
            DECLARE @JobId UNIQUEIDENTIFIER;

            UPDATE TOP(1) jq
            SET
                @JobId = jq.Id,
                jq.Status = 'Claimed',
                jq.ClaimedByAgentId = @AgentId,
                jq.ClaimedAt = GETUTCDATE()
            FROM JobQueue jq WITH (ROWLOCK, UPDLOCK, READPAST)
            WHERE jq.Status = 'Pending'
              AND jq.Ready2Execute = 1
              AND (jq.ScheduledAt IS NULL OR jq.ScheduledAt <= GETUTCDATE())
              AND jq.JobType IN @SupportedJobTypes
            ORDER BY jq.Priority DESC, jq.CreatedAt ASC;

            SELECT
                Id, JobType, JobName, RelatedEntityId, RelatedEntityType,
                Status, Priority, Ready2Execute, ScheduledAt, CreatedAt, CreatedBy,
                ClaimedByAgentId, ClaimedAt, StartedAt, CompletedAt, DurationMs,
                ItemsProcessed, ItemsSucceeded, ItemsFailed, ErrorMessage,
                ExceptionDetailsJson, RetryAttempt, MaxRetries, PayloadJson,
                ResultJson, ProgressPercent, ProgressMessage, LastProgressUpdate, Tags
            FROM JobQueue
            WHERE Id = @JobId;
        ";

        return await ExecuteAsync(async connection =>
        {
            var job = await connection.QuerySingleOrDefaultAsync<JobQueueEntry>(sql, new
            {
                AgentId = agentId,
                SupportedJobTypes = supportedJobTypes
            });

            if (job != null)
            {
                _logger.LogInformation("Agent {AgentId} claimed job {JobId} ({JobType})",
                    agentId, job.Id, job.JobType);
            }

            return job;
        });
    }

    public async Task<List<JobQueueEntry>> GetPendingJobsAsync(int limit = 50)
    {
        const string sql = @"
            SELECT TOP(@Limit)
                Id, JobType, JobName, RelatedEntityId, RelatedEntityType,
                Status, Priority, Ready2Execute, ScheduledAt, CreatedAt, CreatedBy,
                ClaimedByAgentId, ClaimedAt, StartedAt, CompletedAt, DurationMs,
                ItemsProcessed, ItemsSucceeded, ItemsFailed, ErrorMessage,
                RetryAttempt, MaxRetries, ProgressPercent, ProgressMessage, Tags
            FROM JobQueue
            WHERE Status IN ('Pending', 'Claimed', 'Processing')
            ORDER BY
                CASE Status
                    WHEN 'Processing' THEN 1
                    WHEN 'Claimed' THEN 2
                    ELSE 3
                END,
                Priority DESC,
                CreatedAt ASC
        ";

        return await ExecuteAsync(async connection =>
        {
            var jobs = await connection.QueryAsync<JobQueueEntry>(sql, new { Limit = limit });
            return jobs.ToList();
        });
    }

    public async Task<JobQueueEntry?> GetJobByIdAsync(Guid jobId)
    {
        const string sql = @"
            SELECT
                Id, JobType, JobName, RelatedEntityId, RelatedEntityType,
                Status, Priority, Ready2Execute, ScheduledAt, CreatedAt, CreatedBy,
                ClaimedByAgentId, ClaimedAt, StartedAt, CompletedAt, DurationMs,
                ItemsProcessed, ItemsSucceeded, ItemsFailed, ErrorMessage,
                ExceptionDetailsJson, RetryAttempt, MaxRetries, PayloadJson,
                ResultJson, ProgressPercent, ProgressMessage, LastProgressUpdate, Tags
            FROM JobQueue
            WHERE Id = @JobId
        ";

        return await ExecuteAsync(async connection =>
            await connection.QuerySingleOrDefaultAsync<JobQueueEntry>(sql, new { JobId = jobId }));
    }

    public async Task UpdateJobProgressAsync(Guid jobId, int progressPercent, string? progressMessage,
        int itemsProcessed, int itemsSucceeded, int itemsFailed)
    {
        const string sql = @"
            UPDATE JobQueue
            SET
                Status = CASE WHEN Status = 'Claimed' THEN 'Processing' ELSE Status END,
                StartedAt = CASE WHEN StartedAt IS NULL THEN GETUTCDATE() ELSE StartedAt END,
                ProgressPercent = @ProgressPercent,
                ProgressMessage = @ProgressMessage,
                ItemsProcessed = @ItemsProcessed,
                ItemsSucceeded = @ItemsSucceeded,
                ItemsFailed = @ItemsFailed,
                LastProgressUpdate = GETUTCDATE()
            WHERE Id = @JobId
              AND Status IN ('Claimed', 'Processing')
        ";

        await ExecuteNonQueryAsync(async connection =>
            await connection.ExecuteAsync(sql, new
            {
                JobId = jobId,
                ProgressPercent = progressPercent,
                ProgressMessage = progressMessage,
                ItemsProcessed = itemsProcessed,
                ItemsSucceeded = itemsSucceeded,
                ItemsFailed = itemsFailed
            }));
    }

    public async Task CompleteJobAsync(Guid jobId, bool success, int itemsProcessed,
        int itemsSucceeded, int itemsFailed, string? errorMessage, string? resultJson)
    {
        const string sql = @"
            UPDATE JobQueue
            SET
                Status = @Status,
                CompletedAt = GETUTCDATE(),
                DurationMs = DATEDIFF(MILLISECOND, ISNULL(StartedAt, ClaimedAt), GETUTCDATE()),
                ItemsProcessed = @ItemsProcessed,
                ItemsSucceeded = @ItemsSucceeded,
                ItemsFailed = @ItemsFailed,
                ErrorMessage = @ErrorMessage,
                ResultJson = @ResultJson,
                ProgressPercent = 100,
                LastProgressUpdate = GETUTCDATE()
            WHERE Id = @JobId
        ";

        await ExecuteNonQueryAsync(async connection =>
            await connection.ExecuteAsync(sql, new
            {
                JobId = jobId,
                Status = success ? "Completed" : "Failed",
                ItemsProcessed = itemsProcessed,
                ItemsSucceeded = itemsSucceeded,
                ItemsFailed = itemsFailed,
                ErrorMessage = errorMessage,
                ResultJson = resultJson
            }));

        _logger.LogInformation("Job {JobId} completed with status {Status}. Processed: {Processed}, Succeeded: {Succeeded}, Failed: {Failed}",
            jobId, success ? "Completed" : "Failed", itemsProcessed, itemsSucceeded, itemsFailed);
    }

    public async Task<Guid> QueueJobAsync(JobQueueEntry job)
    {
        const string sql = @"
            INSERT INTO JobQueue (
                Id, JobType, JobName, RelatedEntityId, RelatedEntityType,
                Status, Priority, Ready2Execute, ScheduledAt, CreatedAt, CreatedBy,
                RetryAttempt, MaxRetries, PayloadJson, Tags
            )
            VALUES (
                @Id, @JobType, @JobName, @RelatedEntityId, @RelatedEntityType,
                'Pending', @Priority, @Ready2Execute, @ScheduledAt, GETUTCDATE(), @CreatedBy,
                0, @MaxRetries, @PayloadJson, @Tags
            )
        ";

        job.Id = Guid.NewGuid();

        await ExecuteNonQueryAsync(async connection =>
            await connection.ExecuteAsync(sql, job));

        _logger.LogInformation("Queued job {JobId} ({JobType}): {JobName}", job.Id, job.JobType, job.JobName);

        return job.Id;
    }

    public async Task<JobQueueSummary> GetQueueSummaryAsync()
    {
        const string sql = @"
            SELECT JobType, COUNT(*) as Count
            FROM JobQueue
            WHERE Status = 'Pending'
            GROUP BY JobType;

            SELECT CAST(ClaimedByAgentId AS NVARCHAR(50)) as AgentId, COUNT(*) as Count
            FROM JobQueue
            WHERE Status IN ('Claimed', 'Processing')
            GROUP BY ClaimedByAgentId;

            SELECT
                (SELECT COUNT(*) FROM JobQueue WHERE Status = 'Pending') as TotalPending,
                (SELECT COUNT(*) FROM JobQueue WHERE Status IN ('Claimed', 'Processing')) as TotalProcessing,
                (SELECT COUNT(*) FROM JobQueue WHERE Status = 'Completed' AND CompletedAt >= DATEADD(HOUR, -24, GETUTCDATE())) as TotalCompleted24h,
                (SELECT COUNT(*) FROM JobQueue WHERE Status = 'Failed' AND CompletedAt >= DATEADD(HOUR, -24, GETUTCDATE())) as TotalFailed24h;
        ";

        return await ExecuteAsync(async connection =>
        {
            using var multi = await connection.QueryMultipleAsync(sql);

            var pendingByType = (await multi.ReadAsync<(string JobType, int Count)>())
                .ToDictionary(x => x.JobType, x => x.Count);

            var processingByAgent = (await multi.ReadAsync<(string AgentId, int Count)>())
                .Where(x => x.AgentId != null)
                .ToDictionary(x => x.AgentId, x => x.Count);

            var counts = await multi.ReadSingleAsync<(int TotalPending, int TotalProcessing, int TotalCompleted24h, int TotalFailed24h)>();

            return new JobQueueSummary
            {
                TotalPending = counts.TotalPending,
                TotalProcessing = counts.TotalProcessing,
                TotalCompleted24h = counts.TotalCompleted24h,
                TotalFailed24h = counts.TotalFailed24h,
                PendingByType = pendingByType,
                ProcessingByAgent = processingByAgent
            };
        });
    }

    public async Task ReleaseStaleJobsAsync(int staleMinutes = 30)
    {
        const string sql = @"
            UPDATE JobQueue
            SET
                Status = 'Pending',
                ClaimedByAgentId = NULL,
                ClaimedAt = NULL,
                StartedAt = NULL,
                RetryAttempt = RetryAttempt + 1,
                ProgressPercent = 0,
                ProgressMessage = 'Released due to timeout'
            WHERE Status IN ('Claimed', 'Processing')
              AND (LastProgressUpdate IS NULL AND ClaimedAt < DATEADD(MINUTE, -@StaleMinutes, GETUTCDATE()))
              OR (LastProgressUpdate < DATEADD(MINUTE, -@StaleMinutes, GETUTCDATE()))
              AND RetryAttempt < MaxRetries
        ";

        var released = await ExecuteAsync(async connection =>
            await connection.ExecuteAsync(sql, new { StaleMinutes = staleMinutes }));

        if (released > 0)
        {
            _logger.LogWarning("Released {Count} stale jobs back to pending status", released);
        }
    }

    public async Task<bool> CancelJobAsync(Guid jobId)
    {
        const string sql = @"
            UPDATE JobQueue
            SET
                Status = 'Cancelled',
                CompletedAt = GETUTCDATE()
            WHERE Id = @JobId
              AND Status IN ('Pending', 'Claimed')
        ";

        var affected = await ExecuteAsync(async connection =>
            await connection.ExecuteAsync(sql, new { JobId = jobId }));

        return affected > 0;
    }
}
