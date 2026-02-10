using System;
using System.Numerics;

namespace RedHoleEngine.Rendering.Raytracing;

public struct RaytracerBvhNode
{
    public Vector3 BoundsMin;
    public int LeftFirst;
    public Vector3 BoundsMax;
    public int TriCount;
}

/// <summary>
/// Triangle data for raytracing with UV coordinates.
/// Layout matches GpuTriangle in the shader (128 bytes total).
/// </summary>
public struct RaytracerTriangle
{
    // Vertex positions (48 bytes)
    public Vector3 V0;           // 12 bytes
    public int MaterialIndex;    // 4 bytes
    public Vector3 V1;           // 12 bytes
    public int Pad1;             // 4 bytes
    public Vector3 V2;           // 12 bytes
    public int Pad2;             // 4 bytes
    
    // Normal (16 bytes)
    public Vector3 Normal;       // 12 bytes
    public int Pad3;             // 4 bytes
    
    // UV coordinates for each vertex (24 bytes)
    public Vector2 UV0;          // 8 bytes
    public Vector2 UV1;          // 8 bytes
    public Vector2 UV2;          // 8 bytes
    
    // Tangent for normal mapping (16 bytes)
    public Vector4 Tangent;      // 16 bytes (xyz = tangent, w = handedness)
    
    // Colors (32 bytes, kept for backward compatibility)
    public Vector4 Albedo;       // 16 bytes
    public Vector4 Emissive;     // 16 bytes
    
    // Total: 48 + 16 + 24 + 16 + 32 = 136 bytes
    // Padded to 144 bytes for alignment
}

public class RaytracerMeshData
{
    public RaytracerBvhNode[] Nodes { get; set; } = Array.Empty<RaytracerBvhNode>();
    public RaytracerTriangle[] Triangles { get; set; } = Array.Empty<RaytracerTriangle>();
}
