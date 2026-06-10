<#
.SYNOPSIS
    Stops and removes the IdentityCenter.Api Windows Service installed by install-service.ps1.

.NOTES
    Run from an ELEVATED PowerShell. Removes the service registration (including its
    registry environment block). Does NOT touch the publish folder, the database, or
    the DataProtection keyring.

.EXAMPLE
    .\uninstall-service.ps1
    .\uninstall-service.ps1 -ServiceName IdentityCenterApi
#>
[CmdletBinding()]
param(
    [string]$ServiceName = "IdentityCenterApi"
)

$ErrorActionPreference = "Stop"

$id = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = New-Object Security.Principal.WindowsPrincipal($id)
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw "This script must be run from an ELEVATED (Administrator) PowerShell."
}

$svc = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if (-not $svc) {
    Write-Host "Service '$ServiceName' is not installed. Nothing to do." -ForegroundColor Yellow
    return
}

if ($svc.Status -ne 'Stopped') {
    Write-Host "Stopping '$ServiceName'..." -ForegroundColor Cyan
    Stop-Service -Name $ServiceName -Force
    # Wait briefly for a clean stop before deletion.
    $svc.WaitForStatus('Stopped', '00:00:30')
}

Write-Host "Removing '$ServiceName'..." -ForegroundColor Cyan
sc.exe delete $ServiceName | Out-Null
Start-Sleep -Seconds 1

if (Get-Service -Name $ServiceName -ErrorAction SilentlyContinue) {
    Write-Host "Service still present - Windows may be finalizing deletion. Re-check with: Get-Service $ServiceName" -ForegroundColor Yellow
} else {
    Write-Host "Service '$ServiceName' removed." -ForegroundColor Green
}
