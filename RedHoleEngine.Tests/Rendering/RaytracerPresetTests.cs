using RedHoleEngine.Rendering;

namespace RedHoleEngine.Tests.Rendering;

public class RaytracerPresetTests
{
    [Theory]
    [InlineData(RaytracerQualityPreset.Fast, 1, 1, 1, false, false)]
    [InlineData(RaytracerQualityPreset.Balanced, 2, 2, 1, true, false)]
    [InlineData(RaytracerQualityPreset.Quality, 4, 4, 2, true, true)]
    [InlineData(RaytracerQualityPreset.Custom, 2, 2, 1, true, false)]
    public void Presets_ExposeDocumentedSampleCounts(
        RaytracerQualityPreset preset, int rays, int bounces, int samples, bool accumulate, bool denoise)
    {
        var values = RaytracerPresetUtilities.GetPresetValues(preset);
        Assert.Equal(rays, values.RaysPerPixel);
        Assert.Equal(bounces, values.MaxBounces);
        Assert.Equal(samples, values.SamplesPerFrame);
        Assert.Equal(accumulate, values.Accumulate);
        Assert.Equal(denoise, values.Denoise);
    }
}
