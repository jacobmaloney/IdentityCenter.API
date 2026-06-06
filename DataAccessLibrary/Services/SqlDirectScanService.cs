using Common.Encryption;
using Dapper;
using DataAccessLibrary.Models;
using DataAccessLibrary.Repositories;
using Logging;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Principal;

namespace DataAccessLibrary.Services;

public class SqlDirectScanService : ISqlDirectScanService
{
    private readonly ISqlLicenseRepository _repo;
    private readonly ISqlCredentialRepository _credRepo;
    private readonly IEncryptionService _encryption;
    private readonly IConfiguration _config;
    private readonly IGlobalLogger _logger;

    public SqlDirectScanService(
        ISqlLicenseRepository repo,
        ISqlCredentialRepository credRepo,
        IEncryptionService encryption,
        IConfiguration config,
        IGlobalLogger logger)
    {
        _repo = repo;
        _credRepo = credRepo;
        _encryption = encryption;
        _config = config;
        _logger = logger;
    }

    public async Task<SqlDirectScanResult> ScanAsync(
        string hostOrIp, Guid? credentialId = null, string? instanceName = null,
        int port = 1433, CancellationToken ct = default)
    {
        // Build a connection string from the stored credential, then delegate to the core scan
        var cred = await GetCredentialAsync(credentialId, ct);
        if (cred == null)
        {
            return new SqlDirectScanResult
            {
                ServerName = hostOrIp,
                ErrorMessage = "No SQL credential configured. Add one in Settings → SQL Credentials."
            };
        }

        var server = string.IsNullOrEmpty(instanceName) ? hostOrIp : string.Concat(hostOrIp, "\\", instanceName);
        var builder = new SqlConnectionStringBuilder
        {
            DataSource = string.Concat(server, ",", port.ToString()),
            TrustServerCertificate = true,
            ConnectTimeout = 15
        };

        if (cred.AuthType == "SqlAuth")
        {
            builder.IntegratedSecurity = false;
            builder.UserID = cred.Username ?? "sa";
            builder.Password = string.IsNullOrEmpty(cred.EncryptedPassword)
                ? ""
                : await _encryption.DecryptAsync(cred.EncryptedPassword);
        }
        else
        {
            builder.IntegratedSecurity = true;
        }

        var result = await ScanWithConnectionStringAsync(builder.ConnectionString, persistForRescan: true, ct);
        if (result.Success) await UpdateCredentialLastUsedAsync(cred.Id, ct);
        return result;
    }

    public async Task<SqlDirectScanResult> RescanAsync(Guid serverId, CancellationToken ct = default)
    {
        var server = await _repo.GetServerByIdAsync(serverId);
        if (server == null)
        {
            return new SqlDirectScanResult { ErrorMessage = "Server not found in inventory" };
        }

        // Prefer credential profile (works for all auth types including Windows impersonation)
        if (server.CredentialId.HasValue)
        {
            var host = server.IpAddress ?? server.Fqdn ?? server.ServerName;
            return await ScanWithCredentialAsync(host, server.CredentialId.Value, server.InstanceName, server.Port, ct);
        }

        // Fallback: use stored encrypted connection string (SqlAuth / AzureAD only)
        if (!string.IsNullOrEmpty(server.EncryptedConnectionString))
        {
            var connString = await _encryption.DecryptAsync(server.EncryptedConnectionString);
            return await ScanWithConnectionStringAsync(connString, persistForRescan: false, ct);
        }

        return new SqlDirectScanResult
        {
            ServerName = server.ServerName,
            ErrorMessage = "No saved connection or credential for this server. Scan manually first to store credentials."
        };
    }

