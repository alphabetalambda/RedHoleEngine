using System;
using ImGuiNET;
using RedHoleEngine.Rendering;

namespace RedHoleEngine.Editor.UI.Panels;

/// <summary>
/// Panel for raytracer quality settings
/// </summary>
public class RaytracerSettingsPanel : EditorPanel
{
    private readonly RaytracerSettings _settings;

    public RaytracerSettingsPanel(RaytracerSettings settings)
    {
        _settings = settings;
    }

    public override string Title => "Raytracer";

    protected override void OnDraw()
    {
        if (_settings == null)
        {
            ImGui.TextDisabled("No raytracer settings available");
            return;
        }

        int raysPerPixel = _settings.RaysPerPixel;
        int maxRays = _settings.MaxRaysPerPixelLimit;
        if (ImGui.SliderInt("Rays Per Pixel", ref raysPerPixel, 1, Math.Max(1, maxRays)))
        {
            _settings.RaysPerPixel = raysPerPixel;
            _settings.Preset = RaytracerQualityPreset.Custom;
        }

        int maxBounces = _settings.MaxBounces;
        int maxBouncesLimit = _settings.MaxBouncesLimit;
        if (ImGui.SliderInt("Max Bounces", ref maxBounces, 1, Math.Max(1, maxBouncesLimit)))
        {
            _settings.MaxBounces = maxBounces;
            _settings.Preset = RaytracerQualityPreset.Custom;
        }

        int samplesPerFrame = _settings.SamplesPerFrame;
        int maxSamples = _settings.MaxSamplesPerFrameLimit;
        if (ImGui.SliderInt("Samples/Frame", ref samplesPerFrame, 1, Math.Max(1, maxSamples)))
        {
            _settings.SamplesPerFrame = samplesPerFrame;
            _settings.Preset = RaytracerQualityPreset.Custom;
        }

        var accumulate = _settings.Accumulate;
        if (ImGui.Checkbox("Accumulate", ref accumulate))
        {
            _settings.Accumulate = accumulate;
            _settings.Preset = RaytracerQualityPreset.Custom;
        }

        var denoise = _settings.Denoise;
        if (ImGui.Checkbox("Denoise", ref denoise))
        {
            _settings.Denoise = denoise;
            _settings.Preset = RaytracerQualityPreset.Custom;
        }

        var presets = new[] { "Fast", "Balanced", "Quality", "Custom" };
        int presetIndex = _settings.Preset switch
        {
            RaytracerQualityPreset.Fast => 0,
            RaytracerQualityPreset.Balanced => 1,
            RaytracerQualityPreset.Quality => 2,
            _ => 3
        };
        if (ImGui.Combo("Preset", ref presetIndex, presets, presets.Length))
        {
            var preset = presetIndex switch
            {
                0 => RaytracerQualityPreset.Fast,
                1 => RaytracerQualityPreset.Balanced,
                2 => RaytracerQualityPreset.Quality,
                _ => RaytracerQualityPreset.Custom
            };
            if (preset == RaytracerQualityPreset.Custom)
            {
                _settings.Preset = preset;
            }
            else
            {
                _settings.ApplyPreset(preset);
            }
        }

        if (ImGui.Button("Reset Accumulation"))
        {
            _settings.ResetAccumulation = true;
        }

        ImGui.Separator();
        ImGui.TextDisabled("Higher values improve quality at a performance cost.");
    }
}
