namespace DataAccessLibrary.Models;

// ─────────────────────────────────────────────────────────────────────────────
// Table-mapped entities for cloud activity monitoring
// Sign-in logs, M365 usage reports, app role assignments, enterprise apps
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Individual sign-in event from Entra ID audit logs. One row per sign-in attempt.
/// </summary>
public class SignInLog
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>FK to Objects.Id — the identity that performed this sign-in.</summary>
    public Guid ObjectId { get; set; }

    public Guid SourceConnectionId { get; set; }

    /// <summary>Entra ID sign-in record ID.</summary>
    public string? SignInId { get; set; }

    public DateTime SignInDateTime { get; set; }
    public string? AppDisplayName { get; set; }
    public string? AppId { get; set; }
    public string? ClientAppUsed { get; set; }

    /// <summary>JSON-serialized device detail from Graph API.</summary>
    public string? DeviceDetail { get; set; }

    public string? IpAddress { get; set; }

    /// <summary>JSON-serialized location from Graph API.</summary>
    public string? Location { get; set; }

    /// <summary>"Success" or "Failure" derived from Status.ErrorCode.</summary>
    public string? Status { get; set; }

    public int? ErrorCode { get; set; }

    /// <summary>Risk level during sign-in (e.g. "none", "low", "medium", "high").</summary>
    public string? RiskLevel { get; set; }

    /// <summary>Risk state (e.g. "none", "confirmedSafe", "remediated", "atRisk").</summary>
    public string? RiskState { get; set; }

    /// <summary>Conditional access evaluation result (e.g. "success", "failure", "notApplied").</summary>
    public string? ConditionalAccessStatus { get; set; }

    public bool IsInteractive { get; set; }
    public string? ResourceDisplayName { get; set; }
    public string? ResourceId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // ── Populated by service layer, not Dapper auto-map ──
    public string? UserDisplayName { get; set; }
    public string? Username { get; set; }

    /// <summary>
    /// Entra user ID (string) from the sign-in event. Used during sync to resolve
    /// ObjectId via SourceUniqueId lookup. Not persisted to database.
    /// </summary>
    public string? EntraUserId { get; set; }
}

/// <summary>
/// Daily sign-in activity summary per user per app. Aggregated from SignInLog records.
/// </summary>
public class SignInSummary
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ObjectId { get; set; }
    public Guid SourceConnectionId { get; set; }
    public string AppDisplayName { get; set; } = string.Empty;
    public DateTime SummaryDate { get; set; }
    public int SuccessCount { get; set; }
    public int FailureCount { get; set; }
    public int InteractiveCount { get; set; }
    public int NonInteractiveCount { get; set; }
    public int UniqueLocations { get; set; }
}

/// <summary>
/// M365 per-user usage detail from the getOffice365ActiveUserDetail report.
/// One row per user per sync, showing license ownership and last activity dates.
/// </summary>
public class M365UsageReport
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ObjectId { get; set; }
    public Guid SourceConnectionId { get; set; }

    public DateTime ReportRefreshDate { get; set; }
    public string? UserPrincipalName { get; set; }
    public string? DisplayName { get; set; }

    // License flags
    public bool HasExchangeLicense { get; set; }
    public bool HasOneDriveLicense { get; set; }
    public bool HasSharePointLicense { get; set; }
    public bool HasTeamsLicense { get; set; }
    public bool HasYammerLicense { get; set; }

    // Last activity dates per workload
    public DateTime? ExchangeLastActivityDate { get; set; }
    public DateTime? OneDriveLastActivityDate { get; set; }
    public DateTime? SharePointLastActivityDate { get; set; }
    public DateTime? TeamsLastActivityDate { get; set; }
    public DateTime? YammerLastActivityDate { get; set; }

    // Exchange usage metrics
    public int? ExchangeMailSent { get; set; }
    public int? ExchangeMailReceived { get; set; }

    // OneDrive usage metrics
    public int? OneDriveFilesViewed { get; set; }
    public int? OneDriveFilesSynced { get; set; }

    // Storage bytes (from getOneDriveUsageAccountDetail / getMailboxUsageDetail)
    public long? OneDriveStorageUsedBytes { get; set; }
    public long? OneDriveStorageAllocatedBytes { get; set; }
    public long? MailboxStorageUsedBytes { get; set; }
    public long? MailboxQuotaBytes { get; set; }

    // SharePoint usage metrics
    public int? SharePointFilesViewed { get; set; }
    public int? SharePointFilesShared { get; set; }

    // Teams usage metrics
    public int? TeamsChatMessages { get; set; }
    public int? TeamsCallCount { get; set; }
    public int? TeamsMeetingCount { get; set; }

    /// <summary>Semicolon-delimited list of assigned product names.</summary>
    public string? AssignedProducts { get; set; }

    public DateTime LastSyncedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// UPN from the report CSV. Used during sync to resolve ObjectId via Objects table.
    /// Not persisted to database.
    /// </summary>
    public string? EntraUserPrincipalName { get; set; }
}

