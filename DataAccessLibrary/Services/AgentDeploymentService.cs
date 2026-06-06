using DataAccessLibrary.Repositories;
using Logging;
using Microsoft.Extensions.Configuration;
using System.Diagnostics;
using System.Text;

namespace DataAccessLibrary.Services;

public class AgentDeploymentService : IAgentDeploymentService
{
    private readonly IApiKeyRepository _apiKeyRepo;
    private readonly IConfiguration _configuration;
    private readonly IGlobalLogger _logger;

    public AgentDeploymentService(
        IApiKeyRepository apiKeyRepo,
        IConfiguration configuration,
        IGlobalLogger logger)
    {
        _apiKeyRepo = apiKeyRepo;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<AgentDeploymentResult> GenerateDeploymentAsync(string hostname, string? apiBaseUrlOverride = null, CancellationToken ct = default)
    {
        var result = new AgentDeploymentResult { Hostname = hostname };

        try
        {
            var (keyId, apiKey) = await _apiKeyRepo.CreateApiKeyAsync(
                $"Agent-{hostname}", "Agent", "agent",
                expiresAt: null, createdBy: "AgentDeploymentService");

            // Priority: explicit override (from NavigationManager) > config > fallback
            var apiBaseUrl = apiBaseUrlOverride
                ?? _configuration["AgentDeployment:ApiBaseUrl"]
                ?? _configuration["ApiSettings:BaseUrl"]
                ?? "https://localhost:7048";

            result.Success = true;
            result.ApiKey = apiKey;
            result.AgentId = keyId.ToString();
            result.InstallCommand = BuildInstallCommand(apiBaseUrl, apiKey);

            _logger.LogInformation("AgentDeployment: generated deployment for {Hostname} (keyId: {KeyId})",
                hostname, keyId);
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.ErrorMessage = ex.Message;
            _logger.LogError(ex, "AgentDeployment: failed to generate deployment for {Hostname}", hostname);
        }

        return result;
    }

    public async Task<AgentDeploymentResult> DeployToHostAsync(string hostname, string? apiBaseUrlOverride = null, CancellationToken ct = default)
    {
        var result = await GenerateDeploymentAsync(hostname, apiBaseUrlOverride, ct);
        if (!result.Success) return result;

        try
        {
            _logger.LogInformation("AgentDeployment: deploying to {Hostname} via WinRM...", hostname);

            // Encode the install script as base64 so we can pass it safely
            var installBytes = Encoding.Unicode.GetBytes(result.InstallCommand!);
            var encodedCommand = Convert.ToBase64String(installBytes);

            // Invoke-Command on the remote host with the encoded script
            var remoteCmd = string.Concat(
                "Invoke-Command -ComputerName '", hostname, "' ",
                "-ScriptBlock { $enc = '", encodedCommand, "'; ",
                "$bytes = [Convert]::FromBase64String($enc); ",
                "$script = [System.Text.Encoding]::Unicode.GetString($bytes); ",
                "Invoke-Expression $script }");

            var psi = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = string.Concat(
                    "-NoProfile -NonInteractive -ExecutionPolicy Bypass -Command ",
                    "\"", remoteCmd.Replace("\"", "\\\""), "\""),
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi);
            if (process == null)
            {
                result.Success = false;
                result.ErrorMessage = "Failed to start PowerShell process";
                return result;
            }

            // Timeout after 3 minutes
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromMinutes(3));

            var output = await process.StandardOutput.ReadToEndAsync(cts.Token);
            var errors = await process.StandardError.ReadToEndAsync(cts.Token);
            await process.WaitForExitAsync(cts.Token);

            result.Output = output;

            if (process.ExitCode != 0)
            {
                result.Success = false;
                result.ErrorMessage = string.Concat("Remote deployment failed (exit code ",
                    process.ExitCode.ToString(), "): ", errors);
                _logger.LogWarning("AgentDeployment: WinRM to {Hostname} failed: {Errors}", hostname, errors);
            }
            else
            {
                result.Success = true;
                _logger.LogInformation("AgentDeployment: successfully deployed to {Hostname}", hostname);
            }
        }
        catch (OperationCanceledException)
        {
            result.Success = false;
            result.ErrorMessage = "Deployment timed out after 3 minutes. Check that the target is online and WinRM is enabled.";
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.ErrorMessage = string.Concat("WinRM deployment failed: ", ex.Message);
            _logger.LogError(ex, "AgentDeployment: remote deployment to {Hostname} threw exception", hostname);
        }

