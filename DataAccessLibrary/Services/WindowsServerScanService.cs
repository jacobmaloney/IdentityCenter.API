using Common.Encryption;
using DataAccessLibrary.Models;
using DataAccessLibrary.Repositories;
using Logging;
using Microsoft.Extensions.Configuration;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Principal;
using System.Text.Json;

namespace DataAccessLibrary.Services;

public class WindowsServerScanService : IServerScanService
{
    private readonly ISqlLicenseRepository _sqlRepo;
    private readonly ISqlCredentialRepository _credRepo;
    private readonly IServerScanRepository _scanRepo;
    private readonly IEncryptionService _encryption;
    private readonly IGlobalLogger _logger;

    public WindowsServerScanService(
        ISqlLicenseRepository sqlRepo,
        ISqlCredentialRepository credRepo,
        IServerScanRepository scanRepo,
        IEncryptionService encryption,
        IGlobalLogger logger)
    {
        _sqlRepo = sqlRepo;
        _credRepo = credRepo;
        _scanRepo = scanRepo;
        _encryption = encryption;
        _logger = logger;
    }

    public async Task<ServerScanResult> ScanAsync(string hostOrIp, Guid? credentialId = null, CancellationToken ct = default)
    {
        if (!OperatingSystem.IsWindows())
            return new ServerScanResult { ErrorMessage = "WinRM scanning is only available on Windows." };

        // Resolve credential
        SqlServerCredential? cred = null;
        if (credentialId.HasValue)
            cred = await _credRepo.GetByIdAsync(credentialId.Value);
        cred ??= await _credRepo.GetDefaultAsync();

        if (cred != null && cred.AuthType != "WindowsAuth" && cred.AuthType != "WindowsAuthSpecified")
            return new ServerScanResult { ErrorMessage = "WinRM scan requires Windows Authentication credentials (WindowsAuth or WindowsAuthSpecified)." };

        // Find or create server inventory record
        var server = await _sqlRepo.GetServerByNameAsync(hostOrIp)
                     ?? await _sqlRepo.GetServerByIpAsync(hostOrIp);

        if (server == null)
        {
            // Create a new inventory entry for this server
            server = new SqlServerInventory
            {
                ServerName = hostOrIp,
                DiscoveryMethod = "WinRMScan",
                DiscoveryStatus = "Managed",
                LastDiscoveredAt = DateTime.UtcNow,
                CredentialId = cred?.Id
            };
            server.Id = await _sqlRepo.UpsertServerAsync(server);
        }

        return await ExecuteScanAsync(server, cred, ct);
    }

    public async Task<ServerScanResult> RescanAsync(Guid serverId, CancellationToken ct = default)
    {
        if (!OperatingSystem.IsWindows())
            return new ServerScanResult { ErrorMessage = "WinRM scanning is only available on Windows." };

        var server = await _sqlRepo.GetServerAsync(serverId);
        if (server == null)
            return new ServerScanResult { ErrorMessage = "Server not found." };

        SqlServerCredential? cred = null;
        if (server.CredentialId.HasValue)
            cred = await _credRepo.GetByIdAsync(server.CredentialId.Value);
        cred ??= await _credRepo.GetDefaultAsync();

        return await ExecuteScanAsync(server, cred, ct);
    }

    public async Task<ServerScanResult> ScanWithCredentialAsync(string hostOrIp, Guid credentialId, CancellationToken ct = default)
    {
        return await ScanAsync(hostOrIp, credentialId, ct);
    }

