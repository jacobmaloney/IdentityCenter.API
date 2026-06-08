<#
.SYNOPSIS
    Registers the PUBLISHED IdentityCenter.Api as a Windows Service: auto-start,
    auto-restart-on-failure, pinned URL + environment, survives reboot.

.DESCRIPTION
    "Deploy to a server and forget about it." This script installs the publish-folder
    output of publish.ps1 as a Windows Service via New-Service, then layers on the bits
    New-Service does not do natively:

      * StartupType = Automatic           (starts at boot)
      * sc.exe failure ... actions= restart/5000  (auto-restart 5s after a crash)
      * a per-service Environment block written to the registry, so the service runs with
        the right ASPNETCORE_ENVIRONMENT, ASPNETCORE_URLS, and connection strings WITHOUT
        relying on a logged-in user's env vars (a Windows service does NOT inherit them).

    It supports BOTH publish modes produced by publish.ps1:
      * Self-contained  -> launches  <PublishPath>\IdentityCenter.API.exe
      * Framework-dep.   -> launches  dotnet "<PublishPath>\IdentityCenter.API.dll"
    Auto-detected from the publish folder; override with -UseDotnet.

.NOTES
    Run from an ELEVATED PowerShell (service control requires admin).
    NEVER pass real secrets on a shared command line in a way that lands in history;
    prefer -UseMachineEnvVars (set the connection strings as MACHINE env vars yourself,
    once) OR an appsettings.Production.json placed next to the exe (kept out of git).
    This script does not hardcode any secret.

.EXAMPLE
    # Framework-dependent, connection strings supplied to the service registry env block:
    .\install-service.ps1 -DefaultConnection "Data Source=...;Initial Catalog=...;User ID=sa;Password=..." `
                          -ControlPlaneConnection "Data Source=...;Initial Catalog=IdentityCenterControlPlane;..."

.EXAMPLE
    # Self-contained, bound to the network (all interfaces), connection strings set as machine env vars already:
    .\install-service.ps1 -PublishPath .\publish -BindAll -UseMachineEnvVars
#>
[CmdletBinding()]
param(
    [string]$PublishPath = ".\publish",
    [string]$ServiceName = "IdentityCenterApi",
    [string]$DisplayName = "IdentityCenter API",
    [int]$Port = 5062,
    [string]$Environment = "Production",

    # URL binding: default loopback-only (safe). -BindAll listens on every interface
    # (http://0.0.0.0:<port>) so other machines (e.g. Conduit on another box) can reach it.
    # If you open it to the network you MUST also open the Windows Firewall port (see note at end).
    [switch]$BindAll,

    # Connection strings. Leave empty and use -UseMachineEnvVars (or appsettings.Production.json)
    # if you do not want them written into the service's registry environment block.
    [string]$DefaultConnection = "",
    [string]$ControlPlaneConnection = "",

    # Do NOT write connection strings into the service env block; rely on MACHINE env vars
    # (ConnectionStrings__DefaultConnection / ConnectionStrings__ControlPlane) or
    # appsettings.Production.json next to the exe instead.
    [switch]$UseMachineEnvVars,

    # Force framework-dependent launch (dotnet <dll>) even if an .exe is present.
    [switch]$UseDotnet
)

$ErrorActionPreference = "Stop"

function Assert-Admin {
    $id = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = New-Object Security.Principal.WindowsPrincipal($id)
    if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
        throw "This script must be run from an ELEVATED (Administrator) PowerShell."
    }
}

Assert-Admin

# Resolve publish path to an absolute path (services need a fully-qualified binPath).
$PublishPath = (Resolve-Path $PublishPath).Path
$exe = Join-Path $PublishPath "IdentityCenter.API.exe"
$dll = Join-Path $PublishPath "IdentityCenter.API.dll"

# Decide how to launch: self-contained .exe (preferred) vs framework-dependent dotnet <dll>.
if ((Test-Path $exe) -and -not $UseDotnet) {
    $binPath = "`"$exe`""
    $mode = "self-contained exe"
}
elseif (Test-Path $dll) {
    $dotnet = (Get-Command dotnet -ErrorAction SilentlyContinue).Source
    if (-not $dotnet) { throw "dotnet not found on PATH but publish is framework-dependent. Install the ASP.NET Core 8 Runtime or publish -SelfContained." }
    $binPath = "`"$dotnet`" `"$dll`""
    $mode = "framework-dependent (dotnet <dll>)"
}
else {
    throw "Neither IdentityCenter.API.exe nor IdentityCenter.API.dll found in '$PublishPath'. Run publish.ps1 first."
}

Write-Host "Installing service '$ServiceName'" -ForegroundColor Cyan
Write-Host "  Launch mode : $mode"
Write-Host "  binPath     : $binPath"
Write-Host "  Environment : $Environment"

