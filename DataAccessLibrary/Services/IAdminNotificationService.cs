namespace DataAccessLibrary.Services;

/// <summary>
/// Service interface for sending admin notifications to the system chat/activity feed.
/// Implementation is in WebPortal.Hubs.AdminNotificationService.
/// </summary>
public interface IAdminNotificationService
{
    /// <summary>
    /// Sends a notification to all connected admins and persists to database.
    /// </summary>
    Task SendNotificationAsync(DataAccessLibrary.Models.AdminNotification notification);

    /// <summary>
    /// Sends a policy violation notification.
    /// </summary>
    Task SendPolicyViolationAsync(string policyName, string entityName, string severity, string message, Guid? violationId = null);

    /// <summary>
    /// Sends a system alert notification.
    /// </summary>
    Task SendSystemAlertAsync(string title, string message, string severity = "Info");

    /// <summary>
    /// Sends a sync status notification.
    /// </summary>
    Task SendSyncStatusAsync(string title, string message, string severity = "Info", Guid? syncProjectId = null);

    /// <summary>
    /// Sends an access review campaign notification (started, completed, etc.).
    /// </summary>
    Task SendAccessReviewCampaignAsync(string title, string message, string severity = "Info", Guid? campaignId = null, string? campaignName = null, int? assignmentCount = null);

    /// <summary>
    /// Sends an access review decision notification (approved, denied).
    /// </summary>
    Task SendAccessReviewDecisionAsync(string decision, string targetName, string reviewerName, Guid? campaignId = null, string? campaignName = null, string? comment = null);
}
