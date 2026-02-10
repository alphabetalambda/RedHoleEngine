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
        
        // Lensing Quality Section
        if (ImGui.CollapsingHeader("Lensing Quality"))
        {
            var lensingPresets = new[] { "Low", "Medium", "High", "Ultra", "Custom" };
            int lensingIndex = _settings.LensingQuality switch
            {
                LensingQuality.Low => 0,
                LensingQuality.Medium => 1,
                LensingQuality.High => 2,
                LensingQuality.Ultra => 3,
                _ => 4
            };
            if (ImGui.Combo("Quality##Lensing", ref lensingIndex, lensingPresets, lensingPresets.Length))
            {
                var quality = lensingIndex switch
                {
                    0 => LensingQuality.Low,
                    1 => LensingQuality.Medium,
                    2 => LensingQuality.High,
                    3 => LensingQuality.Ultra,
                    _ => LensingQuality.Custom
                };
                _settings.ApplyLensingQuality(quality);
                _settings.ResetAccumulation = true;
            }
            
            if (_settings.LensingQuality == LensingQuality.Custom)
            {
                int maxSteps = _settings.LensingMaxSteps;
                if (ImGui.SliderInt("Max Steps", ref maxSteps, 16, 256))
                {
                    _settings.LensingMaxSteps = maxSteps;
                    _settings.ResetAccumulation = true;
                }
                
                float stepSize = _settings.LensingStepSize;
                if (ImGui.SliderFloat("Step Size", ref stepSize, 0.1f, 1.0f))
                {
                    _settings.LensingStepSize = stepSize;
                    _settings.ResetAccumulation = true;
                }
            }
        }
        
        // Post-Processing Section
        if (ImGui.CollapsingHeader("Post-Processing", ImGuiTreeNodeFlags.DefaultOpen))
        {
            float exposure = _settings.Exposure;
            if (ImGui.SliderFloat("Exposure", ref exposure, 0.1f, 5.0f))
            {
                _settings.Exposure = exposure;
            }
            
            var bloom = _settings.EnableBloom;
            if (ImGui.Checkbox("Bloom", ref bloom))
            {
                _settings.EnableBloom = bloom;
            }
            
            if (_settings.EnableBloom)
            {
                ImGui.Indent();
                float bloomThreshold = _settings.BloomThreshold;
                if (ImGui.SliderFloat("Threshold##Bloom", ref bloomThreshold, 0.5f, 3.0f))
                {
                    _settings.BloomThreshold = bloomThreshold;
                }
                
                float bloomIntensity = _settings.BloomIntensity;
                if (ImGui.SliderFloat("Intensity##Bloom", ref bloomIntensity, 0.0f, 1.0f))
                {
                    _settings.BloomIntensity = bloomIntensity;
                }
                ImGui.Unindent();
            }
            
            var vignette = _settings.EnableVignette;
            if (ImGui.Checkbox("Vignette", ref vignette))
            {
                _settings.EnableVignette = vignette;
            }
            
            if (_settings.EnableVignette)
            {
                ImGui.Indent();
                float vignetteIntensity = _settings.VignetteIntensity;
                if (ImGui.SliderFloat("Intensity##Vignette", ref vignetteIntensity, 0.0f, 1.0f))
                {
                    _settings.VignetteIntensity = vignetteIntensity;
                }
                ImGui.Unindent();
            }
            
            var filmGrain = _settings.EnableFilmGrain;
            if (ImGui.Checkbox("Film Grain", ref filmGrain))
            {
                _settings.EnableFilmGrain = filmGrain;
            }
        }
        
        // Volumetric Effects Section
        if (ImGui.CollapsingHeader("Volumetric Effects"))
        {
            var volumetrics = _settings.EnableVolumetrics;
            if (ImGui.Checkbox("Volumetric Scattering", ref volumetrics))
            {
                _settings.EnableVolumetrics = volumetrics;
                _settings.ResetAccumulation = true;
            }
            
            if (_settings.EnableVolumetrics)
            {
                ImGui.Indent();
                float volIntensity = _settings.VolumetricIntensity;
                if (ImGui.SliderFloat("Intensity##Vol", ref volIntensity, 0.0f, 2.0f))
                {
                    _settings.VolumetricIntensity = volIntensity;
                    _settings.ResetAccumulation = true;
                }
                ImGui.Unindent();
            }
            
            var godRays = _settings.EnableGodRays;
            if (ImGui.Checkbox("God Rays", ref godRays))
            {
                _settings.EnableGodRays = godRays;
                _settings.ResetAccumulation = true;
            }
            
            if (_settings.EnableGodRays)
            {
                ImGui.Indent();
                float rayIntensity = _settings.GodRayIntensity;
                if (ImGui.SliderFloat("Intensity##Rays", ref rayIntensity, 0.0f, 2.0f))
                {
                    _settings.GodRayIntensity = rayIntensity;
                    _settings.ResetAccumulation = true;
                }
                ImGui.Unindent();
            }
        }
        
        // Black Hole Visualization Section
        if (ImGui.CollapsingHeader("Black Hole Visualization"))
        {
            var showErgo = _settings.ShowErgosphere;
            if (ImGui.Checkbox("Show Ergosphere", ref showErgo))
            {
                _settings.ShowErgosphere = showErgo;
                _settings.ResetAccumulation = true;
            }
            
            if (_settings.ShowErgosphere)
            {
                ImGui.Indent();
                float ergoOpacity = _settings.ErgosphereOpacity;
                if (ImGui.SliderFloat("Opacity##Ergo", ref ergoOpacity, 0.0f, 1.0f))
                {
                    _settings.ErgosphereOpacity = ergoOpacity;
                    _settings.ResetAccumulation = true;
                }
                ImGui.Unindent();
            }
            
            var showPhoton = _settings.ShowPhotonSphere;
            if (ImGui.Checkbox("Show Photon Sphere", ref showPhoton))
            {
                _settings.ShowPhotonSphere = showPhoton;
                _settings.ResetAccumulation = true;
            }
            
            if (_settings.ShowPhotonSphere)
            {
                ImGui.Indent();
                float photonOpacity = _settings.PhotonSphereOpacity;
                if (ImGui.SliderFloat("Opacity##Photon", ref photonOpacity, 0.0f, 1.0f))
                {
                    _settings.PhotonSphereOpacity = photonOpacity;
                    _settings.ResetAccumulation = true;
                }
                ImGui.Unindent();
            }
        }

        ImGui.Separator();
        ImGui.TextDisabled("Higher values improve quality at a performance cost.");
    }
}
