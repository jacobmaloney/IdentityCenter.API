# IdentityCenter.Api Run Script (Windows / PowerShell)
# Runs the REST API locally on http://localhost:5062 (Swagger at /swagger).
param(
    [Parameter(Position = 0)]
    [ValidateSet("build", "run", "clean")]
    [string]$Command = "run",

    [switch]$Release,
    [switch]$Watch
)

$ErrorActionPreference = "Stop"
$ApiProject = "IdentityCenter.API/IdentityCenter.API.csproj"

function Write-Header {
    param([string]$Text)
    Write-Host "`n$Text" -ForegroundColor Cyan
    Write-Host ("-" * $Text.Length) -ForegroundColor Cyan
}

function Build-Solution {
    Write-Header "Building IdentityCenter.Api"
    $configuration = if ($Release) { "Release" } else { "Debug" }
    dotnet build IdentityCenter.Api.sln -c $configuration
    if ($LASTEXITCODE -ne 0) { Write-Host "Build failed!" -ForegroundColor Red; exit 1 }
    Write-Host "Build completed successfully!" -ForegroundColor Green
}

function Run-Application {
    Write-Header "Starting IdentityCenter.Api on http://localhost:5062"
    if ($Watch) {
        Write-Host "Starting in watch mode (hot reload enabled)..." -ForegroundColor Yellow
        dotnet watch --project $ApiProject run
    }
    else {
        dotnet run --project $ApiProject
    }
}

function Clean-Solution {
    Write-Header "Cleaning Solution"
    Get-ChildItem -Path . -Include bin,obj -Recurse -Directory | Remove-Item -Recurse -Force
    Write-Host "Clean completed!" -ForegroundColor Green
}

switch ($Command) {
    "build" { Build-Solution }
    "run"   { Build-Solution; Run-Application }
    "clean" { Clean-Solution }
}

Write-Host "`nDone!" -ForegroundColor Green
