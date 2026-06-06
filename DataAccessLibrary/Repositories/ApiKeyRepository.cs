using System.Security.Cryptography;
using Dapper;
using DataAccessLibrary.Models;
using Logging;
using Microsoft.Extensions.Configuration;

namespace DataAccessLibrary.Repositories;

public class ApiKeyRepository : DapperRepositoryBase, IApiKeyRepository
{
    public ApiKeyRepository(IConfiguration configuration, IGlobalLogger logger)
        : base(configuration, logger) { }

    public async Task<ApiKeyValidationResult> ValidateApiKeyAsync(string apiKey, string clientIp)
    {
        var keyHash = HashApiKey(apiKey);
        var keyPrefix = apiKey.Length >= 8 ? apiKey.Substring(0, 8) : apiKey;

        const string sql = @"
            SELECT
                Id, Name, KeyType, Scopes, AgentId, UserId,
                IsEnabled, ExpiresAt, RevokedAt
            FROM ApiKeys
            WHERE KeyHash = @KeyHash
              AND KeyPrefix = @KeyPrefix
        ";

        return await ExecuteAsync(async connection =>
        {
            var key = await connection.QuerySingleOrDefaultAsync<ApiKey>(sql, new { KeyHash = keyHash, KeyPrefix = keyPrefix });

            if (key == null)
                return new ApiKeyValidationResult { IsValid = false, FailureReason = "Invalid API key" };

            if (!key.IsEnabled)
                return new ApiKeyValidationResult { IsValid = false, FailureReason = "API key is disabled" };

            if (key.RevokedAt.HasValue)
                return new ApiKeyValidationResult { IsValid = false, FailureReason = "API key has been revoked" };

            if (key.ExpiresAt.HasValue && key.ExpiresAt.Value < DateTime.UtcNow)
                return new ApiKeyValidationResult { IsValid = false, FailureReason = "API key has expired" };

            // Update usage statistics
            const string updateSql = @"
                UPDATE ApiKeys
                SET LastUsedAt = GETUTCDATE(),
                    LastUsedFromIp = @ClientIp,
                    UsageCount = UsageCount + 1
                WHERE Id = @KeyId
            ";

            await connection.ExecuteAsync(updateSql, new { KeyId = key.Id, ClientIp = clientIp });

            return new ApiKeyValidationResult
            {
                IsValid = true,
                KeyId = key.Id,
                KeyName = key.Name,
                KeyType = key.KeyType,
                Scopes = key.Scopes,
                AgentId = key.AgentId,
                UserId = key.UserId
            };
        });
    }

    public async Task<(Guid KeyId, string ApiKey)> CreateApiKeyAsync(
        string name, string keyType, string scopes,
        Guid? agentId = null, string? userId = null,
        DateTime? expiresAt = null, string createdBy = "System")
    {
        var apiKey = GenerateApiKey();
        var keyHash = HashApiKey(apiKey);
        var keyPrefix = apiKey.Substring(0, 8);

        var key = new ApiKey
        {
            Id = Guid.NewGuid(),
            Name = name,
            KeyHash = keyHash,
            KeyPrefix = keyPrefix,
            KeyType = keyType,
            AgentId = agentId,
            UserId = userId,
            Scopes = scopes,
            IsEnabled = true,
            ExpiresAt = expiresAt,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = createdBy
        };

        const string sql = @"
            INSERT INTO ApiKeys (
                Id, Name, KeyHash, KeyPrefix, KeyType, AgentId, UserId,
                Scopes, IsEnabled, ExpiresAt, CreatedAt, CreatedBy, UsageCount
            )
            VALUES (
                @Id, @Name, @KeyHash, @KeyPrefix, @KeyType, @AgentId, @UserId,
                @Scopes, @IsEnabled, @ExpiresAt, @CreatedAt, @CreatedBy, 0
            )
        ";

        await ExecuteNonQueryAsync(async connection =>
            await connection.ExecuteAsync(sql, key));

        _logger.LogInformation("Created API key {KeyId} ({KeyType}) for {Name}", key.Id, keyType, name);

        return (key.Id, apiKey);
    }

    public async Task<bool> RevokeApiKeyAsync(Guid keyId, string reason)
    {
        const string sql = @"
            UPDATE ApiKeys
            SET RevokedAt = GETUTCDATE(),
                RevokedReason = @Reason,
                IsEnabled = 0
            WHERE Id = @KeyId
              AND RevokedAt IS NULL
        ";

        var affected = await ExecuteAsync(async connection =>
            await connection.ExecuteAsync(sql, new { KeyId = keyId, Reason = reason }));

        if (affected > 0)
        {
            _logger.LogWarning("API key {KeyId} revoked: {Reason}", keyId, reason);
        }

        return affected > 0;
    }

    public async Task<bool> UpdateApiKeyAgentAsync(Guid keyId, Guid agentId)
    {
        const string sql = @"
            UPDATE ApiKeys
            SET AgentId = @AgentId
            WHERE Id = @KeyId
        ";

        var affected = await ExecuteAsync(async connection =>
            await connection.ExecuteAsync(sql, new { KeyId = keyId, AgentId = agentId }));

        return affected > 0;
    }

    public async Task<List<ApiKey>> GetApiKeysAsync(string? keyType = null)
    {
        var sql = @"
            SELECT
                Id, Name, KeyPrefix, KeyType, AgentId, UserId, Scopes,
                IsEnabled, ExpiresAt, CreatedAt, CreatedBy,
                LastUsedAt, LastUsedFromIp, UsageCount, RevokedAt, RevokedReason
            FROM ApiKeys
        ";

        if (!string.IsNullOrEmpty(keyType))
        {
            sql += " WHERE KeyType = @KeyType";
        }

        sql += " ORDER BY CreatedAt DESC";

        return await ExecuteAsync(async connection =>
        {
            var keys = await connection.QueryAsync<ApiKey>(sql, new { KeyType = keyType });
            return keys.ToList();
        });
    }

    private static string GenerateApiKey()
    {
        var bytes = new byte[32];
        using var rng = RandomNumberGenerator.Create();
        rng.GetBytes(bytes);
        return "ic_" + Convert.ToHexString(bytes).ToLower();
    }

    public string HashApiKey(string apiKey)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(apiKey);
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash).ToLower();
    }
}
