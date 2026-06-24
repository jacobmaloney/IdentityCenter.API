namespace IdentityCenter.API.Models;

/// <summary>
/// Paged result envelope returned from <c>GET /api/objects/query</c>.
/// </summary>
public class ObjectQueryResponse
{
    public IReadOnlyList<ObjectQueryItem> Items { get; set; } = Array.Empty<ObjectQueryItem>();
    public int TotalCount { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
    public bool HasMore { get; set; }
}

/// <summary>
/// One row in <see cref="ObjectQueryResponse.Items"/>. Shape designed for
/// Conduit's <c>IConnectorSource</c> path — every field needed to build a
/// <c>ConnectorObject</c> in the IC adapter is materialised here.
/// </summary>
public class ObjectQueryItem
{
    public Guid Id { get; set; }
    public string ObjectClass { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
    public string SourceUniqueId { get; set; } = string.Empty;
    public string? CN { get; set; }
    public string? DN { get; set; }
    public string? Username { get; set; }
    public string? UserPrincipalName { get; set; }
    public string? DisplayName { get; set; }
    public bool IsActive { get; set; }
    public DateTime ModifiedAt { get; set; }
    public IReadOnlyDictionary<string, string?> Attributes { get; set; } =
        new Dictionary<string, string?>();
}

/// <summary>
/// Request body for <c>POST /api/objects/bulk</c>. <c>BatchId</c> is an
/// idempotency key supplied by the caller (Conduit run id is the typical
/// choice) — IC echoes it back in <see cref="BulkUpsertResponse.BatchId"/>.
/// </summary>
public class BulkUpsertRequest
{
    public Guid BatchId { get; set; }
    /// <summary>
    /// Durable instance GUID of the job server (Conduit installation) that pushed this
    /// batch. IC resolves it to an Agents row (auto-registering one if absent) and stamps
    /// Objects.SourceJobServerId so each object records which job server last wrote it.
    /// Null/empty leaves SourceJobServerId NULL (pre-Phase-C callers don't set this).
    /// </summary>
    public Guid? SourceJobServerId { get; set; }
    /// <summary>Friendly name of the job server, used as the auto-registered Agents.Name.</summary>
    public string? SourceJobServerName { get; set; }
    public IReadOnlyList<BulkUpsertItem> Items { get; set; } = Array.Empty<BulkUpsertItem>();
}

public class BulkUpsertItem
{
    public string SourceUniqueId { get; set; } = string.Empty;
    public string ObjectClass { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
    /// <summary>
    /// Upstream origin (e.g. "ActiveDirectory", "EntraID", "Okta") when this row
    /// is being projected through an intermediary like Conduit. Null/empty
    /// leaves the IC column NULL — pre-V126 callers don't need to set this.
    /// </summary>
    public string? OriginalSource { get; set; }
    public IReadOnlyDictionary<string, string?> Attributes { get; set; } =
        new Dictionary<string, string?>();
}

public class BulkUpsertResponse
{
    public Guid BatchId { get; set; }
    public IReadOnlyList<BulkUpsertResult> Results { get; set; } = Array.Empty<BulkUpsertResult>();
}

public class BulkUpsertResult
{
    public string SourceUniqueId { get; set; } = string.Empty;
    /// <summary>One of: Created, Updated, Skipped, Failed.</summary>
    public string Outcome { get; set; } = string.Empty;
    public string? ErrorMessage { get; set; }
}

// ── Phase 2.2 Part B: group-membership ingest ───────────────────────────────

/// <summary>
/// Request body for <c>POST /api/objects/group-memberships/bulk</c>. Conduit
/// pushes the membership edges it read from the source directory; IC resolves
/// the external ids to Objects rows and persists through the same repo primitive
/// (<c>BulkUpsertObjectGroupMembershipsAsync</c>) the internal sync uses.
/// Idempotent — re-posting the same edges is a no-op upsert.
/// </summary>
public class GroupMembershipBulkRequest
{
    public Guid BatchId { get; set; }
    /// <summary>
    /// Source string identifying the connection these edges belong to (same
    /// semantics as <see cref="BulkUpsertItem.Source"/> — typically "Conduit").
    /// Must resolve to an existing DirectoryConnection.
    /// </summary>
    public string Source { get; set; } = string.Empty;
    /// <summary>Durable instance GUID of the job server (Conduit installation) that pushed
    /// these edges. Used to keep the Agents registry live (auto-register if absent).</summary>
    public Guid? SourceJobServerId { get; set; }
    /// <summary>Friendly name of the job server, used as the auto-registered Agents.Name.</summary>
    public string? SourceJobServerName { get; set; }
    public IReadOnlyList<GroupMembershipEdge> Memberships { get; set; } = Array.Empty<GroupMembershipEdge>();
}

public class GroupMembershipEdge
{
    /// <summary>Group's SourceUniqueId (objectGUID / Entra group id / etc.).</summary>
    public string GroupSourceUniqueId { get; set; } = string.Empty;
    /// <summary>Member SourceUniqueIds belonging to the group.</summary>
    public IReadOnlyList<string> MemberSourceUniqueIds { get; set; } = Array.Empty<string>();
}

public class GroupMembershipBulkResponse
{
    public Guid BatchId { get; set; }
    public int GroupsResolved { get; set; }
    public int GroupsUnresolved { get; set; }
    public int MembersResolved { get; set; }
    public int MembersUnresolved { get; set; }
    public int EdgesPersisted { get; set; }
}

// ── Phase 2.2 Part C: tombstone soft-delete ─────────────────────────────────

/// <summary>
/// Request body for <c>POST /api/objects/tombstones</c>. Conduit emits the
/// SourceUniqueIds it detected as ABSENT from a COMPLETE source read. IC
/// SOFT-deletes (sets DeletedAt) the matching rows for the connection. NEVER a
/// hard delete; reversible if the object reappears in a later ingest.
/// </summary>
public class TombstoneRequest
{
    public Guid BatchId { get; set; }
    /// <summary>Source string identifying the connection (must resolve to a DirectoryConnection).</summary>
    public string Source { get; set; } = string.Empty;
    /// <summary>Durable instance GUID of the job server (Conduit installation) that detected
    /// these as absent. Used to keep the Agents registry live (auto-register if absent).</summary>
    public Guid? SourceJobServerId { get; set; }
    /// <summary>Friendly name of the job server, used as the auto-registered Agents.Name.</summary>
    public string? SourceJobServerName { get; set; }
    /// <summary>SourceUniqueIds to soft-delete.</summary>
    public IReadOnlyList<string> SourceUniqueIds { get; set; } = Array.Empty<string>();
    /// <summary>
    /// SAFETY OVERRIDE. When false (default) and the batch would soft-delete more
    /// than 50% of the connection's currently-live objects, IC ABORTS the delete
    /// portion and returns Aborted=true. Set true ONLY for a deliberate large
    /// purge (e.g. a connection being decommissioned).
    /// </summary>
    public bool Override { get; set; }
}

public class TombstoneResponse
{
    public Guid BatchId { get; set; }
    /// <summary>True when the 50% cap tripped and no soft-deletes were applied.</summary>
    public bool Aborted { get; set; }
    public string? AbortReason { get; set; }
    public int LiveBefore { get; set; }
    public int Requested { get; set; }
    public int Matched { get; set; }
    public int SoftDeleted { get; set; }
}

// ── Phase 2.2 Part D: ingest-triggered post-processing ──────────────────────

/// <summary>Request body for <c>POST /api/objects/post-process</c>.</summary>
public class PostProcessRequest
{
    /// <summary>Source string identifying the connection to post-process.</summary>
    public string Source { get; set; } = string.Empty;
    public bool RunPersonMatch { get; set; } = true;
    public bool RunManagerResolution { get; set; } = true;
}

public class PostProcessResponse
{
    public bool Enqueued { get; set; }
    public Guid ConnectionId { get; set; }
    public string Message { get; set; } = string.Empty;
}

// ── Phase 2.2 Part E: sign-in event ingest ──────────────────────────────────

/// <summary>
/// Request body for <c>POST /api/objects/signin-logs/bulk</c>. Conduit pushes the
/// Entra sign-in events it read from Graph; IC resolves each event's user to an
/// Objects row for the connection and persists through the set-based repo
/// primitive (<c>BulkInsertSignInLogsAsync</c>). Idempotent — re-posting the same
/// events (keyed on <see cref="SignInLogEvent.SignInId"/>) is a no-op.
/// </summary>
public class SignInLogBulkRequest
{
    public Guid BatchId { get; set; }
    /// <summary>
    /// Source string identifying the connection these events belong to (same
    /// semantics as <see cref="BulkUpsertItem.Source"/> — typically "Conduit").
    /// Must resolve to an existing DirectoryConnection.
    /// </summary>
    public string Source { get; set; } = string.Empty;
    /// <summary>Durable instance GUID of the job server (Conduit installation) that pushed
    /// these events. Used to keep the Agents registry live (auto-register if absent).</summary>
    public Guid? SourceJobServerId { get; set; }
    /// <summary>Friendly name of the job server, used as the auto-registered Agents.Name.</summary>
    public string? SourceJobServerName { get; set; }
    public IReadOnlyList<SignInLogEvent> Events { get; set; } = Array.Empty<SignInLogEvent>();
}

public class SignInLogEvent
{
    /// <summary>Graph sign-in event id — the idempotency key. Required.</summary>
    public string SignInId { get; set; } = string.Empty;
    /// <summary>Entra userId / objectGUID used to resolve the event to an Objects row.</summary>
    public string UserSourceUniqueId { get; set; } = string.Empty;
    /// <summary>UPN fallback resolver when UserSourceUniqueId does not match an Objects row.</summary>
    public string? UserPrincipalName { get; set; }
    public DateTime SignInDateTime { get; set; }
    public string? AppDisplayName { get; set; }
    public string? AppId { get; set; }
    public string? ClientAppUsed { get; set; }
    /// <summary>JSON-serialized device detail from Graph.</summary>
    public string? DeviceDetail { get; set; }
    public string? IpAddress { get; set; }
    /// <summary>JSON-serialized location from Graph.</summary>
    public string? Location { get; set; }
    /// <summary>"Success" or "Failure".</summary>
    public string? Status { get; set; }
    public int? ErrorCode { get; set; }
    public string? RiskLevel { get; set; }
    public string? RiskState { get; set; }
    public string? ConditionalAccessStatus { get; set; }
    public bool IsInteractive { get; set; }
    public string? ResourceDisplayName { get; set; }
    public string? ResourceId { get; set; }
}

public class SignInLogBulkResponse
{
    public Guid BatchId { get; set; }
    public int UsersResolved { get; set; }
    public int UsersUnresolved { get; set; }
    public int EventsPersisted { get; set; }
}

/// <summary>
/// Request body for <c>POST /api/objects/m365-usage/bulk</c>. Conduit pushes the
/// per-user M365 usage rows it joined from the Graph usage reports (ObjectClass
/// "m365usage"); IC resolves each row's user to an Objects row for the connection
/// by UPN and persists the typed M365UsageReport (idempotent MERGE keyed on
/// ObjectId + ReportRefreshDate). Mirrors <see cref="SignInLogBulkRequest"/>.
/// </summary>
public class M365UsageBulkRequest
{
    public Guid BatchId { get; set; }
    /// <summary>Source string identifying the connection these rows belong to;
    /// must resolve to an existing DirectoryConnection (same as sign-in logs).</summary>
    public string Source { get; set; } = string.Empty;
    /// <summary>Durable instance GUID of the pushing job server (Conduit installation).</summary>
    public Guid? SourceJobServerId { get; set; }
    /// <summary>Friendly name of the job server, used as the auto-registered Agents.Name.</summary>
    public string? SourceJobServerName { get; set; }
    public IReadOnlyList<M365UsageRow> Rows { get; set; } = Array.Empty<M365UsageRow>();
}

/// <summary>One per-user M365 usage row. UPN is the resolver to an Objects row.</summary>
public class M365UsageRow
{
    /// <summary>UPN — the join key to the IC user object. Required.</summary>
    public string? UserPrincipalName { get; set; }
    public string? DisplayName { get; set; }
    /// <summary>Report refresh date (the second half of the upsert key). Required.</summary>
    public DateTime? ReportRefreshDate { get; set; }

