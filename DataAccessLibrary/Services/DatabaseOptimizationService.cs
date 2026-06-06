using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using Microsoft.Data.SqlClient;
using Dapper;
using System.Diagnostics;

namespace DataAccessLibrary.Services;

/// <summary>
/// Database optimization service that rebuilds indexes and updates statistics on sync-related tables.
/// Can be called on-demand (pre-sync) or automatically (post-migration).
/// </summary>
public class DatabaseOptimizationService : IDatabaseOptimizationService
{
    private readonly string _connectionString;
    private readonly ILogger<DatabaseOptimizationService> _logger;

    /// <summary>
    /// Tables to optimize - ordered by importance for sync performance.
    /// </summary>
    private static readonly string[] _tablesToOptimize = new[]
    {
        // Core data tables (most important for sync performance)
        "Objects",
        "Identities",
        "ObjectAttributes",
        "ObjectTags",

        // Sync execution tables
        "SyncStepRuns",
        "SyncAuditLogs",
        "PostSyncTasks",

        // Sync configuration tables
        "SyncProjects",
        "SyncWorkflows",
        "SyncSteps",
        "AttributeMappings",

        // Supporting tables
        "SyncStepTags",
        "WorkflowTags",
        "Tags",
        "DirectoryConnections"
    };

    public DatabaseOptimizationService(
        IConfiguration configuration,
        ILogger<DatabaseOptimizationService> logger)
    {
        _connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found");
        _logger = logger;
    }

    /// <inheritdoc />
    public string[] GetTablesToOptimize() => _tablesToOptimize;

    /// <inheritdoc />
    public async Task<bool> NeedsOptimizationAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

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
                END", commandTimeout: 30).ConfigureAwait(false);

            // Get current migration checksum
            var currentChecksum = await GetMigrationChecksumAsync(connection).ConfigureAwait(false);

            // Get last optimization checksum
            var lastChecksum = await connection.QueryFirstOrDefaultAsync<string>(
                "SELECT TOP 1 MigrationChecksum FROM __OptimizationHistory ORDER BY OptimizedAt DESC").ConfigureAwait(false);

            return currentChecksum != lastChecksum;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error checking if optimization is needed, assuming yes");
            return true;
        }
    }

    /// <inheritdoc />
    public async Task<OptimizationResult> RunOptimizationAsync(
        IProgress<OptimizationProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var result = new OptimizationResult
        {
            TotalTables = _tablesToOptimize.Length
        };

        var stopwatch = Stopwatch.StartNew();

        try
        {
            await using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            _logger.LogInformation("Database optimization starting - {TableCount} tables to process", _tablesToOptimize.Length);

            var tablesCompleted = 0;

            foreach (var table in _tablesToOptimize)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    // Report progress - starting table
                    progress?.Report(new OptimizationProgress
                    {
                        CurrentTable = table,
                        CurrentOperation = "Checking table",
                        TablesCompleted = tablesCompleted,
                        TotalTables = _tablesToOptimize.Length
                    });

                    // Check if table exists
                    var exists = await connection.QueryFirstOrDefaultAsync<int>(
                        "SELECT COUNT(*) FROM sys.tables WHERE name = @Table",
                        new { Table = table }).ConfigureAwait(false);

                    if (exists == 0)
                    {
                        _logger.LogDebug("Table {Table} does not exist, skipping", table);
                        tablesCompleted++;
                        continue;
                    }

                    // Rebuild indexes
                    progress?.Report(new OptimizationProgress
                    {
                        CurrentTable = table,
                        CurrentOperation = "Rebuilding indexes",
                        TablesCompleted = tablesCompleted,
                        TotalTables = _tablesToOptimize.Length
                    });

                    await connection.ExecuteAsync(
                        $"ALTER INDEX ALL ON [{table}] REBUILD",
                        commandTimeout: 300).ConfigureAwait(false);

                    // Update statistics
                    progress?.Report(new OptimizationProgress
                    {
                        CurrentTable = table,
                        CurrentOperation = "Updating statistics",
                        TablesCompleted = tablesCompleted,
                        TotalTables = _tablesToOptimize.Length
                    });

                    await connection.ExecuteAsync(
                        $"UPDATE STATISTICS [{table}] WITH FULLSCAN",
                        commandTimeout: 300).ConfigureAwait(false);

                    result.TablesOptimized++;
                    tablesCompleted++;

                    _logger.LogDebug("Optimized table: {Table}", table);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to optimize table {Table}, continuing...", table);
                    result.FailedTables.Add(table);
                    tablesCompleted++;
                }
            }

            // Final progress report
            progress?.Report(new OptimizationProgress
            {
                CurrentTable = "Complete",
                CurrentOperation = "Done",
                TablesCompleted = tablesCompleted,
                TotalTables = _tablesToOptimize.Length
            });

            // Record optimization for checksum tracking
            await RecordOptimizationAsync(connection, result.TablesOptimized, (int)stopwatch.ElapsedMilliseconds).ConfigureAwait(false);

            result.Success = true;
            result.DurationSeconds = (int)stopwatch.Elapsed.TotalSeconds;

            _logger.LogInformation(
                "Database optimization completed in {Duration}s - {TablesOptimized}/{TotalTables} tables optimized",
                result.DurationSeconds,
                result.TablesOptimized,
                _tablesToOptimize.Length);
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("Database optimization was cancelled");
            result.Success = false;
            result.ErrorMessage = "Optimization was cancelled";
            result.DurationSeconds = (int)stopwatch.Elapsed.TotalSeconds;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Database optimization failed");
            result.Success = false;
            result.ErrorMessage = ex.Message;
            result.DurationSeconds = (int)stopwatch.Elapsed.TotalSeconds;
        }

        return result;
    }

    private async Task<string> GetMigrationChecksumAsync(SqlConnection connection)
    {
        // Get all applied migrations and create a checksum
        var migrations = await connection.QueryAsync<string>(
            "SELECT MigrationId FROM __EFMigrationsHistory ORDER BY MigrationId").ConfigureAwait(false);

        var migrationList = string.Join("|", migrations);

        // Simple checksum using hash
        using var sha = System.Security.Cryptography.SHA256.Create();
        var bytes = System.Text.Encoding.UTF8.GetBytes(migrationList);
        var hash = sha.ComputeHash(bytes);
        return Convert.ToBase64String(hash).Substring(0, 20);
    }

    private async Task RecordOptimizationAsync(SqlConnection connection, int tablesOptimized, int durationMs)
    {
        try
        {
            var checksum = await GetMigrationChecksumAsync(connection).ConfigureAwait(false);

            await connection.ExecuteAsync(@"
                INSERT INTO __OptimizationHistory (MigrationChecksum, TablesOptimized, DurationMs)
                VALUES (@Checksum, @TablesOptimized, @DurationMs)",
                new { Checksum = checksum, TablesOptimized = tablesOptimized, DurationMs = durationMs }).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to record optimization history");
        }
    }
}
