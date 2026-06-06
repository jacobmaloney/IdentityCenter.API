using IdentityCenter.API.Models;
using Logging;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace IdentityCenter.API.Controllers;

[ApiController]
[Route("api/discovery")]
[Authorize(Policy = "AgentPolicy")]
public class DiscoveryController : ControllerBase
{
    private readonly IGlobalLogger _logger;

    public DiscoveryController(IGlobalLogger logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Agent pushes a batch of discovered AD objects (users, groups, computers).
    /// Lightweight push path — full sync still goes through SyncProject orchestration.
    /// </summary>
    [HttpPost("objects")]
    public async Task<IActionResult> ReceiveObjects([FromBody] AgentObjectDiscoveryPayload payload)
    {
        var agentId = User.FindFirst("agent_id")?.Value;
        var count = payload?.Objects?.Count ?? 0;

        _logger.LogInformation("Object discovery from agent {AgentId}: {Count} objects (connection: {ConnId})",
            agentId, count, payload?.ConnectionId);

        // TODO: Bulk upsert discovered objects into Objects + ObjectAttributes tables

        return Ok(new { status = "accepted", count });
    }

    /// <summary>
    /// Agent pushes results from a network scan it ran.
    /// </summary>
    [HttpPost("network-scan-result")]
    public async Task<IActionResult> ReceiveNetworkScanResult([FromBody] AgentNetworkScanPayload payload)
    {
        var agentId = User.FindFirst("agent_id")?.Value;
        var openPorts = payload?.Results?.Count(r => r.IsOpen) ?? 0;

        _logger.LogInformation("Network scan from agent {AgentId}: {Range}, {Open} open ports found",
            agentId, payload?.CidrRange, openPorts);

        // TODO: For each open-port result matching SQL ports (1433, 1434): upsert into inventory

        return Ok(new { status = "accepted", totalScanned = payload?.Results?.Count ?? 0, openPorts });
    }
}
