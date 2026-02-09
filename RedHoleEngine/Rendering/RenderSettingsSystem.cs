using RedHoleEngine.Components;
using RedHoleEngine.Core.ECS;

namespace RedHoleEngine.Rendering;

/// <summary>
/// Applies per-scene render settings to the active graphics backend.
/// </summary>
public class RenderSettingsSystem : GameSystem
{
    private IGraphicsBackend? _backend;

    public override int Priority => -52;

    public void SetBackend(IGraphicsBackend backend)
    {
        _backend = backend;
    }

    public override void Update(float deltaTime)
    {
        if (_backend == null)
            return;

        foreach (var entity in World.Query<RenderSettingsComponent>())
        {
            ref var settings = ref World.GetComponent<RenderSettingsComponent>(entity);
            if (!settings.Enabled)
                continue;

            _backend.RenderSettings.Mode = settings.Mode;

            if (settings.Preset != RaytracerQualityPreset.Custom)
            {
                var preset = RaytracerPresetUtilities.GetPresetValues(settings.Preset);
                settings.RaysPerPixel = preset.RaysPerPixel;
                settings.MaxBounces = preset.MaxBounces;
                settings.SamplesPerFrame = preset.SamplesPerFrame;
                settings.Accumulate = preset.Accumulate;
                settings.Denoise = preset.Denoise;
            }

            _backend.RaytracerSettings.MaxRaysPerPixelLimit = settings.MaxRaysPerPixelLimit;
            _backend.RaytracerSettings.MaxBouncesLimit = settings.MaxBouncesLimit;
            _backend.RaytracerSettings.MaxSamplesPerFrameLimit = settings.MaxSamplesPerFrameLimit;
            _backend.RaytracerSettings.RaysPerPixel = settings.RaysPerPixel;
            _backend.RaytracerSettings.MaxBounces = settings.MaxBounces;
            _backend.RaytracerSettings.SamplesPerFrame = settings.SamplesPerFrame;
            _backend.RaytracerSettings.Accumulate = settings.Accumulate;
            _backend.RaytracerSettings.Denoise = settings.Denoise;
            
            // Apply lensing quality settings
            _backend.RaytracerSettings.LensingQuality = settings.LensingQuality;
            _backend.RaytracerSettings.LensingMaxSteps = settings.LensingMaxSteps;
            _backend.RaytracerSettings.LensingStepSize = settings.LensingStepSize;
            _backend.RaytracerSettings.LensingBvhCheckInterval = settings.LensingBvhCheckInterval;
            _backend.RaytracerSettings.LensingMaxDistance = settings.LensingMaxDistance;
            
            if (settings.ResetAccumulation)
            {
                _backend.RaytracerSettings.ResetAccumulation = true;
                settings.ResetAccumulation = false;
            }
            _backend.RaytracerSettings.Clamp();
            break;
        }
    }
}
