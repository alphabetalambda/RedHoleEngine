using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.InteropServices;
using RedHoleEngine.Components;
using RedHoleEngine.Core.ECS;
using RedHoleEngine.Engine;
using RedHoleEngine.Rendering.Rasterization;

namespace RedHoleEngine.Rendering;

/// <summary>
/// Vertex data for laser beam rendering
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct LaserVertex
{
    public Vector3 Position;
    public Vector4 Color;
    public Vector2 UV;  // For glow falloff
    
    public static readonly int SizeInBytes = Marshal.SizeOf<LaserVertex>();
}

/// <summary>
/// Data for a laser beam segment to be rendered
/// </summary>
public struct LaserRenderData
{
    public Vector3 Start;
    public Vector3 End;
    public float Width;
    public Vector4 BeamColor;
    public Vector4 CoreColor;
    public float CoreWidth;
    public float Intensity;
}

/// <summary>
/// Renders laser beams as camera-facing billboard quads.
/// Generates mesh data that can be rendered by the raster pipeline.
/// </summary>
public class LaserRenderer
{
    private readonly List<LaserVertex> _vertices = new(1024);
    private readonly List<uint> _indices = new(2048);
    private World? _world;
    private Camera? _camera;
    
    public IReadOnlyList<LaserVertex> Vertices => _vertices;
    public IReadOnlyList<uint> Indices => _indices;
    public int VertexCount => _vertices.Count;
    public int IndexCount => _indices.Count;

    public void SetWorld(World world)
    {
        _world = world;
    }

    public void SetCamera(Camera camera)
    {
        _camera = camera;
    }

    /// <summary>
    /// Build mesh data for all laser segments in the scene
    /// </summary>
    public void BuildMeshData()
    {
        _vertices.Clear();
        _indices.Clear();

        if (_world == null || _camera == null)
            return;

        // Gather all laser segments
        foreach (var entity in _world.Query<LaserSegmentComponent>())
        {
            ref var segment = ref _world.GetComponent<LaserSegmentComponent>(entity);
            
            AddBeamQuad(
                segment.Start,
                segment.End,
                segment.Width,
                segment.BeamColor,
                segment.CoreColor,
                segment.CoreWidth,
                segment.Intensity);
        }
    }

    private void AddBeamQuad(Vector3 start, Vector3 end, float width, Vector4 beamColor, Vector4 coreColor, float coreWidth, float intensity)
    {
        var beamDir = end - start;
        float length = beamDir.Length();
        if (length < 0.001f)
            return;

        beamDir /= length;

        // Calculate billboard vectors
        var toCamera = _camera!.Position - (start + end) * 0.5f;
        var right = Vector3.Cross(beamDir, toCamera);
        if (right.LengthSquared() < 0.001f)
        {
            // Beam pointing at camera, use arbitrary perpendicular
            right = Vector3.Cross(beamDir, Vector3.UnitY);
            if (right.LengthSquared() < 0.001f)
                right = Vector3.Cross(beamDir, Vector3.UnitX);
        }
        right = Vector3.Normalize(right);

        float halfWidth = width * 0.5f;
        var offset = right * halfWidth;

        // Apply intensity to colors
        var outerColor = beamColor * intensity;
        var innerColor = coreColor * intensity;
        
        // Outer glow layer (full width, transparent edges)
        AddQuadLayer(start, end, offset * 1.5f, new Vector4(outerColor.X, outerColor.Y, outerColor.Z, outerColor.W * 0.3f));
        
        // Main beam layer
        AddQuadLayer(start, end, offset, outerColor);
        
        // Inner core layer (brighter, narrower)
        if (coreWidth > 0)
        {
            AddQuadLayer(start, end, offset * coreWidth, innerColor);
        }
    }

    private void AddQuadLayer(Vector3 start, Vector3 end, Vector3 offset, Vector4 color)
    {
        uint baseIndex = (uint)_vertices.Count;

        // Four corners of the quad
        _vertices.Add(new LaserVertex
        {
            Position = start - offset,
            Color = color,
            UV = new Vector2(0, 0)
        });
        _vertices.Add(new LaserVertex
        {
            Position = start + offset,
            Color = color,
            UV = new Vector2(1, 0)
        });
        _vertices.Add(new LaserVertex
        {
            Position = end + offset,
            Color = color,
            UV = new Vector2(1, 1)
        });
        _vertices.Add(new LaserVertex
        {
            Position = end - offset,
            Color = color,
            UV = new Vector2(0, 1)
        });

        // Two triangles
        _indices.Add(baseIndex + 0);
        _indices.Add(baseIndex + 1);
        _indices.Add(baseIndex + 2);

        _indices.Add(baseIndex + 0);
        _indices.Add(baseIndex + 2);
        _indices.Add(baseIndex + 3);
    }

    /// <summary>
    /// Get vertices as array for GPU upload
    /// </summary>
    public LaserVertex[] GetVertexArray() => _vertices.ToArray();

    /// <summary>
    /// Get indices as array for GPU upload
    /// </summary>
    public uint[] GetIndexArray() => _indices.ToArray();

    /// <summary>
    /// Convert laser vertices to raster vertices for rendering through existing pipeline
    /// </summary>
    public RasterVertex[] ToRasterVertices()
    {
        var result = new RasterVertex[_vertices.Count];
        for (int i = 0; i < _vertices.Count; i++)
        {
            result[i] = new RasterVertex
            {
                Position = _vertices[i].Position,
                Color = _vertices[i].Color
            };
        }
        return result;
    }
}

/// <summary>
/// System that renders lasers by generating mesh data each frame
/// </summary>
public sealed class LaserRenderSystem : GameSystem
{
    private readonly LaserRenderer _renderer = new();
    private IGraphicsBackend? _backend;
    
    public override int Priority => -45; // Run after LaserSystem, before actual rendering
    
    public LaserRenderer Renderer => _renderer;

    public void SetBackend(IGraphicsBackend backend)
    {
        _backend = backend;
    }

    public void SetCamera(Camera camera)
    {
        _renderer.SetCamera(camera);
    }

    public override void Update(float deltaTime)
    {
        if (World == null)
            return;

        _renderer.SetWorld(World);
        _renderer.BuildMeshData();

        // The actual rendering will be handled by integrating with VulkanBackend
        // For now, laser data is available via _renderer.Vertices/Indices
    }
}
