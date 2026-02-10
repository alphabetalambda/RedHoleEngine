using System;
using System.Collections.Generic;
using System.Numerics;
using RedHoleEngine.Components;
using RedHoleEngine.Core.ECS;
using RedHoleEngine.Engine;
using RedHoleEngine.Physics;
using RedHoleEngine.Rendering;
using RedHoleEngine.Rendering.PBR;
using RedHoleEngine.Rendering.Raytracing;
using RedHoleEngine.Resources;

namespace RedHoleEngine.Rendering.Raytracing;

/// <summary>
/// Builds a static BVH for raytraced meshes and uploads to the graphics backend.
/// </summary>
public class RaytracerMeshSystem : GameSystem
{
    private IGraphicsBackend? _backend;
    private Camera? _camera;
    private MaterialLibrary? _materialLibrary;
    private int _lastSceneHash;
    private int _lastTriangleCount;
    private int _lastMaterialHash;

    public override int Priority => -50;

    public void SetBackend(IGraphicsBackend backend)
    {
        _backend = backend;
    }
    
    public void SetCamera(Camera camera)
    {
        _camera = camera;
    }

    /// <summary>
    /// Set the material library for PBR material lookups
    /// </summary>
    public void SetMaterialLibrary(MaterialLibrary library)
    {
        _materialLibrary = library;
    }

    public override void Update(float deltaTime)
    {
        if (_backend == null || !_backend.SupportsComputeShaders)
            return;

        if (_backend.RenderSettings.Mode == RenderMode.Rasterized)
            return;

        var triangles = new List<RaytracerTriangle>(1024);
        var hash = new HashCode();

        foreach (var entity in World.Query<MeshComponent, TransformComponent, RaytracerMeshComponent>())
        {
            ref var meshComponent = ref World.GetComponent<MeshComponent>(entity);
            ref var transform = ref World.GetComponent<TransformComponent>(entity);
            ref var rayComponent = ref World.GetComponent<RaytracerMeshComponent>(entity);

            if (!rayComponent.Enabled)
                continue;

            if (rayComponent.StaticOnly && World.HasComponent<RigidBodyComponent>(entity))
            {
                ref var rb = ref World.GetComponent<RigidBodyComponent>(entity);
                if (rb.Type != RigidBodyType.Static)
                    continue;
            }

            var mesh = meshComponent.MeshHandle.Get();
            if (mesh == null || mesh.Indices.Length < 3)
                continue;

            MaterialComponent material = MaterialComponent.Default;
            if (World.TryGetComponent<MaterialComponent>(entity, out var mat))
                material = mat;

            AddHash(ref hash, meshComponent.MeshHandle.Id, material, transform);

            AppendTriangles(triangles, mesh, transform, material);
        }

        // Add laser segments as emissive geometry
        if (_camera != null)
        {
            foreach (var entity in World.Query<LaserSegmentComponent>())
            {
                ref var segment = ref World.GetComponent<LaserSegmentComponent>(entity);
                AppendLaserSegment(triangles, ref segment, _camera, ref hash);
            }
            // Force rebuild every frame when lasers are present (they move)
            hash.Add(Environment.TickCount);
        }

        int sceneHash = hash.ToHashCode();
        
        // Check if materials need uploading
        int materialHash = _materialLibrary?.GetHashCode() ?? 0;
        bool materialsChanged = materialHash != _lastMaterialHash;
        
        if (materialsChanged && _materialLibrary != null)
        {
            // Upload materials to GPU
            _backend.UploadMaterials(_materialLibrary);
            _lastMaterialHash = materialHash;
        }
        
        if (sceneHash == _lastSceneHash && triangles.Count == _lastTriangleCount && !materialsChanged)
            return;

        var data = RaytracerMeshBuilder.Build(triangles);
        _backend.SetRaytracerMeshData(data);

        _lastSceneHash = sceneHash;
        _lastTriangleCount = triangles.Count;
    }
    
