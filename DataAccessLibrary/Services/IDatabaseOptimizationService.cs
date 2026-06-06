namespace DataAccessLibrary.Services;

/// <summary>
/// Service for database optimization operations.
/// Provides index rebuilding and statistics updates for sync-related tables.
/// </summary>
public interface IDatabaseOptimizationService
{
    /// <summary>
    /// Runs full database optimization (index rebuild + statistics update) on sync-related tables.
    /// </summary>
    /// <param name="progress">Optional progress reporter for tracking table-by-table progress</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Result containing success status, tables optimized, and duration</returns>
    Task<OptimizationResult> RunOptimizationAsync(
        IProgress<OptimizationProgress>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks if optimization is needed based on migration checksum.
    /// </summary>
    Task<bool> NeedsOptimizationAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the list of tables that will be optimized.
    /// </summary>
    string[] GetTablesToOptimize();
}

/// <summary>
/// Progress information during database optimization.
/// </summary>
public class OptimizationProgress
{
    /// <summary>
    /// Name of the table currently being optimized
    /// </summary>
    public string CurrentTable { get; set; } = "";

    /// <summary>
    /// Current operation being performed: "Rebuilding indexes" or "Updating statistics"
    /// </summary>
    public string CurrentOperation { get; set; } = "";

    /// <summary>
    /// Number of tables completed so far
    /// </summary>
    public int TablesCompleted { get; set; }

    /// <summary>
    /// Total number of tables to optimize
    /// </summary>
    public int TotalTables { get; set; }

    /// <summary>
    /// Progress percentage (0-100)
    /// </summary>
    public int ProgressPercentage => TotalTables > 0 ? (TablesCompleted * 100) / TotalTables : 0;
}

/// <summary>
/// Result of a database optimization operation.
/// </summary>
public class OptimizationResult
{
    /// <summary>
    /// Whether optimization completed successfully
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// Number of tables successfully optimized
    /// </summary>
    public int TablesOptimized { get; set; }

    /// <summary>
    /// Total number of tables attempted
    /// </summary>
    public int TotalTables { get; set; }

    /// <summary>
    /// Duration of optimization in seconds
    /// </summary>
    public int DurationSeconds { get; set; }

    /// <summary>
    /// Error message if optimization failed
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// List of tables that failed to optimize (if any)
    /// </summary>
    public List<string> FailedTables { get; set; } = new();
}
