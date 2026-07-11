namespace DataAccessLibrary.ControlPlane;

/// <summary>
/// Control-plane API-key authority. This is the auth oracle for multi-tenant: a presented key is
/// resolved here to {TenantId, Scope} BEFORE any tenant DB is opened, because that resolution is what
/// selects the DB.
///
/// Lives in the control-plane database (table <c>TenantApiKeys</c>) — NOT in any tenant catalog — for
/// exactly that reason: you cannot look a key up inside the tenant DB you have not yet chosen.
/// </summary>
public interface ITenantApiKeyRepository
{
    /// <summary>
    /// Validates a presented raw key against the control-plane store. Returns IsValid=false (never throws
    /// for an unknown key) when there is no active matching row. On success returns the key's TenantId
    /// (null for admin scope) and Scope. A revoked key is rejected.
    /// </summary>
    Task<TenantApiKeyValidationResult> ValidateAsync(string rawKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// Mints a new key with the given scope/tenant, stores ONLY its hash, and returns the RAW key once.
    /// For <see cref="TenantApiKeyScope.Tenant"/>, <paramref name="tenantId"/> is required; for
    /// <see cref="TenantApiKeyScope.Admin"/> it must be null.
    /// </summary>
    Task<(Guid KeyId, string RawKey)> CreateAsync(
        TenantApiKeyScope scope, Guid? tenantId, string name, CancellationToken cancellationToken = default);

    /// <summary>
    /// Same as <see cref="CreateAsync(TenantApiKeyScope, Guid?, string, CancellationToken)"/> with an
    /// explicit TTL (Day 5). <paramref name="ttl"/> null falls back to the
    /// <c>ControlPlane:ApiKeyDefaultTtlDays</c> config (itself default null = non-expiring, which is
    /// also what the TTL-less overload yields — every existing mint site is unchanged).
    /// </summary>
    Task<(Guid KeyId, string RawKey)> CreateAsync(
        TenantApiKeyScope scope, Guid? tenantId, string name, TimeSpan? ttl, CancellationToken cancellationToken = default);

    /// <summary>
    /// Mints a <see cref="TenantApiKeyScope.Agent"/> key bound to one tenant AND one agent
    /// (Conduit instance GUID). The AgentId becomes the agent_id claim — the only source of agent
    /// identity on the agent channel. Same hashing scheme and one-time raw-key return as
    /// <see cref="CreateAsync"/>.
    /// </summary>
    Task<(Guid KeyId, string RawKey)> CreateAgentAsync(
        Guid tenantId, Guid agentId, string name, CancellationToken cancellationToken = default);

    /// <summary>Agent mint with an explicit TTL (Day 5). Same fallback chain as the TTL CreateAsync overload.</summary>
    Task<(Guid KeyId, string RawKey)> CreateAgentAsync(
        Guid tenantId, Guid agentId, string name, TimeSpan? ttl, CancellationToken cancellationToken = default);

    /// <summary>
    /// Mints a <see cref="TenantApiKeyScope.Tenant"/> key BOUND to one agent (Conduit instance GUID).
    /// The binding is provisioning lineage only — the key authenticates as the TENANT (scope=tenant;
    /// AgentId is never surfaced as an agent_id claim for Tenant scope). It exists so re-enroll and
    /// agent deactivation can revoke the paired sync key instead of orphaning a live data-plane
    /// credential (HIGH-1). Used by the enroll path for the conduit-sync key.
    /// </summary>
    Task<(Guid KeyId, string RawKey)> CreateTenantKeyForAgentAsync(
        Guid tenantId, Guid agentId, string name, CancellationToken cancellationToken = default);

    /// <summary>
    /// Rotates a live Admin/Tenant key (Day 5): mints a replacement with the same scope, tenant, and
    /// name, and stamps the OLD key's ExpiresAt to UtcNow + <paramref name="grace"/> (default: config
    /// <c>ControlPlane:ApiKeyRotationGraceHours</c>, 24h) — never LATER than a pre-existing expiry, so
    /// rotation can never revive or extend an already-expiring key. Both keys validate during the
    /// grace window; after it, only the replacement does. Returns null when the key does not exist or
    /// is already revoked/expired. Agent-scope keys are NOT rotatable here (throws) — re-enroll is
    /// their rotation path, preserving the one-live-credential-per-agent invariant.
    /// </summary>
    Task<TenantApiKeyRotationResult?> RotateAsync(
        Guid keyId, TimeSpan? grace = null, CancellationToken cancellationToken = default);

    /// <summary>Revokes a key by id (sets RevokedAt). Returns true if a non-revoked key was revoked.</summary>
    Task<bool> RevokeAsync(Guid keyId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Revokes ALL live keys belonging to one (tenant, agent) pair: the Agent-scope identity key(s)
    /// AND the Tenant-scope sync key(s) bound to the agent via AgentId (HIGH-1 — a reinstall or
    /// deactivation must never orphan a live data-plane credential). Supersedes the agent-keys-only
    /// RevokeAgentKeysAsync (Worf pass-2 LOW-1). <paramref name="legacySyncKeyName"/>, when given,
    /// additionally sweeps UNBOUND Tenant-scope keys with exactly that name — the deterministic
    /// <c>conduit-sync-{instanceId[..8]}</c> mint name — so sync keys minted before binding existed
    /// are retired too. Returns the number of keys revoked.
    /// </summary>
    Task<int> RevokeKeysForAgentAsync(
        Guid tenantId, Guid agentId, string? legacySyncKeyName = null, CancellationToken cancellationToken = default);

    /// <summary>Lists keys (hashes only, no raw secret) for the given tenant, or all admin keys when null.</summary>
    Task<IReadOnlyList<TenantApiKeyRecord>> ListAsync(Guid? tenantId, CancellationToken cancellationToken = default);
}
