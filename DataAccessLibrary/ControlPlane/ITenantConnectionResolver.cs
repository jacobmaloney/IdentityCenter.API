namespace DataAccessLibrary.ControlPlane;

/// <summary>
/// Resolves the connection string the CURRENT request's data access must use. This is THE seam that
/// enforces DB-per-tenant isolation: a tenant-scoped request resolves to that tenant's own DB and to
/// nothing else; a legacy/admin/unresolved request resolves to DefaultConnection.
///
/// The tenant identity comes ONLY from <see cref="ITenantContext"/> (set from the validated key), never
/// from client input — so no request can steer itself at another tenant's database.
/// </summary>
public interface ITenantConnectionResolver
{
    /// <summary>
    /// Returns the connection string for the current request.
    ///   - Tenant-scoped context  ⇒ that tenant's decrypted IcDbConnectionString (and ONLY that). If the
    ///     tenant is unknown or has no connection string, THROWS — it must never silently fall back to
    ///     DefaultConnection, because that would route a tenant request at the wrong database.
    ///   - Admin or unresolved context ⇒ DefaultConnection (control-plane ops + existing single-tenant).
    /// </summary>
    string Resolve();
}
