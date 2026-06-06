using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace DataAccessLibrary.Models;

// ============================================================================
// REMOTE AGENT MODELS
// Supports distributed job processing via Windows Service agents
// ============================================================================

/// <summary>
/// Represents a remote sync agent that polls for jobs and executes them.
/// Agents are Windows Services deployed to servers with access to target systems.
/// </summary>
[Table("RemoteAgents")]
public class RemoteAgent
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>
    /// Unique agent identifier (typically machine name + service instance)
    /// </summary>
    [Required]
    [MaxLength(200)]
    public string AgentName { get; set; } = string.Empty;

    /// <summary>
    /// Descriptive name for display
    /// </summary>
    [MaxLength(500)]
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// Machine/server where the agent is installed
    /// </summary>
    [MaxLength(200)]
    public string MachineName { get; set; } = string.Empty;

    /// <summary>
    /// IP address of the agent (last known)
    /// </summary>
    [MaxLength(50)]
    public string? IpAddress { get; set; }

    /// <summary>
    /// Agent version number
    /// </summary>
    [MaxLength(50)]
    public string Version { get; set; } = "1.0.0";

    /// <summary>
    /// Operating system info
    /// </summary>
    [MaxLength(200)]
    public string? OperatingSystem { get; set; }

    /// <summary>
    /// API key hash for authentication
    /// </summary>
    [MaxLength(500)]
    public string ApiKeyHash { get; set; } = string.Empty;

    /// <summary>
    /// Current status: Online, Offline, Busy, Error
    /// </summary>
    [Required]
    [MaxLength(20)]
    public string Status { get; set; } = "Offline";

    /// <summary>
    /// Comma-separated list of job types this agent can handle
    /// e.g., "SyncProject,PolicyEvaluation"
    /// </summary>
    [MaxLength(500)]
    public string SupportedJobTypes { get; set; } = "SyncProject";

    /// <summary>
    /// Maximum concurrent jobs this agent can process
    /// </summary>
    public int MaxConcurrentJobs { get; set; } = 1;

    /// <summary>
    /// Current number of jobs being processed
    /// </summary>
    public int CurrentJobCount { get; set; }

    /// <summary>
    /// Last heartbeat timestamp
    /// </summary>
    public DateTime? LastHeartbeat { get; set; }

    /// <summary>
    /// Last job claimed timestamp
    /// </summary>
    public DateTime? LastJobClaimed { get; set; }

    /// <summary>
    /// Last successful job completion timestamp
    /// </summary>
    public DateTime? LastJobCompleted { get; set; }

    /// <summary>
    /// Total jobs processed since registration
    /// </summary>
    public int TotalJobsProcessed { get; set; }

    /// <summary>
    /// Total jobs that failed
    /// </summary>
    public int TotalJobsFailed { get; set; }

    /// <summary>
    /// Is this agent enabled for job processing?
    /// </summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>
    /// Registration timestamp
    /// </summary>
    public DateTime RegisteredAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Last configuration update
    /// </summary>
    public DateTime? ConfigUpdatedAt { get; set; }

    /// <summary>
    /// Agent-specific configuration (JSON)
    /// </summary>
    public string? ConfigurationJson { get; set; }

    /// <summary>
    /// Tags for agent grouping/filtering
    /// </summary>
    [MaxLength(500)]
    public string? Tags { get; set; }

    /// <summary>
    /// Priority for job assignment (higher = preferred)
    /// </summary>
    public int Priority { get; set; } = 100;

    // -------------------------------------------------------------------------
    // Distributed Execution Server extensions (V052)
    // -------------------------------------------------------------------------

    /// <summary>
    /// True if this is the primary IdentityCenter instance (hosts web UI + Quartz scheduler).
    /// Exactly one primary exists per deployment.
    /// </summary>
    public bool IsPrimary { get; set; }

    /// <summary>
    /// Role of this server: "Primary", "Worker", or "Hybrid".
    /// </summary>
    [MaxLength(20)]
    public string ServerRole { get; set; } = "Worker";

    /// <summary>
    /// Base URL of this server's API endpoint (e.g., "https://server01:5001").
    /// Null for workers that do not expose an HTTP API.
    /// </summary>
    [MaxLength(500)]
    public string? BaseUrl { get; set; }

    /// <summary>
    /// Timestamp when drain mode was entered. Null means the server is not draining.
    /// A draining server finishes current jobs but claims no new ones.
    /// </summary>
    public DateTime? DrainStartedAt { get; set; }

    /// <summary>
    /// Timestamp when this server process last started (set on each startup).
    /// </summary>
    public DateTime? LastStartedAt { get; set; }

    /// <summary>
    /// Deployment environment name (e.g., "Production", "Staging", "Development").
    /// </summary>
    [MaxLength(50)]
    public string? EnvironmentName { get; set; }

    /// <summary>
    /// .NET runtime version string (e.g., ".NET 8.0.12").
    /// Populated from RuntimeInformation.FrameworkDescription on startup.
    /// </summary>
    [MaxLength(50)]
    public string? DotNetVersion { get; set; }

    // -------------------------------------------------------------------------
    // Computed properties (not mapped to DB columns)
    // -------------------------------------------------------------------------

    /// <summary>
    /// True if the server has sent a heartbeat within the last 5 minutes.
    /// </summary>
    [NotMapped]
    public bool IsOnline => LastHeartbeat.HasValue && (DateTime.UtcNow - LastHeartbeat.Value).TotalMinutes < 5;

    /// <summary>
    /// True if the server is online, enabled, and not in drain mode.
    /// </summary>
    [NotMapped]
    public bool IsHealthy => IsOnline && IsEnabled && !DrainStartedAt.HasValue;

    /// <summary>
    /// Number of additional concurrent job slots available on this server.
    /// </summary>
    [NotMapped]
    public int AvailableSlots => Math.Max(0, MaxConcurrentJobs - CurrentJobCount);

    /// <summary>
    /// Percentage of concurrent capacity currently in use (0–100).
    /// </summary>
    [NotMapped]
    public double LoadPercentage => MaxConcurrentJobs > 0
        ? Math.Round((double)CurrentJobCount / MaxConcurrentJobs * 100, 1)
        : 0;
}

