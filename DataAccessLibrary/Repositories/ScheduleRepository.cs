using Dapper;
using DataAccessLibrary.Models;
using Logging;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;

namespace DataAccessLibrary.Repositories;

public class ScheduleRepository : DapperRepositoryBase, IScheduleRepository
{
    public ScheduleRepository(IConfiguration configuration, IGlobalLogger logger) : base(configuration, logger) { }

    private SqlConnection CreateConnection() => new SqlConnection(_connectionString);

    #region Workflow Triggers

    public async Task<List<WorkflowTrigger>> GetActiveScheduledTriggersAsync()
    {
        using var conn = CreateConnection();
        var result = await conn.QueryAsync<WorkflowTrigger>(@"
            SELECT * FROM WorkflowTriggers
            WHERE IsActive = 1 AND CronExpression IS NOT NULL
            ORDER BY Name").ConfigureAwait(false);
        return result.ToList();
    }

    public async Task<WorkflowTrigger?> GetTriggerByIdAsync(Guid id)
    {
        using var conn = CreateConnection();
        return await conn.QueryFirstOrDefaultAsync<WorkflowTrigger>(
            "SELECT * FROM WorkflowTriggers WHERE Id = @Id", new { Id = id }).ConfigureAwait(false);
    }

    public async Task UpdateTriggerNextRunAsync(Guid id, DateTime? nextRun)
    {
        using var conn = CreateConnection();
        await conn.ExecuteAsync(
            "UPDATE WorkflowTriggers SET NextScheduledRun = @NextRun WHERE Id = @Id",
            new { Id = id, NextRun = nextRun }).ConfigureAwait(false);
    }

    public async Task<List<WorkflowTrigger>> GetDueTriggersAsync(DateTime now)
    {
        using var conn = CreateConnection();
        var result = await conn.QueryAsync<WorkflowTrigger>(@"
            SELECT * FROM WorkflowTriggers
            WHERE IsActive = 1 AND NextScheduledRun IS NOT NULL AND NextScheduledRun <= @Now
            ORDER BY NextScheduledRun", new { Now = now }).ConfigureAwait(false);
        return result.ToList();
    }

    #endregion

    #region Report Schedules

    public async Task<List<ReportSchedule>> GetActiveReportSchedulesAsync()
    {
        using var conn = CreateConnection();
        var result = await conn.QueryAsync<ReportSchedule>(@"
            SELECT * FROM ReportSchedules WHERE IsActive = 1 ORDER BY Name").ConfigureAwait(false);
        return result.ToList();
    }

    #endregion

    #region Job Execution History

    public async Task<Guid> CreateJobExecutionAsync(JobExecutionHistory execution)
    {
        using var conn = CreateConnection();
        execution.Id = execution.Id == Guid.Empty ? Guid.NewGuid() : execution.Id;

        await conn.ExecuteAsync(@"
            INSERT INTO JobExecutionHistory (Id, JobType, JobName, RelatedEntityId, RelatedEntityType,
                QuartzJobId, TriggerType, TriggeredBy, StartedAt, Status, ExecutingServer, IsRetry, RetryCount, ParentExecutionId,
                ItemsProcessed, ItemsSucceeded, ItemsFailed)
            VALUES (@Id, @JobType, @JobName, @RelatedEntityId, @RelatedEntityType,
                @QuartzJobId, @TriggerType, @TriggeredBy, @StartedAt, @Status, @ExecutingServer, @IsRetry, @RetryCount, @ParentExecutionId,
                @ItemsProcessed, @ItemsSucceeded, @ItemsFailed)",
            execution).ConfigureAwait(false);

        return execution.Id;
    }

    public async Task UpdateJobExecutionAsync(JobExecutionHistory execution)
    {
        using var conn = CreateConnection();
        await conn.ExecuteAsync(@"
            UPDATE JobExecutionHistory SET
                CompletedAt = @CompletedAt, DurationMs = @DurationMs, Status = @Status,
                ItemsProcessed = @ItemsProcessed, ItemsSucceeded = @ItemsSucceeded, ItemsFailed = @ItemsFailed,
                ResultSummaryJson = @ResultSummaryJson, ErrorMessage = @ErrorMessage,
                ExceptionDetails = @ExceptionDetails, NextScheduledRun = @NextScheduledRun
            WHERE Id = @Id", execution).ConfigureAwait(false);
    }

    public async Task<JobExecutionHistory?> GetJobExecutionAsync(Guid id)
    {
        using var conn = CreateConnection();
        return await conn.QueryFirstOrDefaultAsync<JobExecutionHistory>(
            "SELECT * FROM JobExecutionHistory WHERE Id = @Id", new { Id = id }).ConfigureAwait(false);
    }

    public async Task<(List<JobExecutionHistory> Items, int TotalCount)> GetJobExecutionsPagedAsync(
        string? jobType = null, string? status = null, int page = 1, int pageSize = 50)
    {
        using var conn = CreateConnection();
        var where = "WHERE 1=1";
        var p = new DynamicParameters();

        if (!string.IsNullOrEmpty(jobType)) { where += " AND JobType = @JobType"; p.Add("JobType", jobType); }
        if (!string.IsNullOrEmpty(status)) { where += " AND Status = @Status"; p.Add("Status", status); }

        var totalCount = await conn.ExecuteScalarAsync<int>(
            $"SELECT COUNT(*) FROM JobExecutionHistory {where}", p).ConfigureAwait(false);

        var offset = (page - 1) * pageSize;
        p.Add("Offset", offset);
        p.Add("PageSize", pageSize);
        var items = (await conn.QueryAsync<JobExecutionHistory>(
            $"SELECT * FROM JobExecutionHistory {where} ORDER BY StartedAt DESC OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY",
            p).ConfigureAwait(false)).ToList();

        return (items, totalCount);
    }

    public async Task<List<JobExecutionStatistics>> GetJobStatisticsAsync(DateTime since)
    {
        using var conn = CreateConnection();
        var result = await conn.QueryAsync<JobExecutionStatistics>(@"
            SELECT
                JobType,
                COUNT(*) as TotalExecutions,
                SUM(CASE WHEN Status = 'Completed' THEN 1 ELSE 0 END) as SuccessfulExecutions,
                SUM(CASE WHEN Status = 'Failed' THEN 1 ELSE 0 END) as FailedExecutions,
                SUM(CASE WHEN Status = 'Cancelled' THEN 1 ELSE 0 END) as CancelledExecutions,
                CASE WHEN COUNT(*) > 0
                    THEN CAST(SUM(CASE WHEN Status = 'Completed' THEN 1 ELSE 0 END) AS FLOAT) / COUNT(*) * 100
                    ELSE 0 END as SuccessRate,
                AVG(CAST(DurationMs AS FLOAT)) as AverageDurationMs,
                MAX(StartedAt) as LastExecution
            FROM JobExecutionHistory
            WHERE StartedAt >= @Since
            GROUP BY JobType
            ORDER BY JobType",
            new { Since = since }).ConfigureAwait(false);
        return result.ToList();
    }

    public async Task<int> MarkStaleRunningJobsAsync(DateTime cutoffTime)
    {
        using var conn = CreateConnection();
        return await conn.ExecuteAsync(@"
            UPDATE JobExecutionHistory
            SET Status = 'Failed',
                CompletedAt = GETUTCDATE(),
                ErrorMessage = 'Marked as failed - exceeded maximum execution time'
            WHERE Status = 'Running' AND StartedAt < @CutoffTime",
            new { CutoffTime = cutoffTime }).ConfigureAwait(false);
    }

    public async Task<List<JobExecutionHistory>> GetRecentJobExecutionsAsync(int limit = 20)
    {
        using var conn = CreateConnection();
        var result = await conn.QueryAsync<JobExecutionHistory>(
            "SELECT TOP (@Limit) * FROM JobExecutionHistory ORDER BY StartedAt DESC",
            new { Limit = limit }).ConfigureAwait(false);
        return result.ToList();
    }

    #endregion

    #region Scheduled Sync Projects

    public async Task<List<SyncProject>> GetScheduledSyncProjectsAsync()
    {
        using var conn = CreateConnection();
        var result = await conn.QueryAsync<SyncProject>(@"
            SELECT * FROM SyncProjects
            WHERE IsEnabled = 1 AND CronSchedule IS NOT NULL AND CronSchedule != ''
            ORDER BY Name").ConfigureAwait(false);
        return result.ToList();
    }

    #endregion

    #region Active Compliance Policies

    public async Task<List<CompliancePolicy>> GetActiveCompliancePoliciesWithScheduleAsync()
    {
        using var conn = CreateConnection();
        var result = await conn.QueryAsync<CompliancePolicy>(@"
            SELECT * FROM CompliancePolicies
            WHERE IsActive = 1 AND EvaluationFrequencyHours > 0
            ORDER BY Name").ConfigureAwait(false);
        return result.ToList();
    }

    #endregion
}
