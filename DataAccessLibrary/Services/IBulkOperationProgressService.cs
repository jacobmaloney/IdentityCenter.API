namespace DataAccessLibrary.Services;

/// <summary>
/// Progress update for a bulk operation
/// </summary>
public class BulkOperationProgress
{
    /// <summary>
    /// The session ID for this operation
    /// </summary>
    public Guid SessionId { get; set; }

    /// <summary>
    /// The bulk issue type being fixed
    /// </summary>
    public string IssueId { get; set; } = string.Empty;

    /// <summary>
    /// Human-readable title
    /// </summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// Current status: Starting, InProgress, Completing, Completed, Failed, Cancelling, Cancelled
    /// </summary>
    public string Status { get; set; } = "Starting";

    /// <summary>
    /// Total items to process
    /// </summary>
    public int TotalItems { get; set; }

    /// <summary>
    /// Number of items processed so far
    /// </summary>
    public int ProcessedItems { get; set; }

    /// <summary>
    /// Number of successful operations
    /// </summary>
    public int SuccessCount { get; set; }

    /// <summary>
    /// Number of failed operations
    /// </summary>
    public int FailedCount { get; set; }

    /// <summary>
    /// Progress percentage (0-100)
    /// </summary>
    public int PercentComplete => TotalItems > 0 ? (ProcessedItems * 100) / TotalItems : 0;

    /// <summary>
    /// Current item being processed (for display)
    /// </summary>
    public string? CurrentItem { get; set; }

    /// <summary>
    /// Estimated time remaining in seconds
    /// </summary>
    public int? EstimatedSecondsRemaining { get; set; }

    /// <summary>
    /// When the operation started
    /// </summary>
    public DateTime StartedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// When the operation completed (if finished)
    /// </summary>
    public DateTime? CompletedAt { get; set; }

    /// <summary>
    /// Error message if failed
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// Recent errors (last 5)
    /// </summary>
    public List<string> RecentErrors { get; set; } = new();

    /// <summary>
    /// User who initiated the operation
    /// </summary>
    public string UserId { get; set; } = string.Empty;

    /// <summary>
    /// Whether this operation can be cancelled
    /// </summary>
    public bool CanCancel { get; set; } = true;
}

/// <summary>
/// Service interface for reporting bulk operation progress via SignalR.
/// Implemented in WebPortal.Hubs.BulkOperationProgressService.
/// </summary>
public interface IBulkOperationProgressService
{
    /// <summary>
    /// Start tracking a new bulk operation
    /// </summary>
    Task<BulkOperationProgress> StartOperationAsync(
        Guid sessionId,
        string issueId,
        string title,
        int totalItems,
        string userId,
        CancellationToken ct = default);

    /// <summary>
    /// Report progress on an operation
    /// </summary>
    Task ReportProgressAsync(
        Guid sessionId,
        int processedItems,
        int successCount,
        int failedCount,
        string? currentItem = null,
        CancellationToken ct = default);

    /// <summary>
    /// Report an error during processing
    /// </summary>
    Task ReportErrorAsync(
        Guid sessionId,
        string errorMessage,
        CancellationToken ct = default);

    /// <summary>
    /// Complete an operation
    /// </summary>
    Task CompleteOperationAsync(
        Guid sessionId,
        int successCount,
        int failedCount,
        CancellationToken ct = default);

    /// <summary>
    /// Fail an operation
    /// </summary>
    Task FailOperationAsync(
        Guid sessionId,
        string errorMessage,
        CancellationToken ct = default);

    /// <summary>
    /// Get current progress for a session
    /// </summary>
    BulkOperationProgress? GetProgress(Guid sessionId);

    /// <summary>
    /// Get all active operations
    /// </summary>
    List<BulkOperationProgress> GetActiveOperations();

    /// <summary>
    /// Fired whenever progress changes on any operation.
    /// Subscribers should call InvokeAsync(StateHasChanged) to update Blazor UI.
    /// </summary>
    event Action<BulkOperationProgress>? OnProgressUpdated;

    /// <summary>
    /// Cancel an operation (if supported)
    /// </summary>
    Task<bool> CancelOperationAsync(Guid sessionId, CancellationToken ct = default);
}
