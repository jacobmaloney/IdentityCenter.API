using DataAccessLibrary.Models;

namespace DataAccessLibrary.Repositories;

/// <summary>
/// Dev Center script CRUD and step assignment operations.
/// </summary>
public interface ISyncScriptRepository
{
    Task<List<StepScriptInfo>> GetStepScriptsAsync(
        Guid syncStepId, string executionPhase, CancellationToken cancellationToken = default);

    Task<SyncProcessingScript?> GetScriptByIdAsync(Guid scriptId, CancellationToken cancellationToken = default);

    Task<Guid> RecordScriptExecutionAsync(
        SyncScriptExecution execution, CancellationToken cancellationToken = default);

    Task UpdateScriptCompilationStatusAsync(
        Guid scriptId, string status, string? errorMessage, CancellationToken cancellationToken = default);

    Task<List<SyncProcessingScript>> GetAllScriptsAsync(CancellationToken cancellationToken = default);

    Task<Guid> SaveScriptAsync(SyncProcessingScript script, CancellationToken cancellationToken = default);

    Task<bool> DeleteScriptAsync(Guid scriptId, CancellationToken cancellationToken = default);

    Task AssignScriptToStepAsync(
        Guid syncStepId, Guid scriptId, string executionPhase, int executionOrder,
        CancellationToken cancellationToken = default);

    Task RemoveScriptFromStepAsync(Guid syncStepScriptId, CancellationToken cancellationToken = default);

    Task AutoAssignPersonMatchingScriptAsync(Guid syncStepId, CancellationToken cancellationToken = default);

    Task RemovePersonMatchingScriptAsync(Guid syncStepId, CancellationToken cancellationToken = default);
}
