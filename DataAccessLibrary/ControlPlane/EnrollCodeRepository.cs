using System.Security.Cryptography;
using System.Text;
using Dapper;
using Logging;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;

namespace DataAccessLibrary.ControlPlane;

/// <summary>
/// Pure code-generation/hashing seams for agent enrollment codes (Day 4). Kept static and
/// side-effect-free so the format and normalization rules are unit-testable without a database.
///
/// FORMAT: 32 bytes CSPRNG → base32 (A-Z, 2-7; no padding) → dash-grouped in 4s for pasteability
/// (e.g. "Q7XW-K2M9-…"). 256 bits of entropy — comfortably above the 128-bit floor.
///
/// NORMALIZATION: dashes/whitespace are cosmetic and case is display-only, so BOTH mint and consume
/// hash the NORMALIZED form (grouping stripped, uppercased). A code pasted with or without dashes
/// verifies identically. Storage is the SHA-256 lowercase hex of the normalized code — the same
/// hashing contract as TenantApiKeys; the raw code is returned exactly once and never stored.
/// </summary>
public static class EnrollCodes
{
    private const string Base32Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
    private const int CodeBytes = 32;
    private const int GroupSize = 4;

    public static string GenerateCode()
    {
        var bytes = new byte[CodeBytes];
        using var rng = RandomNumberGenerator.Create();
        rng.GetBytes(bytes);

        var raw = ToBase32(bytes);
        var grouped = new StringBuilder(raw.Length + raw.Length / GroupSize);
        for (var i = 0; i < raw.Length; i++)
        {
            if (i > 0 && i % GroupSize == 0) grouped.Append('-');
            grouped.Append(raw[i]);
        }
        return grouped.ToString();
    }

    public static string Normalize(string code)
    {
        if (string.IsNullOrWhiteSpace(code)) return string.Empty;
        var sb = new StringBuilder(code.Length);
        foreach (var c in code)
        {
            if (c == '-' || char.IsWhiteSpace(c)) continue;
            sb.Append(char.ToUpperInvariant(c));
        }
        return sb.ToString();
    }

    public static string Hash(string code)
    {
        var bytes = Encoding.UTF8.GetBytes(Normalize(code));
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static string ToBase32(byte[] bytes)
    {
        var sb = new StringBuilder((bytes.Length * 8 + 4) / 5);
        int buffer = 0, bitsInBuffer = 0;
        foreach (var b in bytes)
        {
            buffer = (buffer << 8) | b;
            bitsInBuffer += 8;
            while (bitsInBuffer >= 5)
            {
                bitsInBuffer -= 5;
                sb.Append(Base32Alphabet[(buffer >> bitsInBuffer) & 0x1F]);
            }
        }
        if (bitsInBuffer > 0)
            sb.Append(Base32Alphabet[(buffer << (5 - bitsInBuffer)) & 0x1F]);
        return sb.ToString();
    }
}

/// <summary>The one-time result of minting an enroll code. <see cref="Code"/> is shown ONCE.</summary>
public sealed record EnrollCodeIssued(Guid Id, string Code, DateTime ExpiresAtUtc);

/// <summary>
/// Point-in-time claim status of a minted code, keyed by the mint id — NEVER by the code itself
/// (the plaintext is shown once and not retained; the portal polls by <see cref="Id"/>).
/// </summary>
public sealed record EnrollCodeStatus(Guid Id, DateTime ExpiresAtUtc, DateTime? UsedAtUtc, Guid? UsedByInstanceId);

/// <summary>
/// Control-plane store for single-use agent enrollment codes. A code is the tenant-admin
/// authorization for enrollment: minted by an admin in the tenant portal, short-TTL, hashed at
/// rest, and consumed atomically exactly once.
/// </summary>
public interface IEnrollCodeRepository
{
    /// <summary>Mints a code for the tenant. Returns the PLAINTEXT code exactly once; stores only its hash.</summary>
    Task<EnrollCodeIssued> CreateAsync(Guid tenantId, TimeSpan ttl, string? createdBy, CancellationToken cancellationToken = default);

