namespace DataAccessLibrary.Models;

/// <summary>
/// Row in CustomCatalogItems — admin-authored, non-sync catalog entries
/// (applications, file shares, licenses, physical access, etc.) that show up
/// in the Access Catalog alongside synced groups. Schema:
/// V116__CatalogCurationAndCustomItems.sql.
///
/// Soft-deleted via <see cref="IsActive"/>=false (codebase convention) rather
/// than a hard delete or DeletedAt column.
/// </summary>
public class CustomCatalogItem
{
    public Guid Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    /// <summary>
    /// Freeform but suggested values: Application / FileShare / License /
    /// PhysicalAccess / Other. Used to drive icon + filtering in the UI.
    /// </summary>
    public string ResourceType { get; set; } = "Application";

    /// <summary>Optional URL the user can click to access the resource directly.</summary>
    public string? ExternalUrl { get; set; }

    /// <summary>Low / Medium / High — used for the catalog risk-level filter.</summary>
    public string RiskLevel { get; set; } = "Low";

    /// <summary>Optional owner — FK to Objects.Id of the responsible person.</summary>
    public Guid? OwnerObjectId { get; set; }

    public DateTime CreatedAt { get; set; }

    public string? CreatedBy { get; set; }

    public DateTime? ModifiedAt { get; set; }

    public string? ModifiedBy { get; set; }

    /// <summary>Soft-delete flag — IsActive=false means the row is "deleted" but kept for audit.</summary>
    public bool IsActive { get; set; } = true;
}
