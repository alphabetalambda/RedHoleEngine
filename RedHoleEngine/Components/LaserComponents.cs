using System.Numerics;
using RedHoleEngine.Core.ECS;

namespace RedHoleEngine.Components;

/// <summary>
/// Type of laser behavior
/// </summary>
public enum LaserType
{
    /// <summary>Continuous beam from origin to target/max range</summary>
    Beam,
    /// <summary>Discrete projectile that travels along path</summary>
    Pulse,
    /// <summary>Sweeping beam that rotates over time</summary>
    Scanning
}

/// <summary>
/// Component for laser emitters - attach to an entity to make it emit lasers
/// </summary>
public struct LaserEmitterComponent : IComponent
{
    // Basic settings
    public LaserType Type;
    public bool Enabled;
    public bool FireOnStart;
    
    // Beam properties
    public float MaxRange;
    public float BeamWidth;
    public Vector4 BeamColor;      // RGBA, A controls intensity
    public Vector4 CoreColor;      // Inner bright core color
    public float CoreWidth;        // Width of bright inner core (0-1 ratio of BeamWidth)
    
    // Pulse properties (for LaserType.Pulse)
    public float PulseSpeed;
    public float PulseLength;
    public float FireRate;         // Pulses per second
    public bool AutoFire;
    
    // Scanning properties (for LaserType.Scanning)
    public float ScanSpeed;        // Degrees per second
    public float ScanAngle;        // Total sweep angle
    public Vector3 ScanAxis;       // Axis to rotate around
    
    // Physics interaction
    public bool UseRaycast;
    public uint CollisionMask;
    public float Damage;           // Damage per second (beam) or per hit (pulse)
    public float PushForce;        // Force applied to hit objects
    
    // Particle trail
    public bool EmitParticles;
    public float ParticleRate;     // Particles per unit length per second
    public float ParticleSize;
    public float ParticleLifetime;
    public Vector4 ParticleColor;
    
    // Redirection
    public bool CanRedirect;
    public int MaxBounces;
    public float RedirectAngle;    // Max angle change per redirect in degrees
    
    // Runtime state
    internal float FireTimer;
    internal float ScanAngleCurrent;
    internal int ScanDirection;
    
    public static LaserEmitterComponent CreateBeam(
        float range = 100f,
        float width = 0.1f,
        Vector4? color = null)
    {
        return new LaserEmitterComponent
        {
            Type = LaserType.Beam,
            Enabled = true,
            FireOnStart = true,
            MaxRange = range,
            BeamWidth = width,
            BeamColor = color ?? new Vector4(1f, 0.2f, 0.2f, 1f),
            CoreColor = new Vector4(1f, 0.9f, 0.8f, 1f),
            CoreWidth = 0.3f,
            UseRaycast = true,
            CollisionMask = uint.MaxValue,
            Damage = 10f,
            PushForce = 5f,
            EmitParticles = true,
            ParticleRate = 50f,
            ParticleSize = 0.05f,
            ParticleLifetime = 0.3f,
            ParticleColor = new Vector4(1f, 0.5f, 0.3f, 0.8f),
            CanRedirect = false,
            MaxBounces = 0,
            ScanDirection = 1
        };
    }
    
    public static LaserEmitterComponent CreatePulse(
        float speed = 50f,
        float length = 0.5f,
        float fireRate = 5f,
        Vector4? color = null)
    {
        return new LaserEmitterComponent
        {
            Type = LaserType.Pulse,
            Enabled = true,
            FireOnStart = false,
            AutoFire = true,
            MaxRange = 200f,
            BeamWidth = 0.08f,
            BeamColor = color ?? new Vector4(0.2f, 0.5f, 1f, 1f),
            CoreColor = new Vector4(0.8f, 0.9f, 1f, 1f),
            CoreWidth = 0.4f,
            PulseSpeed = speed,
            PulseLength = length,
            FireRate = fireRate,
            UseRaycast = true,
            CollisionMask = uint.MaxValue,
            Damage = 25f,
            PushForce = 20f,
            EmitParticles = true,
            ParticleRate = 100f,
            ParticleSize = 0.03f,
            ParticleLifetime = 0.2f,
            ParticleColor = new Vector4(0.3f, 0.6f, 1f, 0.9f),
            CanRedirect = true,
            MaxBounces = 3,
            RedirectAngle = 45f,
            ScanDirection = 1
        };
    }
    
