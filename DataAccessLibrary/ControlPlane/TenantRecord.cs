namespace DataAccessLibrary.ControlPlane;

/// <summary>
/// Lifecycle state of a tenant in the control plane.
/// Stored as the string name in the Tenants.Status column.
/// </summary>
public enum TenantStatus
{
    /// <summary>Record created; tenant DB provisioning not yet completed.</summary>
    Provisioning,
    /// <summary>Tenant DB provisioned + migrated; tenant is live.</summary>
    Active,
    /// <summary>Tenant temporarily disabled (billing/admin) but data retained.</summary>
    Suspended,
    /// <summary>Provisioning failed; record retained for diagnosis/retry.</summary>
    Failed
}

/// <summary>
/// A single tenant in the DB-per-tenant control plane registry.
///
/// This is the ONLY new schema in the SaaS foundation — it lives in a dedicated control-plane
/// database (e.g. <c>IdentityCenterControlPlane</c>), entirely separate from the per-tenant
/// V001..V135 schema. One row per customer tenant; <see cref="IcDbConnectionString"/> points the
/// per-request connection factory (Day 4) at that tenant's own IdentityCenter database.
///
/// SECURITY: <see cref="IcDbConnectionString"/> and <see cref="ConduitToken"/> are SECRETS. They
/// are stored ENCRYPTED at rest (DataProtection, "enc:" sentinel) and the repository transparently
/// encrypts on write / decrypts on read. The plaintext properties on this object are only ever
/// populated in-memory after an authorized read; they are never persisted in the clear.
/// </summary>
public sealed class TenantRecord
{
    public Guid Id { get; set; }

    /// <summary>Unique, lowercase, injection-safe slug. Drives the tenant DB name. See <see cref="TenantSlug"/>.</summary>
    public string Slug { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public TenantStatus Status { get; set; } = TenantStatus.Provisioning;

    /// <summary>Deployment region label (e.g. "us-east"). Free-form for now; used for routing/placement later.</summary>
    public string? Region { get; set; }

    /// <summary>
    /// PLAINTEXT connection string for this tenant's IdentityCenter database.
    /// Populated on read (decrypted) and consumed on write (encrypted by the repository).
    /// Never persisted in this form.
    /// </summary>
    public string? IcDbConnectionString { get; set; }

    /// <summary>Base URL of this tenant's Conduit sync engine, if any. Not a secret.</summary>
    public string? ConduitBaseUrl { get; set; }

    /// <summary>
    /// PLAINTEXT Conduit API token. Populated on read (decrypted) and consumed on write (encrypted).
    /// Never persisted in this form.
    /// </summary>
    public string? ConduitToken { get; set; }

    /// <summary>Plan/tier label (room for billing later). Nullable until billing lands.</summary>
    public string? Plan { get; set; }

    /// <summary>
    /// When this tenant's trial ends (UTC). Set at provision time for Trial-plan tenants
    /// (UtcNow + configured trial length). NULL for non-trial plans or when unset.
    ///
    /// RECORDING ONLY: nothing in the product currently enforces this. Suspend-on-expiry /
    /// convert-to-paid is a deliberate follow-up (Travis's Stripe side + a future enforcement job).
    /// </summary>
    public DateTime? TrialExpiresAt { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime ModifiedAt { get; set; }

    /// <summary>
    /// Convenience predicate: true if this is a trial whose <see cref="TrialExpiresAt"/> is in the past
    /// relative to <paramref name="utcNow"/>. Pure computation — it does NOT change status or enforce
    /// anything. No caller acts on this yet; an enforcement job is a tracked follow-up.
    /// </summary>
    public bool IsTrialExpired(DateTime utcNow) =>
        TrialExpiresAt.HasValue && TrialExpiresAt.Value <= utcNow;
}