/// <summary>
/// App role assignment linking a principal (user/group/SP) to a resource (enterprise app).
/// Sourced from ServicePrincipal.AppRoleAssignedTo in Graph API.
/// </summary>
public class AppRoleAssignment
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid SourceConnectionId { get; set; }

    /// <summary>Entra ID appRoleAssignment ID.</summary>
    public string? AppRoleAssignmentId { get; set; }

    /// <summary>Entra object ID of the principal (user/group/SP).</summary>
    public Guid? PrincipalId { get; set; }

    /// <summary>FK to Objects.Id for the principal. Resolved during sync.</summary>
    public Guid? PrincipalObjectId { get; set; }

    /// <summary>"User", "Group", or "ServicePrincipal".</summary>
    public string PrincipalType { get; set; } = string.Empty;

    public string? PrincipalDisplayName { get; set; }

    /// <summary>Entra object ID of the resource service principal.</summary>
    public Guid? ResourceId { get; set; }

    /// <summary>FK to Objects.Id for the resource. Resolved during sync.</summary>
    public Guid? ResourceObjectId { get; set; }

    public string ResourceDisplayName { get; set; } = string.Empty;

    /// <summary>The specific app role GUID. Guid.Empty means default access.</summary>
    public Guid? AppRoleId { get; set; }

    public string? AppRoleName { get; set; }
    public DateTime? CreatedDateTime { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime LastSyncedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Enterprise application (service principal) with aggregated assignment counts.
/// </summary>
public class EnterpriseApp
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid SourceConnectionId { get; set; }

    /// <summary>Entra service principal ID.</summary>
    public string ServicePrincipalId { get; set; } = string.Empty;

    /// <summary>FK to Objects.Id if this SP was synced as an object. Null otherwise.</summary>
    public Guid? ObjectId { get; set; }

    public string? AppId { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public string? ServicePrincipalType { get; set; }
    public string? SignInAudience { get; set; }
    public string? Homepage { get; set; }
    public string? LogoUrl { get; set; }

    public int TotalAssignments { get; set; }
    public int UserAssignments { get; set; }
    public int GroupAssignments { get; set; }

    public bool IsEnabled { get; set; } = true;
    public DateTime LastSyncedAt { get; set; } = DateTime.UtcNow;
}

// ─────────────────────────────────────────────────────────────────────────────
// DTOs / view models (no table backing)
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Aggregated sign-in dashboard summary for a configurable time period.
/// </summary>
public class SignInDashboardSummary
{
    public int TotalSignIns { get; set; }
    public int SuccessfulSignIns { get; set; }
    public int FailedSignIns { get; set; }
    public int UniqueUsers { get; set; }
    public int UniqueApps { get; set; }
    public List<AppSignInCount> TopApps { get; set; } = new();
    public int RiskySignIns { get; set; }

    /// <summary>Period in days this summary covers (e.g. 7, 30).</summary>
    public int PeriodDays { get; set; }
}

/// <summary>
/// Sign-in count for a single application, used in dashboard top-apps lists.
/// </summary>
public class AppSignInCount
{
    public string AppDisplayName { get; set; } = string.Empty;
    public int Count { get; set; }
}

/// <summary>
/// Aggregated M365 usage summary across all users for dashboard display.
/// </summary>
public class M365UsageSummary
{
    public int TotalUsers { get; set; }
    public int ActiveExchangeUsers { get; set; }
    public int ActiveTeamsUsers { get; set; }
    public int ActiveSharePointUsers { get; set; }
    public int ActiveOneDriveUsers { get; set; }
    public DateTime? ReportDate { get; set; }
}

/// <summary>
/// Aggregated enterprise app access summary for dashboard display.
/// </summary>
public class AppAccessSummary
{
    public int TotalApps { get; set; }
    public int TotalAssignments { get; set; }
    public List<EnterpriseApp> TopApps { get; set; } = new();
}
