using RedHoleEngine.Platform;
using RedHoleEngine.Rendering;
using RedHoleEngine.Rendering.Upscaling;
using Xunit.Abstractions;

namespace RedHoleEngine.Tests.Platform;

/// <summary>
/// Unit tests for performance profiles, especially Steam Deck FSR2 integration.
/// </summary>
public class PerformanceProfileTests
{
    private readonly ITestOutputHelper _output;

    public PerformanceProfileTests(ITestOutputHelper output)
    {
        _output = output;
    }

    #region Steam Deck Profile

    [Fact]
    public void SteamDeckProfile_UsesFSR2()
    {
        var profile = PerformanceProfiles.SteamDeck;
        
        Assert.Equal(UpscaleMethod.FSR2, profile.UpscaleMethod);
        _output.WriteLine($"Steam Deck upscale method: {profile.UpscaleMethod}");
    }

    [Fact]
    public void SteamDeckProfile_UsesPerformanceQuality()
    {
        var profile = PerformanceProfiles.SteamDeck;
        
        Assert.Equal(UpscaleQuality.Performance, profile.UpscaleQuality);
        _output.WriteLine($"Steam Deck upscale quality: {profile.UpscaleQuality}");
    }

    [Fact]
    public void SteamDeckProfile_HasReducedLensingSteps()
    {
        var profile = PerformanceProfiles.SteamDeck;
        var balancedProfile = PerformanceProfiles.Balanced;
        
        Assert.True(profile.LensingMaxSteps < balancedProfile.LensingMaxSteps);
        Assert.Equal(32, profile.LensingMaxSteps);
        _output.WriteLine($"Steam Deck lensing steps: {profile.LensingMaxSteps} (vs Balanced: {balancedProfile.LensingMaxSteps})");
    }

    [Fact]
    public void SteamDeckProfile_DisablesExpensiveEffects()
    {
        var profile = PerformanceProfiles.SteamDeck;
        
        Assert.False(profile.EnableVolumetrics);
        Assert.False(profile.EnableGodRays);
        Assert.False(profile.ShowErgosphere);
        Assert.False(profile.ShowPhotonSphere);
        Assert.False(profile.EnableFilmGrain);
        
        _output.WriteLine("Steam Deck disabled effects: Volumetrics, GodRays, Ergosphere, PhotonSphere, FilmGrain");
    }

    [Fact]
    public void SteamDeckProfile_KeepsCheapEffects()
    {
        var profile = PerformanceProfiles.SteamDeck;
        
        Assert.True(profile.EnableBloom);      // Bloom is relatively cheap
        Assert.True(profile.EnableVignette);   // Vignette is very cheap
        
        _output.WriteLine("Steam Deck enabled effects: Bloom, Vignette");
    }

    [Fact]
    public void SteamDeckProfile_MinimizesRaytracing()
    {
        var profile = PerformanceProfiles.SteamDeck;
        
        Assert.Equal(1, profile.RaysPerPixel);
        Assert.Equal(1, profile.MaxBounces);
        Assert.Equal(1, profile.SamplesPerFrame);
        Assert.False(profile.Accumulate);
        Assert.False(profile.Denoise);
    }

    [Fact]
    public void SteamDeckProfile_HasCorrectMetadata()
    {
        var profile = PerformanceProfiles.SteamDeck;
        
        Assert.Equal(PerformanceProfileType.SteamDeck, profile.Type);
        Assert.Equal("Steam Deck", profile.Name);
        Assert.Contains("FSR2", profile.Description);
    }

    [Fact]
    public void SteamDeckProfile_FSR2Settings_AreOptimal()
    {
        var profile = PerformanceProfiles.SteamDeck;
        
        // FSR2 sharpening should be enabled with good value
        Assert.True(profile.EnableUpscalerSharpening);
        Assert.InRange(profile.UpscalerSharpness, 0.5f, 0.7f);
        
        _output.WriteLine($"FSR2 sharpness: {profile.UpscalerSharpness}");
    }

    #endregion

    #region Profile Application

