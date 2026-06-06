using DataAccessLibrary.Models;

namespace DataAccessLibrary.Services;

/// <summary>
/// Handles execution of a specific job type within the distributed job runner.
///
/// Each job type (e.g. "SyncProject", "PolicyEvaluation") is implemented as a separate
/// IJobTypeHandler registered in DI. The ExecutionServerJobRunner resolves the correct
/// handler by matching JobType to IJobTypeHandler.JobType.
///
/// Implementations receive a scoped IServiceProvider so they can resolve
/// any scoped services they need (repositories, orchestrators, etc.).
/// </summary>
public interface IJobTypeHandler
{
    /// <summary>
    /// The job type string this handler is responsible for (e.g. "SyncProject").
    /// Must match the JobType column in the JobQueue table exactly.
    /// </summary>
    string JobType { get; }

    /// <summary>
    /// Executes the job. Called by ExecutionServerJobRunner inside a DI scope.
    ///
    /// The handler is responsible for all business logic. The runner handles
    /// status transitions (Processing, Completed, Failed, Cancelled) and
    /// timing. The handler should respect the cancellation token and throw
    /// OperationCanceledException if cancelled.
    /// </summary>
    /// <param name="job">The claimed job entry, including PayloadJson and RelatedEntityId.</param>
    /// <param name="scopedProvider">A scoped IServiceProvider for resolving dependencies.</param>
    /// <param name="ct">Cancellation token. Check this at regular intervals.</param>
    Task ExecuteAsync(JobQueueEntry job, IServiceProvider scopedProvider, CancellationToken ct);
}
