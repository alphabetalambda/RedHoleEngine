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
/// Layout matches GpuTriangle in the shader with std430 layout.
/// IMPORTANT: std430 requires vec4 to be 16-byte aligned.
/// </summary>
public struct RaytracerTriangle
{
    // Vertex positions (48 bytes, offset 0)
    public Vector3 V0;           // 12 bytes, offset 0
    public int MaterialIndex;    // 4 bytes, offset 12
    public Vector3 V1;           // 12 bytes, offset 16
    public int Pad1;             // 4 bytes, offset 28
    public Vector3 V2;           // 12 bytes, offset 32
    public int Pad2;             // 4 bytes, offset 44
    
    // Normal (16 bytes, offset 48)
    public Vector3 Normal;       // 12 bytes, offset 48
    public int Pad3;             // 4 bytes, offset 60
    
    // UV coordinates for each vertex (24 bytes, offset 64)
    public Vector2 UV0;          // 8 bytes, offset 64
    public Vector2 UV1;          // 8 bytes, offset 72
    public Vector2 UV2;          // 8 bytes, offset 80
    
    // Padding to align Tangent (vec4) to 16-byte boundary (8 bytes, offset 88)
    public float PadUV0;         // 4 bytes, offset 88
    public float PadUV1;         // 4 bytes, offset 92
    
    // Tangent for normal mapping (16 bytes, offset 96)
    public Vector4 Tangent;      // 16 bytes (xyz = tangent, w = handedness)
    
    // Colors (32 bytes, offset 112)
    public Vector4 Albedo;       // 16 bytes, offset 112
    public Vector4 Emissive;     // 16 bytes, offset 128
    
    // Total: 144 bytes
}

public class RaytracerMeshData
{
    public RaytracerBvhNode[] Nodes { get; set; } = Array.Empty<RaytracerBvhNode>();
    public RaytracerTriangle[] Triangles { get; set; } = Array.Empty<RaytracerTriangle>();
}