    public async Task<SqlDirectScanResult> ScanWithCredentialAsync(
        string hostOrIp, Guid credentialId, string? instanceName = null,
        int port = 1433, CancellationToken ct = default)
    {
        var cred = await _credRepo.GetByIdAsync(credentialId);
        if (cred == null || !cred.IsActive)
        {
            return new SqlDirectScanResult
            {
                ServerName = hostOrIp,
                ErrorMessage = "Credential profile not found or inactive"
            };
        }

        var dataSource = string.IsNullOrEmpty(instanceName)
            ? string.Concat(hostOrIp, ",", port.ToString())
            : string.Concat(hostOrIp, "\\", instanceName, ",", port.ToString());

        var builder = new SqlConnectionStringBuilder
        {
            DataSource = dataSource,
            TrustServerCertificate = true,
            ConnectTimeout = 15
        };

        string? decryptedPassword = string.IsNullOrEmpty(cred.EncryptedPassword)
            ? null
            : await _encryption.DecryptAsync(cred.EncryptedPassword);

        SqlDirectScanResult result;

        switch (cred.AuthType)
        {
            case "SqlAuth":
                builder.IntegratedSecurity = false;
                builder.UserID = cred.Username ?? "sa";
                builder.Password = decryptedPassword ?? "";
                result = await ScanWithConnectionStringAsync(builder.ConnectionString, persistForRescan: false, ct);
                break;

            case "WindowsAuthSpecified":
                if (string.IsNullOrEmpty(cred.Username) || string.IsNullOrEmpty(decryptedPassword))
                {
                    result = new SqlDirectScanResult
                    {
                        ServerName = hostOrIp,
                        ErrorMessage = "Windows credential profile missing username or password"
                    };
                    break;
                }
                builder.IntegratedSecurity = true;
                if (OperatingSystem.IsWindows())
                {
                    result = await ScanAsWindowsUserAsync(
                        builder.ConnectionString, cred.Username, decryptedPassword, persistForRescan: false, ct);
                }
                else
                {
                    result = new SqlDirectScanResult
                    {
                        ErrorMessage = "Windows impersonation is only supported on Windows hosts"
                    };
                }
                break;

            case "AzureAD":
                builder.Authentication = SqlAuthenticationMethod.ActiveDirectoryPassword;
                builder.UserID = cred.Username ?? "";
                builder.Password = decryptedPassword ?? "";
                result = await ScanWithConnectionStringAsync(builder.ConnectionString, persistForRescan: false, ct);
                break;

            case "WindowsAuth":
            default:
                builder.IntegratedSecurity = true;
                result = await ScanWithConnectionStringAsync(builder.ConnectionString, persistForRescan: false, ct);
                break;
        }

        if (result.Success)
        {
            // Link the credential to the inventory row for future rescans
            if (result.ServerId.HasValue)
            {
                await _repo.UpdateServerCredentialAsync(result.ServerId.Value, credentialId);
            }
            await _credRepo.MarkUsedAsync(credentialId);
        }

        return result;
    }

