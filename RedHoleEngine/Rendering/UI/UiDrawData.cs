using System.Collections.Generic;
using System.Numerics;
using System.Runtime.InteropServices;

namespace RedHoleEngine.Rendering.UI;

[StructLayout(LayoutKind.Sequential)]
public struct UiVertex
{
    public Vector2 Position;
    public Vector2 UV;
    public Vector4 Color;
}

public readonly struct UiDrawCommand
{
    public UiDrawCommand(int textureId, int vertexOffset, int vertexCount)
    {
        TextureId = textureId;
        VertexOffset = vertexOffset;
        VertexCount = vertexCount;
    }

    public int TextureId { get; }
    public int VertexOffset { get; }
    public int VertexCount { get; }
}

public sealed class UiTextureFrame
{
    public UiTextureFrame(int width, int height, byte[] rgba, int version)
    {
        Width = width;
        Height = height;
        Rgba = rgba;
        Version = version;
    }

    public int Width { get; }
    public int Height { get; }
    public byte[] Rgba { get; }
    public int Version { get; }
}

public sealed class UiDrawData
{
    public List<UiVertex> Vertices { get; } = new();
    public List<UiDrawCommand> Commands { get; } = new();
    public Dictionary<int, UiTextureFrame> Textures { get; } = new();

    public int VertexCount => Vertices.Count;

    public void Clear()
    {
        Vertices.Clear();
        Commands.Clear();
        Textures.Clear();
    }

    public void AddVertices(int textureId, UiVertex v0, UiVertex v1, UiVertex v2, UiVertex v3, UiVertex v4, UiVertex v5)
    {
        int start = Vertices.Count;
        Vertices.Add(v0);
        Vertices.Add(v1);
        Vertices.Add(v2);
        Vertices.Add(v3);
        Vertices.Add(v4);
        Vertices.Add(v5);

        if (Commands.Count > 0 && Commands[^1].TextureId == textureId)
        {
            var last = Commands[^1];
            Commands[^1] = new UiDrawCommand(textureId, last.VertexOffset, last.VertexCount + 6);
        }
        else
        {
            Commands.Add(new UiDrawCommand(textureId, start, 6));
        }
    }
}
