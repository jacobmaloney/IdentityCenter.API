using DataAccessLibrary.Models;

namespace DataAccessLibrary.Repositories;

/// <summary>
/// Campaign Repository Interface
/// High-performance data access for Access Review campaigns
/// </summary>
public interface ICampaignRepository
{
    // ========================================
    // CAMPAIGN OPERATIONS
    // ========================================

    /// <summary>Create new campaign</summary>
    Task<Campaign> CreateCampaignAsync(Campaign campaign, CancellationToken cancellationToken = default);

    /// <summary>Get campaign by ID</summary>
    Task<Campaign?> GetCampaignByIdAsync(Guid campaignId, CancellationToken cancellationToken = default);

    /// <summary>Get all campaigns</summary>
    Task<List<Campaign>> GetAllCampaignsAsync(CancellationToken cancellationToken = default);

    /// <summary>Get campaigns by status</summary>
    Task<List<Campaign>> GetCampaignsByStatusAsync(string status, CancellationToken cancellationToken = default);

    /// <summary>Get active campaigns (InProgress or Active status)</summary>
    Task<List<Campaign>> GetActiveCampaignsAsync(CancellationToken cancellationToken = default);

    /// <summary>Get campaigns by compliance framework</summary>
    Task<List<Campaign>> GetCampaignsByFrameworkAsync(string framework, CancellationToken cancellationToken = default);

    /// <summary>Update campaign</summary>
    Task<bool> UpdateCampaignAsync(Campaign campaign, CancellationToken cancellationToken = default);

    /// <summary>Update all campaign settings (used by PolicyModal Campaign tab)</summary>
    Task<bool> UpdateCampaignSettingsAsync(Campaign campaign, CancellationToken cancellationToken = default);

    /// <summary>Update campaign status</summary>
    Task<bool> UpdateCampaignStatusAsync(Guid campaignId, string status, CancellationToken cancellationToken = default);

    /// <summary>Update campaign statistics</summary>
    Task<bool> UpdateCampaignStatisticsAsync(Guid campaignId, CancellationToken cancellationToken = default);

