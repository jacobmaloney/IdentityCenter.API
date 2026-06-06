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
