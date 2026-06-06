using DataAccessLibrary.Models;
using DataAccessLibrary.Repositories;

namespace DataAccessLibrary.Services.Scripting;

/// <summary>
/// Context provided to pre-processing scripts.
/// Pre-processing scripts can modify source data before attribute mapping.
/// </summary>
public class PreProcessingContext
{
    /// <summary>
    /// The raw source objects from LDAP query.
    /// Scripts can modify this collection (filter, transform, enrich).
    /// </summary>
    public List<Dictionary<string, object>> SourceObjects { get; set; } = new();

    /// <summary>
    /// The sync step being executed.
    /// </summary>
    public SyncStep Step { get; }

    /// <summary>
    /// The parent sync project.
    /// </summary>
    public SyncProject Project { get; }

    /// <summary>
    /// The current step run (for logging and metrics).
    /// </summary>
    public SyncStepRun StepRun { get; }

    /// <summary>
    /// Logger for script output.
    /// </summary>
    public IScriptLogger Log { get; }

    /// <summary>
    /// Repository for database operations.
    /// </summary>
    public ISyncRepository Repository { get; }

    /// <summary>
    /// Cancellation token for graceful shutdown.
    /// </summary>
    public CancellationToken CancellationToken { get; }

    /// <summary>
    /// Source connection ID for context.
    /// </summary>
    public Guid SourceConnectionId { get; }

    public PreProcessingContext(
        List<Dictionary<string, object>> sourceObjects,
        SyncStep step,
        SyncProject project,
        SyncStepRun stepRun,
        IScriptLogger log,
        ISyncRepository repository,
        Guid sourceConnectionId,
        CancellationToken cancellationToken)
    {
        SourceObjects = sourceObjects;
        Step = step;
        Project = project;
        StepRun = stepRun;
        Log = log;
        Repository = repository;
        SourceConnectionId = sourceConnectionId;
        CancellationToken = cancellationToken;
    }
}

/// <summary>
/// Context provided to post-processing scripts.
/// Post-processing scripts can create persons, resolve managers, etc.
///
/// IMPORTANT: Post-processing scripts work with database-persisted objects.
/// To persist changes, use the Repository methods (e.g., Repository.UpdateObjectIdentityLinkAsync).
/// Direct modifications to SyncedObjects properties are NOT automatically saved.
/// This is by design - scripts have explicit control over what gets persisted.
/// </summary>
public class PostProcessingContext
{
    /// <summary>
    /// The synced objects from the database after bulk upsert.
    /// Read-only for filtering/iteration. Use Repository methods to persist changes.
    /// </summary>
    public List<IdentityObject> SyncedObjects { get; set; } = new();

    /// <summary>
    /// Extended attributes for each object, keyed by object ID.
    /// </summary>
    public Dictionary<Guid, List<ObjectAttribute>> ObjectAttributes { get; set; } = new();

    /// <summary>
    /// The sync step being executed.
    /// </summary>
    public SyncStep Step { get; }

    /// <summary>
    /// The parent sync project.
    /// </summary>
    public SyncProject Project { get; }

    /// <summary>
    /// The current step run (for logging and metrics).
    /// </summary>
    public SyncStepRun StepRun { get; }

    /// <summary>
    /// Logger for script output.
    /// </summary>
    public IScriptLogger Log { get; }

    /// <summary>
    /// Repository for database operations.
    /// </summary>
    public ISyncRepository Repository { get; }

    /// <summary>
    /// Metrics tracker for the script execution.
    /// </summary>
    public ScriptMetrics Metrics { get; }

    /// <summary>
    /// Cancellation token for graceful shutdown.
    /// </summary>
    public CancellationToken CancellationToken { get; }

    /// <summary>
    /// Source connection ID for context.
    /// </summary>
    public Guid SourceConnectionId { get; }

    public PostProcessingContext(
        List<IdentityObject> syncedObjects,
        Dictionary<Guid, List<ObjectAttribute>> objectAttributes,
        SyncStep step,
        SyncProject project,
        SyncStepRun stepRun,
        IScriptLogger log,
        ISyncRepository repository,
        ScriptMetrics metrics,
        Guid sourceConnectionId,
        CancellationToken cancellationToken)
    {
        SyncedObjects = syncedObjects;
        ObjectAttributes = objectAttributes;
        Step = step;
        Project = project;
        StepRun = stepRun;
        Log = log;
        Repository = repository;
        Metrics = metrics;
        SourceConnectionId = sourceConnectionId;
        CancellationToken = cancellationToken;
    }
}

/// <summary>
/// Tracks metrics during script execution.
/// Updated by post-processing scripts to report what was accomplished.
/// </summary>
public class ScriptMetrics
{
    /// <summary>
    /// Number of Identity (Person) records created.
    /// </summary>
    public int IdentitiesCreated { get; set; }

