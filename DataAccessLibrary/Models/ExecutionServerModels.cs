using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace DataAccessLibrary.Models;

// ============================================================================
// EXECUTION SERVER MODELS
// Distributed Execution Server feature — V052
//
// All persistence uses Dapper (no EF Core).  These classes are plain POCOs
// used for DB mapping, service boundaries, and API serialisation.
// ============================================================================


// ----------------------------------------------------------------------------
// TABLE-MAPPED MODELS
// ----------------------------------------------------------------------------

/// <summary>
/// Time-series heartbeat record written by each execution server on every
/// heartbeat cycle. Clustered on (ServerId, Timestamp) for efficient range
/// queries. Cleaned up periodically by usp_CleanupOldHeartbeats.
/// </summary>
[Table("ServerHeartbeats")]
public class ServerHeartbeat
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>
    /// FK to RemoteAgents.Id — the server that recorded this heartbeat.
    /// </summary>
    public Guid ServerId { get; set; }

    /// <summary>
    /// UTC timestamp when the heartbeat was recorded.
    /// </summary>
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// CPU usage percentage (0–100).
    /// </summary>
    public double CpuPercent { get; set; }

    /// <summary>
    /// Memory usage percentage (0–100).
    /// </summary>
    public double MemoryPercent { get; set; }

    /// <summary>
    /// Physical memory in use (MB).
    /// </summary>
    public long MemoryUsedMb { get; set; }

    /// <summary>
    /// Free disk space on the primary drive (GB).
    /// </summary>
    public double DiskFreeGb { get; set; }

    /// <summary>
    /// Number of jobs actively being processed at the time of this heartbeat.
    /// </summary>
    public int ActiveJobCount { get; set; }

    /// <summary>
    /// Number of thread-pool threads actively executing work items.
    /// </summary>
    public int ThreadPoolActive { get; set; }

    /// <summary>
    /// Number of work items queued but not yet executing on the thread pool.
    /// </summary>
    public int ThreadPoolQueued { get; set; }

    /// <summary>
    /// Cumulative GC Gen-0 collection count since process start.
    /// </summary>
    public long GcGen0Count { get; set; }

    /// <summary>
    /// Cumulative GC Gen-2 collection count since process start.
    /// </summary>
    public long GcGen2Count { get; set; }

    /// <summary>
    /// Managed heap size (MB).
    /// </summary>
    public double HeapSizeMb { get; set; }

    /// <summary>
    /// False when any health check fails (high CPU/memory, disk low, etc.).
    /// </summary>
    public bool IsHealthy { get; set; } = true;

    /// <summary>
    /// Human-readable status message, populated when IsHealthy is false.
    /// </summary>
    [MaxLength(500)]
    public string? StatusMessage { get; set; }
}

/// <summary>
/// Per-server, per-job-type routing assignment.  Allows fine-grained control
/// over which servers handle which job types, with independent priority and
/// concurrency limits per assignment.
/// </summary>
[Table("ServerJobTypeAssignments")]
public class ServerJobTypeAssignment
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>
    /// FK to RemoteAgents.Id — the server this assignment belongs to.
    /// </summary>
    public Guid ServerId { get; set; }

    /// <summary>
    /// Job type identifier, e.g. "SyncProject", "PolicyEvaluation".
    /// </summary>
    [Required]
    [MaxLength(50)]
    public string JobType { get; set; } = string.Empty;

    /// <summary>
    /// When false the server will not claim jobs of this type even if it is
    /// listed in SupportedJobTypes.
    /// </summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>
    /// Claim priority for this job type on this server (higher = preferred).
    /// </summary>
    public int Priority { get; set; } = 100;

    /// <summary>
    /// Maximum concurrent jobs of this type on this server.
    /// 0 means no per-type limit — the server's MaxConcurrentJobs cap applies.
    /// </summary>
    public int MaxConcurrent { get; set; }

    /// <summary>
    /// When the assignment was created.
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// When the assignment was last modified.
    /// </summary>
    public DateTime? ModifiedAt { get; set; }
}


// ----------------------------------------------------------------------------
// SERVICE / REPOSITORY DTOs
// ----------------------------------------------------------------------------

