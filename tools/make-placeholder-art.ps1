# Makes simple stand-in pictures for any of assets\device.png, mfd.png, emblem.png, icon.ico that are missing, so the project
# can be built from the public source without the third-party photos used in the official releases.
# (build.ps1 calls this automatically. Drop your own pictures into assets\ to replace the stand-ins.)
param([string]$Root = (Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)))
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$assets = Join-Path $Root 'assets'
New-Item -ItemType Directory -Force $assets | Out-Null

function RoundRect([float]$x, [float]$y, [float]$w, [float]$h, [float]$r) {
    $p = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = $r * 2
    $p.AddArc($x, $y, $d, $d, 180, 90); $p.AddArc($x + $w - $d, $y, $d, $d, 270, 90)
    $p.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0, 90); $p.AddArc($x, $y + $h - $d, $d, $d, 90, 90)
    $p.CloseFigure(); return $p
}
function Fill($g, $color, $path) { $b = New-Object System.Drawing.SolidBrush $color; $g.FillPath($b, $path); $b.Dispose() }
function C($r, $g, $b) { [System.Drawing.Color]::FromArgb(255, $r, $g, $b) }

# ---- ATAK tablet stand-in: 774 x 396 "logical" picture, white backdrop (the app cuts that away), tan body, dark screen ----
$dev = Join-Path $assets 'device.png'
if (-not (Test-Path $dev)) {
    $bmp = New-Object System.Drawing.Bitmap 774, 396
    $g = [System.Drawing.Graphics]::FromImage($bmp); $g.SmoothingMode = 'AntiAlias'; $g.Clear([System.Drawing.Color]::White)
    $body = RoundRect 18 20 738 356 46
    $lg = New-Object System.Drawing.Drawing2D.LinearGradientBrush ((New-Object System.Drawing.Rectangle 0, 20, 774, 356), (C 178 152 112), (C 126 102 70), 90)
    $g.FillPath($lg, $body); $lg.Dispose()
    Fill $g (C 28 28 28) (RoundRect 114 57 546 274 20)        # dark rubber ring around the screen
    Fill $g (C 14 16 14) (RoundRect 126 69 522 250 13)        # the screen (the app cuts this out)
    Fill $g (C 120 98 68) (RoundRect 668 140 36 70 12)        # a speaker grille
    $g.Dispose(); $bmp.Save($dev, [System.Drawing.Imaging.ImageFormat]::Png); $bmp.Dispose()
    Write-Host "made stand-in assets\device.png"
}

# ---- pilot display stand-in: 780 x 569, transparent outside the dark plate, 24 buttons around the screen ----
$mfd = Join-Path $assets 'mfd.png'
if (-not (Test-Path $mfd)) {
    $bmp = New-Object System.Drawing.Bitmap 780, 569, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp); $g.SmoothingMode = 'AntiAlias'; $g.Clear([System.Drawing.Color]::Transparent)
    Fill $g (C 58 56 54) (RoundRect 8 8 764 553 26)            # the plate
    Fill $g (C 24 24 24) (RoundRect 88 83 604 411 12)          # dark frame
    $scr = RoundRect 100 95 582 387 5; Fill $g (C 48 50 47) $scr
    $pts = @()
    foreach ($x in 168, 233, 297, 481, 546, 611) { $pts += , @($x, 25) }
    foreach ($y in 188, 238, 287, 336, 385, 435) { $pts += , @(24, $y) }
    foreach ($y in 189, 238, 288, 337, 387, 436) { $pts += , @(753, $y) }
    foreach ($x in 103, 173, 246, 527, 598, 670) { $pts += , @($x, 541) }
    foreach ($p in $pts) {
        Fill $g (C 30 30 30) (RoundRect ($p[0] - 16) ($p[1] - 14) 32 28 4)
        $wb = New-Object System.Drawing.SolidBrush (C 225 225 225); $g.FillEllipse($wb, $p[0] - 3.5, $p[1] - 3.5, 7, 7); $wb.Dispose()
    }
    $g.Dispose(); $bmp.Save($mfd, [System.Drawing.Imaging.ImageFormat]::Png); $bmp.Dispose()
    Write-Host "made stand-in assets\mfd.png"
}

# ---- emblem (tray icon / exe icon source): a simple crosshair badge ----
$emb = Join-Path $assets 'emblem.png'
if (-not (Test-Path $emb)) {
    $bmp = New-Object System.Drawing.Bitmap 256, 256, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp); $g.SmoothingMode = 'AntiAlias'; $g.Clear([System.Drawing.Color]::Transparent)
    $bb = New-Object System.Drawing.SolidBrush (C 24 27 22); $g.FillEllipse($bb, 8, 8, 240, 240); $bb.Dispose()
    $pen = New-Object System.Drawing.Pen (C 150 205 90), 14; $g.DrawEllipse($pen, 40, 40, 176, 176)
    $pen.Width = 10; $g.DrawLine($pen, 128, 14, 128, 242); $g.DrawLine($pen, 14, 128, 242, 128); $pen.Dispose()
    $g.Dispose(); $bmp.Save($emb, [System.Drawing.Imaging.ImageFormat]::Png); $bmp.Dispose()
    Write-Host "made stand-in assets\emblem.png"
}

# ---- icon.ico (PNG-compressed icon with 6 sizes, built from emblem.png) ----
$ico = Join-Path $assets 'icon.ico'
if (-not (Test-Path $ico)) {
    $src = [System.Drawing.Image]::FromFile($emb)
    $sizes = 16, 24, 32, 48, 64, 128, 256
    $blobs = @()
    foreach ($s in $sizes) {
        $b = New-Object System.Drawing.Bitmap $s, $s, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
        $g = [System.Drawing.Graphics]::FromImage($b); $g.InterpolationMode = 'HighQualityBicubic'; $g.DrawImage($src, 0, 0, $s, $s); $g.Dispose()
        $ms = New-Object System.IO.MemoryStream; $b.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png); $blobs += , $ms.ToArray(); $b.Dispose(); $ms.Dispose()
    }
    $src.Dispose()
    $fs = [System.IO.File]::Create($ico); $w = New-Object System.IO.BinaryWriter $fs
    $w.Write([uint16]0); $w.Write([uint16]1); $w.Write([uint16]$sizes.Count)
    $offset = 6 + 16 * $sizes.Count
    for ($i = 0; $i -lt $sizes.Count; $i++) {
        $s = $sizes[$i]; $dim = if ($s -ge 256) { 0 } else { $s }
        $w.Write([byte]$dim); $w.Write([byte]$dim); $w.Write([byte]0); $w.Write([byte]0)
        $w.Write([uint16]1); $w.Write([uint16]32); $w.Write([uint32]$blobs[$i].Length); $w.Write([uint32]$offset)
        $offset += $blobs[$i].Length
    }
    foreach ($bl in $blobs) { $w.Write($bl) }
    $w.Close(); $fs.Close()
    Write-Host "made stand-in assets\icon.ico"
}
