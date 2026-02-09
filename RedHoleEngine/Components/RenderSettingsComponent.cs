using RedHoleEngine.Core.ECS;
using RedHoleEngine.Rendering;

namespace RedHoleEngine.Components;

/// <summary>
/// Per-scene render settings override.
/// Attach to a single entity to drive renderer quality.
/// </summary>
public struct RenderSettingsComponent : IComponent
{
    public bool Enabled;
    public RenderMode Mode;
    public int RaysPerPixel;
    public int MaxBounces;
    public int SamplesPerFrame;
    public bool Accumulate;
    public bool Denoise;
    public bool ResetAccumulation;
    public RaytracerQualityPreset Preset;
    public int MaxRaysPerPixelLimit;
    public int MaxBouncesLimit;
    public int MaxSamplesPerFrameLimit;

    public RenderSettingsComponent(RenderMode mode = RenderMode.Raytraced)
    {
        Enabled = true;
        Mode = mode;
        RaysPerPixel = 2;
        MaxBounces = 2;
        SamplesPerFrame = 1;
        Accumulate = true;
        Denoise = false;
        ResetAccumulation = false;
        Preset = RaytracerQualityPreset.Balanced;
        MaxRaysPerPixelLimit = 64;
        MaxBouncesLimit = 8;
        MaxSamplesPerFrameLimit = 8;
    }
}
