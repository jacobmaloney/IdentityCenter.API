using System.Security.Cryptography;
using System.Text;
using Dapper;
using Logging;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;

namespace DataAccessLibrary.ControlPlane;

/// <summary>
/// Dapper implementation of <see cref="ITenantApiKeyRepository"/> over the control-plane
/// <c>TenantApiKeys</c> table.
///
/// Reads the <c>ControlPlane</c> connection string (NOT <c>DefaultConnection</c>) — like the tenant
/// registry, this is control-plane data and must never live in a tenant DB.
///
/// HASHING: identical scheme to IC's existing <c>ApiKeyRepository</c> — raw key is
/// <c>ic_</c> + 32 random bytes (hex); stored value is the lowercase-hex SHA-256 of the UTF-8 raw key.
/// Reusing the scheme verbatim means a single hashing contract across the product.
///
/// LOOKUP / TIMING: validation matches on the stored SHA-256 hash via an indexed equality predicate.
/// The secret being hashed is 256 bits of CSPRNG entropy, so the hash is not guessable and a per-byte
/// timing oracle on the SQL comparison yields nothing exploitable (you cannot incrementally forge a
/// 256-bit preimage). This mirrors IC's proven ApiKeys model. The raw key is never stored or logged.
/// </summary>
public sealed class TenantApiKeyRepository : ITenantApiKeyRepository
{
    private readonly string _connectionString;
    private readonly IGlobalLogger _logger;

    public TenantApiKeyRepository(IConfiguration configuration, IGlobalLogger logger)
    {
        _connectionString = configuration.GetConnectionString(ControlPlaneMigrationService.ConnectionStringName)
            ?? throw new InvalidOperationException(
                $"Control-plane connection string '{ControlPlaneMigrationService.ConnectionStringName}' not found. " +
                "Configure it via user-secrets (dev) or an environment variable / secret store (prod).");
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    private const string SelectColumns =
        "Id, TenantId, KeyHash, KeyPrefix, Scope, Name, CreatedAt, RevokedAt";

    public async Task<TenantApiKeyValidationResult> ValidateAsync(string rawKey, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(rawKey))
            return TenantApiKeyValidationResult.Fail("Empty key");

        var keyHash = HashKey(rawKey);
        var keyPrefix = rawKey.Length >= 8 ? rawKey.Substring(0, 8) : rawKey;

        await using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync(cancellationToken).ConfigureAwait(false);

        // Match on hash AND prefix (prefix narrows the index seek; hash is the authority). Both are
        // derived from the raw key — neither is client-controlled beyond presenting the key itself.
        var row = await conn.QuerySingleOrDefaultAsync<TenantApiKeyRow>(
            $"SELECT {SelectColumns} FROM TenantApiKeys WHERE KeyHash = @KeyHash AND KeyPrefix = @KeyPrefix",
            new { KeyHash = keyHash, KeyPrefix = keyPrefix }).ConfigureAwait(false);

        if (row is null)
            return TenantApiKeyValidationResult.Fail("Invalid API key");

        if (row.RevokedAt.HasValue)
            return TenantApiKeyValidationResult.Fail("API key has been revoked");

        var scope = ParseScope(row.Scope);

        // Defense in depth: a Tenant-scope row MUST carry a TenantId, an Admin-scope row MUST NOT.
        // A row that violates its own invariant is treated as invalid rather than silently trusted.
        if (scope == TenantApiKeyScope.Tenant && row.TenantId is null)
            return TenantApiKeyValidationResult.Fail("Malformed tenant key (no tenant)");
        if (scope == TenantApiKeyScope.Admin && row.TenantId is not null)
            return TenantApiKeyValidationResult.Fail("Malformed admin key (tenant set)");

        return new TenantApiKeyValidationResult
        {
            IsValid = true,
            KeyId = row.Id,
            TenantId = row.TenantId,
            Scope = scope,
            Name = row.Name
        };
    }

