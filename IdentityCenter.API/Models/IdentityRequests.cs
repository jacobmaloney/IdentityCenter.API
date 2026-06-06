namespace IdentityCenter.API.Models;

// ── Identities API request DTOs (Prompt 11 Part 2) ───────────────────────────

public class CreateIdentityRequest
{
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? Department { get; set; }
    public string? JobTitle { get; set; }
    public string? EmployeeId { get; set; }
    public Guid? ManagerId { get; set; }
    public DateTime? StartDate { get; set; }
}

/// <summary>
/// Partial-update payload. Any property left null is left unchanged on the
/// underlying row. The response echoes back the field names that were actually
/// updated.
/// </summary>
public class UpdateIdentityRequest
{
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? Email { get; set; }
    public string? Department { get; set; }
    public string? JobTitle { get; set; }
    public string? Status { get; set; }
    public Guid? ManagerId { get; set; }
    public string? MobilePhone { get; set; }
    public string? Office { get; set; }
}

public class DeactivateIdentityRequest
{
    public DateTime? EffectiveDate { get; set; }
    public string? Reason { get; set; }
}

// ── Phase 7 person-aware additions ──────────────────────────────────────────
//
// Conduit's workflow-step engine calls these endpoints when an IC tenant sits
// on the sink side of a Sync Project and an operator wires a PersonMatch /
// PersonCreate / AssignManager / AssignGroupOwner step into the workflow tree.
//
// Match is a probe: Conduit hands over the candidate keys it has for an inbound
// directory object; IC tells it whether a person already exists. Create is
// next, only invoked on misses, and writes a new Identities row. Manager /
// owner patches reuse the lookup-by-external-id helper so Conduit can refer to
// the identity by its directory-side SourceUniqueId without learning IC's GUIDs.

public class MatchIdentityRequest
{
    /// <summary>
    /// Sink-agnostic context — the upstream SystemType that produced the
    /// candidate (e.g. "ActiveDirectory"). Optional; helps the match key
    /// disambiguate if two upstream systems issue overlapping employeeIds.
    /// </summary>
    public string? Source { get; set; }

    /// <summary>The source-side stable id (e.g. AD objectGUID, Entra id).</summary>
    public string? SourceUniqueId { get; set; }

    /// <summary>Bag of candidate keys IC will try in order of strength.</summary>
    public MatchIdentityCandidateKeys? CandidateKeys { get; set; }
}

public class MatchIdentityCandidateKeys
{
    public string? Upn { get; set; }
    public string? Email { get; set; }
    public string? EmployeeId { get; set; }
    public string? Username { get; set; }
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
}

public class MatchIdentityResponse
{
    public bool Matched { get; set; }
    public Guid? IdentityId { get; set; }
    /// <summary>Short label of the key that scored — "upn" / "email" / "employeeId" / "name".</summary>
    public string? MatchedBy { get; set; }
    public double Confidence { get; set; }
}

public class AssignManagerRequest
{
    /// <summary>Identifier of the manager. Either an IC Identity GUID or a UPN/email.</summary>
    public Guid? ManagerIdentityId { get; set; }
    /// <summary>Free-form external id. UPN, email, or sAMAccountName.</summary>
    public string? ManagerExternalId { get; set; }
}

public class AssignGroupOwnerRequest
{
    public Guid? OwnerIdentityId { get; set; }
    public string? OwnerExternalId { get; set; }
}
