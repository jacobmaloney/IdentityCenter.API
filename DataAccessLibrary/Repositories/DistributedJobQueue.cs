using System.Data;
using Dapper;
using DataAccessLibrary.Models;
using Logging;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace DataAccessLibrary.Repositories;

/// <summary>
/// Distributed-aware job queue repository.
/// Inherits all <see cref="IJobQueueRepository"/> methods from <see cref="JobQueueRepository"/>
/// and adds batch operations, server-aware claiming, cooperative cancellation, job
/// reassignment, and per-server queries required by the execution-server infrastructure
/// introduced in V052.
///
/// All Dapper queries use <c>await using var connection = CreateConnection()</c> to keep
/// each operation self-contained with its own connection lifetime.
/// </summary>
public class DistributedJobQueue : JobQueueRepository, IDistributedJobQueue
{
    private readonly ILogger<DistributedJobQueue> _distributedLogger;

    // The base-class _connectionString field is protected, but we expose a typed
    // helper here for clarity in this class.
    private SqlConnection CreateConnection() => new(_connectionString);

    public DistributedJobQueue(IConfiguration configuration, IGlobalLogger globalLogger,
        ILogger<DistributedJobQueue> logger)
        : base(configuration, globalLogger)
    {
        _distributedLogger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    // =========================================================================
    // BATCH OPERATIONS
    // =========================================================================

    /// <inheritdoc/>
    public async Task<int> EnqueueBatchAsync(
        IReadOnlyList<JobQueueEntry> jobs,
        CancellationToken cancellationToken = default)
    {
        if (jobs.Count == 0) return 0;

        // Build the DataTable matching the dbo.JobQueueBatchType TVP definition from V052.
        var table = new DataTable();
        table.Columns.Add("Id",                typeof(Guid));
        table.Columns.Add("JobType",           typeof(string));
        table.Columns.Add("JobName",           typeof(string));
        table.Columns.Add("RelatedEntityId",   typeof(Guid));
        table.Columns.Add("RelatedEntityType", typeof(string));
        table.Columns.Add("Priority",          typeof(int));
        table.Columns.Add("Ready2Execute",     typeof(bool));
        table.Columns.Add("ScheduledAt",       typeof(DateTime));
        table.Columns.Add("CreatedBy",         typeof(string));
        table.Columns.Add("MaxRetries",        typeof(int));
        table.Columns.Add("PayloadJson",       typeof(string));
        table.Columns.Add("Tags",              typeof(string));
        table.Columns.Add("TargetServerId",    typeof(Guid));

        foreach (var job in jobs)
        {
            var row = table.NewRow();
            row["Id"]                = job.Id == Guid.Empty ? Guid.NewGuid() : job.Id;
            row["JobType"]           = job.JobType;
            row["JobName"]           = job.JobName;
            row["RelatedEntityId"]   = job.RelatedEntityId.HasValue
                                           ? (object)job.RelatedEntityId.Value : DBNull.Value;
            row["RelatedEntityType"] = (object?)job.RelatedEntityType ?? DBNull.Value;
            row["Priority"]          = job.Priority;
            row["Ready2Execute"]     = job.Ready2Execute;
            row["ScheduledAt"]       = job.ScheduledAt.HasValue
                                           ? (object)job.ScheduledAt.Value : DBNull.Value;
            row["CreatedBy"]         = job.CreatedBy;
            row["MaxRetries"]        = job.MaxRetries;
            row["PayloadJson"]       = (object?)job.PayloadJson ?? DBNull.Value;
            row["Tags"]              = (object?)job.Tags ?? DBNull.Value;
            row["TargetServerId"]    = job.TargetServerId.HasValue
                                           ? (object)job.TargetServerId.Value : DBNull.Value;
            table.Rows.Add(row);
        }

        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        var parameters = new DynamicParameters();
        parameters.Add("Jobs", table.AsTableValuedParameter("dbo.JobQueueBatchType"));

        var inserted = await connection.QuerySingleAsync<int>(
            "usp_EnqueueJobBatch",
            parameters,
            commandType: CommandType.StoredProcedure);

        _distributedLogger.LogInformation("EnqueueBatchAsync: inserted {Count} of {Total} jobs",
            inserted, jobs.Count);

        return inserted;
    }

    // =========================================================================
    // DISTRIBUTED CLAIMING
    // =========================================================================

    /// <inheritdoc/>
    public async Task<List<JobQueueEntry>> ClaimJobBatchAsync(
        Guid serverId,
        IReadOnlyList<string> supportedJobTypes,
        int maxJobs = 5,
        CancellationToken cancellationToken = default)
    {
        var jobTypesCsv = string.Join(",", supportedJobTypes);

        var parameters = new DynamicParameters();
        parameters.Add("ServerId",           serverId);
        parameters.Add("SupportedJobTypes",  jobTypesCsv);
        parameters.Add("MaxJobs",            maxJobs);

        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        using var multi = await connection.QueryMultipleAsync(
            "usp_ClaimJobsForServer",
            parameters,
            commandType: CommandType.StoredProcedure);

        // Result set 1: claimed job rows (full columns).
        var claimedJobs = (await multi.ReadAsync<JobQueueEntry>()).ToList();

        // The procedure may emit additional result sets (e.g. the RemoteAgents UPDATE
        // with NOCOUNT OFF). Consume any remaining grids so the reader closes cleanly.
        while (!multi.IsConsumed)
        {
            await multi.ReadAsync();
        }

        if (claimedJobs.Count > 0)
        {
            _distributedLogger.LogInformation(
                "Server {ServerId} claimed {Count} job(s) via ClaimJobBatchAsync",
                serverId, claimedJobs.Count);
        }

        return claimedJobs;
    }

    // =========================================================================
    // COOPERATIVE CANCELLATION
    // =========================================================================

    /// <inheritdoc/>
    public async Task<JobCancellationResult> RequestCancellationAsync(
        Guid jobId, CancellationToken cancellationToken = default)
    {
        const string selectSql =
            "SELECT Status FROM JobQueue WHERE Id = @JobId";

        const string cancelPendingSql = @"
            UPDATE JobQueue
            SET Status = 'Cancelled', CompletedAt = GETUTCDATE()
            WHERE Id = @JobId AND Status = 'Pending'";

        const string flagRunningSql = @"
            UPDATE JobQueue
            SET CancellationRequested = 1
            WHERE Id = @JobId AND Status IN ('Claimed', 'Processing')";

        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        var status = await connection.QuerySingleOrDefaultAsync<string?>(
            selectSql, new { JobId = jobId });

        if (status is null)
            return JobCancellationResult.NotFound;

        if (status is "Completed" or "Failed" or "Cancelled")
            return JobCancellationResult.AlreadyCompleted;

        if (status == "Pending")
        {
            var rows = await connection.ExecuteAsync(cancelPendingSql, new { JobId = jobId });
            // If another thread already moved it out of Pending, treat as completed.
            return rows > 0 ? JobCancellationResult.Cancelled : JobCancellationResult.AlreadyCompleted;
        }

        // Claimed or Processing — set the cooperative flag.
        await connection.ExecuteAsync(flagRunningSql, new { JobId = jobId });
        _distributedLogger.LogInformation("Cancellation requested for job {JobId}", jobId);
        return JobCancellationResult.CancellationRequested;
    }

    /// <inheritdoc/>
    public async Task<bool> IsCancellationRequestedAsync(
        Guid jobId, CancellationToken cancellationToken = default)
    {
        const string sql =
            "SELECT CancellationRequested FROM JobQueue WHERE Id = @JobId";

        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        return await connection.QuerySingleOrDefaultAsync<bool>(sql, new { JobId = jobId });
    }

    /// <inheritdoc/>
    public async Task<List<Guid>> GetCancelledJobIdsForServerAsync(
        Guid serverId, CancellationToken cancellationToken = default)
    {
        const string sql = @"
            SELECT Id
            FROM JobQueue
            WHERE ClaimedByAgentId = @ServerId
              AND CancellationRequested = 1
              AND Status IN ('Claimed', 'Processing')";

        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        var ids = await connection.QueryAsync<Guid>(sql, new { ServerId = serverId });
        return ids.ToList();
    }

    // =========================================================================
    // JOB REASSIGNMENT
    // =========================================================================

    /// <inheritdoc/>
    public async Task<bool> ReassignJobAsync(
        Guid jobId, Guid? targetServerId, CancellationToken cancellationToken = default)
    {
        const string sql = @"
            UPDATE JobQueue
            SET
                Status           = 'Pending',
                ClaimedByAgentId = NULL,
                ClaimedAt        = NULL,
                TargetServerId   = @NewTargetServerId
            WHERE Id = @JobId
              AND Status IN ('Pending', 'Claimed')";

        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        var affected = await connection.ExecuteAsync(sql, new
        {
            JobId             = jobId,
            NewTargetServerId = (object?)targetServerId ?? DBNull.Value
        });

        return affected > 0;
    }

    // =========================================================================
    // SERVER-SCOPED QUERIES
    // =========================================================================

    /// <summary>
    /// Releases all Claimed/Processing jobs for <paramref name="serverId"/> back to Pending.
    /// Used during graceful shutdown or after a server failure is detected.
    /// </summary>
    /// <returns>Number of jobs released.</returns>
    public async Task<int> ReleaseServerJobsAsync(
        Guid serverId, CancellationToken cancellationToken = default)
    {
        const string sql = @"
            UPDATE JobQueue
            SET
                Status           = 'Pending',
                ClaimedByAgentId = NULL,
                ClaimedAt        = NULL,
                StartedAt        = NULL,
                ProgressPercent  = 0
            WHERE ClaimedByAgentId = @ServerId
              AND Status IN ('Claimed', 'Processing');

            SELECT @@ROWCOUNT;";

        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        var released = await connection.QuerySingleAsync<int>(sql, new { ServerId = serverId });

        if (released > 0)
        {
            _distributedLogger.LogWarning(
                "Released {Count} job(s) for server {ServerId} back to Pending",
                released, serverId);
        }

        return released;
    }

    /// <inheritdoc/>
    public async Task<List<JobQueueEntry>> GetActiveJobsForServerAsync(
        Guid serverId, CancellationToken cancellationToken = default)
    {
        const string sql = @"
            SELECT
                Id, JobType, JobName, RelatedEntityId, RelatedEntityType,
                Status, Priority, Ready2Execute, ScheduledAt, CreatedAt, CreatedBy,
                ClaimedByAgentId, ClaimedAt, StartedAt, CompletedAt, DurationMs,
                ItemsProcessed, ItemsSucceeded, ItemsFailed, ErrorMessage,
                ExceptionDetailsJson, RetryAttempt, MaxRetries, PayloadJson,
                ResultJson, ProgressPercent, ProgressMessage, LastProgressUpdate, Tags,
                TargetServerId, CancellationRequested
            FROM JobQueue
            WHERE ClaimedByAgentId = @ServerId
              AND Status IN ('Claimed', 'Processing')
            ORDER BY StartedAt";

        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        var jobs = await connection.QueryAsync<JobQueueEntry>(sql, new { ServerId = serverId });
        return jobs.ToList();
    }

    /// <inheritdoc/>
    public async Task<DistributedQueueSummary> GetDistributedQueueSummaryAsync(
        CancellationToken cancellationToken = default)
    {
        // Single multi-result-set round trip for efficiency.
        const string sql = @"
            -- 1. Aggregate counts
            SELECT
                (SELECT COUNT(*) FROM JobQueue WHERE Status = 'Pending')
                    AS TotalPending,
                (SELECT COUNT(*) FROM JobQueue WHERE Status IN ('Claimed','Processing'))
                    AS TotalProcessing,
                (SELECT COUNT(*) FROM JobQueue WHERE Status = 'Completed'
                    AND CompletedAt >= DATEADD(HOUR,-24,GETUTCDATE()))
                    AS TotalCompleted24h,
                (SELECT COUNT(*) FROM JobQueue WHERE Status = 'Failed'
                    AND CompletedAt >= DATEADD(HOUR,-24,GETUTCDATE()))
                    AS TotalFailed24h,
                (SELECT COUNT(*) FROM JobQueue WHERE Status = 'Pending'
                    AND TargetServerId IS NOT NULL)
                    AS PendingTargeted,
                (SELECT COUNT(*) FROM JobQueue WHERE Status = 'Pending'
                    AND TargetServerId IS NULL)
                    AS PendingUnassigned,
                (SELECT COUNT(*) FROM JobQueue WHERE CancellationRequested = 1
                    AND Status IN ('Claimed','Processing'))
                    AS PendingCancellation;

            -- 2. Pending jobs by type (for JobQueueSummary.PendingByType)
            SELECT JobType, COUNT(*) AS Count
            FROM JobQueue
            WHERE Status = 'Pending'
            GROUP BY JobType;

            -- 3. Active jobs by agent name (for JobQueueSummary.ProcessingByAgent)
            SELECT CAST(ClaimedByAgentId AS nvarchar(50)) AS AgentId, COUNT(*) AS Count
            FROM JobQueue
            WHERE Status IN ('Claimed','Processing')
            GROUP BY ClaimedByAgentId;

            -- 4. Per-server active counts + capacity (for DistributedQueueSummary)
            SELECT
                ra.Id                       AS ServerId,
                ra.AgentName,
                ra.MaxConcurrentJobs,
                ISNULL(jq.ActiveCount, 0)   AS ActiveCount
            FROM RemoteAgents ra
            LEFT JOIN (
                SELECT ClaimedByAgentId, COUNT(*) AS ActiveCount
                FROM JobQueue
                WHERE Status IN ('Claimed','Processing')
                GROUP BY ClaimedByAgentId
            ) jq ON ra.Id = jq.ClaimedByAgentId
            WHERE ra.IsEnabled = 1;";

        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        using var multi = await connection.QueryMultipleAsync(sql);

        var counts = await multi.ReadSingleAsync<(
            int TotalPending,
            int TotalProcessing,
            int TotalCompleted24h,
            int TotalFailed24h,
            int PendingTargeted,
            int PendingUnassigned,
            int PendingCancellation)>();

        var pendingByType = (await multi.ReadAsync<(string JobType, int Count)>())
            .ToDictionary(x => x.JobType, x => x.Count);

        var processingByAgent = (await multi.ReadAsync<(string? AgentId, int Count)>())
            .Where(x => x.AgentId is not null)
            .ToDictionary(x => x.AgentId!, x => x.Count);

        var serverRows = (await multi.ReadAsync<(
            Guid ServerId, string AgentName, int MaxConcurrentJobs, int ActiveCount)>()).ToList();

        return new DistributedQueueSummary
        {
            TotalPending        = counts.TotalPending,
            TotalProcessing     = counts.TotalProcessing,
            TotalCompleted24h   = counts.TotalCompleted24h,
            TotalFailed24h      = counts.TotalFailed24h,
            PendingByType       = pendingByType,
            ProcessingByAgent   = processingByAgent,
            ActiveJobsByServer  = serverRows.ToDictionary(r => r.ServerId, r => r.ActiveCount),
            ServerNames         = serverRows.ToDictionary(r => r.ServerId, r => r.AgentName),
            CapacityByServer    = serverRows.ToDictionary(r => r.ServerId,
                                      r => Math.Max(0, r.MaxConcurrentJobs - r.ActiveCount)),
            PendingTargeted     = counts.PendingTargeted,
            PendingUnassigned   = counts.PendingUnassigned,
            PendingCancellation = counts.PendingCancellation
        };
    }

    /// <inheritdoc/>
    public async Task<List<JobQueueEntry>> GetJobHistoryForServerAsync(
        Guid serverId,
        int skip = 0,
        int take = 50,
        CancellationToken cancellationToken = default)
    {
        const string sql = @"
            SELECT
                Id, JobType, JobName, RelatedEntityId, RelatedEntityType,
                Status, Priority, Ready2Execute, ScheduledAt, CreatedAt, CreatedBy,
                ClaimedByAgentId, ClaimedAt, StartedAt, CompletedAt, DurationMs,
                ItemsProcessed, ItemsSucceeded, ItemsFailed, ErrorMessage,
                ExceptionDetailsJson, RetryAttempt, MaxRetries, PayloadJson,
                ResultJson, ProgressPercent, ProgressMessage, LastProgressUpdate, Tags,
                TargetServerId, CancellationRequested
            FROM JobQueue
            WHERE ClaimedByAgentId = @ServerId
              AND Status IN ('Completed', 'Failed', 'Cancelled')
            ORDER BY StartedAt DESC
            OFFSET @Skip ROWS FETCH NEXT @Take ROWS ONLY";

        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        var jobs = await connection.QueryAsync<JobQueueEntry>(sql,
            new { ServerId = serverId, Skip = skip, Take = take });

        return jobs.ToList();
    }
}
