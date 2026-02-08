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
    /// Schwarzschild radius: rs = 2GM/c^2
    /// In natural units (G = c = 1): rs = 2M
    /// </summary>
    public float SchwarzschildRadius => 2.0f * Mass;

    /// <summary>
    /// Photon sphere radius: r = 1.5 * rs = 3M
    /// Light can orbit here in unstable circular orbits
    /// </summary>
    public float PhotonSphereRadius => 1.5f * SchwarzschildRadius;

    /// <summary>
    /// Inner radius of the accretion disk
    /// Typically at the innermost stable circular orbit (ISCO) = 3 * rs for Schwarzschild
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

    public BlackHole(Vector3 position, float mass)
    {
        Position = position;
        Mass = mass;
        
        // Default disk radii based on mass
        DiskInnerRadius = 3.0f * SchwarzschildRadius; // ISCO
        DiskOuterRadius = 15.0f * SchwarzschildRadius;
    }

    /// <summary>
    /// Create a black hole with reasonable default values for visualization
    /// </summary>
    public static BlackHole CreateDefault()
    {
        // Mass = 2.0 gives Schwarzschild radius of 4.0
        // This makes the black hole more visible and lensing effects stronger
        return new BlackHole(Vector3.Zero, 2.0f);
    }
}