    private static void AppendLaserSegment(
        List<RaytracerTriangle> triangles,
        ref LaserSegmentComponent segment,
        Camera camera,
        ref HashCode hash)
    {
        var start = segment.Start;
        var end = segment.End;
        var beamDir = end - start;
        float length = beamDir.Length();
        if (length < 0.001f)
            return;

        beamDir /= length;

        // Calculate billboard vectors (face camera)
        var toCamera = camera.Position - (start + end) * 0.5f;
        var right = Vector3.Cross(beamDir, toCamera);
        if (right.LengthSquared() < 0.001f)
        {
            right = Vector3.Cross(beamDir, Vector3.UnitY);
            if (right.LengthSquared() < 0.001f)
                right = Vector3.Cross(beamDir, Vector3.UnitX);
        }
        right = Vector3.Normalize(right);

        float halfWidth = segment.Width * 0.5f;
        var offset = right * halfWidth;

        // Main beam quad (2 triangles)
        var p0 = start - offset;
        var p1 = start + offset;
        var p2 = end + offset;
        var p3 = end - offset;

        var normal = Vector3.Normalize(Vector3.Cross(p1 - p0, p2 - p0));
        
        // Emissive color from beam color (make it very bright for visibility)
        var emissive = new Vector4(
            segment.BeamColor.X * segment.Intensity * 25f,
            segment.BeamColor.Y * segment.Intensity * 25f,
            segment.BeamColor.Z * segment.Intensity * 25f,
            1f);

        // Triangle 1
        triangles.Add(new RaytracerTriangle
        {
            V0 = p0, V1 = p1, V2 = p2,
            Normal = normal,
            Albedo = segment.BeamColor,
            Emissive = emissive
        });

        // Triangle 2
        triangles.Add(new RaytracerTriangle
        {
            V0 = p0, V1 = p2, V2 = p3,
            Normal = normal,
            Albedo = segment.BeamColor,
            Emissive = emissive
        });

        // Core layer (brighter, narrower)
        if (segment.CoreWidth > 0)
        {
            var coreOffset = offset * segment.CoreWidth;
            var cp0 = start - coreOffset;
            var cp1 = start + coreOffset;
            var cp2 = end + coreOffset;
            var cp3 = end - coreOffset;

            var coreEmissive = new Vector4(
                segment.CoreColor.X * segment.Intensity * 50f,
                segment.CoreColor.Y * segment.Intensity * 50f,
                segment.CoreColor.Z * segment.Intensity * 50f,
                1f);

            triangles.Add(new RaytracerTriangle
            {
                V0 = cp0, V1 = cp1, V2 = cp2,
                Normal = normal,
                Albedo = segment.CoreColor,
                Emissive = coreEmissive
            });

            triangles.Add(new RaytracerTriangle
            {
                V0 = cp0, V1 = cp2, V2 = cp3,
                Normal = normal,
                Albedo = segment.CoreColor,
                Emissive = coreEmissive
            });
        }

        // Add to hash
        hash.Add(start.X); hash.Add(start.Y); hash.Add(start.Z);
        hash.Add(end.X); hash.Add(end.Y); hash.Add(end.Z);
    }

    private static void AppendTriangles(
        List<RaytracerTriangle> triangles,
        Mesh mesh,
        TransformComponent transform,
        MaterialComponent material)
    {
        var vertices = mesh.Vertices;
        var indices = mesh.Indices;

        for (int i = 0; i + 2 < indices.Length; i += 3)
        {
            int i0 = (int)indices[i];
            int i1 = (int)indices[i + 1];
            int i2 = (int)indices[i + 2];

            var v0 = transform.Transform.TransformPoint(vertices[i0].Position);
            var v1 = transform.Transform.TransformPoint(vertices[i1].Position);
            var v2 = transform.Transform.TransformPoint(vertices[i2].Position);

            var normal = Vector3.Normalize(Vector3.Cross(v1 - v0, v2 - v0));
            
            // Extract UV coordinates from mesh vertices
            var uv0 = vertices[i0].TexCoord;
            var uv1 = vertices[i1].TexCoord;
            var uv2 = vertices[i2].TexCoord;
            
            // Transform tangent to world space (only xyz, preserve w for handedness)
            var tangent = vertices[i0].Tangent;
            var tangentDir = Vector3.TransformNormal(
                new Vector3(tangent.X, tangent.Y, tangent.Z),
                transform.Transform.WorldMatrix);
            tangentDir = Vector3.Normalize(tangentDir);
            var worldTangent = new Vector4(tangentDir, tangent.W);

            triangles.Add(new RaytracerTriangle
            {
                V0 = v0,
                V1 = v1,
                V2 = v2,
                MaterialIndex = material.PbrMaterialId, // -1 if using inline, or valid material index
                Normal = normal,
                UV0 = uv0,
                UV1 = uv1,
                UV2 = uv2,
                Tangent = worldTangent,
                Albedo = material.BaseColor,
                Emissive = new Vector4(material.EmissiveColor, 1f)
            });
        }
    }

    private static void AddHash(ref HashCode hash, string meshId, MaterialComponent material, TransformComponent transform)
    {
        hash.Add(meshId ?? string.Empty);
        hash.Add(material.PbrMaterialId); // Include PBR material ID in hash
        hash.Add(material.BaseColor.X);
        hash.Add(material.BaseColor.Y);
        hash.Add(material.BaseColor.Z);
        hash.Add(material.BaseColor.W);
        hash.Add(material.EmissiveColor.X);
        hash.Add(material.EmissiveColor.Y);
        hash.Add(material.EmissiveColor.Z);

        var m = transform.Transform.WorldMatrix;
        hash.Add(m.M11); hash.Add(m.M12); hash.Add(m.M13); hash.Add(m.M14);
        hash.Add(m.M21); hash.Add(m.M22); hash.Add(m.M23); hash.Add(m.M24);
        hash.Add(m.M31); hash.Add(m.M32); hash.Add(m.M33); hash.Add(m.M34);
        hash.Add(m.M41); hash.Add(m.M42); hash.Add(m.M43); hash.Add(m.M44);
    }
}
