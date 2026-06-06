namespace DataAccessLibrary.Services;

/// <summary>
/// Centralized service for updating Object records with automatic write-back to the target directory system.
/// All code paths that modify AD-writable fields on the Objects table should route through this service
/// to ensure DB and directory stay in sync.
/// </summary>
public interface IObjectWriteBackService
{
    /// <summary>
    /// Update one or more fields on an Object record, write changes back to the target directory,
    /// and log audit entries for each changed field.
    /// </summary>
    /// <param name="objectId">The Object ID to update</param>
    /// <param name="fields">Dictionary of DB field name → new value (null to clear)</param>
    /// <param name="source">Audit source identifier (e.g., "ManualEdit", "Lifecycle", "Remediation")</param>
    /// <param name="caller">Optional caller context for background jobs without HTTP context</param>
    /// <returns>Result with per-field success/failure details</returns>
    Task<WriteBackResult> UpdateFieldsAsync(
        Guid objectId,
        Dictionary<string, string?> fields,
        string source,
        WriteBackCallerContext? caller = null);

    /// <summary>
    /// Enable or disable an Object's account in both the database and the target directory.
    /// Handles UAC flag manipulation in AD.
    /// </summary>
    Task<WriteBackResult> SetObjectEnabledAsync(
        Guid objectId,
        bool enabled,
        string source,
        WriteBackCallerContext? caller = null);

    /// <summary>
    /// Update an Object's manager reference in both the database and the target directory.
    /// </summary>
    Task<WriteBackResult> SetObjectManagerAsync(
        Guid objectId,
        string? managerDn,
        Guid? managerObjectId,
        string source,
        WriteBackCallerContext? caller = null);

    /// <summary>
    /// Remove a license assignment from a user object. Routes through
    /// IObjectWriteBackService for centralized audit + delegation-scope checks.
    /// In DryRun mode (Settings(Category='LicenseManagement', Key='AutoReclaimMode')='DryRun')
    /// the intended action is logged and a DryRun LicenseAssignmentEvents row is written,
    /// but no source-system call is made. The default mode is DryRun.
    /// </summary>
    /// <param name="objectId">The Object ID holding the license assignment</param>
    /// <param name="licensePoolId">The pool to revoke from</param>
    /// <param name="source">Routing hint: "Entra"/"Synced" → Graph PATCH; "AD"/"AutoCount" → DB-only; "SQL" → not yet implemented</param>
    /// <param name="caller">Caller context (use WriteBackCallerContext.System(...) for jobs)</param>
    /// <remarks>
    /// System-only — bypasses delegation scope. Background-job callers must use
    /// <see cref="WriteBackCallerContext.System(string)"/> or
    /// <see cref="WriteBackCallerContext.SystemOnBehalfOf(string, string, string?)"/>.
    /// HTTP-bound callers will throw <see cref="InvalidOperationException"/>.
    /// </remarks>
    Task<WriteBackResult> RemoveLicenseAssignmentAsync(
        Guid objectId,
        Guid licensePoolId,
        string source,
        WriteBackCallerContext? caller = null,
        CancellationToken ct = default);
}

/// <summary>
/// Result of a write-back operation, including per-field details.
/// </summary>
public class WriteBackResult
{
    /// <summary>True if all operations (DB + directory) succeeded</summary>
    public bool Success { get; set; }

    /// <summary>True if the database was updated successfully</summary>
    public bool DatabaseUpdated { get; set; }

    /// <summary>True if all directory write-backs succeeded</summary>
    public bool DirectoryUpdated { get; set; }

    /// <summary>Per-field results for multi-field updates</summary>
    public List<WriteBackFieldResult> FieldResults { get; set; } = new();

    /// <summary>Errors encountered during the operation</summary>
    public List<string> Errors { get; set; } = new();

    public static WriteBackResult Failed(string error) => new()
    {
        Success = false,
        Errors = { error }
    };

    /// <summary>True when no action was taken (e.g. unsupported source). Reported as Success=true.</summary>
    public bool WasSkipped { get; set; }

    /// <summary>True when the call ran in DryRun mode and did not contact the source system.</summary>
    public bool WasDryRun { get; set; }

    public static WriteBackResult Skipped(string reason) => new()
    {
        Success = true,
        DatabaseUpdated = false,
        DirectoryUpdated = false,
        WasSkipped = true,
        Errors = { reason }
    };
}

/// <summary>
/// Result for a single field in a write-back operation.
/// </summary>
public class WriteBackFieldResult
{
    public required string FieldName { get; set; }
    public string? OldValue { get; set; }
    public string? NewValue { get; set; }
    public bool DirectoryWriteSuccess { get; set; }
    public string? Error { get; set; }
}

/// <summary>
/// Caller identity context for operations that run outside an HTTP request
/// (background jobs, lifecycle automation, workflow triggers, etc.)
/// </summary>
public class WriteBackCallerContext
{
    public string? UserId { get; set; }
    public string? UserDisplayName { get; set; }
    public string? UserEmail { get; set; }
    public string? IpAddress { get; set; }

    /// <summary>
    /// Real human user ID this system action is being performed on behalf of (e.g. the
    /// reviewer who clicked Deny in a campaign). Captured for audit so SOX/HIPAA/GDPR
    /// queries can answer "who authorized this revoke?" — not just "which subsystem ran it."
    /// </summary>
    public string? OnBehalfOfUserId { get; set; }

    /// <summary>
    /// Display name of the on-behalf-of user, when available.
    /// </summary>
    public string? OnBehalfOfDisplayName { get; set; }

    /// <summary>
    /// Audit source / subsystem identifier (e.g. "Campaign-Denial", "Lifecycle-Auto").
    /// </summary>
    public string? Source { get; set; }

    /// <summary>
    /// Create a system caller context for automated operations.
    /// </summary>
    public static WriteBackCallerContext System(string displayName = "System") => new()
    {
        UserId = "system",
        UserDisplayName = displayName
    };

    /// <summary>
    /// Create a system caller context that records the real human user the action is
    /// being performed on behalf of. UserId stays "system" (so the H1 system-only gate
    /// passes), but the reviewer's identity is captured in OnBehalfOf* for audit.
    /// </summary>
    public static WriteBackCallerContext SystemOnBehalfOf(
        string realUserId,
        string source,
        string? realDisplayName = null) => new()
    {
        UserId = "system",
        UserDisplayName = string.IsNullOrEmpty(realDisplayName)
            ? $"System on behalf of {realUserId}"
            : $"System on behalf of {realDisplayName}",
        OnBehalfOfUserId = realUserId,
        OnBehalfOfDisplayName = realDisplayName,
        Source = source
    };
}