    public async Task<SqlDirectScanResult> ScanWithConnectionStringAsync(
        string connectionString, bool persistForRescan = true, CancellationToken ct = default)
    {
        var result = new SqlDirectScanResult();
        string hostOrIp = "";
        string? instanceName = null;
        int port = 1433;

        try
        {
            // Parse the connection string to extract host/port/instance for later matching
            var parsed = new SqlConnectionStringBuilder(connectionString);
            var ds = parsed.DataSource ?? "";
            // DataSource can be "host,port", "host\instance", or "host\instance,port"
            var instSplit = ds.Split('\\');
            var hostPart = instSplit[0];
            if (instSplit.Length > 1)
            {
                var tail = instSplit[1];
                var portSplit = tail.Split(',');
                instanceName = portSplit[0];
                if (portSplit.Length > 1 && int.TryParse(portSplit[1], out var p2)) port = p2;
            }
            else
            {
                var portSplit = hostPart.Split(',');
                hostPart = portSplit[0];
                if (portSplit.Length > 1 && int.TryParse(portSplit[1], out var p2)) port = p2;
            }
            hostOrIp = hostPart;
            result.ServerName = hostOrIp;

            using var conn = new SqlConnection(connectionString);
            await conn.OpenAsync(ct);

            _logger.LogInformation("SqlDirectScan: connected to {Server}", hostOrIp);

            // 3. Collect inventory
            var (machineName, instance, edition, version, versionMajor) = await GetInstanceInfoAsync(conn, ct);
            result.Edition = edition;
            result.Version = version;

            var cpuCores = await GetCpuCoresAsync(conn, ct);
            var memoryGb = await GetMemoryGbAsync(conn, ct);

            // Determine IP vs hostname from what the user entered
            var isIpAddress = System.Net.IPAddress.TryParse(hostOrIp, out _);
            var canonicalServerName = machineName; // Always use the real machine name from SQL
            var ipAddress = isIpAddress ? hostOrIp : null;

            // If user entered a hostname, resolve to IP for storage (best effort)
            if (!isIpAddress)
            {
                try
                {
                    var addresses = await System.Net.Dns.GetHostAddressesAsync(hostOrIp, ct);
                    ipAddress = addresses.FirstOrDefault(a => a.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)?.ToString();
                }
                catch { /* best effort */ }
            }

            // 4. Upsert SqlServerInventory
            // Look up by machine name first, then fall back to IP (handles re-scans via different paths)
            var existingServer = await _repo.GetServerByNameAsync(canonicalServerName, instanceName);
            if (existingServer == null && ipAddress != null)
            {
                existingServer = await _repo.GetServerByIpAsync(ipAddress, instanceName);
            }

            var serverInv = existingServer ?? new SqlServerInventory
            {
                ServerName = canonicalServerName,
                InstanceName = instanceName,
                DiscoveryMethod = "DirectScan"
            };
            serverInv.ServerName = canonicalServerName; // Update in case an old row had the IP as name
            serverInv.IpAddress = ipAddress;
            serverInv.Fqdn = isIpAddress ? null : hostOrIp;
            serverInv.SqlEdition = edition;
            serverInv.SqlVersion = version;
            serverInv.SqlVersionMajor = versionMajor;
            serverInv.CpuCores = cpuCores;
            serverInv.MemoryGb = memoryGb;
            serverInv.Port = port;
            serverInv.LastDiscoveredAt = DateTime.UtcNow;
            serverInv.IsOnline = true;

            // Persist the encrypted connection string for future one-click rescans
            if (persistForRescan)
            {
                serverInv.EncryptedConnectionString = await _encryption.EncryptAsync(connectionString);
            }

            var serverId = await _repo.UpsertServerAsync(serverInv);
            result.ServerId = serverId;
            result.ServerName = canonicalServerName;

            // 5. Collect databases
            var databases = await GetDatabasesAsync(conn, ct);
            await _repo.UpsertDatabasesAsync(serverId, databases);
            result.DatabasesCollected = databases.Count;

            // 6. Collect permissions
            var permissions = await GetPermissionsAsync(conn, serverId, ct);
            await _repo.DeactivateServerPermissionsAsync(serverId);
            var (inserted, adMatched) = await _repo.UpsertPermissionsAsync(serverId, permissions);
            result.PermissionsCollected = permissions.Count;
            result.PrivilegedPermissions = permissions.Count(p => p.IsPrivileged);
            result.AdMatchedPermissions = adMatched;

            result.Success = true;
            _logger.LogInformation("SqlDirectScan: {Server} — {DBs} DBs, {Perms} perms, {Priv} privileged, {Matched} AD-matched",
                hostOrIp, databases.Count, permissions.Count, result.PrivilegedPermissions, adMatched);
        }
        catch (SqlException ex) when (ex.Number == 18456)
        {
            result.ErrorMessage = string.Concat("Login failed. Check credentials. (", ex.Message, ")");
            _logger.LogWarning("SqlDirectScan: login failed for {Server}", hostOrIp);
        }
        catch (SqlException ex) when (ex.Number == 10060 || ex.Number == 53)
        {
            result.ErrorMessage = string.Concat("Cannot connect to server. Check hostname/IP and port ", port.ToString(), ".");
            _logger.LogWarning("SqlDirectScan: cannot reach {Server}", hostOrIp);
        }
        catch (Exception ex)
        {
            result.ErrorMessage = ex.Message;
            _logger.LogError(ex, "SqlDirectScan: failed for {Server}", hostOrIp);
        }

        return result;
    }

    private async Task<SqlServerCredential?> GetCredentialAsync(Guid? credentialId, CancellationToken ct)
    {
        var connStr = _config.GetConnectionString("DefaultConnection");
        using var conn = new SqlConnection(connStr);
        await conn.OpenAsync(ct);

        if (credentialId.HasValue)
        {
            return await conn.QuerySingleOrDefaultAsync<SqlServerCredential>(
                "SELECT * FROM SqlServerCredentials WHERE Id = @Id AND IsActive = 1",
                new { Id = credentialId.Value });
        }

        return await conn.QuerySingleOrDefaultAsync<SqlServerCredential>(
            "SELECT TOP 1 * FROM SqlServerCredentials WHERE IsDefault = 1 AND IsActive = 1 ORDER BY CreatedAt DESC");
    }

    private async Task UpdateCredentialLastUsedAsync(Guid credId, CancellationToken ct)
    {
        var connStr = _config.GetConnectionString("DefaultConnection");
        using var conn = new SqlConnection(connStr);
        await conn.OpenAsync(ct);
        await conn.ExecuteAsync(
            "UPDATE SqlServerCredentials SET LastUsedAt = @Now WHERE Id = @Id",
            new { Id = credId, Now = DateTime.UtcNow });
    }