# Compute the listen URL. localhost by default (only reachable from the server itself);
# -BindAll opens it to the network.
$urls = if ($BindAll) { "http://0.0.0.0:$Port" } else { "http://localhost:$Port" }
Write-Host "  ASPNETCORE_URLS : $urls"
if ($BindAll) {
    Write-Host "  (network-bound: remember to open the firewall port $Port — see end of script output)" -ForegroundColor Yellow
}

# Remove any prior instance so re-running is idempotent.
$existing = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if ($existing) {
    Write-Host "  Existing service found — stopping and removing first." -ForegroundColor Yellow
    if ($existing.Status -ne 'Stopped') { Stop-Service -Name $ServiceName -Force -ErrorAction SilentlyContinue }
    sc.exe delete $ServiceName | Out-Null
    Start-Sleep -Seconds 1
}

# Create the service. -StartupType Automatic = starts at boot.
New-Service -Name $ServiceName `
            -BinaryPathName $binPath `
            -DisplayName $DisplayName `
            -Description "IdentityCenter REST API (sync sink + integrations). Auto-start, auto-restart." `
            -StartupType Automatic | Out-Null

# ── Environment block ────────────────────────────────────────────────────────
# A Windows service does NOT inherit a user's process env vars, so we write the
# environment the host needs into the service's own registry Environment value
# (REG_MULTI_SZ at HKLM\SYSTEM\CurrentControlSet\Services\<name>\Environment).
# This is the most reliable way to feed ASPNETCORE_* + connection strings to a service.
$envEntries = @(
    "ASPNETCORE_ENVIRONMENT=$Environment",
    "ASPNETCORE_URLS=$urls"
)

if (-not $UseMachineEnvVars) {
    # .NET configuration maps the __ (double-underscore) env-var form to nested keys:
    #   ConnectionStrings__DefaultConnection -> ConnectionStrings:DefaultConnection
    if ($DefaultConnection)      { $envEntries += "ConnectionStrings__DefaultConnection=$DefaultConnection" }
    if ($ControlPlaneConnection) { $envEntries += "ConnectionStrings__ControlPlane=$ControlPlaneConnection" }
}

$svcKey = "HKLM:\SYSTEM\CurrentControlSet\Services\$ServiceName"
New-ItemProperty -Path $svcKey -Name "Environment" -Value $envEntries -PropertyType MultiString -Force | Out-Null
Write-Host "  Wrote service environment block ($($envEntries.Count) entries)." -ForegroundColor Green
if ($UseMachineEnvVars) {
    Write-Host "  NOTE: connection strings NOT written. Set them as MACHINE env vars:" -ForegroundColor Yellow
    Write-Host "        [Environment]::SetEnvironmentVariable('ConnectionStrings__DefaultConnection','...','Machine')" -ForegroundColor Yellow
    Write-Host "        [Environment]::SetEnvironmentVariable('ConnectionStrings__ControlPlane','...','Machine')" -ForegroundColor Yellow
    Write-Host "        ...or place appsettings.Production.json next to the exe (kept out of git)." -ForegroundColor Yellow
}
elseif (-not $DefaultConnection) {
    Write-Host "  NOTE: no -DefaultConnection given. The service will look to machine env vars /" -ForegroundColor Yellow
    Write-Host "        appsettings.Production.json for ConnectionStrings:DefaultConnection." -ForegroundColor Yellow
}

# ── Auto-restart on failure ──────────────────────────────────────────────────
# reset= 86400 : the failure counter resets after 1 day of healthy uptime.
# actions= restart/5000 : on each failure, restart after 5000 ms.
sc.exe failure $ServiceName reset= 86400 actions= restart/5000 | Out-Null
Write-Host "  Configured auto-restart on failure (restart after 5s)." -ForegroundColor Green

# Start it now.
Start-Service -Name $ServiceName
$svc = Get-Service -Name $ServiceName
Write-Host "`nService '$ServiceName' is $($svc.Status) (StartType=Automatic)." -ForegroundColor Green
Write-Host "Listening on $urls" -ForegroundColor Green

Write-Host "`n--- Windows Server hand-steps ---" -ForegroundColor Cyan
if ($BindAll) {
    Write-Host "* Open the firewall port so other machines can reach it:" -ForegroundColor Yellow
    Write-Host "    New-NetFirewallRule -DisplayName 'IdentityCenter API $Port' -Direction Inbound -Action Allow -Protocol TCP -LocalPort $Port" -ForegroundColor Yellow
}
Write-Host "* If a connection string is stored 'enc:', copy C:\ProgramData\IdentityCenter\Keys to this server;" -ForegroundColor Yellow
Write-Host "  plaintext / env-var connection strings need no keyring." -ForegroundColor Yellow
Write-Host "* The service runs as LocalSystem by default. If you switch it to a domain account AND use" -ForegroundColor Yellow
Write-Host "  Windows-auth SQL, grant that account DB access. (Lab uses sql-auth 'sa', so N/A there.)" -ForegroundColor Yellow
Write-Host "* Verify: Invoke-WebRequest http://localhost:$Port/  (Swagger is Development-only unless Swagger:EnableInProduction=true)." -ForegroundColor Yellow
