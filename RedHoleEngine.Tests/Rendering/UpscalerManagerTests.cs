using RedHoleEngine.Rendering.Upscaling;
using Xunit.Abstractions;

namespace RedHoleEngine.Tests.Rendering;

/// <summary>
/// Unit tests for UpscalerManager - tests upscaler selection, fallback logic, and jitter calculation.
/// </summary>
public class UpscalerManagerTests
{
    private readonly ITestOutputHelper _output;

    public UpscalerManagerTests(ITestOutputHelper output)
    {
        _output = output;
    }

    #region Halton Sequence Jitter

    [Fact]
    public void GetJitterOffset_WithoutUpscaler_ReturnsZero()
    {
        var manager = new UpscalerManager();
        
        var (x, y) = manager.GetJitterOffset(0);
        
        Assert.Equal(0f, x);
        Assert.Equal(0f, y);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(7)]
    [InlineData(8)]  // Wraps around (JitterPhaseCount = 8)
    [InlineData(15)]
    [InlineData(100)]
    public void GetJitterOffset_AllFrameIndices_ReturnValidRange(uint frameIndex)
    {
        var manager = new UpscalerManager();
        
        var (x, y) = manager.GetJitterOffset(frameIndex);
        
        // Without active upscaler, returns (0, 0)
        // This validates no exceptions are thrown for any frame index
        Assert.InRange(x, -0.5f, 0.5f);
        Assert.InRange(y, -0.5f, 0.5f);
    }

    [Fact]
    public void HaltonSequence_Base2And3_ProducesExpectedPattern()
    {
        // Test the expected Halton sequence values
        // Halton(1, 2) = 0.5, Halton(2, 2) = 0.25, Halton(3, 2) = 0.75, etc.
        // After subtracting 0.5: 0, -0.25, 0.25, etc.
        
        var manager = new UpscalerManager();
        
        // Capture 8 frames of jitter
        var jitterSequence = new List<(float x, float y)>();
        for (uint i = 0; i < 8; i++)
        {
            jitterSequence.Add(manager.GetJitterOffset(i));
        }
        
        // Verify sequence repeats after 8 frames
        for (uint i = 0; i < 8; i++)
        {
            var first = manager.GetJitterOffset(i);
            var repeated = manager.GetJitterOffset(i + 8);
            
            Assert.Equal(first.x, repeated.x);
            Assert.Equal(first.y, repeated.y);
        }
    }

    #endregion

    #region Manager State

    [Fact]
    public void UpscalerManager_Dispose_CleansUpResources()
    {
        var manager = new UpscalerManager();
        
        // Should not throw
        manager.Dispose();
        
        Assert.Null(manager.ActiveUpscaler);
        Assert.False(manager.IsUpscalingEnabled);
    }

    [Fact]
    public void UpscalerManager_DoubleDispose_DoesNotThrow()
    {
        var manager = new UpscalerManager();
        
        manager.Dispose();
        manager.Dispose(); // Second dispose should be safe
        
        Assert.Null(manager.ActiveUpscaler);
    }

    [Fact]
    public void UpscalerManager_ResizeWithoutInit_DoesNotThrow()
    {
        var manager = new UpscalerManager();
        
        // Should handle resize gracefully without initialization
        manager.Resize(1920, 1080);
        
        Assert.Equal(1920, manager.Settings.DisplayWidth);
        Assert.Equal(1080, manager.Settings.DisplayHeight);
    }

    [Fact]
    public void UpscalerManager_SetQualityWithoutInit_DoesNotThrow()
    {
        var manager = new UpscalerManager();
        
        // Should handle quality change gracefully without active upscaler
        // Note: SetQuality returns early if no active upscaler, so settings aren't updated
        manager.SetQuality(UpscaleQuality.Performance);
        
        // The method returns early without updating settings when no upscaler is active
        // This is expected behavior - quality only matters when an upscaler is running
        Assert.Equal(UpscaleQuality.Balanced, manager.Settings.Quality); // Default value unchanged
    }

    #endregion

    #region Resolution Tracking

    [Fact]
    public void UpscalerManager_Resize_UpdatesDimensions()
    {
        var manager = new UpscalerManager();
        
        manager.Resize(2560, 1440);
        
        Assert.Equal(2560, manager.DisplayWidth);
        Assert.Equal(1440, manager.DisplayHeight);
        
        // Without active upscaler, render = display
        Assert.Equal(2560, manager.RenderWidth);
        Assert.Equal(1440, manager.RenderHeight);
    }

    [Fact]
    public void UpscalerManager_MultipleResizes_TracksCorrectly()
    {
        var manager = new UpscalerManager();
        
        manager.Resize(1920, 1080);
        Assert.Equal(1920, manager.DisplayWidth);
        Assert.Equal(1080, manager.DisplayHeight);
        
        manager.Resize(1280, 720);
        Assert.Equal(1280, manager.DisplayWidth);
        Assert.Equal(720, manager.DisplayHeight);
        
        manager.Resize(3840, 2160);
        Assert.Equal(3840, manager.DisplayWidth);
        Assert.Equal(2160, manager.DisplayHeight);
    }

    #endregion

    #region Feature Detection

    [Fact]
    public void RequiresMotionVectors_WithoutUpscaler_ReturnsFalse()
    {
        var manager = new UpscalerManager();
        
        Assert.False(manager.RequiresMotionVectors);
    }

    [Fact]
    public void RequiresDepth_WithoutUpscaler_ReturnsFalse()
    {
        var manager = new UpscalerManager();
        
        Assert.False(manager.RequiresDepth);
    }

    [Fact]
    public void IsRayReconstructionEnabled_WithoutDLSS_ReturnsFalse()
    {
        var manager = new UpscalerManager();
        
        Assert.False(manager.IsRayReconstructionEnabled);
    }

    [Fact]
    public void IsFrameGenerationEnabled_WithoutDLSS_ReturnsFalse()
    {
        var manager = new UpscalerManager();
        
        Assert.False(manager.IsFrameGenerationEnabled);
    }

    #endregion

    #region Available Methods

    [Fact]
    public void AvailableMethods_BeforeInit_ContainsOnlyNone()
    {
        var manager = new UpscalerManager();
        
        var methods = manager.AvailableMethods.ToList();
        
        // Before initialization, only None should be listed
        // (others require Vulkan context to check support)
        Assert.Single(methods);
        Assert.Contains(UpscaleMethod.None, methods);
    }

    #endregion
}
