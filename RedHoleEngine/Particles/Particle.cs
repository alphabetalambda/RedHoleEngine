using System.Numerics;
using System.Runtime.InteropServices;

namespace RedHoleEngine.Particles;

/// <summary>
/// Individual particle data structure.
/// Designed for cache-friendly iteration and GPU upload.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct Particle
{
    /// <summary>
    /// Current world position
    /// </summary>
    public Vector3 Position;
    
    /// <summary>
    /// Current velocity
    /// </summary>
    public Vector3 Velocity;
    
    /// <summary>
    /// Current color (RGBA)
    /// </summary>
    public Vector4 Color;
    
    /// <summary>
    /// Current size (uniform scale)
    /// </summary>
    public float Size;
    
    /// <summary>
    /// Current rotation in radians (for 2D billboards)
    /// </summary>
    public float Rotation;
    
    /// <summary>
    /// Angular velocity (rotation speed)
    /// </summary>
    public float AngularVelocity;
    
    /// <summary>
    /// Time remaining before death (seconds)
    /// </summary>
    public float Lifetime;
    
    /// <summary>
    /// Initial lifetime (for normalized age calculation)
    /// </summary>
    public float InitialLifetime;
    
    /// <summary>
    /// Whether this particle slot is active
    /// </summary>
    public bool IsAlive;
    
    /// <summary>
    /// Normalized age (0 = just spawned, 1 = about to die)
    /// </summary>
    public readonly float NormalizedAge => InitialLifetime > 0 
        ? 1f - (Lifetime / InitialLifetime) 
        : 1f;

    /// <summary>
    /// Create a new particle
    /// </summary>
    public static Particle Create(
        Vector3 position, 
        Vector3 velocity, 
        Vector4 color, 
        float size, 
        float lifetime,
        float rotation = 0f,
        float angularVelocity = 0f)
    {
        return new Particle
        {
            Position = position,
            Velocity = velocity,
            Color = color,
            Size = size,
            Rotation = rotation,
            AngularVelocity = angularVelocity,
            Lifetime = lifetime,
            InitialLifetime = lifetime,
            IsAlive = true
        };
    }
}

/// <summary>
/// GPU-friendly particle data for rendering (32 bytes per particle)
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct ParticleRenderData
{
    public Vector3 Position;      // 12 bytes
    public float Size;            // 4 bytes
    public Vector4 Color;         // 16 bytes
    // Total: 32 bytes (aligned)

    public static ParticleRenderData FromParticle(in Particle p)
    {
        return new ParticleRenderData
        {
            Position = p.Position,
            Size = p.Size,
            Color = p.Color
        };
    }
}
