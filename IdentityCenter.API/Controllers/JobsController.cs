using DataAccessLibrary.Models;
using DataAccessLibrary.Repositories;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace IdentityCenter.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class JobsController : ControllerBase
{
    private readonly IJobQueueRepository _jobQueue;
    private readonly IAgentRepository _agentRepo;
    private readonly ILogger<JobsController> _logger;

    public JobsController(
        IJobQueueRepository jobQueue,
        IAgentRepository agentRepo,
        ILogger<JobsController> logger)
    {
        _jobQueue = jobQueue;
        _agentRepo = agentRepo;
        _logger = logger;
    }

    /// <summary>
    /// Claims the next available job for an agent.
    /// Uses atomic row locking to prevent race conditions.
    /// </summary>
    [HttpPost("claim")]
    [Authorize(Policy = "AgentPolicy")]
    public async Task<ActionResult<ClaimJobResponse>> ClaimJob([FromBody] ClaimJobRequest request)
    {
        try
        {
            // Verify the agent ID matches the authenticated agent
            var authenticatedAgentId = GetAgentIdFromClaims();
            if (authenticatedAgentId != request.AgentId)
            {
                return Unauthorized(new ClaimJobResponse
                {
                    Success = false,
                    Message = "Agent ID mismatch"
                });
            }

            // Verify agent exists and is enabled
            var agent = await _agentRepo.GetAgentByIdAsync(request.AgentId);
            if (agent == null || !agent.IsEnabled)
            {
                return BadRequest(new ClaimJobResponse
                {
                    Success = false,
                    Message = "Agent not found or disabled"
                });
            }

            // Check if agent can accept more jobs
            if (agent.CurrentJobCount >= agent.MaxConcurrentJobs)
            {
                return Ok(new ClaimJobResponse
                {
                    Success = false,
                    Message = "Agent at maximum concurrent job capacity"
                });
            }

            // Claim a job
            var job = await _jobQueue.ClaimNextJobAsync(request.AgentId, request.SupportedJobTypes);

            if (job == null)
            {
                return Ok(new ClaimJobResponse
                {
                    Success = false,
                    Message = "No jobs available"
                });
            }

            return Ok(new ClaimJobResponse
            {
                Success = true,
                Job = job
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error claiming job for agent {AgentId}", request.AgentId);
            return StatusCode(500, new ClaimJobResponse
            {
                Success = false,
                Message = "Internal server error"
            });
        }
    }

    /// <summary>
    /// Updates the progress of a running job.
    /// </summary>
    [HttpPost("{jobId}/progress")]
    [Authorize(Policy = "AgentPolicy")]
    public async Task<ActionResult> UpdateProgress(Guid jobId, [FromBody] JobProgressUpdate update)
    {
        try
        {
            // Verify job exists and is owned by this agent
            var job = await _jobQueue.GetJobByIdAsync(jobId);
            if (job == null)
            {
                return NotFound(new { error = "Job not found" });
            }

            var authenticatedAgentId = GetAgentIdFromClaims();
            if (job.ClaimedByAgentId != authenticatedAgentId)
            {
                return Unauthorized(new { error = "Job not owned by this agent" });
            }

            await _jobQueue.UpdateJobProgressAsync(
                jobId,
                update.ProgressPercent,
                update.ProgressMessage,
                update.ItemsProcessed,
                update.ItemsSucceeded,
                update.ItemsFailed);

            return Ok(new { message = "Progress updated" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating progress for job {JobId}", jobId);
            return StatusCode(500, new { error = "Internal server error" });
        }
    }

    /// <summary>
    /// Completes a job (success or failure).
    /// </summary>
    [HttpPost("{jobId}/complete")]
    [Authorize(Policy = "AgentPolicy")]
    public async Task<ActionResult> CompleteJob(Guid jobId, [FromBody] CompleteJobRequest request)
    {
        try
        {
            // Verify job exists and is owned by this agent
            var job = await _jobQueue.GetJobByIdAsync(jobId);
            if (job == null)
            {
                return NotFound(new { error = "Job not found" });
            }

            var authenticatedAgentId = GetAgentIdFromClaims();
            if (job.ClaimedByAgentId != authenticatedAgentId)
            {
                return Unauthorized(new { error = "Job not owned by this agent" });
            }

            await _jobQueue.CompleteJobAsync(
                jobId,
                request.Success,
                request.ItemsProcessed,
                request.ItemsSucceeded,
                request.ItemsFailed,
                request.ErrorMessage,
                request.ResultJson);

            return Ok(new { message = request.Success ? "Job completed successfully" : "Job failed" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error completing job {JobId}", jobId);
            return StatusCode(500, new { error = "Internal server error" });
        }
    }

    /// <summary>
    /// Gets job details by ID.
    /// </summary>
    [HttpGet("{jobId}")]
    public async Task<ActionResult<JobQueueEntry>> GetJob(Guid jobId)
    {
        var job = await _jobQueue.GetJobByIdAsync(jobId);
        if (job == null)
        {
            return NotFound(new { error = "Job not found" });
        }

        return Ok(job);
    }

    /// <summary>
    /// Gets pending jobs in the queue.
    /// </summary>
    [HttpGet("pending")]
    public async Task<ActionResult<List<JobQueueEntry>>> GetPendingJobs([FromQuery] int limit = 50)
    {
        var jobs = await _jobQueue.GetPendingJobsAsync(limit);
        return Ok(jobs);
    }

    /// <summary>
    /// Gets a summary of the job queue status.
    /// </summary>
    [HttpGet("summary")]
    public async Task<ActionResult<JobQueueSummary>> GetQueueSummary()
    {
        var summary = await _jobQueue.GetQueueSummaryAsync();
        return Ok(summary);
    }

    /// <summary>
    /// Queues a new job.
    /// </summary>
    [HttpPost]
    [Authorize(Policy = "AdminPolicy")]
    public async Task<ActionResult<Guid>> QueueJob([FromBody] JobQueueEntry job)
    {
        try
        {
            var jobId = await _jobQueue.QueueJobAsync(job);
            return CreatedAtAction(nameof(GetJob), new { jobId }, new { jobId });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error queueing job");
            return StatusCode(500, new { error = "Internal server error" });
        }
    }

    /// <summary>
    /// Cancels a pending job.
    /// </summary>
    [HttpDelete("{jobId}")]
    [Authorize(Policy = "AdminPolicy")]
    public async Task<ActionResult> CancelJob(Guid jobId)
    {
        var cancelled = await _jobQueue.CancelJobAsync(jobId);
        if (!cancelled)
        {
            return BadRequest(new { error = "Job cannot be cancelled (may already be processing or completed)" });
        }

        return Ok(new { message = "Job cancelled" });
    }

    /// <summary>
    /// Releases stale jobs back to pending status.
    /// </summary>
    [HttpPost("release-stale")]
    [Authorize(Policy = "AdminPolicy")]
    public async Task<ActionResult> ReleaseStaleJobs([FromQuery] int staleMinutes = 30)
    {
        await _jobQueue.ReleaseStaleJobsAsync(staleMinutes);
        return Ok(new { message = "Stale jobs released" });
    }

    private Guid? GetAgentIdFromClaims()
    {
        var agentIdClaim = User.FindFirst("agent_id")?.Value;
        if (Guid.TryParse(agentIdClaim, out var agentId))
        {
            return agentId;
        }
        return null;
    }
}
