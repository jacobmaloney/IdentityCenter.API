namespace DataAccessLibrary.Models;

// DTOs for Intelligence analyzer queries

public class AdminAccountRecord
{
    public Guid Id { get; set; }
    public Guid ObjectGuid { get; set; }
    public string? DisplayName { get; set; }
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? Email { get; set; }
    public string? Username { get; set; }
    public DateTime? LastLogon { get; set; }
    public bool IsActive { get; set; }
    public string? ObjectClass { get; set; }
    public int DaysSinceLastLogin { get; set; }
    public string? DirectorySource { get; set; }
}

public class AdminStatsRecord
{
    public int TotalAdmins { get; set; }
    public int InactiveAdmins { get; set; }
    public int HighRiskAdmins { get; set; }
    public int CriticalAdmins { get; set; }
}

public class InactiveAccountRecord
{
    public Guid Id { get; set; }
    public Guid ObjectGuid { get; set; }
    public string? DisplayName { get; set; }
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? Email { get; set; }
    public string? Username { get; set; }
    public DateTime? LastLogon { get; set; }
    public bool IsActive { get; set; }
    public string? ObjectClass { get; set; }
    public int DaysSinceLastLogin { get; set; }
    public string? DirectorySource { get; set; }
}

public class InactivityStatsRecord
{
    public int Inactive90Days { get; set; }
    public int Inactive180Days { get; set; }
    public int Inactive365Days { get; set; }
    public int NeverLoggedIn { get; set; }
    public int TotalActiveUsers { get; set; }
}

public class GroupInfoRecord
{
    public Guid GroupId { get; set; }
    public string? GroupName { get; set; }
    public string? Description { get; set; }
    public string? DistinguishedName { get; set; }
    public int MemberCount { get; set; }
    public DateTime? CreatedDate { get; set; }
    public DateTime? ModifiedDate { get; set; }
    public string? ManagedBy { get; set; }
    public string? DirectorySource { get; set; }
}

public class GroupStatsRecord
{
    public int TotalGroups { get; set; }
    public int EmptyGroups { get; set; }
    public int SingleMemberGroups { get; set; }
    public int StaleGroups { get; set; }
    public int GroupsWithNoManager { get; set; }
}

public class RedundantGroupRecord
{
    public Guid GroupId { get; set; }
    public string? GroupName { get; set; }
    public int MemberCount { get; set; }
    public string? RedundantWith { get; set; }
}

public class OrgUserRecord
{
    public Guid UserId { get; set; }
    public Guid ObjectId { get; set; }
    public string? UserName { get; set; }
    public string? DisplayName { get; set; }
    public string? Email { get; set; }
    public string? Department { get; set; }
    public string? Title { get; set; }
    public bool IsActive { get; set; }
    public string? DirectorySource { get; set; }
    public Guid? ManagerId { get; set; }
    public string? ManagerName { get; set; }
    public bool? ManagerIsActive { get; set; }
    public int DirectReportCount { get; set; }
}

public class CircularChainRecord
{
    public Guid UserId { get; set; }
    public string? DisplayName { get; set; }
    public Guid? ManagerId { get; set; }
    public string? ManagerName { get; set; }
    public int ChainLength { get; set; }
}

public class OrgStatsRecord
{
    public int TotalUsers { get; set; }
    public int UsersWithManager { get; set; }
    public int UsersWithoutManager { get; set; }
    public int UsersWithDepartment { get; set; }
    public int UsersWithTitle { get; set; }
    public int TotalManagers { get; set; }
    public double AverageDirectReports { get; set; }
}

public class ManagerHierarchyRecord
{
    public Guid ManagerId { get; set; }
    public string? ManagerName { get; set; }
    public string? ManagerDisplayName { get; set; }
    public string? Department { get; set; }
    public int DirectReportCount { get; set; }
}

// Bulk Insight Detection Records

public class PersonWithIssueRecord
{
    public Guid Id { get; set; }
    public string? DisplayName { get; set; }
    public string? Email { get; set; }
    public string? Department { get; set; }
    public string? JobTitle { get; set; }
    public Guid? SuggestedManagerId { get; set; }
    public string? SuggestedManagerName { get; set; }
    public string? IssueDetail { get; set; }
}

public class GroupWithIssueRecord
{
    public Guid Id { get; set; }
    public string? Name { get; set; }
    public int MemberCount { get; set; }
    public DateTime? LastModified { get; set; }
    public Guid? SuggestedOwnerId { get; set; }
    public string? SuggestedOwnerName { get; set; }
    public string? IssueDetail { get; set; }
}

public class ObjectWithIssueRecord
{
    public Guid Id { get; set; }
    public string? DisplayName { get; set; }
    public string? Username { get; set; }
    public string? SourceType { get; set; }
    public string? IssueDetail { get; set; }
    public Guid? LinkedIdentityId { get; set; }
}

// Ownership Suggestion Engine models

public class GroupMemberOrgData
{
    public Guid ObjectId { get; set; }
    public string? DisplayName { get; set; }
    public Guid? ManagerObjectId { get; set; }
    public string? Department { get; set; }
    public int DirectReportCount { get; set; }
}

public class OrphanedGroupSummary
{
    public Guid GroupId { get; set; }
    public string? GroupName { get; set; }
    public int MemberCount { get; set; }
}

public class OwnershipSuggestion
{
    public Guid GroupId { get; set; }
    public string? GroupName { get; set; }
    public int MemberCount { get; set; }
    public Guid? SuggestedOwnerId { get; set; }
    public string? SuggestedOwnerName { get; set; }
    public string Confidence { get; set; } = "Low";
    public string Reason { get; set; } = string.Empty;
    public double DominancePercent { get; set; }
}

// Shadow Admin Detection models

public class ShadowAdminRecord
{
    public Guid ObjectId { get; set; }
    public string? DisplayName { get; set; }
    public string? Username { get; set; }
    public string? Email { get; set; }
    public Guid AdminGroupId { get; set; }
    public string? AdminGroupName { get; set; }
    public int NestingDepth { get; set; }
    public string? AccessPath { get; set; }
    public string? Department { get; set; }
}

// Impact Projection models

public class ImpactProjection
{
    public string IssueId { get; set; } = string.Empty;
    public int CurrentHealthScore { get; set; }
    public int ProjectedHealthScore { get; set; }
    public int HealthScoreDelta { get; set; }
    public string ImpactStatement { get; set; } = string.Empty;
}
