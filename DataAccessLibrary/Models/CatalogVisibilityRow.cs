namespace DataAccessLibrary.Models;

/// <summary>
/// Row in CatalogVisibility — tracks synced Objects (groups) that an admin has
/// hidden from the Access Catalog. Schema: V116__CatalogCurationAndCustomItems.sql.
///
/// Presence of a row = hidden. Absence = visible. <see cref="IsHidden"/> is kept
/// as a column for future "soft hide / suppressed" distinctions but today the
/// repo treats DELETE = show, INSERT/UPDATE = hide.
/// </summary>
public class CatalogVisibilityRow
{
    /// <summary>FK to Objects.Id — the synced group (or other object) being hidden.</summary>
    public Guid ObjectId { get; set; }

    public bool IsHidden { get; set; } = true;

    public DateTime HiddenAt { get; set; }

    /// <summary>User principal who hid the entry, or "system" for automation.</summary>
    public string? HiddenBy { get; set; }

    /// <summary>Optional admin note explaining why the entry was hidden.</summary>
    public string? Reason { get; set; }
}
