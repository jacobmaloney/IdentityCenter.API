using DataAccessLibrary.Models;

namespace DataAccessLibrary.Services;

/// <summary>
/// Repository for managing bulk operation sessions and changes.
/// Supports rollback functionality by tracking all changes made during bulk operations.
/// </summary>
public interface IBulkOperationSessionRepository
{
    // ============================================================================
    // SESSION MANAGEMENT
    // ============================================================================

    /// <summary>
    /// Create a new bulk operation session
    /// </summary>
    Task<Guid> CreateSessionAsync(BulkOperationSession session, CancellationToken ct = default);

    /// <summary>
    /// Get a session by ID with all its changes
    /// </summary>
    Task<BulkOperationSession?> GetSessionAsync(Guid sessionId, bool includeChanges = true, CancellationToken ct = default);

    /// <summary>
    /// Get the most recent session for a user
    /// </summary>
    Task<BulkOperationSession?> GetLastSessionForUserAsync(string userId, CancellationToken ct = default);

    /// <summary>
    /// Get recent sessions (for history command)
    /// </summary>
    Task<List<BulkOperationHistoryItem>> GetRecentSessionsAsync(
        string? userId = null,
        int limit = 20,
        CancellationToken ct = default);

    /// <summary>
    /// Get sessions for a specific issue type
    /// </summary>
    Task<List<BulkOperationHistoryItem>> GetSessionsByIssueAsync(
        string issueId,
        int limit = 10,
        CancellationToken ct = default);

    /// <summary>
    /// Update session status
    /// </summary>
    Task UpdateSessionStatusAsync(
        Guid sessionId,
        string status,
        string? rolledBackBy = null,
        CancellationToken ct = default);

    // ============================================================================
    // CHANGE TRACKING
    // ============================================================================

    /// <summary>
    /// Record multiple changes for a session (batch insert)
    /// </summary>
    Task RecordChangesAsync(Guid sessionId, List<BulkOperationChange> changes, CancellationToken ct = default);

    /// <summary>
    /// Get all changes for a session
    /// </summary>
    Task<List<BulkOperationChange>> GetSessionChangesAsync(Guid sessionId, CancellationToken ct = default);

    /// <summary>
    /// Get changes that haven't been rolled back yet
    /// </summary>
    Task<List<BulkOperationChange>> GetRollbackableChangesAsync(Guid sessionId, CancellationToken ct = default);

    /// <summary>
    /// Mark a change as rolled back
    /// </summary>
    Task MarkChangeRolledBackAsync(Guid changeId, string? error = null, CancellationToken ct = default);

    /// <summary>
    /// Mark multiple changes as rolled back
    /// </summary>
    Task MarkChangesRolledBackAsync(List<Guid> changeIds, CancellationToken ct = default);

    // ============================================================================
    // ROLLBACK OPERATIONS
    // ============================================================================

    /// <summary>
    /// Check if a session can be rolled back (within time limit, not already rolled back)
    /// </summary>
    Task<(bool CanRollback, string Reason)> CanRollbackSessionAsync(Guid sessionId, CancellationToken ct = default);

    /// <summary>
    /// Get rollback statistics for a session
    /// </summary>
    Task<(int Total, int Rollbackable, int AlreadyRolledBack)> GetRollbackStatsAsync(
        Guid sessionId,
        CancellationToken ct = default);

    // ============================================================================
    // CLEANUP
    // ============================================================================

    /// <summary>
    /// Cleanup old sessions and their changes
    /// </summary>
    Task CleanupOldSessionsAsync(int retentionDays = 30, CancellationToken ct = default);
}
