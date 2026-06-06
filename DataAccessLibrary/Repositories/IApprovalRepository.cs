using DataAccessLibrary.Models;

namespace DataAccessLibrary.Repositories;

/// <summary>
/// High-performance repository for Approval Center operations.
/// Provides unified access to both Access Review Assignments and Access Requests.
/// </summary>
public interface IApprovalRepository
{
    /// <summary>
    /// Get pending approvals for a specific approver with filtering and pagination.
    /// Performance target: 200ms for 50 items.
    /// </summary>
    /// <param name="approverId">ID of the approver (Person or User ID)</param>
    /// <param name="filter">Filter criteria for approvals</param>
    /// <param name="pagination">Pagination parameters</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Paginated list of pending approvals with total count</returns>
    Task<ApprovalInboxResult> GetPendingApprovalsAsync(
        Guid approverId,
        ApprovalFilter filter,
        ApprovalPagination pagination,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get detailed information about a specific approval.
    /// Performance target: 100ms.
    /// </summary>
    /// <param name="approvalId">ID of the approval (Assignment ID or Request ID)</param>
    /// <param name="approvalType">Type of approval: "ReviewAssignment" or "AccessRequest"</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Detailed approval information with full context</returns>
    Task<ApprovalDetails?> GetApprovalDetailsAsync(
        Guid approvalId,
        string approvalType,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Approve a review assignment or access request.
    /// Performance target: 500ms with audit trail.
    /// </summary>
    /// <param name="decision">Approval decision with justification</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if successful, false if approval already processed</returns>
    Task<ApprovalResult> ApproveAsync(
        ApprovalDecision decision,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deny a review assignment or access request.
    /// Performance target: 500ms with audit trail.
    /// </summary>
    /// <param name="decision">Denial decision with justification</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if successful, false if approval already processed</returns>
    Task<ApprovalResult> DenyAsync(
        ApprovalDecision decision,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Delegate an approval to another approver.
    /// Performance target: 300ms.
    /// </summary>
    /// <param name="approvalId">ID of the approval to delegate</param>
    /// <param name="approvalType">Type of approval</param>
    /// <param name="delegateToId">ID of person to delegate to</param>
    /// <param name="reason">Reason for delegation</param>
    /// <param name="delegatedById">ID of person delegating</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if successful, false if already processed</returns>
    Task<ApprovalResult> DelegateAsync(
        Guid approvalId,
        string approvalType,
        Guid delegateToId,
        string reason,
        Guid delegatedById,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Bulk approve multiple assignments with single justification.
    /// Performance target: 3 seconds for 10 items.
    /// </summary>
    /// <param name="approvalIds">List of approval IDs to approve</param>
    /// <param name="approvalType">Type of approvals (all must be same type)</param>
    /// <param name="justification">Justification for all approvals</param>
    /// <param name="approverId">ID of approver</param>
    /// <param name="approverName">Name of approver</param>
    /// <param name="ipAddress">IP address of approver</param>
    /// <param name="userAgent">User agent of approver</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Result with success count and any failures</returns>
    Task<BulkApprovalResult> BulkApproveAsync(
        List<Guid> approvalIds,
        string approvalType,
        string justification,
        Guid approverId,
        string approverName,
        string? ipAddress = null,
        string? userAgent = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get real-time approval statistics for dashboard.
    /// Performance target: 100ms with caching.
    /// </summary>
    /// <param name="approverId">ID of approver</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Dashboard statistics</returns>
    Task<ApprovalStats> GetApprovalStatsAsync(
        Guid approverId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get approvals that are overdue or approaching due date.
    /// Performance target: 200ms.
    /// </summary>
    /// <param name="approverId">ID of approver</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of urgent approvals</returns>
    Task<List<ApprovalInboxItem>> GetUrgentApprovalsAsync(
        Guid approverId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Invalidate cache for specific approver (call after decision submission).
    /// </summary>
    /// <param name="approverId">ID of approver</param>
    void InvalidateCache(Guid approverId);

    /// <summary>
    /// Invalidate all approval caches (call after bulk operations).
    /// </summary>
    void InvalidateAllCaches();
}

/// <summary>
/// Filter criteria for approval inbox.
/// </summary>
public class ApprovalFilter
{
    /// <summary>
    /// Filter by approval type: null (all), "ReviewAssignment", "AccessRequest"
    /// </summary>
    public string? ApprovalType { get; set; }

    /// <summary>
    /// Filter by risk level: null (all), "Critical", "High", "Medium", "Low"
    /// </summary>
    public string? RiskLevel { get; set; }

    /// <summary>
    /// Filter by campaign ID (for review assignments)
    /// </summary>
    public Guid? CampaignId { get; set; }

    /// <summary>
    /// Filter by resource type (for access requests): "Application", "Group", "Role"
    /// </summary>
    public string? ResourceType { get; set; }

    /// <summary>
    /// Only show overdue approvals
    /// </summary>
    public bool? OnlyOverdue { get; set; }

    /// <summary>
    /// Search term for target name/email
    /// </summary>
    public string? SearchTerm { get; set; }

    /// <summary>
    /// Sort field: "DueDate", "RiskScore", "TargetName", "AssignedDate"
    /// </summary>
    public string SortBy { get; set; } = "DueDate";

    /// <summary>
    /// Sort direction: "Asc" or "Desc"
    /// </summary>
    public string SortDirection { get; set; } = "Asc";
}

/// <summary>
/// Pagination parameters.
/// </summary>
public class ApprovalPagination
{
    public int PageNumber { get; set; } = 1;
    public int PageSize { get; set; } = 50;
}

/// <summary>
/// Result from GetPendingApprovalsAsync.
/// </summary>
public class ApprovalInboxResult
{
    public List<ApprovalInboxItem> Approvals { get; set; } = new();
    public int TotalCount { get; set; }
    public int PageNumber { get; set; }
    public int PageSize { get; set; }
    public int TotalPages => (int)Math.Ceiling((double)TotalCount / PageSize);
}

/// <summary>
/// Single approval item in inbox view.
/// Unified model for both review assignments and access requests.
/// </summary>
public class ApprovalInboxItem
{
    // Identification
    public Guid ApprovalId { get; set; }
    public string ApprovalType { get; set; } = string.Empty; // "ReviewAssignment" or "AccessRequest"

    // Target Information
    public string TargetName { get; set; } = string.Empty;
    public string TargetType { get; set; } = string.Empty; // "User", "Group", "Application", etc.
    public Guid TargetId { get; set; }

    // Context
    public string? CampaignName { get; set; } // For review assignments
    public string? ResourceName { get; set; } // For access requests
    public string? RequestReason { get; set; } // For access requests

    // Risk & Priority
    public int RiskScore { get; set; }
    public string? RiskLevel { get; set; }
    public bool IsOverdue { get; set; }
    public int DaysUntilDue { get; set; }

    // Dates
    public DateTime AssignedDate { get; set; }
    public DateTime? DueDate { get; set; }
    public DateTime? LastAccessDate { get; set; }

    // Quick Context
    public string? ContextSummary { get; set; } // Brief summary for quick decision
    public string? Department { get; set; }
    public string? AccessFrequency { get; set; }
}

/// <summary>
/// Detailed approval information.
/// </summary>
public class ApprovalDetails
{
    // Identification
    public Guid ApprovalId { get; set; }
    public string ApprovalType { get; set; } = string.Empty;

    // Review Assignment Details (if applicable)
    public AccessReviewAssignment? ReviewAssignment { get; set; }
    public Campaign? Campaign { get; set; }

    // Access Request Details (if applicable)
    public AccessRequest? AccessRequest { get; set; }

    // Process Approval Details (if applicable)
    public Services.ProcessInstanceInfo? ProcessInstance { get; set; }

    // Target Details
    public PersonIdentity? TargetPerson { get; set; } // For user reviews
    public GroupObject? TargetGroup { get; set; } // For group access
    public string? TargetResourceInfo { get; set; } // JSON with additional resource context

    // Historical Context
    public List<ReviewDecisionHistory>? PreviousDecisions { get; set; }
    public List<string>? RecentActivityLog { get; set; }

    // Risk Analysis
    public RiskAnalysis? RiskAnalysis { get; set; }
}

/// <summary>
/// Risk analysis for approval decision.
/// </summary>
public class RiskAnalysis
{
    public int RiskScore { get; set; }
    public string RiskLevel { get; set; } = string.Empty;
    public List<string> RiskFactors { get; set; } = new();
    public string? MlRecommendation { get; set; }
    public decimal? ConfidenceScore { get; set; }
}

/// <summary>
/// Approval or denial decision.
/// </summary>
public class ApprovalDecision
{
    public Guid ApprovalId { get; set; }
    public string ApprovalType { get; set; } = string.Empty; // "ReviewAssignment" or "AccessRequest"
    public string Decision { get; set; } = string.Empty; // "Approved" or "Denied"
    public string Justification { get; set; } = string.Empty;
    public string? Comments { get; set; }
    public Guid DecisionMakerId { get; set; }
    public string DecisionMakerName { get; set; } = string.Empty;
    public string? DecisionMakerEmail { get; set; }
    public string? IpAddress { get; set; }
    public string? UserAgent { get; set; }
}

/// <summary>
/// Result from approval/denial operations.
/// </summary>
public class ApprovalResult
{
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    public string? WarningMessage { get; set; } // e.g., "Already processed"
    public DateTime? ProcessedAt { get; set; }
}

/// <summary>
/// Result from bulk approval operations.
/// </summary>
public class BulkApprovalResult
{
    public int TotalRequested { get; set; }
    public int SuccessCount { get; set; }
    public int FailureCount { get; set; }
    public List<BulkApprovalFailure> Failures { get; set; } = new();
    public DateTime ProcessedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Individual failure in bulk operation.
/// </summary>
public class BulkApprovalFailure
{
    public Guid ApprovalId { get; set; }
    public string ErrorMessage { get; set; } = string.Empty;
}

/// <summary>
/// Approval dashboard statistics.
/// </summary>
public class ApprovalStats
{
    // Overall Counts
    public int TotalPending { get; set; }
    public int TotalOverdue { get; set; }
    public int DueThisWeek { get; set; }
    public int CompletedThisMonth { get; set; }

    // By Type
    public int PendingReviewAssignments { get; set; }
    public int PendingAccessRequests { get; set; }
    public int PendingProcessApprovals { get; set; }

    // By Risk
    public int CriticalRisk { get; set; }
    public int HighRisk { get; set; }
    public int MediumRisk { get; set; }
    public int LowRisk { get; set; }

    // Performance
    public decimal AverageDecisionTimeHours { get; set; }
    public decimal ApprovalRate { get; set; } // Percentage of approvals vs denials
}

/// <summary>
/// Person/Identity information for approval context.
/// </summary>
public class PersonIdentity
{
    public Guid Id { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public string? Email { get; set; }
    public string? Department { get; set; }
    public string? JobTitle { get; set; }
    public string? ManagerName { get; set; }
    public int TotalGroupMemberships { get; set; }
    public int HighRiskGroupMemberships { get; set; }
    public DateTime? LastLoginDate { get; set; }
}

/// <summary>
/// Group information for approval context.
/// </summary>
public class GroupObject
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? GroupType { get; set; }
    public int MemberCount { get; set; }
    public bool IsHighRisk { get; set; }
    public string? RiskReason { get; set; }
    public string? OwnerName { get; set; }
}
