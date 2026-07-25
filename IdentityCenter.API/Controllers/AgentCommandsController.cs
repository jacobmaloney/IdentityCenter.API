using DataAccessLibrary.Repositories;
using IdentityCenter.API.Authentication;
using IdentityCenter.API.Models;
using Logging;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace IdentityCenter.API.Controllers;

/// <summary>
/// Command channel for remote scan agents (Conduit's IcAgentCommandPollerService).
///
/// Targeted path (per-agent key carrying an agent_id claim; AgentChannelCommandsPolicy,
/// scope agent:commands — a heartbeat-only key cannot claim or complete):
///   POST /api/agent/commands/claim          -> atomically claims this agent's pending
///                                              commands (Pending -> Acked). Payloads are
///                                              visible ONLY through this claim.
///   POST /api/agent/commands/{id}/complete  -> { success, message }; guarded to the
///                                              claiming agent. Zero rows -> uniform 404.
///
/// Legacy untargeted path (shared key WITHOUT agent_id; TenantDataPolicy; gated by
/// AgentCommands:AllowLegacyUntargeted, default true; sees TargetAgentId IS NULL only):
///   GET  /api/agent/commands/pending        -> [{ id, commandType, payloadJson }]
///   POST /api/agent/commands/{id}/ack       -> Pending -> Acked; success ONLY when this
///                                              caller won the transition (no double-run)
///   POST /api/agent/commands/{id}/complete  -> untargeted rows, from Acked only
///
/// A key WITH an agent_id claim may NOT use the legacy endpoints (403): one key,
/// one path. Agent identity is read EXCLUSIVELY from the agent_id claim — never
/// from query/route/body/header.
/// </summary>
[ApiController]
[Route("api/agent/commands")]
public class AgentCommandsController : ControllerBase
{
    private readonly IAgentCommandRepository _commands;
    private readonly IConfiguration _configuration;
    private readonly IGlobalLogger _logger;

    public AgentCommandsController(IAgentCommandRepository commands, IConfiguration configuration, IGlobalLogger logger)
    {
        _commands = commands;
        _configuration = configuration;
        _logger = logger;
    }

    private Guid? AgentId =>
        Guid.TryParse(User.FindFirst("agent_id")?.Value, out var id) ? id : null;

    private string KeyId =>
        User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? "unknown";

    private string CallerIp => ClientIp.Resolve(HttpContext, _configuration);

    private bool LegacyUntargetedAllowed =>
        _configuration.GetValue("AgentCommands:AllowLegacyUntargeted", true);

    /// <summary>
    /// Atomically claims up to {max} (1-10) of this agent's pending commands.
    /// Claiming is a state change, hence POST. The claimed payloads are returned
    /// exactly once, here.
    /// </summary>
    [HttpPost("claim")]
    [Authorize(Policy = "AgentChannelCommandsPolicy")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Claim([FromBody] AgentCommandClaimRequest? request)
    {
        // The policy guarantees the claim EXISTS; it does not guarantee it parses.
        if (AgentId is not { } agentId)
            return StatusCode(StatusCodes.Status403Forbidden, new { error = "agent_id claim is missing or malformed" });
        var max = Math.Clamp(request?.Max ?? 10, 1, 10);

        var claimed = await _commands.ClaimAsync(agentId, max);
        if (claimed.Count > 0)
            _logger.LogInformation("AgentCommands: agent {AgentId} (key {KeyId}, ip {Ip}) claimed {Count} command(s): {Ids}",
                agentId, KeyId, CallerIp, claimed.Count, string.Join(",", claimed.Select(c => c.Id)));

        return Ok(claimed.Select(c => new
        {
            id = c.Id,
            commandType = c.CommandType,
            payloadJson = c.PayloadJson
        }));
    }

    /// <summary>Legacy: pending UNTARGETED commands, oldest first, capped at 10.</summary>
    [HttpGet("pending")]
    [Authorize(Policy = "TenantDataPolicy")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetPending()
    {
        if (AgentId is not null)
            return StatusCode(StatusCodes.Status403Forbidden, new { error = "Per-agent keys must use POST /api/agent/commands/claim" });
        if (!LegacyUntargetedAllowed)
            return NotFound();

        var pending = await _commands.GetPendingAsync(10);
        return Ok(pending.Select(c => new
        {
            id = c.Id,
            commandType = c.CommandType,
            payloadJson = c.PayloadJson
        }));
    }

    /// <summary>
    /// Legacy claim of an UNTARGETED command (Pending -> Acked). Succeeds only for
    /// the caller that actually won the transition — a lost race is a 404, so two
    /// pollers can no longer both run the same command.
    /// </summary>
    [HttpPost("{id:guid}/ack")]
    [Authorize(Policy = "TenantDataPolicy")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Ack(Guid id)
    {
        if (AgentId is not null)
            return StatusCode(StatusCodes.Status403Forbidden, new { error = "Per-agent keys must use POST /api/agent/commands/claim" });
        if (!LegacyUntargetedAllowed)
            return NotFound();

        var transitioned = await _commands.AckAsync(id);
        if (!transitioned)
            return NotFound(new { error = "Unknown command id" });

        _logger.LogWarning("AgentCommands: LEGACY untargeted claim of command {Id} by key {KeyId} from {Ip} — migrate this poller to a per-agent key",
            id, KeyId, CallerIp);
        return Ok(new { status = "Acked" });
    }

    /// <summary>
    /// Reports the run outcome ({success, message} -> Completed/Failed). Per-agent
    /// keys may complete only commands they claimed; legacy keys only untargeted
    /// commands. Anything else is a uniform 404 — no existence oracle.
    /// </summary>
    [HttpPost("{id:guid}/complete")]
    [Authorize(Policy = "AgentCommandsCompletePolicy")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Complete(Guid id, [FromBody] AgentCommandCompleteRequest? request)
    {
        if (request is null)
            return BadRequest(new { error = "Body { success, message } is required" });

        // Reject (not truncate) an oversize structured result at the boundary, so it surfaces as a 400
        // rather than an unhandled 500 from the repository's hard cap.
        if (request.ResultJson is not null &&
            System.Text.Encoding.UTF8.GetByteCount(request.ResultJson) > 64 * 1024)
            return BadRequest(new { error = "resultJson exceeds the 64KB limit." });

        var agentId = AgentId;
        // A key CARRYING an agent_id claim that fails to parse must not slide
        // into the legacy untargeted path.
        if (agentId is null && User.FindFirst("agent_id") is not null)
            return StatusCode(StatusCodes.Status403Forbidden, new { error = "agent_id claim is malformed" });
        var transitioned = agentId is not null
            ? await _commands.CompleteClaimedAsync(id, agentId.Value, request.Success, request.Message, request.ResultJson)
            : await _commands.CompleteAsync(id, request.Success, request.Message, request.ResultJson);
        if (!transitioned)
            return NotFound(new { error = "Unknown command id" });

        _logger.LogInformation("AgentCommands: command {Id} completed (success={Success}) by agent {AgentId} (key {KeyId}, ip {Ip})",
            id, request.Success, agentId?.ToString() ?? "legacy", KeyId, CallerIp);
        return Ok(new { status = request.Success ? "Completed" : "Failed" });
    }
}
