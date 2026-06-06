using DataAccessLibrary.Models;

namespace DataAccessLibrary.Repositories;

public interface IJobQueueRepository
{
    Task<JobQueueEntry?> ClaimNextJobAsync(Guid agentId, List<string> supportedJobTypes);
    Task<List<JobQueueEntry>> GetPendingJobsAsync(int limit = 50);
    Task<JobQueueEntry?> GetJobByIdAsync(Guid jobId);
    Task UpdateJobProgressAsync(Guid jobId, int progressPercent, string? progressMessage, int itemsProcessed, int itemsSucceeded, int itemsFailed);
    Task CompleteJobAsync(Guid jobId, bool success, int itemsProcessed, int itemsSucceeded, int itemsFailed, string? errorMessage, string? resultJson);
    Task<Guid> QueueJobAsync(JobQueueEntry job);
    Task<JobQueueSummary> GetQueueSummaryAsync();
    Task ReleaseStaleJobsAsync(int staleMinutes = 30);
    Task<bool> CancelJobAsync(Guid jobId);
}
