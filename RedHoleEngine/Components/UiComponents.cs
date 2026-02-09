using System.Numerics;
using RedHoleEngine.Core.ECS;

namespace RedHoleEngine.Components;

public struct UiRectComponent : IComponent
{
    public Vector2 Position;
    public Vector2 Size;
    public Vector4 Color;
    public int Layer;

    public UiRectComponent(Vector2 position, Vector2 size, Vector4 color, int layer = 0)
    {
        Position = position;
        Size = size;
        Color = color;
        Layer = layer;
    }
}

public struct UiTextComponent : IComponent
{
    public Vector2 Position;
    public string Text;
    public Vector4 Color;
    public float Scale;
    public int Layer;

    public UiTextComponent(Vector2 position, string text, Vector4 color, float scale = 1f, int layer = 0)
    {
        Position = position;
        Text = text;
        Color = color;
        Scale = scale;
        Layer = layer;
    }
}

public struct UiImageComponent : IComponent
{
    public Vector2 Position;
    public Vector2 Size;
    public string SourcePath;
    public Vector4 Tint;
    public int MediaId;
    public int Layer;

    public UiImageComponent(Vector2 position, Vector2 size, string sourcePath, Vector4 tint, int layer = 0)
    {
        Position = position;
        Size = size;
        SourcePath = sourcePath;
        Tint = tint;
        MediaId = 0;
        Layer = layer;
    }

    public UiImageComponent(Vector2 position, Vector2 size, int mediaId, Vector4 tint, int layer = 0)
    {
        Position = position;
        Size = size;
        SourcePath = string.Empty;
        Tint = tint;
        MediaId = mediaId;
        Layer = layer;
    }
}

public struct UiVideoComponent : IComponent
{
    public Vector2 Position;
    public Vector2 Size;
    public string SourcePath;
    public float FramesPerSecond;
    public bool Loop;
    public bool Playing;
    public int MediaId;
    public int Layer;

    public UiVideoComponent(Vector2 position, Vector2 size, string sourcePath, float framesPerSecond = 12f, bool loop = true, bool playing = true, int layer = 0)
    {
        Position = position;
        Size = size;
        SourcePath = sourcePath;
        FramesPerSecond = framesPerSecond;
        Loop = loop;
        Playing = playing;
        MediaId = 0;
        Layer = layer;
    }

    public UiVideoComponent(Vector2 position, Vector2 size, int mediaId, float framesPerSecond = 12f, bool loop = true, bool playing = true, int layer = 0)
    {
        Position = position;
        Size = size;
        SourcePath = string.Empty;
        FramesPerSecond = framesPerSecond;
        Loop = loop;
        Playing = playing;
        MediaId = mediaId;
        Layer = layer;
    }
}
