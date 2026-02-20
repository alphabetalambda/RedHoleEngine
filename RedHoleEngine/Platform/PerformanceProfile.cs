using RedHoleEngine.Rendering;
using RedHoleEngine.Rendering.Upscaling;

namespace RedHoleEngine.Platform;

/// <summary>
/// Performance profile presets for different hardware targets
/// </summary>
public enum PerformanceProfileType
{
    /// <summary>Auto-detect based on hardware</summary>
    Auto,
    
    /// <summary>Optimized for Steam Deck (AMD Van Gogh APU)</summary>
    SteamDeck,
    
    /// <summary>Optimized for ROG Ally (AMD Z1/Z1 Extreme)</summary>
    ROGAlly,
    
    /// <summary>Optimized for handheld gaming PCs (generic)</summary>
    Handheld,
    
    /// <summary>Apple Silicon M1 Macs</summary>
    AppleSiliconM1,
    
    /// <summary>Apple Silicon M2 Macs</summary>
    AppleSiliconM2,
    
    /// <summary>Apple Silicon M3+ Macs</summary>
    AppleSiliconM3,
    
    /// <summary>Low-end desktop or laptop with integrated graphics</summary>
    LowEnd,
    
    /// <summary>Entry-level gaming laptop</summary>
    GamingLaptopLowEnd,
    
    /// <summary>High-end gaming laptop</summary>
    GamingLaptopHighEnd,
    
    /// <summary>Balanced settings for mid-range hardware</summary>
    Balanced,
    
    /// <summary>High quality for powerful desktop GPUs</summary>
    HighQuality,
    
    /// <summary>Maximum quality for screenshots/cinematics</summary>
    Ultra
}

/// <summary>
/// Contains all settings for a performance profile
/// </summary>
public class PerformanceProfile
{
    public PerformanceProfileType Type { get; init; }
    public string Name { get; init; } = "Custom";
    public string Description { get; init; } = "";
    
    // Raytracing quality
    public int RaysPerPixel { get; init; } = 1;
    public int MaxBounces { get; init; } = 2;
    public int SamplesPerFrame { get; init; } = 1;
    public bool Accumulate { get; init; } = false;
    public bool Denoise { get; init; } = false;
    public DenoiseMethod DenoiseMethod { get; init; } = DenoiseMethod.Bilateral;
    
    // Lensing quality
    public LensingQuality LensingQuality { get; init; } = LensingQuality.Medium;
    public int LensingMaxSteps { get; init; } = 64;
    public float LensingStepSize { get; init; } = 0.4f;
    public int LensingBvhCheckInterval { get; init; } = 6;
    public float LensingMaxDistance { get; init; } = 200f;
    
    // Visualization
    public bool ShowErgosphere { get; init; } = false;
    public bool ShowPhotonSphere { get; init; } = false;
    
    // Post-processing
    public bool EnableBloom { get; init; } = true;
    public bool EnableVignette { get; init; } = true;
    public bool EnableFilmGrain { get; init; } = false;
    public bool EnableVolumetrics { get; init; } = true;
    public bool EnableGodRays { get; init; } = true;
    
    // Upscaling
    public UpscaleMethod UpscaleMethod { get; init; } = UpscaleMethod.None;
    public UpscaleQuality UpscaleQuality { get; init; } = UpscaleQuality.Quality;
    public bool EnableUpscalerSharpening { get; init; } = true;
    public float UpscalerSharpness { get; init; } = 0.5f;
    
    /// <summary>
    /// Apply this profile to the given raytracer settings
    /// </summary>
    public void ApplyTo(RaytracerSettings settings)
    {
        // Core raytracing
        settings.RaysPerPixel = RaysPerPixel;
        settings.MaxBounces = MaxBounces;
        settings.SamplesPerFrame = SamplesPerFrame;
        settings.Accumulate = Accumulate;
        settings.Denoise = Denoise;
        settings.DenoiseMethod = DenoiseMethod;
        
        // Lensing
        settings.LensingQuality = LensingQuality;
        settings.LensingMaxSteps = LensingMaxSteps;
        settings.LensingStepSize = LensingStepSize;
        settings.LensingBvhCheckInterval = LensingBvhCheckInterval;
        settings.LensingMaxDistance = LensingMaxDistance;
        
        // Visualization
        settings.ShowErgosphere = ShowErgosphere;
        settings.ShowPhotonSphere = ShowPhotonSphere;
        
        // Post-processing
        settings.EnableBloom = EnableBloom;
        settings.EnableVignette = EnableVignette;
        settings.EnableFilmGrain = EnableFilmGrain;
        settings.EnableVolumetrics = EnableVolumetrics;
        settings.EnableGodRays = EnableGodRays;
        
        // Upscaling
        settings.UpscaleMethod = UpscaleMethod;
        settings.UpscaleQuality = UpscaleQuality;
        settings.EnableUpscalerSharpening = EnableUpscalerSharpening;
        settings.UpscalerSharpness = UpscalerSharpness;
        
        settings.Clamp();
    }
}

