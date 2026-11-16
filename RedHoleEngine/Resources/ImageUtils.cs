using System;
using System.Collections.Generic;
using System.IO;
using StbImageSharp;

namespace RedHoleEngine.Resources;

/// <summary>
/// Decoded 8-bit RGBA image. Rows are stored top-down (row 0 is the top of the image),
/// 4 bytes per pixel, no padding.
/// </summary>
public readonly record struct RgbaImage(int Width, int Height, byte[] Rgba);

/// <summary>
/// Decoded floating-point RGB image (e.g. Radiance .hdr). Rows are stored top-down,
/// 3 floats per pixel, linear radiance values.
/// </summary>
public readonly record struct RgbFloatImage(int Width, int Height, float[] Rgb);

/// <summary>
/// Image decoding and pixel helpers built on StbImageSharp.
/// Supported inputs: PNG, JPEG (baseline/progressive), BMP (uncompressed), TGA, PSD (composited),
/// GIF (incl. animated), Radiance HDR, PIC and binary PNM (P5/P6).
/// TIFF, WebP, QOI and ASCII/1-bit PBM (P1-P4) are recognised and rejected with a clear error.
/// Images are never flipped: row 0 is always the top row, as with the previous ImageSharp loader.
/// </summary>
public static class ImageUtils
{
    /// <summary>Decode an image file to 8-bit RGBA.</summary>
    public static RgbaImage LoadRgba8(string path) => LoadRgba8(File.ReadAllBytes(path), path);

    /// <summary>Decode encoded image bytes to 8-bit RGBA.</summary>
    public static RgbaImage LoadRgba8(byte[] data, string? sourceName = null)
    {
        ThrowIfUnsupported(data, sourceName);
        var result = Decode(() => ImageResult.FromMemory(data, ColorComponents.RedGreenBlueAlpha), sourceName);
        return new RgbaImage(result.Width, result.Height, result.Data);
    }

    /// <summary>
    /// Decode every frame of an animated GIF to 8-bit RGBA (each frame fully composited onto the
    /// canvas). Non-GIF input is decoded as a single frame.
    /// </summary>
    public static List<RgbaImage> LoadFramesRgba8(string path) => LoadFramesRgba8(File.ReadAllBytes(path), path);

    /// <inheritdoc cref="LoadFramesRgba8(string)"/>
    public static List<RgbaImage> LoadFramesRgba8(byte[] data, string? sourceName = null)
    {
        if (!IsGif(data))
            return new List<RgbaImage> { LoadRgba8(data, sourceName) };

        var frames = new List<RgbaImage>();
        Decode(() =>
        {
            using var stream = new MemoryStream(data, writable: false);
            foreach (var frame in ImageResult.AnimatedGifFramesFromStream(stream, ColorComponents.RedGreenBlueAlpha))
            {
                // The enumerator may reuse its frame buffer, so copy each frame out.
                frames.Add(new RgbaImage(frame.Width, frame.Height, (byte[])frame.Data.Clone()));
            }
            return frames;
        }, sourceName);

        if (frames.Count == 0)
            throw new InvalidDataException($"GIF contains no frames: {sourceName ?? "<memory>"}");
        return frames;
    }

    /// <summary>Decode a Radiance .hdr image to linear float RGB.</summary>
    public static RgbFloatImage LoadRgbFloat(string path) => LoadRgbFloat(File.ReadAllBytes(path), path);

    /// <inheritdoc cref="LoadRgbFloat(string)"/>
    public static RgbFloatImage LoadRgbFloat(byte[] data, string? sourceName = null)
    {
        ThrowIfUnsupported(data, sourceName);
        var result = Decode(() => ImageResultFloat.FromMemory(data, ColorComponents.RedGreenBlue), sourceName);
        return new RgbFloatImage(result.Width, result.Height, result.Data);
    }

    /// <summary>
    /// Identify image formats that the previous decoder (ImageSharp) accepted but StbImageSharp
    /// cannot decode. Returns the format name, or null if the data is not one of them.
    /// </summary>
    public static string? DetectUnsupportedFormat(ReadOnlySpan<byte> data)
    {
        if (data.Length >= 12 && data[..4].SequenceEqual("RIFF"u8) && data.Slice(8, 4).SequenceEqual("WEBP"u8))
            return "WebP";
        if (data.Length >= 4 && (data[..4].SequenceEqual("II*\0"u8) || data[..4].SequenceEqual("MM\0*"u8)))
            return "TIFF";
        if (data.Length >= 4 && data[..4].SequenceEqual("qoif"u8))
            return "QOI";
        if (data.Length >= 2 && data[0] == (byte)'P' && data[1] >= (byte)'1' && data[1] <= (byte)'4')
            return $"PBM/PNM (P{(char)data[1]})";
        return null;
    }

