using RedHoleEngine.Core.ECS;
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
    }
}
