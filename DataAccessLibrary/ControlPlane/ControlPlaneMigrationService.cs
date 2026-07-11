using Dapper;
using Logging;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;

namespace DataAccessLibrary.ControlPlane;

/// <summary>
/// Idempotent bootstrap for the SaaS CONTROL-PLANE database (the tenant registry).
///
/// This is deliberately SEPARATE from <see cref="Services.DatabaseMigrationService"/> and the
/// per-tenant V001..V135 scripts. The control-plane schema (a single <c>Tenants</c> table) must
/// never be mixed into the tenant migration stream:
///   - tenant DBs are created/migrated per-customer and contain governance data;
///   - the control-plane DB is a single global registry that maps slugs → tenant DBs.
///
/// Mirrors the proven primitive in DatabaseMigrationService.EnsureDatabaseExistsAsync: connect to
/// <c>master</c>, create the catalog if absent, then create the table if absent. Safe to call on
/// every startup.
///
/// CONFIG: the control-plane connection string is read from <c>ConnectionStrings:ControlPlane</c>,
/// which MUST be supplied from secure config (user-secrets in dev, environment variable / secret
/// store in prod) — NOT hardcoded and NOT the tenant <c>DefaultConnection</c>.
/// </summary>
public sealed class ControlPlaneMigrationService
{
    public const string ConnectionStringName = "ControlPlane";

    private readonly string _connectionString;
    private readonly IGlobalLogger _logger;