/// <summary>
/// Job queue entry for remote agents to poll and claim.
/// Uses atomic claiming with row-level locking for concurrency safety.
/// </summary>
[Table("JobQueue")]
public class JobQueueEntry
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>
    /// Type of job: SyncProject, PolicyEvaluation, etc.
    /// </summary>
    [Required]
    [MaxLength(50)]
    public string JobType { get; set; } = string.Empty;

    /// <summary>
    /// Display name for the job
    /// </summary>
    [MaxLength(200)]
    public string JobName { get; set; } = string.Empty;

    /// <summary>
    /// Related entity ID (e.g., SyncProjectId, PolicyId)
    /// </summary>
    public Guid? RelatedEntityId { get; set; }

    /// <summary>
    /// Type of related entity
    /// </summary>
    [MaxLength(50)]
    public string? RelatedEntityType { get; set; }

    /// <summary>
    /// Job status: Pending, Claimed, Processing, Completed, Failed, Cancelled
    /// </summary>
    [Required]
    [MaxLength(20)]
    public string Status { get; set; } = "Pending";

    /// <summary>
    /// Priority (0-1000, higher = more urgent)
    /// </summary>
    public int Priority { get; set; } = 500;

    /// <summary>
    /// Job is ready to be executed (vs. scheduled for later)
    /// </summary>
    public bool Ready2Execute { get; set; } = true;

    /// <summary>
    /// Scheduled execution time (null = execute immediately)
    /// </summary>
    public DateTime? ScheduledAt { get; set; }

    /// <summary>
    /// When the job was added to the queue
    /// </summary>
    [Required]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Who/what created this job entry
    /// </summary>
    [MaxLength(200)]
    public string CreatedBy { get; set; } = "System";

    /// <summary>
    /// Agent ID that claimed this job
    /// </summary>
    public Guid? ClaimedByAgentId { get; set; }

    /// <summary>
    /// When the job was claimed
    /// </summary>
    public DateTime? ClaimedAt { get; set; }

    /// <summary>
    /// When processing started
    /// </summary>
    public DateTime? StartedAt { get; set; }

    /// <summary>
    /// When processing completed
    /// </summary>
    public DateTime? CompletedAt { get; set; }

    /// <summary>
    /// Processing duration in milliseconds
    /// </summary>
    public int? DurationMs { get; set; }

    /// <summary>
    /// Number of items processed
    /// </summary>
    public int ItemsProcessed { get; set; }

    /// <summary>
    /// Number of successful items
    /// </summary>
    public int ItemsSucceeded { get; set; }

    /// <summary>
    /// Number of failed items
    /// </summary>
    public int ItemsFailed { get; set; }

    /// <summary>
    /// Error message if failed
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// Full exception details (JSON)
    /// </summary>
    public string? ExceptionDetailsJson { get; set; }

    /// <summary>
    /// Current retry attempt (0 = first attempt)
    /// </summary>
    public int RetryAttempt { get; set; }

    /// <summary>
    /// Maximum retry attempts allowed
    /// </summary>
    public int MaxRetries { get; set; } = 3;

    /// <summary>
    /// Job-specific payload (JSON)
    /// </summary>
    public string? PayloadJson { get; set; }

    /// <summary>
    /// Result data (JSON)
    /// </summary>
    public string? ResultJson { get; set; }

    /// <summary>
    /// Progress percentage (0-100)
    /// </summary>
    public int ProgressPercent { get; set; }

    /// <summary>
    /// Current progress message
    /// </summary>
    [MaxLength(500)]
    public string? ProgressMessage { get; set; }

    /// <summary>
    /// Last progress update timestamp
    /// </summary>
    public DateTime? LastProgressUpdate { get; set; }

    /// <summary>
    /// Version/concurrency token for optimistic locking
    /// </summary>
    [Timestamp]
    public byte[]? RowVersion { get; set; }

    /// <summary>
    /// Tags for job filtering
    /// </summary>
    [MaxLength(500)]
    public string? Tags { get; set; }

    // -------------------------------------------------------------------------
    // Distributed Execution Server extensions (V052)
    // -------------------------------------------------------------------------

    /// <summary>
    /// When set, this job is routed exclusively to the specified execution server.
    /// Null means any eligible server can claim it.
    /// </summary>
    public Guid? TargetServerId { get; set; }

    /// <summary>
    /// Cooperative cancellation flag. Set to true to signal the executing server
    /// to stop work at its next checkpoint. The server must check this periodically.
    /// </summary>
    public bool CancellationRequested { get; set; }

    /// <summary>
    /// Optional idempotency key to prevent duplicate job enqueuing.
    /// If a job with the same key already exists in a non-terminal state, re-enqueue is a no-op.
    /// </summary>
    [MaxLength(200)]
    public string? IdempotencyKey { get; set; }

    // Navigation properties
    [ForeignKey("ClaimedByAgentId")]
    public virtual RemoteAgent? ClaimedByAgent { get; set; }

    [ForeignKey("TargetServerId")]
    public virtual RemoteAgent? TargetServer { get; set; }
}

