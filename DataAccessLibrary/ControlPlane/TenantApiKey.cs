namespace DataAccessLibrary.ControlPlane;

/// <summary>
/// Scope of a control-plane API key. Determines what the key may call.
///
/// SECURITY: scope is the authorization axis between the SaaS control plane and a tenant's data.
///   - <see cref="Admin"/> keys operate ONLY on control-plane endpoints (/provision, key mgmt). They
///     carry NO tenant and MUST be rejected on tenant-data endpoints (no ambient access to tenant data).
///   - <see cref="Tenant"/> keys carry a non-null <see cref="TenantApiKeyRecord.TenantId"/> and are the
///     ONLY way to reach that one tenant's data. They are rejected on control-plane endpoints (403).
/// </summary>
public enum TenantApiKeyScope
{
    /// <summary>Control-plane / admin key. TenantId is NULL. Control-plane endpoints only.</summary>
    Admin,
    /// <summary>Tenant-scoped key. TenantId is set. That tenant's data endpoints only.</summary>
    Tenant,
    /// <summary>
    /// Per-agent key (Day 4 enroll). TenantId AND AgentId are set. Agent-channel endpoints only
    /// (heartbeat + command claim/complete) — the key_type=Agent claim keeps TenantDataPolicy denying
    /// it on /api/objects/* (the June-11 invariant). Sync pushes use the paired Tenant-scope key.
    /// </summary>
    Agent
}

/// <summary>
/// A single control-plane API key.
///
/// WHY THE CONTROL PLANE (not a tenant DB): a key must be resolvable to {TenantId, Scope} BEFORE we know
/// which tenant DB to open — resolution is literally what picks the DB. So the key authority lives in the
/// control-plane <c>TenantApiKeys</c> table, never inside a tenant catalog.
///
/// SECURITY:
///   - Only the SHA-256 <see cref="KeyHash"/> is stored (hex, lowercase) — never the raw key. Hashing
///     reuses IC's existing scheme (<c>IApiKeyRepository.HashApiKey</c>) so it is identical across stores.
///   - The raw key is returned to the caller EXACTLY ONCE at mint time (via the one-time vault) and is
///     never persisted, logged, or recoverable thereafter.
///   - <see cref="TenantId"/> is the trust anchor for the per-request connection: it comes ONLY from the
///     validated key row, never from a client header/param (no IDOR).
/// </summary>
public sealed class TenantApiKeyRecord
{
    public Guid Id { get; set; }

    /// <summary>
    /// The tenant this key authorizes. NULL ⇒ a control-plane/admin key (no specific tenant).
    /// For a <see cref="TenantApiKeyScope.Tenant"/> key this is required and drives the connection.
    /// </summary>
    public Guid? TenantId { get; set; }

    /// <summary>SHA-256 hash (lowercase hex) of the raw key. The raw key is never stored.</summary>
    public string KeyHash { get; set; } = string.Empty;

    /// <summary>First 8 chars of the raw key — a non-secret lookup discriminant (mirrors IC's ApiKeys).</summary>
    public string KeyPrefix { get; set; } = string.Empty;

    public TenantApiKeyScope Scope { get; set; }

    /// <summary>
    /// The agent (Conduit instance GUID) this key belongs to. Two meanings by scope:
    ///   - <see cref="TenantApiKeyScope.Agent"/>: agent IDENTITY — becomes the agent_id claim, the
    ///     sole source of agent identity on the channel (no endpoint accepts a caller agentId).
    ///   - <see cref="TenantApiKeyScope.Tenant"/>: provisioning BINDING only (HIGH-1) — the enroll
    ///     path stamps the paired conduit-sync key with its instance so re-enroll/deactivate can
    ///     revoke it. NEVER surfaced as an agent_id claim (ValidateAsync returns AgentId=null for
    ///     Tenant scope), so the sync key's claim set is byte-identical to an unbound tenant key.
    /// NULL for Admin keys and unbound Tenant keys.
    /// </summary>
    public Guid? AgentId { get; set; }

    public string Name { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }

    /// <summary>Set when the key is revoked. A revoked key fails validation. NULL ⇒ active.</summary>
    public DateTime? RevokedAt { get; set; }

    /// <summary>
    /// Optional expiry (Day 5). NULL ⇒ non-expiring (every pre-Day-5 key is grandfathered).
    /// An expired key fails validation exactly like a revoked one — same uniform failure,
    /// no expired-vs-revoked-vs-unknown oracle for a caller.
    /// </summary>
    public DateTime? ExpiresAt { get; set; }
}

/// <summary>
/// Result of rotating a control-plane key: the replacement's raw key (returned ONCE, exactly
/// like a fresh mint) plus the grace deadline stamped onto the old key.
/// </summary>
public sealed class TenantApiKeyRotationResult
{
    public Guid OldKeyId { get; init; }
    /// <summary>The rotated key's tenant (NULL for an admin key) — carried so the audit row keeps tenant linkage.</summary>
    public Guid? TenantId { get; init; }
    /// <summary>When the OLD key stops validating (UtcNow + grace, never later than a pre-existing expiry).</summary>
    public DateTime OldKeyExpiresAt { get; init; }
    public Guid NewKeyId { get; init; }
    /// <summary>The replacement raw key. Returned exactly once; never stored or logged.</summary>
    public string NewRawKey { get; init; } = string.Empty;
}

/// <summary>
/// Result of validating a presented control-plane key. <see cref="IsValid"/> is the gate; on success
/// <see cref="TenantId"/> + <see cref="Scope"/> drive both authorization and connection resolution.
/// </summary>
public sealed class TenantApiKeyValidationResult
{
    public bool IsValid { get; init; }
    public Guid KeyId { get; init; }
    public Guid? TenantId { get; init; }
    public TenantApiKeyScope Scope { get; init; }
    /// <summary>Non-null only for a valid <see cref="TenantApiKeyScope.Agent"/> key.</summary>
    public Guid? AgentId { get; init; }
    public string? Name { get; init; }
    public string? FailureReason { get; init; }

    public static TenantApiKeyValidationResult Fail(string reason) =>
        new() { IsValid = false, FailureReason = reason };
}
