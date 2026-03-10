param(
    [Parameter(Mandatory = $true)]
    [string]$PngPath,

    [Parameter(Mandatory = $true)]
    [string]$IcoPath
)

# This script converts a PNG source icon into a valid BMP-based ICO file.
# The generated ICO is compatible with the C# Win32 resource compiler and Inno Setup.
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

Add-Type -AssemblyName System.Drawing

if (-not (Test-Path -LiteralPath $PngPath)) {
    throw "PNG icon file was not found: $PngPath"
}

$targetDirectory = Split-Path -Path $IcoPath -Parent

if (-not [string]::IsNullOrWhiteSpace($targetDirectory)) {
    New-Item -ItemType Directory -Force -Path $targetDirectory | Out-Null
}

$sourceImage = [System.Drawing.Image]::FromFile($PngPath)

try {
    $iconSize = 64
    $bitmap = New-Object System.Drawing.Bitmap($iconSize, $iconSize, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)

    try {
        $graphics.Clear([System.Drawing.Color]::Transparent)
        $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
        $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
        $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality

        $scale = [Math]::Min($iconSize / $sourceImage.Width, $iconSize / $sourceImage.Height)
        $drawWidth = [int][Math]::Round($sourceImage.Width * $scale)
        $drawHeight = [int][Math]::Round($sourceImage.Height * $scale)
        $drawX = [int][Math]::Floor(($iconSize - $drawWidth) / 2)
        $drawY = [int][Math]::Floor(($iconSize - $drawHeight) / 2)

        $graphics.DrawImage($sourceImage, $drawX, $drawY, $drawWidth, $drawHeight)
    }
    finally {
        $graphics.Dispose()
    }

    $maskStride = [int](([Math]::Ceiling($iconSize / 32.0)) * 4)
    $maskSize = $maskStride * $iconSize
    $xorSize = $iconSize * $iconSize * 4
    $imageSize = 40 + $xorSize + $maskSize

    $memoryStream = New-Object System.IO.MemoryStream
    $writer = New-Object System.IO.BinaryWriter($memoryStream)

    # ICONDIR
    $writer.Write([UInt16]0)
    $writer.Write([UInt16]1)
    $writer.Write([UInt16]1)

    # ICONDIRENTRY
    $writer.Write([byte]$iconSize)
    $writer.Write([byte]$iconSize)
    $writer.Write([byte]0)
    $writer.Write([byte]0)
    $writer.Write([UInt16]1)
    $writer.Write([UInt16]32)
    $writer.Write([UInt32]$imageSize)
    $writer.Write([UInt32]22)

    # BITMAPINFOHEADER
    $writer.Write([UInt32]40)
    $writer.Write([Int32]$iconSize)
    $writer.Write([Int32]($iconSize * 2))
    $writer.Write([UInt16]1)
    $writer.Write([UInt16]32)
    $writer.Write([UInt32]0)
    $writer.Write([UInt32]$xorSize)
    $writer.Write([Int32]0)
    $writer.Write([Int32]0)
    $writer.Write([UInt32]0)
    $writer.Write([UInt32]0)

    for ($y = $iconSize - 1; $y -ge 0; $y--) {
        for ($x = 0; $x -lt $iconSize; $x++) {
            $pixel = $bitmap.GetPixel($x, $y)
            $writer.Write([byte]$pixel.B)
            $writer.Write([byte]$pixel.G)
            $writer.Write([byte]$pixel.R)
            $writer.Write([byte]$pixel.A)
        }
    }

    $writer.Write((New-Object byte[] $maskSize))
    [System.IO.File]::WriteAllBytes($IcoPath, $memoryStream.ToArray())
}
finally {
    if ($bitmap) {
        $bitmap.Dispose()
    }

    $sourceImage.Dispose()
}
