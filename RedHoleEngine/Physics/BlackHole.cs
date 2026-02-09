using System.Numerics;

namespace RedHoleEngine.Physics;

public class BlackHole
{
    /// <summary>
    /// Position of the black hole in world space
    /// </summary>
    public Vector3 Position { get; set; }

    /// <summary>
    /// Mass of the black hole (in natural units where G = c = 1)
    /// </summary>
    public float Mass { get; set; }
    
    /// <summary>
    /// Dimensionless spin parameter: a* = a/M = J/(M²c)
    /// Range: 0 (Schwarzschild, non-rotating) to 1 (extremal Kerr, maximum rotation)
    /// Values > 1 would create a naked singularity (unphysical)
    /// </summary>
    public float Spin { get; set; }
    
    /// <summary>
    /// Spin axis direction (normalized)
    /// Defines the axis around which the black hole rotates
    /// Default is +Y (up), meaning the accretion disk lies in the XZ plane
    /// </summary>
    public Vector3 SpinAxis { get; set; } = Vector3.UnitY;

    /// <summary>
    /// Schwarzschild radius: rs = 2GM/c^2
    /// In natural units (G = c = 1): rs = 2M
    /// </summary>
    public float SchwarzschildRadius => 2.0f * Mass;
    
    /// <summary>
    /// Kerr spin parameter in length units: a = J/(Mc) = a* × M
    /// </summary>
    public float KerrParameter => Spin * Mass;
    
    /// <summary>
    /// Outer event horizon radius for Kerr black hole
    /// r+ = M + sqrt(M² - a²)
    /// Reduces to rs = 2M when a = 0 (Schwarzschild)
    /// </summary>
    public float OuterHorizonRadius
    {
        get
        {
            float a = KerrParameter;
            float discriminant = Mass * Mass - a * a;
            if (discriminant < 0) return 0; // Naked singularity (unphysical)
            return Mass + MathF.Sqrt(discriminant);
        }
    }
    
    /// <summary>
    /// Inner (Cauchy) horizon radius for Kerr black hole
    /// r- = M - sqrt(M² - a²)
    /// Only exists for rotating black holes (a > 0)
    /// </summary>
    public float InnerHorizonRadius
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
    /// Ergosphere radius at the equator (θ = π/2)
    /// r_ergo = M + sqrt(M² - a²cos²θ)
    /// At equator: r_ergo = M + sqrt(M² - 0) = 2M (same as Schwarzschild radius)
    /// </summary>
    public float ErgosphereEquatorialRadius => 2.0f * Mass;
    
    /// <summary>
    /// Ergosphere radius at the poles (θ = 0 or π)
    /// r_ergo = M + sqrt(M² - a²)
    /// At poles, ergosphere touches the outer horizon
    /// </summary>
    public float ErgospherePolarRadius => OuterHorizonRadius;

    /// <summary>
    /// Photon sphere radius: r = 1.5 * rs = 3M
    /// Light can orbit here in unstable circular orbits
    /// Note: For Kerr, photon orbits are more complex (prograde vs retrograde)
    /// </summary>
    public float PhotonSphereRadius => 1.5f * SchwarzschildRadius;

    /// <summary>
    /// Inner radius of the accretion disk
    /// For Kerr: ISCO depends on spin and orbit direction
    /// </summary>
    public float DiskInnerRadius { get; set; }

    /// <summary>
    /// Outer radius of the accretion disk
    /// </summary>
    public float DiskOuterRadius { get; set; }

    /// <summary>
    /// Temperature at inner edge of disk (affects color)
    /// Higher = bluer/whiter, lower = redder
    /// </summary>
    public float DiskInnerTemperature { get; set; } = 10000.0f;

    /// <summary>
    /// Temperature at outer edge of disk
    /// </summary>
    public float DiskOuterTemperature { get; set; } = 3000.0f;

    public BlackHole(Vector3 position, float mass, float spin = 0f, Vector3? spinAxis = null)
    {
        Position = position;
        Mass = mass;
        Spin = MathF.Min(MathF.Abs(spin), 0.998f); // Clamp to avoid extremal/naked singularity
        SpinAxis = spinAxis.HasValue ? Vector3.Normalize(spinAxis.Value) : Vector3.UnitY;
        
        // Default disk radii based on mass and spin
        // For prograde orbits, ISCO decreases with spin (down to 1M at a*=1)
        // For simplicity, use prograde ISCO approximation
        DiskInnerRadius = CalculateProgradeISCO();
        DiskOuterRadius = 6.0f * SchwarzschildRadius;  // Reduced from 15x to 6x for better visualization
    }
    