    [SupportedOSPlatform("windows")]
    private async Task<ServerScanResult> ExecuteScanAsync(SqlServerInventory server, SqlServerCredential? cred, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        var result = new ServerScanResult { ServerName = server.ServerName, ServerId = server.Id };
        var hostname = server.Fqdn ?? server.IpAddress ?? server.ServerName;

        _logger.LogInformation("WindowsServerScan: Starting WinRM scan of {Host}", hostname);
        await _scanRepo.UpdateWinRmScanStatusAsync(server.Id, "Running", null, null);

        try
        {
            // Build the remote PowerShell script
            var script = BuildScanScript(hostname);
            string? output;

            if (cred?.AuthType == "WindowsAuthSpecified" && !string.IsNullOrEmpty(cred.Username))
            {
                var password = !string.IsNullOrEmpty(cred.EncryptedPassword)
                    ? await _encryption.DecryptAsync(cred.EncryptedPassword) : "";

                output = await RunImpersonatedAsync(hostname, cred.Username, password, script, ct);
                if (cred.Id != Guid.Empty)
                    await _credRepo.MarkUsedAsync(cred.Id);
            }
            else
            {
                // WindowsAuth — use service account identity
                output = await RunPowerShellAsync(script, ct);
                if (cred != null && cred.Id != Guid.Empty)
                    await _credRepo.MarkUsedAsync(cred.Id);
            }

            if (string.IsNullOrWhiteSpace(output))
            {
                result.ErrorMessage = "WinRM scan returned no output. Check WinRM is enabled on the target.";
                sw.Stop();
                await _scanRepo.UpdateWinRmScanStatusAsync(server.Id, "Failed", result.ErrorMessage, (int)sw.ElapsedMilliseconds);
                return result;
            }

            // Parse JSON output
            var scanData = ParseScanOutput(output);
            if (scanData == null)
            {
                result.ErrorMessage = "Failed to parse WinRM scan output.";
                sw.Stop();
                await _scanRepo.UpdateWinRmScanStatusAsync(server.Id, "Failed", result.ErrorMessage, (int)sw.ElapsedMilliseconds);
                return result;
            }

            // Persist local users
            if (scanData.LocalUsers.Count > 0)
            {
                var (inserted, matched) = await _scanRepo.UpsertLocalUsersAsync(server.Id, scanData.LocalUsers);
                result.LocalUsersCollected = scanData.LocalUsers.Count;
                result.LocalAdminsFound = scanData.LocalUsers.Count(u => u.IsLocalAdmin);
                result.AdMatchedUsers = matched;
            }

            // Persist installed products
            if (scanData.InstalledProducts.Count > 0)
            {
                await _scanRepo.UpsertInstalledProductsAsync(server.Id, scanData.InstalledProducts);
                result.ProductsCollected = scanData.InstalledProducts.Count;
            }

            // Update OS info on inventory if available
            if (!string.IsNullOrEmpty(scanData.OsName))
            {
                server.OsName = scanData.OsName;
                server.OsVersion = scanData.OsVersion;
                await _sqlRepo.UpsertServerAsync(server);
            }

            result.Success = true;
            sw.Stop();
            await _scanRepo.UpdateWinRmScanStatusAsync(server.Id, "Success", null, (int)sw.ElapsedMilliseconds);

            _logger.LogInformation(
                "WindowsServerScan: {Host} complete — {Users} users ({Admins} admins), {Products} products, {Matched} AD-matched in {Ms}ms",
                hostname, result.LocalUsersCollected, result.LocalAdminsFound,
                result.ProductsCollected, result.AdMatchedUsers, sw.ElapsedMilliseconds);

            return result;
        }
        catch (Exception ex)
        {
            sw.Stop();
            result.ErrorMessage = ClassifyError(ex);
            _logger.LogError(ex, "WindowsServerScan: Failed for {Host}", hostname);
            await _scanRepo.UpdateWinRmScanStatusAsync(server.Id, "Failed", result.ErrorMessage, (int)sw.ElapsedMilliseconds);
            return result;
        }
    }

    // ────── PowerShell Execution ──────

    private static string BuildScanScript(string hostname)
    {
        // Remote script that collects local users, admins, and installed products as JSON
        return string.Concat(
            "Invoke-Command -ComputerName '", hostname.Replace("'", "''"), "' -ScriptBlock { ",
            "$result = @{ ",
            "  LocalUsers = @(); ",
            "  AdminMembers = @(); ",
            "  Products = @(); ",
            "  OS = $null ",
            "}; ",

            // OS info
            "try { ",
            "  $os = Get-CimInstance Win32_OperatingSystem; ",
            "  $result.OS = @{ Caption = $os.Caption; Version = $os.Version; BuildNumber = $os.BuildNumber } ",
            "} catch { }; ",

            // Local users
            "try { ",
            "  $result.LocalUsers = Get-LocalUser | Select-Object Name, Enabled, SID, ",
            "    @{N='LastLogon';E={$_.LastLogon}} | ForEach-Object { ",
            "    @{ Name = $_.Name; Enabled = $_.Enabled; SID = $_.SID.Value; LastLogon = $_.LastLogon } ",
            "  } ",
            "} catch { }; ",

            // Admin group members
            "try { ",
            "  $result.AdminMembers = Get-LocalGroupMember -Group 'Administrators' | Select-Object Name, ObjectClass, PrincipalSource | ForEach-Object { ",
            "    @{ Name = $_.Name; ObjectClass = $_.ObjectClass; Source = if($_.PrincipalSource){$_.PrincipalSource.ToString()}else{'Unknown'} } ",
            "  } ",
            "} catch { }; ",

            // Installed products (registry-based, faster than Win32_Product)
            "try { ",
            "  $paths = @('HKLM:\\SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Uninstall\\*', ",
            "             'HKLM:\\SOFTWARE\\WOW6432Node\\Microsoft\\Windows\\CurrentVersion\\Uninstall\\*'); ",
            "  $result.Products = Get-ItemProperty $paths -ErrorAction SilentlyContinue | ",
            "    Where-Object { $_.DisplayName -and $_.Publisher -like '*Microsoft*' } | ",
            "    Select-Object DisplayName, DisplayVersion, Publisher, InstallDate, InstallLocation | ",
            "    ForEach-Object { ",
            "      @{ Name = $_.DisplayName; Version = $_.DisplayVersion; Publisher = $_.Publisher; ",
            "         InstallDate = $_.InstallDate; InstallPath = $_.InstallLocation } ",
            "    } ",
            "} catch { }; ",

            "$result | ConvertTo-Json -Depth 3 -Compress ",
            "} -ErrorAction Stop"
        );
    }