/// <summary>
/// Factory for creating performance profiles
/// </summary>
public static class PerformanceProfiles
{
    /// <summary>
    /// Get the profile for the current platform (auto-detected)
    /// </summary>
    public static PerformanceProfile GetAutoProfile()
    {
        return PlatformDetector.CurrentPlatform switch
        {
            // Handhelds
            PlatformType.SteamDeck => SteamDeck,
            PlatformType.ROGAlly => ROGAlly,
            PlatformType.LegionGo => ROGAlly, // Similar to ROG Ally (Z1 Extreme)
            PlatformType.AyaNeo => Handheld,
            PlatformType.OneXPlayer => Handheld,
            PlatformType.GPDWin => Handheld,
            PlatformType.GenericHandheld => Handheld,
            
            // Apple Silicon
            PlatformType.AppleSiliconM1 => AppleSiliconM1,
            PlatformType.AppleSiliconM2 => AppleSiliconM2,
            PlatformType.AppleSiliconM3 => AppleSiliconM3,
            PlatformType.AppleSiliconM4Plus => AppleSiliconM3, // Use M3 profile for M4+
            
            // Gaming Laptops
            PlatformType.GamingLaptopLowEnd => GamingLaptopLowEnd,
            PlatformType.GamingLaptopHighEnd => GamingLaptopHighEnd,
            
            // Desktops
            PlatformType.LowEndPC => LowEnd,
            PlatformType.MidRangePC => Balanced,
            PlatformType.HighEndPC => HighQuality,
            
            _ => Balanced
        };
    }
    
    /// <summary>
    /// Get a profile by type
    /// </summary>
    public static PerformanceProfile Get(PerformanceProfileType type)
    {
        return type switch
        {
            PerformanceProfileType.Auto => GetAutoProfile(),
            PerformanceProfileType.SteamDeck => SteamDeck,
            PerformanceProfileType.ROGAlly => ROGAlly,
            PerformanceProfileType.Handheld => Handheld,
            PerformanceProfileType.AppleSiliconM1 => AppleSiliconM1,
            PerformanceProfileType.AppleSiliconM2 => AppleSiliconM2,
            PerformanceProfileType.AppleSiliconM3 => AppleSiliconM3,
            PerformanceProfileType.LowEnd => LowEnd,
            PerformanceProfileType.GamingLaptopLowEnd => GamingLaptopLowEnd,
            PerformanceProfileType.GamingLaptopHighEnd => GamingLaptopHighEnd,
            PerformanceProfileType.Balanced => Balanced,
            PerformanceProfileType.HighQuality => HighQuality,
            PerformanceProfileType.Ultra => Ultra,
            _ => Balanced
        };
    }

    #region Handheld Profiles

    /// <summary>
    /// Steam Deck optimized profile - targets 30-40 FPS with FSR2 upscaling
    /// </summary>
    public static PerformanceProfile SteamDeck { get; } = new()
    {
        Type = PerformanceProfileType.SteamDeck,
        Name = "Steam Deck",
        Description = "Optimized for Steam Deck's AMD Van Gogh APU with FSR2 upscaling",
        
        RaysPerPixel = 1,
        MaxBounces = 1,
        SamplesPerFrame = 1,
        Accumulate = false,
        Denoise = false,
        
        LensingQuality = LensingQuality.Low,
        LensingMaxSteps = 32,
        LensingStepSize = 0.6f,
        LensingBvhCheckInterval = 8,
        LensingMaxDistance = 100f,
        
        ShowErgosphere = false,
        ShowPhotonSphere = false,
        
        EnableBloom = true,
        EnableVignette = true,
        EnableFilmGrain = false,
        EnableVolumetrics = false,
        EnableGodRays = false,
        
        UpscaleMethod = UpscaleMethod.FSR2,
        UpscaleQuality = UpscaleQuality.Performance,
        EnableUpscalerSharpening = true,
        UpscalerSharpness = 0.6f
    };

