<#
.SYNOPSIS
    Incremental build + deploy loop for MAL+ MAUI (Android).

.DESCRIPTION
    Replaces the previous `dotnet build -t:Rebuild` + manual `adb install` cycle.

    The build is always INCREMENTAL. Never pass -t:Rebuild here: Rebuild discards
    all incremental state and regenerates ~4000 files on every cycle.

    Two deploy modes:
      default    - assemblies embedded in the APK (EmbedAssembliesIntoApk=true,
                  which MALPlus.csproj sets for Debug). Plain `adb install -r`.
      -FastDeploy - passes -p:EmbedAssembliesIntoApk=false and delegates install +
                  launch to the `Run` target (real fast deployment).
                  OPT-IN ONLY: MALPlus.csproj documents that a non-embedded Debug
                  APK can die with a native SIGABRT at startup. Keep the default
                  unless you are actively debugging the deploy path.

.PARAMETER Device
    adb target. Over WiFi in this setup, so it may need reconnecting each session.

.PARAMETER FastDeploy
    Opt in to real fast deployment (smaller APK, assemblies pushed by adb).
    Installs and launches through the `Run` target; the manual install step is
    skipped because there is no self-contained APK to push.

.EXAMPLE
    .\build_and_deploy.ps1
    .\build_and_deploy.ps1 -SkipBuild
    .\build_and_deploy.ps1 -FastDeploy
#>
[CmdletBinding()]
param(
    [string]$Device = "192.168.1.201:35695",
    [string]$Configuration = "Debug",
    [int]$DeviceTimeoutSeconds = 30,
    [switch]$SkipBuild,
    [switch]$SkipInstall,
    [switch]$NoLaunch,
    [switch]$FastDeploy
)

# adb writes informational text to stderr (e.g. `monkey` echoes its parsed args),
# which "Stop" would turn into a terminating NativeCommandError mid-script. Every
# step below checks $LASTEXITCODE explicitly instead.
$ErrorActionPreference = "Continue"

$Root = $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($Root)) {
    $Root = (Get-Location).Path
}

# Portable toolchain: this repo pins the standalone .NET 11 RC SDK, not the VS one.
$env:DOTNET_ROOT = "D:\Descargas\dotnet11"
$env:JAVA_HOME = "D:\Descargas\jdk21\jdk-21.0.12.1+1"
$env:ANDROID_HOME = "D:\Descargas\android-sdk"
$env:ANDROID_SDK_ROOT = $env:ANDROID_HOME
$env:PATH = "$env:DOTNET_ROOT;$env:JAVA_HOME\bin;$env:ANDROID_HOME\platform-tools;$env:PATH"

$dotnet = Join-Path $env:DOTNET_ROOT "dotnet.exe"
$adb = Join-Path $env:ANDROID_HOME "platform-tools\adb.exe"
$project = Join-Path $Root "MALPlus\MALPlus.csproj"
$package = "com.idarkalex.malplus.maui11"
$apkDir = Join-Path $Root "MALPlus\bin\Debug\net11.0-android37.0"

function Format-Duration([double]$seconds) {
    if ($seconds -lt 60) {
        return "{0:N1}s" -f $seconds
    }
    return "{0:N0}s ({1:N1}m)" -f $seconds, ($seconds / 60)
}

function Assert-Tool([string]$path, [string]$name) {
    if (-not (Test-Path -LiteralPath $path)) {
        throw "$name not found at $path"
    }
}

Assert-Tool $dotnet "dotnet"
Assert-Tool $adb "adb"
Assert-Tool $project "MALPlus.csproj"

$total = [System.Diagnostics.Stopwatch]::StartNew()

Write-Host ""
Write-Host "MAL+ MAUI dev loop" -ForegroundColor Cyan
Write-Host "  root       : $Root"
Write-Host "  config     : $Configuration"
Write-Host "  device     : $Device"
Write-Host "  fastDeploy : $FastDeploy"

Write-Host ""
Write-Host "[1/3] device" -ForegroundColor Yellow
$step = [System.Diagnostics.Stopwatch]::StartNew()
& $adb connect $Device 2>&1 | Out-Null

# Bounded wait instead of `adb wait-for-device`, which blocks forever when the
# wireless-debugging port changed and the phone has to be re-paired first.
$deadline = (Get-Date).AddSeconds($DeviceTimeoutSeconds)
$state = ""
while ((Get-Date) -lt $deadline) {
    $state = (& $adb -s $Device get-state 2>&1 | Out-String).Trim()
    if ($state -eq "device") {
        break
    }
    Start-Sleep -Seconds 2
}

