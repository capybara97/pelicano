param(
    [string]$SourcePngPath,

    [Parameter(Mandatory = $true)]
    [string]$PngPath,

    [Parameter(Mandatory = $true)]
    [string]$IcoPath
)

# This script prepares the app icon from the artwork source and emits:
# - a cleaned square PNG for WinUI resources
# - a multi-size ICO for the EXE, tray, shortcuts, and installer
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if ([string]::IsNullOrWhiteSpace($SourcePngPath)) {
    $SourcePngPath = $PngPath
}

if (-not (Test-Path -LiteralPath $SourcePngPath)) {
    throw "Source PNG icon file was not found: $SourcePngPath"
}

$pngDirectory = Split-Path -Path $PngPath -Parent
$icoDirectory = Split-Path -Path $IcoPath -Parent

if (-not [string]::IsNullOrWhiteSpace($pngDirectory)) {
    New-Item -ItemType Directory -Force -Path $pngDirectory | Out-Null
}

if (-not [string]::IsNullOrWhiteSpace($icoDirectory)) {
    New-Item -ItemType Directory -Force -Path $icoDirectory | Out-Null
}

Add-Type -AssemblyName System.Drawing

$csharp = @"
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;

public static class PelicanoIconPreparer
{
    private static readonly int[] IconSizes = new[] { 16, 20, 24, 32, 40, 48, 64, 96, 128, 256 };
    private const int CanonicalSize = 1024;
    private const float BadgeCornerRadiusRatio = 0.225f;
    private const float ArtworkPaddingRatio = 0.01f;

    public static void Prepare(string sourcePath, string pngPath, string icoPath)
    {
        using (var sourceBitmap = new Bitmap(sourcePath))
        {
            using (var croppedBitmap = CropToArtwork(sourceBitmap))
            {
                using (var canonicalBitmap = RenderSquare(croppedBitmap, CanonicalSize))
                {
                    var pngDirectory = Path.GetDirectoryName(pngPath);
                    if (!string.IsNullOrWhiteSpace(pngDirectory))
                    {
                        Directory.CreateDirectory(pngDirectory);
                    }

                    var icoDirectory = Path.GetDirectoryName(icoPath);
                    if (!string.IsNullOrWhiteSpace(icoDirectory))
                    {
                        Directory.CreateDirectory(icoDirectory);
                    }

                    if (File.Exists(pngPath))
                    {
                        File.Delete(pngPath);
                    }

                    canonicalBitmap.Save(pngPath, ImageFormat.Png);
                    WriteMultiSizeIcon(canonicalBitmap, icoPath);
                }
            }
        }
    }

    private static Bitmap CropToArtwork(Bitmap sourceBitmap)
    {
        var bounds = FindBadgeBounds(sourceBitmap);
        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            var shortest = Math.Min(sourceBitmap.Width, sourceBitmap.Height);
            var startX = (sourceBitmap.Width - shortest) / 2;
            var startY = (sourceBitmap.Height - shortest) / 2;
            bounds = new Rectangle(startX, startY, shortest, shortest);
        }

        var expandedBounds = ExpandToSquare(sourceBitmap.Size, bounds, ArtworkPaddingRatio);
        var croppedBitmap = new Bitmap(expandedBounds.Width, expandedBounds.Height, PixelFormat.Format32bppArgb);

        using (var graphics = Graphics.FromImage(croppedBitmap))
        {
            ConfigureGraphics(graphics);
            graphics.Clear(Color.Transparent);
            graphics.DrawImage(
                sourceBitmap,
                new Rectangle(0, 0, croppedBitmap.Width, croppedBitmap.Height),
                expandedBounds,
                GraphicsUnit.Pixel);
        }