    /// <summary>
    /// ROG Ally optimized profile - Z1 Extreme is more powerful than Steam Deck
    /// </summary>
    public static PerformanceProfile ROGAlly { get; } = new()
    {
        Type = PerformanceProfileType.ROGAlly,
        Name = "ROG Ally",
        Description = "Optimized for ASUS ROG Ally with AMD Z1 Extreme",
        
        RaysPerPixel = 1,
        MaxBounces = 1,
        SamplesPerFrame = 1,
        Accumulate = false,
        Denoise = false,
        
        // Z1 Extreme is ~30% faster than Van Gogh, can handle more steps
        LensingQuality = LensingQuality.Low,
        LensingMaxSteps = 48,
        LensingStepSize = 0.5f,
        LensingBvhCheckInterval = 6,
        LensingMaxDistance = 120f,
        
        ShowErgosphere = false,
        ShowPhotonSphere = false,
        
        EnableBloom = true,
        EnableVignette = true,
        EnableFilmGrain = false,
        EnableVolumetrics = false,
        EnableGodRays = false,
        
        // FSR2 Balanced for better quality on 1080p screen
        UpscaleMethod = UpscaleMethod.FSR2,
        UpscaleQuality = UpscaleQuality.Balanced,
        EnableUpscalerSharpening = true,
        UpscalerSharpness = 0.5f
    };

    /// <summary>
    /// Generic handheld profile (AYA NEO, OneXPlayer, GPD Win, etc.)
    /// </summary>
    public static PerformanceProfile Handheld { get; } = new()
    {
        Type = PerformanceProfileType.Handheld,
        Name = "Handheld",
        Description = "Generic profile for handheld gaming PCs",
        
        RaysPerPixel = 1,
        MaxBounces = 1,
        SamplesPerFrame = 1,
        Accumulate = false,
        Denoise = false,
        
        LensingQuality = LensingQuality.Low,
        LensingMaxSteps = 40,
        LensingStepSize = 0.55f,
        LensingBvhCheckInterval = 8,
        LensingMaxDistance = 100f,
        
        ShowErgosphere = false,
        ShowPhotonSphere = false,
        
        EnableBloom = true,
        EnableVignette = true,
        EnableFilmGrain = false,
        EnableVolumetrics = false,
        EnableGodRays = false,
        
        UpscaleMethod = UpscaleMethod.FSR2,
        UpscaleQuality = UpscaleQuality.Performance,
        EnableUpscalerSharpening = true,
        UpscalerSharpness = 0.5f
    };

    #endregion

    #region Apple Silicon Profiles

    /// <summary>
    /// Apple Silicon M1 profile
    /// </summary>
    public static PerformanceProfile AppleSiliconM1 { get; } = new()
    {
        Type = PerformanceProfileType.AppleSiliconM1,
        Name = "Apple M1",
        Description = "Optimized for Apple M1 Macs",
        
        RaysPerPixel = 1,
        MaxBounces = 1,
        SamplesPerFrame = 1,
        Accumulate = false,
        Denoise = true,
        DenoiseMethod = DenoiseMethod.Bilateral,
        
        LensingQuality = LensingQuality.Medium,
        LensingMaxSteps = 48,
        LensingStepSize = 0.5f,
        LensingBvhCheckInterval = 6,
        LensingMaxDistance = 150f,
        
        ShowErgosphere = false,
        ShowPhotonSphere = false,
        
        EnableBloom = true,
        EnableVignette = true,
        EnableFilmGrain = false,
        EnableVolumetrics = true,
        EnableGodRays = false,
        
        // No FSR on macOS (Metal), use native
        UpscaleMethod = UpscaleMethod.None,
        UpscaleQuality = UpscaleQuality.Native,
        EnableUpscalerSharpening = false,
        UpscalerSharpness = 0.0f
    };

    /// <summary>
    /// Apple Silicon M2 profile
    /// </summary>
    public static PerformanceProfile AppleSiliconM2 { get; } = new()
    {
        Type = PerformanceProfileType.AppleSiliconM2,
        Name = "Apple M2",
        Description = "Optimized for Apple M2 Macs",
        
        RaysPerPixel = 1,
        MaxBounces = 2,
        SamplesPerFrame = 1,
        Accumulate = false,
        Denoise = true,
        DenoiseMethod = DenoiseMethod.Bilateral,
        
        LensingQuality = LensingQuality.Medium,
        LensingMaxSteps = 64,
        LensingStepSize = 0.4f,
        LensingBvhCheckInterval = 6,
        LensingMaxDistance = 180f,
        
        ShowErgosphere = false,
        ShowPhotonSphere = false,
        
        EnableBloom = true,
        EnableVignette = true,
        EnableFilmGrain = true,
        EnableVolumetrics = true,
        EnableGodRays = true,
        
        UpscaleMethod = UpscaleMethod.None,
        UpscaleQuality = UpscaleQuality.Native,
        EnableUpscalerSharpening = false,
        UpscalerSharpness = 0.0f
    };