    public bool HasExchangeLicense { get; set; }
    public bool HasOneDriveLicense { get; set; }
    public bool HasSharePointLicense { get; set; }
    public bool HasTeamsLicense { get; set; }
    public bool HasYammerLicense { get; set; }

    public DateTime? ExchangeLastActivityDate { get; set; }
    public DateTime? OneDriveLastActivityDate { get; set; }
    public DateTime? SharePointLastActivityDate { get; set; }
    public DateTime? TeamsLastActivityDate { get; set; }
    public DateTime? YammerLastActivityDate { get; set; }

    public long? OneDriveStorageUsedBytes { get; set; }
    public long? OneDriveStorageAllocatedBytes { get; set; }
    public long? MailboxStorageUsedBytes { get; set; }
    public long? MailboxQuotaBytes { get; set; }

    public int? OneDriveFilesViewed { get; set; }
    public int? OneDriveFilesSynced { get; set; }
    public int? TeamsChatMessages { get; set; }
    public int? TeamsCallCount { get; set; }
    public int? TeamsMeetingCount { get; set; }

    public string? AssignedProducts { get; set; }
}

public class M365UsageBulkResponse
{
    public Guid BatchId { get; set; }
    public int UsersResolved { get; set; }
    public int UsersUnresolved { get; set; }
    public int ReportsPersisted { get; set; }
}

/// <summary>
/// Request body for <c>POST /api/objects/licenses/bulk</c>. Conduit pushes the
/// Entra license-assignment rows it read from Graph (<c>/subscribedSkus</c> joined
/// with each user's <c>assignedLicenses</c>); IC upserts the org-level
/// <c>LicensePools</c> SKU inventory and resolves each row's user (by UPN, falling
/// back to objectGUID) to an Objects row, then upserts the per-user
/// <c>LicenseAssignments</c>. Idempotent — re-posting the same rows is a no-op
/// (pool upsert keyed on connection+SkuId, assignment upsert keyed on
/// pool+ObjectId). Mirrors <see cref="M365UsageBulkRequest"/> for Source / job
/// server / batching semantics.
/// </summary>
public class LicenseBulkRequest
{
    public Guid BatchId { get; set; }
    /// <summary>Source string identifying the connection these rows belong to;
    /// must resolve to an existing DirectoryConnection (same as usage / sign-in).</summary>
    public string Source { get; set; } = string.Empty;
    /// <summary>Durable instance GUID of the pushing job server (Conduit installation).</summary>
    public Guid? SourceJobServerId { get; set; }
    /// <summary>Friendly name of the job server, used as the auto-registered Agents.Name.</summary>
    public string? SourceJobServerName { get; set; }
    public IReadOnlyList<LicenseAssignmentRow> Rows { get; set; } = Array.Empty<LicenseAssignmentRow>();
}

/// <summary>
/// One per-user, per-SKU license assignment row. The SKU fields describe the pool
/// (org-level inventory); the user fields resolve the assignee. A user holding N
/// SKUs produces N rows.
/// </summary>
public class LicenseAssignmentRow
{
    /// <summary>Entra SKU id (subscribedSku skuId GUID). Required — the pool key.</summary>
    public string SkuId { get; set; } = string.Empty;
    /// <summary>Human SKU name (e.g. "ENTERPRISEPACK"). Required for display.</summary>
    public string SkuName { get; set; } = string.Empty;
    /// <summary>SKU part number, when distinct from SkuName.</summary>
    public string? SkuPartNumber { get; set; }
    /// <summary>Pool capacity (subscribedSku prepaidUnits.enabled). Pool-level; same for every row of this SKU.</summary>
    public int? TotalUnits { get; set; }
    public int? ConsumedUnits { get; set; }
    public int? WarningUnits { get; set; }
    public int? SuspendedUnits { get; set; }

