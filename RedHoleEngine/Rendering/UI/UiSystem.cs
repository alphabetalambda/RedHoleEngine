using System;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using RedHoleEngine.Components;
using RedHoleEngine.Core.ECS;

namespace RedHoleEngine.Rendering.UI;

public sealed class UiSystem : GameSystem
{
    private readonly UiDrawData _drawData = new();
    private Vector2 _viewportSize;
    private float _time;
    private int _nextTextureId = 1;
    private readonly Dictionary<string, UiTextureEntry> _imageCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, UiVideoEntry> _videoCache = new(StringComparer.OrdinalIgnoreCase);

    public string ResourceBasePath { get; set; } = "";

    public UiDrawData DrawData => _drawData;

    public void SetViewportSize(int width, int height)
    {
        _viewportSize = new Vector2(width, height);
    }

    public override void Update(float deltaTime)
    {
        if (World == null)
            return;

        _time += deltaTime;
        _drawData.Clear();

        var elements = new List<UiElement>();
        foreach (var entity in World.Query<UiRectComponent>())
        {
            ref var rect = ref World.GetComponent<UiRectComponent>(entity);
            elements.Add(new UiElement(rect.Layer, rect));
        }
        foreach (var entity in World.Query<UiTextComponent>())
        {
            ref var text = ref World.GetComponent<UiTextComponent>(entity);
            elements.Add(new UiElement(text.Layer, text));
        }
        foreach (var entity in World.Query<UiImageComponent>())
        {
            ref var image = ref World.GetComponent<UiImageComponent>(entity);
            elements.Add(new UiElement(image.Layer, image));
        }
        foreach (var entity in World.Query<UiVideoComponent>())
        {
            ref var video = ref World.GetComponent<UiVideoComponent>(entity);
            elements.Add(new UiElement(video.Layer, video));
        }

        elements.Sort((a, b) => a.Layer.CompareTo(b.Layer));

        foreach (var element in elements)
        {
            if (element.IsText)
            {
                AddText(element.Text);
            }
            else if (element.IsImage)
            {
                AddImage(element.Image);
            }
            else if (element.IsVideo)
            {
                AddVideo(element.Video);
            }
            else
            {
                AddRect(element.Rect);
            }
        }
    }

    private void AddRect(UiRectComponent rect)
    {
        var uv = UiFontAtlas.GetGlyphUv(UiFontAtlas.WhiteGlyphIndex);
        AddQuad(0, rect.Position, rect.Size, rect.Color, uv);
    }

    private void AddText(UiTextComponent text)
    {
        var scale = Math.Max(0.5f, text.Scale);
        var pos = text.Position;
        foreach (var ch in text.Text ?? string.Empty)
        {
            if (ch == '\n')
            {
                pos.X = text.Position.X;
                pos.Y += UiFontAtlas.GlyphSize * scale;
                continue;
            }

            int glyphIndex = ch;
            var uv = UiFontAtlas.GetGlyphUv(glyphIndex);
            AddQuad(0, pos, new Vector2(UiFontAtlas.GlyphSize * scale, UiFontAtlas.GlyphSize * scale), text.Color, uv);
            pos.X += UiFontAtlas.GlyphSize * scale;
        }
    }

    private void AddImage(UiImageComponent image)
    {
        var sourcePath = image.SourcePath;
        if (image.MediaId > 0)
        {
            var registryEntry = UiMediaRegistry.Get(image.MediaId);
            if (registryEntry != null)
            {
                sourcePath = registryEntry.Path;
            }
        }

        var path = ResolvePath(sourcePath);
        if (string.IsNullOrWhiteSpace(path))
            return;

        var imageEntry = GetOrLoadImage(path);
        _drawData.Textures[imageEntry.TextureId] = imageEntry.Frame;
        var uv = new Vector4(0f, 0f, 1f, 1f);
        AddQuad(imageEntry.TextureId, image.Position, image.Size, image.Tint, uv);
    }

    private void AddVideo(UiVideoComponent video)
    {
        var sourcePath = video.SourcePath;
        float fps = video.FramesPerSecond;
        bool loop = video.Loop;
        bool playing = video.Playing;

        if (video.MediaId > 0)
        {
            var registryEntry = UiMediaRegistry.Get(video.MediaId);
            if (registryEntry != null)
            {
                sourcePath = registryEntry.Path;
                fps = fps <= 0 ? registryEntry.FramesPerSecond : fps;
                loop = loop || registryEntry.Loop;
            }
        }

        var path = ResolvePath(sourcePath);
        if (string.IsNullOrWhiteSpace(path))
            return;

        var videoEntry = GetOrLoadVideo(path, fps, loop, playing);
        if (videoEntry.Frames.Count == 0)
            return;

        int frameIndex = videoEntry.Playing ? (int)(_time * videoEntry.FramesPerSecond) : videoEntry.FrameIndex;
        if (videoEntry.Loop)
        {
            frameIndex %= videoEntry.Frames.Count;
        }
        else
        {
            frameIndex = Math.Clamp(frameIndex, 0, videoEntry.Frames.Count - 1);
        }

        videoEntry.FrameIndex = frameIndex;
        var frame = videoEntry.Frames[frameIndex];
        _drawData.Textures[videoEntry.TextureId] = frame;

        var uv = new Vector4(0f, 0f, 1f, 1f);
        AddQuad(videoEntry.TextureId, video.Position, video.Size, Vector4.One, uv);
    }

