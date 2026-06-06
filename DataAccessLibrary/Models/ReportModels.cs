using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace DataAccessLibrary.Models;

/// <summary>
/// Core report definition model - stores report configurations
/// </summary>
public class Report
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required]
    [MaxLength(100)]
    public string Name { get; set; } = "";

    [MaxLength(200)]
    public string DisplayName { get; set; } = "";

    [MaxLength(1000)]
    public string Description { get; set; } = "";

    [Required]
    [MaxLength(50)]
    public string Category { get; set; } = "General"; // Compliance, Security, Access, Identity, Audit, Custom

    [MaxLength(50)]
    public string SubCategory { get; set; } = "";

    [MaxLength(50)]
    public string Icon { get; set; } = "fa-file-alt";

    /// <summary>
    /// The base SQL query or view name for the report
    /// </summary>
    public string QueryDefinition { get; set; } = "";

    /// <summary>
    /// JSON configuration for columns, filters, sorting
    /// </summary>
    public string ConfigurationJson { get; set; } = "{}";

    /// <summary>
    /// Default WHERE clause conditions
    /// </summary>
    public string DefaultFilters { get; set; } = "";

    /// <summary>
    /// Available parameters for the report (JSON array)
    /// </summary>
    public string ParametersJson { get; set; } = "[]";

    public bool IsBuiltIn { get; set; } = false;
    public bool IsActive { get; set; } = true;
    public bool IsPublic { get; set; } = true; // Visible to all users

    /// <summary>
    /// Required role to view this report (null = any authenticated user)
    /// </summary>
    [MaxLength(50)]
    public string? RequiredRole { get; set; }

    /// <summary>
    /// Tags for filtering/search (comma-separated)
    /// </summary>
    [MaxLength(500)]
    public string Tags { get; set; } = "";

    /// <summary>
    /// Sort order within category
    /// </summary>
    public int SortOrder { get; set; } = 0;

    // Audit fields
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string CreatedBy { get; set; } = "System";
    public DateTime? ModifiedAt { get; set; }
    public string? ModifiedBy { get; set; }

    // Navigation properties
    public virtual ICollection<ReportColumn> Columns { get; set; } = new List<ReportColumn>();
    public virtual ICollection<ReportParameter> Parameters { get; set; } = new List<ReportParameter>();
    public virtual ICollection<ReportSchedule> Schedules { get; set; } = new List<ReportSchedule>();
    public virtual ICollection<ReportExecution> Executions { get; set; } = new List<ReportExecution>();
}

/// <summary>
/// Report column definitions
/// </summary>
public class ReportColumn
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid ReportId { get; set; }

    [Required]
    [MaxLength(100)]
    public string ColumnName { get; set; } = "";

    [MaxLength(200)]
    public string DisplayName { get; set; } = "";

    [MaxLength(50)]
    public string DataType { get; set; } = "string"; // string, number, date, boolean, currency, percentage

    [MaxLength(100)]
    public string? FormatString { get; set; } // e.g., "yyyy-MM-dd", "#,##0.00"

    public int SortOrder { get; set; } = 0;
    public bool IsVisible { get; set; } = true;
    public bool AllowFilter { get; set; } = true;
    public bool AllowSort { get; set; } = true;
    public bool IsRequired { get; set; } = false;

    [MaxLength(20)]
    public string? DefaultSortDirection { get; set; } // ASC, DESC

    /// <summary>
    /// Column width (e.g., "150px", "auto", "20%")
    /// </summary>
    [MaxLength(20)]
    public string? Width { get; set; }

    /// <summary>
    /// Aggregation function for grouping (Sum, Avg, Count, Min, Max)
    /// </summary>
    [MaxLength(20)]
    public string? AggregateFunction { get; set; }

    // Navigation
    [ForeignKey("ReportId")]
    public virtual Report? Report { get; set; }
}

/// <summary>
/// Report parameter definitions for dynamic filtering
/// </summary>
public class ReportParameter
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid ReportId { get; set; }

    [Required]
    [MaxLength(100)]
    public string ParameterName { get; set; } = "";

    [MaxLength(200)]
    public string DisplayName { get; set; } = "";

    [MaxLength(50)]
    public string DataType { get; set; } = "string"; // string, number, date, boolean, list

    [MaxLength(50)]
    public string ControlType { get; set; } = "text"; // text, dropdown, datepicker, checkbox, multiselect

    public bool IsRequired { get; set; } = false;

    public string? DefaultValue { get; set; }

    /// <summary>
    /// For dropdowns - JSON array of options or SQL query
    /// </summary>
    public string? OptionsSource { get; set; }

    /// <summary>
    /// Validation rules (JSON)
    /// </summary>
    public string? ValidationRules { get; set; }

    public int SortOrder { get; set; } = 0;

    // Navigation
    [ForeignKey("ReportId")]
    public virtual Report? Report { get; set; }
}