    private static async Task<(string machineName, string instance, string edition, string version, int versionMajor)> GetInstanceInfoAsync(
        SqlConnection conn, CancellationToken ct)
    {
        const string sql = @"
            SELECT
                CAST(SERVERPROPERTY('MachineName') AS NVARCHAR(200)) AS MachineName,
                CAST(SERVERPROPERTY('InstanceName') AS NVARCHAR(200)) AS InstanceName,
                CAST(SERVERPROPERTY('Edition') AS NVARCHAR(200)) AS Edition,
                CAST(SERVERPROPERTY('ProductVersion') AS NVARCHAR(50)) AS Version,
                CAST(SERVERPROPERTY('ProductMajorVersion') AS INT) AS VersionMajor";

        var row = await conn.QuerySingleAsync<dynamic>(sql);
        return (
            (string?)row.MachineName ?? "Unknown",
            (string?)row.InstanceName ?? "MSSQLSERVER",
            (string?)row.Edition ?? "Unknown",
            (string?)row.Version ?? "0",
            (int?)row.VersionMajor ?? 0);
    }

    private static async Task<int?> GetCpuCoresAsync(SqlConnection conn, CancellationToken ct)
    {
        try
        {
            return await conn.ExecuteScalarAsync<int?>("SELECT cpu_count FROM sys.dm_os_sys_info");
        }
        catch { return null; }
    }

    private static async Task<int?> GetMemoryGbAsync(SqlConnection conn, CancellationToken ct)
    {
        try
        {
            var kb = await conn.ExecuteScalarAsync<long?>(
                "SELECT physical_memory_kb FROM sys.dm_os_sys_info");
            return kb.HasValue ? (int?)(kb.Value / 1024 / 1024) : null;
        }
        catch { return null; }
    }

    private static async Task<List<SqlDatabaseInventory>> GetDatabasesAsync(SqlConnection conn, CancellationToken ct)
    {
        const string sql = @"
            SELECT
                d.name AS DatabaseName,
                d.state_desc AS State,
                d.recovery_model_desc AS RecoveryModel,
                d.compatibility_level AS CompatibilityLevel,
                CASE WHEN d.database_id <= 4 THEN 1 ELSE 0 END AS IsSystemDb,
                ISNULL(CAST(SUM(CAST(mf.size AS BIGINT)) * 8.0 / 1024 / 1024 AS FLOAT), 0) AS SizeGb
            FROM sys.databases d
            LEFT JOIN sys.master_files mf ON d.database_id = mf.database_id AND mf.type = 0
            GROUP BY d.name, d.state_desc, d.recovery_model_desc, d.compatibility_level, d.database_id";

        var rows = await conn.QueryAsync<SqlDatabaseInventory>(sql);
        return rows.ToList();
    }

    private static async Task<List<SqlServerPermission>> GetPermissionsAsync(
        SqlConnection conn, Guid serverId, CancellationToken ct)
    {
        var privileged = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "sysadmin", "serveradmin", "securityadmin", "db_owner", "db_securityadmin",
            "CONTROL SERVER", "ALTER ANY LOGIN", "ALTER ANY DATABASE"
        };

        var permissions = new List<SqlServerPermission>();

        // Server-level role memberships
        const string serverRoleSql = @"
            SELECT sp.name AS PrincipalName, sp.type_desc AS PrincipalType,
                   CONVERT(NVARCHAR(200), sp.sid, 1) AS PrincipalSid,
                   sr.name AS RoleName
            FROM sys.server_role_members srm
            JOIN sys.server_principals sp ON srm.member_principal_id = sp.principal_id
            JOIN sys.server_principals sr ON srm.role_principal_id = sr.principal_id
            WHERE sp.name NOT LIKE '##%' AND sp.name != 'sa'";

        foreach (var row in await conn.QueryAsync<dynamic>(serverRoleSql))
        {
            string roleName = row.RoleName;
            permissions.Add(new SqlServerPermission
            {
                SqlServerInventoryId = serverId,
                PrincipalName = row.PrincipalName,
                PrincipalType = MapPrincipalType((string)row.PrincipalType),
                PrincipalSid = row.PrincipalSid,
                PermissionScope = "Server",
                DatabaseName = null,
                PermissionName = roleName,
                PermissionClass = "ROLE_MEMBERSHIP",
                GrantState = "GRANT",
                IsPrivileged = privileged.Contains(roleName),
                RiskLevel = privileged.Contains(roleName) ? "Critical" : "Low"
            });
        }

        // Database role memberships (across all user databases)
        var dbNames = (await conn.QueryAsync<string>(
            "SELECT name FROM sys.databases WHERE state = 0 AND database_id > 4")).ToList();

