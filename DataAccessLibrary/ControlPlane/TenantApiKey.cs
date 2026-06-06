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
    Tenant
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

    public string Name { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }

    /// <summary>Set when the key is revoked. A revoked key fails validation. NULL ⇒ active.</summary>
    public DateTime? RevokedAt { get; set; }
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
    public string? Name { get; init; }
    public string? FailureReason { get; init; }

    public static TenantApiKeyValidationResult Fail(string reason) =>
        new() { IsValid = false, FailureReason = reason };
}
