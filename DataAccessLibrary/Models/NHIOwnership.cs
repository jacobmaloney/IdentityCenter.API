namespace DataAccessLibrary.Models;

/// <summary>
/// Owner attribution for a Non-Human Identity (service account, service principal,
/// gMSA/MSA). Backed by the NHIOwnership table created in V110. One row per
/// Object (UNIQUE on ObjectId).
/// </summary>
public class NHIOwnership
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>FK to Objects.Id — the service-account object being owned.</summary>
    public Guid ObjectId { get; set; }

    /// <summary>FK to Objects.Id — the human identity who owns this NHI. Null if name-only.</summary>
    public Guid? OwnerId { get; set; }

    /// <summary>Denormalized owner name for display when OwnerId is unresolved.</summary>
    public string? OwnerName { get; set; }

    public DateTime AssignedAt { get; set; } = DateTime.UtcNow;
    public string? AssignedBy { get; set; }
    public string? Notes { get; set; }
}
