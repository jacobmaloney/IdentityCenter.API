using DataAccessLibrary.Models;

namespace DataAccessLibrary.Repositories;

public interface IApiKeyRepository
{
    Task<ApiKeyValidationResult> ValidateApiKeyAsync(string apiKey, string clientIp);
    Task<(Guid KeyId, string ApiKey)> CreateApiKeyAsync(string name, string keyType, string scopes, Guid? agentId = null, string? userId = null, DateTime? expiresAt = null, string createdBy = "System");
    Task<bool> RevokeApiKeyAsync(Guid keyId, string reason);
    Task<List<ApiKey>> GetApiKeysAsync(string? keyType = null);

    /// <summary>
    /// Re-points an existing API key at a different agent. Used by agent
    /// registration when a key minted against a provisional agent Id must be
    /// linked to the surviving agent on re-registration by name.
    /// </summary>
    Task<bool> UpdateApiKeyAgentAsync(Guid keyId, Guid agentId);

    /// <summary>
    /// Computes the at-rest hash of a plaintext API key using the same algorithm
    /// the repository uses internally (SHA-256, lowercase hex). Callers that must
    /// store the hash alongside another record (e.g. RemoteAgents.ApiKeyHash) use
    /// this so generation and verification stay in agreement. Never persist the
    /// plaintext — only this hash.
    /// </summary>
    string HashApiKey(string apiKey);
}
