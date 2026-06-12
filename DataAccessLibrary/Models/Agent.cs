namespace DataAccessLibrary.Models;

/// <summary>
/// A registered remote agent installation (V140 Agents). Admin pre-registers the
/// agent (name + location) and mints a per-agent API key (ApiKeys.AgentId -> Id);
/// the agent reports Version/Capabilities/LastSeenAt via POST /api/agent/heartbeat.
/// Distinct from the execution-server RemoteAgents channel.
/// </summary>
public class Agent
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public string? Location { get; set; }

    /// <summary>JSON array of allow-listed capability strings, e.g. ["SqlDiscovery"].</summary>
    public string? Capabilities { get; set; }

    public string? Version { get; set; }
    public DateTime? LastSeenAt { get; set; }
    public string? LastSeenFromIp { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; }
}
