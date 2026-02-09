using System;
using System.Numerics;

namespace RedHoleEngine.Rendering.Rasterization;

public struct RasterVertex
{
    public Vector3 Position;
    public Vector4 Color;
}

public class RasterMeshData
{
    public RasterVertex[] Vertices { get; set; } = Array.Empty<RasterVertex>();
    public uint[] Indices { get; set; } = Array.Empty<uint>();
}
