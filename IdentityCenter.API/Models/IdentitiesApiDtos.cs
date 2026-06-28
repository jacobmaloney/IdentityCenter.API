namespace IdentityCenter.API.Models;

// ── Table-to-table connector DTOs ───────────────────────────────────────────
//
// These power the Conduit IdentityCenter connector when it is parameterised to
// the Identities (people) table rather than the Objects (accounts) table:
//   - Conduit SOURCES from Identities via GET  /api/identities/query
//   - Conduit SINKS   into Identities via POST /api/identities/bulk
//
// LOCKED BOUNDARY: the Identities table is the correlated golden-record people
// table. These endpoints do RAW, DETERMINISTIC, field-mapped movement ONLY —
// they pick a row by an EXACT key the payload carries and write the mapped
// columns. They do NOT run PersonMatchOrchestrator or any fuzzy object↔person
// correlation; that governance stays inside IC. (Contrast the pre-existing
// /api/identities/match probe, which IS a correlation helper for Conduit's
// Phase-7 person-aware workflow steps and is unrelated to this raw path.)

/// <summary>
/// Paged result envelope returned from <c>GET /api/identities/query</c>. Mirrors
/// <see cref="ObjectQueryResponse"/> so the Conduit source path treats both
/// tables uniformly.
/// </summary>
public class IdentityQueryResponse
{
    public IReadOnlyList<IdentityQueryItem> Items { get; set; } = Array.Empty<IdentityQueryItem>();
    public int TotalCount { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
    public bool HasMore { get; set; }
}

/// <summary>
/// One row in <see cref="IdentityQueryResponse.Items"/>. The <see cref="KeyField"/>
/// + <see cref="KeyValue"/> pair tells a downstream sink which deterministic key
/// the row is addressable by, so an Identities→Identities round-trip is stable.
/// <see cref="Attributes"/> carries the full typed-column projection (flattened)
/// so the Conduit adapter can map any Identities column without a second call.
/// </summary>
public class IdentityQueryItem
{
    public Guid Id { get; set; }
    /// <summary>Deterministic key field this row is keyed on (employeeId|userPrincipalName|username|email).</summary>
    public string KeyField { get; set; } = string.Empty;
    /// <summary>Value of <see cref="KeyField"/> for this row (the natural idempotency key).</summary>
    public string? KeyValue { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? PrimaryEmail { get; set; }
    public string? UserPrincipalName { get; set; }
    public string? Username { get; set; }
    public string? EmployeeId { get; set; }
    public string? Department { get; set; }
    public string? JobTitle { get; set; }
    public string? Status { get; set; }
    public bool IsActive { get; set; }
    public DateTime ModifiedAt { get; set; }
    /// <summary>Full flattened typed-column projection (string-valued).</summary>
    public IReadOnlyDictionary<string, string?> Attributes { get; set; } =
        new Dictionary<string, string?>();
}

/// <summary>
/// Request body for <c>POST /api/identities/bulk</c>. <c>BatchId</c> is the
/// caller-supplied idempotency key (Conduit run id), echoed back in the response.
/// </summary>
public class IdentityBulkUpsertRequest
{
    public Guid BatchId { get; set; }

    /// <summary>
    /// Which deterministic column each item is matched on. One of:
    /// <c>employeeId</c> (default), <c>userPrincipalName</c>, <c>username</c>,
    /// <c>email</c>. This is an EXACT equality match on a typed column — NOT
    /// fuzzy correlation. The chosen column is validated against an allow-list
    /// so a caller can never inject an arbitrary column name into the SQL.
    /// </summary>
    public string KeyField { get; set; } = "employeeId";

    public IReadOnlyList<IdentityBulkUpsertItem> Items { get; set; } = Array.Empty<IdentityBulkUpsertItem>();
}

public class IdentityBulkUpsertItem
{
    /// <summary>
    /// The value of the batch's <see cref="IdentityBulkUpsertRequest.KeyField"/>
    /// for this row — the deterministic key used to find an existing Identity.
    /// Required and non-empty.
    /// </summary>
    public string KeyValue { get; set; } = string.Empty;

    /// <summary>
    /// Typed-column writes for this row. Keys are Identities column names (from
    /// the server-side allow-list); unknown keys are ignored. The key column
    /// itself is always written on insert (from <see cref="KeyValue"/>) so the
    /// row stays addressable.
    /// </summary>
    public IReadOnlyDictionary<string, string?> Attributes { get; set; } =
        new Dictionary<string, string?>();

    /// <summary>
    /// Tag NAMES to apply to this identity (e.g. ["Contractor"]). ASSIGN-EXISTING-ONLY:
    /// IC resolves each name to an EXISTING <c>Tags</c> row (case-insensitive) and
    /// upserts an <c>IdentityTags</c> row (IsInherited=1). Unknown names are SKIPPED and
    /// logged — IC NEVER creates a Tag from caller input. Null/empty applies no tags
    /// (and never removes existing tags — this is additive/idempotent only). Mirrors
    /// <c>BulkUpsertItem.Tags</c> on the Objects path (Phase 2 tag carry-through).
    /// </summary>
    public string[]? Tags { get; set; }
}

public class IdentityBulkUpsertResponse
{
    public Guid BatchId { get; set; }
    public IReadOnlyList<IdentityBulkUpsertResult> Results { get; set; } = Array.Empty<IdentityBulkUpsertResult>();
}

public class IdentityBulkUpsertResult
{
    /// <summary>The deterministic key value this result is for.</summary>
    public string KeyValue { get; set; } = string.Empty;
    /// <summary>One of: Created, Updated, Skipped, Failed.</summary>
    public string Outcome { get; set; } = string.Empty;
    public string? ErrorMessage { get; set; }
}
