namespace RedHoleEngine.Rendering;

public enum RaytracerQualityPreset
{
    Fast,
    Balanced,
    Quality,
    Custom
}

public readonly struct RaytracerPresetValues
{
    public readonly int RaysPerPixel;
    public readonly int MaxBounces;
    public readonly int SamplesPerFrame;
    public readonly bool Accumulate;
    public readonly bool Denoise;

    public RaytracerPresetValues(int raysPerPixel, int maxBounces, int samplesPerFrame, bool accumulate, bool denoise)
    {
        RaysPerPixel = raysPerPixel;
        MaxBounces = maxBounces;
        SamplesPerFrame = samplesPerFrame;
        Accumulate = accumulate;
        Denoise = denoise;
    }
}

public static class RaytracerPresetUtilities
{
    public static RaytracerPresetValues GetPresetValues(RaytracerQualityPreset preset)
    {
        return preset switch
        {
            RaytracerQualityPreset.Fast => new RaytracerPresetValues(1, 1, 1, false, false),
            RaytracerQualityPreset.Quality => new RaytracerPresetValues(4, 4, 2, true, true),
            RaytracerQualityPreset.Balanced => new RaytracerPresetValues(2, 2, 1, true, false),
            _ => new RaytracerPresetValues(2, 2, 1, true, false)
        };
    }
}