    public ControlPlaneMigrationService(IConfiguration configuration, IGlobalLogger logger)
    {
        _connectionString = configuration.GetConnectionString(ConnectionStringName)
            ?? throw new InvalidOperationException(
                $"Control-plane connection string '{ConnectionStringName}' not found. " +
                "Configure it via user-secrets (dev) or an environment variable / secret store (prod). " +
                "Do not hardcode it.");
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Ensures the control-plane database AND the Tenants table exist. Idempotent.
    /// </summary>
    public async Task EnsureCreatedAsync(CancellationToken cancellationToken = default)
    {
        await EnsureDatabaseExistsAsync(cancellationToken).ConfigureAwait(false);
        await EnsureTenantsTableAsync(cancellationToken).ConfigureAwait(false);
        await EnsureTenantsColumnsAsync(cancellationToken).ConfigureAwait(false);
        await EnsureTenantApiKeysTableAsync(cancellationToken).ConfigureAwait(false);
        await EnsureTenantApiKeysColumnsAsync(cancellationToken).ConfigureAwait(false);
        await EnsureEnrollCodesTableAsync(cancellationToken).ConfigureAwait(false);
        await EnsureControlPlaneAuditLogTableAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task EnsureDatabaseExistsAsync(CancellationToken cancellationToken)
    {
        var builder = new SqlConnectionStringBuilder(_connectionString);
        var databaseName = builder.InitialCatalog;

        if (string.IsNullOrEmpty(databaseName))
            throw new InvalidOperationException(
                "Control-plane connection string has no Initial Catalog / Database. " +
                "Specify the control-plane database name (e.g. IdentityCenterControlPlane).");

        // The catalog name comes from OUR connection string (config), never from user input,
        // so bracket-interpolation here is safe — same trust model as DatabaseMigrationService.
        builder.InitialCatalog = "master";
        await using var masterConn = new SqlConnection(builder.ConnectionString);
        await masterConn.OpenAsync(cancellationToken).ConfigureAwait(false);

        var exists = await masterConn.ExecuteScalarAsync<int>(
            "SELECT CASE WHEN DB_ID(@DbName) IS NOT NULL THEN 1 ELSE 0 END",
            new { DbName = databaseName }).ConfigureAwait(false);

        if (exists == 0)
        {
            _logger.LogInformation("Control-plane database '{Db}' does not exist - creating it", databaseName);
            var safeName = databaseName.Replace("]", "]]");
            await masterConn.ExecuteAsync($"CREATE DATABASE [{safeName}]").ConfigureAwait(false);
            _logger.LogInformation("Control-plane database '{Db}' created", databaseName);
        }
        else
        {
            _logger.LogInformation("Control-plane database '{Db}' already exists", databaseName);
        }
    }

    private async Task EnsureTenantsTableAsync(CancellationToken cancellationToken)
    {
        await using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync(cancellationToken).ConfigureAwait(false);

        // Idempotent DDL: table + a UNIQUE index on Slug. Slug is the natural lookup key and
        // must be globally unique (it maps 1:1 to a physical tenant DB name).
        await conn.ExecuteAsync(@"
IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'Tenants')
BEGIN
    CREATE TABLE Tenants (
        Id                      UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_Tenants PRIMARY KEY,
        Slug                    NVARCHAR(40)     NOT NULL,
        DisplayName             NVARCHAR(256)    NOT NULL,
        Status                  NVARCHAR(32)     NOT NULL CONSTRAINT DF_Tenants_Status DEFAULT 'Provisioning',
        Region                  NVARCHAR(64)     NULL,
        IcDbConnectionString    NVARCHAR(MAX)    NULL,   -- ENCRYPTED at rest (enc: sentinel)
        ConduitBaseUrl          NVARCHAR(512)    NULL,
        ConduitTokenEncrypted   NVARCHAR(MAX)    NULL,   -- ENCRYPTED at rest (enc: sentinel)
        [Plan]                  NVARCHAR(64)     NULL,   -- room for billing later ('Plan' is a reserved word — must be bracketed)
        CreatedAt               DATETIME2        NOT NULL CONSTRAINT DF_Tenants_CreatedAt DEFAULT SYSUTCDATETIME(),
        ModifiedAt              DATETIME2        NOT NULL CONSTRAINT DF_Tenants_ModifiedAt DEFAULT SYSUTCDATETIME()
    );

    CREATE UNIQUE INDEX UX_Tenants_Slug ON Tenants (Slug);
END
", commandTimeout: 120).ConfigureAwait(false);

        _logger.LogInformation("Control-plane Tenants table verified/created");
    }

    /// <summary>
    /// Idempotent, column-level migration for the Tenants table — for registries that already exist
    /// from before a column was introduced. Each ALTER is guarded by INFORMATION_SCHEMA.COLUMNS so it
    /// runs at most once and is safe on every startup. New columns added here MUST be nullable (or have
    /// a DEFAULT) so the add never fails against existing rows.
    ///
    /// This is the control-plane analogue of the per-column guard pattern; it must NEVER be folded into
    /// the per-tenant V001..V135 stream.
    /// </summary>
    private async Task EnsureTenantsColumnsAsync(CancellationToken cancellationToken)
    {
        await using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync(cancellationToken).ConfigureAwait(false);

        // TrialExpiresAt: when a Trial-plan tenant's trial ends. NULL for non-trial / unset.
        // No enforcement here — recording only. (See follow-up: trial-expiry enforcement job.)
        await conn.ExecuteAsync(@"
IF NOT EXISTS (
    SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
    WHERE TABLE_NAME = 'Tenants' AND COLUMN_NAME = 'TrialExpiresAt')
BEGIN
    ALTER TABLE Tenants ADD TrialExpiresAt DATETIME2 NULL;
END
", commandTimeout: 120).ConfigureAwait(false);

        _logger.LogInformation("Control-plane Tenants columns verified (TrialExpiresAt)");
    }

    /// <summary>
    /// Idempotent DDL for the control-plane <c>TenantApiKeys</c> table — the auth authority for the
    /// multi-tenant API. A presented key is resolved here to {TenantId, Scope} BEFORE any tenant DB is
    /// opened, so the key store MUST live in the control plane, never in a tenant catalog.
    ///
    /// This is the control-plane analogue of a V-script — it must NEVER be folded into the per-tenant
    /// V001..V135 stream. Guarded by INFORMATION_SCHEMA so it is safe on every startup.
    ///
    /// TenantId is NULL for an admin/control-plane key and FK-references Tenants(Id) for a tenant key.
    /// ON DELETE CASCADE: deleting a tenant registry row also drops that tenant's keys (the keys are
    /// worthless once the tenant is gone, and this prevents orphaned keys lingering as a credential).
    /// </summary>
    private async Task EnsureTenantApiKeysTableAsync(CancellationToken cancellationToken)
    {
        await using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync(cancellationToken).ConfigureAwait(false);

        await conn.ExecuteAsync(@"
IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'TenantApiKeys')
BEGIN
    CREATE TABLE TenantApiKeys (
        Id          UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_TenantApiKeys PRIMARY KEY,
        TenantId    UNIQUEIDENTIFIER NULL,   -- NULL = control-plane/admin key; else FK to Tenants(Id)
        KeyHash     NVARCHAR(128)    NOT NULL, -- SHA-256 hex (lowercase) of the raw key; raw never stored
        KeyPrefix   NVARCHAR(16)     NOT NULL, -- first 8 chars of the raw key (non-secret lookup discriminant)
        Scope       NVARCHAR(16)     NOT NULL, -- 'Admin' | 'Tenant'
        Name        NVARCHAR(256)    NOT NULL,
        CreatedAt   DATETIME2        NOT NULL CONSTRAINT DF_TenantApiKeys_CreatedAt DEFAULT SYSUTCDATETIME(),
        RevokedAt   DATETIME2        NULL,
        CONSTRAINT FK_TenantApiKeys_Tenants FOREIGN KEY (TenantId)
            REFERENCES Tenants (Id) ON DELETE CASCADE
    );

    -- Unique on KeyHash: a hash collision (or a duplicate mint) is a hard error, not a silent dupe.
    CREATE UNIQUE INDEX UX_TenantApiKeys_KeyHash ON TenantApiKeys (KeyHash);
    -- Covering seek for validation (hash + prefix) and for per-tenant listing.
    CREATE INDEX IX_TenantApiKeys_Prefix ON TenantApiKeys (KeyPrefix);
    CREATE INDEX IX_TenantApiKeys_TenantId ON TenantApiKeys (TenantId);
END
", commandTimeout: 120).ConfigureAwait(false);

        _logger.LogInformation("Control-plane TenantApiKeys table verified/created");
    }

    /// <summary>
    /// Idempotent column-level migration for TenantApiKeys (registries created before a column was
    /// introduced). Same pattern as <see cref="EnsureTenantsColumnsAsync"/>: guarded, nullable, safe on
    /// every startup. AgentId carries the Conduit instance GUID for Scope='Agent' keys (Day 4 enroll) —
    /// NULL for Admin/Tenant keys. Scope='Agent' itself needs no DDL (Scope is already NVARCHAR(16)).
    /// </summary>
    private async Task EnsureTenantApiKeysColumnsAsync(CancellationToken cancellationToken)
    {
        await using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync(cancellationToken).ConfigureAwait(false);

        await conn.ExecuteAsync(@"
IF NOT EXISTS (
    SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
    WHERE TABLE_NAME = 'TenantApiKeys' AND COLUMN_NAME = 'AgentId')
BEGIN
    ALTER TABLE TenantApiKeys ADD AgentId UNIQUEIDENTIFIER NULL;
END
", commandTimeout: 120).ConfigureAwait(false);

        // ExpiresAt (Day 5): optional key expiry. NULL = non-expiring — every key minted before
        // this column existed keeps working forever (grandfathered). Expiry only ever comes from
        // an explicit TTL at mint time, the ControlPlane:ApiKeyDefaultTtlDays config, or a
        // rotation grace stamp.
        await conn.ExecuteAsync(@"
IF NOT EXISTS (
    SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
    WHERE TABLE_NAME = 'TenantApiKeys' AND COLUMN_NAME = 'ExpiresAt')
BEGIN
    ALTER TABLE TenantApiKeys ADD ExpiresAt DATETIME2 NULL;
END
", commandTimeout: 120).ConfigureAwait(false);

        _logger.LogInformation("Control-plane TenantApiKeys columns verified (AgentId, ExpiresAt)");
    }

    /// <summary>
    /// Idempotent DDL for the <c>EnrollCodes</c> table — single-use, short-TTL agent enrollment codes
    /// (Day 4: POST /api/agent/enroll). A code is minted by a tenant admin in the portal, stored as its
    /// SHA-256 hex only (same scheme as TenantApiKeys), and consumed atomically exactly once by
    /// <see cref="EnrollCodeRepository.TryConsumeAsync"/>. ON DELETE CASCADE: codes are worthless once
    /// the tenant is gone.
    /// </summary>
    private async Task EnsureEnrollCodesTableAsync(CancellationToken cancellationToken)
    {
        await using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync(cancellationToken).ConfigureAwait(false);

        await conn.ExecuteAsync(@"
IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'EnrollCodes')
BEGIN
    CREATE TABLE EnrollCodes (
        Id               UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_EnrollCodes PRIMARY KEY,
        TenantId         UNIQUEIDENTIFIER NOT NULL,
        CodeHash         NVARCHAR(128)    NOT NULL, -- SHA-256 hex (lowercase) of the normalized code; raw never stored
        ExpiresAt        DATETIME2        NOT NULL,
        UsedAt           DATETIME2        NULL,     -- single-use stamp; NULL = unconsumed
        UsedByInstanceId UNIQUEIDENTIFIER NULL,
        CreatedAt        DATETIME2        NOT NULL CONSTRAINT DF_EnrollCodes_CreatedAt DEFAULT SYSUTCDATETIME(),
        CreatedBy        NVARCHAR(256)    NULL,
        CONSTRAINT FK_EnrollCodes_Tenants FOREIGN KEY (TenantId)
            REFERENCES Tenants (Id) ON DELETE CASCADE
    );

    CREATE INDEX IX_EnrollCodes_CodeHash ON EnrollCodes (CodeHash);
END
", commandTimeout: 120).ConfigureAwait(false);

        _logger.LogInformation("Control-plane EnrollCodes table verified/created");
    }

    /// <summary>
    /// Idempotent DDL for the <c>ControlPlaneAuditLog</c> table — append-only trail for control-plane
    /// actions (enrollment, key mgmt, suspend/resume/delete). Detail must never contain secrets.
    /// </summary>
    private async Task EnsureControlPlaneAuditLogTableAsync(CancellationToken cancellationToken)
    {
        await using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync(cancellationToken).ConfigureAwait(false);

        await conn.ExecuteAsync(@"
IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'ControlPlaneAuditLog')
BEGIN
    CREATE TABLE ControlPlaneAuditLog (
        Id        BIGINT           NOT NULL IDENTITY(1,1) CONSTRAINT PK_ControlPlaneAuditLog PRIMARY KEY,
        At        DATETIME2        NOT NULL CONSTRAINT DF_ControlPlaneAuditLog_At DEFAULT SYSUTCDATETIME(),
        Actor     NVARCHAR(256)    NOT NULL, -- API-key name/prefix or 'agent-enroll'
        Action    NVARCHAR(64)     NOT NULL,
        TenantId  UNIQUEIDENTIFIER NULL,
        Slug      NVARCHAR(40)     NULL,
        ClientIp  NVARCHAR(64)     NULL,
        Detail    NVARCHAR(MAX)    NULL
    );

    CREATE INDEX IX_ControlPlaneAuditLog_TenantId_At ON ControlPlaneAuditLog (TenantId, At);
END
", commandTimeout: 120).ConfigureAwait(false);

        _logger.LogInformation("Control-plane ControlPlaneAuditLog table verified/created");
    }
}
