using DataAccessLibrary.Models;
using Logging;
using Microsoft.Extensions.DependencyInjection;

namespace DataAccessLibrary.Services.JobHandlers;

/// <summary>
/// Handles SyncProject jobs in the distributed execution system.
/// Resolves SyncProjectOrchestrator and runs the sync for the specified project.
/// </summary>
public class SyncProjectJobHandler : IJobTypeHandler
{
    public string JobType => "SyncProject";

    public async Task ExecuteAsync(JobQueueEntry job, IServiceProvider scopedProvider, CancellationToken ct)
    {
        var logger = scopedProvider.GetRequiredService<IGlobalLogger>();
        var orchestrator = scopedProvider.GetRequiredService<SyncProjectOrchestrator>();

        var projectId = job.RelatedEntityId
            ?? throw new InvalidOperationException("SyncProject job missing RelatedEntityId (ProjectId)");

        var triggerType = ExtractPayload(job, "TriggerType") ?? "Scheduled";
        var triggeredBy = ExtractPayload(job, "TriggeredBy") ?? "System";

        logger.LogInformation("SyncProjectJobHandler: executing sync for project {ProjectId} (trigger: {TriggerType})",
            projectId, triggerType);

        await orchestrator.ExecuteSyncProjectAsync(projectId, triggerType, triggeredBy, ct);
    }

    private static string? ExtractPayload(JobQueueEntry job, string key)
    {
        if (string.IsNullOrEmpty(job.PayloadJson)) return null;
        try
        {
            var doc = System.Text.Json.JsonDocument.Parse(job.PayloadJson);
            return doc.RootElement.TryGetProperty(key, out var val) ? val.GetString() : null;
        }
        catch { return null; }
    }
}