/// <summary>
/// Report scheduling for automated execution
/// </summary>
public class ReportSchedule
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid ReportId { get; set; }

    [Required]
    [MaxLength(100)]
    public string Name { get; set; } = "";

    [MaxLength(50)]
    public string Frequency { get; set; } = "Daily"; // Daily, Weekly, Monthly, Quarterly, Annually

    /// <summary>
    /// Cron expression for precise scheduling
    /// </summary>
    [MaxLength(100)]
    public string? CronExpression { get; set; }

    /// <summary>
    /// Time of day to execute (e.g., "06:00")
    /// </summary>
    [MaxLength(10)]
    public string ExecutionTime { get; set; } = "06:00";

    /// <summary>
    /// Day of week for weekly reports (0-6, Sunday=0)
    /// </summary>
    public int? DayOfWeek { get; set; }

    /// <summary>
    /// Day of month for monthly reports (1-31)
    /// </summary>
    public int? DayOfMonth { get; set; }

    public bool IsActive { get; set; } = true;

    /// <summary>
    /// Output format: PDF, Excel, CSV, HTML
    /// </summary>
    [MaxLength(20)]
    public string OutputFormat { get; set; } = "PDF";

    /// <summary>
    /// Email recipients (comma-separated)
    /// </summary>
    public string EmailRecipients { get; set; } = "";

    [MaxLength(200)]
    public string? EmailSubject { get; set; }

    public string? EmailBody { get; set; }

    public bool AttachReport { get; set; } = true;
    public bool EmbedInEmail { get; set; } = false;

    /// <summary>
    /// Parameter values for scheduled execution (JSON)
    /// </summary>
    public string? ParameterValuesJson { get; set; }

    public DateTime? LastExecutedAt { get; set; }
    public DateTime? NextExecutionAt { get; set; }
    public string? LastExecutionStatus { get; set; }
    public string? LastExecutionError { get; set; }

    // Audit
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string CreatedBy { get; set; } = "";

    // Navigation
    [ForeignKey("ReportId")]
    public virtual Report? Report { get; set; }
}

/// <summary>
/// Report execution history for auditing
/// </summary>
public class ReportExecution
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid ReportId { get; set; }
    public Guid? ScheduleId { get; set; }

    public DateTime ExecutedAt { get; set; } = DateTime.UtcNow;
    public string ExecutedBy { get; set; } = "";

    [MaxLength(50)]
    public string ExecutionContext { get; set; } = "Manual"; // Manual, Scheduled, API

    /// <summary>
    /// Time taken to execute (milliseconds)
    /// </summary>
    public int ExecutionTimeMs { get; set; }

    [Column("RowCount")]
    public int ResultRowCount { get; set; }

    [MaxLength(20)]
    public string Status { get; set; } = "Completed"; // Running, Completed, Failed, Cancelled

    public string? ErrorMessage { get; set; }

    /// <summary>
    /// Parameters used for this execution (JSON)
    /// </summary>
    public string? ParametersUsed { get; set; }

    /// <summary>
    /// Output format used
    /// </summary>
    [MaxLength(20)]
    public string? OutputFormat { get; set; }

    /// <summary>
    /// File path if exported
    /// </summary>
    public string? OutputFilePath { get; set; }

    // Navigation
    [ForeignKey("ReportId")]
    public virtual Report? Report { get; set; }

    [ForeignKey("ScheduleId")]
    public virtual ReportSchedule? Schedule { get; set; }
}

/// <summary>
/// User's saved/favorite reports
/// </summary>
public class UserReportFavorite
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid UserId { get; set; }
    public Guid ReportId { get; set; }

    public DateTime AddedAt { get; set; } = DateTime.UtcNow;
    public int SortOrder { get; set; } = 0;

    /// <summary>
    /// User's saved parameter values (JSON)
    /// </summary>
    public string? SavedParametersJson { get; set; }

    // Navigation
    [ForeignKey("ReportId")]
    public virtual Report? Report { get; set; }
}

/// <summary>
/// Report template for creating new reports
/// </summary>
public class ReportTemplate
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required]
    [MaxLength(100)]
    public string Name { get; set; } = "";

    [MaxLength(500)]
    public string Description { get; set; } = "";

    [MaxLength(50)]
    public string Category { get; set; } = "";

    [MaxLength(50)]
    public string Icon { get; set; } = "fa-file-alt";

    /// <summary>
    /// Full report configuration as JSON template
    /// </summary>
    public string ConfigurationTemplate { get; set; } = "{}";

    public bool IsBuiltIn { get; set; } = true;
    public bool IsActive { get; set; } = true;

    public int SortOrder { get; set; } = 0;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