    [Fact]
    public void ApplyTo_RaytracerSettings_SetsAllValues()
    {
        var profile = PerformanceProfiles.SteamDeck;
        var settings = new RaytracerSettings();
        
        // Store original values
        var originalMethod = settings.UpscaleMethod;
        var originalSteps = settings.LensingMaxSteps;
        
        // Apply profile
        profile.ApplyTo(settings);
        
        // Verify changes
        Assert.Equal(profile.UpscaleMethod, settings.UpscaleMethod);
        Assert.Equal(profile.UpscaleQuality, settings.UpscaleQuality);
        Assert.Equal(profile.LensingMaxSteps, settings.LensingMaxSteps);
        Assert.Equal(profile.EnableVolumetrics, settings.EnableVolumetrics);
        Assert.Equal(profile.EnableGodRays, settings.EnableGodRays);
        Assert.Equal(profile.RaysPerPixel, settings.RaysPerPixel);
        Assert.Equal(profile.MaxBounces, settings.MaxBounces);
        
        _output.WriteLine($"Applied Steam Deck profile to RaytracerSettings");
        _output.WriteLine($"UpscaleMethod: {originalMethod} -> {settings.UpscaleMethod}");
        _output.WriteLine($"LensingMaxSteps: {originalSteps} -> {settings.LensingMaxSteps}");
    }

    [Fact]
    public void ApplyPerformanceProfile_ByType_Works()
    {
        var settings = new RaytracerSettings();
        
        settings.ApplyPerformanceProfile(PerformanceProfileType.SteamDeck);
        
        Assert.Equal(UpscaleMethod.FSR2, settings.UpscaleMethod);
        Assert.Equal(UpscaleQuality.Performance, settings.UpscaleQuality);
        Assert.Equal(32, settings.LensingMaxSteps);
    }

    [Fact]
    public void ApplyPerformanceProfile_Clamps_Values()
    {
        var settings = new RaytracerSettings();
        
        // Apply a profile
        settings.ApplyPerformanceProfile(PerformanceProfileType.SteamDeck);
        
        // Values should be within valid ranges after Clamp()
        Assert.InRange(settings.LensingMaxSteps, 16, 256);
        Assert.InRange(settings.LensingStepSize, 0.1f, 1.0f);
        Assert.InRange(settings.RaysPerPixel, 1, settings.MaxRaysPerPixelLimit);
    }

    #endregion

    #region All Profiles

    [Theory]
    [InlineData(PerformanceProfileType.SteamDeck)]
    [InlineData(PerformanceProfileType.ROGAlly)]
    [InlineData(PerformanceProfileType.Handheld)]
    [InlineData(PerformanceProfileType.AppleSiliconM1)]
    [InlineData(PerformanceProfileType.AppleSiliconM2)]
    [InlineData(PerformanceProfileType.AppleSiliconM3)]
    [InlineData(PerformanceProfileType.LowEnd)]
    [InlineData(PerformanceProfileType.GamingLaptopLowEnd)]
    [InlineData(PerformanceProfileType.GamingLaptopHighEnd)]
    [InlineData(PerformanceProfileType.Balanced)]
    [InlineData(PerformanceProfileType.HighQuality)]
    [InlineData(PerformanceProfileType.Ultra)]
    public void GetProfile_ReturnsValidProfile(PerformanceProfileType type)
    {
        var profile = PerformanceProfiles.Get(type);
        
        Assert.NotNull(profile);
        Assert.Equal(type, profile.Type);
        Assert.NotEmpty(profile.Name);
        Assert.NotEmpty(profile.Description);
        
        _output.WriteLine($"{type}: {profile.Name} - {profile.Description}");
    }

    [Fact]
    public void GetProfile_Auto_ReturnsNonNullProfile()
    {
        var profile = PerformanceProfiles.Get(PerformanceProfileType.Auto);
        
        Assert.NotNull(profile);
        Assert.NotEmpty(profile.Name);
    }

    [Fact]
    public void AllProfiles_HaveValidLensingSettings()
    {
        foreach (PerformanceProfileType type in Enum.GetValues<PerformanceProfileType>())
        {
            if (type == PerformanceProfileType.Auto) continue;
            
            var profile = PerformanceProfiles.Get(type);
            
            Assert.InRange(profile.LensingMaxSteps, 16, 256);
            Assert.InRange(profile.LensingStepSize, 0.1f, 1.0f);
            Assert.InRange(profile.LensingBvhCheckInterval, 1, 16);
            Assert.InRange(profile.LensingMaxDistance, 50f, 1000f);
            
            _output.WriteLine($"{type}: Steps={profile.LensingMaxSteps}, StepSize={profile.LensingStepSize}");
        }
    }