    public static LaserEmitterComponent CreateScanning(
        float scanSpeed = 45f,
        float scanAngle = 90f,
        Vector4? color = null)
    {
        return new LaserEmitterComponent
        {
            Type = LaserType.Scanning,
            Enabled = true,
            FireOnStart = true,
            MaxRange = 50f,
            BeamWidth = 0.05f,
            BeamColor = color ?? new Vector4(0.2f, 1f, 0.3f, 1f),
            CoreColor = new Vector4(0.9f, 1f, 0.9f, 1f),
            CoreWidth = 0.5f,
            ScanSpeed = scanSpeed,
            ScanAngle = scanAngle,
            ScanAxis = Vector3.UnitY,
            UseRaycast = true,
            CollisionMask = uint.MaxValue,
            Damage = 5f,
            PushForce = 2f,
            EmitParticles = true,
            ParticleRate = 30f,
            ParticleSize = 0.04f,
            ParticleLifetime = 0.4f,
            ParticleColor = new Vector4(0.3f, 1f, 0.5f, 0.7f),
            CanRedirect = false,
            MaxBounces = 0,
            ScanDirection = 1
        };
    }
}

/// <summary>
/// Component for active laser pulses (projectiles)
/// </summary>
public struct LaserPulseComponent : IComponent
{
    public Vector3 Position;
    public Vector3 Direction;
    public float Speed;
    public float Length;
    public float DistanceTraveled;
    public float MaxRange;
    public int BouncesRemaining;
    public float RedirectAngle;
    
    // Visual properties (copied from emitter)
    public float Width;
    public Vector4 BeamColor;
    public Vector4 CoreColor;
    public float CoreWidth;
    
    // Physics
    public uint CollisionMask;
    public float Damage;
    public float PushForce;
    
    // Particles
    public bool EmitParticles;
    public float ParticleRate;
    public float ParticleSize;
    public float ParticleLifetime;
    public Vector4 ParticleColor;
    
    // Runtime
    internal float ParticleAccumulator;
    internal Entity EmitterEntity;
}

/// <summary>
/// Component for beam segment data (used for rendering)
/// </summary>
public struct LaserSegmentComponent : IComponent
{
    public Vector3 Start;
    public Vector3 End;
    public float Width;
    public Vector4 BeamColor;
    public Vector4 CoreColor;
    public float CoreWidth;
    public float Intensity;        // For fade effects
    public Entity EmitterEntity;
    
    // Energy pulse settings
    public bool ShowEnergyPulses;
    public int EnergyPulseCount;       // Number of pulses along the beam
    public float EnergyPulseSpeed;     // How fast pulses travel (units per second)
    public float EnergyPulseSize;      // Size of each pulse segment (0-1 of beam length)
    public Vector4 EnergyPulseColor;   // Color of energy pulses
}

/// <summary>
/// Component marking an entity as having been hit by a laser this frame
/// </summary>
public struct LaserHitComponent : IComponent
{
    public Vector3 HitPoint;
    public Vector3 HitNormal;
    public float Damage;
    public Vector3 PushDirection;
    public float PushForce;
    public Entity LaserEntity;
}

/// <summary>
/// Component for laser redirect surfaces (mirrors, prisms, etc.)
/// </summary>
public struct LaserRedirectComponent : IComponent
{
    public enum RedirectType
    {
        Mirror,      // Perfect reflection
        Refract,     // Bend by angle
        Split,       // Create multiple beams
        Absorb       // Stop beam, maybe charge something
    }
    
    public RedirectType Type;
    public float Efficiency;       // 0-1, how much energy is preserved
    public float RefractAngle;     // For Refract type
    public int SplitCount;         // For Split type
    public Vector4 TintColor;      // Color modification on redirect
}
