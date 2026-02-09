using System;
using System.Collections.Generic;
using System.Numerics;
using RedHoleEngine.Components;
using RedHoleEngine.Core.ECS;
using RedHoleEngine.Engine;
using RedHoleEngine.Rendering;
using RedHoleEngine.Rendering.Rasterization;
using RedHoleEngine.Resources;

namespace RedHoleEngine.Rendering.Rasterization;

/// <summary>
/// Builds combined mesh buffers for primitive rasterization.
/// </summary>
public class RasterMeshSystem : GameSystem
{
    private IGraphicsBackend? _backend;
    private Camera? _camera;
    private int _lastSceneHash;
    private int _lastVertexCount;
    private int _lastIndexCount;

    public override int Priority => -51;

    public void SetBackend(IGraphicsBackend backend)
    {
        _backend = backend;
    }

    public void SetCamera(Camera camera)
    {
        _camera = camera;
    }

    public override void Update(float deltaTime)
    {
        if (_backend == null)
            return;

        bool isRasterized = _backend.RenderSettings.Mode == RenderMode.Rasterized;

        var vertices = new List<RasterVertex>(1024);
        var indices = new List<uint>(2048);
        var hash = new HashCode();

        // Render regular meshes (only in rasterized mode - raytracer handles meshes itself)
        if (isRasterized)
        {
            foreach (var entity in World.Query<MeshComponent, TransformComponent>())
            {
                ref var meshComponent = ref World.GetComponent<MeshComponent>(entity);
                ref var transform = ref World.GetComponent<TransformComponent>(entity);

                if (!meshComponent.Visible)
                    continue;

                var mesh = meshComponent.MeshHandle.Get();
                if (mesh == null || mesh.Indices.Length < 3)
                    continue;

                MaterialComponent material = MaterialComponent.Default;
                if (World.TryGetComponent<MaterialComponent>(entity, out var mat))
                    material = mat;

                AddHash(ref hash, meshComponent.MeshHandle.Id, material, transform);

                AppendMesh(vertices, indices, mesh, transform, material);
            }
        }

        // Render laser segments (always - overlay on both raytraced and rasterized)
        if (_camera != null)
        {
            int laserCount = 0;
            foreach (var entity in World.Query<LaserSegmentComponent>())
            {
                ref var segment = ref World.GetComponent<LaserSegmentComponent>(entity);
                AppendLaserSegment(vertices, indices, ref segment, _camera);
                laserCount++;
            }
            hash.Add(laserCount);
            hash.Add(Environment.TickCount); // Lasers change every frame
        }

        // Skip if nothing to render
        if (vertices.Count == 0)
            return;

        int sceneHash = hash.ToHashCode();
        if (sceneHash == _lastSceneHash && vertices.Count == _lastVertexCount && indices.Count == _lastIndexCount)
            return;

        _backend.SetRasterMeshData(new RasterMeshData
        {
            Vertices = vertices.ToArray(),
            Indices = indices.ToArray()
        });

        _lastSceneHash = sceneHash;
        _lastVertexCount = vertices.Count;
        _lastIndexCount = indices.Count;
    }

    private static void AppendMesh(
        List<RasterVertex> vertices,
        List<uint> indices,
        Mesh mesh,
        TransformComponent transform,
        MaterialComponent material)
    {
        uint baseIndex = (uint)vertices.Count;
        var color = material.BaseColor;

        foreach (var vertex in mesh.Vertices)
        {
            vertices.Add(new RasterVertex
            {
                Position = transform.Transform.TransformPoint(vertex.Position),
                Color = color
            });
        }

        foreach (var index in mesh.Indices)
        {
            indices.Add(baseIndex + index);
        }
    }

    private static void AddHash(ref HashCode hash, string meshId, MaterialComponent material, TransformComponent transform)
    {
        hash.Add(meshId ?? string.Empty);
        hash.Add(material.BaseColor.X);
        hash.Add(material.BaseColor.Y);
        hash.Add(material.BaseColor.Z);
        hash.Add(material.BaseColor.W);

        var m = transform.Transform.WorldMatrix;
        hash.Add(m.M11); hash.Add(m.M12); hash.Add(m.M13); hash.Add(m.M14);
        hash.Add(m.M21); hash.Add(m.M22); hash.Add(m.M23); hash.Add(m.M24);
        hash.Add(m.M31); hash.Add(m.M32); hash.Add(m.M33); hash.Add(m.M34);
        hash.Add(m.M41); hash.Add(m.M42); hash.Add(m.M43); hash.Add(m.M44);
    }

    private static void AppendLaserSegment(
        List<RasterVertex> vertices,
        List<uint> indices,
        ref LaserSegmentComponent segment,
        Camera camera)
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
        
        // Calculate up vector for energy pulses on the beam surface
        var up = Vector3.Cross(right, beamDir);
        up = Vector3.Normalize(up);

        float halfWidth = segment.Width * 0.5f;
        var offset = right * halfWidth;

        // Apply intensity
        var color = segment.BeamColor * segment.Intensity;
        var coreColor = segment.CoreColor * segment.Intensity;

        // Outer glow layer
        AppendQuadLayer(vertices, indices, start, end, offset * 1.5f, 
            new Vector4(color.X, color.Y, color.Z, color.W * 0.3f));
        
        // Main beam layer
        AppendQuadLayer(vertices, indices, start, end, offset, color);
        
