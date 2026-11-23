# Navigation and Back Stack Automated Verification Protocol for MAL+ MAUI
param(
    [string]$Device = "192.168.1.201:35917",
    [string]$OutputDir = "D:\Descargas\MALPlus-MAUI10\test_results"
)

$ErrorActionPreference = "Stop"

if (-not (Test-Path $OutputDir)) {
    New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null
}

function Log-Step($msg) {
    $ts = Get-Date -Format "HH:mm:ss"
    Write-Host "[$ts] $msg" -ForegroundColor Cyan
}

function Tap-Screen($x, $y, $desc) {
    Log-Step "TAP ($x, $y) -> $desc"
    adb -s $Device shell input tap $x $y
    Start-Sleep -Milliseconds 1200
}

function Press-Back($desc) {
    Log-Step "BACK KEY (Keyevent 4) -> $desc"
    adb -s $Device shell input keyevent 4
    Start-Sleep -Milliseconds 1200
}

function Capture-State($name) {
    $pngPath = Join-Path $OutputDir "$name.png"
    adb -s $Device exec-out screencap -p > $pngPath
    Log-Step "Saved screenshot: $name.png"
    return $pngPath
}

Log-Step "=== INICIANDO PROTOCOLO AUTOMATIZADO DE VERIFICACION DE NAVEGACION ==="

# Connect if needed
Log-Step "Conectando ADB a $Device..."
adb connect $Device
Start-Sleep -Seconds 1

# WAKE UP & UNLOCK
adb -s $Device shell input keyevent 224
adb -s $Device shell input keyevent 82
Start-Sleep -Seconds 1

# Launch MAL+ App
Log-Step "Lanzando com.idarkalex.malplus..."
adb -s $Device shell am start -n com.idarkalex.malplus/crc649cbfe6b852dbb03f.MainActivity
Start-Sleep -Seconds 3

# SUITE 1: TAB SWITCHING (Discover -> Anime -> Manga -> More -> Discover -> Anime)
Log-Step "--- SUITE 1: Ciclo de Pestañas Principales (Tab Switching) ---"
Tap-Screen 450 2600 "Pestaña Anime"
Capture-State "suite1_1_anime"

Tap-Screen 760 2600 "Pestaña Manga"
Capture-State "suite1_2_manga"

Tap-Screen 1070 2600 "Pestaña More"
Capture-State "suite1_3_more"

Tap-Screen 150 2600 "Pestaña Discover"
Capture-State "suite1_4_discover"

Tap-Screen 450 2600 "Pestaña Anime (Retorno)"
Capture-State "suite1_5_anime_return"

Tap-Screen 760 2600 "Pestaña Manga (Retorno)"
Capture-State "suite1_6_manga_return"

# SUITE 2: NAVEGACION NIVEL 1 Y BACK (Anime -> Details -> Back -> Anime)
Log-Step "--- SUITE 2: Navegación Nivel 1 y Retroceso ---"
Tap-Screen 450 2600 "Volver a Anime"
Start-Sleep -Seconds 1
Tap-Screen 300 700 "Tocar primer Anime de la lista"
Start-Sleep -Seconds 2
Capture-State "suite2_1_animedetails"

Press-Back "Pulsar Atrás desde AnimeDetails"
Start-Sleep -Seconds 1
Capture-State "suite2_2_anime_restored"

# SUITE 3: CAMBIO DE PESTAÑA CON SUBPAGINA ABIERTA (Stack Flush Test)
Log-Step "--- SUITE 3: Interrupción de Subpágina cambiando de Pestaña ---"
Tap-Screen 300 700 "Abrir AnimeDetails de nuevo"
Start-Sleep -Seconds 2
Capture-State "suite3_1_details_open"

Tap-Screen 760 2600 "Pulsar pestaña Manga directamente desde Details"
Start-Sleep -Seconds 2
Capture-State "suite3_2_manga_from_details"

Tap-Screen 450 2600 "Pulsar pestaña Anime para volver"
Start-Sleep -Seconds 2
Capture-State "suite3_3_anime_recovered"

Log-Step "=== PROTOCOLO COMPLETADO CON ÉXITO ==="
