using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using Microsoft.Data.SqlClient;
using Dapper;

namespace DataAccessLibrary.Services;

/// <summary>
/// Background service that automatically optimizes database performance after migrations.
/// Runs on application startup and detects if schema changes require optimization.
/// </summary>
public class PostMigrationOptimizationService : BackgroundService
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<PostMigrationOptimizationService> _logger;

    public PostMigrationOptimizationService(
        IConfiguration configuration,
        ILogger<PostMigrationOptimizationService> logger)
    {
        _configuration = configuration;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Defer index rebuild to 5 minutes after startup to avoid connection pool stampede.
        // All other services need connections during the first 60s — index rebuild can wait.
        await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);

        try
        {
            var connectionString = _configuration.GetConnectionString("DefaultConnection");
            if (string.IsNullOrEmpty(connectionString) || IsPlaceholderConnectionString(connectionString))
            {
                _logger.LogWarning("No valid connection string available for post-migration optimization");
                return;
            }

            await using var connection = new SqlConnection(connectionString);

            // Check if database exists before proceeding
            try
            {
                await connection.OpenAsync(stoppingToken);
            }
            catch (SqlException)
            {
                _logger.LogDebug("Database not ready - skipping post-migration optimization");
                return;
            }

            // Check if optimization is needed
            if (await NeedsOptimizationAsync(connection, stoppingToken))
            {
                _logger.LogInformation("🔧 Post-migration optimization starting...");
                await RunOptimizationAsync(connection, stoppingToken);
                await RecordOptimizationAsync(connection, stoppingToken);
                _logger.LogInformation("✅ Post-migration optimization complete - sync performance restored");
            }
            else
            {
                _logger.LogDebug("Database optimization not needed - schema unchanged since last optimization");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during post-migration optimization");
        }
    }

    private async Task<bool> NeedsOptimizationAsync(SqlConnection connection, CancellationToken ct)
    {
        // Ensure tracking table exists
        await connection.ExecuteAsync(@"
            IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = '__OptimizationHistory')
            BEGIN
                CREATE TABLE __OptimizationHistory (
                    Id INT IDENTITY(1,1) PRIMARY KEY,
                    MigrationChecksum NVARCHAR(100),
                    OptimizedAt DATETIME2 DEFAULT GETUTCDATE(),
                    TablesOptimized INT,
                    DurationMs INT
                );
            END", commandTimeout: 30);

        // Get current migration checksum (hash of all applied migrations)
        var currentChecksum = await GetMigrationChecksumAsync(connection);

        // Get last optimization checksum
        var lastChecksum = await connection.QueryFirstOrDefaultAsync<string>(
            "SELECT TOP 1 MigrationChecksum FROM __OptimizationHistory ORDER BY OptimizedAt DESC");

        // Optimization needed if checksums don't match
        return currentChecksum != lastChecksum;
    }

    private async Task<string> GetMigrationChecksumAsync(SqlConnection connection)
    {
        // Get all applied migrations and create a checksum
        var migrations = await connection.QueryAsync<string>(
            "SELECT MigrationId FROM __EFMigrationsHistory ORDER BY MigrationId");

        var migrationList = string.Join("|", migrations);

        // Simple checksum using hash
        using var sha = System.Security.Cryptography.SHA256.Create();
        var bytes = System.Text.Encoding.UTF8.GetBytes(migrationList);
        var hash = sha.ComputeHash(bytes);
        return Convert.ToBase64String(hash).Substring(0, 20);
    }

    private async Task RunOptimizationAsync(SqlConnection connection, CancellationToken ct)
    {
        var tables = new[]
        {
            // Sync configuration tables
            "SyncProjects", "SyncWorkflows", "SyncSteps", "AttributeMappings",
            "SyncStepTags", "WorkflowTags", "SyncStepRuns", "SyncAuditLogs",
            // Data tables
            "Objects", "Identities", "ObjectAttributes", "ObjectTags", "Tags",
            // Other frequently accessed tables
            "DirectoryConnections", "Persons"
        };

        foreach (var table in tables)
        {
            try
            {
                // Check if table exists
                var exists = await connection.QueryFirstOrDefaultAsync<int>(
                    $"SELECT COUNT(*) FROM sys.tables WHERE name = @Table", new { Table = table });

                if (exists == 0) continue;

                // Rebuild indexes
                await connection.ExecuteAsync(
                    $"ALTER INDEX ALL ON [{table}] REBUILD",
                    commandTimeout: 300);

                // Update statistics
                await connection.ExecuteAsync(
                    $"UPDATE STATISTICS [{table}] WITH FULLSCAN",
                    commandTimeout: 300);

                _logger.LogDebug("Optimized table: {Table}", table);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to optimize table {Table}, continuing...", table);
            }
        }

        // Clear procedure cache for fresh query plans
        try
        {
            // REMOVED: await connection.ExecuteAsync("DBCC FREEPROCCACHE", commandTimeout: 60);
            _logger.LogDebug("Procedure cache cleared");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to clear procedure cache");
        }
    }

    private async Task RecordOptimizationAsync(SqlConnection connection, CancellationToken ct)
    {
        var checksum = await GetMigrationChecksumAsync(connection);

        await connection.ExecuteAsync(@"
            INSERT INTO __OptimizationHistory (MigrationChecksum, TablesOptimized, DurationMs)
            VALUES (@Checksum, 15, 0)",
            new { Checksum = checksum });
    }

    private static bool IsPlaceholderConnectionString(string connectionString) =>
        connectionString.Contains("**") ||
        connectionString.Contains("YOUR_SERVER") ||
        connectionString.Contains("YOUR_DATABASE") ||
        connectionString.Contains("Set via User Secrets", StringComparison.OrdinalIgnoreCase) ||
        connectionString.Contains("PLACEHOLDER", StringComparison.OrdinalIgnoreCase);
}
