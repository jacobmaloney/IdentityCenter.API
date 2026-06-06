namespace IdentityCenter.API.Models;

/// <summary>
/// Request body for POST /api/provision. Only <see cref="Slug"/> ever influences a physical
/// database name, and it is validated/normalized by <c>TenantSlug</c> before it can reach any DDL.
/// Everything else is metadata stored as parameterized values.
/// </summary>
public sealed class ProvisionTenantRequest
{
    /// <summary>Desired tenant slug (lowercase letters/digits/hyphens, 2-40 chars). Drives the DB name.</summary>
    public string Slug { get; set; } = string.Empty;

    /// <summary>Human-friendly tenant name. Required.</summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>Optional deployment region label (free-form for now).</summary>
    public string? Region { get; set; }

    /// <summary>
    /// Optional plan/tier label. Defaults to <c>Trial</c> when omitted. A Trial plan causes the
    /// provision flow to stamp a <c>TrialExpiresAt</c> (UtcNow + configured trial length) on the
    /// tenant record. Recording only — nothing enforces expiry yet (see API docs / follow-up).
    /// </summary>
    public string? Plan { get; set; }

    /// <summary>
    /// Optional admin email for the seeded tenant administrator. Defaults to
    /// <c>admin@{slug}.identitycenter.local</c> when omitted. The generated password is returned ONCE.
    /// </summary>
    public string? AdminEmail { get; set; }
}

/// <summary>
/// 202 response for POST /api/provision: the tenant id + slug + a status URL the caller polls.
/// Carries NO secrets — the admin credential is delivered exactly once via the status endpoint when
/// provisioning reaches <c>Active</c>, then never again.
/// </summary>
public sealed class ProvisionAcceptedResponse
{
    public Guid TenantId { get; set; }
    public string Slug { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string StatusUrl { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
}

/// <summary>
/// Response for GET /api/provision/{id}. Reports lifecycle status and, on the FIRST read after the
/// tenant goes Active, the one-time access bundle (admin email + generated password). Subsequent reads
/// never return the password again — it is held in a short-lived in-memory vault, surfaced once, then
/// purged. The password is never persisted and never logged.
/// </summary>
public sealed class ProvisionStatusResponse
{
    public Guid TenantId { get; set; }
    public string Slug { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string? Region { get; set; }
    public string? Plan { get; set; }

    /// <summary>
    /// When this tenant's trial ends (UTC), for Trial-plan tenants. Null for non-trial plans. Surfaced
    /// for the caller's UI / billing logic; the API itself does not act on it (no enforcement yet).
    /// </summary>
    public DateTime? TrialExpiresAt { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime ModifiedAt { get; set; }

    /// <summary>Schema version applied to the tenant DB once Active (e.g. 135). Null until Active.</summary>
    public int? SchemaVersion { get; set; }

    /// <summary>Populated only when provisioning failed — a sanitized reason. Null otherwise.</summary>
    public string? Error { get; set; }

    /// <summary>
    /// One-time access bundle. Non-null ONLY on the first status read after the tenant reaches Active.
    /// Once returned it is purged from memory and all later reads leave this null.
    /// </summary>
    public TenantAccessBundle? AccessBundle { get; set; }
}

/// <summary>
/// The one-time secret bundle handed back to the caller when a tenant goes Active. The
/// <see cref="AdminPassword"/> is shown exactly once and is never persisted in plaintext anywhere.
/// </summary>
public sealed class TenantAccessBundle
{
    public string AdminEmail { get; set; } = string.Empty;
    public string AdminPassword { get; set; } = string.Empty;

    /// <summary>
    /// The tenant's control-plane API key (scope=tenant), returned EXACTLY ONCE here. This is what the
    /// tenant's integrations present in <c>X-API-Key</c> to call the tenant-data API; every such request
    /// is bound to THIS tenant's DB by the validated key. Like the password, the raw key is never
    /// persisted (only its hash is stored) and never logged — copy it now.
    /// </summary>
    public string TenantApiKey { get; set; } = string.Empty;

    public string Notice { get; set; } =
        "Store these credentials now — the admin password and the tenant API key are shown only once and " +
        "are not persisted anywhere. Change the password on first sign-in.";
}
