namespace DataAccessLibrary.Repositories;

/// <summary>
/// Persists user thumbs-up / thumbs-down feedback on bot messages.
/// Backed by the ChatFeedback table created in V109.
/// </summary>
public interface IChatFeedbackRepository
{
    /// <summary>
    /// Save a feedback row. <paramref name="feedback"/> must be +1 (thumbs up) or -1 (thumbs down).
    /// Returns the new row id.
    /// </summary>
    Task<Guid> SaveFeedbackAsync(string messageId, string? userId, int feedback, CancellationToken ct = default);

    /// <summary>
    /// Get the most recent feedback value (+1 / -1) given by a user for a specific message,
    /// or null if no feedback was recorded.
    /// </summary>
    Task<int?> GetLatestFeedbackAsync(string messageId, string? userId, CancellationToken ct = default);
}