    public async Task<(Guid KeyId, string RawKey)> CreateAsync(
        TenantApiKeyScope scope, Guid? tenantId, string name, CancellationToken cancellationToken = default)
    {
        // Enforce the scope/tenant invariant at mint time so the store can never hold a malformed key.
        if (scope == TenantApiKeyScope.Tenant && tenantId is null)
            throw new ArgumentException("A tenant-scoped key requires a tenantId.", nameof(tenantId));
        if (scope == TenantApiKeyScope.Admin && tenantId is not null)
            throw new ArgumentException("An admin-scoped key must not carry a tenantId.", nameof(tenantId));
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Key name is required.", nameof(name));

        var rawKey = GenerateRawKey();
        var keyHash = HashKey(rawKey);
        var keyPrefix = rawKey.Substring(0, 8);
        var id = Guid.NewGuid();

        await using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
        await conn.ExecuteAsync(@"
INSERT INTO TenantApiKeys (Id, TenantId, KeyHash, KeyPrefix, Scope, Name, CreatedAt)
VALUES (@Id, @TenantId, @KeyHash, @KeyPrefix, @Scope, @Name, @CreatedAt)",
            new
            {
                Id = id,
                TenantId = tenantId,
                KeyHash = keyHash,
                KeyPrefix = keyPrefix,
                Scope = scope.ToString(),
                Name = name.Trim(),
                CreatedAt = DateTime.UtcNow
            }).ConfigureAwait(false);

        // Log the mint WITHOUT the raw key (only id/scope/tenant). The raw key never reaches a log sink.
        _logger.LogInformation("Control-plane: minted {Scope} API key {KeyId} for tenant {TenantId}",
            scope, id, tenantId);

        return (id, rawKey);
    }

    public async Task<bool> RevokeAsync(Guid keyId, CancellationToken cancellationToken = default)
    {
        await using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
        var affected = await conn.ExecuteAsync(
            "UPDATE TenantApiKeys SET RevokedAt = @Now WHERE Id = @Id AND RevokedAt IS NULL",
            new { Id = keyId, Now = DateTime.UtcNow }).ConfigureAwait(false);
        if (affected > 0)
            _logger.LogWarning("Control-plane: revoked API key {KeyId}", keyId);
        return affected > 0;
    }

    public async Task<IReadOnlyList<TenantApiKeyRecord>> ListAsync(Guid? tenantId, CancellationToken cancellationToken = default)
    {
        await using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync(cancellationToken).ConfigureAwait(false);

        // null tenantId ⇒ list admin keys (TenantId IS NULL); otherwise that tenant's keys.
        var sql = tenantId is null
            ? $"SELECT {SelectColumns} FROM TenantApiKeys WHERE TenantId IS NULL ORDER BY CreatedAt DESC"
            : $"SELECT {SelectColumns} FROM TenantApiKeys WHERE TenantId = @TenantId ORDER BY CreatedAt DESC";

        var rows = await conn.QueryAsync<TenantApiKeyRow>(sql, new { TenantId = tenantId }).ConfigureAwait(false);
        return rows.Select(Map).ToList();
    }

    // ── Hashing (identical to ApiKeyRepository) ─────────────────────────────────

    private static string GenerateRawKey()
    {
        var bytes = new byte[32];
        using var rng = RandomNumberGenerator.Create();
        rng.GetBytes(bytes);
        return "ic_" + Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static string HashKey(string rawKey)
    {
        var bytes = Encoding.UTF8.GetBytes(rawKey);
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static TenantApiKeyScope ParseScope(string? raw) =>
        Enum.TryParse<TenantApiKeyScope>(raw, ignoreCase: true, out var s) ? s : TenantApiKeyScope.Tenant;

    private static TenantApiKeyRecord Map(TenantApiKeyRow row) => new()
    {
        Id = row.Id,
        TenantId = row.TenantId,
        KeyHash = row.KeyHash,
        KeyPrefix = row.KeyPrefix,
        Scope = ParseScope(row.Scope),
        Name = row.Name,
        CreatedAt = row.CreatedAt,
        RevokedAt = row.RevokedAt
    };

    private sealed class TenantApiKeyRow
    {
        public Guid Id { get; set; }
        public Guid? TenantId { get; set; }
        public string KeyHash { get; set; } = string.Empty;
        public string KeyPrefix { get; set; } = string.Empty;
        public string Scope { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
        public DateTime? RevokedAt { get; set; }
    }
}