    [Fact]
    public void AllProfiles_HaveValidRaytracingSettings()
    {
        foreach (PerformanceProfileType type in Enum.GetValues<PerformanceProfileType>())
        {
            if (type == PerformanceProfileType.Auto) continue;
            
            var profile = PerformanceProfiles.Get(type);
            
            Assert.InRange(profile.RaysPerPixel, 1, 64);
            Assert.InRange(profile.MaxBounces, 1, 8);
            Assert.InRange(profile.SamplesPerFrame, 1, 8);
            
            _output.WriteLine($"{type}: RPP={profile.RaysPerPixel}, Bounces={profile.MaxBounces}, SPF={profile.SamplesPerFrame}");
        }
    }

    #endregion

    #region Profile Hierarchy

    [Fact]
    public void Profiles_HaveIncreasingQuality()
    {
        var lowEnd = PerformanceProfiles.LowEnd;
        var balanced = PerformanceProfiles.Balanced;
        var highQuality = PerformanceProfiles.HighQuality;
        var ultra = PerformanceProfiles.Ultra;
        
        // Lensing steps should increase with quality
        Assert.True(lowEnd.LensingMaxSteps <= balanced.LensingMaxSteps);
        Assert.True(balanced.LensingMaxSteps <= highQuality.LensingMaxSteps);
        Assert.True(highQuality.LensingMaxSteps <= ultra.LensingMaxSteps);
        
        // Rays per pixel should increase with quality
        Assert.True(lowEnd.RaysPerPixel <= balanced.RaysPerPixel);
        Assert.True(balanced.RaysPerPixel <= highQuality.RaysPerPixel);
        Assert.True(highQuality.RaysPerPixel <= ultra.RaysPerPixel);
        
        _output.WriteLine("Quality hierarchy verified:");
        _output.WriteLine($"  LowEnd: {lowEnd.LensingMaxSteps} steps, {lowEnd.RaysPerPixel} RPP");
        _output.WriteLine($"  Balanced: {balanced.LensingMaxSteps} steps, {balanced.RaysPerPixel} RPP");
        _output.WriteLine($"  HighQuality: {highQuality.LensingMaxSteps} steps, {highQuality.RaysPerPixel} RPP");
        _output.WriteLine($"  Ultra: {ultra.LensingMaxSteps} steps, {ultra.RaysPerPixel} RPP");
    }

    [Fact]
    public void SteamDeck_IsSimilarToLowEnd()
    {
        var steamDeck = PerformanceProfiles.SteamDeck;
        var lowEnd = PerformanceProfiles.LowEnd;
        
        Assert.Equal(lowEnd.RaysPerPixel, steamDeck.RaysPerPixel);
        Assert.Equal(lowEnd.MaxBounces, steamDeck.MaxBounces);
        Assert.Equal(lowEnd.SamplesPerFrame, steamDeck.SamplesPerFrame);
    }

    #endregion

    #region Handheld Profiles

    [Fact]
    public void ROGAlly_IsBetterThanSteamDeck()
    {
        var steamDeck = PerformanceProfiles.SteamDeck;
        var rogAlly = PerformanceProfiles.ROGAlly;
        
        // ROG Ally has Z1 Extreme which is more powerful, allowing more lensing steps
        Assert.True(rogAlly.LensingMaxSteps >= steamDeck.LensingMaxSteps);
        
        // ROG Ally can use better upscale quality (Balanced vs Performance)
        // Lower enum value = better quality (Native=0, Performance=4)
        Assert.True((int)rogAlly.UpscaleQuality <= (int)steamDeck.UpscaleQuality);
        
        _output.WriteLine($"Steam Deck: {steamDeck.LensingMaxSteps} steps, {steamDeck.UpscaleQuality}");
        _output.WriteLine($"ROG Ally: {rogAlly.LensingMaxSteps} steps, {rogAlly.UpscaleQuality}");
    }

    [Fact]
    public void AllHandhelds_UseFSR2()
    {
        Assert.Equal(UpscaleMethod.FSR2, PerformanceProfiles.SteamDeck.UpscaleMethod);
        Assert.Equal(UpscaleMethod.FSR2, PerformanceProfiles.ROGAlly.UpscaleMethod);
        Assert.Equal(UpscaleMethod.FSR2, PerformanceProfiles.Handheld.UpscaleMethod);
    }

    [Fact]
    public void AllHandhelds_DisableExpensiveEffects()
    {
        var handhelds = new[]
        {
            PerformanceProfiles.SteamDeck,
            PerformanceProfiles.ROGAlly,
            PerformanceProfiles.Handheld
        };

        foreach (var profile in handhelds)
        {
            Assert.False(profile.EnableVolumetrics);
            Assert.False(profile.EnableGodRays);
            _output.WriteLine($"{profile.Name}: Volumetrics={profile.EnableVolumetrics}, GodRays={profile.EnableGodRays}");
        }
    }