        return result;
    }

    /// <summary>
    /// Builds a self-contained PowerShell script that downloads the agent ZIP from the
    /// IdentityCenter server, extracts it, writes the config, and registers a Windows Service.
    /// </summary>
    private string BuildInstallCommand(string apiBaseUrl, string apiKey)
    {
        var escapedUrl = apiBaseUrl.TrimEnd('/').Replace("'", "''");
        var escapedKey = apiKey.Replace("'", "''");

        var sb = new StringBuilder();
        sb.AppendLine("$ErrorActionPreference = 'Stop'");
        sb.AppendLine("$installDir = 'C:\\Program Files\\IdentityCenter\\Agent'");
        sb.Append("$apiUrl = '").Append(escapedUrl).AppendLine("'");
        sb.Append("$apiKey = '").Append(escapedKey).AppendLine("'");
        sb.AppendLine("$serviceName = 'IdentityCenter Agent'");
        sb.AppendLine();
        sb.AppendLine("Write-Host 'IdentityCenter Agent Deployment Starting...'");
        sb.AppendLine();
        sb.AppendLine("# Stop existing service");
        sb.AppendLine("$existing = Get-Service -Name $serviceName -ErrorAction SilentlyContinue");
        sb.AppendLine("if ($existing) { Stop-Service -Name $serviceName -Force -ErrorAction SilentlyContinue; Start-Sleep -Seconds 2 }");
        sb.AppendLine();
        sb.AppendLine("# Create install directory");
        sb.AppendLine("New-Item -ItemType Directory -Path $installDir -Force | Out-Null");
        sb.AppendLine("New-Item -ItemType Directory -Path \"$installDir\\logs\" -Force | Out-Null");
        sb.AppendLine();
        sb.AppendLine("# Download agent binaries (bypass SSL for self-signed)");
        sb.AppendLine("[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12");
        sb.AppendLine("Add-Type @\"");
        sb.AppendLine("using System.Net;");
        sb.AppendLine("using System.Security.Cryptography.X509Certificates;");
        sb.AppendLine("public class TrustAllCertsPolicy : ICertificatePolicy {");
        sb.AppendLine("  public bool CheckValidationResult(ServicePoint sp, X509Certificate cert, WebRequest req, int problem) { return true; }");
        sb.AppendLine("}");
        sb.AppendLine("\"@");
        sb.AppendLine("[System.Net.ServicePointManager]::CertificatePolicy = New-Object TrustAllCertsPolicy");
        sb.AppendLine();
        sb.AppendLine("Write-Host 'Downloading agent package...'");
        sb.AppendLine("$zipPath = \"$env:TEMP\\ICAgent.zip\"");
        sb.AppendLine("Invoke-WebRequest -Uri \"$apiUrl/agent/ICAgent.zip\" -OutFile $zipPath -UseBasicParsing");
        sb.AppendLine();
        sb.AppendLine("Write-Host 'Extracting agent...'");
        sb.AppendLine("Expand-Archive -Path $zipPath -DestinationPath $installDir -Force");
        sb.AppendLine("Remove-Item $zipPath -Force");
        sb.AppendLine();
        sb.AppendLine("# Write configuration");
        sb.AppendLine("$config = @{");
        sb.AppendLine("    Agent = @{");
        sb.AppendLine("        ApiBaseUrl = $apiUrl");
        sb.AppendLine("        ApiKey = $apiKey");
        sb.AppendLine("        AgentId = ''");
        sb.AppendLine("        AgentName = $env:COMPUTERNAME");
        sb.AppendLine("        CollectSqlInventory = $true");
        sb.AppendLine("        CollectComputerInfo = $true");
        sb.AppendLine("        HeartbeatIntervalMinutes = 5");
        sb.AppendLine("        SqlCollectionIntervalMinutes = 60");
        sb.AppendLine("    }");
        sb.AppendLine("}");
        sb.AppendLine("$config | ConvertTo-Json -Depth 5 | Set-Content \"$installDir\\agent-config.json\" -Encoding UTF8");
        sb.AppendLine();
        sb.AppendLine("# Install and start as Windows Service");
        sb.AppendLine("$exe = \"$installDir\\IdentityCenter.Agent.exe\"");
        sb.AppendLine("if (-not (Test-Path $exe)) { throw 'Agent executable not found after extraction' }");
        sb.AppendLine();
        sb.AppendLine("$existingSvc = Get-Service -Name $serviceName -ErrorAction SilentlyContinue");
        sb.AppendLine("if ($existingSvc) {");
        sb.AppendLine("    sc.exe delete \"$serviceName\" | Out-Null");
        sb.AppendLine("    Start-Sleep -Seconds 2");
        sb.AppendLine("}");
        sb.AppendLine();
        sb.AppendLine("New-Service -Name $serviceName -BinaryPathName $exe -DisplayName 'IdentityCenter Agent' -Description 'Collects SQL Server inventory and permissions for IdentityCenter' -StartupType Automatic | Out-Null");
        sb.AppendLine("Start-Service -Name $serviceName");
        sb.AppendLine();
        sb.AppendLine("Write-Host 'IdentityCenter Agent deployed and running on' $env:COMPUTERNAME -ForegroundColor Green");

        return sb.ToString();
    }
}
