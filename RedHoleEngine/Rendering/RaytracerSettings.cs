namespace RedHoleEngine.Rendering;

/// <summary>
/// Gravitational lensing quality presets
/// </summary>
public enum LensingQuality
{
    Low,      // Fast, lower accuracy
    Medium,   // Balanced (default)
    High,     // Best quality, slower
    Ultra,    // Maximum quality for screenshots
    Custom    // Use manual settings
}

/// <summary>
/// Runtime raytracer quality settings.
/// </summary>
public class RaytracerSettings
{
    public int RaysPerPixel { get; set; } = 1;
    public int MaxBounces { get; set; } = 1;
    public int SamplesPerFrame { get; set; } = 1;
    public bool Accumulate { get; set; } = true;
    public bool Denoise { get; set; }
    public bool ResetAccumulation { get; set; }
    public RaytracerQualityPreset Preset { get; set; } = RaytracerQualityPreset.Balanced;
    
    // Gravitational lensing quality settings
    public LensingQuality LensingQuality { get; set; } = LensingQuality.Medium;
    public int LensingMaxSteps { get; set; } = 64;
    public float LensingStepSize { get; set; } = 0.4f;
    public int LensingBvhCheckInterval { get; set; } = 6;
    public float LensingMaxDistance { get; set; } = 200f;
    
    // Kerr black hole visualization
    public bool ShowErgosphere { get; set; } = false;
    public float ErgosphereOpacity { get; set; } = 0.3f;
    public bool ShowPhotonSphere { get; set; } = false;
    public float PhotonSphereOpacity { get; set; } = 0.2f;
    
    // Post-processing settings
    public bool EnableBloom { get; set; } = true;
    public float BloomThreshold { get; set; } = 1.2f;
    public float BloomIntensity { get; set; } = 0.15f;
    public float Exposure { get; set; } = 1.0f;
    public bool EnableVignette { get; set; } = true;
    public float VignetteIntensity { get; set; } = 0.5f;
    public bool EnableFilmGrain { get; set; } = true;
    public float FilmGrainIntensity { get; set; } = 0.02f;
    
    // Volumetric effects
    public bool EnableVolumetrics { get; set; } = true;
    public float VolumetricIntensity { get; set; } = 1.0f;
    public bool EnableGodRays { get; set; } = true;
    public float GodRayIntensity { get; set; } = 1.0f;

    public int MaxRaysPerPixelLimit { get; set; } = 64;
    public int MaxBouncesLimit { get; set; } = 8;
    public int MaxSamplesPerFrameLimit { get; set; } = 8;

    public void Clamp()
    {
        if (MaxRaysPerPixelLimit < 1) MaxRaysPerPixelLimit = 1;
        if (MaxBouncesLimit < 1) MaxBouncesLimit = 1;
        if (MaxSamplesPerFrameLimit < 1) MaxSamplesPerFrameLimit = 1;

        RaysPerPixel = Math.Clamp(RaysPerPixel, 1, MaxRaysPerPixelLimit);
        MaxBounces = Math.Clamp(MaxBounces, 1, MaxBouncesLimit);
        SamplesPerFrame = Math.Clamp(SamplesPerFrame, 1, MaxSamplesPerFrameLimit);
        
        LensingMaxSteps = Math.Clamp(LensingMaxSteps, 16, 256);
        LensingStepSize = Math.Clamp(LensingStepSize, 0.1f, 1.0f);
        LensingBvhCheckInterval = Math.Clamp(LensingBvhCheckInterval, 1, 16);
        LensingMaxDistance = Math.Clamp(LensingMaxDistance, 50f, 1000f);
        
        // Post-processing clamps
        BloomThreshold = Math.Clamp(BloomThreshold, 0.5f, 3.0f);
        BloomIntensity = Math.Clamp(BloomIntensity, 0.0f, 1.0f);
        Exposure = Math.Clamp(Exposure, 0.1f, 5.0f);
        VignetteIntensity = Math.Clamp(VignetteIntensity, 0.0f, 1.0f);
        FilmGrainIntensity = Math.Clamp(FilmGrainIntensity, 0.0f, 0.1f);
        VolumetricIntensity = Math.Clamp(VolumetricIntensity, 0.0f, 2.0f);
        GodRayIntensity = Math.Clamp(GodRayIntensity, 0.0f, 2.0f);
    }

    public void ApplyPreset(RaytracerQualityPreset preset)
    {
        if (preset == RaytracerQualityPreset.Custom)
        {
            Preset = preset;
            return;
        }
        Preset = preset;
        var values = RaytracerPresetUtilities.GetPresetValues(preset);
        RaysPerPixel = values.RaysPerPixel;
        MaxBounces = values.MaxBounces;
        SamplesPerFrame = values.SamplesPerFrame;
        Accumulate = values.Accumulate;
        Denoise = values.Denoise;
    }
    
    /// <summary>
    /// Apply a lensing quality preset
    /// </summary>
    public void ApplyLensingQuality(LensingQuality quality)
    {
        LensingQuality = quality;
        switch (quality)
        {
            case LensingQuality.Low:
                LensingMaxSteps = 32;
                LensingStepSize = 0.6f;
                LensingBvhCheckInterval = 8;
                break;
            case LensingQuality.Medium:
                LensingMaxSteps = 64;
                LensingStepSize = 0.4f;
                LensingBvhCheckInterval = 6;
                break;
            case LensingQuality.High:
                LensingMaxSteps = 128;
                LensingStepSize = 0.25f;
                LensingBvhCheckInterval = 4;
                break;
            case LensingQuality.Ultra:
                LensingMaxSteps = 200;
                LensingStepSize = 0.15f;
                LensingBvhCheckInterval = 2;
                break;
        }
    }
}
