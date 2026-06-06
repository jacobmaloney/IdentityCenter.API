using DataAccessLibrary.Models;
using DataAccessLibrary.Repositories;
using Logging;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace IdentityCenter.API.Controllers;

/// <summary>
/// Public API for triggering sync project runs and reading their status.
/// </summary>
[ApiController]
[Route("api/sync")]
[Authorize(Policy = "TenantDataPolicy")]
public class SyncController : ControllerBase
{
    private readonly IJobQueueRepository _jobQueue;
    private readonly ISyncExecutionRepository _syncExec;
    private readonly IGlobalLogger _logger;

    public SyncController(
        IJobQueueRepository jobQueue,
        ISyncExecutionRepository syncExec,
        IGlobalLogger logger)
    {
        _jobQueue = jobQueue;
        _syncExec = syncExec;
        _logger = logger;
    }

    /// <summary>
    /// Triggers an immediate run of the given sync project by enqueuing a
    /// SyncProject job. The WebPortal's Quartz scheduler picks the job up out
    /// of the same JobQueue table that backs the distributed agent queue.
    /// </summary>
    [HttpPost("{projectId:guid}/run")]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> RunSyncProject(Guid projectId)
    {
        if (projectId == Guid.Empty)
            return BadRequest(new { error = "projectId is required" });

        try
        {
            var entry = new JobQueueEntry
            {
                JobType = "SyncProject",
                JobName = "API trigger: SyncProject " + projectId,
                RelatedEntityId = projectId,
                RelatedEntityType = "SyncProject",
                Status = "Pending",
                CreatedAt = DateTime.UtcNow,
                CreatedBy = User.Identity?.Name ?? "API",
                PayloadJson = "{\"triggerType\":\"API\"}"
            };

            var runId = await _jobQueue.QueueJobAsync(entry);

            _logger.LogInformation("API: Sync project {ProjectId} queued as job {RunId}", projectId, runId);

            return Accepted(new
            {
                runId,
                projectId,
                status = "Queued",
                message = "Sync project queued. Poll GET /api/sync/{projectId}/status/{runId} for progress."
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to queue sync project run for {ProjectId}", projectId);
            return StatusCode(500, new { error = "Failed to queue sync project run" });
        }
    }

    /// <summary>
    /// Returns the status of a queued or running sync. The returned shape
    /// merges the JobQueue row (queue position / picked up by) with the
    /// SyncProjectRun row (objects processed, errors) so callers see one
    /// consolidated view regardless of where the job is in its lifecycle.
    /// </summary>
    [HttpGet("{projectId:guid}/status/{runId:guid}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetSyncStatus(Guid projectId, Guid runId)
    {
        if (projectId == Guid.Empty || runId == Guid.Empty)
            return BadRequest(new { error = "projectId and runId are required" });

        try
        {
            // Job queue row — present while queued or being claimed by a worker.
            var jobRow = await _jobQueue.GetJobByIdAsync(runId);

            // Sync run row — present once the orchestrator has begun processing.
            // The run-detail row may have a different Id than the job-queue row
            // (the orchestrator creates its own run record) so we also fall back
            // to the latest run for the project as a hint for callers.
            var runDetails = await _syncExec.GetSyncRunDetailsAsync(runId);
            if (runDetails == null)
            {
                var latest = await _syncExec.GetLatestRunForProjectAsync(projectId);
                if (latest != null && latest.Id == runId)
                {
                    runDetails = await _syncExec.GetSyncRunDetailsAsync(latest.Id);
                }
            }

            if (jobRow == null && runDetails == null)
                return NotFound(new { error = "No queued job or sync run matched the supplied projectId/runId pair." });

            var status = runDetails?.Run?.Status ?? jobRow?.Status ?? "Unknown";
            var progress = runDetails?.Run?.ProgressPercentage ?? jobRow?.ProgressPercent ?? 0;
            var objectsProcessed = runDetails?.Run?.TotalObjectsProcessed ?? jobRow?.ItemsProcessed ?? 0;
            var errors = runDetails?.Run?.TotalErrors ?? jobRow?.ItemsFailed ?? 0;

            return Ok(new
            {
                projectId,
                runId,
                status,
                progress,
                objectsProcessed,
                errors,
                queueState = jobRow == null ? null : new
                {
                    jobRow.Status,
                    queuedAt = jobRow.CreatedAt,
                    jobRow.StartedAt,
                    jobRow.CompletedAt,
                    jobRow.ClaimedByAgentId
                },
                runState = runDetails?.Run == null ? null : new
                {
                    runDetails.Run.Status,
                    runDetails.Run.StartedAt,
                    runDetails.Run.CompletedAt,
                    runDetails.Run.ProgressPercentage,
                    runDetails.Run.TotalObjectsProcessed,
                    runDetails.Run.TotalErrors,
                    runDetails.Run.ErrorMessage
                }
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get sync status (project={ProjectId}, run={RunId})", projectId, runId);
            return StatusCode(500, new { error = "Failed to get sync status" });
        }
    }
}
