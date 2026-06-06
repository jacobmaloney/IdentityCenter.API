namespace DataAccessLibrary.Models;

/// <summary>
/// Flat row representing a single group membership for an identity, projected
/// across all directory accounts (Objects) linked to that identity. Used by
/// the unified Access tab on Identities and Objects detail panels.
/// </summary>
public class AccessGroupRow
{
    public Guid GroupId { get; set; }
    public string? GroupName { get; set; }
    public string? GroupSource { get; set; }       // ActiveDirectory, EntraID, Okta, etc.
    public string? GroupType { get; set; }         // Security / Distribution / Role / null
    public bool IsDirect { get; set; }             // false => nested via parent
    public bool IsPrimary { get; set; }            // AD primary group flag
    public string? ParentGroupName { get; set; }   // populated when IsDirect = false
    public string? OwningObjectName { get; set; }  // which linked Object the membership is via
    public bool IsPrivileged { get; set; }         // computed from name/keywords
}

/// <summary>
/// Per-identity license entitlement row, denormalized for the Access tab.
/// </summary>
public class AccessLicenseRow
{
    public Guid AssignmentId { get; set; }
    public Guid PoolId { get; set; }
    public string? PoolName { get; set; }
    public string? SkuName { get; set; }
    public DateTime? AssignedAt { get; set; }
    public DateTime? LastUsedAt { get; set; }
    public bool IsActive { get; set; }
    public decimal? CostPerUnitMonthly { get; set; }
    public string? AssignmentSource { get; set; }   // Direct / Group / Policy

    /// <summary>True when LastUsedAt is null or older than 90 days.</summary>
    public bool IsInactive => !LastUsedAt.HasValue || LastUsedAt.Value < DateTime.UtcNow.AddDays(-90);
}

/// <summary>
/// Aggregated access summary for a single identity (or a single Object), used
/// to populate the Risk Summary card at the top of the Access tab.
/// </summary>
public class AccessSummary
{
    public int DirectoryGroupCount { get; set; }
    public int LicenseCount { get; set; }
    public int PrivilegedGroupCount { get; set; }
    public bool HasUnconstrainedDelegation { get; set; }
    public DateTime? LastReviewedAt { get; set; }

    public int TotalEntitlements => DirectoryGroupCount + LicenseCount;
}

/// <summary>
/// Combined payload returned by IAccessAggregationRepository for a single
/// identity. The repository fetches everything in one round-trip so the UI
/// can render the entire Access tab without further calls.
/// </summary>
public class IdentityAccessPayload
{
    public AccessSummary Summary { get; set; } = new();
    public List<AccessGroupRow> Groups { get; set; } = new();
    public List<AccessLicenseRow> Licenses { get; set; } = new();
}