    [SupportedOSPlatform("windows")]
    private async Task<string?> RunPowerShellAsync(string script, CancellationToken ct)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = string.Concat("-NoProfile -NonInteractive -ExecutionPolicy Bypass -Command \"", script.Replace("\"", "\\\""), "\""),
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(psi);
        if (process == null) return null;

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromMinutes(3));

        var output = await process.StandardOutput.ReadToEndAsync(cts.Token);
        var errors = await process.StandardError.ReadToEndAsync(cts.Token);
        await process.WaitForExitAsync(cts.Token);

        if (process.ExitCode != 0 && !string.IsNullOrWhiteSpace(errors))
        {
            _logger.LogWarning("WindowsServerScan: PowerShell stderr: {Errors}", errors.Length > 500 ? errors[..500] : errors);
            if (string.IsNullOrWhiteSpace(output))
                throw new InvalidOperationException(errors.Length > 300 ? errors[..300] : errors);
        }

        return output;
    }

    // ────── Windows Impersonation ──────

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool LogonUser(
        string lpszUsername, string? lpszDomain, string lpszPassword,
        int dwLogonType, int dwLogonProvider, out IntPtr phToken);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr hObject);

    private const int LOGON32_LOGON_NEW_CREDENTIALS = 9;
    private const int LOGON32_PROVIDER_WINNT50 = 3;

    [SupportedOSPlatform("windows")]
    private async Task<string?> RunImpersonatedAsync(string hostname, string username, string password, string script, CancellationToken ct)
    {
        string user = username;
        string? domain = null;

        if (username.Contains('\\'))
        {
            var parts = username.Split('\\', 2);
            domain = parts[0];
            user = parts[1];
        }

        IntPtr tokenHandle = IntPtr.Zero;
        try
        {
            bool success = LogonUser(user, domain, password,
                LOGON32_LOGON_NEW_CREDENTIALS, LOGON32_PROVIDER_WINNT50, out tokenHandle);

            if (!success)
            {
                var err = Marshal.GetLastWin32Error();
                throw new InvalidOperationException(string.Concat("Windows LogonUser failed (error ", err.ToString(), "). Check credentials."));
            }

            using var identity = new WindowsIdentity(tokenHandle);
            string? result = null;
            await Task.Run(async () =>
            {
                await WindowsIdentity.RunImpersonated(identity.AccessToken, async () =>
                {
                    result = await RunPowerShellAsync(script, ct);
                });
            }, ct);

            return result;
        }
        finally
        {
            if (tokenHandle != IntPtr.Zero)
                CloseHandle(tokenHandle);
        }
    }

    // ────── Output Parsing ──────

    private ScanParsedData? ParseScanOutput(string output)
    {
        try
        {
            // Find the JSON in the output (may have noise before/after)
            var jsonStart = output.IndexOf('{');
            var jsonEnd = output.LastIndexOf('}');
            if (jsonStart < 0 || jsonEnd < 0 || jsonEnd <= jsonStart) return null;

            var json = output[jsonStart..(jsonEnd + 1)];
            var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var result = new ScanParsedData();

            // OS info
            if (root.TryGetProperty("OS", out var os) && os.ValueKind == JsonValueKind.Object)
            {
                result.OsName = os.TryGetProperty("Caption", out var cap) ? cap.GetString() : null;
                result.OsVersion = os.TryGetProperty("Version", out var ver) ? ver.GetString() : null;
            }

            // Local users
            if (root.TryGetProperty("LocalUsers", out var users) && users.ValueKind == JsonValueKind.Array)
            {
                foreach (var u in users.EnumerateArray())
                {
                    var name = u.TryGetProperty("Name", out var n) ? n.GetString() ?? "" : "";
                    var enabled = u.TryGetProperty("Enabled", out var e) && e.ValueKind == JsonValueKind.True;
                    var sid = u.TryGetProperty("SID", out var s) ? s.GetString() : null;

                    result.LocalUsers.Add(new ServerLocalUser
                    {
                        AccountName = name,
                        AccountType = "LocalUser",
                        IsDisabled = !enabled,
                        SID = sid
                    });
                }
            }

            // Admin members (mark matching local users as admin, add domain accounts)
            if (root.TryGetProperty("AdminMembers", out var admins) && admins.ValueKind == JsonValueKind.Array)
            {
                foreach (var a in admins.EnumerateArray())
                {
                    var name = a.TryGetProperty("Name", out var n) ? n.GetString() ?? "" : "";
                    var objClass = a.TryGetProperty("ObjectClass", out var oc) ? oc.GetString() : "User";
                    var source = a.TryGetProperty("Source", out var src) ? src.GetString() : "Unknown";

                    // Check if this admin is already in local users list
                    var shortName = name.Contains('\\') ? name.Split('\\').Last() : name;
                    var existing = result.LocalUsers.FirstOrDefault(u =>
                        u.AccountName.Equals(shortName, StringComparison.OrdinalIgnoreCase));

                    if (existing != null)
                    {
                        existing.IsLocalAdmin = true;
                    }
                    else
                    {
                        // Domain account in Administrators group
                        var accountType = source == "Local" ? "LocalUser"
                            : objClass == "Group" ? "DomainGroup" : "DomainUser";

                        result.LocalUsers.Add(new ServerLocalUser
                        {
                            AccountName = name,
                            AccountType = accountType,
                            GroupName = "Administrators",
                            IsLocalAdmin = true
                        });
                    }
                }
            }

            // Installed products
            if (root.TryGetProperty("Products", out var products) && products.ValueKind == JsonValueKind.Array)
            {
                foreach (var p in products.EnumerateArray())
                {
                    var prodName = p.TryGetProperty("Name", out var pn) ? pn.GetString() ?? "" : "";
                    var version = p.TryGetProperty("Version", out var pv) ? pv.GetString() : null;
                    var publisher = p.TryGetProperty("Publisher", out var pp) ? pp.GetString() : null;
                    var installPath = p.TryGetProperty("InstallPath", out var ip) ? ip.GetString() : null;

                    var category = CategorizeProduct(prodName);

                    result.InstalledProducts.Add(new ServerInstalledProduct
                    {
                        ProductName = prodName,
                        ProductVersion = version,
                        ProductCategory = category,
                        Publisher = publisher,
                        InstallPath = installPath,
                        IsLicensable = category != "Other"
                    });
                }
            }

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "WindowsServerScan: Failed to parse scan output");
            return null;
        }
    }

    private static string CategorizeProduct(string productName)
    {
        if (productName.Contains("SQL Server", StringComparison.OrdinalIgnoreCase))
            return "SQLServer";
        if (productName.Contains("Windows Server", StringComparison.OrdinalIgnoreCase))
            return "WindowsServer";
        if (productName.Contains("Office", StringComparison.OrdinalIgnoreCase)
            || productName.Contains("Microsoft 365", StringComparison.OrdinalIgnoreCase))
            return "Office";
        return "Other";
    }

    private static string ClassifyError(Exception ex)
    {
        var msg = ex.Message;
        if (msg.Contains("WinRM", StringComparison.OrdinalIgnoreCase)
            || msg.Contains("WSMan", StringComparison.OrdinalIgnoreCase)
            || msg.Contains("cannot connect", StringComparison.OrdinalIgnoreCase))
            return "WinRM is not enabled or reachable on this server. Run 'Enable-PSRemoting -Force' on the target, or check port 5985/5986.";

        if (msg.Contains("Access is denied", StringComparison.OrdinalIgnoreCase)
            || msg.Contains("access denied", StringComparison.OrdinalIgnoreCase)
            || msg.Contains("LogonUser failed", StringComparison.OrdinalIgnoreCase))
            return "Access denied. Ensure the credential has local administrator rights on the target server.";

        if (msg.Contains("timed out", StringComparison.OrdinalIgnoreCase)
            || msg.Contains("timeout", StringComparison.OrdinalIgnoreCase))
            return "Connection timed out. Check hostname/IP and ensure the server is reachable.";

        return string.Concat("Scan failed: ", msg.Length > 300 ? msg[..300] : msg);
    }

    private class ScanParsedData
    {
        public string? OsName { get; set; }
        public string? OsVersion { get; set; }
        public List<ServerLocalUser> LocalUsers { get; set; } = new();
        public List<ServerInstalledProduct> InstalledProducts { get; set; } = new();
    }
}
