using DataAccessLibrary.Models;

namespace DataAccessLibrary.Services;

/// <summary>
/// Connects directly to a SQL Server over TCP and collects inventory + permissions.
/// No agent required — runs from the IdentityCenter server using stored credentials.
/// </summary>
public interface ISqlDirectScanService
{
    /// <summary>
    /// Scan a SQL Server and store inventory + permissions. Returns a summary.
    /// </summary>
    /// <param name="hostOrIp">Hostname or IP address (e.g. "SQLPROD01" or "192.168.1.50")</param>
    /// <param name="credentialId">Optional specific credential. If null, uses the default credential.</param>
    /// <param name="instanceName">Optional named instance</param>
    /// <param name="port">TCP port (default 1433)</param>
    Task<SqlDirectScanResult> ScanAsync(
        string hostOrIp,
        Guid? credentialId = null,
        string? instanceName = null,
        int port = 1433,
        CancellationToken ct = default);

    /// <summary>
    /// Scan a SQL Server using a direct connection string (bypasses credential lookup).
    /// Used for ad-hoc scans and saving the connection for future rescans.
    /// </summary>
    Task<SqlDirectScanResult> ScanWithConnectionStringAsync(
        string connectionString,
        bool persistForRescan = true,
        CancellationToken ct = default);

    /// <summary>
    /// Scan a SQL Server using Integrated Security, impersonating a specific Windows account.
    /// Used when the IC service identity doesn't have SQL access but another Windows account does.
    /// </summary>
    /// <param name="connectionString">Must have IntegratedSecurity=true</param>
    /// <param name="windowsUsername">DOMAIN\user or user@domain.local</param>
    /// <param name="windowsPassword">Windows password (not stored; used only during scan)</param>
    Task<SqlDirectScanResult> ScanAsWindowsUserAsync(
        string connectionString,
        string windowsUsername,
        string windowsPassword,
        bool persistForRescan = true,
        CancellationToken ct = default);

    /// <summary>
    /// Rescan an existing SqlServerInventory using its stored encrypted connection string
    /// OR its linked credential profile (preferred).
    /// </summary>
    Task<SqlDirectScanResult> RescanAsync(Guid serverId, CancellationToken ct = default);

    /// <summary>
    /// Scan a server using a named credential profile. Handles all auth types including
    /// Windows impersonation transparently.
    /// </summary>
    /// <param name="hostOrIp">Host or IP of the target SQL server</param>
    /// <param name="credentialId">ID of the credential profile to use</param>
    /// <param name="instanceName">Optional named instance</param>
    /// <param name="port">TCP port (default 1433)</param>
    Task<SqlDirectScanResult> ScanWithCredentialAsync(
        string hostOrIp,
        Guid credentialId,
        string? instanceName = null,
        int port = 1433,
        CancellationToken ct = default);
}

public class SqlDirectScanResult
{
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    public string ServerName { get; set; } = "";
    public Guid? ServerId { get; set; }
    public string? Edition { get; set; }
    public string? Version { get; set; }
    public int DatabasesCollected { get; set; }
    public int PermissionsCollected { get; set; }
    public int PrivilegedPermissions { get; set; }
    public int AdMatchedPermissions { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}
