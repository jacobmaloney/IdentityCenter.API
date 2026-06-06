namespace DataAccessLibrary.Services;

/// <summary>
/// Auto-generates LicensePool records from discovered infrastructure data.
/// For SQL Server: scans Objects + ObjectAttributes for MSSQLSvc SPNs and
/// sqlServerEdition attributes, creates/updates pools per connection+edition.
/// </summary>
public interface ILicensePoolGeneratorService
{
    /// <summary>
    /// Generate SQL Server license pools for a single connection based on
    /// discovered SQL servers (Objects with MSSQLSvc SPN or sqlServerEdition attr).
    /// Creates one pool per edition (Enterprise, Standard, Express, Developer).
    /// Per-core pools for Enterprise/Standard; instance pools for Express/Developer.
    /// </summary>
    /// <returns>Number of pools created or updated.</returns>
    Task<int> GenerateSqlPoolsAsync(Guid connectionId, CancellationToken ct = default);

    /// <summary>
    /// Regenerate all auto-discoverable pools for a connection.
    /// Currently covers SQL Server (expandable to Exchange, SharePoint, etc.).
    /// </summary>
    Task<int> RegeneratePoolsForConnectionAsync(Guid connectionId, CancellationToken ct = default);

    /// <summary>
    /// Regenerate pools for ALL active connections. Called by the background job.
    /// </summary>
    Task<int> RegenerateAllPoolsAsync(CancellationToken ct = default);
}
