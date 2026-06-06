using DataAccessLibrary.Models;

namespace DataAccessLibrary.Repositories;

/// <summary>
/// Queue persistence for password-reset requests originating from the
/// /admin/password-policy admin UI. Backed by the PendingPasswordResets
/// table (V112). Inserts only flag a row as 'Pending'; an AD write-back
/// worker (out of scope for the UI prompt) is what actually applies the
/// reset against the source directory and updates Status to Applied/Failed.
/// </summary>
public interface IPendingPasswordResetRepository
{
    /// <summary>
    /// Queue a "must change password at next logon" request for an Object.
    /// Returns the new row id. Multiple pending rows for the same object are allowed
    /// (the worker dedupes); callers do not need to check first.
    /// </summary>
    Task<Guid> RequestAsync(Guid objectId, string? requestedBy, string? notes, CancellationToken ct = default);

    /// <summary>
    /// Returns the most recent reset rows for an object, ordered by RequestedAt DESC.
    /// Used to show "queued reset" pills on the password-policy table.
    /// </summary>
    Task<IReadOnlyList<PendingPasswordReset>> GetPendingForObjectAsync(Guid objectId, CancellationToken ct = default);
}
