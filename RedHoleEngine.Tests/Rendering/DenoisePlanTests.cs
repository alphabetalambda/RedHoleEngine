using System.Runtime.InteropServices;
using RedHoleEngine.Rendering;
using RedHoleEngine.Rendering.Denoising;

namespace RedHoleEngine.Tests.Rendering;

public class DenoisePlanTests
{
    [Fact]
    public void DenoiseOff_RunsNothing()
    {
        var settings = new RaytracerSettings { Denoise = false, DenoiseMethod = DenoiseMethod.SVGF, DenoiseUseHistory = true };
        var plan = DenoisePlan.Create(settings, gpuDenoiserAvailable: true);

        Assert.Equal(DenoisePlan.Off, plan);
        Assert.False(plan.RunsGpuPasses);
        Assert.False(plan.NeedsMotionVectors);
    }

    [Fact]
    public void MethodNone_RunsNothing()
    {
        var settings = new RaytracerSettings { Denoise = true, DenoiseMethod = DenoiseMethod.None };
        Assert.Equal(DenoisePlan.Off, DenoisePlan.Create(settings, true));
    }

    [Fact]
    public void Bilateral_UsesInShaderFilterOnly()
    {
        var settings = new RaytracerSettings { Denoise = true, DenoiseMethod = DenoiseMethod.Bilateral, DenoiseUseHistory = true };
        var plan = DenoisePlan.Create(settings, true);

        Assert.True(plan.InShaderBilateral);
        Assert.False(plan.RunsGpuPasses);
        Assert.False(plan.RunTemporal);
    }

