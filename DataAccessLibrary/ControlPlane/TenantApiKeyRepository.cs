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
    /// <summary>Default TTL (days) applied when a mint passes no explicit TTL. Null/absent = non-expiring (today's behavior).</summary>
    public const string DefaultTtlDaysKey = "ControlPlane:ApiKeyDefaultTtlDays";
    /// <summary>Grace window (hours) a rotated-out key keeps validating. Default 24.</summary>
    public const string RotationGraceHoursKey = "ControlPlane:ApiKeyRotationGraceHours";
    public const int DefaultRotationGraceHours = 24;

    private readonly string _connectionString;
    private readonly IConfiguration _configuration;
    private readonly IGlobalLogger _logger;

    public TenantApiKeyRepository(IConfiguration configuration, IGlobalLogger logger)
    {
        _connectionString = configuration.GetConnectionString(ControlPlaneMigrationService.ConnectionStringName)
            ?? throw new InvalidOperationException(
                $"Control-plane connection string '{ControlPlaneMigrationService.ConnectionStringName}' not found. " +
                "Configure it via user-secrets (dev) or an environment variable / secret store (prod).");
        _configuration = configuration;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    private const string SelectColumns =
        "Id, TenantId, KeyHash, KeyPrefix, Scope, AgentId, Name, CreatedAt, RevokedAt, ExpiresAt";

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

        // Expired == invalid, same shape as revoked (Day 5). The FailureReason never leaves the
        // process (the auth handler answers with the same uniform 401 for every invalid key), so
        // there is no expired-vs-revoked-vs-unknown oracle for a caller. NULL ExpiresAt = never
        // expires — every pre-Day-5 key is grandfathered.
        if (IsExpired(row.ExpiresAt, DateTime.UtcNow))
            return TenantApiKeyValidationResult.Fail("API key has expired");

        var scope = ParseScope(row.Scope);

        // Defense in depth: a row that violates its own scope invariant is treated as invalid
        // rather than silently trusted. See RowShapeError for the per-scope rules.
        if (RowShapeError(scope, row.TenantId, row.AgentId) is string shapeError)
            return TenantApiKeyValidationResult.Fail(shapeError);

        return new TenantApiKeyValidationResult
        {
            IsValid = true,
            KeyId = row.Id,
            TenantId = row.TenantId,
            Scope = scope,
            // HIGH-1 GUARD: AgentId surfaces as identity (→ agent_id claim) for Agent scope ONLY.
            // A Tenant-scope sync key may CARRY an AgentId (provisioning binding for revocation),
            // but it must authenticate exactly as before — never as the agent.
            AgentId = SurfacedAgentId(scope, row.AgentId),
            Name = row.Name
        };
    }

    // ── Row-shape invariants (pinned by unit tests) ─────────────────────────────
    //   - Tenant: TenantId required. AgentId ALLOWED (HIGH-1 sync-key binding — lineage, not identity).
    //   - Admin:  no TenantId, no AgentId.
    //   - Agent:  TenantId AND AgentId required.

    internal static string? RowShapeError(TenantApiKeyScope scope, Guid? tenantId, Guid? agentId) => scope switch
    {
        TenantApiKeyScope.Tenant when tenantId is null => "Malformed tenant key (no tenant)",
        TenantApiKeyScope.Admin when tenantId is not null => "Malformed admin key (tenant set)",
        TenantApiKeyScope.Admin when agentId is not null => "Malformed admin key (agent set)",
        TenantApiKeyScope.Agent when tenantId is null || agentId is null => "Malformed agent key (tenant or agent missing)",
        _ => null
    };

    /// <summary>AgentId is agent IDENTITY only on Agent-scope keys; a bound Tenant key surfaces null.</summary>
    internal static Guid? SurfacedAgentId(TenantApiKeyScope scope, Guid? agentId) =>
        scope == TenantApiKeyScope.Agent ? agentId : null;

    public Task<(Guid KeyId, string RawKey)> CreateAsync(
        TenantApiKeyScope scope, Guid? tenantId, string name, CancellationToken cancellationToken = default)
        => CreateAsync(scope, tenantId, name, ttl: null, cancellationToken);

    public async Task<(Guid KeyId, string RawKey)> CreateAsync(
        TenantApiKeyScope scope, Guid? tenantId, string name, TimeSpan? ttl, CancellationToken cancellationToken = default)
    {
        // Enforce the scope/tenant invariant at mint time so the store can never hold a malformed key.
        if (scope == TenantApiKeyScope.Agent)
            throw new ArgumentException("Agent-scoped keys require an agentId — use CreateAgentAsync.", nameof(scope));
        if (scope == TenantApiKeyScope.Tenant && tenantId is null)
            throw new ArgumentException("A tenant-scoped key requires a tenantId.", nameof(tenantId));
        if (scope == TenantApiKeyScope.Admin && tenantId is not null)
            throw new ArgumentException("An admin-scoped key must not carry a tenantId.", nameof(tenantId));
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Key name is required.", nameof(name));

        return await InsertKeyAsync(scope, tenantId, agentId: null, name, ttl, cancellationToken).ConfigureAwait(false);
    }

    public Task<(Guid KeyId, string RawKey)> CreateAgentAsync(
        Guid tenantId, Guid agentId, string name, CancellationToken cancellationToken = default)
        => CreateAgentAsync(tenantId, agentId, name, ttl: null, cancellationToken);

    public async Task<(Guid KeyId, string RawKey)> CreateAgentAsync(
        Guid tenantId, Guid agentId, string name, TimeSpan? ttl, CancellationToken cancellationToken = default)
    {
        if (tenantId == Guid.Empty)
            throw new ArgumentException("An agent-scoped key requires a tenantId.", nameof(tenantId));
        if (agentId == Guid.Empty)
            throw new ArgumentException("An agent-scoped key requires an agentId.", nameof(agentId));
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Key name is required.", nameof(name));

        return await InsertKeyAsync(TenantApiKeyScope.Agent, tenantId, agentId, name, ttl, cancellationToken).ConfigureAwait(false);
    }

    public async Task<(Guid KeyId, string RawKey)> CreateTenantKeyForAgentAsync(
        Guid tenantId, Guid agentId, string name, CancellationToken cancellationToken = default)
    {
        if (tenantId == Guid.Empty)
            throw new ArgumentException("A tenant-scoped key requires a tenantId.", nameof(tenantId));
        if (agentId == Guid.Empty)
            throw new ArgumentException("A bound tenant key requires an agentId.", nameof(agentId));
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Key name is required.", nameof(name));

        return await InsertKeyAsync(TenantApiKeyScope.Tenant, tenantId, agentId, name, ttl: null, cancellationToken).ConfigureAwait(false);
    }

    private async Task<(Guid KeyId, string RawKey)> InsertKeyAsync(
        TenantApiKeyScope scope, Guid? tenantId, Guid? agentId, string name, TimeSpan? ttl, CancellationToken cancellationToken)
    {
        var rawKey = GenerateRawKey();
        var keyHash = HashKey(rawKey);
        var keyPrefix = rawKey.Substring(0, 8);
        var id = Guid.NewGuid();
        var expiresAt = ResolveMintExpiry(ttl, _configuration.GetValue<int?>(DefaultTtlDaysKey), DateTime.UtcNow);

        await using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
        await conn.ExecuteAsync(@"
INSERT INTO TenantApiKeys (Id, TenantId, KeyHash, KeyPrefix, Scope, AgentId, Name, CreatedAt, ExpiresAt)
VALUES (@Id, @TenantId, @KeyHash, @KeyPrefix, @Scope, @AgentId, @Name, @CreatedAt, @ExpiresAt)",
            new
            {
                Id = id,
                TenantId = tenantId,
                KeyHash = keyHash,
                KeyPrefix = keyPrefix,
                Scope = scope.ToString(),
                AgentId = agentId,
                Name = name.Trim(),
                CreatedAt = DateTime.UtcNow,
                ExpiresAt = expiresAt
            }).ConfigureAwait(false);

        // Log the mint WITHOUT the raw key (only id/scope/tenant). The raw key never reaches a log sink.
        _logger.LogInformation("Control-plane: minted {Scope} API key {KeyId} for tenant {TenantId} (expires: {ExpiresAt})",
            scope, id, tenantId, expiresAt?.ToString("o") ?? "never");

        return (id, rawKey);
    }

    public async Task<TenantApiKeyRotationResult?> RotateAsync(
        Guid keyId, TimeSpan? grace = null, CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;

        await using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync(cancellationToken).ConfigureAwait(false);

        var row = await conn.QuerySingleOrDefaultAsync<TenantApiKeyRow>(
            $"SELECT {SelectColumns} FROM TenantApiKeys WHERE Id = @Id",
            new { Id = keyId }).ConfigureAwait(false);

        // Not found, revoked, and already-expired all answer null (nothing live to rotate).
        if (row is null || row.RevokedAt.HasValue || IsExpired(row.ExpiresAt, now))
            return null;

        var scope = ParseScope(row.Scope);
        if (scope == TenantApiKeyScope.Agent)
            throw new InvalidOperationException(
                "Agent-scoped keys are not rotatable here — re-enroll the agent instead. " +
                "(Rotation's grace window would leave two live credentials for one agent identity.)");

        var effectiveGrace = ResolveRotationGrace(grace, _configuration.GetValue<int?>(RotationGraceHoursKey));
        // Never EXTEND: a key already expiring sooner than the grace deadline keeps its own expiry.
        var oldExpiresAt = ClampOldKeyExpiry(row.ExpiresAt, now.Add(effectiveGrace));

        // Mint the replacement first (fresh mint semantics — the default-TTL config applies), then
        // stamp the old key's grace deadline. Both statements share one transaction so a failure
        // can never leave the old key expiring with no replacement minted.
        var rawKey = GenerateRawKey();
        var newKeyId = Guid.NewGuid();
        var newExpiresAt = ResolveMintExpiry(null, _configuration.GetValue<int?>(DefaultTtlDaysKey), now);

        await using var tx = (SqlTransaction)await conn.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        // AgentId carries over: a bound Tenant sync key keeps its instance binding through rotation,
        // or the replacement would silently become an orphan the HIGH-1 revocation sweep misses.
        // (Agent-scope keys never reach this INSERT — they throw above.)
        await conn.ExecuteAsync(new CommandDefinition(@"
INSERT INTO TenantApiKeys (Id, TenantId, KeyHash, KeyPrefix, Scope, AgentId, Name, CreatedAt, ExpiresAt)
VALUES (@Id, @TenantId, @KeyHash, @KeyPrefix, @Scope, @AgentId, @Name, @CreatedAt, @ExpiresAt)",
            new
            {
                Id = newKeyId,
                row.TenantId,
                KeyHash = HashKey(rawKey),
                KeyPrefix = rawKey.Substring(0, 8),
                Scope = scope.ToString(),
                row.AgentId,
                row.Name,
                CreatedAt = now,
                ExpiresAt = newExpiresAt
            }, transaction: tx, cancellationToken: cancellationToken)).ConfigureAwait(false);

        // Optimistic concurrency (Worf L2): the stamp only wins if ExpiresAt is still what we read.
        // A racing second rotate that read the same row will find ExpiresAt already changed and get
        // 0 rows — so it rolls back its own mint instead of overwriting an earlier grace deadline
        // with a later one (which would revive the key). Also re-checks RevokedAt.
        var stamped = await conn.ExecuteAsync(new CommandDefinition(@"
UPDATE TenantApiKeys SET ExpiresAt = @ExpiresAt
WHERE Id = @Id AND RevokedAt IS NULL
  AND ((ExpiresAt IS NULL AND @ReadExpiresAt IS NULL) OR ExpiresAt = @ReadExpiresAt)",
            new { Id = keyId, ExpiresAt = oldExpiresAt, ReadExpiresAt = row.ExpiresAt },
            transaction: tx, cancellationToken: cancellationToken)).ConfigureAwait(false);

        if (stamped == 0)
        {
            // The old key was revoked, or concurrently rotated, between our read and the stamp —
            // abort; nothing changed (the replacement mint rolls back with the transaction).
            await tx.RollbackAsync(cancellationToken).ConfigureAwait(false);
            return null;
        }

        await tx.CommitAsync(cancellationToken).ConfigureAwait(false);

        _logger.LogWarning(
            "Control-plane: rotated {Scope} API key {OldKeyId} → {NewKeyId} for tenant {TenantId}; old key expires {OldExpiresAt:o}",
            scope, keyId, newKeyId, row.TenantId, oldExpiresAt);

        return new TenantApiKeyRotationResult
        {
            OldKeyId = keyId,
            TenantId = row.TenantId,
            OldKeyExpiresAt = oldExpiresAt,
            NewKeyId = newKeyId,
            NewRawKey = rawKey
        };
    }

    // ── Pure TTL/expiry decisions (pinned by unit tests) ────────────────────────

    /// <summary>NULL ExpiresAt never expires; otherwise strictly-before-now is expired.</summary>
    internal static bool IsExpired(DateTime? expiresAt, DateTime utcNow) =>
        expiresAt.HasValue && expiresAt.Value < utcNow;

    /// <summary>
    /// Explicit TTL wins; else the config default-TTL-days (when positive); else null = non-expiring.
    /// With no TTL and no config this returns null — the pre-Day-5 behavior, exactly.
    /// </summary>
    internal static DateTime? ResolveMintExpiry(TimeSpan? ttl, int? defaultTtlDays, DateTime utcNow)
    {
        if (ttl.HasValue)
            return utcNow.Add(ttl.Value);
        if (defaultTtlDays is > 0)
            return utcNow.AddDays(defaultTtlDays.Value);
        return null;
    }

    /// <summary>Explicit grace wins; else config hours (when positive); else 24h.</summary>
    internal static TimeSpan ResolveRotationGrace(TimeSpan? grace, int? configHours)
    {
        if (grace.HasValue && grace.Value >= TimeSpan.Zero)
            return grace.Value;
        if (configHours is > 0)
            return TimeSpan.FromHours(configHours.Value);
        return TimeSpan.FromHours(DefaultRotationGraceHours);
    }

    /// <summary>The old key's grace deadline can only ever move an expiry EARLIER, never extend it.</summary>
    internal static DateTime ClampOldKeyExpiry(DateTime? currentExpiresAt, DateTime proposed) =>
        currentExpiresAt.HasValue && currentExpiresAt.Value < proposed ? currentExpiresAt.Value : proposed;

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

    // Predicate set pinned by unit test: live rows for exactly one (tenant, agent) pair — the
    // Agent-scope identity key(s) and the Tenant-scope sync key(s) bound via AgentId (HIGH-1) —
    // plus, only when a legacy name is supplied, UNBOUND Tenant-scope rows with exactly that name
    // (sync keys minted before binding existed). Never Admin keys, never other agents' keys,
    // never already-revoked rows.
    internal const string RevokeKeysForAgentSql = @"
