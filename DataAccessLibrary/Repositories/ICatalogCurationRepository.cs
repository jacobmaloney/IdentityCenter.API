using DataAccessLibrary.Models;

namespace DataAccessLibrary.Repositories;

/// <summary>
/// Repository for the Access Catalog admin-curation feature (V116):
///
/// 1. Hide/show synced groups via the <c>CatalogVisibility</c> table. Presence
///    of a row = hidden, absence = visible. <see cref="ShowAsync"/> deletes
///    the row rather than flipping a flag.
/// 2. CRUD for admin-authored <see cref="CustomCatalogItem"/> entries
///    (applications, file shares, etc.) that surface in the catalog alongside
///    synced groups. Deletes are soft (IsActive=0) per codebase convention.
/// </summary>
public interface ICatalogCurationRepository
{
    // ---- Curation: hide/show synced groups -------------------------------

    /// <summary>True if a CatalogVisibility row exists for the given ObjectId.</summary>
    Task<bool> IsHiddenAsync(Guid objectId, CancellationToken ct = default);

    /// <summary>Returns the list of all currently-hidden ObjectIds for catalog filtering.</summary>
    Task<IReadOnlyList<Guid>> GetHiddenObjectIdsAsync(CancellationToken ct = default);

    /// <summary>
    /// Hide a synced object from the catalog. Idempotent: re-hiding an already-
    /// hidden ObjectId updates HiddenAt/HiddenBy/Reason in place rather than failing.
    /// </summary>
    Task HideAsync(Guid objectId, string? reason, string? hiddenBy, CancellationToken ct = default);

    /// <summary>
    /// Un-hide a synced object by deleting its CatalogVisibility row. Idempotent:
    /// ShowAsync on an object that isn't hidden is a no-op (zero rows affected).
    /// </summary>
    Task ShowAsync(Guid objectId, CancellationToken ct = default);

    // ---- Custom catalog items --------------------------------------------

    /// <summary>
    /// List all custom catalog items. <paramref name="activeOnly"/> defaults to
    /// true and excludes soft-deleted rows.
    /// </summary>
    Task<IReadOnlyList<CustomCatalogItem>> GetCustomItemsAsync(bool activeOnly = true, CancellationToken ct = default);

    /// <summary>Load a single custom item by Id. Returns null if not found.</summary>
    Task<CustomCatalogItem?> GetCustomItemAsync(Guid id, CancellationToken ct = default);

    /// <summary>
    /// Insert a new custom item. The repo generates a new Guid; any value passed
    /// in <see cref="CustomCatalogItem.Id"/> is ignored. Returns the new Id.
    /// </summary>
    Task<Guid> CreateCustomItemAsync(CustomCatalogItem item, CancellationToken ct = default);

    /// <summary>
    /// Update an existing custom item. The caller is responsible for setting
    /// <see cref="CustomCatalogItem.ModifiedBy"/>; <c>ModifiedAt</c> is forced
    /// to UTC now in the repo.
    /// </summary>
    Task UpdateCustomItemAsync(CustomCatalogItem item, CancellationToken ct = default);

    /// <summary>Soft-delete: <c>UPDATE ... SET IsActive=0, ModifiedAt=now</c>.</summary>
    Task DeleteCustomItemAsync(Guid id, CancellationToken ct = default);
}