    /// <summary>
    /// Number of Identity (Person) records updated.
    /// </summary>
    public int IdentitiesUpdated { get; set; }

    /// <summary>
    /// Number of objects modified (IdentityId assigned, ManagerObjectId set, etc.).
    /// </summary>
    public int ObjectsModified { get; set; }

    /// <summary>
    /// Number of manager relationships resolved.
    /// </summary>
    public int ManagersResolved { get; set; }

    /// <summary>
    /// Number of group owner relationships resolved.
    /// </summary>
    public int OwnersResolved { get; set; }

    /// <summary>
    /// Number of group memberships processed.
    /// </summary>
    public int MembershipsProcessed { get; set; }

    /// <summary>
    /// Number of errors encountered during script execution.
    /// </summary>
    public int Errors { get; set; }

    /// <summary>
    /// Number of warnings logged.
    /// </summary>
    public int Warnings { get; set; }

    /// <summary>
    /// Custom metrics (for user-defined scripts).
    /// </summary>
    public Dictionary<string, int> Custom { get; set; } = new();

    /// <summary>
    /// Reset all metrics to zero.
    /// </summary>
    public void Reset()
    {
        IdentitiesCreated = 0;
        IdentitiesUpdated = 0;
        ObjectsModified = 0;
        ManagersResolved = 0;
        OwnersResolved = 0;
        MembershipsProcessed = 0;
        Errors = 0;
        Warnings = 0;
        Custom.Clear();
    }

    /// <summary>
    /// Get a summary of the metrics.
    /// </summary>
    public string GetSummary()
    {
        var parts = new List<string>();

        if (IdentitiesCreated > 0) parts.Add($"Identities Created: {IdentitiesCreated}");
        if (IdentitiesUpdated > 0) parts.Add($"Identities Updated: {IdentitiesUpdated}");
        if (ObjectsModified > 0) parts.Add($"Objects Modified: {ObjectsModified}");
        if (ManagersResolved > 0) parts.Add($"Managers Resolved: {ManagersResolved}");
        if (OwnersResolved > 0) parts.Add($"Owners Resolved: {OwnersResolved}");
        if (MembershipsProcessed > 0) parts.Add($"Memberships: {MembershipsProcessed}");
        if (Errors > 0) parts.Add($"Errors: {Errors}");
        if (Warnings > 0) parts.Add($"Warnings: {Warnings}");

        foreach (var kvp in Custom)
        {
            parts.Add($"{kvp.Key}: {kvp.Value}");
        }

        return parts.Count > 0 ? string.Join(", ", parts) : "No changes";
    }
}

/// <summary>
/// Result of a script execution.
/// </summary>
public class ScriptExecutionResult
{
    /// <summary>
    /// Whether the script executed successfully.
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// Error message if the script failed.
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// Stack trace if the script threw an exception.
    /// </summary>
    public string? StackTrace { get; set; }

    /// <summary>
    /// Execution duration in milliseconds.
    /// </summary>
    public int DurationMs { get; set; }

    /// <summary>
    /// Metrics collected during execution.
    /// </summary>
    public ScriptMetrics Metrics { get; set; } = new();

    /// <summary>
    /// Log entries from the script.
    /// </summary>
    public List<ScriptLogEntry> LogEntries { get; set; } = new();

    /// <summary>
    /// Number of objects processed by the script.
    /// </summary>
    public int ObjectsProcessed { get; set; }

    /// <summary>
    /// Create a successful result.
    /// </summary>
    public static ScriptExecutionResult Succeeded(int durationMs, ScriptMetrics metrics, List<ScriptLogEntry> logs, int objectsProcessed)
    {
        return new ScriptExecutionResult
        {
            Success = true,
            DurationMs = durationMs,
            Metrics = metrics,
            LogEntries = logs,
            ObjectsProcessed = objectsProcessed
        };
    }

    /// <summary>
    /// Create a failed result.
    /// </summary>
    public static ScriptExecutionResult Failed(string errorMessage, string? stackTrace, int durationMs, List<ScriptLogEntry> logs)
    {
        return new ScriptExecutionResult
        {
            Success = false,
            ErrorMessage = errorMessage,
            StackTrace = stackTrace,
            DurationMs = durationMs,
            LogEntries = logs
        };
    }
}

/// <summary>
/// A single log entry from a script.
/// </summary>
public class ScriptLogEntry
{
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public ScriptLogLevel Level { get; set; }
    public string Message { get; set; } = string.Empty;

    public ScriptLogEntry() { }

    public ScriptLogEntry(ScriptLogLevel level, string message)
    {
        Level = level;
        Message = message;
    }
}

/// <summary>
/// Log levels for script output.
/// </summary>
public enum ScriptLogLevel
{
    Debug,
    Info,
    Warning,
    Error
}
