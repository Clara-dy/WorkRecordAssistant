param(
    [string]$PngPath,
    [string]$IcoPath
)

Add-Type -AssemblyName System.Drawing

$bitmap = [System.Drawing.Bitmap]::FromFile($PngPath)
$size = 256
$resized = New-Object System.Drawing.Bitmap($size, $size)
$graphics = [System.Drawing.Graphics]::FromImage($resized)
$graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
$graphics.DrawImage($bitmap, 0, 0, $size, $size)
$graphics.Dispose()
$bitmap.Dispose()

$hIcon = $resized.GetHicon()
$icon = [System.Drawing.Icon]::FromHandle($hIcon)
$stream = [System.IO.File]::Create($IcoPath)
$icon.Save($stream)
$stream.Close()
$icon.Dispose()
$resized.Dispose()

Write-Host "Created: $IcoPath"