    /// <summary>
    /// Resize an 8-bit RGBA image. Each axis is filtered independently: downscaling uses an exact
    /// box (area-average) filter, upscaling uses bilinear interpolation with edge clamping.
    /// Filtering is done on premultiplied alpha so transparent pixels do not bleed colour.
    /// </summary>
    public static byte[] ResizeRgba8(byte[] rgba, int width, int height, int newWidth, int newHeight)
    {
        if (width <= 0 || height <= 0)
            throw new ArgumentOutOfRangeException(nameof(width), "Source dimensions must be positive.");
        if (newWidth <= 0 || newHeight <= 0)
            throw new ArgumentOutOfRangeException(nameof(newWidth), "Target dimensions must be positive.");
        if (rgba.Length < width * height * 4)
            throw new ArgumentException("Pixel buffer is smaller than width * height * 4.", nameof(rgba));

        if (newWidth == width && newHeight == height)
            return (byte[])rgba.Clone();

        // Premultiply into float.
        var src = new float[width * height * 4];
        for (int i = 0; i < width * height; i++)
        {
            float a = rgba[i * 4 + 3] / 255f;
            src[i * 4] = rgba[i * 4] * a;
            src[i * 4 + 1] = rgba[i * 4 + 1] * a;
            src[i * 4 + 2] = rgba[i * 4 + 2] * a;
            src[i * 4 + 3] = rgba[i * 4 + 3];
        }

        var xWeights = ComputeWeights(width, newWidth);
        var yWeights = ComputeWeights(height, newHeight);

        // Horizontal pass: width -> newWidth, for every source row.
        var tmp = new float[newWidth * height * 4];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < newWidth; x++)
            {
                float r = 0, g = 0, b = 0, a = 0;
                foreach (var (index, weight) in xWeights[x])
                {
                    int s = (y * width + index) * 4;
                    r += src[s] * weight;
                    g += src[s + 1] * weight;
                    b += src[s + 2] * weight;
                    a += src[s + 3] * weight;
                }
                int d = (y * newWidth + x) * 4;
                tmp[d] = r;
                tmp[d + 1] = g;
                tmp[d + 2] = b;
                tmp[d + 3] = a;
            }
        }

        // Vertical pass: height -> newHeight, then un-premultiply.
        var output = new byte[newWidth * newHeight * 4];
        for (int y = 0; y < newHeight; y++)
        {
            for (int x = 0; x < newWidth; x++)
            {
                float r = 0, g = 0, b = 0, a = 0;
                foreach (var (index, weight) in yWeights[y])
                {
                    int s = (index * newWidth + x) * 4;
                    r += tmp[s] * weight;
                    g += tmp[s + 1] * weight;
                    b += tmp[s + 2] * weight;
                    a += tmp[s + 3] * weight;
                }

                int d = (y * newWidth + x) * 4;
                float alpha = a / 255f;
                if (alpha > 0f)
                {
                    output[d] = ToByte(r / alpha);
                    output[d + 1] = ToByte(g / alpha);
                    output[d + 2] = ToByte(b / alpha);
                }
                output[d + 3] = ToByte(a);
            }
        }

        return output;
    }

    /// <summary>
    /// Per-destination-sample source contributions for one axis. Weights for each sample sum to 1.
    /// </summary>
    internal static (int Index, float Weight)[][] ComputeWeights(int srcSize, int dstSize)
    {
        var weights = new (int, float)[dstSize][];
        double scale = (double)srcSize / dstSize;

        for (int i = 0; i < dstSize; i++)
        {
            if (dstSize < srcSize)
            {
                // Box filter: average the source interval [i*scale, (i+1)*scale) by overlap.
                double start = i * scale;
                double end = (i + 1) * scale;
                int first = (int)Math.Floor(start);
                int last = Math.Min(srcSize - 1, (int)Math.Ceiling(end) - 1);
                var list = new List<(int, float)>(last - first + 1);
                for (int j = first; j <= last; j++)
                {
                    double overlap = Math.Min(end, j + 1) - Math.Max(start, j);
                    if (overlap > 0)
                        list.Add((j, (float)(overlap / scale)));
                }
                weights[i] = list.ToArray();
            }
            else
            {
                // Bilinear: sample at the destination pixel centre mapped into source space.
                double center = (i + 0.5) * scale - 0.5;
                int j0 = (int)Math.Floor(center);
                float t = (float)(center - j0);
                int a = Math.Clamp(j0, 0, srcSize - 1);
                int b = Math.Clamp(j0 + 1, 0, srcSize - 1);
                weights[i] = t == 0f || a == b
                    ? new[] { (a, 1f) }
                    : new[] { (a, 1f - t), (b, t) };
            }
        }

        return weights;
    }

    private static byte ToByte(float value) => (byte)Math.Clamp((int)(value + 0.5f), 0, 255);

    private static bool IsGif(ReadOnlySpan<byte> data) =>
        data.Length >= 6 && data[..3].SequenceEqual("GIF"u8);

    private static void ThrowIfUnsupported(byte[] data, string? sourceName)
    {
        var format = DetectUnsupportedFormat(data);
        if (format == null)
            return;

        var message = $"Unsupported image format {format}: {sourceName ?? "<memory>"}. " +
                      "Supported formats are PNG, JPEG, BMP, TGA, PSD, GIF, HDR, PIC and binary PNM (P5/P6); " +
                      "convert the asset to PNG.";
        Console.Error.WriteLine(message);
        throw new NotSupportedException(message);
    }

    private static T Decode<T>(Func<T> decode, string? sourceName)
    {
        try
        {
            return decode();
        }
        catch (Exception ex) when (ex is not NotSupportedException and not InvalidDataException)
        {
            throw new InvalidDataException($"Failed to decode image {sourceName ?? "<memory>"}: {ex.Message}", ex);
        }
    }
}
