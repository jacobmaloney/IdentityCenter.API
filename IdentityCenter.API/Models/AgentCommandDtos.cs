namespace IdentityCenter.API.Models;

/// <summary>
/// Body of POST /api/agent/commands/{id}/complete. The agent reports the run
/// outcome; nothing else from the body is trusted.
/// </summary>
public class AgentCommandCompleteRequest
{
    public bool Success { get; set; }
    public string? Message { get; set; }
}

/// <summary>
/// Body of POST /api/agent/commands/claim. The claiming agent's identity comes
/// EXCLUSIVELY from the agent_id claim on its API key — never from the body.
/// </summary>
public class AgentCommandClaimRequest
{
    public int Max { get; set; } = 10;
}

/// <summary>
/// Body of POST /api/agent/heartbeat. Identity comes from the agent_id claim;
/// capabilities are validated against the server-side allow-list and unknown
/// entries are dropped.
/// </summary>
public class AgentHeartbeatRequest
{
    public string? Version { get; set; }
    public List<string>? Capabilities { get; set; }
}
