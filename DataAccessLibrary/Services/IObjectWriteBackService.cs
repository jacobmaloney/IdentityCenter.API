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
        WriteBackCallerContext? caller = null,
        string? stepUpToken = null);

    /// <summary>
    /// Enable or disable an Object's account in both the database and the target directory.
    /// Handles UAC flag manipulation in AD.
    /// </summary>
    /// <param name="stepUpToken">
    /// When <paramref name="requireStepUp"/> is true, a server-issued single-use step-up token
    /// bound to (caller, EnableDisable, objectId). Verified server-side before the write.
    /// </param>
    /// <param name="requireStepUp">
    /// True for interactive surfaces that mandate step-up on enable/disable (e.g. the Entra Manage
    /// pane). When true and the token is absent/invalid for a non-system caller, the write is
    /// rejected and audited. Defaults false so existing remediation/automation callers are
    /// unaffected.
    /// </param>
    Task<WriteBackResult> SetObjectEnabledAsync(
        Guid objectId,
        bool enabled,
        string source,
        WriteBackCallerContext? caller = null,
        string? stepUpToken = null,
        bool requireStepUp = false);

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

    /// <summary>
    /// Replace the proxyAddresses multivalued attribute on an Entra ID user. The supplied list
    /// is the COMPLETE desired set (add/remove/set-primary are expressed by the full target
    /// list). Gated under <see cref="WriteCapability.EditAttributes"/>, validated server-side in
    /// the connector (whole payload rejected on any malformed entry), and audited with before/
    /// after. Interactive callers must pass a real <paramref name="caller"/> built from the
    /// authenticated principal.
    /// </summary>
    /// <param name="stepUpToken">
    /// Server-issued single-use step-up token bound to (caller, EditProxyAddresses, objectId).
    /// proxyAddress edits are always step-up-required for non-system callers; verified server-side.
    /// </param>
    Task<WriteBackResult> UpdateProxyAddressesAsync(
        Guid objectId,
        IReadOnlyList<string> proxyAddresses,
        string source,
        WriteBackCallerContext? caller = null,
        string? stepUpToken = null,
        CancellationToken ct = default);

    /// <summary>
    /// Create a new directory object (user, group, …) in the target system and ingest the
    /// resulting record into the Objects table. The SINGLE create entry point — routes to the
    /// correct connector via the write-service factory, runs the read-only-default + delegation
    /// capability gate, and writes a Create audit row (Success=false on denial/failure, real UserId).
    /// </summary>
    /// <param name="connectionId">The connection the object is created in.</param>
    /// <param name="objectClass">"user" | "group" | … (drives capability + factory routing).</param>
    /// <param name="sourceType">"EntraID" | "ActiveDirectory" | … for schema/scope resolution.</param>
    /// <param name="fields">Schema field key → value (already server-side validated by the caller).</param>
    /// <param name="source">Audit source identifier (e.g. "ManualCreate").</param>
    /// <param name="caller">Caller context. Interactive callers are subject to the capability gate.</param>
    Task<CreateObjectResult> CreateObjectAsync(
        Guid connectionId,
        string objectClass,
        string sourceType,
        Dictionary<string, string?> fields,
        string source,
        WriteBackCallerContext? caller = null,
        CancellationToken ct = default);

    /// <summary>
    /// Phase D increment 2: route an Active-Directory write to the originating job server
    /// (Conduit agent) over the existing HTTP agent-command channel (Option B). The agent
    /// performs the LDAP mutation; IC never holds AD credentials. Enqueues an
    /// "ApplyObjectWrite" AgentCommand and returns immediately with Status="Pending" — the
    /// UI polls <see cref="GetAgentWriteStatusAsync"/> for the result.
    /// </summary>
    Task<AgentWriteDispatchResult> ApplyAdWriteViaAgentAsync(
        Guid objectId,
        AdAgentWriteRequest request,
        WriteBackCallerContext caller,
        string? stepUpToken = null);

    /// <summary>
    /// Read the current status of an agent-routed AD write by its command id. The UI polls
    /// this until it transitions to Completed/Failed. The agent's ResultMessage is returned
    /// verbatim and MUST be treated as untrusted (encode before rendering).
    /// </summary>
    Task<AgentWriteDispatchResult> GetAgentWriteStatusAsync(Guid commandId);
}

/// <summary>
/// The AWS IAM operations the agent-routed write path supports. Names match the payload contract
/// the consuming Conduit agent obeys exactly — do not rename without updating both sides.
/// </summary>
public enum AwsAgentWriteOperation
{
    TagUser,
    UntagUser,
    AddGroupMember,
    RemoveGroupMember,
    AttachManagedPolicy,
    DetachManagedPolicy,
    EnableAccessKey,
    DisableAccessKey,
    RemoveConsoleAccess
}

