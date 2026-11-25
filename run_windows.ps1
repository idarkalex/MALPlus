<#
.SYNOPSIS
    Launcher script for MAL+ MAUI on Windows Desktop (WinUI 3).

.DESCRIPTION
    Configures the required .NET 11 environment and launches MALPlus.exe.
    Use -Build to perform an incremental build before launching.

.EXAMPLE
    .\run_windows.ps1
    .\run_windows.ps1 -Build
#>
param(
    [switch]$NoBuild,
    [switch]$Build
)

$ErrorActionPreference = "Stop"
$Root = $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($Root)) {
    $Root = (Get-Location).Path
}

$dotnetCandidates = @(
    "D:\Desarrollo\sdks\dotnet11",
    "D:\Desarrollo\dotnet11",
    (Join-Path $Root "..\sdks\dotnet11"),
    (Join-Path $Root "..\dotnet11"),
    "D:\Descargas\dotnet11"
)
$DotnetRoot = $null
foreach ($cand in $dotnetCandidates) {
    if (Test-Path "$cand\dotnet.exe") {
        $DotnetRoot = $cand
        break
    }
}
if ($DotnetRoot -and (Test-Path "$DotnetRoot\dotnet.exe")) {
    $env:DOTNET_ROOT = $DotnetRoot
    $dotnet = "$DotnetRoot\dotnet.exe"
} else {
    $dotnet = "dotnet"
}

$ExePath = Join-Path $Root "MALPlus\bin\Debug\net11.0-windows10.0.19041.0\win-x64\MALPlus.exe"

if (-not $NoBuild -or -not (Test-Path $ExePath)) {
    Write-Host "Building MAL+ for Windows desktop (incremental)..." -ForegroundColor Cyan
    $projectPath = Join-Path $Root "MALPlus\MALPlus.csproj"
    & $dotnet build $projectPath -f net11.0-windows10.0.19041.0 -c Debug -v:q
    if ($LASTEXITCODE -ne 0) {
        Write-Error "Build failed with exit code $LASTEXITCODE."
        exit $LASTEXITCODE
    }
}

Write-Host "Launching MAL+ Windows Desktop ($ExePath)..." -ForegroundColor Green
Start-Process -FilePath $ExePath -WorkingDirectory (Split-Path $ExePath)
