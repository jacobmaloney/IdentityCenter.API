namespace DataAccessLibrary.Models;

/// <summary>
/// One attestation record per NHI per cycle (90-day cadence). Backed by the
/// NHIAttestation table created in V110. The repository computes
/// NextDueDate = AttestedAt + 90 days at insert time.
/// </summary>
public class NHIAttestation
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>FK to Objects.Id — the service-account object being attested.</summary>
    public Guid ObjectId { get; set; }

    public string AttestedBy { get; set; } = string.Empty;
    public DateTime AttestedAt { get; set; } = DateTime.UtcNow;

    public string? Notes { get; set; }

    /// <summary>AttestedAt + 90 days — used to flag overdue NHIs.</summary>
    public DateTime NextDueDate { get; set; }

    /// <summary>True if NextDueDate is in the past.</summary>
    public bool IsOverdue => DateTime.UtcNow > NextDueDate;
}
