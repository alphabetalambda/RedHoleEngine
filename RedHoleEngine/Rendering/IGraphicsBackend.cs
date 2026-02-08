using System.Numerics;
using RedHoleEngine.Engine;
using RedHoleEngine.Physics;

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
    /// Handle window resize
    /// </summary>
    void Resize(int width, int height);

    /// <summary>
    /// Get the backend type
    /// </summary>
    GraphicsBackendType BackendType { get; }

    /// <summary>
    /// Check if compute shaders are supported
    /// </summary>
    bool SupportsComputeShaders { get; }
}