    /// <summary>
    /// Calculate the Innermost Stable Circular Orbit (ISCO) for prograde orbits
    /// For a* = 0: ISCO = 6M (= 3 × rs)
    /// For a* = 1: ISCO = M
    /// </summary>
    public float CalculateProgradeISCO()
    {
        if (Spin < 0.001f)
            return 3.0f * SchwarzschildRadius; // 6M for Schwarzschild
            
        // Kerr ISCO formula (prograde)
        float a = Spin;
        float Z1 = 1 + MathF.Pow(1 - a * a, 1f/3f) * (MathF.Pow(1 + a, 1f/3f) + MathF.Pow(1 - a, 1f/3f));
        float Z2 = MathF.Sqrt(3 * a * a + Z1 * Z1);
        float isco = Mass * (3 + Z2 - MathF.Sqrt((3 - Z1) * (3 + Z1 + 2 * Z2)));
        return isco;
    }
    
    /// <summary>
    /// Calculate the Innermost Stable Circular Orbit (ISCO) for retrograde orbits
    /// For a* = 0: ISCO = 6M (same as prograde)
    /// For a* = 1: ISCO = 9M
    /// </summary>
    public float CalculateRetrogradeISCO()
    {
        if (Spin < 0.001f)
            return 3.0f * SchwarzschildRadius; // 6M for Schwarzschild
            
        // Kerr ISCO formula (retrograde)
        float a = Spin;
        float Z1 = 1 + MathF.Pow(1 - a * a, 1f/3f) * (MathF.Pow(1 + a, 1f/3f) + MathF.Pow(1 - a, 1f/3f));
        float Z2 = MathF.Sqrt(3 * a * a + Z1 * Z1);
        float isco = Mass * (3 + Z2 + MathF.Sqrt((3 - Z1) * (3 + Z1 + 2 * Z2)));
        return isco;
    }
    
    /// <summary>
    /// Calculate the photon sphere radius (where light can orbit)
    /// For Schwarzschild: r_ph = 1.5 × rs = 3M
    /// For Kerr (prograde, equatorial): r_ph = 2M(1 + cos(2/3 × arccos(-a*)))
    /// </summary>
    public float CalculatePhotonSphereRadius()
    {
        if (Spin < 0.001f)
            return 1.5f * SchwarzschildRadius; // 3M for Schwarzschild
        
        // For Kerr, photon sphere depends on direction
        // Use prograde equatorial value (smallest, most visible)
        float a = Spin;
        float photonR = 2 * Mass * (1 + MathF.Cos(2f/3f * MathF.Acos(-a)));
        return photonR;
    }
    
    /// <summary>
    /// Kerr ring singularity radius (a = J/M in geometric units)
    /// The singularity is a ring of radius a in the equatorial plane
    /// </summary>
    public float RingSingularityRadius => KerrParameter;
    
    /// <summary>
    /// Frame dragging angular velocity at a given position
    /// ω = 2Mar / ((r² + a²)² - a²Δsin²θ)
    /// This is the angular velocity at which spacetime itself rotates
    /// </summary>
    public float FrameDraggingAngularVelocity(float r, float theta)
    {
        float a = KerrParameter;
        float a2 = a * a;
        float r2 = r * r;
        float sinTheta = MathF.Sin(theta);
        float sin2Theta = sinTheta * sinTheta;
        
        float delta = r2 - 2 * Mass * r + a2;
        float sigma = r2 + a2 * MathF.Cos(theta) * MathF.Cos(theta);
        
        float rPlusA2 = r2 + a2;
        float denominator = rPlusA2 * rPlusA2 - a2 * delta * sin2Theta;
        
        if (MathF.Abs(denominator) < 1e-10f)
            return 0;
            
        return 2 * Mass * a * r / denominator;
    }

    /// <summary>
    /// Create a non-rotating (Schwarzschild) black hole with reasonable default values
    /// </summary>
    public static BlackHole CreateDefault()
    {
        return new BlackHole(Vector3.Zero, 2.0f);
    }
    
    /// <summary>
    /// Create a rotating (Kerr) black hole
    /// </summary>
    /// <param name="position">Position in world space</param>
    /// <param name="mass">Mass (M)</param>
    /// <param name="spin">Dimensionless spin parameter a* (0 to ~0.998)</param>
    /// <param name="spinAxis">Rotation axis direction</param>
    public static BlackHole CreateKerr(Vector3 position, float mass, float spin, Vector3? spinAxis = null)
    {
        return new BlackHole(position, mass, spin, spinAxis);
    }
}
