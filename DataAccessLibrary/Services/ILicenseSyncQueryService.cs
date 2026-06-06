using DataAccessLibrary.Models;

namespace DataAccessLibrary.Services;

/// <summary>
/// Abstraction for querying license data from an identity provider (e.g., Entra ID).
/// Implementation lives in ConnectionService; interface here so SyncProjectOrchestrator can depend on it.
/// </summary>
public interface ILicenseSyncQueryService
{
    /// <summary>
    /// Queries tenant-level license pools (subscribedSkus) from Graph API.
    /// Returns LicensePool + associated LicenseServicePlan objects ready for DB upsert.
    /// </summary>
    Task<List<(LicensePool Pool, List<LicenseServicePlan> Plans)>> QueryLicensePoolsAsync(
        Guid sourceConnectionId,
        string tenantId,
        string clientId,
        string clientSecret,
        CancellationToken ct = default);

    /// <summary>
    /// Queries all users' assigned licenses in bulk via $select=id,assignedLicenses,licenseAssignmentStates.
    /// Returns (Entra userId string, list of per-SKU assignment info).
    /// </summary>
    Task<List<UserLicenseInfo>> QueryUserLicenseAssignmentsAsync(
        string tenantId,
        string clientId,
        string clientSecret,
        CancellationToken ct = default);

    /// <summary>
    /// Queries Entra ID sign-in logs since the specified date.
    /// Requires AuditLog.Read.All and Directory.Read.All permissions.
    /// ObjectId on returned records is Guid.Empty; callers must resolve
    /// EntraUserId to Objects.Id via SourceUniqueId lookup.
    /// </summary>
    Task<List<DataAccessLibrary.Models.SignInLog>> QuerySignInLogsAsync(
        Guid sourceConnectionId,
        string tenantId,
        string clientId,
        string clientSecret,
        DateTime since,
        CancellationToken ct = default);

    /// <summary>
    /// Queries the M365 active user detail report for the last 30 days.
    /// Requires Reports.Read.All permission.
    /// ObjectId on returned records is Guid.Empty; callers must resolve
    /// EntraUserPrincipalName to Objects.Id via UPN lookup.
    /// </summary>
    Task<List<DataAccessLibrary.Models.M365UsageReport>> QueryM365UsageReportAsync(
        Guid sourceConnectionId,
        string tenantId,
        string clientId,
        string clientSecret,
        CancellationToken ct = default);

    /// <summary>
    /// Queries all service principals and their app role assignments from Entra ID.
    /// Returns both the assignment list and the enterprise app records built from SPs.
    /// Requires Application.Read.All or Directory.Read.All permission.
    /// </summary>
    Task<(List<DataAccessLibrary.Models.AppRoleAssignment> Assignments, List<DataAccessLibrary.Models.EnterpriseApp> Apps)> QueryAppRoleAssignmentsAsync(
        Guid sourceConnectionId,
        string tenantId,
        string clientId,
        string clientSecret,
        CancellationToken ct = default);
}

/// <summary>
/// Lightweight DTO for a single user's license assignments from Graph API.
/// </summary>
public class UserLicenseInfo
{
    public string EntraUserId { get; set; } = string.Empty;
    public List<UserSkuAssignment> Assignments { get; set; } = new();
}

public class UserSkuAssignment
{
    public string SkuId { get; set; } = string.Empty;
    /// <summary>Direct, Group, or Unknown</summary>
    public string AssignmentSource { get; set; } = "Direct";
    /// <summary>Entra Object ID of the group if group-based assignment</summary>
    public string? SourceGroupId { get; set; }
}
