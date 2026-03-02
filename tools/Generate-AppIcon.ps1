param(
    [Parameter(Mandatory = $true)]
    [string]$InputImage,

    [Parameter(Mandatory = $true)]
    [string]$OutputIco
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

Add-Type -AssemblyName System.Drawing

if (-not (Test-Path -LiteralPath $InputImage)) {
    throw "Input image not found: $InputImage"
}

$resolvedInput = (Resolve-Path -LiteralPath $InputImage).Path
$sizes = @(16, 24, 32, 48, 64, 128, 256)

function New-PngBytes {
    param(
        [System.Drawing.Bitmap]$SourceImage,
        [System.Drawing.Rectangle]$SourceRect,
        [int]$Size
    )

    $bitmap = New-Object System.Drawing.Bitmap($Size, $Size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $memoryStream = New-Object System.IO.MemoryStream

    try {
        $graphics.Clear([System.Drawing.Color]::Transparent)
        $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
        $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
        $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
        $graphics.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality

        $scale = [Math]::Min($Size / $SourceRect.Width, $Size / $SourceRect.Height)
        $targetWidth = [int][Math]::Round($SourceRect.Width * $scale)
        $targetHeight = [int][Math]::Round($SourceRect.Height * $scale)
        $targetX = [int][Math]::Floor(($Size - $targetWidth) / 2.0)
        $targetY = [int][Math]::Floor(($Size - $targetHeight) / 2.0)

        $destRect = New-Object System.Drawing.Rectangle($targetX, $targetY, $targetWidth, $targetHeight)
        $graphics.DrawImage($SourceImage, $destRect, $SourceRect, [System.Drawing.GraphicsUnit]::Pixel)
        $bitmap.Save($memoryStream, [System.Drawing.Imaging.ImageFormat]::Png)
        return $memoryStream.ToArray()
    }
    finally {
        $memoryStream.Dispose()
        $graphics.Dispose()
        $bitmap.Dispose()
    }
}

function Get-VisibleBounds {
    param([System.Drawing.Bitmap]$Bitmap)

    $width = $Bitmap.Width
    $height = $Bitmap.Height

    $minX = $width
    $minY = $height
    $maxX = -1
    $maxY = -1

    for ($y = 0; $y -lt $height; $y++) {
        for ($x = 0; $x -lt $width; $x++) {
            if ($Bitmap.GetPixel($x, $y).A -gt 10) {
                if ($x -lt $minX) { $minX = $x }
                if ($y -lt $minY) { $minY = $y }
                if ($x -gt $maxX) { $maxX = $x }
                if ($y -gt $maxY) { $maxY = $y }
            }
        }
    }

    if ($maxX -lt 0 -or $maxY -lt 0) {
        return New-Object System.Drawing.Rectangle(0, 0, $width, $height)
    }

    return New-Object System.Drawing.Rectangle(
        $minX,
        $minY,
        ($maxX - $minX + 1),
        ($maxY - $minY + 1))
}

$source = New-Object System.Drawing.Bitmap($resolvedInput)

try {
    $pngPayloads = New-Object System.Collections.Generic.List[byte[]]
    $sourceRect = Get-VisibleBounds -Bitmap $source

    foreach ($size in $sizes) {
        $pngPayloads.Add((New-PngBytes -SourceImage $source -SourceRect $sourceRect -Size $size))
    }

    $outputDir = Split-Path -Parent $OutputIco
    if (-not [string]::IsNullOrWhiteSpace($outputDir)) {
        New-Item -ItemType Directory -Force -Path $outputDir | Out-Null
    }

    $fileStream = [System.IO.File]::Open($OutputIco, [System.IO.FileMode]::Create, [System.IO.FileAccess]::Write, [System.IO.FileShare]::None)
    $writer = New-Object System.IO.BinaryWriter($fileStream)

    try {
        $count = $pngPayloads.Count
        $writer.Write([UInt16]0)  # reserved
        $writer.Write([UInt16]1)  # type: icon
        $writer.Write([UInt16]$count)

        $offset = 6 + (16 * $count)

        for ($i = 0; $i -lt $count; $i++) {
            $size = $sizes[$i]
            $bytes = $pngPayloads[$i]
            $iconDimensionByte = if ($size -eq 256) { [byte]0 } else { [byte]$size }

            $writer.Write($iconDimensionByte)
            $writer.Write($iconDimensionByte)
            $writer.Write([byte]0)          # color count
            $writer.Write([byte]0)          # reserved
            $writer.Write([UInt16]1)        # planes
            $writer.Write([UInt16]32)       # bit depth
            $writer.Write([UInt32]$bytes.Length)
            $writer.Write([UInt32]$offset)

            $offset += $bytes.Length
        }

        for ($i = 0; $i -lt $count; $i++) {
            $writer.Write($pngPayloads[$i])
        }
    }
    finally {
        $writer.Dispose()
        $fileStream.Dispose()
    }
}
finally {
    $source.Dispose()
}

Write-Host "Generated app icon: $OutputIco"
