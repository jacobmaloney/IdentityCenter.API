using DataAccessLibrary.Models;

namespace DataAccessLibrary.Repositories;

public interface IScheduleRepository
{
    // Workflow Triggers
    Task<List<WorkflowTrigger>> GetActiveScheduledTriggersAsync();
    Task<WorkflowTrigger?> GetTriggerByIdAsync(Guid id);
    Task UpdateTriggerNextRunAsync(Guid id, DateTime? nextRun);
    Task<List<WorkflowTrigger>> GetDueTriggersAsync(DateTime now);

    // Report Schedules
    Task<List<ReportSchedule>> GetActiveReportSchedulesAsync();

    // Job Execution History
    Task<Guid> CreateJobExecutionAsync(JobExecutionHistory execution);
    Task UpdateJobExecutionAsync(JobExecutionHistory execution);
    Task<JobExecutionHistory?> GetJobExecutionAsync(Guid id);
    Task<(List<JobExecutionHistory> Items, int TotalCount)> GetJobExecutionsPagedAsync(
        string? jobType = null, string? status = null, int page = 1, int pageSize = 50);
    Task<List<JobExecutionStatistics>> GetJobStatisticsAsync(DateTime since);
    Task<int> MarkStaleRunningJobsAsync(DateTime cutoffTime);
    Task<List<JobExecutionHistory>> GetRecentJobExecutionsAsync(int limit = 20);

    // Scheduled Sync Projects (projects with CronSchedule set)
    Task<List<SyncProject>> GetScheduledSyncProjectsAsync();

    // Active Compliance Policies with schedule
    Task<List<CompliancePolicy>> GetActiveCompliancePoliciesWithScheduleAsync();
}
