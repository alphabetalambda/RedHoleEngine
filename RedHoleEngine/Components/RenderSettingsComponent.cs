using RedHoleEngine.Core.ECS;
using RedHoleEngine.Platform;
using RedHoleEngine.Rendering;

namespace RedHoleEngine.Components;

/// <summary>
/// Per-scene render settings override.
/// Attach to a single entity to drive renderer quality.
/// </summary>
public struct RenderSettingsComponent : IComponent
{
    public bool Enabled;
    public RenderMode Mode;
    public int RaysPerPixel;
    public int MaxBounces;
    public int SamplesPerFrame;
    public bool Accumulate;
    public bool Denoise;
    public bool ResetAccumulation;
    public RaytracerQualityPreset Preset;
    public int MaxRaysPerPixelLimit;
    public int MaxBouncesLimit;
    public int MaxSamplesPerFrameLimit;
    
    // Gravitational lensing quality
    public LensingQuality LensingQuality;
    public int LensingMaxSteps;
    public float LensingStepSize;
    public int LensingBvhCheckInterval;
    public float LensingMaxDistance;
    
    // Kerr black hole visualization
    public bool ShowErgosphere;
    public float ErgosphereOpacity;
    public bool ShowPhotonSphere;
    public float PhotonSphereOpacity;
    
    // Performance profile (Auto = use platform detection)
    public PerformanceProfileType PerformanceProfile;
    public bool UsePerformanceProfile;

    public RenderSettingsComponent(RenderMode mode = RenderMode.Raytraced)
    {
        Enabled = true;
        Mode = mode;
        RaysPerPixel = 2;
        MaxBounces = 2;
        SamplesPerFrame = 1;
        Accumulate = true;
        Denoise = false;
        ResetAccumulation = false;
        Preset = RaytracerQualityPreset.Balanced;
        MaxRaysPerPixelLimit = 64;
        MaxBouncesLimit = 8;
        MaxSamplesPerFrameLimit = 8;
        
        // Default to Medium lensing quality
        LensingQuality = LensingQuality.Medium;
        LensingMaxSteps = 64;
        LensingStepSize = 0.4f;
        LensingBvhCheckInterval = 6;
        LensingMaxDistance = 200f;
        
        // Kerr visualization defaults
        ShowErgosphere = false;
        ErgosphereOpacity = 0.3f;
        ShowPhotonSphere = false;
        PhotonSphereOpacity = 0.2f;
        
        // Performance profile defaults
        PerformanceProfile = PerformanceProfileType.Auto;
        UsePerformanceProfile = false; // Default to manual settings for backward compatibility
    }
    
    /// <summary>
    /// Create a render settings component using a performance profile.
    /// This is the recommended way to configure settings for different hardware.
    /// </summary>
    public static RenderSettingsComponent FromProfile(PerformanceProfileType profileType, RenderMode mode = RenderMode.Raytraced)
    {
        var profile = PerformanceProfiles.Get(profileType);
        return new RenderSettingsComponent(mode)
        {
            UsePerformanceProfile = true,
            PerformanceProfile = profileType,
            
            // Apply profile values
            RaysPerPixel = profile.RaysPerPixel,
            MaxBounces = profile.MaxBounces,
            SamplesPerFrame = profile.SamplesPerFrame,
            Accumulate = profile.Accumulate,
            Denoise = profile.Denoise,
            
            LensingQuality = profile.LensingQuality,
            LensingMaxSteps = profile.LensingMaxSteps,
            LensingStepSize = profile.LensingStepSize,
            LensingBvhCheckInterval = profile.LensingBvhCheckInterval,
            LensingMaxDistance = profile.LensingMaxDistance,
            
            ShowErgosphere = profile.ShowErgosphere,
            ShowPhotonSphere = profile.ShowPhotonSphere
        };
    }
    
    /// <summary>
    /// Create a render settings component with auto-detected performance profile.
    /// Steam Deck and other platforms will be automatically detected.
    /// </summary>
    public static RenderSettingsComponent AutoDetect(RenderMode mode = RenderMode.Raytraced)
    {
        return FromProfile(PerformanceProfileType.Auto, mode);
    }
}
