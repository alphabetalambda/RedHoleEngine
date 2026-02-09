using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace RedHoleEngine.Rendering.UI;

public static class UiMediaLoader
{
    private static readonly Dictionary<string, List<UiTextureFrame>> VideoCache = new(StringComparer.OrdinalIgnoreCase);

    public static UiTextureFrame LoadImage(string path, int version)
    {
        using var image = Image.Load<Rgba32>(path);
        var rgba = new byte[image.Width * image.Height * 4];
        image.CopyPixelDataTo(rgba);
        return new UiTextureFrame(image.Width, image.Height, rgba, version);
    }

    public static List<UiTextureFrame> LoadGifFrames(string path, int baseVersion)
    {
        using var image = Image.Load<Rgba32>(path);
        var frames = new List<UiTextureFrame>();
        int version = baseVersion;

        for (int i = 0; i < image.Frames.Count; i++)
        {
            var frame = image.Frames.CloneFrame(i);
            var rgba = new byte[frame.Width * frame.Height * 4];
            frame.CopyPixelDataTo(rgba);
            frames.Add(new UiTextureFrame(frame.Width, frame.Height, rgba, version++));
        }

        return frames;
    }

    public static List<UiTextureFrame> LoadImageSequence(string directory, int baseVersion)
    {
        var files = Directory.GetFiles(directory)
            .Where(file => file.EndsWith(".png", StringComparison.OrdinalIgnoreCase)
                        || file.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase)
                        || file.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase))
            .OrderBy(file => file)
            .ToList();

        var frames = new List<UiTextureFrame>();
        int version = baseVersion;
        foreach (var file in files)
        {
            frames.Add(LoadImage(file, version++));
        }

        return frames;
    }

    public static List<UiTextureFrame> LoadVideoFrames(string path, float fps, int baseVersion)
    {
        var cacheKey = $"{path}|{fps:0.00}";
        if (VideoCache.TryGetValue(cacheKey, out var cached))
            return cached;

        var tempDir = Path.Combine(Path.GetTempPath(), "redhole_ui", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var outputPattern = Path.Combine(tempDir, "frame_%05d.png");

        if (!TryRunFfmpeg(path, outputPattern, fps))
        {
            return new List<UiTextureFrame>();
        }

        var frames = LoadImageSequence(tempDir, baseVersion);
        VideoCache[cacheKey] = frames;
        return frames;
    }

    private static bool TryRunFfmpeg(string inputPath, string outputPattern, float fps)
    {
        if (!IsFfmpegAvailable())
            return false;

        var args = $"-y -i \"{inputPath}\" -vf fps={fps:0.##} \"{outputPattern}\"";
        var info = new ProcessStartInfo
        {
            FileName = "ffmpeg",
            Arguments = args,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        try
        {
            using var process = Process.Start(info);
            if (process == null)
                return false;
            process.WaitForExit(10000);
            return process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    private static bool IsFfmpegAvailable()
    {
        try
        {
            var info = new ProcessStartInfo
            {
                FileName = "ffmpeg",
                Arguments = "-version",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(info);
            if (process == null)
                return false;
            process.WaitForExit(2000);
            return process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }
}
