using System;
using System.Collections.Generic;
using System.IO;
using StbImageSharp;

namespace RedHoleEngine.Rendering;

/// <summary>
/// Thin wrapper over StbImageSharp (pure C#, MIT). Output is always top-row-first,
/// 4 bytes per pixel RGBA (or 3 floats per pixel RGB for HDR).
/// </summary>
internal static class ImageDecoder
{
    internal readonly record struct Rgba8(int Width, int Height, byte[] Pixels);
    internal readonly record struct RgbFloat(int Width, int Height, float[] Pixels);

    public static Rgba8 LoadRgba(string path)
    {
        using var stream = File.OpenRead(path);
        return LoadRgba(stream);
    }

    public static Rgba8 LoadRgba(Stream stream)
    {
        var image = ImageResult.FromStream(stream, ColorComponents.RedGreenBlueAlpha);
        return new Rgba8(image.Width, image.Height, image.Data);
    }

    public static Rgba8 LoadRgba(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes);
        return LoadRgba(stream);
    }

    /// <summary>Decode every frame of a GIF (each is a full composited canvas).</summary>
    public static List<Rgba8> LoadGifFrames(string path)
    {
        // Non-GIF input (e.g. a PNG) yields a single frame, matching the previous behavior.
        var bytes = File.ReadAllBytes(path);
        if (bytes.Length < 3 || bytes[0] != (byte)'G' || bytes[1] != (byte)'I' || bytes[2] != (byte)'F')
            return new List<Rgba8> { LoadRgba(bytes) };

        using var stream = new MemoryStream(bytes);
        return LoadGifFrames(stream);
    }

    public static List<Rgba8> LoadGifFrames(Stream stream)
    {
        var frames = new List<Rgba8>();
        foreach (var frame in ImageResult.AnimatedGifFramesFromStream(stream, ColorComponents.RedGreenBlueAlpha))
        {
            // The enumerator reuses its buffer between frames, so copy each one.
            frames.Add(new Rgba8(frame.Width, frame.Height, (byte[])frame.Data.Clone()));
        }
        return frames;
    }

    /// <summary>Decode a Radiance .hdr file to linear RGB floats (3 per pixel).</summary>
    public static RgbFloat LoadHdrRgb(string path)
    {
        using var stream = File.OpenRead(path);
        return LoadHdrRgb(stream);
    }

    public static RgbFloat LoadHdrRgb(Stream stream)
    {
        var image = ImageResultFloat.FromStream(stream, ColorComponents.RedGreenBlue);
        return new RgbFloat(image.Width, image.Height, image.Data);
    }

    /// <summary>
    /// Box-filter resize of an RGBA8 image (averages the covered source pixels; bilinear when upscaling).
    /// </summary>
    public static Rgba8 Resize(Rgba8 src, int newWidth, int newHeight)
    {
        newWidth = Math.Max(1, newWidth);
        newHeight = Math.Max(1, newHeight);
        if (newWidth == src.Width && newHeight == src.Height)
            return src;

        var dst = new byte[newWidth * newHeight * 4];
        float sx = (float)src.Width / newWidth;
        float sy = (float)src.Height / newHeight;

        for (int y = 0; y < newHeight; y++)
        {
            for (int x = 0; x < newWidth; x++)
            {
                int o = (y * newWidth + x) * 4;
                if (sx <= 1f && sy <= 1f)
                {
                    SampleBilinear(src, (x + 0.5f) * sx - 0.5f, (y + 0.5f) * sy - 0.5f, dst, o);
                    continue;
                }

                int x0 = (int)MathF.Floor(x * sx), x1 = Math.Min(src.Width, Math.Max(x0 + 1, (int)MathF.Ceiling((x + 1) * sx)));
                int y0 = (int)MathF.Floor(y * sy), y1 = Math.Min(src.Height, Math.Max(y0 + 1, (int)MathF.Ceiling((y + 1) * sy)));
                float r = 0, g = 0, b = 0, a = 0;
                int count = 0;
                for (int yy = y0; yy < y1; yy++)
                {
                    for (int xx = x0; xx < x1; xx++)
                    {
                        int i = (yy * src.Width + xx) * 4;
                        r += src.Pixels[i]; g += src.Pixels[i + 1]; b += src.Pixels[i + 2]; a += src.Pixels[i + 3];
                        count++;
                    }
                }
                dst[o] = (byte)(r / count + 0.5f);
                dst[o + 1] = (byte)(g / count + 0.5f);
                dst[o + 2] = (byte)(b / count + 0.5f);
                dst[o + 3] = (byte)(a / count + 0.5f);
            }
        }
        return new Rgba8(newWidth, newHeight, dst);
    }

    private static void SampleBilinear(Rgba8 src, float fx, float fy, byte[] dst, int o)
    {
        fx = Math.Clamp(fx, 0f, src.Width - 1);
        fy = Math.Clamp(fy, 0f, src.Height - 1);
        int x0 = (int)fx, y0 = (int)fy;
        int x1 = Math.Min(x0 + 1, src.Width - 1), y1 = Math.Min(y0 + 1, src.Height - 1);
        float tx = fx - x0, ty = fy - y0;
        for (int c = 0; c < 4; c++)
        {
            float top = src.Pixels[(y0 * src.Width + x0) * 4 + c] * (1 - tx) + src.Pixels[(y0 * src.Width + x1) * 4 + c] * tx;
            float bot = src.Pixels[(y1 * src.Width + x0) * 4 + c] * (1 - tx) + src.Pixels[(y1 * src.Width + x1) * 4 + c] * tx;
            dst[o + c] = (byte)(top * (1 - ty) + bot * ty + 0.5f);
        }
    }
}
