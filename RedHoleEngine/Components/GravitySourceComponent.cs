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
        GravityType.Kerr => OuterHorizonRadius,
        _ => 0f
    };
    
    /// <summary>
    /// Kerr spin parameter in length units: a = a* × M
    /// </summary>
    public readonly float KerrParameter => SpinParameter * Mass;
    
    /// <summary>
    /// Outer event horizon radius for Kerr black hole
    /// r+ = M + sqrt(M² - a²)
    /// </summary>
    public readonly float OuterHorizonRadius
    {
        get
        {
            float a = KerrParameter;
            float discriminant = Mass * Mass - a * a;
            if (discriminant < 0) return 0;
            return Mass + MathF.Sqrt(discriminant);
        }
    }
    
    /// <summary>
    /// Inner (Cauchy) horizon radius for Kerr black hole
    /// r- = M - sqrt(M² - a²)
    /// </summary>
    public readonly float InnerHorizonRadius
    {
        get
        {
            float a = KerrParameter;
            float discriminant = Mass * Mass - a * a;
            if (discriminant < 0) return 0;
            return Mass - MathF.Sqrt(discriminant);
        }
    }
    
    /// <summary>
    /// Ergosphere radius at the equator: r_ergo(θ=π/2) = 2M
    /// </summary>
    public readonly float ErgosphereEquatorialRadius => 2f * Mass;
    
    /// <summary>
    /// Ergosphere radius at a given polar angle θ
    /// r_ergo = M + sqrt(M² - a²cos²θ)
    /// </summary>
    public readonly float ErgosphereRadius(float theta)
    {
        float a = KerrParameter;
        float cosTheta = MathF.Cos(theta);
        float discriminant = Mass * Mass - a * a * cosTheta * cosTheta;
        if (discriminant < 0) return Mass;
        return Mass + MathF.Sqrt(discriminant);
    }
    
    /// <summary>
    /// Frame dragging angular velocity at radius r and angle θ
    /// ω = 2Mar / ((r² + a²)² - a²Δsin²θ)
    /// </summary>
    public readonly float FrameDraggingAngularVelocity(float r, float theta)
    {
        float a = KerrParameter;
        float a2 = a * a;
        float r2 = r * r;
        float sinTheta = MathF.Sin(theta);
        float sin2Theta = sinTheta * sinTheta;
        
        float delta = r2 - 2 * Mass * r + a2;
        float rPlusA2 = r2 + a2;
        float denominator = rPlusA2 * rPlusA2 - a2 * delta * sin2Theta;
        
        if (MathF.Abs(denominator) < 1e-10f)
            return 0;
            
        return 2 * Mass * a * r / denominator;
    }
    
    /// <summary>
    /// Prograde ISCO radius for Kerr black hole
    /// </summary>
    public readonly float ProgradeISCO
    {
        get
        {
            if (SpinParameter < 0.001f)
                return 6f * Mass;
                
            float a = SpinParameter;
            float Z1 = 1 + MathF.Pow(1 - a * a, 1f/3f) * (MathF.Pow(1 + a, 1f/3f) + MathF.Pow(1 - a, 1f/3f));
            float Z2 = MathF.Sqrt(3 * a * a + Z1 * Z1);
            return Mass * (3 + Z2 - MathF.Sqrt((3 - Z1) * (3 + Z1 + 2 * Z2)));
        }
    }
    
    /// <summary>
    /// Retrograde ISCO radius for Kerr black hole
    /// </summary>
    public readonly float RetrogradeISCO
    {
        get
        {
            if (SpinParameter < 0.001f)
                return 6f * Mass;
                
            float a = SpinParameter;
            float Z1 = 1 + MathF.Pow(1 - a * a, 1f/3f) * (MathF.Pow(1 + a, 1f/3f) + MathF.Pow(1 - a, 1f/3f));
            float Z2 = MathF.Sqrt(3 * a * a + Z1 * Z1);
            return Mass * (3 + Z2 + MathF.Sqrt((3 - Z1) * (3 + Z1 + 2 * Z2)));
        }
    }

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