if ($state -ne "device") {
    throw "device '$Device' never reached state 'device' (last: '$state'). The wireless-debugging port changes every time adb restarts on the phone: open Developer options > Wireless debugging and read the current port, then rerun with -Device <ip>:<port>."
}
$step.Stop()
Write-Host ("      ready ({0})" -f (Format-Duration $step.Elapsed.TotalSeconds)) -ForegroundColor DarkGray

$buildArguments = @(
    "build",
    $project,
    "-f", "net11.0-android37.0",
    "-p:Configuration=$Configuration",
    "-p:AndroidSdkDirectory=`"$env:ANDROID_HOME`"",
    "-v:m",
    "--nologo",
    # This repo produces ~90 known warnings (obsolete APIs, CS0618 Frame/DisplayAlert,
    # CS4014 fire-and-forget, MA002). Release/CI is where warnings are meant to be read;
    # flooding the dev loop only hides real errors.
    "-clp:ErrorsOnly"
)

if ($FastDeploy) {
    Write-Host ""
    Write-Host "[2/3] build + fast deploy" -ForegroundColor Yellow
    $step = [System.Diagnostics.Stopwatch]::StartNew()
    # `Run` builds, pushes the assemblies and launches; there is no standalone APK to install.
    $buildArguments += "-t:Run"
    $buildArguments += "-p:EmbedAssembliesIntoApk=false"
    $buildArguments += "-p:AdbTarget=`"-s $Device`""
    & $dotnet @buildArguments
    if ($LASTEXITCODE -ne 0) {
        throw "build / fast deploy failed"
    }
    $step.Stop()
    $total.Stop()
    Write-Host ("      done ({0})" -f (Format-Duration $step.Elapsed.TotalSeconds)) -ForegroundColor DarkGray
    Write-Host ""
    Write-Host ("TOTAL {0}" -f (Format-Duration $total.Elapsed.TotalSeconds)) -ForegroundColor Cyan
    return
}

if ($SkipBuild) {
    Write-Host ""
    Write-Host "[2/3] build SKIPPED" -ForegroundColor Yellow
} else {
    Write-Host ""
    Write-Host "[2/3] build (incremental)" -ForegroundColor Yellow
    $step = [System.Diagnostics.Stopwatch]::StartNew()
    # NOTE: deliberately no -t:Rebuild. Incremental is the whole point of this script.
    & $dotnet @buildArguments
    if ($LASTEXITCODE -ne 0) {
        throw "build failed"
    }
    $step.Stop()
    Write-Host ("      done ({0})" -f (Format-Duration $step.Elapsed.TotalSeconds)) -ForegroundColor DarkGray
}

if ($SkipInstall) {
    Write-Host ""
    Write-Host "[3/3] install SKIPPED" -ForegroundColor Yellow
} else {
    Write-Host ""
    Write-Host "[3/3] install" -ForegroundColor Yellow
    $step = [System.Diagnostics.Stopwatch]::StartNew()

    $apk = Get-ChildItem -Path $apkDir -Recurse -Filter "*.apk" -ErrorAction SilentlyContinue |
        Where-Object { $_.Name -like "*-Signed.apk" } |
        Sort-Object LastWriteTime -Descending |
        Select-Object -First 1

    if (-not $apk) {
        throw "no *-Signed.apk found under $apkDir"
    }

    $sizeMb = [math]::Round($apk.Length / 1MB, 1)
    Write-Host "      $($apk.Name) ($sizeMb MB)"

    # Drop any cached runtime state so the first launch is a real cold start.
    & $adb -s $Device shell am force-stop $package | Out-Null

    $installOutput = & $adb -s $Device install -r $apk.FullName 2>&1
    if ($LASTEXITCODE -ne 0) {
        throw "adb install failed:`n$($installOutput -join [Environment]::NewLine)"
    }
    $step.Stop()
    Write-Host ("      installed ({0})" -f (Format-Duration $step.Elapsed.TotalSeconds)) -ForegroundColor DarkGray

    if (-not $NoLaunch) {
        # `monkey` writes its parsed args to stderr; merge into stdout so a
        # $ErrorActionPreference = "Stop" does not treat it as a fatal error.
        & $adb -s $Device shell monkey -p $package -c android.intent.category.LAUNCHER 1 2>&1 | Out-Null
        Write-Host "      launched" -ForegroundColor DarkGray
    }
}

$total.Stop()
Write-Host ""
Write-Host ("TOTAL {0}" -f (Format-Duration $total.Elapsed.TotalSeconds)) -ForegroundColor Cyan