UPDATE TenantApiKeys
SET RevokedAt = @Now
WHERE TenantId = @TenantId AND RevokedAt IS NULL AND Scope IN ('Agent', 'Tenant')
  AND (AgentId = @AgentId
       OR (@LegacySyncKeyName IS NOT NULL AND Scope = 'Tenant' AND AgentId IS NULL AND Name = @LegacySyncKeyName))";

    public async Task<int> RevokeKeysForAgentAsync(
        Guid tenantId, Guid agentId, string? legacySyncKeyName = null, CancellationToken cancellationToken = default)
    {
        if (tenantId == Guid.Empty) throw new ArgumentException("TenantId is required.", nameof(tenantId));
        if (agentId == Guid.Empty) throw new ArgumentException("AgentId is required.", nameof(agentId));

        await using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
        var affected = await conn.ExecuteAsync(
            RevokeKeysForAgentSql,
            new { TenantId = tenantId, AgentId = agentId, LegacySyncKeyName = legacySyncKeyName, Now = DateTime.UtcNow })
            .ConfigureAwait(false);
        if (affected > 0)
            _logger.LogWarning("Control-plane: revoked {Count} key(s) (agent + bound sync) for tenant {TenantId} agent {AgentId}",
                affected, tenantId, agentId);
        return affected;
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
        AgentId = row.AgentId,
        Name = row.Name,
        CreatedAt = row.CreatedAt,
        RevokedAt = row.RevokedAt,
        ExpiresAt = row.ExpiresAt
    };

    private sealed class TenantApiKeyRow
    {
        public Guid Id { get; set; }
        public Guid? TenantId { get; set; }
        public string KeyHash { get; set; } = string.Empty;
        public string KeyPrefix { get; set; } = string.Empty;
        public string Scope { get; set; } = string.Empty;
        public Guid? AgentId { get; set; }
        public string Name { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
        public DateTime? RevokedAt { get; set; }
        public DateTime? ExpiresAt { get; set; }
    }
}
