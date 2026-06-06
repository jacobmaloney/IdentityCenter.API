# IdentityCenter.Api Publish Script (Windows / PowerShell)
# Produces a self-contained PUBLISH FOLDER deploy (no Docker required).
#
# DEFAULT: framework-dependent (smaller artifact; requires the ASP.NET Core 8
#          Runtime installed on the target server). Run with:
#               .\publish.ps1
#          Deploy: copy .\publish to the server, then run:
#               dotnet IdentityCenter.API.dll        (binds http://localhost:5062)
#
# RUNTIME-INCLUDED (no .NET install needed on server): pass -SelfContained
#               .\publish.ps1 -SelfContained
#          This emits a win-x64 self-contained folder. Deploy: copy .\publish
#          to the server, then run the native host:
#               .\IdentityCenter.API.exe
param(
    [switch]$SelfContained,
    [string]$Output = "./publish",
    [string]$Runtime = "win-x64"
)

$ErrorActionPreference = "Stop"
$ApiProject = "IdentityCenter.API/IdentityCenter.API.csproj"

Write-Host "`nPublishing IdentityCenter.Api -> $Output" -ForegroundColor Cyan
Write-Host ("-" * 50) -ForegroundColor Cyan

if (Test-Path $Output) { Remove-Item $Output -Recurse -Force }

if ($SelfContained) {
    Write-Host "Mode: SELF-CONTAINED ($Runtime) — runtime included, larger folder." -ForegroundColor Yellow
    dotnet publish $ApiProject -c Release -o $Output --self-contained true -r $Runtime
}
else {
    Write-Host "Mode: FRAMEWORK-DEPENDENT — requires ASP.NET Core 8 Runtime on server." -ForegroundColor Yellow
    dotnet publish $ApiProject -c Release -o $Output --self-contained false
}

if ($LASTEXITCODE -ne 0) { Write-Host "Publish failed!" -ForegroundColor Red; exit 1 }

Write-Host "`nPublish complete: $Output" -ForegroundColor Green
Write-Host "Deploy: copy the folder to the server and run:" -ForegroundColor Green
if ($SelfContained) {
    Write-Host "  .\IdentityCenter.API.exe" -ForegroundColor Green
} else {
    Write-Host "  dotnet IdentityCenter.API.dll" -ForegroundColor Green
}
Write-Host "Listens on http://localhost:5062  (Swagger at /swagger)" -ForegroundColor Green
Write-Host "NOTE: configure ConnectionStrings:DefaultConnection on the server (env var or user-secrets)." -ForegroundColor Yellow
Write-Host "      To decrypt an 'enc:' connection string, the DataProtection keyring at" -ForegroundColor Yellow
Write-Host "      C:\ProgramData\IdentityCenter\Keys (app name 'IdentityCenter') MUST be present." -ForegroundColor Yellow
