<#
.SYNOPSIS
    Safe deploy of the IdentityCenter API publish folder to the .56 server.
    Mirrors the staged publish output to the remote publish root WITHOUT deleting
    runtime-written directories, then restarts the service and health-checks it.

.DESCRIPTION
    The deploy uses 'robocopy /MIR' so the server exactly matches the local publish
    output -- stale build files are removed. The danger of /MIR is that it ALSO deletes
    server-only directories the running app created at runtime. A prior WebPortal deploy
    wiped a server-local MLModels directory this way. This script excludes runtime dirs
    by FULL REMOTE PATH (/XD) so only the publish-root copies are protected, not
    same-named directories deeper in the tree.

    Runtime-written directories under the API publish root (the /XD exclude set):
      The API writes NOTHING under its own publish root at runtime. Both of its
      filesystem write targets are ABSOLUTE paths OUTSIDE the publish root:
        * Serilog rolling file sink -> C:\ProgramData\IdentityCenter\logs
              (Program.cs resolves SpecialFolder.CommonApplicationData\IdentityCenter\logs;
               the same path is read back by LogFileService for the /admin/logs viewer).
        * DataProtection keyring     -> C:\ProgramData\IdentityCenter\Keys
              (Program.cs: PersistKeysToFileSystem(@"C:\ProgramData\IdentityCenter\Keys")).
      Neither is under the publish root, so /MIR can never touch them.

      The exclude set below is therefore PURELY DEFENSIVE -- conventional names a future
      feature might start writing under the app root. Excluding them now keeps a later
      landmine from arming itself:
        * log       -> if anyone ever points Logging:Directory at a relative "./log"
                       (the WebPortal's sink does exactly that), this protects it.
        * App_Data, uploads, temp -> the conventional names for app-root scratch / upload
                       / cache state. Not present today on the API; excluded pre-emptively.

      NOTE: unlike the WebPortal deploy, there is NO 'MLModels' exclude -- the ML model
      storage is an Analytics/WebPortal concern and does not exist in the API.

    NOT excluded (these are real build outputs and MUST mirror):
      wwwroot, runtimes, refs, Scripts, and the localization culture dirs
      (cs, de, es, fr, it, ja, ko, pl, pt-BR, ru, tr, zh-Hans, zh-Hant).

    DataProtection Keys and the API service logs live OUTSIDE the publish root
    (C:\ProgramData\IdentityCenter\Keys and ...\logs, both absolute) and are never
    touched by this deploy regardless of the exclude set.

.NOTES
    No secrets are stored in this file. Pass the SMB credential via -Credential, or
    -SmbUser/-SmbPassword, or be prompted. ASCII-only on purpose: PowerShell 5.1 reads
    a UTF-8 file as Windows-1252, so any em-dash / smart-quote / box-drawing char would
    mojibake and can break parsing.

.EXAMPLE
    # Real deploy with default targets, prompted for the SMB password:
    .\deploy-api.ps1 -SmbUser "domain\administrator"

.EXAMPLE
    # Dry run -- proves the excludes without stopping the service or copying anything:
    .\deploy-api.ps1 -DryRun

.EXAMPLE
    # Real deploy, explicit credential object:
    $cred = Get-Credential domain\administrator
    .\deploy-api.ps1 -Credential $cred

.EXAMPLE
    # Deploy from a non-default staging publish dir:
    .\deploy-api.ps1 -PublishDir "C:\Users\jacob\source\repos\_deploy-api\publish" -SmbUser "domain\administrator"
#>
[CmdletBinding()]
param(
    [string]$Server      = "192.168.1.56",
    [string]$ServiceName = "IdentityCenterApi",
    [string]$PublishDir  = "C:\Users\jacob\source\repos\IdentityCenter.Api\publish",
    [string]$RemotePath  = "\\192.168.1.56\C$\Software\IdentityCenter.API\publish",
    [int]$Port           = 8080,

    # SMB credentials for the C$ admin share. Provide ONE of: -Credential, or
    # -SmbUser (+ -SmbPassword), else you will be prompted. Nothing is hardcoded.
    [System.Management.Automation.PSCredential]$Credential,
    [string]$SmbUser,
    [string]$SmbPassword,

    # Dry run: robocopy in /L list-only mode, NO service stop/start, NO file changes.
    # Use this to prove the /XD excludes before a real deploy.
    [switch]$DryRun,

    # Seconds to wait for the service health endpoint to return HTTP 200 after start.
    [int]$HealthTimeoutSeconds = 60
)

$ErrorActionPreference = "Stop"

# -- Runtime-written dirs to protect from /MIR delete. The API writes nothing under its
#    publish root today (logs + keyring are absolute, under C:\ProgramData), so this set is
#    DEFENSIVE only. Names are joined to the remote publish root below so the /XD match is
#    the publish-root copy ONLY, by full path. NO MLModels (that is WebPortal/Analytics). --
$RuntimeDirNames = @("log", "App_Data", "uploads", "temp")

function Write-Step  { param([string]$m) Write-Host "==> $m" -ForegroundColor Cyan }
function Write-Ok    { param([string]$m) Write-Host "    OK: $m" -ForegroundColor Green }
function Write-Warn2 { param([string]$m) Write-Host "    WARN: $m" -ForegroundColor Yellow }
function Write-Err2  { param([string]$m) Write-Host "    ERROR: $m" -ForegroundColor Red }

$healthUrl = "http://${Server}:$Port/"

Write-Host ""
Write-Host ("=" * 64) -ForegroundColor Cyan
Write-Host " IdentityCenter API deploy" -ForegroundColor Cyan
Write-Host ("=" * 64) -ForegroundColor Cyan
Write-Host "  Server      : $Server"
Write-Host "  Service     : $ServiceName"
Write-Host "  PublishDir  : $PublishDir"
Write-Host "  RemotePath  : $RemotePath"
Write-Host "  Port / URL  : $Port  ($healthUrl)"
Write-Host "  Mode        : $(if ($DryRun) { 'DRY RUN (no service stop/start, list-only copy)' } else { 'LIVE DEPLOY' })" -ForegroundColor $(if ($DryRun) { 'Yellow' } else { 'Green' })
Write-Host ""

# -- 1. Verify the local publish is truly self-contained ----------------------------
Write-Step "Verifying publish is self-contained"
if (-not (Test-Path $PublishDir)) {
    Write-Err2 "PublishDir not found: $PublishDir"
    Write-Err2 "Publish first with:  .\publish.ps1 -SelfContained   (output ./publish)"
    exit 1
}
if (-not (Test-Path (Join-Path $PublishDir "coreclr.dll"))) {
    Write-Err2 "coreclr.dll NOT found in $PublishDir -- this is not a self-contained publish. ABORTING."
    Write-Err2 "Publish with:  .\publish.ps1 -SelfContained   (or dotnet publish ... --self-contained true -r win-x64)"
    exit 1
}
Write-Ok "coreclr.dll present (self-contained)."

# -- 2. Establish SMB to the C$ admin share -----------------------------------------
$smbRoot = "\\$Server\C$"
Write-Step "Establishing SMB session to $smbRoot"
# Drop any existing mapping so a stale/expired session does not mask a cred problem.
# net use /delete errors when there is NO existing mapping; under -EAP Stop that native
# stderr becomes terminating and would abort the deploy. Relax the preference around it.
$prevEAP = $ErrorActionPreference
$ErrorActionPreference = "Continue"
& net use $smbRoot /delete /y 2>$null | Out-Null
$ErrorActionPreference = $prevEAP

if (-not $Credential) {
    if ($SmbUser -and $SmbPassword) {
        $sec = ConvertTo-SecureString $SmbPassword -AsPlainText -Force
        $Credential = New-Object System.Management.Automation.PSCredential($SmbUser, $sec)
    }
    elseif ($SmbUser) {
        $Credential = Get-Credential -UserName $SmbUser -Message "SMB password for $SmbUser on $Server"
    }
    else {
        $Credential = Get-Credential -Message "SMB credentials for $smbRoot (e.g. domain\administrator)"
    }
}
$plainPwd = $Credential.GetNetworkCredential().Password
$netUser  = $Credential.UserName

# From the SMB connect onward, run inside try/finally so the authenticated C$ mapping is
# ALWAYS torn down and the plaintext password ALWAYS cleared -- on success, on every error
# exit, and on the dry-run exit. A PowerShell 'finally' runs before 'exit' takes effect,
# so the teardown fires on all paths below while the original exit codes are preserved.
try {
& net use $smbRoot $plainPwd /user:$netUser | Out-Null
if ($LASTEXITCODE -ne 0 -or -not (Test-Path $RemotePath)) {
    Write-Err2 "Could not access $RemotePath over SMB. Check credentials / .56 reachability (the VM may have slept -- retry)."
    exit 1
}
Write-Ok "SMB session established; remote publish root reachable."

# -- 3. Build the /XD exclude list as FULL REMOTE PATHS -----------------------------
$xdPaths = $RuntimeDirNames | ForEach-Object { Join-Path $RemotePath $_ }
Write-Step "Runtime dirs excluded from /MIR delete (by full remote path):"
foreach ($p in $xdPaths) {
    $exists = if (Test-Path $p) { "present on server" } else { "not present (defensive)" }
    Write-Host "      $p   [$exists]"
}

# -- 4. Stop the service (skipped on dry run) ---------------------------------------
if (-not $DryRun) {
    Write-Step "Stopping service '$ServiceName' on $Server"
    & sc.exe "\\$Server" stop $ServiceName | Out-Null
    # Poll until STOPPED (or timeout). sc.exe query parsing kept simple/ASCII.
    $stopped = $false
    for ($i = 0; $i -lt 30; $i++) {
        Start-Sleep -Seconds 1
        $q = & sc.exe "\\$Server" query $ServiceName 2>$null
        if ($q -match "STOPPED") { $stopped = $true; break }
    }
    if ($stopped) { Write-Ok "Service stopped." }
    else { Write-Warn2 "Service did not report STOPPED within 30s; continuing (file locks may cause robocopy retries)." }
}
else {
    Write-Warn2 "DryRun: NOT stopping the service."
}

# -- 5. Mirror with /MIR, excluding the runtime dirs --------------------------------
Write-Step "Mirroring publish -> server"
$roArgs = @($PublishDir, $RemotePath, "/MIR", "/NJH", "/NJS", "/NP", "/FP", "/R:2", "/W:2")
$roArgs += "/XD"
$roArgs += $xdPaths
if ($DryRun) { $roArgs += "/L" }

Write-Host "    robocopy $($roArgs -join ' ')" -ForegroundColor DarkGray
& robocopy @roArgs
$roExit = $LASTEXITCODE
# robocopy exit codes 0-7 are success (8+ are failures). /L always reports 'extra' (2) etc.
if ($roExit -ge 8) {
    Write-Err2 "robocopy reported a failure (exit $roExit)."
    if (-not $DryRun) {
        Write-Warn2 "Attempting to restart the service before exiting."
        & sc.exe "\\$Server" start $ServiceName | Out-Null
    }
    exit 1
}
Write-Ok "robocopy completed (exit $roExit)."

if ($DryRun) {
    Write-Host ""
    Write-Host ("=" * 64) -ForegroundColor Yellow
    Write-Host " DRY RUN complete. No service action and no files changed." -ForegroundColor Yellow
    Write-Host " Review the robocopy output above: any line marked '*EXTRA Dir' that is" -ForegroundColor Yellow
    Write-Host " one of the excluded runtime dirs should NOT appear (it is protected)." -ForegroundColor Yellow
    Write-Host ("=" * 64) -ForegroundColor Yellow
    exit 0
}

# -- 6. Start the service -----------------------------------------------------------
Write-Step "Starting service '$ServiceName' on $Server"
& sc.exe "\\$Server" start $ServiceName | Out-Null
Write-Ok "Start command issued."

# -- 7. Poll health -----------------------------------------------------------------
Write-Step "Polling $healthUrl for HTTP 200 (timeout ${HealthTimeoutSeconds}s)"
$healthy = $false
$deadline = (Get-Date).AddSeconds($HealthTimeoutSeconds)
while ((Get-Date) -lt $deadline) {
    try {
        $resp = Invoke-WebRequest -Uri $healthUrl -UseBasicParsing -TimeoutSec 5
        if ($resp.StatusCode -eq 200) { $healthy = $true; break }
    }
    catch {
        # Service still warming up (DB migrate / DI graph). Keep polling.
    }
    Start-Sleep -Seconds 3
}

Write-Host ""
Write-Host ("=" * 64) -ForegroundColor Cyan
if ($healthy) {
    Write-Host " DEPLOY SUCCESS" -ForegroundColor Green
    Write-Host " $ServiceName on $Server responded 200 at $healthUrl" -ForegroundColor Green
    Write-Host ("=" * 64) -ForegroundColor Cyan
    exit 0
}
else {
    Write-Host " DEPLOY FAILED HEALTH CHECK" -ForegroundColor Red
    Write-Host " Files were mirrored and the service was started, but $healthUrl did not" -ForegroundColor Red
    Write-Host " return 200 within ${HealthTimeoutSeconds}s. Check the service + logs on ${Server}:" -ForegroundColor Red
    Write-Host "   sc.exe \\$Server query $ServiceName" -ForegroundColor Red
    Write-Host "   C:\ProgramData\IdentityCenter\logs  (Serilog file sink -- OUTSIDE the publish root)" -ForegroundColor Red
    Write-Host ("=" * 64) -ForegroundColor Cyan
    exit 1
}
}
finally {
    # Always tear down the authenticated C$ mapping and scrub the plaintext password,
    # on every exit path above (success, error, dry-run). net use /delete errors when
    # there is no mapping; relax EAP around just the delete (same pattern as start-of-script).
    $prevEAP = $ErrorActionPreference
    $ErrorActionPreference = "Continue"
    & net use $smbRoot /delete /y 2>$null | Out-Null
    $ErrorActionPreference = $prevEAP

    $plainPwd = $null
    if ($Credential) { $Credential = $null }
}
