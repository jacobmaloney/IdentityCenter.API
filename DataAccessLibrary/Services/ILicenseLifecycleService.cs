using DataAccessLibrary.Models;

namespace DataAccessLibrary.Services;

/// <summary>
/// Tracks license assignment state transitions for audit and governance.
/// State machine: Assigned → FirstUsed → Dormant → Reactivated | Revoked → Removed
/// </summary>
public interface ILicenseLifecycleService
{
    /// <summary>Emit a lifecycle event (creates LicenseAssignmentEvent row).</summary>
    Task EmitEventAsync(Guid assignmentId, Guid poolId, Guid objectId, string eventType,
        string? actor = null, string? reason = null, string? metadata = null, CancellationToken ct = default);

    /// <summary>Get full event history for an assignment, newest first.</summary>
    Task<List<LicenseAssignmentEvent>> GetEventsForAssignmentAsync(Guid assignmentId, CancellationToken ct = default);

    /// <summary>Get recent lifecycle events across all assignments (for activity feed).</summary>
    Task<List<LicenseAssignmentEvent>> GetRecentEventsAsync(int limit = 50, CancellationToken ct = default);

    /// <summary>
    /// Nightly job: scan all active assignments, emit state transitions
    /// (Assigned → FirstUsed, Assigned|FirstUsed → Dormant, Dormant → Reactivated).
    /// Returns count of events emitted.
    /// </summary>
    Task<int> EvaluateStateTransitionsAsync(int dormantDays = 90, CancellationToken ct = default);
}
