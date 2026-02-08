using RedHoleEngine.Core.ECS;

namespace RedHoleEngine.Components;

/// <summary>
/// Component for rendering an accretion disk around a gravity source.
/// Should be attached to the same entity as a GravitySourceComponent.
/// </summary>
public struct AccretionDiskComponent : IComponent
{
    /// <summary>
    /// Inner radius of the disk (typically at ISCO)
    /// </summary>
    public float InnerRadius;
    
    /// <summary>
    /// Outer radius of the disk
    /// </summary>
    public float OuterRadius;
    
    /// <summary>
    /// Temperature at inner edge (Kelvin) - affects color
    /// </summary>
    public float InnerTemperature;
    
    /// <summary>
    /// Temperature at outer edge (Kelvin) - affects color
    /// </summary>
    public float OuterTemperature;
    
    /// <summary>
    /// Disk thickness (0 = infinitely thin, 1 = sphere-like)
    /// </summary>
    public float Thickness;
    
    /// <summary>
    /// Angular velocity of the disk (for visual rotation)
    /// </summary>
    public float AngularVelocity;
    
    /// <summary>
    /// Disk opacity/brightness
    /// </summary>
    public float Opacity;
    
    /// <summary>
    /// Whether to apply Doppler beaming effects
    /// </summary>
    public bool DopplerBeaming;
    
    /// <summary>
    /// Whether to apply gravitational redshift
    /// </summary>
    public bool GravitationalRedshift;

    /// <summary>
    /// Create a default accretion disk based on a black hole mass
    /// </summary>
    public static AccretionDiskComponent CreateForBlackHole(float blackHoleMass)
    {
        float rs = 2f * blackHoleMass; // Schwarzschild radius
        
        return new AccretionDiskComponent
        {
            InnerRadius = 3f * rs,      // ISCO
            OuterRadius = 15f * rs,
            InnerTemperature = 10000f,  // Hot inner region
            OuterTemperature = 3000f,   // Cooler outer region
            Thickness = 0.1f,
            AngularVelocity = 1f,
            Opacity = 1f,
            DopplerBeaming = true,
            GravitationalRedshift = true
        };
    }

    /// <summary>
    /// Create a custom accretion disk
    /// </summary>
    public static AccretionDiskComponent Create(
        float innerRadius,
        float outerRadius,
        float innerTemp = 10000f,
        float outerTemp = 3000f)
    {
        return new AccretionDiskComponent
        {
            InnerRadius = innerRadius,
            OuterRadius = outerRadius,
            InnerTemperature = innerTemp,
            OuterTemperature = outerTemp,
            Thickness = 0.1f,
            AngularVelocity = 1f,
            Opacity = 1f,
            DopplerBeaming = true,
            GravitationalRedshift = true
        };
    }
}
