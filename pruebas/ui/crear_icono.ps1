param([string]$OutIco, [string]$PreviewPng)
Add-Type -AssemblyName System.Drawing
$ErrorActionPreference = 'Stop'

# Icono de Recodec: la misma cámara de la interfaz (glifo E714 de Segoe Fluent Icons),
# en blanco sobre un cuadrado redondeado azul con degradado.
function New-IconBitmap([int]$size) {
    $bmp = New-Object System.Drawing.Bitmap($size, $size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'
    $g.TextRenderingHint = 'AntiAliasGridFit'
    $g.PixelOffsetMode = 'HighQuality'
    $g.Clear([System.Drawing.Color]::Transparent)

    $m = [Math]::Max(0.5, $size * 0.03)
    $r = $size * 0.22
    $w = $size - 2 * $m
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $path.AddArc($m, $m, 2 * $r, 2 * $r, 180, 90)
    $path.AddArc($m + $w - 2 * $r, $m, 2 * $r, 2 * $r, 270, 90)
    $path.AddArc($m + $w - 2 * $r, $m + $w - 2 * $r, 2 * $r, 2 * $r, 0, 90)
    $path.AddArc($m, $m + $w - 2 * $r, 2 * $r, 2 * $r, 90, 90)
    $path.CloseFigure()
    $rect = New-Object System.Drawing.RectangleF($m, $m, $w, $w)
    $top = [System.Drawing.Color]::FromArgb(255, 0x3A, 0x96, 0xEE)
    $bottom = [System.Drawing.Color]::FromArgb(255, 0x0B, 0x5C, 0xC4)
    $brush = New-Object System.Drawing.Drawing2D.LinearGradientBrush($rect, $top, $bottom, 90.0)
    $g.FillPath($brush, $path)

    # Glifo: en tamaños pequeños se dibuja algo más grande y con un trazo extra para que no se pierda.
    $scale = if ($size -le 24) { 0.80 } elseif ($size -le 48) { 0.72 } else { 0.64 }
    $font = New-Object System.Drawing.Font('Segoe Fluent Icons', [single]($size * $scale), [System.Drawing.FontStyle]::Regular, [System.Drawing.GraphicsUnit]::Pixel)
    $gp = New-Object System.Drawing.Drawing2D.GraphicsPath
    $fmt = [System.Drawing.StringFormat]::GenericTypographic
    $gp.AddString([string][char]0xE714, $font.FontFamily, 0, $font.Size, (New-Object System.Drawing.PointF(0, 0)), $fmt)
    $b = $gp.GetBounds()
    $mx = New-Object System.Drawing.Drawing2D.Matrix
    $mx.Translate(($size - $b.Width) / 2 - $b.X, ($size - $b.Height) / 2 - $b.Y)
    $gp.Transform($mx)
    $white = [System.Drawing.Brushes]::White
    $g.FillPath($white, $gp)
    if ($size -le 32) {
        $pen = New-Object System.Drawing.Pen([System.Drawing.Color]::White, [single]([Math]::Max(0.6, $size / 32.0)))
        $pen.LineJoin = 'Round'
        $g.DrawPath($pen, $gp)
    }
    $g.Dispose()
    return $bmp
}

$sizes = 16, 20, 24, 32, 40, 48, 64, 128, 256
$pngs = @()
foreach ($s in $sizes) {
    $bmp = New-IconBitmap $s
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $pngs += , ($ms.ToArray())
    $bmp.Dispose()
}

# Archivo .ico con cada tamaño guardado como PNG
$fs = [System.IO.File]::Create($OutIco)
$bw = New-Object System.IO.BinaryWriter($fs)
$bw.Write([UInt16]0); $bw.Write([UInt16]1); $bw.Write([UInt16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $s = $sizes[$i]; $len = $pngs[$i].Length
    $bw.Write([byte]($(if ($s -ge 256) { 0 } else { $s })))
    $bw.Write([byte]($(if ($s -ge 256) { 0 } else { $s })))
    $bw.Write([byte]0); $bw.Write([byte]0)
    $bw.Write([UInt16]1); $bw.Write([UInt16]32)
    $bw.Write([UInt32]$len); $bw.Write([UInt32]$offset)
    $offset += $len
}
foreach ($p in $pngs) { $bw.Write($p) }
$bw.Close()

# Vista previa: todos los tamaños sobre fondo claro y oscuro
$W = 760; $H = 330
$prev = New-Object System.Drawing.Bitmap($W, $H)
$g = [System.Drawing.Graphics]::FromImage($prev)
$g.Clear([System.Drawing.Color]::FromArgb(255, 243, 243, 243))
$g.FillRectangle((New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 32, 32, 32))), 0, $H / 2, $W, $H / 2)
foreach ($row in 0, 1) {
    $x = 16
    foreach ($s in 256, 64, 48, 32, 24, 16) {
        $bmp = New-IconBitmap $s
        $d = [Math]::Min($s, 140)
        $y = $row * ($H / 2) + ($H / 2 - $d) / 2
        $g.DrawImage($bmp, $x, $y, $d, $d)
        $x += $d + 24
        $bmp.Dispose()
    }
}
$g.Dispose()
$prev.Save($PreviewPng, [System.Drawing.Imaging.ImageFormat]::Png)
"ok"