        // Inner core layer
        if (segment.CoreWidth > 0)
        {
            AppendQuadLayer(vertices, indices, start, end, offset * segment.CoreWidth, coreColor);
        }

        // Energy pulses traveling along the beam surface - each rail has independent timing
        if (segment.ShowEnergyPulses && segment.EnergyPulseCount > 0)
        {
            float time = Environment.TickCount / 1000f;
            
            // Create stable coordinate system around beam (not view-dependent)
            Vector3 beamRight, beamUp;
            if (MathF.Abs(Vector3.Dot(beamDir, Vector3.UnitY)) < 0.99f)
            {
                beamRight = Vector3.Normalize(Vector3.Cross(beamDir, Vector3.UnitY));
            }
            else
            {
                beamRight = Vector3.Normalize(Vector3.Cross(beamDir, Vector3.UnitX));
            }
            beamUp = Vector3.Normalize(Vector3.Cross(beamRight, beamDir));
            
            // Pulse segment properties  
            float surfaceRadius = halfWidth * 2.0f;  // On beam surface
            float railWidth = segment.Width * 0.4f;  // Width of each rail strip
            
            // Number of rail tracks around the beam
            const int railCount = 4;
            
            // Each rail gets its own pulse with randomized timing offset
            for (int rail = 0; rail < railCount; rail++)
            {
                // Angle around beam for this rail
                float angle = (rail / (float)railCount) * MathF.PI * 2f;
                
                // Pseudo-random offset per rail (deterministic based on rail index and segment position)
                uint seed = (uint)(rail * 73856093) ^ (uint)(segment.Start.GetHashCode());
                float randomOffset = (seed % 1000) / 1000f;  // 0 to 1
                float speedVariation = 0.7f + ((seed >> 10) % 1000) / 1000f * 0.6f;  // 0.7 to 1.3
                
                // Calculate this rail's pulse position (each rail independent)
                float animT = (time * segment.EnergyPulseSpeed * speedVariation / length + randomOffset) % 1f;
                
                // Direction from beam center to this rail
                var radial = beamRight * MathF.Cos(angle) + beamUp * MathF.Sin(angle);
                var surfaceOffset = radial * surfaceRadius;
                
                // Tangent for rail width (perpendicular to beam direction, along circumference)
                var tangent = Vector3.Normalize(Vector3.Cross(beamDir, radial)) * railWidth * 0.5f;
                
                // Pulse start and end positions along beam
                float pulseStartT = animT;
                float pulseEndT = animT + segment.EnergyPulseSize;
                
                // Render the pulse segment (handle wrap-around)
                if (pulseEndT > 1f)
                {
                    // Part 1: from pulseStartT to 1.0
                    RenderSingleRail(vertices, indices, 
                        start + beamDir * (length * pulseStartT) + surfaceOffset,
                        end + surfaceOffset,
                        tangent, segment.EnergyPulseColor);
                    
                    // Part 2: from 0 to remainder  
                    float remainder = pulseEndT - 1f;
                    RenderSingleRail(vertices, indices,
                        start + surfaceOffset,
                        start + beamDir * (length * remainder) + surfaceOffset,
                        tangent, segment.EnergyPulseColor);
                }
                else
                {
                    RenderSingleRail(vertices, indices,
                        start + beamDir * (length * pulseStartT) + surfaceOffset,
                        start + beamDir * (length * pulseEndT) + surfaceOffset,
                        tangent, segment.EnergyPulseColor);
                }
            }
        }
    }
    
    private static void RenderSingleRail(
        List<RasterVertex> vertices,
        List<uint> indices,
        Vector3 railStart,
        Vector3 railEnd,
        Vector3 tangent,
        Vector4 color)
    {
        // Four corners of the rail segment quad
        var p0 = railStart - tangent;
        var p1 = railStart + tangent;
        var p2 = railEnd + tangent;
        var p3 = railEnd - tangent;
        
        uint baseIndex = (uint)vertices.Count;
        vertices.Add(new RasterVertex { Position = p0, Color = color });
        vertices.Add(new RasterVertex { Position = p1, Color = color });
        vertices.Add(new RasterVertex { Position = p2, Color = color });
        vertices.Add(new RasterVertex { Position = p3, Color = color });
        
        indices.Add(baseIndex + 0);
        indices.Add(baseIndex + 1);
        indices.Add(baseIndex + 2);
        indices.Add(baseIndex + 0);
        indices.Add(baseIndex + 2);
        indices.Add(baseIndex + 3);
    }

    private static void AppendQuadLayer(
        List<RasterVertex> vertices,
        List<uint> indices,
        Vector3 start,
        Vector3 end,
        Vector3 offset,
        Vector4 color)
    {
        uint baseIndex = (uint)vertices.Count;

        vertices.Add(new RasterVertex { Position = start - offset, Color = color });
        vertices.Add(new RasterVertex { Position = start + offset, Color = color });
        vertices.Add(new RasterVertex { Position = end + offset, Color = color });
        vertices.Add(new RasterVertex { Position = end - offset, Color = color });

        indices.Add(baseIndex + 0);
        indices.Add(baseIndex + 1);
        indices.Add(baseIndex + 2);

        indices.Add(baseIndex + 0);
        indices.Add(baseIndex + 2);
        indices.Add(baseIndex + 3);
    }
}
