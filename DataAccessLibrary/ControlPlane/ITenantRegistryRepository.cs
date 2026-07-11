namespace DataAccessLibrary.ControlPlane;

/// <summary>
/// CRUD over the control-plane <c>Tenants</c> registry. This is the seam the API's per-tenant
/// connection factory (Day 4) and the <c>/provision</c> endpoint (Day 3) will build on.
///
/// All secret values (tenant DB connection string, Conduit token) are encrypted at rest by the
/// implementation; callers pass and receive PLAINTEXT on the <see cref="TenantRecord"/> and never
/// see ciphertext.
/// </summary>
public interface ITenantRegistryRepository
{
    /// <summary>Inserts a new tenant. Slug is normalized + validated; secrets are encrypted. Returns the stored record (with Id/timestamps).</summary>
    Task<TenantRecord> CreateAsync(TenantRecord tenant, CancellationToken cancellationToken = default);

    /// <summary>Returns the tenant by Id with secrets decrypted, or null if not found.</summary>
    Task<TenantRecord?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Returns the tenant by slug (normalized) with secrets decrypted, or null if not found.</summary>
    Task<TenantRecord?> GetBySlugAsync(string slug, CancellationToken cancellationToken = default);

    /// <summary>Returns all tenants (secrets decrypted). Intended for control-plane admin use only.</summary>
    Task<IReadOnlyList<TenantRecord>> GetAllAsync(CancellationToken cancellationToken = default);

    /// <summary>True if a tenant with the given (normalized) slug already exists.</summary>
    Task<bool> SlugExistsAsync(string slug, CancellationToken cancellationToken = default);

    /// <summary>Updates DisplayName, Status, Region, ConduitBaseUrl, and Plan. Does NOT touch secret columns.</summary>
    Task UpdateMetadataAsync(TenantRecord tenant, CancellationToken cancellationToken = default);

    /// <summary>Sets the tenant status only (e.g. Provisioning → Active/Failed).</summary>
    Task SetStatusAsync(Guid id, TenantStatus status, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lightweight status-only read (no secret decryption). Null when the tenant does not exist.
    /// Used by the per-request suspension gate, which runs on every tenant-key request.
    /// </summary>
    Task<TenantStatus?> GetStatusAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Encrypts and stores the tenant DB connection string.</summary>
    Task SetConnectionStringAsync(Guid id, string plaintextConnectionString, CancellationToken cancellationToken = default);

    /// <summary>Returns the decrypted tenant DB connection string, or null if unset/not found.</summary>
    Task<string?> GetConnectionStringAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Encrypts and stores the Conduit token.</summary>
    Task SetConduitTokenAsync(Guid id, string plaintextToken, CancellationToken cancellationToken = default);

    /// <summary>Deletes a tenant registry row. Does NOT drop the tenant database (caller's concern).</summary>
    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}
