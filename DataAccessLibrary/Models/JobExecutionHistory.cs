using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace DataAccessLibrary.Models;

/// <summary>
/// Tracks execution history for all scheduled jobs across the system.
/// Provides auditing, monitoring, and troubleshooting capabilities.
/// </summary>
[Table("JobExecutionHistory")]
public class JobExecutionHistory
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>
    /// Type of job: SyncProject, PolicyEvaluation, ReportGeneration, SystemMaintenance, ReviewReminder, Escalation
    /// </summary>
    [Required]
    [MaxLength(50)]
    public string JobType { get; set; } = string.Empty;

    /// <summary>
    /// Name of the job for display
    /// </summary>
    [MaxLength(200)]
    public string JobName { get; set; } = string.Empty;

    /// <summary>
    /// Optional related entity ID (e.g., SyncProjectId, PolicyId, ScheduleId)
    /// </summary>
    public Guid? RelatedEntityId { get; set; }

    /// <summary>
    /// Type of the related entity for filtering
    /// </summary>
    [MaxLength(50)]
    public string? RelatedEntityType { get; set; }

    /// <summary>
    /// Quartz job instance ID
    /// </summary>
    [MaxLength(100)]
    public string? QuartzJobId { get; set; }

    /// <summary>
    /// How the job was triggered: Scheduled, Manual, API, System
    /// </summary>
    [Required]
    [MaxLength(50)]
    public string TriggerType { get; set; } = "Scheduled";

    /// <summary>
    /// Who or what triggered the job
    /// </summary>
    [MaxLength(200)]
    public string TriggeredBy { get; set; } = "System";

    /// <summary>
    /// When the job started
    /// </summary>
    [Required]
    public DateTime StartedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// When the job completed (null if still running)
    /// </summary>
    public DateTime? CompletedAt { get; set; }

    /// <summary>
    /// Duration in milliseconds
    /// </summary>
    public int? DurationMs { get; set; }

    /// <summary>
    /// Job status: Running, Completed, Failed, Cancelled
    /// </summary>
    [Required]
    [MaxLength(20)]
    public string Status { get; set; } = "Running";

    /// <summary>
    /// Number of items processed (e.g., records synced, reports generated)
    /// </summary>
    public int ItemsProcessed { get; set; }

    /// <summary>
    /// Number of items that succeeded
    /// </summary>
    public int ItemsSucceeded { get; set; }

    /// <summary>
    /// Number of items that failed
    /// </summary>
    public int ItemsFailed { get; set; }

    /// <summary>
    /// Result summary (JSON) - job-specific metrics
    /// </summary>
    public string? ResultSummaryJson { get; set; }

    /// <summary>
    /// Error message if job failed
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// Full exception details for debugging
    /// </summary>
    public string? ExceptionDetails { get; set; }

    /// <summary>
    /// Server/machine that executed the job (for clustered scenarios)
    /// </summary>
    [MaxLength(100)]
    public string? ExecutingServer { get; set; }

    /// <summary>
    /// Next scheduled execution time (if recurring)
    /// </summary>
    public DateTime? NextScheduledRun { get; set; }

    /// <summary>
    /// Was this a retry attempt?
    /// </summary>
    public bool IsRetry { get; set; }

    /// <summary>
    /// Retry count (0 for first attempt)
    /// </summary>
    public int RetryCount { get; set; }

    /// <summary>
    /// Parent execution ID if this is a retry
    /// </summary>
    public Guid? ParentExecutionId { get; set; }

    // -------------------------------------------------------------------------
    // Distributed Execution Server extensions (V052)
    // -------------------------------------------------------------------------

    /// <summary>
    /// FK to RemoteAgents.Id — the execution server that ran this job.
    /// Null for legacy history rows recorded before V052.
    /// </summary>
    public Guid? ExecutionServerId { get; set; }

    /// <summary>
    /// Display name of the execution server (denormalised for query convenience).
    /// Populated at record creation from RemoteAgents.AgentName.
    /// </summary>
    [MaxLength(200)]
    public string? ExecutionServerName { get; set; }
}

/// <summary>
/// Summary statistics for job execution dashboard
/// </summary>
public class JobExecutionStatistics
{
    public string JobType { get; set; } = string.Empty;
    public int TotalExecutions { get; set; }
    public int SuccessfulExecutions { get; set; }
    public int FailedExecutions { get; set; }
    public int CancelledExecutions { get; set; }
    public double SuccessRate { get; set; }
    public double AverageDurationMs { get; set; }
    public DateTime? LastExecution { get; set; }
    public DateTime? NextScheduledRun { get; set; }
}

/// <summary>
/// Recent job execution for monitoring dashboard
/// </summary>
public class RecentJobExecution
{
    public Guid Id { get; set; }
    public string JobType { get; set; } = string.Empty;
    public string JobName { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTime StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public int? DurationMs { get; set; }
    public int ItemsProcessed { get; set; }
    public string? ErrorMessage { get; set; }
    public string TriggerType { get; set; } = string.Empty;
    public string TriggeredBy { get; set; } = string.Empty;
}
