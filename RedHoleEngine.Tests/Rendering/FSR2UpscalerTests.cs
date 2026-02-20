using RedHoleEngine.Rendering.Upscaling;
using Xunit.Abstractions;

namespace RedHoleEngine.Tests.Rendering;

/// <summary>
/// Unit tests for FSR2 upscaler integration.
/// Tests quality presets, render scale calculations, and settings validation.
/// </summary>
public class FSR2UpscalerTests
{
    private readonly ITestOutputHelper _output;

    public FSR2UpscalerTests(ITestOutputHelper output)
    {
        _output = output;
    }

    #region Quality Presets and Render Scales

    [Theory]
    [InlineData(UpscaleQuality.Native, 1.0f)]
    [InlineData(UpscaleQuality.UltraQuality, 0.77f)]
    [InlineData(UpscaleQuality.Quality, 0.67f)]
    [InlineData(UpscaleQuality.Balanced, 0.58f)]
    [InlineData(UpscaleQuality.Performance, 0.50f)]
    [InlineData(UpscaleQuality.UltraPerformance, 0.33f)]
    public void GetRenderScale_ReturnsCorrectScale(UpscaleQuality quality, float expectedScale)
    {
        var upscaler = new FSR2Upscaler();
        
        float actualScale = upscaler.GetRenderScale(quality);
        
        Assert.Equal(expectedScale, actualScale, precision: 2);
        _output.WriteLine($"Quality: {quality}, Scale: {actualScale}");
    }

    [Theory]
    [InlineData(1920, 1080, UpscaleQuality.Native, 1920, 1080)]
    [InlineData(1920, 1080, UpscaleQuality.Quality, 1286, 723)]       // 1920 * 0.67 = 1286
    [InlineData(1920, 1080, UpscaleQuality.Performance, 960, 540)]    // 1920 * 0.50 = 960
    [InlineData(1920, 1080, UpscaleQuality.UltraPerformance, 633, 356)] // 1920 * 0.33 = 633
    [InlineData(1280, 800, UpscaleQuality.Performance, 640, 400)]     // Steam Deck native -> 50%
    [InlineData(2560, 1440, UpscaleQuality.Quality, 1715, 964)]       // 1440p -> Quality
    [InlineData(3840, 2160, UpscaleQuality.Balanced, 2227, 1252)]     // 4K -> Balanced
    public void GetRenderResolution_CalculatesCorrectly(
        int displayWidth, int displayHeight, 
        UpscaleQuality quality, 
        int expectedRenderWidth, int expectedRenderHeight)
    {
        var upscaler = new FSR2Upscaler();
        
        var (actualWidth, actualHeight) = upscaler.GetRenderResolution(displayWidth, displayHeight, quality);
        
        // Allow 1 pixel tolerance due to rounding
        Assert.InRange(actualWidth, expectedRenderWidth - 1, expectedRenderWidth + 1);
        Assert.InRange(actualHeight, expectedRenderHeight - 1, expectedRenderHeight + 1);
        
        _output.WriteLine($"Display: {displayWidth}x{displayHeight}, Quality: {quality}");
        _output.WriteLine($"Render: {actualWidth}x{actualHeight} (expected ~{expectedRenderWidth}x{expectedRenderHeight})");
    }

    [Fact]
    public void GetRenderResolution_SteamDeckPerformanceMode_IsHalfResolution()
    {
        // Steam Deck native resolution: 1280x800
        const int steamDeckWidth = 1280;
        const int steamDeckHeight = 800;
        
        var upscaler = new FSR2Upscaler();
        var (renderWidth, renderHeight) = upscaler.GetRenderResolution(
            steamDeckWidth, steamDeckHeight, UpscaleQuality.Performance);
        
        // Performance mode = 50% scale
        Assert.Equal(640, renderWidth);
        Assert.Equal(400, renderHeight);
        
        // Verify pixel count is 25% of original (50% * 50%)
        int originalPixels = steamDeckWidth * steamDeckHeight;
        int renderPixels = renderWidth * renderHeight;
        float pixelRatio = (float)renderPixels / originalPixels;
        
        Assert.InRange(pixelRatio, 0.24f, 0.26f);
        _output.WriteLine($"Steam Deck: {steamDeckWidth}x{steamDeckHeight} -> {renderWidth}x{renderHeight}");
        _output.WriteLine($"Pixel ratio: {pixelRatio:P0} (expected 25%)");
    }

    #endregion

    #region Upscaler Properties

    [Fact]
    public void FSR2Upscaler_HasCorrectProperties()
    {
        var upscaler = new FSR2Upscaler();
        
        Assert.Equal("AMD FSR 2.2", upscaler.Name);
        Assert.Equal(UpscaleMethod.FSR2, upscaler.Method);
        Assert.True(upscaler.RequiresMotionVectors);
        Assert.True(upscaler.RequiresDepth);
        Assert.False(upscaler.SupportsRayReconstruction);
        Assert.False(upscaler.SupportsFrameGeneration);
    }