/// <summary>
/// Data required to register a new execution server or update an existing one.
/// Used by IExecutionServerRegistry.RegisterServerAsync.
/// </summary>
public class ExecutionServerRegistration
{
    /// <summary>
    /// Existing server ID to update. Null to register a new server.
    /// </summary>
    public Guid? Id { get; set; }

    /// <summary>
    /// Unique agent name (typically MachineName + instance suffix).
    /// </summary>
    [Required]
    [MaxLength(200)]
    public string AgentName { get; set; } = string.Empty;

    [MaxLength(500)]
    public string Description { get; set; } = string.Empty;

    [Required]
    [MaxLength(200)]
    public string MachineName { get; set; } = string.Empty;

    [MaxLength(50)]
    public string? IpAddress { get; set; }

    [MaxLength(50)]
    public string Version { get; set; } = "1.0.0";

    [MaxLength(200)]
    public string? OperatingSystem { get; set; }

    public bool IsPrimary { get; set; }

    [MaxLength(20)]
    public string ServerRole { get; set; } = "Worker";

    [MaxLength(500)]
    public string? BaseUrl { get; set; }

    /// <summary>
    /// Comma-separated job types, or "*" for all types.
    /// </summary>
    [MaxLength(500)]
    public string SupportedJobTypes { get; set; } = "SyncProject";

    [Range(1, 100)]
    public int MaxConcurrentJobs { get; set; } = 5;

    [MaxLength(500)]
    public string? Tags { get; set; }

    [Range(1, 10000)]
    public int Priority { get; set; } = 100;

    [MaxLength(50)]
    public string? EnvironmentName { get; set; }

    [MaxLength(50)]
    public string? DotNetVersion { get; set; }
}

/// <summary>
/// Full server information including computed fields.  Returned by the
/// IExecutionServerRegistry query methods and the ServersController.
/// </summary>
public class ExecutionServerInfo
{
    public Guid Id { get; set; }
    public string AgentName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string MachineName { get; set; } = string.Empty;
    public string? IpAddress { get; set; }
    public string Version { get; set; } = string.Empty;
    public string? OperatingSystem { get; set; }
    public string Status { get; set; } = "Offline";
    public bool IsPrimary { get; set; }
    public string ServerRole { get; set; } = "Worker";
    public string? BaseUrl { get; set; }

    /// <summary>
    /// Comma-separated list of supported job types, or "*" for all.
    /// </summary>
    public string SupportedJobTypes { get; set; } = string.Empty;

    public int MaxConcurrentJobs { get; set; }
    public int CurrentJobCount { get; set; }
    public DateTime? LastHeartbeat { get; set; }
    public DateTime? LastJobClaimed { get; set; }
    public DateTime? LastJobCompleted { get; set; }
    public int TotalJobsProcessed { get; set; }
    public int TotalJobsFailed { get; set; }
    public bool IsEnabled { get; set; }
    public DateTime RegisteredAt { get; set; }
    public DateTime? DrainStartedAt { get; set; }
    public DateTime? LastStartedAt { get; set; }
    public string? EnvironmentName { get; set; }
    public string? DotNetVersion { get; set; }
    public string? Tags { get; set; }
    public int Priority { get; set; }

    // ------------------------------------------------------------------
    // Computed (not stored in DB)
    // ------------------------------------------------------------------

    /// <summary>
    /// True when the last heartbeat was received within the last 5 minutes.
    /// </summary>
    public bool IsOnline => LastHeartbeat.HasValue && (DateTime.UtcNow - LastHeartbeat.Value).TotalMinutes < 5;

    /// <summary>
    /// True when DrainStartedAt has a value (server is finishing jobs, not claiming new ones).
    /// </summary>
    public bool IsDraining => DrainStartedAt.HasValue;

    /// <summary>
    /// Job success rate as a percentage (0–100).
    /// </summary>
    public double SuccessRate => TotalJobsProcessed > 0
        ? Math.Round((double)(TotalJobsProcessed - TotalJobsFailed) / TotalJobsProcessed * 100, 2)
        : 0;

    /// <summary>
    /// Remaining concurrent job slots (MaxConcurrentJobs − CurrentJobCount, floored at 0).
    /// </summary>
    public int AvailableCapacity => Math.Max(0, MaxConcurrentJobs - CurrentJobCount);
}

