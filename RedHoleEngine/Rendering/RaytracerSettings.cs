namespace RedHoleEngine.Rendering;

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
}
