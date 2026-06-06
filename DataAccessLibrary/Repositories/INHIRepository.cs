using DataAccessLibrary.Models;

namespace DataAccessLibrary.Repositories;

/// <summary>
/// Read/write data access for the NHI Governance page (V110).
/// </summary>
public interface INHIRepository
{
    /// <summary>
    /// Returns service-account Objects (NHIs) matching the given filter:
    /// All / ServiceAccount / ServicePrincipal / GMSA / Unowned / Privileged.
    /// Detection is OR-logic over (a) objectClass in {serviceprincipal,gmsa,msa},
    /// (b) user objects with a servicePrincipalName attribute, or (c) user
    /// objects whose Username or DisplayName matches the service-account
    /// naming pattern. Soft-deleted objects (DeletedAt IS NOT NULL) are excluded.
    /// </summary>
    Task<IEnumerable<IdentityObject>> GetNHIsAsync(string? filter = null, CancellationToken ct = default);

    /// <summary>Returns the ownership row for the given NHI, or null if unowned.</summary>
    Task<NHIOwnership?> GetOwnershipAsync(Guid objectId, CancellationToken ct = default);

    /// <summary>
    /// Inserts or updates the ownership row for an NHI. There is at most one
    /// owner per NHI (UNIQUE index on ObjectId).
    /// </summary>
    Task SetOwnerAsync(Guid objectId, Guid? ownerId, string ownerName, string assignedBy, CancellationToken ct = default);

    /// <summary>Returns the most recent attestation for an NHI, or null.</summary>
    Task<NHIAttestation?> GetLatestAttestationAsync(Guid objectId, CancellationToken ct = default);

    /// <summary>
    /// Records a new attestation. NextDueDate is computed as AttestedAt + 90 days.
    /// </summary>
    Task RecordAttestationAsync(Guid objectId, string attestedBy, string? notes, CancellationToken ct = default);

    /// <summary>Returns aggregated NHI counts for the dashboard stat cards.</summary>
    Task<NHISummaryStats> GetSummaryStatsAsync(CancellationToken ct = default);
}