    /// <summary>UPN — the join key to the IC user object. Required.</summary>
    public string? UserPrincipalName { get; set; }
    /// <summary>Entra userId / objectGUID — fallback resolver when UPN does not match.</summary>
    public string? UserSourceUniqueId { get; set; }
    /// <summary>When the license was assigned, if Graph supplied it.</summary>
    public DateTime? AssignedAt { get; set; }
    /// <summary>"Direct" or "Group" (inherited via group-based licensing).</summary>
    public string? AssignmentSource { get; set; }
}

public class LicenseBulkResponse
{
    public Guid BatchId { get; set; }
    public int PoolsUpserted { get; set; }
    public int UsersResolved { get; set; }
    public int UsersUnresolved { get; set; }
    public int AssignmentsPersisted { get; set; }
}

/// <summary>
/// Request body for <c>POST /api/objects/app-role-assignments/bulk</c>. Conduit
/// pushes the Entra enterprise-app role assignments it read from Graph
/// (<c>servicePrincipals/{id}/appRoleAssignedTo</c>); IC resolves each assignment's
/// principal AND resource service principal to Objects rows (by objectGUID), then
/// inserts the <c>AppRoleAssignments</c> through the EXISTING repo primitive
/// (<c>BulkUpsertAppRoleAssignmentsAsync</c>, idempotent on connection +
/// AppRoleAssignmentId). Object resolution is best-effort: an unresolved principal
/// or resource is stored as a null FK (the Entra GUID + display name are still
/// retained), never dropping the assignment. Mirrors <see cref="M365UsageBulkRequest"/>.
/// </summary>
public class AppRoleAssignmentBulkRequest
{
    public Guid BatchId { get; set; }
    public string Source { get; set; } = string.Empty;
    public Guid? SourceJobServerId { get; set; }
    public string? SourceJobServerName { get; set; }
    public IReadOnlyList<AppRoleAssignmentRow> Rows { get; set; } = Array.Empty<AppRoleAssignmentRow>();
}

/// <summary>One enterprise-app role assignment row from Graph appRoleAssignedTo.</summary>
public class AppRoleAssignmentRow
{
    /// <summary>Graph appRoleAssignment id — the idempotency key. Required.</summary>
    public string? AppRoleAssignmentId { get; set; }
    /// <summary>Entra object id (GUID) of the principal (user/group/SP).</summary>
    public string? PrincipalId { get; set; }
    /// <summary>"User", "Group", or "ServicePrincipal".</summary>
    public string? PrincipalType { get; set; }
    public string? PrincipalDisplayName { get; set; }
    /// <summary>Entra object id (GUID) of the resource service principal (the enterprise app).</summary>
    public string? ResourceId { get; set; }
    public string? ResourceDisplayName { get; set; }
    /// <summary>App role GUID (Guid.Empty / null = default access).</summary>
    public string? AppRoleId { get; set; }
    public string? AppRoleName { get; set; }
    public DateTime? CreatedDateTime { get; set; }
}

public class AppRoleAssignmentBulkResponse
{
    public Guid BatchId { get; set; }
    public int PrincipalsResolved { get; set; }
    public int PrincipalsUnresolved { get; set; }
    public int AssignmentsPersisted { get; set; }
}
