# Renders the DeskDeck icon (same design as the Android launcher icon) as a multi-size .ico.
# Usage: pwsh tools/make-icon.ps1  ->  agent/DeskDeck.Agent/deskdeck.ico
Add-Type -AssemblyName System.Drawing
$outDir = Join-Path $PSScriptRoot '..\agent\DeskDeck.Agent'
$out = Join-Path (Resolve-Path -LiteralPath $outDir).Path 'deskdeck.ico'

function New-RoundRect([float]$x, [float]$y, [float]$w, [float]$h, [float]$r) {
    $p = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = $r * 2
    $p.AddArc($x, $y, $d, $d, 180, 90); $p.AddArc($x + $w - $d, $y, $d, $d, 270, 90)
    $p.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0, 90); $p.AddArc($x, $y + $h - $d, $d, $d, 90, 90)
    $p.CloseFigure()
    return $p
}

function New-Brush([string]$hex) { New-Object System.Drawing.SolidBrush ([System.Drawing.ColorTranslator]::FromHtml($hex)) }

function New-IconPng([int]$size) {
    $bmp = New-Object System.Drawing.Bitmap $size, $size
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'
    $g.Clear([System.Drawing.Color]::Transparent)
    $s = $size / 256.0

    # background
    $g.FillPath((New-Brush '#0F1318'), (New-RoundRect 0 0 ($size - 1) ($size - 1) (56 * $s)))
    # control deck: a 2 x 2 grid of tiles
    $tiles = @(@(58, 58, '#2DD4BF'), @(138, 58, '#76B900'), @(58, 138, '#A78BFA'), @(138, 138, '#F59E0B'))
    foreach ($t in $tiles) { $g.FillPath((New-Brush $t[2]), (New-RoundRect ($t[0] * $s) ($t[1] * $s) (60 * $s) (60 * $s) (14 * $s))) }

    $g.Dispose()
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    return , $ms.ToArray()
}

$sizes = 16, 24, 32, 48, 64, 128, 256
$images = foreach ($sz in $sizes) { , (New-IconPng $sz) }

$fs = [System.IO.File]::Create($out)
$bw = New-Object System.IO.BinaryWriter $fs
$bw.Write([uint16]0); $bw.Write([uint16]1); $bw.Write([uint16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $sz = $sizes[$i]; $len = $images[$i].Length
    $dim = if ($sz -ge 256) { 0 } else { $sz }
    $bw.Write([byte]$dim); $bw.Write([byte]$dim)
    $bw.Write([byte]0); $bw.Write([byte]0); $bw.Write([uint16]1); $bw.Write([uint16]32)
    $bw.Write([uint32]$len); $bw.Write([uint32]$offset)
    $offset += $len
}
foreach ($img in $images) { $bw.Write([byte[]]$img) }
$bw.Close()
"icon written: $out ($((Get-Item $out).Length) bytes)"
