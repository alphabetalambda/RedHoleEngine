using System.Numerics;
using RedHoleEngine.Rendering;
using RedHoleEngine.Rendering.Denoising;

namespace RedHoleEngine.Tests.Rendering;

public class DenoiserCoverageTests
{
    [Fact]
    public void InitializeResizeAndDispose_TrackResolution()
    {
        var settings = new RaytracerSettings();
        var denoiser = new Denoiser(settings);
        Assert.False(denoiser.IsInitialized);
        Assert.Equal(0, denoiser.Width);

        denoiser.Initialize(1280, 720);
        Assert.True(denoiser.IsInitialized);
        Assert.Equal(1280, denoiser.Width);
        Assert.Equal(720, denoiser.Height);

        denoiser.Resize(1280, 720);
        Assert.Equal(1280, denoiser.Width);
        denoiser.Resize(640, 360);
        Assert.Equal(640, denoiser.Width);
        Assert.Equal(360, denoiser.Height);

        denoiser.ATrousShaderHandle = 4;
        denoiser.Dispose();
        Assert.False(denoiser.IsInitialized);
        denoiser.Dispose();
        Assert.Equal(4u, denoiser.ATrousShaderHandle);
    }

    [Fact]
    public void ParameterBuilders_FollowSettingsAndIterationStep()
    {
        var settings = new RaytracerSettings
        {
            ATrousColorSigma = 0.8f,
            ATrousNormalSigma = 0.2f,
            ATrousDepthSigma = 0.3f,
            SVGFTemporalAlpha = 0.25f,
            SVGFPhiColor = 12f,
            SVGFPhiNormal = 64f
        };
        var denoiser = new Denoiser(settings);
        denoiser.Initialize(100, 50);

        var first = denoiser.GetATrousParams(0);
        var third = denoiser.GetATrousParams(3);
        Assert.Equal(new Vector2(100, 50), first.Resolution);
        Assert.Equal(1, first.StepSize);
        Assert.Equal(8, third.StepSize);
        Assert.Equal(0.8f, first.ColorSigma);
        Assert.Equal(0.2f, third.NormalSigma);
        Assert.Equal(0.3f, third.DepthSigma);

        var temporal = denoiser.GetSVGFTemporalParams();
        Assert.Equal(0.25f, temporal.TemporalAlpha);
        Assert.Equal(0.9f, temporal.NormalThreshold);
        Assert.Equal(0.1f, temporal.DepthThreshold);
        Assert.Equal(32, temporal.MaxHistoryLength);
        Assert.Equal(new Vector2(100, 50), temporal.Resolution);

        var variance = denoiser.GetSVGFVarianceParams();
        Assert.Equal(4, variance.MinHistoryForVariance);
        Assert.Equal(4f, variance.SpatialVarianceBoost);

        var spatial = denoiser.GetSVGFSpatialParams(2);
        Assert.Equal(4, spatial.StepSize);
        Assert.Equal(12f, spatial.PhiColor);
        Assert.Equal(64f, spatial.PhiNormal);
        Assert.Equal(1f, spatial.PhiDepth);
        Assert.Equal(1f, spatial.VarianceBoost);

        settings.ATrousColorSigma = 1.5f;
        Assert.Equal(1.5f, denoiser.GetATrousParams(1).ColorSigma);
        Assert.Equal(2, denoiser.GetATrousParams(1).StepSize);
    }
}
