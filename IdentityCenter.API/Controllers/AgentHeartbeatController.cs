using System.Collections.Concurrent;
using System.Text.Json;
using DataAccessLibrary.Repositories;
using IdentityCenter.API.Authentication;
using IdentityCenter.API.Models;
using Logging;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace IdentityCenter.API.Controllers;

/// <summary>
/// POST /api/agent/heartbeat — a registered agent reports {version, capabilities[]}.
///
/// Identity comes EXCLUSIVELY from the agent_id claim on the per-agent key
/// (AgentChannelHeartbeatPolicy, scope agent:heartbeat — a heartbeat-only key cannot
/// claim or complete commands). UPDATE-only against the V140 Agents registry: a heartbeat
/// can never create or reactivate a registration (enrollment is admin-driven Flow A).
/// Capabilities are validated against the server-side allow-list; unknown entries
/// are dropped. Response echoes the server-side identity so the agent can display
/// what it is enrolled as.
/// </summary>
[ApiController]
[Route("api/agent")]
public class AgentHeartbeatController : ControllerBase
{
    /// <summary>Server-side capability allow-list. Anything else is dropped, never stored.</summary>
    private static readonly string[] AllowedCapabilities =
    {
        "AdIdentitySync",
        "SqlDiscovery",
        "ScimProvisioning"
    };

    private const int MaxCapabilityEntries = 32;
    private const int MaxVersionLength = 64;
    private static readonly TimeSpan MinHeartbeatInterval = TimeSpan.FromSeconds(10);

    /// <summary>In-memory per-agent rate limiter (last accepted heartbeat, UTC).</summary>
    private static readonly ConcurrentDictionary<Guid, DateTime> LastHeartbeatUtc = new();

    private readonly IAgentRegistryRepository _agents;
    private readonly IConfiguration _configuration;
    private readonly IGlobalLogger _logger;

    public AgentHeartbeatController(IAgentRegistryRepository agents, IConfiguration configuration, IGlobalLogger logger)
    {
        _agents = agents;
        _configuration = configuration;
        _logger = logger;
    }

    [HttpPost("heartbeat")]
    [Authorize(Policy = "AgentChannelHeartbeatPolicy")]
    [RequestSizeLimit(16 * 1024)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> Heartbeat([FromBody] AgentHeartbeatRequest? request)
    {
        // The policy guarantees the claim EXISTS; it does not guarantee it parses.
        if (!Guid.TryParse(User.FindFirst("agent_id")?.Value, out var agentId))
            return StatusCode(StatusCodes.Status403Forbidden, new { error = "agent_id claim is missing or malformed" });

        var now = DateTime.UtcNow;
        var last = LastHeartbeatUtc.GetValueOrDefault(agentId);
        if (now - last < MinHeartbeatInterval)
        {
            _logger.LogInformation("Agent heartbeat rate-limited for agent {AgentId} from {Ip}",
                agentId, ClientIp.Resolve(HttpContext, _configuration));
            return StatusCode(StatusCodes.Status429TooManyRequests, new { error = "Heartbeat interval too short" });
        }
        LastHeartbeatUtc[agentId] = now;

        var version = request?.Version is { Length: > MaxVersionLength } v
            ? v[..MaxVersionLength]
            : request?.Version;

        // Null = "no change": an absent capabilities list, or one with no allow-listed
        // entries left after filtering, must never wipe a previously stored value.
        string? capabilitiesJson = null;
        if (request?.Capabilities is { Count: > 0 } caps)
        {
            var validated = caps
                .Take(MaxCapabilityEntries)
                .Where(c => AllowedCapabilities.Contains(c, StringComparer.Ordinal))
                .Distinct(StringComparer.Ordinal)
                .ToList();
            if (validated.Count < Math.Min(caps.Count, MaxCapabilityEntries) || caps.Count > MaxCapabilityEntries)
                _logger.LogInformation("Agent heartbeat from {AgentId} carried unknown/excess capabilities; dropped to {Kept} allow-listed entries",
                    agentId, validated.Count);
            if (validated.Count > 0)
                capabilitiesJson = JsonSerializer.Serialize(validated);
        }

        var ip = ClientIp.Resolve(HttpContext, _configuration);
        var agent = await _agents.HeartbeatAsync(agentId, version, capabilitiesJson, ip);
        if (agent is null)
            return NotFound(new { error = "Unknown agent" }); // unregistered or deactivated — uniform

        _logger.LogDebug("Agent heartbeat: {AgentId} ({Name}) v{Version} from {Ip}", agentId, agent.Name, version, ip);

        return Ok(new
        {
            agentId = agent.Id,
            name = agent.Name,
            location = agent.Location
        });
    }
}
