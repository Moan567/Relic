using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using System;
using System.IO;
using System.Runtime.InteropServices;

namespace Rockwall2.Editor.Common.Utils;

/// <summary>
/// Pixel operations for loose texture images, backed by Avalonia's Bitmap/WriteableBitmap.
///
/// Everything round-trips through a 32bpp Bgra8888 buffer so Avalonia's WriteableBitmap.Lock()
/// can hand out raw pixels. Decoding any PNG/JPG/TGA happens in Avalonia's Skia backend;
/// encoding back out uses Bitmap.Save, which picks the container from the destination extension.
/// </summary>
public static class ImageEditing
{
    /// <summary>Decodes any supported image into a mutable 32bpp buffer.</summary>
    public static WriteableBitmap Load(string path)
    {
        // DecodeToWidth is the only route from an arbitrary encoded image to writable pixels;
        // Bitmap itself is read-only and exposes no pixel address. Asking for the image's own
        // width makes it a straight 1:1 copy rather than a resample.
        int width;
        using (var probe = new Bitmap(path))
            width = probe.PixelSize.Width;

        if (width <= 0) width = 1;

        using var stream = File.OpenRead(path);
        return WriteableBitmap.DecodeToWidth(stream, width);
    }

    /// <summary>Copies pixels out of a bitmap into a managed BGRA array.</summary>
    public static byte[] ExtractPixels(WriteableBitmap bitmap)
    {
        int stride = bitmap.PixelSize.Width * 4;
        var pixels = new byte[stride * bitmap.PixelSize.Height];

        using (var fb = bitmap.Lock())
            Marshal.Copy(fb.Address, pixels, 0, Math.Min(pixels.Length, fb.RowBytes * bitmap.PixelSize.Height));

        return pixels;
    }

    /// <summary>Writes a BGRA pixel block back into a bitmap.</summary>
    public static void WritePixels(WriteableBitmap bitmap, byte[] pixels)
    {
        using var fb = bitmap.Lock();

        int copy = Math.Min(pixels.Length, fb.RowBytes * bitmap.PixelSize.Height);
        Marshal.Copy(pixels, 0, fb.Address, copy);
    }

    /// <summary>Crops to a rectangle, clamped to the bitmap bounds.</summary>
    public static WriteableBitmap Crop(WriteableBitmap source, int x, int y, int width, int height)
    {
        var size = source.PixelSize;

        x = Math.Clamp(x, 0, Math.Max(0, size.Width - 1));
        y = Math.Clamp(y, 0, Math.Max(0, size.Height - 1));
        width = Math.Clamp(width, 1, size.Width - x);
        height = Math.Clamp(height, 1, size.Height - y);

        byte[] src = ExtractPixels(source);
        var result = CreateBitmap(width, height);
        var dst = new byte[width * height * 4];

        for (int row = 0; row < height; row++)
            Buffer.BlockCopy(src, ((y + row) * size.Width + x) * 4, dst, row * width * 4, width * 4);

        WritePixels(result, dst);
        return result;
    }

    /// <summary>
    /// Box-filtered resize. Textures get tiled at arbitrary scales in the engine, so suppressing
    /// aliasing on the way down matters more than preserving fine detail.
    /// </summary>
    public static WriteableBitmap Resize(WriteableBitmap source, int width, int height)
    {
        width = Math.Max(1, width);
        height = Math.Max(1, height);

        var size = source.PixelSize;
        byte[] src = ExtractPixels(source);
        var result = CreateBitmap(width, height);
        var dst = new byte[width * height * 4];

        for (int y = 0; y < height; y++)
        {
            int sy0 = (int)((long)y * size.Height / height);
            int sy1 = (int)Math.Min(size.Height, ((long)(y + 1) * size.Height + height - 1) / height);
            if (sy1 <= sy0) sy1 = Math.Min(size.Height, sy0 + 1);

            for (int x = 0; x < width; x++)
            {
                int sx0 = (int)((long)x * size.Width / width);
                int sx1 = (int)Math.Min(size.Width, ((long)(x + 1) * size.Width + width - 1) / width);
                if (sx1 <= sx0) sx1 = Math.Min(size.Width, sx0 + 1);

                long a = 0, r = 0, g = 0, b = 0;
                int count = 0;

                for (int sy = sy0; sy < sy1; sy++)
                {
                    int rowStart = sy * size.Width * 4;
                    for (int sx = sx0; sx < sx1; sx++)
                    {
                        int i = rowStart + sx * 4;
                        b += src[i + 0];
                        g += src[i + 1];
                        r += src[i + 2];
                        a += src[i + 3];
                        count++;
                    }
                }

                int o = (y * width + x) * 4;
                if (count > 0)
                {
                    dst[o + 0] = (byte)(b / count);
                    dst[o + 1] = (byte)(g / count);
                    dst[o + 2] = (byte)(r / count);
                    dst[o + 3] = (byte)(a / count);
                }
            }
        }

        WritePixels(result, dst);
        return result;
    }

    /// <summary>
    /// Blends every pixel toward a tint colour, leaving alpha untouched so masks and decals
    /// keep their coverage. <paramref name="amount"/> 0 is unchanged, 1 is fully tinted.
    ///
    /// This is a lerp rather than a multiply: tinting a texture red by multiplication also
    /// darkens the blue and green channels to zero, which turns most mid-tones black.
    ///
    /// Returns a NEW bitmap rather than editing in place. Avalonia caches an Image's
    /// rendered bitmap and doesn't repaint just because a WriteableBitmap's pixels changed,
    /// so an in-place edit is invisible until something else invalidates it. Crop and
    /// Resize already allocate; this keeps all three operations consistent.
    /// </summary>
    public static WriteableBitmap Recolor(WriteableBitmap source, byte tintR, byte tintG, byte tintB, double amount)
    {
        amount = Math.Clamp(amount, 0.0, 1.0);

        var result = Copy(source);
        if (amount <= 0.0) return result;

        byte[] pixels = ExtractPixels(result);

        for (int i = 0; i < pixels.Length; i += 4)
        {
            pixels[i + 0] = (byte)(pixels[i + 0] + (tintB - pixels[i + 0]) * amount);
            pixels[i + 1] = (byte)(pixels[i + 1] + (tintG - pixels[i + 1]) * amount);
            pixels[i + 2] = (byte)(pixels[i + 2] + (tintR - pixels[i + 2]) * amount);
        }

        WritePixels(result, pixels);
        return result;
    }

    /// <summary>Pixel-for-pixel copy, as a fresh bitmap the UI will treat as a new image.</summary>
    public static WriteableBitmap Copy(WriteableBitmap source)
    {
        var size = source.PixelSize;
        var result = CreateBitmap(size.Width, size.Height);
        WritePixels(result, ExtractPixels(source));
        return result;
    }

    /// <summary>Replaces RGB while preserving the source alpha channel.</summary>
    public static void Flatten(WriteableBitmap target, byte r, byte g, byte b)
    {
        byte[] pixels = ExtractPixels(target);

        for (int i = 0; i < pixels.Length; i += 4)
        {
            pixels[i + 0] = b;
            pixels[i + 1] = g;
            pixels[i + 2] = r;
        }

        WritePixels(target, pixels);
    }

    public static void Save(WriteableBitmap bitmap, string path)
    {
        string dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        bitmap.Save(path);
    }

    static WriteableBitmap CreateBitmap(int width, int height)
    {
        return new WriteableBitmap(
            new PixelSize(width, height),
            new Vector(96, 96),
            PixelFormat.Bgra8888,
            AlphaFormat.Premul);
    }
}