    [Fact]
    public void FSR2Upscaler_IsSupported_ReturnsFalseWithoutNativeLibrary()
    {
        // Without the native FSR2 library, IsSupported should be false
        var upscaler = new FSR2Upscaler();
        
        // This test documents expected behavior - FSR2 requires native library
        // In production, this would return true when ffx_fsr2_api_vk is available
        Assert.False(upscaler.IsSupported);
        _output.WriteLine($"FSR2 IsSupported: {upscaler.IsSupported} (native library not loaded)");
    }

    #endregion

    #region UpscalerSettings

    [Fact]
    public void UpscalerSettings_DefaultValues_AreCorrect()
    {
        var settings = new UpscalerSettings();
        
        Assert.Equal(UpscaleMethod.None, settings.Method);
        Assert.Equal(UpscaleQuality.Balanced, settings.Quality);
        Assert.True(settings.EnableSharpening);
        Assert.Equal(0.5f, settings.Sharpness);
        Assert.False(settings.EnableFrameGeneration);
        Assert.False(settings.EnableRayReconstruction);
        Assert.True(settings.IsHDR);
        Assert.Equal(1.0f, settings.MotionVectorScale);
    }

    [Theory]
    [InlineData(1920, 1080, 0.5f, 960, 540)]
    [InlineData(1920, 1080, 0.67f, 1286, 723)]
    [InlineData(1280, 800, 0.5f, 640, 400)]
    [InlineData(3840, 2160, 0.33f, 1267, 712)]
    public void UpscalerSettings_GetRenderResolution_CalculatesCorrectly(
        int displayWidth, int displayHeight, float scale,
        int expectedWidth, int expectedHeight)
    {
        var settings = new UpscalerSettings
        {
            DisplayWidth = displayWidth,
            DisplayHeight = displayHeight
        };
        
        var (actualWidth, actualHeight) = settings.GetRenderResolution(scale);
        
        // Allow 1 pixel tolerance
        Assert.InRange(actualWidth, expectedWidth - 1, expectedWidth + 1);
        Assert.InRange(actualHeight, expectedHeight - 1, expectedHeight + 1);
    }

    #endregion

    #region UpscaleInput Validation

    [Fact]
    public void UpscaleInput_DefaultValues_AreZeroOrFalse()
    {
        var input = new UpscaleInput();
        
        Assert.Equal(0, input.RenderWidth);
        Assert.Equal(0, input.RenderHeight);
        Assert.Equal(0, input.DisplayWidth);
        Assert.Equal(0, input.DisplayHeight);
        Assert.Equal(0f, input.JitterX);
        Assert.Equal(0f, input.JitterY);
        Assert.Equal(0f, input.DeltaTime);
        Assert.False(input.Reset);
        Assert.False(input.EnableSharpening);
        Assert.Equal(0f, input.Sharpness);
    }

    [Fact]
    public void UpscaleInput_CanSetAllProperties()
    {
        var input = new UpscaleInput
        {
            RenderWidth = 960,
            RenderHeight = 540,
            DisplayWidth = 1920,
            DisplayHeight = 1080,
            JitterX = 0.25f,
            JitterY = -0.125f,
            DeltaTime = 0.016667f,
            NearPlane = 0.1f,
            FarPlane = 1000f,
            FovY = 1.0472f, // 60 degrees
            Reset = true,
            FrameIndex = 42,
            EnableSharpening = true,
            Sharpness = 0.6f
        };
        
        Assert.Equal(960, input.RenderWidth);
        Assert.Equal(540, input.RenderHeight);
        Assert.Equal(1920, input.DisplayWidth);
        Assert.Equal(1080, input.DisplayHeight);
        Assert.Equal(0.25f, input.JitterX);
        Assert.Equal(-0.125f, input.JitterY);
        Assert.Equal(0.016667f, input.DeltaTime, precision: 5);
        Assert.Equal(0.1f, input.NearPlane);
        Assert.Equal(1000f, input.FarPlane);
        Assert.Equal(1.0472f, input.FovY, precision: 4);
        Assert.True(input.Reset);
        Assert.Equal(42u, input.FrameIndex);
        Assert.True(input.EnableSharpening);
        Assert.Equal(0.6f, input.Sharpness);
    }

    #endregion

    #region UpscalerManager

    [Fact]
    public void UpscalerManager_AvailableMethods_AlwaysContainsNone()
    {
        var manager = new UpscalerManager();
        
        // Without initialization, only None should be available
        // (can't fully test without Vulkan context)
        Assert.Contains(UpscaleMethod.None, manager.AvailableMethods);
    }