    #endregion

    #region Apple Silicon Profiles

    [Fact]
    public void AppleSilicon_NoUpscaling()
    {
        // Apple Silicon doesn't have FSR2/DLSS, uses native resolution
        Assert.Equal(UpscaleMethod.None, PerformanceProfiles.AppleSiliconM1.UpscaleMethod);
        Assert.Equal(UpscaleMethod.None, PerformanceProfiles.AppleSiliconM2.UpscaleMethod);
        Assert.Equal(UpscaleMethod.None, PerformanceProfiles.AppleSiliconM3.UpscaleMethod);
    }

    [Fact]
    public void AppleSilicon_IncreasingQuality()
    {
        var m1 = PerformanceProfiles.AppleSiliconM1;
        var m2 = PerformanceProfiles.AppleSiliconM2;
        var m3 = PerformanceProfiles.AppleSiliconM3;
        
        // Each generation should allow more steps
        Assert.True(m1.LensingMaxSteps <= m2.LensingMaxSteps);
        Assert.True(m2.LensingMaxSteps <= m3.LensingMaxSteps);
        
        _output.WriteLine($"M1: {m1.LensingMaxSteps} steps");
        _output.WriteLine($"M2: {m2.LensingMaxSteps} steps");
        _output.WriteLine($"M3: {m3.LensingMaxSteps} steps");
    }

    #endregion

    #region Gaming Laptop Profiles

    [Fact]
    public void GamingLaptops_UseDLSS()
    {
        // Gaming laptops typically have NVIDIA GPUs
        Assert.Equal(UpscaleMethod.DLSS, PerformanceProfiles.GamingLaptopLowEnd.UpscaleMethod);
        Assert.Equal(UpscaleMethod.DLSS, PerformanceProfiles.GamingLaptopHighEnd.UpscaleMethod);
    }

    [Fact]
    public void GamingLaptopHighEnd_IsBetterThanLowEnd()
    {
        var lowEnd = PerformanceProfiles.GamingLaptopLowEnd;
        var highEnd = PerformanceProfiles.GamingLaptopHighEnd;
        
        Assert.True(highEnd.LensingMaxSteps > lowEnd.LensingMaxSteps);
        Assert.True(highEnd.MaxBounces >= lowEnd.MaxBounces);
        
        _output.WriteLine($"Low-End Laptop: {lowEnd.LensingMaxSteps} steps, {lowEnd.MaxBounces} bounces");
        _output.WriteLine($"High-End Laptop: {highEnd.LensingMaxSteps} steps, {highEnd.MaxBounces} bounces");
    }

    #endregion

    #region FSR2 Integration Verification

    [Fact]
    public void SteamDeckProfile_FSR2_ProvidesMeaningfulUpscaling()
    {
        var profile = PerformanceProfiles.SteamDeck;
        var fsr2 = new FSR2Upscaler();
        
        // Steam Deck native: 1280x800
        // With Performance mode: should be 640x400
        var (renderWidth, renderHeight) = fsr2.GetRenderResolution(1280, 800, profile.UpscaleQuality);
        
        Assert.Equal(640, renderWidth);
        Assert.Equal(400, renderHeight);
        
        // This means 4x fewer pixels to raytrace
        float pixelReduction = 1.0f - ((float)(renderWidth * renderHeight) / (1280 * 800));
        Assert.InRange(pixelReduction, 0.74f, 0.76f); // 75% reduction
        
        _output.WriteLine($"Steam Deck FSR2 Performance mode:");
        _output.WriteLine($"  Native: 1280x800 = 1,024,000 pixels");
        _output.WriteLine($"  Render: {renderWidth}x{renderHeight} = {renderWidth * renderHeight:N0} pixels");
        _output.WriteLine($"  Reduction: {pixelReduction:P0}");
    }

    [Fact]
    public void HighQualityProfile_NoUpscaling()
    {
        var profile = PerformanceProfiles.HighQuality;
        
        Assert.Equal(UpscaleMethod.None, profile.UpscaleMethod);
        
        _output.WriteLine("High Quality profile uses native resolution (no upscaling)");
    }

    [Fact]
    public void UltraProfile_NoUpscaling()
    {
        var profile = PerformanceProfiles.Ultra;
        
        Assert.Equal(UpscaleMethod.None, profile.UpscaleMethod);
        Assert.Equal(UpscaleQuality.Native, profile.UpscaleQuality);
        
        _output.WriteLine("Ultra profile uses native resolution for maximum quality");
    }

    #endregion
}
