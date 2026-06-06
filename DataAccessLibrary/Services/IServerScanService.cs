using DataAccessLibrary.Models;

namespace DataAccessLibrary.Services;

/// <summary>
/// Scans Windows servers via WinRM/PowerShell to discover local users, groups,
/// and installed Microsoft products. Follows the same credential and lifecycle
/// pattern as ISqlDirectScanService.
/// </summary>
public interface IServerScanService
{
    Task<ServerScanResult> ScanAsync(string hostOrIp, Guid? credentialId = null, CancellationToken ct = default);
    Task<ServerScanResult> RescanAsync(Guid serverId, CancellationToken ct = default);
    Task<ServerScanResult> ScanWithCredentialAsync(string hostOrIp, Guid credentialId, CancellationToken ct = default);
}
