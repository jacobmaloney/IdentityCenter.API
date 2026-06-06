namespace DataAccessLibrary.Services;

/// <summary>
/// Singleton service that represents the identity of the current execution server.
/// Populated on application startup and remains constant for the lifetime of the process.
///
/// Every execution server (primary or remote worker) has exactly one instance of this
/// service. It answers the question: "Who am I in the distributed cluster?"
///
/// The primary server discovers or creates its record by querying for IsPrimary=1.
/// Remote workers discover their record by matching on a configured ServerId or
/// by auto-registering using machine name + instance name.
/// </summary>
public interface IExecutionServerContext
{
    /// <summary>
    /// The unique identifier of this execution server in the RemoteAgents table.
    /// Set during application startup and immutable thereafter.
    /// </summary>
    Guid ServerId { get; }

    /// <summary>
    /// The display name of this execution server (e.g., "Primary Server" or "WORKER-01").
    /// </summary>
    string ServerName { get; }

    /// <summary>
    /// The machine name where this server is running (Environment.MachineName).
    /// </summary>
    string MachineName { get; }

    /// <summary>
    /// True if this is the primary IdentityCenter instance (hosts web UI + Quartz scheduler).
    /// False for remote worker instances.
    /// </summary>
    bool IsPrimary { get; }

    /// <summary>
    /// The role of this server: "Primary", "Worker", or "Hybrid".
    /// Hybrid servers run the Quartz scheduler but not the web UI (future use).
    /// </summary>
    string ServerRole { get; }

    /// <summary>
    /// The base URL of this server's API endpoint (e.g., "https://server01:5001").
    /// Null for workers that do not expose an API.
    /// </summary>
    string? BaseUrl { get; }

    /// <summary>
    /// The list of job types this server is configured to handle.
    /// "*" means all job types (default for primary).
    /// Remote workers typically handle a subset (e.g., "SyncProject,PolicyEvaluation").
    /// </summary>
    IReadOnlyList<string> SupportedJobTypes { get; }

    /// <summary>
    /// Maximum number of jobs this server can process concurrently.
    /// Corresponds to the Quartz thread pool size on the primary,
    /// or a configured value on remote workers.
    /// </summary>
    int MaxConcurrentJobs { get; }

    /// <summary>
    /// True if this server is in drain mode (finishing current jobs, not accepting new ones).
    /// Set via the admin UI or API to prepare for maintenance.
    /// </summary>
    bool IsDraining { get; }

    /// <summary>
    /// True if the server has been fully initialized and is ready to process jobs.
    /// False during startup, database migration, or health check failures.
    /// </summary>
    bool IsReady { get; }

    /// <summary>
    /// Initializes the execution server context by discovering or creating the server
    /// record in the database. Called once during application startup.
    ///
    /// For the primary: queries for IsPrimary=1, updates machine name/version/status.
    /// For workers: queries by configured ServerId or registers a new record.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token for startup timeout.</param>
    /// <returns>Task that completes when the server identity is established.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown if the server cannot be registered (e.g., database unavailable).
    /// </exception>
    Task InitializeAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Marks the server as entering drain mode. The server will finish its current
    /// jobs but will not claim new ones. Updates the DrainStartedAt column.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task EnterDrainModeAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Exits drain mode and resumes normal job claiming.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task ExitDrainModeAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Marks the server as going offline. Called during graceful shutdown.
    /// Sets Status='Offline' and CurrentJobCount=0.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task MarkOfflineAsync(CancellationToken cancellationToken = default);
}
