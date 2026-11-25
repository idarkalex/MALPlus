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
    [string]$Device = "",
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

# --- Toolchain Auto-Discovery ---
# 1. Resolve .NET SDK (prefers .NET 11 preview)
$dotnetCandidates = @(
    "D:\Desarrollo\sdks\dotnet11\dotnet.exe",
    "D:\Desarrollo\dotnet11\dotnet.exe",
    (Join-Path $Root "..\sdks\dotnet11\dotnet.exe"),
    (Join-Path $Root "..\dotnet11\dotnet.exe"),
    "D:\Descargas\dotnet11\dotnet.exe"
)
foreach ($p in $dotnetCandidates) {
    if (Test-Path $p) {
        $env:DOTNET_ROOT = Split-Path $p
        break
    }
}
if (-not $env:DOTNET_ROOT -or -not (Test-Path "$env:DOTNET_ROOT\dotnet.exe")) {
    if ($env:ProgramFiles -and (Test-Path "$env:ProgramFiles\dotnet\dotnet.exe")) {
        $env:DOTNET_ROOT = "$env:ProgramFiles\dotnet"
    } else {
        $cmdDotnet = Get-Command dotnet -ErrorAction SilentlyContinue
        if ($cmdDotnet) {
            $env:DOTNET_ROOT = Split-Path $cmdDotnet.Source
        }
    }
}
$dotnet = if ($env:DOTNET_ROOT) { Join-Path $env:DOTNET_ROOT "dotnet.exe" } else { "dotnet.exe" }

# 2. Resolve JDK 21 (or compatible JDK 17+)
$jdkLocalCandidates = @(
    "D:\Desarrollo\sdks\jdk21\jdk-21.0.12.1+1\bin\javac.exe",
    "D:\Desarrollo\jdk21\jdk-21.0.12.1+1\bin\javac.exe",
    (Join-Path $Root "..\sdks\jdk21\jdk-21.0.12.1+1\bin\javac.exe"),
    (Join-Path $Root "..\jdk21\jdk-21.0.12.1+1\bin\javac.exe"),
    "D:\Descargas\jdk21\jdk-21.0.12.1+1\bin\javac.exe"
)
foreach ($p in $jdkLocalCandidates) {
    if (Test-Path $p) {
        $env:JAVA_HOME = Split-Path (Split-Path $p)
        break
    }
}
if (-not $env:JAVA_HOME -or -not (Test-Path "$env:JAVA_HOME\bin\javac.exe")) {
    $jdkCandidates = @(
        "$env:ProgramFiles\Microsoft\jdk-21*",
        "$env:ProgramFiles\Eclipse Adoptium\jdk-21*",
        "$env:ProgramFiles\Java\jdk-21*",
        "$env:ProgramFiles\Android\Android Studio\jbr",
        "$env:ProgramFiles\Microsoft\jdk-17*",
        "C:\Program Files (x86)\Android\openjdk\jdk-17*"
    )
    foreach ($pattern in $jdkCandidates) {
        $matched = Get-Item $pattern -ErrorAction SilentlyContinue | Select-Object -First 1
        if ($matched -and (Test-Path "$($matched.FullName)\bin\javac.exe")) {
            $env:JAVA_HOME = $matched.FullName
            break
        }
    }
}

# 3. Resolve Android SDK
$androidSdkCandidates = @(
    "D:\Desarrollo\sdks\android-sdk\platform-tools\adb.exe",
    "D:\Desarrollo\android-sdk\platform-tools\adb.exe",
    (Join-Path $Root "..\sdks\android-sdk\platform-tools\adb.exe"),
    (Join-Path $Root "..\android-sdk\platform-tools\adb.exe"),
    "D:\Descargas\android-sdk\platform-tools\adb.exe"
)
foreach ($p in $androidSdkCandidates) {
    if (Test-Path $p) {
        $env:ANDROID_HOME = Split-Path (Split-Path $p)
        break
    }
}
if (-not $env:ANDROID_HOME -or -not (Test-Path "$env:ANDROID_HOME\platform-tools\adb.exe")) {
    if ($env:ANDROID_SDK_ROOT -and (Test-Path "$env:ANDROID_SDK_ROOT\platform-tools\adb.exe")) {
        $env:ANDROID_HOME = $env:ANDROID_SDK_ROOT
    } elseif ($env:LOCALAPPDATA -and (Test-Path "$env:LOCALAPPDATA\Android\Sdk\platform-tools\adb.exe")) {
        $env:ANDROID_HOME = "$env:LOCALAPPDATA\Android\Sdk"
    } elseif (Test-Path "C:\Program Files (x86)\Android\android-sdk\platform-tools\adb.exe") {
        $env:ANDROID_HOME = "C:\Program Files (x86)\Android\android-sdk"
    }
}
$env:ANDROID_SDK_ROOT = $env:ANDROID_HOME

# Add discovered paths to PATH
$pathAdditions = @()
if ($env:DOTNET_ROOT) { $pathAdditions += $env:DOTNET_ROOT }
if ($env:JAVA_HOME) { $pathAdditions += "$env:JAVA_HOME\bin" }
if ($env:ANDROID_HOME) { $pathAdditions += "$env:ANDROID_HOME\platform-tools" }
if ($pathAdditions.Count -gt 0) {
    $env:PATH = ($pathAdditions -join ";") + ";$env:PATH"
}