/// <summary>
/// A client-shaped AWS IAM write request. Carries ONLY the operation and its data — never the
/// target agent, connection, or account, which the coordinator resolves server-side from the
/// object's own provenance. No secret/access-key material exists by design.
/// </summary>
public sealed class AwsAgentWriteRequest
{
    public AwsAgentWriteOperation Operation { get; init; }

    /// <summary>Tag key for Tag/UntagUser.</summary>
    public string? TagKey { get; init; }

    /// <summary>Tag value for TagUser (null for UntagUser).</summary>
    public string? TagValue { get; init; }

    /// <summary>The IAM group for Add/RemoveGroupMember.</summary>
    public string? MemberGroupName { get; init; }

    /// <summary>The subject IAM user being acted on.</summary>
    public string? UserName { get; init; }

    /// <summary>The subject IAM group, for group-targeted managed-policy attach/detach.</summary>
    public string? GroupName { get; init; }

    /// <summary>The managed-policy ARN for Attach/DetachManagedPolicy.</summary>
    public string? PolicyArn { get; init; }

    /// <summary>The access key id for Enable/DisableAccessKey.</summary>
    public string? AccessKeyId { get; init; }

    /// <summary>Human-readable summary for the audit row only — never placed in the payload.</summary>
    public string? DisplayName { get; init; }
}

/// <summary>
/// The AD operations the agent-routed write path supports. Names match the payload contract
/// the consuming Conduit agent obeys exactly — do not rename without updating both sides.
/// </summary>
public enum AdAgentWriteOperation
{
    SetAttributes,
    Enable,
    Disable,
    SetManager,
    AddGroupMember,
    RemoveGroupMember
}

/// <summary>
/// A client-shaped AD write request. Carries ONLY the operation and its data — never the
/// target agent, connection, or DN, which the coordinator resolves server-side from the
/// object's own provenance. <see cref="Attributes"/> applies to SetAttributes/SetManager;
/// <see cref="MemberObjectGuid"/> applies to Add/RemoveGroupMember.
/// </summary>
public sealed class AdAgentWriteRequest
{
    public AdAgentWriteOperation Operation { get; init; }

    /// <summary>adAttribute -> value (null clears). For SetManager use key "manager" (DN value).</summary>
    public Dictionary<string, string?> Attributes { get; init; } = new();

    /// <summary>objectGUID of the member being added/removed (Add/RemoveGroupMember only).</summary>
    public string? MemberObjectGuid { get; init; }

    /// <summary>
    /// Display name of the member, for the audit row only — never placed in the payload.
    /// </summary>
    public string? MemberDisplayName { get; init; }
}

/// <summary>
/// Result of dispatching (or reading the status of) an agent-routed AD write. The write is
/// asynchronous: a successful dispatch returns Status="Pending" with the command id; the
/// terminal state ("Completed"/"Failed") and the agent message arrive on a later poll.
/// </summary>
public sealed class AgentWriteDispatchResult
{
    public bool Dispatched { get; init; }
    public Guid? CommandId { get; init; }

    /// <summary>"Pending" | "Acked" | "Completed" | "Failed" | "Denied" | "Error".</summary>
    public string Status { get; init; } = "Pending";

    /// <summary>Set once the command reaches a terminal state (null while in flight).</summary>
    public bool? Success { get; init; }

    /// <summary>Agent result message (UNTRUSTED) or a local denial/error reason.</summary>
    public string? Message { get; init; }

    public static AgentWriteDispatchResult Denied(string reason) =>
        new() { Dispatched = false, Status = "Denied", Success = false, Message = reason };

    public static AgentWriteDispatchResult Error(string reason) =>
        new() { Dispatched = false, Status = "Error", Success = false, Message = reason };

    public static AgentWriteDispatchResult Pending(Guid commandId) =>
        new() { Dispatched = true, CommandId = commandId, Status = "Pending" };
}

/// <summary>
/// Result of a create operation. On success the directory object exists; the local record may
/// not yet be visible due to Graph eventual consistency (<see cref="PendingVisibility"/>).
/// </summary>
public class CreateObjectResult
{
    public bool Success { get; set; }
    public Guid? ObjectId { get; set; }
    /// <summary>The source-system object id (Entra object id / AD objectGUID) from the create response.</summary>
    public string? SourceUniqueId { get; set; }
    public string? DisplayName { get; set; }
    /// <summary>True when the object was created but is not yet visible on immediate re-query.</summary>
    public bool PendingVisibility { get; set; }
    /// <summary>Typed denial reason when the capability gate blocked the create (else None).</summary>
    public WriteDenialReason DenialReason { get; set; } = WriteDenialReason.None;
    public List<string> Errors { get; set; } = new();

    public static CreateObjectResult Failed(string error) =>
        new() { Success = false, Errors = { error } };

    public static CreateObjectResult Denied(WriteCapabilityDecision decision) =>
        new() { Success = false, DenialReason = decision.Reason, Errors = { decision.Message } };
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
