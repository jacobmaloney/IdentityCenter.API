using Common.Encryption;
using Dapper;
using Logging;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;

namespace DataAccessLibrary.ControlPlane;

/// <summary>
/// Dapper implementation of <see cref="ITenantRegistryRepository"/> over the control-plane
/// <c>Tenants</c> table.
///
/// Differences from <c>DapperRepositoryBase</c> (intentional — this repo is control-plane, not
/// tenant): it reads the <c>ControlPlane</c> connection string (NOT <c>DefaultConnection</c>), and
/// it transparently encrypts/decrypts the two secret columns via <see cref="IEncryptionService"/>.
///
/// Encryption-at-rest contract:
///   - On write, secret values are encrypted with <see cref="IEncryptionService.EncryptAsync"/> and
///     stored with the <see cref="ConnectionStringProtector.EncryptedPrefix"/> ("enc:") sentinel so
///     the stored value is self-describing.
///   - On read, a value carrying the sentinel is stripped + decrypted; a bare value (legacy / never
///     happens for new rows) is returned as-is.
/// </summary>
public sealed class TenantRegistryRepository : ITenantRegistryRepository
{
    private readonly string _connectionString;
    private readonly IEncryptionService _encryption;
    private readonly IGlobalLogger _logger;

    public TenantRegistryRepository(
        IConfiguration configuration,
        IEncryptionService encryption,
        IGlobalLogger logger)
    {
        _connectionString = configuration.GetConnectionString(ControlPlaneMigrationService.ConnectionStringName)
            ?? throw new InvalidOperationException(
                $"Control-plane connection string '{ControlPlaneMigrationService.ConnectionStringName}' not found. " +
                "Configure it via user-secrets (dev) or an environment variable / secret store (prod).");
        _encryption = encryption ?? throw new ArgumentNullException(nameof(encryption));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    // NOTE: 'Plan' is a SQL Server reserved word — it MUST be bracketed everywhere it appears as a
    // column identifier. The DDL defines the column as [Plan]; these references match.
    private const string SelectColumns =
        "Id, Slug, DisplayName, Status, Region, IcDbConnectionString, ConduitBaseUrl, ConduitTokenEncrypted, [Plan], TrialExpiresAt, CreatedAt, ModifiedAt";

    // ── Encryption helpers ──────────────────────────────────────────────────

    private async Task<string?> ProtectAsync(string? plaintext)
    {
        if (string.IsNullOrEmpty(plaintext)) return plaintext;
        var cipher = await _encryption.EncryptAsync(plaintext).ConfigureAwait(false);
        return ConnectionStringProtector.EncryptedPrefix + cipher;
    }

    private async Task<string?> UnprotectAsync(string? stored)
    {
        if (string.IsNullOrEmpty(stored)) return stored;
        if (!ConnectionStringProtector.IsEncrypted(stored)) return stored; // bare/legacy — pass through
        var cipher = stored.Substring(ConnectionStringProtector.EncryptedPrefix.Length);
        return await _encryption.DecryptAsync(cipher).ConfigureAwait(false);
    }

    // ── Reads ───────────────────────────────────────────────────────────────

    public async Task<TenantRecord?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
        var row = await conn.QuerySingleOrDefaultAsync<TenantRow>(
            $"SELECT {SelectColumns} FROM Tenants WHERE Id = @Id", new { Id = id }).ConfigureAwait(false);
        return await MapAsync(row).ConfigureAwait(false);
    }

    public async Task<TenantRecord?> GetBySlugAsync(string slug, CancellationToken cancellationToken = default)
    {
        var normalized = TenantSlug.Normalize(slug);
        await using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
        var row = await conn.QuerySingleOrDefaultAsync<TenantRow>(
            $"SELECT {SelectColumns} FROM Tenants WHERE Slug = @Slug", new { Slug = normalized }).ConfigureAwait(false);
        return await MapAsync(row).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<TenantRecord>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        await using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
        var rows = await conn.QueryAsync<TenantRow>(
            $"SELECT {SelectColumns} FROM Tenants ORDER BY CreatedAt DESC").ConfigureAwait(false);

        var list = new List<TenantRecord>();
        foreach (var row in rows)
        {
            var mapped = await MapAsync(row).ConfigureAwait(false);
            if (mapped is not null) list.Add(mapped);
        }
        return list;
    }

