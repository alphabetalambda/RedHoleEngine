using System.Numerics;
using RedHoleEngine.Core.ECS;

namespace RedHoleEngine.Components;

/// <summary>
/// Types of gravitational fields
/// </summary>
public enum GravityType
{
    /// <summary>
    /// Newtonian point mass gravity (F = GMm/r^2)
    /// </summary>
    Newtonian,
    
    /// <summary>
    /// Schwarzschild black hole (includes relativistic effects)
    /// </summary>
    Schwarzschild,
    
    /// <summary>
    /// Kerr black hole (rotating, includes frame dragging)
    /// </summary>
    Kerr,
    
    /// <summary>
    /// Uniform gravity field (like planetary surface)
    /// </summary>
    Uniform
}

/// <summary>
/// Component that makes an entity a source of gravity.
/// Used for black holes, planets, stars, etc.
/// </summary>
public struct GravitySourceComponent : IComponent
{
    /// <summary>
    /// Mass of the object (in natural units where G = c = 1 for relativistic,
    /// or in kg for Newtonian)
    /// </summary>
    public float Mass;
    
    /// <summary>
    /// Type of gravitational field
    /// </summary>
    public GravityType GravityType;
    
    /// <summary>
    /// Angular momentum parameter (for Kerr black holes, 0 to 1)
    /// </summary>
    public float SpinParameter;
    
    /// <summary>
    /// Spin axis (for Kerr black holes)
    /// </summary>
    public Vector3 SpinAxis;
    
    /// <summary>
    /// Uniform gravity direction (for Uniform type)
    /// </summary>
    public Vector3 UniformDirection;
    
    /// <summary>
    /// Uniform gravity strength (for Uniform type)
    /// </summary>
    public float UniformStrength;
    
    /// <summary>
    /// Whether this source affects light (gravitational lensing)
    /// </summary>
    public bool AffectsLight;
    
    /// <summary>
    /// Maximum range of influence (0 = infinite)
    /// </summary>
    public float MaxRange;

    /// <summary>
    /// Schwarzschild radius (for black holes): rs = 2M
    /// </summary>
    public readonly float SchwarzschildRadius => 2f * Mass;

    /// <summary>
    /// Photon sphere radius (for Schwarzschild): r = 1.5 * rs = 3M
    /// </summary>
    public readonly float PhotonSphereRadius => 3f * Mass;

    /// <summary>
    /// Innermost Stable Circular Orbit (for Schwarzschild): r = 3 * rs = 6M
    /// </summary>
    public readonly float ISCO => 6f * Mass;

    /// <summary>
    /// Event horizon radius (same as Schwarzschild for non-rotating)
    /// </summary>
    public readonly float EventHorizonRadius => GravityType switch
    {
        GravityType.Schwarzschild => SchwarzschildRadius,
        GravityType.Kerr => Mass + MathF.Sqrt(Mass * Mass - SpinParameter * SpinParameter * Mass * Mass),
        _ => 0f
    };

    /// <summary>
    /// Create a Schwarzschild black hole
    /// </summary>
    public static GravitySourceComponent CreateBlackHole(float mass)
    {
        return new GravitySourceComponent
        {
            Mass = mass,
            GravityType = GravityType.Schwarzschild,
            SpinParameter = 0f,
            SpinAxis = Vector3.UnitY,
            AffectsLight = true,
            MaxRange = 0f
        };
    }

    /// <summary>
    /// Create a Kerr (rotating) black hole
    /// </summary>
    public static GravitySourceComponent CreateRotatingBlackHole(float mass, float spin, Vector3 spinAxis)
    {
        return new GravitySourceComponent
        {
            Mass = mass,
            GravityType = GravityType.Kerr,
            SpinParameter = Math.Clamp(spin, 0f, 0.998f), // Physical limit
            SpinAxis = Vector3.Normalize(spinAxis),
            AffectsLight = true,
            MaxRange = 0f
        };
    }

    /// <summary>
    /// Create a Newtonian gravity source (planet, star)
    /// </summary>
    public static GravitySourceComponent CreateNewtonian(float mass, float maxRange = 0f)
    {
        return new GravitySourceComponent
        {
            Mass = mass,
            GravityType = GravityType.Newtonian,
            AffectsLight = false,
            MaxRange = maxRange
        };
    }

    /// <summary>
    /// Create a uniform gravity field
    /// </summary>
    public static GravitySourceComponent CreateUniform(Vector3 direction, float strength)
    {
        return new GravitySourceComponent
        {
            GravityType = GravityType.Uniform,
            UniformDirection = Vector3.Normalize(direction),
            UniformStrength = strength,
            AffectsLight = false,
            MaxRange = 0f
        };
    }

    /// <summary>
    /// Calculate gravitational acceleration at a point (Newtonian approximation)
    /// </summary>
    public readonly Vector3 GetAccelerationAt(Vector3 sourcePosition, Vector3 targetPosition)
    {
        if (GravityType == GravityType.Uniform)
        {
            return UniformDirection * UniformStrength;
        }

        var toSource = sourcePosition - targetPosition;
        float distSq = toSource.LengthSquared();
        
        if (distSq < 0.0001f)
            return Vector3.Zero;

        float dist = MathF.Sqrt(distSq);
        
        if (MaxRange > 0 && dist > MaxRange)
            return Vector3.Zero;

        // Newtonian: a = GM/r^2 in direction of source
        // In natural units (G=1): a = M/r^2
        float accelMag = Mass / distSq;
        
        return toSource / dist * accelMag;
    }
}
