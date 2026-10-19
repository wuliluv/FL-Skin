Add-Type -AssemblyName System.Drawing

$size = 64
$bmp = New-Object System.Drawing.Bitmap($size, $size)
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
$g.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::AntiAliasGridFit
$g.Clear([System.Drawing.Color]::Transparent)

$path = New-Object System.Drawing.Drawing2D.GraphicsPath
$r = 14
$path.AddArc(1, 1, $r, $r, 180, 90)
$path.AddArc($size - $r - 2, 1, $r, $r, 270, 90)
$path.AddArc($size - $r - 2, $size - $r - 2, $r, $r, 0, 90)
$path.AddArc(1, $size - $r - 2, $r, $r, 90, 90)
$path.CloseFigure()

$brush = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
    (New-Object System.Drawing.Rectangle(0, 0, $size, $size)),
    [System.Drawing.Color]::FromArgb(255, 108, 92, 231),
    [System.Drawing.Color]::FromArgb(255, 45, 156, 219), 45.0)
$g.FillPath($brush, $path)

$font = New-Object System.Drawing.Font("Segoe UI", 26, [System.Drawing.FontStyle]::Bold, [System.Drawing.GraphicsUnit]::Pixel)
$fmt = New-Object System.Drawing.StringFormat
$fmt.Alignment = [System.Drawing.StringAlignment]::Center
$fmt.LineAlignment = [System.Drawing.StringAlignment]::Center
$rect = New-Object System.Drawing.RectangleF(0, 0, $size, $size)
$g.DrawString("FL", $font, [System.Drawing.Brushes]::White, $rect, $fmt)

$g.Dispose()
$pngPath = Join-Path $PSScriptRoot "icon.png"
$bmp.Save($pngPath, [System.Drawing.Imaging.ImageFormat]::Png)
$bmp.Dispose()

# 用 PNG 负载打包成 .ico（Vista 及以上支持）
$png = [System.IO.File]::ReadAllBytes($pngPath)
$ico = New-Object System.IO.MemoryStream
$bw = New-Object System.IO.BinaryWriter($ico)
$bw.Write([UInt16]0)          # reserved
$bw.Write([UInt16]1)          # type = icon
$bw.Write([UInt16]1)          # count
$bw.Write([Byte]$size)        # width
$bw.Write([Byte]$size)        # height
$bw.Write([Byte]0)            # color count
$bw.Write([Byte]0)            # reserved
$bw.Write([UInt16]1)          # planes
$bw.Write([UInt16]32)         # bit count
$bw.Write([UInt32]$png.Length)
$bw.Write([UInt32]22)         # offset
$bw.Write($png)
$bw.Flush()

[System.IO.File]::WriteAllBytes((Join-Path $PSScriptRoot "icon.ico"), $ico.ToArray())
$bw.Dispose()
$ico.Dispose()
Remove-Item $pngPath -Force
Write-Output "icon.ico written"