    /// <summary>
    /// Apple Silicon M3+ profile
    /// </summary>
    public static PerformanceProfile AppleSiliconM3 { get; } = new()
    {
        Type = PerformanceProfileType.AppleSiliconM3,
        Name = "Apple M3+",
        Description = "Optimized for Apple M3 and newer Macs",
        
        RaysPerPixel = 1,
        MaxBounces = 2,
        SamplesPerFrame = 1,
        Accumulate = false,
        Denoise = true,
        DenoiseMethod = DenoiseMethod.ATrous,
        
        // M3 has hardware ray tracing, can handle more
        LensingQuality = LensingQuality.Medium,
        LensingMaxSteps = 80,
        LensingStepSize = 0.35f,
        LensingBvhCheckInterval = 5,
        LensingMaxDistance = 200f,
        
        ShowErgosphere = false,
        ShowPhotonSphere = true,
        
        EnableBloom = true,
        EnableVignette = true,
        EnableFilmGrain = true,
        EnableVolumetrics = true,
        EnableGodRays = true,
        
        UpscaleMethod = UpscaleMethod.None,
        UpscaleQuality = UpscaleQuality.Native,
        EnableUpscalerSharpening = false,
        UpscalerSharpness = 0.0f
    };

    #endregion

    #region Gaming Laptop Profiles

    /// <summary>
    /// Entry-level gaming laptop (GTX 1650, RTX 3050, etc.)
    /// </summary>
    public static PerformanceProfile GamingLaptopLowEnd { get; } = new()
    {
        Type = PerformanceProfileType.GamingLaptopLowEnd,
        Name = "Gaming Laptop (Entry)",
        Description = "For entry-level gaming laptops (GTX 1650, RTX 3050, etc.)",
        
        RaysPerPixel = 1,
        MaxBounces = 1,
        SamplesPerFrame = 1,
        Accumulate = false,
        Denoise = true,
        DenoiseMethod = DenoiseMethod.Bilateral,
        
        LensingQuality = LensingQuality.Medium,
        LensingMaxSteps = 56,
        LensingStepSize = 0.45f,
        LensingBvhCheckInterval = 6,
        LensingMaxDistance = 180f,
        
        ShowErgosphere = false,
        ShowPhotonSphere = false,
        
        EnableBloom = true,
        EnableVignette = true,
        EnableFilmGrain = true,
        EnableVolumetrics = true,
        EnableGodRays = false,
        
        // DLSS preferred on NVIDIA, FSR2 fallback
        UpscaleMethod = UpscaleMethod.DLSS,
        UpscaleQuality = UpscaleQuality.Balanced,
        EnableUpscalerSharpening = true,
        UpscalerSharpness = 0.5f
    };

    /// <summary>
    /// High-end gaming laptop (RTX 4070+)
    /// </summary>
    public static PerformanceProfile GamingLaptopHighEnd { get; } = new()
    {
        Type = PerformanceProfileType.GamingLaptopHighEnd,
        Name = "Gaming Laptop (High-End)",
        Description = "For high-end gaming laptops (RTX 4070+, RX 7700M+)",
        
        RaysPerPixel = 1,
        MaxBounces = 2,
        SamplesPerFrame = 1,
        Accumulate = false,
        Denoise = true,
        DenoiseMethod = DenoiseMethod.ATrous,
        
        LensingQuality = LensingQuality.High,
        LensingMaxSteps = 96,
        LensingStepSize = 0.3f,
        LensingBvhCheckInterval = 4,
        LensingMaxDistance = 250f,
        
        ShowErgosphere = true,
        ShowPhotonSphere = true,
        
        EnableBloom = true,
        EnableVignette = true,
        EnableFilmGrain = true,
        EnableVolumetrics = true,
        EnableGodRays = true,
        
        // Can run native or DLSS Quality
        UpscaleMethod = UpscaleMethod.DLSS,
        UpscaleQuality = UpscaleQuality.Quality,
        EnableUpscalerSharpening = true,
        UpscalerSharpness = 0.4f
    };

    #endregion

    #region Desktop Profiles

