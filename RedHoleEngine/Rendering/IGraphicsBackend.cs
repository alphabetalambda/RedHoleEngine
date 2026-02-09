using System.Numerics;
using RedHoleEngine.Engine;
using RedHoleEngine.Particles;
using RedHoleEngine.Physics;
using RedHoleEngine.Rendering.Debug;
using RedHoleEngine.Rendering.Raytracing;
using RedHoleEngine.Rendering.Rasterization;
using RedHoleEngine.Rendering.UI;

namespace RedHoleEngine.Rendering;

/// <summary>
/// Available graphics backends
/// </summary>
public enum GraphicsBackendType
{
    OpenGL,
    Vulkan  // Uses MoltenVK on macOS for Metal support
}

/// <summary>
/// Abstract interface for graphics backends
/// Allows switching between OpenGL/Vulkan/Metal
/// </summary>
public interface IGraphicsBackend : IDisposable
{
    /// <summary>
    /// Initialize the graphics backend
    /// </summary>
    void Initialize();

    /// <summary>
    /// Render a frame with the raytracer
    /// </summary>
    void Render(Camera camera, BlackHole blackHole, float time);
    
    /// <summary>
    /// Render a frame with the raytracer and debug overlay
    /// </summary>
    void Render(Camera camera, BlackHole blackHole, float time, DebugDrawManager? debugDraw);
    
    /// <summary>
    /// Render a frame with particles and debug overlay
    /// </summary>
    void Render(Camera camera, BlackHole blackHole, float time, DebugDrawManager? debugDraw, ParticlePool? particles);

    /// <summary>
    /// Handle window resize
    /// </summary>
    void Resize(int width, int height);
    
    /// <summary>
    /// Check if particle rendering is supported
    /// </summary>
    bool SupportsParticleRendering { get; }

    /// <summary>
    /// Get the backend type
    /// </summary>
    GraphicsBackendType BackendType { get; }

    /// <summary>
    /// Check if compute shaders are supported
    /// </summary>
    bool SupportsComputeShaders { get; }
    
    /// <summary>
    /// Check if debug rendering is supported
    /// </summary>
    bool SupportsDebugRendering { get; }

    /// <summary>
    /// Raytracer quality settings (rays per pixel, bounces)
    /// </summary>
    RaytracerSettings RaytracerSettings { get; }

    /// <summary>
    /// Update raytracer BVH mesh data
    /// </summary>
    void SetRaytracerMeshData(RaytracerMeshData data);

    /// <summary>
    /// Update rasterized mesh data
    /// </summary>
    void SetRasterMeshData(RasterMeshData data);

    /// <summary>
    /// Render mode settings
    /// </summary>
    RenderSettings RenderSettings { get; }

    /// <summary>
    /// Update UI draw data
    /// </summary>
    void SetUiDrawData(UiDrawData drawData);
}
