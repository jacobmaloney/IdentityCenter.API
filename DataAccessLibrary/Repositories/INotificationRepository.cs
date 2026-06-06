using DataAccessLibrary.Models;

namespace DataAccessLibrary.Repositories;

/// <summary>
/// Repository for admin notification CRUD and monitoring queries.
/// </summary>
public interface INotificationRepository
{
    /// <summary>
    /// Insert a notification record into the database.
    /// </summary>
    Task InsertNotificationAsync(AdminNotification notification, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get recent failed sync runs that haven't been notified yet.
    /// </summary>
    Task<List<FailedSyncInfo>> GetRecentFailedSyncsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Get sync projects that have been running too long (> 30 minutes).
    /// </summary>
    Task<List<LongRunningSyncInfo>> GetLongRunningSyncsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Check if SMTP is configured and active.
    /// </summary>
    Task<bool> IsSmtpConfiguredAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Get directory connections that have failed test results (not yet notified).
    /// </summary>
    Task<List<FailedConnectionInfo>> GetFailedConnectionsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Check if a notification of a given type already exists recently for deduplication.
    /// </summary>
    Task<bool> HasRecentNotificationAsync(string notificationType, string? titlePattern, TimeSpan window, CancellationToken cancellationToken = default);

    /// <summary>
    /// Check if a notification exists for a specific entity within a time window.
    /// </summary>
    Task<bool> HasRecentEntityNotificationAsync(Guid entityId, string notificationType, TimeSpan window, CancellationToken cancellationToken = default);

    /// <summary>
    /// Insert a notification with full parameters (used by background monitors).
    /// </summary>
    Task InsertMonitorNotificationAsync(
        string notificationType, string category, string severity,
        string title, string message, string? actionUrl, string? actionText,
        Guid? relatedEntityId, string? relatedEntityType, string source,
        CancellationToken cancellationToken = default);

    /// <summary>Get unread notification count.</summary>
    Task<int> GetUnreadCountAsync(CancellationToken cancellationToken = default);

    /// <summary>Get recent notifications (not dismissed), newest first.</summary>
    Task<(List<AdminNotification> Items, int TotalCount)> GetNotificationsPagedAsync(
        int skip = 0, int take = 50, CancellationToken cancellationToken = default);

    /// <summary>Mark a notification as read.</summary>
    Task MarkAsReadAsync(Guid notificationId, CancellationToken cancellationToken = default);
}

public class FailedSyncInfo
{
    public Guid RunId { get; set; }
    public Guid ProjectId { get; set; }
    public string ProjectName { get; set; } = string.Empty;
    public DateTime CompletedAt { get; set; }
    public string? ErrorMessage { get; set; }
}

public class LongRunningSyncInfo
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public DateTime? LastRunAt { get; set; }
}

public class FailedConnectionInfo
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? LastError { get; set; }
}