/// <summary>
/// API Key for agent authentication
/// </summary>
[Table("ApiKeys")]
public class ApiKey
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>
    /// Display name for the API key
    /// </summary>
    [Required]
    [MaxLength(200)]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Hash of the API key (actual key is only shown once at creation)
    /// </summary>
    [Required]
    [MaxLength(500)]
    public string KeyHash { get; set; } = string.Empty;

    /// <summary>
    /// Key prefix for identification (first 8 chars of key)
    /// </summary>
    [MaxLength(10)]
    public string KeyPrefix { get; set; } = string.Empty;

    /// <summary>
    /// Type of key: Agent, User, Service
    /// </summary>
    [Required]
    [MaxLength(20)]
    public string KeyType { get; set; } = "Agent";

    /// <summary>
    /// Associated agent ID (for agent keys)
    /// </summary>
    public Guid? AgentId { get; set; }

    /// <summary>
    /// Associated user ID (for user keys)
    /// </summary>
    public string? UserId { get; set; }

    /// <summary>
    /// Comma-separated list of allowed scopes/permissions
    /// e.g., "jobs:read,jobs:write,sync:execute"
    /// </summary>
    [MaxLength(1000)]
    public string Scopes { get; set; } = string.Empty;

    /// <summary>
    /// Is this key active?
    /// </summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>
    /// Expiration date (null = never expires)
    /// </summary>
    public DateTime? ExpiresAt { get; set; }

    /// <summary>
    /// When the key was created
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Who created the key
    /// </summary>
    [MaxLength(200)]
    public string CreatedBy { get; set; } = string.Empty;

    /// <summary>
    /// Last time the key was used
    /// </summary>
    public DateTime? LastUsedAt { get; set; }

    /// <summary>
    /// IP address that last used the key
    /// </summary>
    [MaxLength(50)]
    public string? LastUsedFromIp { get; set; }

    /// <summary>
    /// Total number of times the key was used
    /// </summary>
    public int UsageCount { get; set; }

    /// <summary>
    /// When the key was revoked
    /// </summary>
    public DateTime? RevokedAt { get; set; }

    /// <summary>
    /// Reason for revocation
    /// </summary>
    [MaxLength(500)]
    public string? RevokedReason { get; set; }

    // Navigation property
    [ForeignKey("AgentId")]
    public virtual RemoteAgent? Agent { get; set; }
}