/// <summary>
/// Heartbeat telemetry sent by each execution server on every heartbeat cycle.
/// Also used to return stored heartbeat rows from the DB.
/// </summary>
public class ServerHeartbeatData
{
    public Guid ServerId { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public double CpuPercent { get; set; }
    public double MemoryPercent { get; set; }
    public long MemoryUsedMb { get; set; }
    public double DiskFreeGb { get; set; }
    public int ActiveJobCount { get; set; }
    public int ThreadPoolActive { get; set; }
    public int ThreadPoolQueued { get; set; }
    public long GcGen0Count { get; set; }
    public long GcGen2Count { get; set; }
    public double HeapSizeMb { get; set; }
    public bool IsHealthy { get; set; } = true;
    public string? StatusMessage { get; set; }
}

/// <summary>
/// Response returned to the execution server after it records a heartbeat.
/// Carries out-of-band signals so the server can react without extra polls.
/// </summary>
public class HeartbeatResponse
{
    /// <summary>
    /// Current UTC time on the primary server (useful for clock-skew detection).
    /// </summary>
    public DateTime ServerTime { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Total jobs currently pending in the queue (informs polling urgency).
    /// </summary>
    public int PendingJobCount { get; set; }

    /// <summary>
    /// IDs of jobs whose CancellationRequested flag has been set.
    /// The receiving server should cancel these at its next checkpoint.
    /// </summary>
    public List<Guid> CancelledJobIds { get; set; } = new();
}

/// <summary>
/// Administrative command sent to an execution server (e.g. via API or DB polling).
/// Enables the primary to direct remote workers without a persistent connection.
/// </summary>
public class ServerCommand
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>
    /// Target server that should act on this command.
    /// </summary>
    public Guid ServerId { get; set; }

    /// <summary>
    /// Command type: "Drain", "Activate", "Shutdown", "CancelJob", "UpdateConfig".
    /// </summary>
    [Required]
    [MaxLength(50)]
    public string CommandType { get; set; } = string.Empty;

    /// <summary>
    /// Optional payload for the command (JSON). E.g., for "CancelJob" this is
    /// {"jobId":"guid"}, for "UpdateConfig" this is the new config values.
    /// </summary>
    public string? PayloadJson { get; set; }

    /// <summary>
    /// When the command was issued.
    /// </summary>
    public DateTime IssuedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// When the server acknowledged / executed the command. Null if pending.
    /// </summary>
    public DateTime? AcknowledgedAt { get; set; }

    /// <summary>
    /// True once the server has processed this command.
    /// </summary>
    public bool IsAcknowledged { get; set; }
}

/// <summary>
/// Job type assignment for a specific server, used by IExecutionServerRegistry
/// for reading and writing ServerJobTypeAssignments rows.
/// </summary>
public class JobTypeAssignment
{
    /// <summary>
    /// DB row ID. Null when creating a new assignment.
    /// </summary>
    public Guid? Id { get; set; }

    public Guid ServerId { get; set; }

    [Required]
    [MaxLength(50)]
    public string JobType { get; set; } = string.Empty;

    public bool IsEnabled { get; set; } = true;

    public int Priority { get; set; } = 100;

    /// <summary>
    /// 0 = no per-type cap; the server's MaxConcurrentJobs applies instead.
    /// </summary>
    public int MaxConcurrent { get; set; }
}

/// <summary>
/// Lightweight status snapshot for a single execution server.
/// Returned by health-check endpoints and the admin dashboard summary.
/// </summary>
public class ExecutionServerStatus
{
    public Guid ServerId { get; set; }
    public string AgentName { get; set; } = string.Empty;
    public string MachineName { get; set; } = string.Empty;
    public string Status { get; set; } = "Offline";
    public bool IsPrimary { get; set; }
    public bool IsOnline { get; set; }
    public bool IsDraining { get; set; }
    public int ActiveJobCount { get; set; }
    public int MaxConcurrentJobs { get; set; }
    public int AvailableCapacity { get; set; }
    public DateTime? LastHeartbeat { get; set; }

    /// <summary>
    /// Seconds elapsed since the last heartbeat was received. -1 if never received.
    /// </summary>
    public int SecondsSinceHeartbeat { get; set; } = -1;

    public double CpuPercent { get; set; }
    public double MemoryPercent { get; set; }