    public async Task<bool> SlugExistsAsync(string slug, CancellationToken cancellationToken = default)
    {
        var normalized = TenantSlug.Normalize(slug);
        await using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
        var count = await conn.ExecuteScalarAsync<int>(
            "SELECT COUNT(1) FROM Tenants WHERE Slug = @Slug", new { Slug = normalized }).ConfigureAwait(false);
        return count > 0;
    }

    public async Task<string?> GetConnectionStringAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
        var stored = await conn.ExecuteScalarAsync<string?>(
            "SELECT IcDbConnectionString FROM Tenants WHERE Id = @Id", new { Id = id }).ConfigureAwait(false);
        return await UnprotectAsync(stored).ConfigureAwait(false);
    }

    // ── Writes ──────────────────────────────────────────────────────────────

    public async Task<TenantRecord> CreateAsync(TenantRecord tenant, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(tenant);

        // Normalize + validate the slug BEFORE it can reach the database or a DB name.
        var slug = TenantSlug.Normalize(tenant.Slug);
        if (string.IsNullOrWhiteSpace(tenant.DisplayName))
            throw new ArgumentException("DisplayName is required.", nameof(tenant));

        var now = DateTime.UtcNow;
        tenant.Id = tenant.Id == Guid.Empty ? Guid.NewGuid() : tenant.Id;
        tenant.Slug = slug;
        tenant.CreatedAt = now;
        tenant.ModifiedAt = now;

        var encConn = await ProtectAsync(tenant.IcDbConnectionString).ConfigureAwait(false);
        var encToken = await ProtectAsync(tenant.ConduitToken).ConfigureAwait(false);

        await using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
        await conn.ExecuteAsync(@"
INSERT INTO Tenants
    (Id, Slug, DisplayName, Status, Region, IcDbConnectionString, ConduitBaseUrl, ConduitTokenEncrypted, [Plan], TrialExpiresAt, CreatedAt, ModifiedAt)
VALUES
    (@Id, @Slug, @DisplayName, @Status, @Region, @IcDbConnectionString, @ConduitBaseUrl, @ConduitTokenEncrypted, @Plan, @TrialExpiresAt, @CreatedAt, @ModifiedAt)",
            new
            {
                tenant.Id,
                tenant.Slug,
                tenant.DisplayName,
                Status = tenant.Status.ToString(),
                tenant.Region,
                IcDbConnectionString = encConn,
                tenant.ConduitBaseUrl,
                ConduitTokenEncrypted = encToken,
                tenant.Plan,
                tenant.TrialExpiresAt,
                tenant.CreatedAt,
                tenant.ModifiedAt
            }).ConfigureAwait(false);

        _logger.LogInformation("Control-plane: created tenant {Slug} ({Id})", tenant.Slug, tenant.Id);
        return tenant;
    }

    public async Task UpdateMetadataAsync(TenantRecord tenant, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(tenant);
        await using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
        await conn.ExecuteAsync(@"
UPDATE Tenants SET
    DisplayName    = @DisplayName,
    Status         = @Status,
    Region         = @Region,
    ConduitBaseUrl = @ConduitBaseUrl,
    [Plan]         = @Plan,
    TrialExpiresAt = @TrialExpiresAt,
    ModifiedAt     = @ModifiedAt
WHERE Id = @Id",
            new
            {
                tenant.Id,
                tenant.DisplayName,
                Status = tenant.Status.ToString(),
                tenant.Region,
                tenant.ConduitBaseUrl,
                tenant.Plan,
                tenant.TrialExpiresAt,
                ModifiedAt = DateTime.UtcNow
            }).ConfigureAwait(false);
    }

    public async Task SetStatusAsync(Guid id, TenantStatus status, CancellationToken cancellationToken = default)
    {
        await using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
        await conn.ExecuteAsync(
            "UPDATE Tenants SET Status = @Status, ModifiedAt = @ModifiedAt WHERE Id = @Id",
            new { Id = id, Status = status.ToString(), ModifiedAt = DateTime.UtcNow }).ConfigureAwait(false);
    }

    public async Task<TenantStatus?> GetStatusAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
        var raw = await conn.ExecuteScalarAsync<string?>(
            "SELECT Status FROM Tenants WHERE Id = @Id", new { Id = id }).ConfigureAwait(false);
        if (raw is null) return null;
        return Enum.TryParse<TenantStatus>(raw, ignoreCase: true, out var s) ? s : TenantStatus.Failed;
    }

    public async Task SetConnectionStringAsync(Guid id, string plaintextConnectionString, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(plaintextConnectionString))
            throw new ArgumentException("Connection string cannot be empty.", nameof(plaintextConnectionString));
        var enc = await ProtectAsync(plaintextConnectionString).ConfigureAwait(false);
        await using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
        await conn.ExecuteAsync(
            "UPDATE Tenants SET IcDbConnectionString = @Enc, ModifiedAt = @ModifiedAt WHERE Id = @Id",
            new { Id = id, Enc = enc, ModifiedAt = DateTime.UtcNow }).ConfigureAwait(false);
    }

    public async Task SetConduitTokenAsync(Guid id, string plaintextToken, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(plaintextToken))
            throw new ArgumentException("Token cannot be empty.", nameof(plaintextToken));
        var enc = await ProtectAsync(plaintextToken).ConfigureAwait(false);
        await using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
        await conn.ExecuteAsync(
            "UPDATE Tenants SET ConduitTokenEncrypted = @Enc, ModifiedAt = @ModifiedAt WHERE Id = @Id",
            new { Id = id, Enc = enc, ModifiedAt = DateTime.UtcNow }).ConfigureAwait(false);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
        await conn.ExecuteAsync("DELETE FROM Tenants WHERE Id = @Id", new { Id = id }).ConfigureAwait(false);
        _logger.LogInformation("Control-plane: deleted tenant {Id}", id);
    }

    // ── Mapping ───────────────────────────────────────────────────────────────

    private async Task<TenantRecord?> MapAsync(TenantRow? row)
    {
        if (row is null) return null;
        return new TenantRecord
        {
            Id = row.Id,
            Slug = row.Slug,
            DisplayName = row.DisplayName,
            Status = Enum.TryParse<TenantStatus>(row.Status, ignoreCase: true, out var s) ? s : TenantStatus.Failed,
            Region = row.Region,
            IcDbConnectionString = await UnprotectAsync(row.IcDbConnectionString).ConfigureAwait(false),
            ConduitBaseUrl = row.ConduitBaseUrl,
            ConduitToken = await UnprotectAsync(row.ConduitTokenEncrypted).ConfigureAwait(false),
            Plan = row.Plan,
            TrialExpiresAt = row.TrialExpiresAt,
            CreatedAt = row.CreatedAt,
            ModifiedAt = row.ModifiedAt
        };
    }

    /// <summary>Raw row shape matching the Tenants columns (ciphertext still in the secret fields).</summary>
    private sealed class TenantRow
    {
        public Guid Id { get; set; }
        public string Slug { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public string? Region { get; set; }
        public string? IcDbConnectionString { get; set; }
        public string? ConduitBaseUrl { get; set; }
        public string? ConduitTokenEncrypted { get; set; }
        public string? Plan { get; set; }
        public DateTime? TrialExpiresAt { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime ModifiedAt { get; set; }
    }
}