    private void AddQuad(int textureId, Vector2 position, Vector2 size, Vector4 color, Vector4 uv)
    {
        var p0 = position;
        var p1 = position + new Vector2(size.X, 0f);
        var p2 = position + size;
        var p3 = position + new Vector2(0f, size.Y);

        var v0 = new UiVertex { Position = p0, UV = new Vector2(uv.X, uv.Y), Color = color };
        var v1 = new UiVertex { Position = p1, UV = new Vector2(uv.Z, uv.Y), Color = color };
        var v2 = new UiVertex { Position = p2, UV = new Vector2(uv.Z, uv.W), Color = color };
        var v3 = new UiVertex { Position = p0, UV = new Vector2(uv.X, uv.Y), Color = color };
        var v4 = new UiVertex { Position = p2, UV = new Vector2(uv.Z, uv.W), Color = color };
        var v5 = new UiVertex { Position = p3, UV = new Vector2(uv.X, uv.W), Color = color };
        _drawData.AddVertices(textureId, v0, v1, v2, v3, v4, v5);
    }

    private readonly struct UiElement
    {
        public UiElement(int layer, UiRectComponent rect)
        {
            Layer = layer;
            Rect = rect;
            Text = default;
            Image = default;
            Video = default;
            IsText = false;
            IsImage = false;
            IsVideo = false;
        }

        public UiElement(int layer, UiTextComponent text)
        {
            Layer = layer;
            Rect = default;
            Text = text;
            Image = default;
            Video = default;
            IsText = true;
            IsImage = false;
            IsVideo = false;
        }

        public UiElement(int layer, UiImageComponent image)
        {
            Layer = layer;
            Rect = default;
            Text = default;
            Image = image;
            Video = default;
            IsText = false;
            IsImage = true;
            IsVideo = false;
        }

        public UiElement(int layer, UiVideoComponent video)
        {
            Layer = layer;
            Rect = default;
            Text = default;
            Image = default;
            Video = video;
            IsText = false;
            IsImage = false;
            IsVideo = true;
        }

        public int Layer { get; }
        public UiRectComponent Rect { get; }
        public UiTextComponent Text { get; }
        public UiImageComponent Image { get; }
        public UiVideoComponent Video { get; }
        public bool IsText { get; }
        public bool IsImage { get; }
        public bool IsVideo { get; }
    }

    private string ResolvePath(string sourcePath)
    {
        if (string.IsNullOrWhiteSpace(sourcePath))
            return string.Empty;

        if (Path.IsPathRooted(sourcePath))
            return sourcePath;

        if (!string.IsNullOrWhiteSpace(ResourceBasePath))
        {
            var combined = Path.Combine(ResourceBasePath, sourcePath);
            return combined;
        }

        return Path.Combine(AppContext.BaseDirectory, sourcePath);
    }

    private UiTextureEntry GetOrLoadImage(string path)
    {
        if (_imageCache.TryGetValue(path, out var entry))
            return entry;

        UiTextureFrame frame;
        try
        {
            // Check if this is the branding logo and use fallback if file not found
            if (path.Contains("redhole_logo", StringComparison.OrdinalIgnoreCase) ||
                path.Contains("Branding", StringComparison.OrdinalIgnoreCase))
            {
                frame = UiMediaLoader.LoadImageWithFallback(
                    path, 
                    RedHoleEngine.Assets.Branding.BrandingAssets.PlaceholderLogoBase64, 
                    1);
            }
            else
            {
                frame = UiMediaLoader.LoadImage(path, 1);
            }
        }
        catch
        {
            frame = CreateFallbackFrame();
        }
        entry = new UiTextureEntry(_nextTextureId++, frame);
        _imageCache[path] = entry;
        return entry;
    }

    private UiVideoEntry GetOrLoadVideo(string path, float fps, bool loop, bool playing)
    {
        if (_videoCache.TryGetValue(path, out var entry))
        {
            entry.FramesPerSecond = fps <= 0 ? 12f : fps;
            entry.Loop = loop;
            entry.Playing = playing;
            return entry;
        }

        var frames = new List<UiTextureFrame>();
        int version = 1;

        try
        {
            if (Directory.Exists(path))
            {
                frames = UiMediaLoader.LoadImageSequence(path, version);
            }
            else
            {
                var extension = Path.GetExtension(path).ToLowerInvariant();
                if (extension == ".gif")
                {
                    frames = UiMediaLoader.LoadGifFrames(path, version);
                }
                else if (extension == ".mp4" || extension == ".mov" || extension == ".mkv" || extension == ".webm")
                {
                    frames = UiMediaLoader.LoadVideoFrames(path, fps <= 0 ? 12f : fps, version);
                }
                else
                {
                    frames = new List<UiTextureFrame> { UiMediaLoader.LoadImage(path, version) };
                }
            }
        }
        catch
        {
            frames = new List<UiTextureFrame> { CreateFallbackFrame() };
        }

        entry = new UiVideoEntry(_nextTextureId++, frames)
        {
            FramesPerSecond = fps <= 0 ? 12f : fps,
            Loop = loop,
            Playing = playing
        };

        _videoCache[path] = entry;
        return entry;
    }

    private sealed class UiTextureEntry
    {
        public UiTextureEntry(int textureId, UiTextureFrame frame)
        {
            TextureId = textureId;
            Frame = frame;
        }

        public int TextureId { get; }
        public UiTextureFrame Frame { get; }
    }

    private sealed class UiVideoEntry
    {
        public UiVideoEntry(int textureId, List<UiTextureFrame> frames)
        {
            TextureId = textureId;
            Frames = frames;
            Loop = true;
            Playing = true;
        }

        public int TextureId { get; }
        public List<UiTextureFrame> Frames { get; }
        public float FramesPerSecond { get; set; } = 12f;
        public bool Loop { get; set; }
        public bool Playing { get; set; }
        public int FrameIndex { get; set; }
    }

    private static UiTextureFrame CreateFallbackFrame()
    {
        return new UiTextureFrame(1, 1, new byte[] { 255, 0, 255, 255 }, 1);
    }
}
