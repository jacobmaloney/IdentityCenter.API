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
    Write-Host "Mode: SELF-CONTAINED ($Runtime) - runtime included, larger folder." -ForegroundColor Yellow
    dotnet publish $ApiProject -c Release -o $Output --self-contained true -r $Runtime
}
else {
    Write-Host "Mode: FRAMEWORK-DEPENDENT - requires ASP.NET Core 8 Runtime on server." -ForegroundColor Yellow
    dotnet publish $ApiProject -c Release -o $Output --self-contained false
}

if ($LASTEXITCODE -ne 0) { Write-Host "Publish failed!" -ForegroundColor Red; exit 1 }

Write-Host "`nPublish complete: $Output" -ForegroundColor Green
Write-Host "Run interactively (test): copy the folder to the server and run:" -ForegroundColor Green
if ($SelfContained) {
    Write-Host "  .\IdentityCenter.API.exe" -ForegroundColor Green
} else {
    Write-Host "  dotnet IdentityCenter.API.dll" -ForegroundColor Green
}
Write-Host "Binds http://localhost:5062 out of the box via the 'DefaultUrls' key in appsettings.json" -ForegroundColor Green
Write-Host "  (launchSettings.json is dev-only and is NOT in this publish output; Program.cs applies" -ForegroundColor Green
Write-Host "   DefaultUrls only when ASPNETCORE_URLS is unset, so the env var always overrides it)." -ForegroundColor Green
Write-Host "  Override per-machine with the ASPNETCORE_URLS env var (e.g. http://0.0.0.0:5062 to" -ForegroundColor Green
Write-Host "  listen on all interfaces). Swagger is at /swagger (Development only by default)." -ForegroundColor Green
Write-Host "`nRun as a Windows Service (deploy-and-forget): from this repo on the server, run elevated:" -ForegroundColor Cyan
Write-Host "  .\install-service.ps1 -PublishPath $Output   (auto-start + auto-restart; see README)" -ForegroundColor Cyan
Write-Host "`nNOTE: configure ConnectionStrings:DefaultConnection on the server (env var, the service" -ForegroundColor Yellow
Write-Host "      env block written by install-service.ps1, or appsettings.Production.json)." -ForegroundColor Yellow
Write-Host "      To decrypt an 'enc:' connection string, the DataProtection keyring at" -ForegroundColor Yellow
Write-Host "      C:\ProgramData\IdentityCenter\Keys (app name 'IdentityCenter') MUST be present;" -ForegroundColor Yellow
Write-Host "      plaintext / env-var connection strings need no keyring." -ForegroundColor Yellow
