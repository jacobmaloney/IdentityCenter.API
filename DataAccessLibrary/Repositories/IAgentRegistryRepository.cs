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

    /// <summary>
    /// Idempotent find-or-create keyed on a CALLER-SUPPLIED Id — used when a Conduit
    /// installation's durable instance GUID becomes its agent identity at enrollment, so
    /// the minted key's ApiKeys.AgentId equals the provenance id stamped on
    /// Objects.SourceJobServerId. Mirrors the ObjectsController auto-register semantics:
    /// inserts only when absent; if a row already exists (e.g. Phase-C auto-registered it
    /// from a prior bulk push), returns it UNCHANGED — never duplicates, never flips
    /// IsActive, never overwrites Name. New rows default to IsActive=0: activation stays a
    /// separate, deliberate admin act.
    /// </summary>
    Task<Agent> CreateOrGetWithIdAsync(Guid id, string name, string? location, string? capabilities, bool active = false);

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
