# Regenera los discover_btn_*.jpg a partir del arte de buttons/.
# Uso:  powershell -File tools\gen_discover_buttons.ps1
#
# El arte llega como PNG 1500x600 (2,5:1) con la capsula recortada sobre fondo
# transparente. Se aplana sobre #051522 (el color de la pagina Discover) y se
# exporta a 620x248, que es justo el ratio que DiscoverTileButton.CapsuleAspect
# usa para derivar su altura. Si el ratio no cuadrase, los capsulos se estirarian.

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$root = Split-Path -Parent $PSScriptRoot
$srcDir = Join-Path $root 'buttons'
$dstDir = Join-Path $root 'MALPlus\Resources\Images'

# El nombre del PNG es el del destino, con el espacio y las mayusculas del arte
# original elimados a mano: los nombres de fichero no son la fuente de verdad
# para el mapeo, este lo es.
$map = [ordered]@{
    'Search button.png'            = 'discover_btn_search.jpg'
    'Trending button.png'          = 'discover_btn_trending.jpg'
    'Global Calendar button.png'   = 'discover_btn_upcoming.jpg'
    'Your Calendar button.png'     = 'discover_btn_calendar.jpg'
    'Random Anime button.png'      = 'discover_btn_random_anime.jpg'
    'Random Manga button.png'      = 'discover_btn_random_manga.jpg'
    'Random Hidden Gem button.png' = 'discover_btn_gem.jpg'
    'News button.png'              = 'discover_btn_news.jpg'
    'Recommendations button.png'   = 'discover_btn_recommend.jpg'
    'ED button.png'                = 'discover_btn_season_oped.jpg'
    'Random Theme button.png'      = 'discover_btn_theme.jpg'
}

$outW = 620
$outH = 248

# El color de la pagina Discover: el recorte se compone sobre el, no sobre
# negro. Un PNG con alfa sobre negro deja un halo oscuro alrededor de la capsula.
$page = [System.Drawing.Color]::FromArgb(255, 0x05, 0x15, 0x22)

$encoder = [System.Drawing.Imaging.ImageCodecInfo]::GetImageEncoders() |
    Where-Object { $_.MimeType -eq 'image/jpeg' }
$quality = [System.Drawing.Imaging.Encoder]::Quality
$params = New-Object System.Drawing.Imaging.EncoderParameters(1)
$params.Param[0] = New-Object System.Drawing.Imaging.EncoderParameter($quality, 92L)

foreach ($entry in $map.GetEnumerator()) {
    $src = Join-Path $srcDir $entry.Key
    if (-not (Test-Path -LiteralPath $src)) {
        Write-Warning "falta el arte: $($entry.Key)  -> se salta"
        continue
    }

    $original = [System.Drawing.Bitmap]::FromFile((Resolve-Path -LiteralPath $src).Path)
    try {
        # Alta calidad en una sola pasada. Resamplear dos veces (1500 -> 1240 ->
        # 620) suaviza los bordes del texto, que es lo unico delicate que hay.
        $flat = New-Object System.Drawing.Bitmap($outW, $outH,
            [System.Drawing.Imaging.PixelFormat]::Format24bppRgb)
        try {
            $flat.SetResolution(96, 96)
            $g = [System.Drawing.Graphics]::FromImage($flat)
            try {
                $g.CompositingMode = [System.Drawing.Drawing2D.CompositingMode]::SourceOver
                $g.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
                $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
                $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
                $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
                $g.Clear($page)
                $g.DrawImage($original, 0, 0, $outW, $outH)
            }
            finally { $g.Dispose() }

            $dst = Join-Path $dstDir $entry.Value
            $flat.Save($dst, $encoder, $params)
            $kb = [math]::Round((Get-Item -LiteralPath $dst).Length / 1KB, 1)
            Write-Output ("  {0,-46} -> {1}  ({2}x{3}, {4} KB)" -f $entry.Key, $entry.Value, $outW, $outH, $kb)
        }
        finally { $flat.Dispose() }
    }
    finally { $original.Dispose() }
}

Write-Output ''
Write-Output 'listo.'