// ============================================================================
// VIEW MODELS / DTOs
// ============================================================================

/// <summary>
/// Agent status for dashboard display
/// </summary>
public class RemoteAgentStatus
{
    public Guid Id { get; set; }
    public string AgentName { get; set; } = string.Empty;
    public string MachineName { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public DateTime? LastHeartbeat { get; set; }
    public int CurrentJobCount { get; set; }
    public int MaxConcurrentJobs { get; set; }
    public int TotalJobsProcessed { get; set; }
    public int TotalJobsFailed { get; set; }
    public double SuccessRate { get; set; }
    public bool IsEnabled { get; set; }
    public bool IsOnline => LastHeartbeat.HasValue && (DateTime.UtcNow - LastHeartbeat.Value).TotalMinutes < 5;
}

/// <summary>
/// Job queue summary for dashboard
/// </summary>
public class JobQueueSummary
{
    public int TotalPending { get; set; }
    public int TotalProcessing { get; set; }
    public int TotalCompleted24h { get; set; }
    public int TotalFailed24h { get; set; }
    public Dictionary<string, int> PendingByType { get; set; } = new();
    public Dictionary<string, int> ProcessingByAgent { get; set; } = new();
}

/// <summary>
/// Request to claim a job from the queue
/// </summary>
public class ClaimJobRequest
{
    public Guid AgentId { get; set; }
    public List<string> SupportedJobTypes { get; set; } = new();
    public int MaxJobs { get; set; } = 1;
}

/// <summary>
/// Response from claiming a job
/// </summary>
public class ClaimJobResponse
{
    public bool Success { get; set; }
    public JobQueueEntry? Job { get; set; }
    public string? Message { get; set; }
}

/// <summary>
/// Request to submit job progress
/// </summary>
public class JobProgressUpdate
{
    public Guid JobId { get; set; }
    public Guid AgentId { get; set; }
    public int ProgressPercent { get; set; }
    public string? ProgressMessage { get; set; }
    public int ItemsProcessed { get; set; }
    public int ItemsSucceeded { get; set; }
    public int ItemsFailed { get; set; }
}

/// <summary>
/// Request to complete a job
/// </summary>
public class CompleteJobRequest
{
    public Guid JobId { get; set; }
    public Guid AgentId { get; set; }
    public bool Success { get; set; }
    public int ItemsProcessed { get; set; }
    public int ItemsSucceeded { get; set; }
    public int ItemsFailed { get; set; }
    public string? ErrorMessage { get; set; }
    public string? ResultJson { get; set; }
}

/// <summary>
/// Agent heartbeat request
/// </summary>
public class AgentHeartbeat
{
    public Guid AgentId { get; set; }
    public string Status { get; set; } = "Online";
    public int CurrentJobCount { get; set; }
    public List<Guid> ActiveJobIds { get; set; } = new();
    public double CpuUsagePercent { get; set; }
    public double MemoryUsagePercent { get; set; }
    public long AvailableDiskSpaceMb { get; set; }
}