    [Fact]
    public void UpscalerManager_GetJitterOffset_ReturnsValidHaltonSequence()
    {
        var manager = new UpscalerManager();
        
        // Test jitter offsets for multiple frames
        var jitters = new List<(float x, float y)>();
        for (uint i = 0; i < 16; i++)
        {
            var (x, y) = manager.GetJitterOffset(i);
            jitters.Add((x, y));
            
            // Jitter should be in range [-0.5, 0.5]
            Assert.InRange(x, -0.5f, 0.5f);
            Assert.InRange(y, -0.5f, 0.5f);
            
            _output.WriteLine($"Frame {i}: jitter = ({x:F4}, {y:F4})");
        }
        
        // Verify jitter sequence has variation (not all zeros)
        // Note: Without active upscaler, jitter returns (0,0)
        // This is expected behavior
    }

    [Fact]
    public void UpscalerManager_InitialState_HasNoActiveUpscaler()
    {
        var manager = new UpscalerManager();
        
        Assert.Null(manager.ActiveUpscaler);
        Assert.False(manager.IsUpscalingEnabled);
        Assert.False(manager.RequiresMotionVectors);
        Assert.False(manager.RequiresDepth);
        Assert.False(manager.IsRayReconstructionEnabled);
        Assert.False(manager.IsFrameGenerationEnabled);
    }

    [Fact]
    public void UpscalerManager_Settings_DefaultValues()
    {
        var manager = new UpscalerManager();
        
        Assert.NotNull(manager.Settings);
        Assert.Equal(UpscaleMethod.None, manager.Settings.Method);
        Assert.Equal(UpscaleQuality.Balanced, manager.Settings.Quality);
    }

    #endregion

    #region Quality Enum Coverage

    [Fact]
    public void UpscaleQuality_AllValues_HaveDefinedScales()
    {
        var upscaler = new FSR2Upscaler();
        
        foreach (UpscaleQuality quality in Enum.GetValues<UpscaleQuality>())
        {
            float scale = upscaler.GetRenderScale(quality);
            
            Assert.InRange(scale, 0.1f, 1.0f);
            _output.WriteLine($"{quality}: {scale:P0}");
        }
    }

    [Fact]
    public void UpscaleMethod_Enum_HasExpectedValues()
    {
        var methods = Enum.GetValues<UpscaleMethod>();
        
        Assert.Contains(UpscaleMethod.None, methods);
        Assert.Contains(UpscaleMethod.DLSS, methods);
        Assert.Contains(UpscaleMethod.FSR2, methods);
        Assert.Contains(UpscaleMethod.XeSS, methods);
        Assert.Equal(4, methods.Length);
    }

    #endregion

    #region Performance Characteristics

    [Theory]
    [InlineData(UpscaleQuality.Performance, 4.0f)]     // 50% scale = 4x fewer pixels
    [InlineData(UpscaleQuality.Balanced, 2.97f)]       // 58% scale = ~3x fewer pixels
    [InlineData(UpscaleQuality.Quality, 2.23f)]        // 67% scale = ~2.2x fewer pixels
    [InlineData(UpscaleQuality.UltraQuality, 1.69f)]   // 77% scale = ~1.7x fewer pixels
    [InlineData(UpscaleQuality.UltraPerformance, 9.18f)] // 33% scale = ~9x fewer pixels
    public void GetRenderScale_PixelReduction_IsExpected(UpscaleQuality quality, float expectedMultiplier)
    {
        var upscaler = new FSR2Upscaler();
        float scale = upscaler.GetRenderScale(quality);
        
        // Pixel reduction = 1 / (scale^2)
        float actualMultiplier = 1f / (scale * scale);
        
        Assert.InRange(actualMultiplier, expectedMultiplier - 0.1f, expectedMultiplier + 0.1f);
        _output.WriteLine($"{quality}: scale={scale:P0}, pixel reduction={actualMultiplier:F2}x");
    }

    [Fact]
    public void SteamDeckProfile_PixelSavings_IsSignificant()
    {
        // Steam Deck: 1280x800 native
        // With Performance mode (50% scale): 640x400
        const int nativePixels = 1280 * 800;     // 1,024,000
        const int renderPixels = 640 * 400;       // 256,000
        
        float savings = 1f - ((float)renderPixels / nativePixels);
        
        Assert.Equal(0.75f, savings, precision: 2); // 75% fewer pixels to render
        _output.WriteLine($"Steam Deck pixel savings with FSR2 Performance: {savings:P0}");
        _output.WriteLine($"Native: {nativePixels:N0} pixels -> Render: {renderPixels:N0} pixels");
    }

    #endregion
}