    /// <summary>
    /// Low-end profile for integrated graphics or old hardware
    /// </summary>
    public static PerformanceProfile LowEnd { get; } = new()
    {
        Type = PerformanceProfileType.LowEnd,
        Name = "Low End",
        Description = "For integrated graphics or older hardware",
        
        RaysPerPixel = 1,
        MaxBounces = 1,
        SamplesPerFrame = 1,
        Accumulate = false,
        Denoise = false,
        
        LensingQuality = LensingQuality.Low,
        LensingMaxSteps = 48,
        LensingStepSize = 0.5f,
        LensingBvhCheckInterval = 8,
        LensingMaxDistance = 150f,
        
        ShowErgosphere = false,
        ShowPhotonSphere = false,
        
        EnableBloom = false,
        EnableVignette = true,
        EnableFilmGrain = false,
        EnableVolumetrics = false,
        EnableGodRays = false,
        
        UpscaleMethod = UpscaleMethod.FSR2,
        UpscaleQuality = UpscaleQuality.Performance,
        EnableUpscalerSharpening = true,
        UpscalerSharpness = 0.5f
    };

    /// <summary>
    /// Balanced profile for mid-range hardware
    /// </summary>
    public static PerformanceProfile Balanced { get; } = new()
    {
        Type = PerformanceProfileType.Balanced,
        Name = "Balanced",
        Description = "Good balance of quality and performance for mid-range hardware",
        
        RaysPerPixel = 1,
        MaxBounces = 2,
        SamplesPerFrame = 1,
        Accumulate = false,
        Denoise = true,
        DenoiseMethod = DenoiseMethod.Bilateral,
        
        LensingQuality = LensingQuality.Medium,
        LensingMaxSteps = 64,
        LensingStepSize = 0.4f,
        LensingBvhCheckInterval = 6,
        LensingMaxDistance = 200f,
        
        ShowErgosphere = false,
        ShowPhotonSphere = false,
        
        EnableBloom = true,
        EnableVignette = true,
        EnableFilmGrain = true,
        EnableVolumetrics = true,
        EnableGodRays = true,
        
        UpscaleMethod = UpscaleMethod.None,
        UpscaleQuality = UpscaleQuality.Quality,
        EnableUpscalerSharpening = true,
        UpscalerSharpness = 0.5f
    };

    /// <summary>
    /// High quality profile for powerful desktop GPUs
    /// </summary>
    public static PerformanceProfile HighQuality { get; } = new()
    {
        Type = PerformanceProfileType.HighQuality,
        Name = "High Quality",
        Description = "High quality for powerful desktop GPUs",
        
        RaysPerPixel = 2,
        MaxBounces = 3,
        SamplesPerFrame = 1,
        Accumulate = false,
        Denoise = true,
        DenoiseMethod = DenoiseMethod.ATrous,
        
        LensingQuality = LensingQuality.High,
        LensingMaxSteps = 128,
        LensingStepSize = 0.25f,
        LensingBvhCheckInterval = 4,
        LensingMaxDistance = 300f,
        
        ShowErgosphere = true,
        ShowPhotonSphere = true,
        
        EnableBloom = true,
        EnableVignette = true,
        EnableFilmGrain = true,
        EnableVolumetrics = true,
        EnableGodRays = true,
        
        UpscaleMethod = UpscaleMethod.None,
        UpscaleQuality = UpscaleQuality.Quality,
        EnableUpscalerSharpening = true,
        UpscalerSharpness = 0.5f
    };

    /// <summary>
    /// Ultra quality for screenshots and cinematics
    /// </summary>
    public static PerformanceProfile Ultra { get; } = new()
    {
        Type = PerformanceProfileType.Ultra,
        Name = "Ultra",
        Description = "Maximum quality for screenshots and cinematics",
        
        RaysPerPixel = 4,
        MaxBounces = 4,
        SamplesPerFrame = 2,
        Accumulate = true,
        Denoise = true,
        DenoiseMethod = DenoiseMethod.SVGF,
        
        LensingQuality = LensingQuality.Ultra,
        LensingMaxSteps = 256,
        LensingStepSize = 0.12f,
        LensingBvhCheckInterval = 2,
        LensingMaxDistance = 500f,
        
        ShowErgosphere = true,
        ShowPhotonSphere = true,
        
        EnableBloom = true,
        EnableVignette = true,
        EnableFilmGrain = true,
        EnableVolumetrics = true,
        EnableGodRays = true,
        
        UpscaleMethod = UpscaleMethod.None,
        UpscaleQuality = UpscaleQuality.Native,
        EnableUpscalerSharpening = false,
        UpscalerSharpness = 0.0f
    };

    #endregion
}
