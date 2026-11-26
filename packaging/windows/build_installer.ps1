param(
    [string]$Version = "0.7.7"
)

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoDir = Split-Path -Parent (Split-Path -Parent $scriptDir)
$csproj = Join-Path $repoDir "MALPlus\MALPlus.csproj"
$versionSuffix = $Version.Replace('.', '')
$outDir = Join-Path $scriptDir "singlefile_v$versionSuffix"
$iss = Join-Path $scriptDir "MALPlus.iss"

# Find dotnet
$dotnetCandidates = @(
    "D:\Desarrollo\sdks\dotnet11\dotnet.exe",
    "D:\Desarrollo\dotnet11\dotnet.exe",
    (Join-Path $repoDir "..\sdks\dotnet11\dotnet.exe"),
    (Join-Path $repoDir "..\dotnet11\dotnet.exe"),
    "D:\Descargas\dotnet11\dotnet.exe"
)
$dotnet = $null
foreach ($cand in $dotnetCandidates) {
    if (Test-Path $cand) {
        $dotnet = $cand
        break
    }
}
if (-not $dotnet -or -not (Test-Path $dotnet)) {
    $dotnet = "dotnet"
}

# Find Inno Setup compiler
$iscc = "ISCC.exe"
$commonPaths = @(
    "C:\Program Files (x86)\Inno Setup 6\ISCC.exe",
    "C:\Program Files\Inno Setup 6\ISCC.exe",
    "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe"
)
foreach ($p in $commonPaths) {
    if (Test-Path $p) {
        $iscc = $p
        break
    }
}

Write-Host "=== 1. Cleaning old AssemblyInfo in obj ===" -ForegroundColor Cyan
Remove-Item (Join-Path $repoDir "MALPlus\obj\Release\net11.0-windows10.0.19041.0\win-x64\MALPlus.AssemblyInfo.cs") -Force -ErrorAction SilentlyContinue

Write-Host "=== 2. Publishing SingleFile v$Version ===" -ForegroundColor Cyan
& $dotnet publish $csproj -f net11.0-windows10.0.19041.0 -c Release -p:TargetFrameworksOverride=net11.0-windows10.0.19041.0 -p:WindowsPackageType=None -p:SelfContained=true -r win-x64 -p:PublishSingleFile=true -o $outDir

if ($LASTEXITCODE -ne 0) {
    Write-Host "Publish failed with exit code $LASTEXITCODE" -ForegroundColor Red
    exit 1
}

Write-Host "=== 3. Compiling Inno Setup MALPlus-Setup-v$Version.exe ===" -ForegroundColor Cyan
& $iscc "/DMyAppVersion=$Version" "/DAppBuildDir=singlefile_v$versionSuffix" "/DOutputFilename=MALPlus-Setup-v$Version" $iss

if ($LASTEXITCODE -ne 0) {
    Write-Host "Inno Setup compilation failed for v$Version" -ForegroundColor Red
    exit 1
}

Write-Host "=== BUILD AND PACKAGING FINISHED SUCCESSFULLY ===" -ForegroundColor Green
