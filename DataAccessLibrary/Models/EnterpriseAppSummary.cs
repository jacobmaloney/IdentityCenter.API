namespace DataAccessLibrary.Models;

/// <summary>
/// Aggregate summary of enterprise apps for the License Center overview card.
/// Sourced from EnterpriseApps + SignInLogs + Objects (oAuth2PermissionGrant scopes).
/// </summary>
public record EnterpriseAppSummary(
    int TotalApps,
    int NewThisWeek,
    int DormantCount,
    int HighPermissionCount,
    IReadOnlyList<EnterpriseAppRow> TopByVolume,
    IReadOnlyList<EnterpriseAppRow> TopDormant,
    IReadOnlyList<EnterpriseAppRow> HighPermission)
{
    public static EnterpriseAppSummary Empty { get; } = new(
        TotalApps: 0,
        NewThisWeek: 0,
        DormantCount: 0,
        HighPermissionCount: 0,
        TopByVolume: Array.Empty<EnterpriseAppRow>(),
        TopDormant: Array.Empty<EnterpriseAppRow>(),
        HighPermission: Array.Empty<EnterpriseAppRow>());
}

/// <summary>
/// Single enterprise-app row for overview lists. PublisherDomain is reserved for
/// future enrichment — EnterpriseApps schema doesn't track it today, so it's null.
/// </summary>
public record EnterpriseAppRow(
    Guid Id,
    string DisplayName,
    string? PublisherDomain,
    int SignInCount30d,
    DateTime? LastSignInAt,
    bool HasHighPermission);
