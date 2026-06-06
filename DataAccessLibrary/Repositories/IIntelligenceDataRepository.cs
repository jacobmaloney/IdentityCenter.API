using DataAccessLibrary.Models;

namespace DataAccessLibrary.Repositories;

public interface IIntelligenceDataRepository
{
    // Admin analysis
    Task<List<AdminAccountRecord>> GetAdminAccountsAsync();
    Task<AdminStatsRecord> GetAdminStatsAsync();

    // Inactivity analysis
    Task<List<InactiveAccountRecord>> GetInactiveAccountsAsync(int inactiveDaysThreshold);
    Task<InactivityStatsRecord> GetInactivityStatsAsync();

    // Group analysis
    Task<List<GroupInfoRecord>> GetAllGroupsWithMetadataAsync(CancellationToken cancellationToken = default);
    Task<int> GetOrphanedMemberCountAsync(Guid groupId, CancellationToken cancellationToken = default);
    Task<List<RedundantGroupRecord>> GetRedundantGroupsAsync(CancellationToken cancellationToken = default);
    Task<GroupStatsRecord> GetGroupStatsAsync(CancellationToken cancellationToken = default);

    // Organization analysis
    Task<List<OrgUserRecord>> GetAllUsersWithOrgDataAsync(CancellationToken cancellationToken = default);
    Task<List<CircularChainRecord>> GetCircularManagerChainsAsync(CancellationToken cancellationToken = default);
    Task<OrgStatsRecord> GetOrganizationStatsAsync(CancellationToken cancellationToken = default);
    Task<double> GetAverageDirectReportsAsync(CancellationToken cancellationToken = default);
    Task<List<ManagerHierarchyRecord>> GetManagerHierarchyAsync(CancellationToken cancellationToken = default);

    // Bulk issue detection - People/Identity
    Task<List<PersonWithIssueRecord>> GetPeopleWithoutManagersAsync(CancellationToken cancellationToken = default);
    Task<List<PersonWithIssueRecord>> GetPeopleWithDisabledManagersAsync(CancellationToken cancellationToken = default);
    Task<List<PersonWithIssueRecord>> GetPeopleWithMissingDepartmentAsync(CancellationToken cancellationToken = default);
    Task<List<PersonWithIssueRecord>> GetPeopleWithMissingJobTitleAsync(CancellationToken cancellationToken = default);
    Task<List<PersonWithIssueRecord>> GetPeopleNeverLoggedInAsync(CancellationToken cancellationToken = default);

    // Bulk issue detection - Groups
    Task<List<GroupWithIssueRecord>> GetGroupsWithoutOwnersAsync(CancellationToken cancellationToken = default);
    Task<List<GroupWithIssueRecord>> GetEmptyGroupsAsync(CancellationToken cancellationToken = default);
    Task<List<GroupWithIssueRecord>> GetSingleMemberGroupsAsync(CancellationToken cancellationToken = default);
    Task<List<GroupWithIssueRecord>> GetStaleGroupsAsync(int staleDaysThreshold = 365, CancellationToken cancellationToken = default);
    Task<List<GroupWithIssueRecord>> GetGroupsWithOrphanedMembersAsync(CancellationToken cancellationToken = default);

    // Bulk issue detection - Accounts/Objects (optional take parameter for pagination)
    Task<List<ObjectWithIssueRecord>> GetAccountsWithPasswordNeverExpiresAsync(int? take = null, CancellationToken cancellationToken = default);
    Task<List<ObjectWithIssueRecord>> GetKerberoastableAccountsAsync(CancellationToken cancellationToken = default);
    Task<List<ObjectWithIssueRecord>> GetUnconstrainedDelegationAccountsAsync(CancellationToken cancellationToken = default);
    Task<List<ObjectWithIssueRecord>> GetPrivilegedAccountsWithoutSensitiveFlagAsync(CancellationToken cancellationToken = default);
    Task<List<ObjectWithIssueRecord>> GetOrphanedAccountsAsync(CancellationToken cancellationToken = default);
    Task<List<ObjectWithIssueRecord>> GetInactiveAccounts90DaysAsync(int? take = null, CancellationToken cancellationToken = default);
    Task<List<ObjectWithIssueRecord>> GetInactiveAccounts365DaysAsync(CancellationToken cancellationToken = default);

    // ========== COUNT-ONLY QUERIES (Fast dashboard loading) ==========
    // These return only counts without fetching full records or suggestions

    // People counts
    Task<int> GetPeopleWithoutManagersCountAsync(CancellationToken cancellationToken = default);
    Task<int> GetPeopleWithDisabledManagersCountAsync(CancellationToken cancellationToken = default);
    Task<int> GetCircularManagerChainsCountAsync(CancellationToken cancellationToken = default);
    Task<int> GetPeopleWithMissingDepartmentCountAsync(CancellationToken cancellationToken = default);
    Task<int> GetPeopleWithMissingJobTitleCountAsync(CancellationToken cancellationToken = default);
    Task<int> GetPeopleNeverLoggedInCountAsync(CancellationToken cancellationToken = default);

    // Group counts
    Task<int> GetGroupsWithoutOwnersCountAsync(CancellationToken cancellationToken = default);
    Task<int> GetEmptyGroupsCountAsync(CancellationToken cancellationToken = default);
    Task<int> GetSingleMemberGroupsCountAsync(CancellationToken cancellationToken = default);
    Task<int> GetStaleGroupsCountAsync(int staleDaysThreshold = 365, CancellationToken cancellationToken = default);
    Task<int> GetGroupsWithOrphanedMembersCountAsync(CancellationToken cancellationToken = default);
    Task<int> GetRedundantGroupsCountAsync(CancellationToken cancellationToken = default);