        foreach (var db in dbNames)
        {
            try
            {
                var safeDb = db.Replace("]", "]]");
                var dbSql = string.Concat(
                    "USE [", safeDb, "]; ",
                    "SELECT dp.name AS PrincipalName, dp.type_desc AS PrincipalType, ",
                    "CONVERT(NVARCHAR(200), dp.sid, 1) AS PrincipalSid, r.name AS RoleName ",
                    "FROM sys.database_role_members drm ",
                    "JOIN sys.database_principals dp ON drm.member_principal_id = dp.principal_id ",
                    "JOIN sys.database_principals r ON drm.role_principal_id = r.principal_id ",
                    "WHERE dp.name NOT IN ('dbo','guest','INFORMATION_SCHEMA','sys') AND r.name != 'public'");

                foreach (var row in await conn.QueryAsync<dynamic>(dbSql))
                {
                    string roleName = row.RoleName;
                    permissions.Add(new SqlServerPermission
                    {
                        SqlServerInventoryId = serverId,
                        PrincipalName = row.PrincipalName,
                        PrincipalType = MapDbPrincipalType((string)row.PrincipalType),
                        PrincipalSid = row.PrincipalSid,
                        PermissionScope = "Database",
                        DatabaseName = db,
                        PermissionName = roleName,
                        PermissionClass = "ROLE_MEMBERSHIP",
                        GrantState = "GRANT",
                        IsPrivileged = privileged.Contains(roleName),
                        RiskLevel = privileged.Contains(roleName) ? "High" : "Low"
                    });
                }
            }
            catch { /* Skip inaccessible databases */ }
        }

        return permissions;
    }

    private static string MapPrincipalType(string typeDesc) => typeDesc switch
    {
        "SQL_LOGIN" => "SqlLogin",
        "WINDOWS_LOGIN" => "WindowsLogin",
        "WINDOWS_GROUP" => "WindowsGroup",
        "SERVER_ROLE" => "ServerRole",
        _ => typeDesc
    };

    private static string MapDbPrincipalType(string typeDesc) => typeDesc switch
    {
        "SQL_USER" => "DatabaseUser",
        "WINDOWS_USER" => "WindowsLogin",
        "WINDOWS_GROUP" => "WindowsGroup",
        "DATABASE_ROLE" => "DatabaseRole",
        _ => typeDesc
    };

    // ────── Windows Impersonation for Cross-Domain / Alternate Credentials ──────

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool LogonUser(
        string lpszUsername, string? lpszDomain, string lpszPassword,
        int dwLogonType, int dwLogonProvider, out IntPtr phToken);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr hObject);

    private const int LOGON32_LOGON_NEW_CREDENTIALS = 9; // Best for cross-domain / network
    private const int LOGON32_PROVIDER_WINNT50 = 3;

    [SupportedOSPlatform("windows")]
    public async Task<SqlDirectScanResult> ScanAsWindowsUserAsync(
        string connectionString, string windowsUsername, string windowsPassword,
        bool persistForRescan = true, CancellationToken ct = default)
    {
        // Split DOMAIN\user or user@domain.local into parts
        string username = windowsUsername;
        string? domain = null;
        if (windowsUsername.Contains('\\'))
        {
            var parts = windowsUsername.Split('\\', 2);
            domain = parts[0];
            username = parts[1];
        }
        else if (windowsUsername.Contains('@'))
        {
            // UPN format — leave as-is, LogonUser handles it with null domain
            username = windowsUsername;
        }

        IntPtr tokenHandle = IntPtr.Zero;
        try
        {
            bool success = LogonUser(username, domain, windowsPassword,
                LOGON32_LOGON_NEW_CREDENTIALS, LOGON32_PROVIDER_WINNT50, out tokenHandle);

            if (!success)
            {
                var err = Marshal.GetLastWin32Error();
                return new SqlDirectScanResult
                {
                    ErrorMessage = string.Concat("Windows LogonUser failed (error ", err.ToString(),
                        "). Check the account name and password.")
                };
            }

            // Run the scan under the impersonated identity
            using var identity = new WindowsIdentity(tokenHandle);
            SqlDirectScanResult? result = null;
            await Task.Run(async () =>
            {
                await WindowsIdentity.RunImpersonated(identity.AccessToken, async () =>
                {
                    result = await ScanWithConnectionStringAsync(connectionString, persistForRescan, ct);
                });
            }, ct);

            return result ?? new SqlDirectScanResult { ErrorMessage = "Scan did not return a result" };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ScanAsWindowsUser failed for {User}", windowsUsername);
            return new SqlDirectScanResult
            {
                ErrorMessage = string.Concat("Impersonation scan failed: ", ex.Message)
            };
        }
        finally
        {
            if (tokenHandle != IntPtr.Zero) CloseHandle(tokenHandle);
        }
    }
}
