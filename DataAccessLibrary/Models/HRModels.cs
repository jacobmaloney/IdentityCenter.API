using System.ComponentModel.DataAnnotations;

namespace DataAccessLibrary.Models;

/// <summary>
/// Configuration for HR data source connections. Serialized into DirectoryConnection.Configuration JSON.
/// Supports CSV file import, REST API polling, and SCIM 2.0 provisioning endpoints.
/// </summary>
public class HRConnectionConfig
{
    /// <summary>CSV, RESTAPI, or SCIM</summary>
    [Required]
    public string SourceType { get; set; } = "CSV";

    // === CSV-specific ===
    public string? FileUploadPath { get; set; }
    public string Delimiter { get; set; } = ",";
    public bool HasHeaderRow { get; set; } = true;
    public string Encoding { get; set; } = "UTF-8";

    // === REST API-specific ===
    public string? ApiBaseUrl { get; set; }
    public string? ApiEndpoint { get; set; }
    public string HttpMethod { get; set; } = "GET";
    public string ResponseFormat { get; set; } = "JSON";
    /// <summary>JSONPath-style path to the data array in the API response (e.g. "data.employees")</summary>
    public string? DataPath { get; set; }
    public Dictionary<string, string>? Headers { get; set; }
    /// <summary>None, Offset, Cursor, LinkHeader</summary>
    public string PaginationType { get; set; } = "None";
    public int PageSize { get; set; } = 100;

    // === SCIM-specific ===
    public string? ScimEndpoint { get; set; }
    public string ScimVersion { get; set; } = "2.0";

    // === Shared ===
    /// <summary>The source field used as the unique identifier for matching (default: EmployeeId)</summary>
    public string UniqueIdField { get; set; } = "EmployeeId";
    public int ImportBatchSize { get; set; } = 500;
}

/// <summary>
/// Credentials for HR data source connections. Encrypted into DirectoryConnection.Credentials.
/// Supports API key, Bearer token, basic auth, and OAuth2 client credentials.
/// </summary>
public class HRCredentials
{
    public string? ApiKey { get; set; }
    public string? BearerToken { get; set; }
    public string? Username { get; set; }
    public string? Password { get; set; }
    public string? ClientId { get; set; }
    public string? ClientSecret { get; set; }
    public string? TokenEndpoint { get; set; }
}

/// <summary>
/// Maps a source field from HR data to an Identity table property.
/// Stored in the HRFieldMappings table.
/// </summary>
public class HRFieldMapping
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid DirectoryConnectionId { get; set; }

    [Required]
    [MaxLength(200)]
    public string SourceField { get; set; } = string.Empty;

    [Required]
    [MaxLength(200)]
    public string TargetField { get; set; } = string.Empty;

    public bool IsRequired { get; set; }

    [MaxLength(500)]
    public string? DefaultValue { get; set; }

    /// <summary>Transformation to apply: None, Uppercase, Lowercase, TitleCase, DateParse, Trim</summary>
    [MaxLength(100)]
    public string? Transformation { get; set; }

    public int MappingOrder { get; set; }

    public bool IsEnabled { get; set; } = true;

    /// <summary>If true, this field is used for matching/deduplication (e.g., EmployeeId)</summary>
    public bool IsKeyField { get; set; }
}

/// <summary>
/// Tracks execution history for an HR import run.
/// Stored in the HRImportRuns table.
/// </summary>
public class HRImportRun
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid SyncProjectId { get; set; }

    /// <summary>Running, Completed, Failed, Cancelled</summary>
    [MaxLength(50)]
    public string Status { get; set; } = "Running";

    /// <summary>Original filename for CSV imports</summary>
    [MaxLength(500)]
    public string? SourceFileName { get; set; }

    public int TotalRecords { get; set; }
    public int CreatedRecords { get; set; }
    public int UpdatedRecords { get; set; }
    public int SkippedRecords { get; set; }
    public int ErrorRecords { get; set; }
    public int EnabledRecords { get; set; }
    public int DisabledRecords { get; set; }

    /// <summary>JSON array of per-row errors: [{row, field, error}]</summary>
    public string? ErrorDetails { get; set; }

    public DateTime StartedAt { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAt { get; set; }
    public int DurationSeconds { get; set; }
}

/// <summary>
/// Result from an HR data source read operation.
/// </summary>
public class HRDataReadResult
{
    public List<Dictionary<string, object?>> Records { get; set; } = new();
    public List<string> FieldNames { get; set; } = new();
    public int TotalRecords { get; set; }
    public string? ErrorMessage { get; set; }
    public bool Success => string.IsNullOrEmpty(ErrorMessage);
}

/// <summary>
/// Result from an HR import bulk upsert operation.
/// </summary>
public class HRImportResult
{
    public int Created { get; set; }
    public int Updated { get; set; }
    public int Skipped { get; set; }
    public int Errors { get; set; }
    public int Enabled { get; set; }
    public int Disabled { get; set; }
    public List<HRImportError> ErrorList { get; set; } = new();
    public bool Success => Errors == 0;
    public string? ErrorMessage { get; set; }

    /// <summary>IDs of Identities created during this import run.</summary>
    public List<Guid> CreatedIdentityIds { get; set; } = new();

    /// <summary>IDs of Identities updated during this import run.</summary>
    public List<Guid> UpdatedIdentityIds { get; set; } = new();

    /// <summary>Detailed change tracking for updated Identities (lifecycle field changes).</summary>
    public List<HRIdentityChange> UpdatedIdentityChanges { get; set; } = new();
}

public class HRImportError
{
    public int Row { get; set; }
    public string? Field { get; set; }
    public string Error { get; set; } = string.Empty;
}

/// <summary>
/// Tracks which lifecycle-relevant fields changed for an updated Identity.
/// Used to trigger Mover/Leaver events in the lifecycle engine.
/// </summary>
public class HRIdentityChange
{
    public Guid IdentityId { get; set; }
    public string EmployeeId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public List<string> ChangedFields { get; set; } = new();
}

/// <summary>
/// Configuration for AD provisioning from lifecycle actions.
/// </summary>
public class ADProvisioningConfig
{
    public string TargetOU { get; set; } = string.Empty;
    public string? UpnSuffix { get; set; }
    public string SamAccountNamePattern { get; set; } = "firstname.lastname";
    public string? DefaultPassword { get; set; }
    public bool EnableAccounts { get; set; } = true;
}

/// <summary>
/// Configuration for HR Import lifecycle filtering. Serialized into SyncStep.Configuration JSON.
/// Controls whether identities are created/disabled based on source employment status.
/// </summary>
public class HRImportStepConfig
{
    /// <summary>Source field name containing employment status (e.g., "Status", "EmploymentStatus")</summary>
    public string? StatusField { get; set; }

    /// <summary>Values that mean "active" — case-insensitive (e.g., "Active", "Employed")</summary>
    public List<string> ActiveStatusValues { get; set; } = new() { "Active", "Employed" };

    /// <summary>Values that mean "inactive" — case-insensitive (e.g., "Terminated", "Inactive", "Leave")</summary>
    public List<string> InactiveStatusValues { get; set; } = new() { "Terminated", "Inactive", "Leave" };

    /// <summary>How to evaluate the StatusField: "StringMatch" (default) or "DateInPast".</summary>
    public string EvaluationMode { get; set; } = "StringMatch";

    /// <summary>Skip creating identities when source status is inactive. Default: false.</summary>
    public bool SkipCreateWhenInactive { get; set; }

    /// <summary>Sync IsActive flag from status field — disable on inactive, re-enable on active. Default: false.</summary>
    public bool SyncIsActiveFromStatus { get; set; }
}