    // Account counts
    Task<int> GetAccountsWithPasswordNeverExpiresCountAsync(CancellationToken cancellationToken = default);
    Task<int> GetKerberoastableAccountsCountAsync(CancellationToken cancellationToken = default);
    Task<int> GetUnconstrainedDelegationAccountsCountAsync(CancellationToken cancellationToken = default);
    Task<int> GetPrivilegedAccountsWithoutSensitiveFlagCountAsync(CancellationToken cancellationToken = default);
    Task<int> GetOrphanedAccountsCountAsync(CancellationToken cancellationToken = default);
    Task<int> GetInactiveAccounts90DaysCountAsync(CancellationToken cancellationToken = default);
    Task<int> GetInactiveAccounts365DaysCountAsync(CancellationToken cancellationToken = default);

    // Bulk fix operations
    Task<int> AssignManagerAsync(Guid userId, Guid managerId, CancellationToken cancellationToken = default);
    Task<int> AssignGroupOwnerAsync(Guid groupId, Guid ownerId, CancellationToken cancellationToken = default);
    Task<int> AssignObjectManagerAsync(Guid objectId, Guid managerObjectId, CancellationToken cancellationToken = default);
    Task<int> SyncManagerToAuthoritativeObjectAsync(Guid personId, Guid managerIdentityId, CancellationToken cancellationToken = default);

    // Rollback operations (nullable values for clearing assignments)
    Task<int> AssignManagerAsync(Guid userId, Guid? managerId, CancellationToken cancellationToken = default);
    Task<int> AssignGroupOwnerAsync(Guid groupId, Guid? ownerId, CancellationToken cancellationToken = default);
    Task<int> SetObjectEnabledAsync(Guid objectId, bool isEnabled, CancellationToken cancellationToken = default);

    // Get current value for change tracking
    Task<Guid?> GetCurrentManagerAsync(Guid userId, CancellationToken cancellationToken = default);
    Task<Guid?> GetCurrentGroupOwnerAsync(Guid groupId, CancellationToken cancellationToken = default);

    // Dashboard insight counts (returns all counts in single method for performance)
    Task<DashboardInsightCounts> GetDashboardInsightCountsAsync(CancellationToken cancellationToken = default);

    // Bulk actions for intelligence center
    Task<int> BulkDisableStaleAccountsAsync(int inactiveDays = 90, CancellationToken cancellationToken = default);
    Task<int> BulkDeleteEmptyGroupsAsync(CancellationToken cancellationToken = default);
    Task<int> BulkEnforcePasswordExpiryAsync(CancellationToken cancellationToken = default);

    // Additional counts for intelligence center
    Task<int> GetEnabledSyncProjectCountAsync(CancellationToken cancellationToken = default);

    // Identity display info for ML prediction results
    Task<PersonWithIssueRecord?> GetIdentityDisplayInfoAsync(Guid identityId, CancellationToken cancellationToken = default);
    Task<Dictionary<Guid, PersonWithIssueRecord>> GetIdentitiesDisplayInfoBatchAsync(List<Guid> identityIds, CancellationToken cancellationToken = default);

    // ChatHub TopIssues: entity-level rows for actionable cards. The optional ScopeFilter
    // restricts results to a delegated user's scope. Predicates are server-built from the
    // typed fields on ScopeFilter with parameterized values — no caller-supplied SQL is
    // appended to the query. Pass null when no scope filter is required.
    Task<List<TopStaleAccountRow>> GetTopStaleAccountsAsync(
        int n, int days, ScopeFilter? scope = null, CancellationToken cancellationToken = default);
    Task<List<TopOwnerlessGroupRow>> GetTopOwnerlessGroupsAsync(
        int n, ScopeFilter? scope = null, CancellationToken cancellationToken = default);
}

/// <summary>
/// Structured scope filter for entity-level queries. Each non-null field becomes a
/// parameterized predicate against a known column — never raw SQL. Mirrors the
/// scope dimensions used by IAdminRepository.GetObjectsAsync / AdvancedSearchObjectsAsync.
/// </summary>
public record ScopeFilter(
    Guid? ConnectionId = null,
    string? ObjectClass = null,
    string? OuPath = null,
    string? Department = null,
    Guid? TagId = null);

public record TopStaleAccountRow(
    Guid ObjectId,
    string DisplayName,
    string? Email,
    int DaysSinceLastLogin,
    string? ConnectionName);

public record TopOwnerlessGroupRow(
    Guid ObjectId,
    string DisplayName,
    int MemberCount,
    string? ConnectionName);

public class DashboardInsightCounts
{
    public int FailedSyncsLast24h { get; set; }
    public int StaleSyncProjects { get; set; }
    public int StaleAccounts90Days { get; set; }
    public int EmptyGroups { get; set; }
    public int UsersWithoutManagers { get; set; }
    public int PasswordNeverExpires { get; set; }
    public int ActiveComplianceViolations { get; set; }
    public int CriticalComplianceViolations { get; set; }
    public int OverdueReviews { get; set; }
    public int RecentErrors24h { get; set; }
    public int GroupsWithoutOwners { get; set; }
    public int StaleServiceAccounts90Days { get; set; }
    public int StalePrivilegedAccounts30Days { get; set; }
    public int PrivilegedAccountsWithoutManager { get; set; }
    public int LockedOutAccounts { get; set; }
    public int WastedLicenseCount { get; set; }
    public int MLDisablementCandidates { get; set; }
    public int MLPeerOutliers { get; set; }
    public decimal EstimatedLicenseWasteMonthly { get; set; }
}
