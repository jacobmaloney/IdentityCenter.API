using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace DataAccessLibrary.Services;

/// <summary>
/// Service for managing database schema versioning and applying migrations.
/// Uses Dapper for all database operations - no EF Core dependency.
/// Migrations are embedded SQL scripts that are applied in order.
/// </summary>
public class DatabaseMigrationService
{
    private string _connectionString;
    private readonly ILogger<DatabaseMigrationService> _logger;
    private readonly bool _failOnChecksumMismatch;

    private const string SchemaVersionTableName = "__SchemaVersion";

    // When true (default) a checksum mismatch on an already-applied migration halts startup;
    // an operator can downgrade fail->warn by setting this to false if a mismatch is known-benign.
    private const string ChecksumFailOnMismatchKey = "Migrations:ChecksumValidation:FailOnMismatch";

    public DatabaseMigrationService(
        IConfiguration configuration,
        ILogger<DatabaseMigrationService> logger)
    {
        _connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("DefaultConnection not found in configuration");
        _logger = logger;
        _failOnChecksumMismatch = configuration.GetValue<bool?>(ChecksumFailOnMismatchKey) ?? true;
    }

    /// <summary>
    /// Updates the connection string used by this service.
    /// Used during first-run setup when the connection string changes after DI construction.
    /// </summary>
    public void SetConnectionString(string connectionString)
    {
        _connectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
        _logger.LogInformation("DatabaseMigrationService connection string updated");
    }

    /// <summary>
    /// Ensures the target database exists, creating it if necessary.
    /// This replaces the implicit database creation that EF Core's Database.Migrate() provided.
    /// </summary>
    public async Task EnsureDatabaseExistsAsync()
    {
        var builder = new SqlConnectionStringBuilder(_connectionString);
        var databaseName = builder.InitialCatalog;

        if (string.IsNullOrEmpty(databaseName))
        {
            _logger.LogWarning("No database name found in connection string - skipping database creation");
            return;
        }

        // Connect to master to check/create the database
        builder.InitialCatalog = "master";
        await using var masterConn = new SqlConnection(builder.ConnectionString);
        await masterConn.OpenAsync();

        var exists = await masterConn.ExecuteScalarAsync<int>(
            "SELECT CASE WHEN DB_ID(@DbName) IS NOT NULL THEN 1 ELSE 0 END",
            new { DbName = databaseName });

        if (exists == 0)
        {
            _logger.LogInformation("Database '{Database}' does not exist - creating it", databaseName);
            // Database names can't be parameterized, but we've already extracted it from the connection string
            await masterConn.ExecuteAsync($"CREATE DATABASE [{databaseName}]");
            _logger.LogInformation("Database '{Database}' created successfully", databaseName);
        }
        else
        {
            _logger.LogInformation("Database '{Database}' already exists", databaseName);
        }
    }

    /// <summary>
    /// Gets the current database schema version.
    /// Returns 0 if the schema version table doesn't exist.
    /// </summary>
    public async Task<int> GetCurrentVersionAsync()
    {
        await using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();

        // Check if table exists first
        var tableExists = await conn.ExecuteScalarAsync<int>(
            @"SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES
              WHERE TABLE_NAME = @TableName",
            new { TableName = SchemaVersionTableName });

        if (tableExists == 0)
            return 0;

        var version = await conn.ExecuteScalarAsync<int?>(
            $"SELECT ISNULL(MAX(Version), 0) FROM {SchemaVersionTableName}");

        return version ?? 0;
    }

    /// <summary>
    /// Ensures the database schema is up to date by applying all pending migrations.
    /// This method is idempotent - it can be safely called multiple times.
    /// </summary>
    public async Task EnsureUpToDateAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Starting database migration check...");

        await using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync(cancellationToken);

        // Create version table if needed
        await EnsureSchemaVersionTableAsync(conn);

        var currentVersion = await GetCurrentVersionAsync();
        var allScripts = LoadEmbeddedScripts();
        _logger.LogInformation("Current database schema version: {Version}. Found {Total} embedded scripts (versions: {Versions})",
            currentVersion, allScripts.Count,
            string.Join(", ", allScripts.Select(s => $"V{s.Version:D3}")));

        await ValidateAppliedChecksumsAsync(conn, allScripts, currentVersion);

        var scripts = GetPendingScripts(currentVersion);
        if (!scripts.Any())
        {
            _logger.LogInformation("Database schema is up to date");
            return;
        }

        _logger.LogInformation("Found {Count} pending migration(s) to apply", scripts.Count);

        foreach (var script in scripts.OrderBy(s => s.Version))
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                _logger.LogInformation("Applying migration V{Version}: {ScriptName}", script.Version, script.ScriptName);

