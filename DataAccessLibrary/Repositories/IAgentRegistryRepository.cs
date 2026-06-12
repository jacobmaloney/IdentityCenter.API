using DataAccessLibrary.Models;

namespace DataAccessLibrary.Repositories;

/// <summary>
/// Registry of remote agent installations (V140 Agents). Enrollment is Flow A
/// only: an admin pre-registers the agent here, mints a per-agent API key
/// (ApiKeys.AgentId), and pastes the key into the agent. There is no
/// self-enroll path. Named "registry" because IAgentRepository is already
/// taken by the execution-server RemoteAgents channel.
/// </summary>
public interface IAgentRegistryRepository
{
    Task<Guid> CreateAsync(string name, string? location);
    Task<Agent?> GetByIdAsync(Guid id);
    Task<List<Agent>> GetAllAsync();
    Task<List<Agent>> GetActiveAsync();
    Task<bool> SetActiveAsync(Guid id, bool isActive);

    /// <summary>True when at least one active agent is registered — the signal that
    /// new commands must be targeted (no more NULL-target broadcasts).</summary>
    Task<bool> AnyActiveAsync();

    /// <summary>
    /// UPDATE-only heartbeat: stamps Version/Capabilities/LastSeenAt/LastSeenFromIp
    /// on an ACTIVE agent row and returns it. Never inserts. Null = unknown or
    /// deactivated agent (callers return a uniform 404).
    /// </summary>
    Task<Agent?> HeartbeatAsync(Guid id, string? version, string? capabilitiesJson, string? fromIp);
}
