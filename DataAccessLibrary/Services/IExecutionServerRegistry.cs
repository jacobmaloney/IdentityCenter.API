using DataAccessLibrary.Models;

namespace DataAccessLibrary.Services;

/// <summary>
/// Manages the registry of execution servers and their health telemetry.
/// Used by the primary server to monitor the cluster and by all servers
/// to record their heartbeats.
///
/// This service is scoped (creates a new DB connection per call) because
/// it is called from both background services (heartbeat timer) and
/// API controllers (admin endpoints).
/// </summary>
public interface IExecutionServerRegistry
{
    // ========================================================================
    // SERVER REGISTRATION
    // ========================================================================

    /// <summary>
    /// Registers a new execution server or updates an existing registration.
    /// Called during server startup and by the admin API when adding remote workers.
    /// </summary>
    /// <param name="server">The server registration details.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The server's unique identifier (existing or newly generated).</returns>
    Task<Guid> RegisterServerAsync(ExecutionServerRegistration server, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes a server registration and all associated data (heartbeats, job type assignments).
    /// Active jobs claimed by this server are reassigned to Pending.
    /// </summary>
    /// <param name="serverId">The server to remove.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if the server was found and removed.</returns>
    Task<bool> UnregisterServerAsync(Guid serverId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all registered execution servers with their current status.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of all servers with current state.</returns>
    Task<List<ExecutionServerInfo>> GetAllServersAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a single server by ID with full details including recent heartbeat data.
    /// </summary>
    /// <param name="serverId">The server ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Server details or null if not found.</returns>
    Task<ExecutionServerInfo?> GetServerAsync(Guid serverId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets only servers that are currently online and capable of processing jobs.
    /// A server is online if its last heartbeat is within the configured threshold.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of online servers.</returns>
    Task<List<ExecutionServerInfo>> GetOnlineServersAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Finds the primary server record.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The primary server info, or null if not registered.</returns>
    Task<ExecutionServerInfo?> GetPrimaryServerAsync(CancellationToken cancellationToken = default);

    // ========================================================================
    // HEARTBEAT & TELEMETRY
    // ========================================================================

    /// <summary>
    /// Records a heartbeat with server telemetry data. Updates the RemoteAgents.LastHeartbeat
    /// column and inserts a row into ServerHeartbeats for time-series tracking.
    ///
    /// Called every N seconds (default: 30) by each execution server.
    /// </summary>
    /// <param name="heartbeat">Heartbeat data including CPU, memory, disk, and job counts.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task RecordHeartbeatAsync(ServerHeartbeatData heartbeat, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets recent heartbeat telemetry for a server. Used by the admin dashboard
    /// to display health charts.
    /// </summary>
    /// <param name="serverId">The server to query.</param>
    /// <param name="duration">How far back to look (e.g., 1 hour, 24 hours).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of heartbeat records ordered by timestamp descending.</returns>
    Task<List<ServerHeartbeatData>> GetRecentHeartbeatsAsync(Guid serverId, TimeSpan duration, CancellationToken cancellationToken = default);

    // ========================================================================
    // ORPHAN DETECTION & RECOVERY
    // ========================================================================

    /// <summary>
    /// Detects servers that have missed heartbeats beyond the configured threshold
    /// and reassigns their claimed jobs back to Pending status.
    ///
    /// Called periodically by the primary server's health monitor (default: every 60 seconds).
    /// Uses the usp_ReassignOrphanedJobs stored procedure.
    /// </summary>
    /// <param name="heartbeatTimeoutMinutes">
    /// How long without a heartbeat before a server is considered dead. Default: 10 minutes.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The number of jobs reassigned.</returns>
    Task<int> DetectAndRecoverOrphansAsync(int heartbeatTimeoutMinutes = 10, CancellationToken cancellationToken = default);

    /// <summary>
    /// Cleans up old heartbeat records to prevent unbounded table growth.
    /// Called by the system maintenance job.
    /// </summary>
    /// <param name="retentionDays">How many days of heartbeat history to retain. Default: 7.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task CleanupOldHeartbeatsAsync(int retentionDays = 7, CancellationToken cancellationToken = default);

    // ========================================================================
    // JOB TYPE ASSIGNMENTS
    // ========================================================================

    /// <summary>
    /// Sets the job types a server is allowed to process. Replaces any existing assignments.
    /// </summary>
    /// <param name="serverId">The server to configure.</param>
    /// <param name="assignments">List of job type assignments with priority and concurrency limits.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task SetJobTypeAssignmentsAsync(Guid serverId, List<JobTypeAssignment> assignments, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the current job type assignments for a server.
    /// </summary>
    /// <param name="serverId">The server to query.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of job type assignments.</returns>
    Task<List<JobTypeAssignment>> GetJobTypeAssignmentsAsync(Guid serverId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Finds all servers capable of handling a specific job type.
    /// Used by the job routing logic to determine eligible servers.
    /// </summary>
    /// <param name="jobType">The job type to check.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of servers that can handle this job type, ordered by priority.</returns>
    Task<List<ExecutionServerInfo>> GetServersForJobTypeAsync(string jobType, CancellationToken cancellationToken = default);

    // ========================================================================
    // SERVER MANAGEMENT
    // ========================================================================

    /// <summary>
    /// Updates server configuration (max concurrent jobs, supported types, etc.).
    /// </summary>
    /// <param name="serverId">The server to update.</param>
    /// <param name="update">The fields to update.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task UpdateServerConfigAsync(Guid serverId, ExecutionServerConfigUpdate update, CancellationToken cancellationToken = default);

    /// <summary>
    /// Enables or disables a server for job processing.
    /// Disabled servers do not appear in claiming queries.
    /// </summary>
    /// <param name="serverId">The server to enable/disable.</param>
    /// <param name="enabled">True to enable, false to disable.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task SetServerEnabledAsync(Guid serverId, bool enabled, CancellationToken cancellationToken = default);

    /// <summary>
    /// Initiates drain mode on a server. The server will finish current jobs
    /// but will not claim new ones.
    /// </summary>
    /// <param name="serverId">The server to drain.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task DrainServerAsync(Guid serverId, CancellationToken cancellationToken = default);
}

// DTOs are defined in DataAccessLibrary.Models.ExecutionServerModels.cs