                // Execute migration script - each GO-separated batch runs independently.
                // We cannot wrap everything in a single transaction because CREATE PROCEDURE,
                // CREATE TYPE, and other DDL statements may fail inside transactions when
                // combined with other batches on some SQL Server versions.
                await ExecuteMigrationScriptAsync(conn, script.Content);

                // Record version (outside the script execution so it only records on full success)
                await conn.ExecuteAsync(
                    $@"INSERT INTO {SchemaVersionTableName} (Version, ScriptName, Checksum)
                       VALUES (@Version, @ScriptName, @Checksum)",
                    new { script.Version, script.ScriptName, script.Checksum });

                _logger.LogInformation("Successfully applied migration V{Version}: {ScriptName}", script.Version, script.ScriptName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to apply migration V{Version}: {ScriptName}", script.Version, script.ScriptName);
                throw new InvalidOperationException($"Migration V{script.Version} ({script.ScriptName}) failed: {ex.Message}", ex);
            }
        }

        var newVersion = await GetCurrentVersionAsync();
        _logger.LogInformation("Database migration complete. Schema version: {Version}", newVersion);
    }

    /// <summary>
    /// Gets a list of all applied migrations.
    /// </summary>
    public async Task<List<AppliedMigration>> GetAppliedMigrationsAsync()
    {
        await using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();

        var tableExists = await conn.ExecuteScalarAsync<int>(
            @"SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES
              WHERE TABLE_NAME = @TableName",
            new { TableName = SchemaVersionTableName });

        if (tableExists == 0)
            return new List<AppliedMigration>();

        var migrations = await conn.QueryAsync<AppliedMigration>(
            $@"SELECT Version, AppliedAt, ScriptName, Checksum
               FROM {SchemaVersionTableName}
               ORDER BY Version");

        return migrations.ToList();
    }

    /// <summary>
    /// Gets a list of pending migration scripts that haven't been applied yet.
    /// </summary>
    public List<MigrationScript> GetPendingScripts(int currentVersion)
    {
        var scripts = LoadEmbeddedScripts();
        return scripts.Where(s => s.Version > currentVersion).ToList();
    }

    /// <summary>
    /// Gets all available migration scripts.
    /// </summary>
    public List<MigrationScript> GetAllScripts()
    {
        return LoadEmbeddedScripts();
    }

    /// <summary>
    /// Validates that already-applied migrations (Version &lt;= currentVersion) still match the
    /// embedded script content they were applied from. The run-decision logic remains purely
    /// version-based; this is an integrity check alongside it. On mismatch we fail closed by
    /// default (throw + halt startup), or log a warning when <see cref="ChecksumFailOnMismatchKey"/>
    /// is set to false.
    /// </summary>
    private async Task ValidateAppliedChecksumsAsync(SqlConnection conn, List<MigrationScript> allScripts, int currentVersion)
    {
        var applied = (await conn.QueryAsync<AppliedMigration>(
            $@"SELECT Version, AppliedAt, ScriptName, Checksum
               FROM {SchemaVersionTableName}
               WHERE Version <= @CurrentVersion
               ORDER BY Version",
            new { CurrentVersion = currentVersion })).ToList();

        var findings = EvaluateAppliedChecksums(applied, allScripts);

        // Log first so the operator always gets the actionable line, then enforce the fail-closed policy.
        foreach (var finding in findings)
        {
            if (finding.Kind == ChecksumFindingKind.Drift && _failOnChecksumMismatch)
                _logger.LogError(finding.Message);
            else
                _logger.LogWarning(finding.Message);
        }

        EnforceFailClosedPolicy(findings, _failOnChecksumMismatch);
    }

    /// <summary>
    /// Applies the fail-closed policy to classified findings: throws on the first drift finding when
    /// <paramref name="failOnMismatch"/> is true, otherwise returns without throwing. Pure and
    /// side-effect-free so both the halt and the downgraded-to-warning paths are directly testable.
    /// </summary>
    internal static void EnforceFailClosedPolicy(IEnumerable<ChecksumFinding> findings, bool failOnMismatch)
    {
        if (!failOnMismatch)
            return;

        var drift = findings.FirstOrDefault(f => f.Kind == ChecksumFindingKind.Drift);
        if (drift is not null)
            throw new InvalidOperationException(drift.Message);
    }

    /// <summary>
    /// Pure, side-effect-free classification of already-applied migrations against the embedded
    /// scripts: emits a Drift finding when a stored checksum no longer matches its embedded script,
    /// and an UnknownApplied finding when an applied row has no matching embedded script. Rows with a
    /// NULL/empty stored checksum (pre-dating the Checksum column) are skipped. The caller applies the
    /// fail-closed policy; keeping this method free of DB access and logging makes it directly testable.
    /// </summary>
    internal static List<ChecksumFinding> EvaluateAppliedChecksums(
        IEnumerable<AppliedMigration> appliedRows,
        IEnumerable<MigrationScript> embeddedScripts)
    {
        var findings = new List<ChecksumFinding>();
        var scriptsByVersion = embeddedScripts.ToDictionary(s => s.Version);

        foreach (var row in appliedRows)
        {
            if (string.IsNullOrWhiteSpace(row.Checksum))
                continue;

            if (!scriptsByVersion.TryGetValue(row.Version, out var script))
            {
                findings.Add(new ChecksumFinding(
                    ChecksumFindingKind.UnknownApplied, row.Version, row.ScriptName,
                    $"Applied migration V{row.Version} ({row.ScriptName}) has no matching embedded script — unknown applied migration; skipping integrity check"));
                continue;
            }

            if (!string.Equals(row.Checksum, script.Checksum, StringComparison.OrdinalIgnoreCase))
            {
                findings.Add(new ChecksumFinding(
                    ChecksumFindingKind.Drift, script.Version, script.ScriptName,
                    $"Migration V{script.Version} ({script.ScriptName}): applied migration content has changed since it was applied (schema drift) — investigate before starting."));
            }
        }

        return findings;
    }

    private async Task EnsureSchemaVersionTableAsync(SqlConnection conn)
    {
        await conn.ExecuteAsync($@"
            IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = '{SchemaVersionTableName}')
            BEGIN
                CREATE TABLE {SchemaVersionTableName} (
                    Version INT PRIMARY KEY,
                    AppliedAt DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
                    ScriptName NVARCHAR(256) NOT NULL,
                    Checksum NVARCHAR(64) NULL
                )
            END");
    }

    private async Task ExecuteMigrationScriptAsync(SqlConnection conn, string script)
    {
        // Split script on GO statements (SQL Server batch separator)
        // Each batch executes independently (no wrapping transaction) because
        // CREATE PROCEDURE, CREATE TYPE, etc. require their own batch context.
        var batches = Regex.Split(script, @"^\s*GO\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase)
            .Where(b => !string.IsNullOrWhiteSpace(b))
            .ToList();

        foreach (var batch in batches)
        {
            await conn.ExecuteAsync(batch, commandTimeout: 300);
        }
    }

    private List<MigrationScript> LoadEmbeddedScripts()
    {
        var scripts = new List<MigrationScript>();
        var assembly = Assembly.GetExecutingAssembly();

        // Look for embedded resources matching the pattern V{version}__{description}.sql
        var resourceNames = assembly.GetManifestResourceNames()
            .Where(name => name.Contains("Migrations.Scripts") && name.EndsWith(".sql"))
            .ToList();

        foreach (var resourceName in resourceNames)
        {
            try
            {
                var fileName = resourceName.Split('.').TakeLast(2).First() + ".sql";

                // Parse version from filename: V001__Description.sql
                var match = Regex.Match(fileName, @"^V(\d+)__(.+)\.sql$", RegexOptions.IgnoreCase);
                if (!match.Success)
                {
                    _logger.LogWarning("Skipping migration script with invalid name format: {FileName}", fileName);
                    continue;
                }

                var version = int.Parse(match.Groups[1].Value);
                var description = match.Groups[2].Value.Replace("_", " ");

                using var stream = assembly.GetManifestResourceStream(resourceName);
                if (stream == null)
                {
                    _logger.LogWarning("Could not read embedded resource: {ResourceName}", resourceName);
                    continue;
                }

                using var reader = new StreamReader(stream);
                var content = reader.ReadToEnd();

                scripts.Add(new MigrationScript
                {
                    Version = version,
                    ScriptName = fileName,
                    Description = description,
                    Content = content,
                    Checksum = ComputeChecksum(content)
                });
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to load migration script: {ResourceName}", resourceName);
            }
        }

        return scripts.OrderBy(s => s.Version).ToList();
    }

    private static string ComputeChecksum(string content)
    {
        using var sha256 = SHA256.Create();
        var bytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(content));
        return Convert.ToHexString(bytes);
    }
}

/// <summary>
/// Represents a migration script to be applied
/// </summary>
public class MigrationScript
{
    public int Version { get; set; }
    public string ScriptName { get; set; } = "";
    public string Description { get; set; } = "";
    public string Content { get; set; } = "";
    public string Checksum { get; set; } = "";
}

/// <summary>
/// Represents a migration that has been applied to the database
/// </summary>
public class AppliedMigration
{
    public int Version { get; set; }
    public DateTime AppliedAt { get; set; }
    public string ScriptName { get; set; } = "";
    public string? Checksum { get; set; }
}

internal enum ChecksumFindingKind
{
    Drift,
    UnknownApplied
}

internal sealed record ChecksumFinding(ChecksumFindingKind Kind, int Version, string ScriptName, string Message);
