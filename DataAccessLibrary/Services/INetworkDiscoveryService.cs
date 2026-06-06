using DataAccessLibrary.Models;

namespace DataAccessLibrary.Services;

/// <summary>
/// Scans IP ranges to discover SQL Server instances and onboards them as
/// computer Objects with a "Discovered" status awaiting admin approval.
/// </summary>
public interface INetworkDiscoveryService
{
    /// <summary>
    /// Scan a CIDR range and upsert discovered SQL servers into SqlServerInventory
    /// with DiscoveryStatus = "Discovered". Also creates computer Objects for any
    /// servers not already in the directory.
    /// </summary>
    /// <param name="cidr">CIDR notation (e.g. "192.168.1.0/24")</param>
    /// <param name="rangeId">Optional NetworkScanRange ID to update LastScannedAt on</param>
    /// <param name="timeoutMs">Per-IP connection timeout in milliseconds</param>
    Task<NetworkScanResult> ScanCidrAsync(
        string cidr,
        Guid? rangeId = null,
        int timeoutMs = 500,
        CancellationToken ct = default);
}

public class NetworkScanResult
{
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    public string CidrRange { get; set; } = "";
    public int TotalScanned { get; set; }
    public int FoundServers { get; set; }
    public int NewServers { get; set; }       // Created new SqlServerInventory rows
    public int ExistingServers { get; set; }  // Updated existing rows
    public int NewObjects { get; set; }       // Created new Object rows (for unmanaged hosts)
    public int OfflineServers { get; set; }   // Previously-known servers that didn't respond this scan
    public int DurationSeconds { get; set; }
    public List<DiscoveredServer> Discovered { get; set; } = new();
}

public class DiscoveredServer
{
    public string IpAddress { get; set; } = "";
    public string? Hostname { get; set; }
    public int Port { get; set; }
    public Guid? SqlInventoryId { get; set; }
    public Guid? ObjectId { get; set; }
    public string? ObjectDisplayName { get; set; }
    public bool IsNew { get; set; }           // SqlServerInventory row was created this scan
    public bool IsObjectNew { get; set; }     // Computer Object was created this scan (vs matched existing)
}