    [Fact]
    public void DefaultDenoiseMethod_IsGpuATrous()
    {
        var settings = new RaytracerSettings { Denoise = true };
        var plan = DenoisePlan.Create(settings, true);

        Assert.Equal(DenoiseSpatialFilter.ATrous, plan.SpatialFilter);
        Assert.False(plan.InShaderBilateral);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ATrous_TemporalFollowsHistoryToggle(bool useHistory)
    {
        var settings = new RaytracerSettings
        {
            Denoise = true,
            DenoiseMethod = DenoiseMethod.ATrous,
            DenoiseUseHistory = useHistory,
            ATrousIterations = 4
        };
        var plan = DenoisePlan.Create(settings, true);

        Assert.True(plan.RunsGpuPasses);
        Assert.False(plan.InShaderBilateral);
        Assert.Equal(DenoiseSpatialFilter.ATrous, plan.SpatialFilter);
        Assert.Equal(4, plan.SpatialIterations);
        Assert.Equal(useHistory, plan.RunTemporal);
        Assert.Equal(useHistory, plan.UseHistory);
        Assert.Equal(useHistory, plan.NeedsMotionVectors);
        Assert.False(plan.RunVariance);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Svgf_AlwaysRunsTemporalAndVariance(bool useHistory)
    {
        var settings = new RaytracerSettings
        {
            Denoise = true,
            DenoiseMethod = DenoiseMethod.SVGF,
            DenoiseUseHistory = useHistory,
            SVGFSpatialIterations = 3
        };
        var plan = DenoisePlan.Create(settings, true);

        Assert.True(plan.RunTemporal);
        Assert.True(plan.RunVariance);
        Assert.Equal(DenoiseSpatialFilter.SVGF, plan.SpatialFilter);
        Assert.Equal(3, plan.SpatialIterations);
        Assert.Equal(useHistory, plan.UseHistory);
        Assert.Equal(useHistory, plan.NeedsMotionVectors);
    }

    [Theory]
    [InlineData(DenoiseMethod.ATrous)]
    [InlineData(DenoiseMethod.SVGF)]
    public void GpuMethods_FallBackToBilateralWithoutPipelines(DenoiseMethod method)
    {
        var settings = new RaytracerSettings { Denoise = true, DenoiseMethod = method, DenoiseUseHistory = true };
        var plan = DenoisePlan.Create(settings, gpuDenoiserAvailable: false);

        Assert.True(plan.InShaderBilateral);
        Assert.False(plan.RunsGpuPasses);
        Assert.False(plan.NeedsMotionVectors);
    }

    [Fact]
    public void Iterations_AreClampedToSupportedRange()
    {
        var settings = new RaytracerSettings { Denoise = true, DenoiseMethod = DenoiseMethod.ATrous, ATrousIterations = 42 };
        Assert.Equal(6, DenoisePlan.Create(settings, true).SpatialIterations);

        settings.ATrousIterations = 0;
        Assert.Equal(1, DenoisePlan.Create(settings, true).SpatialIterations);
    }

    [Theory]
    [InlineData(DenoiseImageSlot.RaytracerOutput, 1)]
    [InlineData(DenoiseImageSlot.RaytracerOutput, 2)]
    [InlineData(DenoiseImageSlot.RaytracerOutput, 5)]
    [InlineData(DenoiseImageSlot.HistoryA, 1)]
    [InlineData(DenoiseImageSlot.HistoryB, 6)]
    public void SpatialChain_IsValidPingPong(DenoiseImageSlot first, int iterations)
    {
        var steps = DenoisePlan.BuildSpatialChain(first, iterations);

        Assert.Equal(iterations, steps.Length);
        Assert.Equal(first, steps[0].Source);
        Assert.Equal(DenoiseImageSlot.Final, steps[^1].Destination);

        for (int i = 0; i < steps.Length; i++)
        {
            Assert.Equal(i, steps[i].Iteration);
            Assert.NotEqual(steps[i].Source, steps[i].Destination);
            if (i > 0)
                Assert.Equal(steps[i - 1].Destination, steps[i].Source);
            if (i < steps.Length - 1)
                Assert.Contains(steps[i].Destination, new[] { DenoiseImageSlot.Ping, DenoiseImageSlot.Pong });
        }
    }

    [Fact]
    public void SpatialChain_AlternatesPingAndPong()
    {
        var steps = DenoisePlan.BuildSpatialChain(DenoiseImageSlot.RaytracerOutput, 4);

        Assert.Equal(
            new[]
            {
                new DenoisePassStep(DenoiseImageSlot.RaytracerOutput, DenoiseImageSlot.Ping, 0),
                new DenoisePassStep(DenoiseImageSlot.Ping, DenoiseImageSlot.Pong, 1),
                new DenoisePassStep(DenoiseImageSlot.Pong, DenoiseImageSlot.Ping, 2),
                new DenoisePassStep(DenoiseImageSlot.Ping, DenoiseImageSlot.Final, 3)
            },
            steps);
    }

    [Fact]
    public void SpatialChain_HandlesEdgeCases()
    {
        Assert.Empty(DenoisePlan.BuildSpatialChain(DenoiseImageSlot.RaytracerOutput, 0));
        Assert.Throws<ArgumentException>(() => DenoisePlan.BuildSpatialChain(DenoiseImageSlot.Final, 2));
    }

    [Fact]
    public void PushConstantStructs_MatchShaderLayouts()
    {
        // Sizes/offsets must match the push_constant blocks in the denoise_*.comp shaders
        Assert.Equal(32, Marshal.SizeOf<ATrousParams>());
        Assert.Equal(8, (int)Marshal.OffsetOf<ATrousParams>(nameof(ATrousParams.StepSize)));
        Assert.Equal(20, (int)Marshal.OffsetOf<ATrousParams>(nameof(ATrousParams.DepthSigma)));

        Assert.Equal(32, Marshal.SizeOf<SVGFTemporalParams>());
        Assert.Equal(20, (int)Marshal.OffsetOf<SVGFTemporalParams>(nameof(SVGFTemporalParams.MaxHistoryLength)));
        Assert.Equal(24, (int)Marshal.OffsetOf<SVGFTemporalParams>(nameof(SVGFTemporalParams.HistoryValid)));

        Assert.Equal(16, Marshal.SizeOf<SVGFVarianceParams>());
        Assert.Equal(32, Marshal.SizeOf<SVGFSpatialParams>());
        Assert.Equal(24, (int)Marshal.OffsetOf<SVGFSpatialParams>(nameof(SVGFSpatialParams.VarianceBoost)));
    }

    [Fact]
    public void TemporalParams_EncodeHistoryValidity()
    {
        var denoiser = new Denoiser(new RaytracerSettings());
        denoiser.Initialize(64, 32);

        Assert.Equal(0, denoiser.GetSVGFTemporalParams().HistoryValid);
        Assert.Equal(1, denoiser.GetSVGFTemporalParams(historyValid: true).HistoryValid);
    }
}