    /// <summary>
    /// Per-check health results: key = check name, value = "Healthy" / "Warning" / "Critical".
    /// </summary>
    public Dictionary<string, string> Checks { get; set; } = new();
}

/// <summary>
/// Extended queue summary with per-server breakdowns.
/// Returned by IDistributedJobQueue.GetDistributedQueueSummaryAsync and the
/// GET /api/jobs/summary/distributed endpoint.
/// </summary>
public class DistributedQueueSummary : JobQueueSummary
{
    /// <summary>
    /// Active job count per server (ServerId → count).
    /// </summary>
    public Dictionary<Guid, int> ActiveJobsByServer { get; set; } = new();

    /// <summary>
    /// Server display names for dashboard rendering (ServerId → AgentName).
    /// </summary>
    public Dictionary<Guid, string> ServerNames { get; set; } = new();

    /// <summary>
    /// Remaining capacity per server (ServerId → available slots).
    /// </summary>
    public Dictionary<Guid, int> CapacityByServer { get; set; } = new();

    /// <summary>
    /// Pending jobs that have an explicit TargetServerId set (server-affinity routing).
    /// </summary>
    public int PendingTargeted { get; set; }

    /// <summary>
    /// Pending jobs with no TargetServerId — available to any eligible server.
    /// </summary>
    public int PendingUnassigned { get; set; }

    /// <summary>
    /// Jobs that have CancellationRequested=1 but are still in Claimed/Processing status.
    /// </summary>
    public int PendingCancellation { get; set; }
}

/// <summary>
/// Active job count for a single server — used in aggregate queries that
/// avoid loading full job rows.
/// </summary>
public class ServerJobCount
{
    public Guid ServerId { get; set; }
    public string AgentName { get; set; } = string.Empty;
    public int ActiveCount { get; set; }
    public int PendingCount { get; set; }
    public int ClaimedCount { get; set; }
    public int ProcessingCount { get; set; }
}

/// <summary>
/// Result of an orphan recovery pass executed by usp_ReassignOrphanedJobs.
/// Returned by IExecutionServerRegistry.DetectAndRecoverOrphansAsync.
/// </summary>
public class OrphanRecoveryResult
{
    /// <summary>
    /// Number of jobs that were reset to Pending after their server went offline.
    /// </summary>
    public int ReassignedCount { get; set; }

    /// <summary>
    /// Number of jobs that were moved to Failed because MaxRetries was exceeded.
    /// </summary>
    public int FailedCount { get; set; }

    /// <summary>
    /// Number of server rows whose Status was set to "Offline" during this pass.
    /// </summary>
    public int ServersMarkedOffline { get; set; }

    /// <summary>
    /// When the recovery pass was executed.
    /// </summary>
    public DateTime RunAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Request for the usp_ClaimJobsForServer stored procedure / ClaimJobBatchAsync.
/// </summary>
public class ClaimJobsRequest
{
    /// <summary>
    /// The execution server claiming the jobs.
    /// </summary>
    public Guid ServerId { get; set; }

    /// <summary>
    /// Job types this server supports. Pass ["*"] to claim any job type.
    /// </summary>
    public List<string> SupportedJobTypes { get; set; } = new();

    /// <summary>
    /// Maximum number of jobs to claim in this batch (default: 5).
    /// Should not exceed the server's remaining capacity.
    /// </summary>
    [Range(1, 50)]
    public int MaxJobs { get; set; } = 5;
}

/// <summary>
/// Response from a batch job claim operation.
/// </summary>
public class ClaimJobsResponse
{
    /// <summary>
    /// The jobs that were atomically claimed and are ready for execution.
    /// </summary>
    public List<JobQueueEntry> ClaimedJobs { get; set; } = new();

    /// <summary>
    /// Number of jobs successfully claimed.
    /// </summary>
    public int ClaimedCount => ClaimedJobs.Count;

    /// <summary>
    /// True when at least one job was claimed.
    /// </summary>
    public bool HasJobs => ClaimedJobs.Count > 0;
}


// ----------------------------------------------------------------------------
// CONFIGURATION
// ----------------------------------------------------------------------------

/// <summary>
/// Configuration for the execution server poll-claim-execute loop and heartbeat
/// subsystem. Bound from appsettings.json section "ExecutionServer".
/// </summary>
public class ExecutionServerOptions
{
    /// <summary>
    /// appsettings.json section key: "ExecutionServer".
    /// </summary>
    public const string SectionName = "ExecutionServer";

