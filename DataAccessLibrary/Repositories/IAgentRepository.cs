using DataAccessLibrary.Models;

namespace DataAccessLibrary.Repositories;

public interface IAgentRepository
{
    Task<RemoteAgent?> GetAgentByIdAsync(Guid agentId);
    Task<List<RemoteAgentStatus>> GetAgentStatusesAsync();
    /// <summary>
    /// Registers a new agent (or re-registers an existing one by AgentName).
    /// <paramref name="apiKeyHash"/> is the at-rest hash of the agent's API key
    /// (from <see cref="IApiKeyRepository.HashApiKey"/>); it is stored in the
    /// NOT-NULL RemoteAgents.ApiKeyHash column. The plaintext key is never passed
    /// here and is returned to the caller exactly once by the registration endpoint.
    /// </summary>
    Task<Guid> RegisterAgentAsync(RemoteAgent agent, string apiKeyHash);
    Task UpdateHeartbeatAsync(AgentHeartbeat heartbeat);
    Task<bool> UpdateAgentStatusAsync(Guid agentId, string status);
    Task<List<RemoteAgent>> GetOnlineAgentsAsync();
}
