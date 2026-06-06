using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace DataAccessLibrary.Models;

/// <summary>
/// Defines a reusable script for sync pre-processing or post-processing.
/// Scripts are C# code compiled at runtime using Roslyn.
/// System scripts are read-only defaults; users can copy and customize.
/// </summary>
public class SyncProcessingScript
{
    [Key]
    public Guid Id { get; set; }

    /// <summary>
    /// Human-readable name (e.g., "ConvertBinaryValues", "CreateOrUpdateIdentity")
    /// </summary>
    [Required]
    [MaxLength(200)]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Detailed description of what this script does
    /// </summary>
    [MaxLength(2000)]
    public string? Description { get; set; }

    /// <summary>
    /// Script type: "PreProcessing" or "PostProcessing"
    /// </summary>
    [Required]
    [MaxLength(50)]
    public string ScriptType { get; set; } = "PostProcessing";

    /// <summary>
    /// The actual C# code to execute
    /// </summary>
    [Required]
    public string ScriptCode { get; set; } = string.Empty;

    /// <summary>
    /// If true, this is a system default script (read-only, can be copied)
    /// </summary>
    public bool IsSystem { get; set; }

    /// <summary>
    /// If true, script is available for use
    /// </summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>
    /// Script version for tracking changes
    /// </summary>
    public int Version { get; set; } = 1;

    /// <summary>
    /// Category for organization (e.g., "Identity", "Manager", "Attributes", "Custom")
    /// </summary>
    [MaxLength(100)]
    public string Category { get; set; } = "Custom";

    /// <summary>
    /// Compilation status: "NotCompiled", "Success", "Error"
    /// </summary>
    [MaxLength(50)]
    public string CompilationStatus { get; set; } = "NotCompiled";

    /// <summary>
    /// Error message if compilation failed
    /// </summary>
    public string? CompilationError { get; set; }

    /// <summary>
    /// When the script was last compiled
    /// </summary>
    public DateTime? LastCompiledAt { get; set; }

    /// <summary>
    /// When this script was created
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Who created this script (username or "System")
    /// </summary>
    [MaxLength(256)]
    public string CreatedBy { get; set; } = "System";

    /// <summary>
    /// When this script was last modified
    /// </summary>
    public DateTime? ModifiedAt { get; set; }

    /// <summary>
    /// Who last modified this script
    /// </summary>
    [MaxLength(256)]
    public string? ModifiedBy { get; set; }

    /// <summary>
    /// If this is a copy of a system script, references the original
    /// </summary>
    public Guid? CopiedFromScriptId { get; set; }

    /// <summary>
    /// Navigation: Steps using this script
    /// </summary>
    public ICollection<SyncStepScript> StepScripts { get; set; } = new List<SyncStepScript>();

    /// <summary>
    /// Navigation: Execution history
    /// </summary>
    public ICollection<SyncScriptExecution> Executions { get; set; } = new List<SyncScriptExecution>();
}

/// <summary>
/// Join table linking scripts to sync steps.
/// A step can have multiple scripts for each phase (pre/post).
/// Scripts execute in order by ExecutionOrder.
/// </summary>
public class SyncStepScript
{
    [Key]
    public Guid Id { get; set; }

    /// <summary>
    /// The sync step this script is attached to
    /// </summary>
    public Guid SyncStepId { get; set; }

    /// <summary>
    /// The script to execute
    /// </summary>
    public Guid ScriptId { get; set; }

    /// <summary>
    /// Execution phase: "PreProcessing" or "PostProcessing"
    /// </summary>
    [Required]
    [MaxLength(50)]
    public string ExecutionPhase { get; set; } = "PostProcessing";

    /// <summary>
    /// Order in which scripts execute within the same phase (lower = first)
    /// </summary>
    public int ExecutionOrder { get; set; } = 0;

    /// <summary>
    /// If false, script is skipped for this step
    /// </summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>
    /// JSON object with parameter overrides for this specific step
    /// </summary>
    public string? ParameterOverrides { get; set; }

    /// <summary>
    /// Navigation: The sync step
    /// </summary>
    [ForeignKey("SyncStepId")]
    public SyncStep? SyncStep { get; set; }

    /// <summary>
    /// Navigation: The script
    /// </summary>
    [ForeignKey("ScriptId")]
    public SyncProcessingScript? Script { get; set; }
}

/// <summary>
/// Audit trail for script executions during sync runs.
/// Records execution time, status, and any errors.
/// </summary>
public class SyncScriptExecution
{
    [Key]
    public Guid Id { get; set; }

    /// <summary>
    /// The step run during which this script executed
    /// </summary>
    public Guid SyncStepRunId { get; set; }

    /// <summary>
    /// The script that was executed
    /// </summary>
    public Guid ScriptId { get; set; }

    /// <summary>
    /// Execution phase: "PreProcessing" or "PostProcessing"
    /// </summary>
    [Required]
    [MaxLength(50)]
    public string ExecutionPhase { get; set; } = "PostProcessing";

    /// <summary>
    /// Execution status: "Success", "Error", "Skipped", "Timeout"
    /// </summary>
    [Required]
    [MaxLength(50)]
    public string Status { get; set; } = "Success";

    /// <summary>
    /// When script execution started
    /// </summary>
    public DateTime StartedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// When script execution completed
    /// </summary>
    public DateTime? CompletedAt { get; set; }

    /// <summary>
    /// Execution duration in milliseconds
    /// </summary>
    public int? DurationMs { get; set; }

    /// <summary>
    /// Number of objects the script received as input
    /// </summary>
    public int ObjectsProcessed { get; set; }

    /// <summary>
    /// Number of objects the script modified
    /// </summary>
    public int ObjectsModified { get; set; }

    /// <summary>
    /// Number of identities created (for CreateOrUpdateIdentity script)
    /// </summary>
    public int IdentitiesCreated { get; set; }

    /// <summary>
    /// Number of manager relationships resolved
    /// </summary>
    public int ManagersResolved { get; set; }

    /// <summary>
    /// Error message if script failed
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// JSON array of log entries from the script's Log calls
    /// </summary>
    public string? OutputLog { get; set; }

    /// <summary>
    /// Navigation: The step run
    /// </summary>
    [ForeignKey("SyncStepRunId")]
    public SyncStepRun? StepRun { get; set; }

    /// <summary>
    /// Navigation: The script
    /// </summary>
    [ForeignKey("ScriptId")]
    public SyncProcessingScript? Script { get; set; }
}

/// <summary>
/// Script types for categorization
/// </summary>
public static class ScriptTypes
{
    public const string PreProcessing = "PreProcessing";
    public const string PostProcessing = "PostProcessing";
}

/// <summary>
/// Script categories for organization
/// </summary>
public static class ScriptCategories
{
    public const string Attributes = "Attributes";
    public const string Identity = "Identity";
    public const string Manager = "Manager";
    public const string Groups = "Groups";
    public const string Custom = "Custom";
}

/// <summary>
/// Compilation status values
/// </summary>
public static class CompilationStatus
{
    public const string NotCompiled = "NotCompiled";
    public const string Success = "Success";
    public const string Error = "Error";
}

/// <summary>
/// Execution status values
/// </summary>
public static class ExecutionStatus
{
    public const string Success = "Success";
    public const string Error = "Error";
    public const string Skipped = "Skipped";
    public const string Timeout = "Timeout";
    public const string Running = "Running";
}
