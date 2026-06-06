namespace DataAccessLibrary.Models;

/// <summary>
/// Row in PendingPasswordResets — a queued "must change password at next logon"
/// flag that has not yet been pushed to the source directory (AD write-back, etc).
/// Schema: V112__PendingPasswordResets.sql.
/// </summary>
public class PendingPasswordReset
{
    public Guid Id { get; set; }

    /// <summary>FK to Objects.Id (the user account being reset).</summary>
    public Guid ObjectId { get; set; }

    public DateTime RequestedAt { get; set; }

    /// <summary>User principal who initiated the request, or "system" for automation.</summary>
    public string? RequestedBy { get; set; }

    /// <summary>Pending / Applied / Failed.</summary>
    public string Status { get; set; } = "Pending";

    public DateTime? AppliedAt { get; set; }

    public string? Notes { get; set; }
}