$adb = if ($env:ANDROID_HOME) { Join-Path $env:ANDROID_HOME "platform-tools\adb.exe" } else { "adb.exe" }
$project = Join-Path $Root "MALPlus\MALPlus.csproj"
$package = "com.idarkalex.malplus"
$apkDir = Join-Path $Root "MALPlus\bin\$Configuration\net11.0-android37.0"

function Format-Duration([double]$seconds) {
    if ($seconds -lt 60) {
        return "{0:N1}s" -f $seconds
    }
    return "{0:N0}s ({1:N1}m)" -f $seconds, ($seconds / 60)
}

function Assert-Tool([string]$path, [string]$name, [string]$helpHint) {
    if (-not (Test-Path -LiteralPath $path) -and -not (Get-Command $path -ErrorAction SilentlyContinue)) {
        throw "$name not found at '$path'. $helpHint (See docs/DEVELOPMENT_AND_BUILD.md for setup instructions)"
    }
}

Assert-Tool $dotnet "dotnet" "Please install .NET 11 SDK or set DOTNET_ROOT."
Assert-Tool $adb "adb" "Please install Android SDK Platform-Tools or set ANDROID_HOME."
Assert-Tool $project "MALPlus.csproj" "Project file not found."

# Auto-detect connected adb device if none specified
if ([string]::IsNullOrWhiteSpace($Device) -and (Test-Path -LiteralPath $adb)) {
    $connectedDevices = @()
    foreach ($line in (& $adb devices 2>$null)) {
        if ($line -match "^(\S+)\s+device$") {
            $connectedDevices += $Matches[1]
        }
    }
    if ($connectedDevices.Count -eq 1) {
        $Device = $connectedDevices[0]
        Write-Host "Auto-detected active device: $Device" -ForegroundColor Green
    } elseif ($connectedDevices.Count -gt 1) {
        $Device = $connectedDevices[0]
        Write-Host "Multiple devices detected, using first: $Device" -ForegroundColor Yellow
    } else {
        # Fallback to local default if no device is connected
        $Device = "192.168.1.201:40115"
    }
}

$total = [System.Diagnostics.Stopwatch]::StartNew()

Write-Host ""
Write-Host "MAL+ MAUI dev loop" -ForegroundColor Cyan
Write-Host "  root       : $Root"
Write-Host "  dotnet     : $dotnet"
Write-Host "  androidSdk : $env:ANDROID_HOME"
Write-Host "  java       : $env:JAVA_HOME"
Write-Host "  config     : $Configuration"
Write-Host "  device     : $Device"
Write-Host "  fastDeploy : $FastDeploy"

if (!$SkipInstall) {
    Write-Host ""
    Write-Host "[1/3] device" -ForegroundColor Yellow
    $step = [System.Diagnostics.Stopwatch]::StartNew()
    & $adb connect $Device 2>&1 | Out-Null

    # Bounded wait instead of `adb wait-for-device`, which blocks forever when the
    # wireless-debugging port changed and the phone has to be re-paired first.
    $deadline = (Get-Date).AddSeconds($DeviceTimeoutSeconds)
    $state = ""
    while ((Get-Date) -lt $deadline) {
        & $adb connect $Device 2>&1 | Out-Null
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
}

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

$releaseKeystore = $null
$keystoreCandidates = @(
    (Join-Path $Root "..\malplus-release.keystore"),
    (Join-Path $Root "malplus-release.keystore"),
    "D:\Desarrollo\malplus-release.keystore",
    "D:\Descargas\malplus-release.keystore"
)
foreach ($ks in $keystoreCandidates) {
    if (Test-Path $ks) {
        $releaseKeystore = $ks
        break
    }
}
if ($releaseKeystore -and (Test-Path $releaseKeystore)) {
    $buildArguments += "-p:AndroidKeyStore=true"
    $buildArguments += "-p:AndroidSigningKeyStore=$releaseKeystore"
    $buildArguments += "-p:AndroidSigningStorePass=MalPlus2026ReleaseKey!"
    $buildArguments += "-p:AndroidSigningKeyAlias=malplus"
    $buildArguments += "-p:AndroidSigningKeyPass=MalPlus2026ReleaseKey!"
}

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
    $releaseKs = $releaseKeystore
    if ($releaseKs -and (Test-Path $releaseKs)) {
        $apksignerBat = "C:\Program Files (x86)\Android\android-sdk\build-tools\35.0.0\apksigner.bat"
        if (-not (Test-Path $apksignerBat)) {
            $found = Get-ChildItem -Path $env:ANDROID_HOME -Recurse -Filter "apksigner.bat" -ErrorAction SilentlyContinue | Select-Object -First 1
            if ($found) { $apksignerBat = $found.FullName }
        }
        if ($apksignerBat -and (Test-Path $apksignerBat)) {
            & $apksignerBat sign --ks $releaseKs --ks-pass "pass:MalPlus2026ReleaseKey!" --ks-key-alias "malplus" --key-pass "pass:MalPlus2026ReleaseKey!" $apk.FullName 2>&1 | Out-Null
        }
    }

    # Drop any cached runtime state so the first launch is a real cold start.
    & $adb -s $Device shell am force-stop $package | Out-Null

    $installOutput = & $adb -s $Device install -r -d $apk.FullName 2>&1
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