        return croppedBitmap;
    }

    private static Rectangle FindBadgeBounds(Bitmap sourceBitmap)
    {
        var minX = sourceBitmap.Width;
        var minY = sourceBitmap.Height;
        var maxX = -1;
        var maxY = -1;

        for (var y = 0; y < sourceBitmap.Height; y += 1)
        {
            for (var x = 0; x < sourceBitmap.Width; x += 1)
            {
                var color = sourceBitmap.GetPixel(x, y);
                if (!IsBadgeBlue(color))
                {
                    continue;
                }

                if (x < minX) minX = x;
                if (y < minY) minY = y;
                if (x > maxX) maxX = x;
                if (y > maxY) maxY = y;
            }
        }

        if (maxX < minX || maxY < minY)
        {
            return Rectangle.Empty;
        }

        return Rectangle.FromLTRB(minX, minY, maxX + 1, maxY + 1);
    }

    private static Rectangle ExpandToSquare(Size sourceSize, Rectangle bounds, float paddingRatio)
    {
        var size = Math.Max(bounds.Width, bounds.Height);
        var padding = Math.Max(8, (int)Math.Round(size * paddingRatio));
        size += padding * 2;

        var centerX = bounds.Left + (bounds.Width / 2f);
        var centerY = bounds.Top + (bounds.Height / 2f);
        var left = (int)Math.Round(centerX - (size / 2f));
        var top = (int)Math.Round(centerY - (size / 2f));

        if (left < 0) left = 0;
        if (top < 0) top = 0;
        if (left + size > sourceSize.Width) left = sourceSize.Width - size;
        if (top + size > sourceSize.Height) top = sourceSize.Height - size;
        if (left < 0) left = 0;
        if (top < 0) top = 0;

        size = Math.Min(size, Math.Min(sourceSize.Width, sourceSize.Height));

        if (left + size > sourceSize.Width)
        {
            left = sourceSize.Width - size;
        }

        if (top + size > sourceSize.Height)
        {
            top = sourceSize.Height - size;
        }

        return new Rectangle(left, top, size, size);
    }

    private static bool IsBadgeBlue(Color color)
    {
        if (color.A <= 16)
        {
            return false;
        }

        return color.B >= 170 &&
               color.G >= 110 &&
               color.R <= 120 &&
               (color.B - color.R) >= 50;
    }

    private static Bitmap RenderSquare(Bitmap sourceBitmap, int size)
    {
        var renderedBitmap = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using (var graphics = Graphics.FromImage(renderedBitmap))
        {
            ConfigureGraphics(graphics);
            graphics.Clear(Color.Transparent);

            var bounds = new Rectangle(0, 0, size, size);
            var cornerRadius = Math.Max(8, size * BadgeCornerRadiusRatio);
            using (var roundedPath = CreateRoundedRectanglePath(bounds, cornerRadius))
            {
                graphics.SetClip(roundedPath);
                graphics.DrawImage(
                    sourceBitmap,
                    bounds,
                    new Rectangle(0, 0, sourceBitmap.Width, sourceBitmap.Height),
                    GraphicsUnit.Pixel);
                graphics.ResetClip();
            }
        }

        return renderedBitmap;
    }

    private static void ConfigureGraphics(Graphics graphics)
    {
        graphics.CompositingMode = CompositingMode.SourceOver;
        graphics.CompositingQuality = CompositingQuality.HighQuality;
        graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
        graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
        graphics.SmoothingMode = SmoothingMode.HighQuality;
    }

    private static GraphicsPath CreateRoundedRectanglePath(Rectangle bounds, float cornerRadius)
    {
        var path = new GraphicsPath();
        var diameter = cornerRadius * 2f;
        if (diameter > bounds.Width)
        {
            diameter = bounds.Width;
        }

        if (diameter > bounds.Height)
        {
            diameter = bounds.Height;
        }

        path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180f, 90f);
        path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270f, 90f);
        path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0f, 90f);
        path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90f, 90f);
        path.CloseFigure();
        return path;
    }

    private static void WriteMultiSizeIcon(Bitmap canonicalBitmap, string icoPath)
    {
        using (var fileStream = new FileStream(icoPath, FileMode.Create, FileAccess.Write, FileShare.None))
        using (var writer = new BinaryWriter(fileStream))
        {
            var imageEntries = new List<IconEntryData>();

            foreach (var iconSize in IconSizes)
            {
                using (var sizedBitmap = RenderSquare(canonicalBitmap, iconSize))
                {
                    imageEntries.Add(new IconEntryData(iconSize, BuildBmpIconImage(sizedBitmap)));
                }
            }

            writer.Write((ushort)0);
            writer.Write((ushort)1);
            writer.Write((ushort)imageEntries.Count);

            var offset = 6 + (16 * imageEntries.Count);
            foreach (var entry in imageEntries)
            {
                writer.Write((byte)(entry.Size >= 256 ? 0 : entry.Size));
                writer.Write((byte)(entry.Size >= 256 ? 0 : entry.Size));
                writer.Write((byte)0);
                writer.Write((byte)0);
                writer.Write((ushort)1);
                writer.Write((ushort)32);
                writer.Write((uint)entry.ImageBytes.Length);
                writer.Write((uint)offset);
                offset += entry.ImageBytes.Length;
            }

            foreach (var entry in imageEntries)
            {
                writer.Write(entry.ImageBytes);
            }
        }
    }

    private static byte[] BuildBmpIconImage(Bitmap bitmap)
    {
        var iconSize = bitmap.Width;
        var maskStride = ((iconSize + 31) / 32) * 4;
        var maskSize = maskStride * iconSize;
        var xorSize = iconSize * iconSize * 4;

        using (var memoryStream = new MemoryStream())
        using (var writer = new BinaryWriter(memoryStream))
        {
            writer.Write((uint)40);
            writer.Write(iconSize);
            writer.Write(iconSize * 2);
            writer.Write((ushort)1);
            writer.Write((ushort)32);
            writer.Write((uint)0);
            writer.Write((uint)xorSize);
            writer.Write(0);
            writer.Write(0);
            writer.Write((uint)0);
            writer.Write((uint)0);

            for (var y = iconSize - 1; y >= 0; y -= 1)
            {
                for (var x = 0; x < iconSize; x += 1)
                {
                    var pixel = bitmap.GetPixel(x, y);
                    writer.Write(pixel.B);
                    writer.Write(pixel.G);
                    writer.Write(pixel.R);
                    writer.Write(pixel.A);
                }
            }

            writer.Write(new byte[maskSize]);
            writer.Flush();
            return memoryStream.ToArray();
        }
    }

    private struct IconEntryData
    {
        public IconEntryData(int size, byte[] imageBytes)
        {
            Size = size;
            ImageBytes = imageBytes;
        }

        public int Size;

        public byte[] ImageBytes;
    }
}
"@

Add-Type -ReferencedAssemblies 'System.Drawing' -TypeDefinition $csharp
[PelicanoIconPreparer]::Prepare($SourcePngPath, $PngPath, $IcoPath)
