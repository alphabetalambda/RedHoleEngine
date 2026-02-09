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

public struct RaytracerTriangle
{
    public Vector3 V0;
    public int MaterialIndex;
    public Vector3 V1;
    public int Pad1;
    public Vector3 V2;
    public int Pad2;
    public Vector3 Normal;
    public int Pad3;
    public Vector4 Albedo;
    public Vector4 Emissive;
}

public class RaytracerMeshData
{
    public RaytracerBvhNode[] Nodes { get; set; } = Array.Empty<RaytracerBvhNode>();
    public RaytracerTriangle[] Triangles { get; set; } = Array.Empty<RaytracerTriangle>();
}