    /// <summary>
    /// How often to poll the database for new jobs (default: 30 seconds).
    /// Shorter intervals mean faster job pickup but more database load.
    /// </summary>
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Maximum jobs to claim per poll cycle (default: 3).
    /// Typically set to the server's remaining capacity at the time of polling.
    /// </summary>
    [Range(1, 50)]
    public int MaxClaimBatchSize { get; set; } = 3;

    /// <summary>
    /// How often to write heartbeat telemetry (default: 30 seconds).
    /// Must be significantly less than HeartbeatTimeoutMinutes.
    /// </summary>
    public TimeSpan HeartbeatInterval { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// How long to wait for active jobs to finish during graceful shutdown (default: 5 minutes).
    /// After this timeout the process exits even if jobs are still running.
    /// </summary>
    public TimeSpan GracefulShutdownTimeout { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// How often to check for CancellationRequested jobs (default: 30 seconds).
    /// </summary>
    public TimeSpan CancellationCheckInterval { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// How often the primary checks for orphaned jobs (default: 60 seconds).
    /// Only the primary server runs orphan detection.
    /// </summary>
    public TimeSpan OrphanDetectionInterval { get; set; } = TimeSpan.FromSeconds(60);

    /// <summary>
    /// Seconds without a heartbeat before a server is considered dead (default: 10 minutes).
    /// Must be significantly longer than HeartbeatInterval to avoid false positives.
    /// </summary>
    [Range(1, 1440)]
    public int HeartbeatTimeoutMinutes { get; set; } = 10;

    /// <summary>
    /// Days of heartbeat history to retain (default: 7).
    /// Older rows are removed by the CleanupOldHeartbeats maintenance job.
    /// </summary>
    [Range(1, 365)]
    public int HeartbeatRetentionDays { get; set; } = 7;

    /// <summary>
    /// Minimum interval between progress-update writes to the DB (default: 2 seconds).
    /// Prevents high-frequency job code from hammering the database.
    /// </summary>
    public TimeSpan ProgressUpdateThrottle { get; set; } = TimeSpan.FromSeconds(2);

    /// <summary>
    /// Override whether this instance is the primary server.
    /// When null the startup code auto-detects based on the presence of the Blazor host.
    /// </summary>
    public bool? IsPrimary { get; set; }

    /// <summary>
    /// Pre-configured server ID for remote workers.
    /// When null the worker auto-registers using the machine name.
    /// </summary>
    public Guid? ServerId { get; set; }

    /// <summary>
    /// Server display name override. Defaults to Environment.MachineName.
    /// </summary>
    [MaxLength(200)]
    public string? ServerName { get; set; }
}


// ----------------------------------------------------------------------------
// GENERIC PAGING HELPER
// ----------------------------------------------------------------------------

/// <summary>
/// Generic paged result wrapper for paginated Dapper queries.
/// </summary>
/// <typeparam name="T">The item type.</typeparam>
public class PagedResult<T>
{
    /// <summary>
    /// The items on the current page.
    /// </summary>
    public List<T> Items { get; set; } = new();

    /// <summary>
    /// Total number of matching records (across all pages).
    /// </summary>
    public int TotalCount { get; set; }

    /// <summary>
    /// Zero-based record offset for this page.
    /// </summary>
    public int Skip { get; set; }

    /// <summary>
    /// Maximum records returned per page.
    /// </summary>
    public int Take { get; set; }

    /// <summary>
    /// Total number of pages, computed from TotalCount and Take.
    /// </summary>
    public int TotalPages => Take > 0 ? (int)Math.Ceiling((double)TotalCount / Take) : 0;

    /// <summary>
    /// True when there are more records after this page.
    /// </summary>
    public bool HasNextPage => Skip + Items.Count < TotalCount;
}

/// <summary>
/// Partial update for server configuration. Only non-null fields are applied.
/// </summary>
public class ExecutionServerConfigUpdate
{
    public string? Description { get; set; }
    public int? MaxConcurrentJobs { get; set; }
    public string? SupportedJobTypes { get; set; }
    public string? Tags { get; set; }
    public int? Priority { get; set; }
}