    /// <summary>
    /// Atomically consumes the code: single-statement UPDATE that stamps UsedAt/UsedByInstanceId and
    /// returns the TenantId ONLY when the code exists, is unconsumed, and is unexpired — all in one
    /// predicate, so two racing callers can never both win. Returns null for unknown, expired, and
    /// already-used codes alike (the caller's uniform-403 contract).
    /// </summary>
    Task<Guid?> TryConsumeAsync(string codePlaintext, Guid instanceId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Read-only claim status for the verify card on /admin/download-conduit. Tenant-scoped: returns
    /// null when the id is unknown, purged, or belongs to another tenant (a portal circuit must never
    /// observe another tenant's codes). Null after expiry+purge is fine — callers treat it as expired.
    /// </summary>
    Task<EnrollCodeStatus?> GetStatusAsync(Guid id, Guid tenantId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Dapper implementation over the control-plane <c>EnrollCodes</c> table. Same connection sourcing
/// as the other control-plane repositories: <c>ConnectionStrings:ControlPlane</c>, never a tenant DB.
/// </summary>
public sealed class EnrollCodeRepository : IEnrollCodeRepository
{
    // Single-statement consume: the WHERE carries ALL three gates (match, unconsumed, unexpired)
    // and the UPDATE itself is the claim — no SELECT-then-UPDATE race window exists.
    internal const string ConsumeSql = @"
UPDATE EnrollCodes
SET UsedAt = SYSUTCDATETIME(), UsedByInstanceId = @InstanceId
OUTPUT inserted.TenantId
WHERE CodeHash = @CodeHash AND UsedAt IS NULL AND ExpiresAt > SYSUTCDATETIME()";

    // Opportunistic retention (Worf pass-2 M3): every mint sweeps codes that expired more than a
    // day ago — GLOBAL, not per-tenant, so the table stays bounded by mint volume with no job
    // infrastructure. The 1-day grace keeps recently-expired rows (and their UsedAt /
    // UsedByInstanceId trail) visible for incident forensics. Live and in-grace rows are untouched.
    internal const string PurgeSql = @"
DELETE FROM EnrollCodes WHERE ExpiresAt < DATEADD(DAY, -1, SYSUTCDATETIME())";

    // Read-only status probe (verify card). SELECT-only by contract — the single-use claim stays
    // exclusively TryConsumeAsync's atomic UPDATE. TenantId in the predicate keeps it tenant-scoped.
    internal const string StatusSql = @"
SELECT Id, ExpiresAt AS ExpiresAtUtc, UsedAt AS UsedAtUtc, UsedByInstanceId
FROM EnrollCodes WHERE Id = @Id AND TenantId = @TenantId";

    private readonly string _connectionString;
    private readonly IGlobalLogger _logger;

    public EnrollCodeRepository(IConfiguration configuration, IGlobalLogger logger)
    {
        _connectionString = configuration.GetConnectionString(ControlPlaneMigrationService.ConnectionStringName)
            ?? throw new InvalidOperationException(
                $"Control-plane connection string '{ControlPlaneMigrationService.ConnectionStringName}' not found. " +
                "Configure it via user-secrets (dev) or an environment variable / secret store (prod).");
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<EnrollCodeIssued> CreateAsync(
        Guid tenantId, TimeSpan ttl, string? createdBy, CancellationToken cancellationToken = default)
    {
        if (tenantId == Guid.Empty)
            throw new ArgumentException("TenantId is required.", nameof(tenantId));
        if (ttl <= TimeSpan.Zero)
            throw new ArgumentException("TTL must be positive.", nameof(ttl));

        var code = EnrollCodes.GenerateCode();
        var id = Guid.NewGuid();
        var expiresAt = DateTime.UtcNow.Add(ttl);

        await using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync(cancellationToken).ConfigureAwait(false);

        var purged = await conn.ExecuteAsync(PurgeSql).ConfigureAwait(false);
        if (purged > 0)
            _logger.LogInformation("Control-plane: purged {Count} expired enroll codes", purged);

        await conn.ExecuteAsync(@"
INSERT INTO EnrollCodes (Id, TenantId, CodeHash, ExpiresAt, CreatedAt, CreatedBy)
VALUES (@Id, @TenantId, @CodeHash, @ExpiresAt, @CreatedAt, @CreatedBy)",
            new
            {
                Id = id,
                TenantId = tenantId,
                CodeHash = EnrollCodes.Hash(code),
                ExpiresAt = expiresAt,
                CreatedAt = DateTime.UtcNow,
                CreatedBy = createdBy
            }).ConfigureAwait(false);

        // Log the mint WITHOUT the code (only id/tenant/expiry). The raw code never reaches a log sink.
        _logger.LogInformation("Control-plane: minted enroll code {CodeId} for tenant {TenantId}, expires {ExpiresAt:u}",
            id, tenantId, expiresAt);

        return new EnrollCodeIssued(id, code, expiresAt);
    }

    public async Task<Guid?> TryConsumeAsync(
        string codePlaintext, Guid instanceId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(codePlaintext) || instanceId == Guid.Empty)
            return null;

        await using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync(cancellationToken).ConfigureAwait(false);

        var tenantId = await conn.QuerySingleOrDefaultAsync<Guid?>(
            ConsumeSql,
            new { CodeHash = EnrollCodes.Hash(codePlaintext), InstanceId = instanceId }).ConfigureAwait(false);

        if (tenantId is not null)
            _logger.LogInformation("Control-plane: enroll code consumed for tenant {TenantId} by instance {InstanceId}",
                tenantId, instanceId);

        return tenantId;
    }

    public async Task<EnrollCodeStatus?> GetStatusAsync(
        Guid id, Guid tenantId, CancellationToken cancellationToken = default)
    {
        if (id == Guid.Empty || tenantId == Guid.Empty) return null;

        await using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync(cancellationToken).ConfigureAwait(false);

        return await conn.QuerySingleOrDefaultAsync<EnrollCodeStatus>(
            StatusSql, new { Id = id, TenantId = tenantId }).ConfigureAwait(false);
    }
}
