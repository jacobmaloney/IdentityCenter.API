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

    /// <summary>Revokes a key by id (sets RevokedAt). Returns true if a non-revoked key was revoked.</summary>
    Task<bool> RevokeAsync(Guid keyId, CancellationToken cancellationToken = default);

    /// <summary>Lists keys (hashes only, no raw secret) for the given tenant, or all admin keys when null.</summary>
    Task<IReadOnlyList<TenantApiKeyRecord>> ListAsync(Guid? tenantId, CancellationToken cancellationToken = default);
}