    /// <summary>Get the most recent active standing (recurring) ComplianceReview campaign</summary>
    Task<Campaign?> GetStandingCampaignAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns campaigns that the recurrence engine should consider on the current run:
    /// active recurrences that are not paused and whose NextScheduledRun is on or before today.
    /// Excludes soft-deleted campaigns. Excludes child clones.
    /// </summary>
    Task<List<Campaign>> GetDueRecurringCampaignsAsync(DateTime today, CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates only the recurrence-tracking columns (NextScheduledRun + IsRecurrencePaused).
    /// Used by CampaignRecurrenceJob to advance a parent campaign's next-run date without
    /// touching unrelated campaign settings.
    /// </summary>
    Task<bool> UpdateRecurrenceTrackingAsync(Guid campaignId, DateTime? nextScheduledRun, bool isRecurrencePaused, CancellationToken cancellationToken = default);

    /// <summary>Get standing campaign for a specific compliance policy</summary>
    Task<Campaign?> GetCampaignBySourcePolicyIdAsync(Guid policyId, CancellationToken cancellationToken = default);

    /// <summary>Get assignments filtered by criteria (for Cases Dashboard)</summary>
    Task<List<AccessReviewAssignment>> GetAssignmentsFilteredAsync(CaseFilterCriteria filter, int limit = 500, CancellationToken cancellationToken = default);

    /// <summary>Get dashboard statistics across all active campaigns</summary>
    Task<CaseDashboardStats> GetCaseDashboardStatsAsync(CancellationToken cancellationToken = default);

    /// <summary>Bulk update reviewer on multiple assignments</summary>
    Task<int> BulkUpdateReviewerAsync(List<Guid> assignmentIds, Guid reviewerId, string reviewerName, string? email, CancellationToken cancellationToken = default);

    /// <summary>Delete campaign (soft delete)</summary>
    Task<bool> DeleteCampaignAsync(Guid campaignId, CancellationToken cancellationToken = default);
    
    /// <summary>Hard delete campaign and all related data (assignments, history, remediation actions)</summary>
    Task<bool> HardDeleteCampaignAsync(Guid campaignId, CancellationToken cancellationToken = default);

    // ========================================
    // ASSIGNMENT OPERATIONS
    // ========================================

    /// <summary>Create single assignment</summary>
    Task<AccessReviewAssignment> CreateAssignmentAsync(AccessReviewAssignment assignment, CancellationToken cancellationToken = default);

    /// <summary>Create assignments in bulk (high-performance)</summary>
    Task<int> CreateAssignmentsBulkAsync(List<AccessReviewAssignment> assignments, CancellationToken cancellationToken = default);

    /// <summary>Get assignment by ID</summary>
    Task<AccessReviewAssignment?> GetAssignmentByIdAsync(Guid assignmentId, CancellationToken cancellationToken = default);

    /// <summary>Get all assignments for a campaign</summary>
    Task<List<AccessReviewAssignment>> GetCampaignAssignmentsAsync(Guid campaignId, CancellationToken cancellationToken = default);

    /// <summary>Get assignments by reviewer</summary>
    Task<List<AccessReviewAssignment>> GetAssignmentsByReviewerAsync(Guid reviewerId, CancellationToken cancellationToken = default);

    /// <summary>Get open assignments targeting a specific object (Pending/InProgress, non-deleted campaigns)</summary>
    Task<List<AccessReviewAssignment>> GetAssignmentsByTargetAsync(Guid reviewTargetId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the most recent CompletedAt date for any access review assignment whose
    /// ReviewTargetId equals the supplied object id. Returns null if the object has
    /// never been reviewed.
    /// </summary>
    Task<DateTime?> GetLastReviewedDateForTargetAsync(Guid reviewTargetId, CancellationToken cancellationToken = default);

    /// <summary>Get assignments by status</summary>
    Task<List<AccessReviewAssignment>> GetAssignmentsByStatusAsync(Guid campaignId, string status, CancellationToken cancellationToken = default);

    /// <summary>Get all pending assignments across all active campaigns</summary>
    Task<List<AccessReviewAssignment>> GetAllPendingAssignmentsAsync(int maxResults = 100, CancellationToken cancellationToken = default);

    /// <summary>Get all assignments across all campaigns with campaign name</summary>
    Task<List<AccessReviewAssignment>> GetAllAssignmentsAsync(int maxResults = 200, CancellationToken cancellationToken = default);

    /// <summary>Update assignment</summary>
    Task<bool> UpdateAssignmentAsync(AccessReviewAssignment assignment, CancellationToken cancellationToken = default);

    /// <summary>Update assignment decision</summary>
    Task<bool> UpdateAssignmentDecisionAsync(Guid assignmentId, string decision, string? justification, Guid decisionMakerId, CancellationToken cancellationToken = default);

    /// <summary>Delegate assignment</summary>
    Task<bool> DelegateAssignmentAsync(Guid assignmentId, Guid delegateTo, string reason, CancellationToken cancellationToken = default);

    /// <summary>Escalate assignment</summary>
    Task<bool> EscalateAssignmentAsync(Guid assignmentId, Guid escalateTo, string reason, CancellationToken cancellationToken = default);

    // ========================================
    // DECISION HISTORY OPERATIONS
    // ========================================

    /// <summary>Record decision in immutable audit trail</summary>
    Task<ReviewDecisionHistory> RecordDecisionAsync(ReviewDecisionHistory decision, CancellationToken cancellationToken = default);

    /// <summary>Get decision history for assignment</summary>
    Task<List<ReviewDecisionHistory>> GetAssignmentHistoryAsync(Guid assignmentId, CancellationToken cancellationToken = default);

    /// <summary>Get decision history for campaign</summary>
    Task<List<ReviewDecisionHistory>> GetCampaignHistoryAsync(Guid campaignId, CancellationToken cancellationToken = default);

    // ========================================
    // REMEDIATION OPERATIONS
    // ========================================

    /// <summary>Create remediation action</summary>
    Task<RemediationAction> CreateRemediationActionAsync(RemediationAction action, CancellationToken cancellationToken = default);

    /// <summary>Get pending remediation actions</summary>
    Task<List<RemediationAction>> GetPendingRemediationActionsAsync(CancellationToken cancellationToken = default);

    /// <summary>Get remediation actions for campaign</summary>
    Task<List<RemediationAction>> GetCampaignRemediationActionsAsync(Guid campaignId, CancellationToken cancellationToken = default);

    /// <summary>Update remediation action status</summary>
    Task<bool> UpdateRemediationActionStatusAsync(Guid actionId, string status, string? result, CancellationToken cancellationToken = default);

    // ========================================
    // TEMPLATE OPERATIONS
    // ========================================

    /// <summary>Get all active templates</summary>
    Task<List<CampaignTemplate>> GetActiveTemplatesAsync(CancellationToken cancellationToken = default);

    /// <summary>Get template by ID</summary>
    Task<CampaignTemplate?> GetTemplateByIdAsync(Guid templateId, CancellationToken cancellationToken = default);

    /// <summary>Get templates by compliance framework</summary>
    Task<List<CampaignTemplate>> GetTemplatesByFrameworkAsync(string framework, CancellationToken cancellationToken = default);

    /// <summary>Create template</summary>
    Task<CampaignTemplate> CreateTemplateAsync(CampaignTemplate template, CancellationToken cancellationToken = default);

    /// <summary>Update template</summary>
    Task<bool> UpdateTemplateAsync(CampaignTemplate template, CancellationToken cancellationToken = default);

    // ========================================
    // STATISTICS & REPORTING
    // ========================================

    /// <summary>Get campaign statistics</summary>
    Task<CampaignStatistics> GetCampaignStatisticsAsync(Guid campaignId, CancellationToken cancellationToken = default);

    /// <summary>Get reviewer workload</summary>
    Task<List<ReviewerWorkload>> GetReviewerWorkloadAsync(Guid campaignId, CancellationToken cancellationToken = default);

    /// <summary>Get overdue assignments count</summary>
    Task<int> GetOverdueAssignmentsCountAsync(Guid campaignId, CancellationToken cancellationToken = default);

    // ========================================
    // SETTINGS OPERATIONS
    // ========================================

    /// <summary>Get access review settings (singleton)</summary>
    Task<AccessReviewSettings> GetSettingsAsync(CancellationToken cancellationToken = default);

    /// <summary>Update access review settings</summary>
    Task<bool> UpdateSettingsAsync(AccessReviewSettings settings, CancellationToken cancellationToken = default);
}

/// <summary>
/// Campaign statistics DTO
/// </summary>
public class CampaignStatistics
{
    public Guid CampaignId { get; set; }
    public int TotalAssignments { get; set; }
    public int CompletedAssignments { get; set; }
    public int PendingAssignments { get; set; }
    public int OverdueAssignments { get; set; }
    public int ApprovedCount { get; set; }
    public int DeniedCount { get; set; }
    public decimal CompletionPercentage { get; set; }
    public int DaysRemaining { get; set; }
}

/// <summary>
/// Reviewer workload DTO
/// </summary>
public class ReviewerWorkload
{
    public Guid ReviewerId { get; set; }
    public string ReviewerName { get; set; } = string.Empty;
    public int TotalAssigned { get; set; }
    public int Completed { get; set; }
    public int Pending { get; set; }
    public decimal CompletionPercentage { get; set; }
}

/// <summary>
/// Filter criteria for Cases Dashboard
/// </summary>
public class CaseFilterCriteria
{
    public Guid? CampaignId { get; set; }
    public string? StatusFilter { get; set; }
    public bool NoManagerOnly { get; set; }
    public string? SearchText { get; set; }
    public Guid? ReviewerId { get; set; }
}

/// <summary>
/// Dashboard statistics across all active campaigns
/// </summary>
public class CaseDashboardStats
{
    public int TotalPending { get; set; }
    public int NoManagerCases { get; set; }
    public int OverdueCases { get; set; }
    public int CompletedToday { get; set; }
    public int ActiveCampaigns { get; set; }
}
