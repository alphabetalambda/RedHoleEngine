using RedHoleEngine.Components;
using RedHoleEngine.Core.ECS;
using RedHoleEngine.Platform;
using RedHoleEngine.Rendering;

namespace RedHoleEngine.Tests.Rendering;

public class RenderSettingsCoverageTests
{
    [Fact]
    public void RenderSettings_DefaultsToRaytraced()
    {
        var settings = new RenderSettings();
        Assert.Equal(RenderMode.Raytraced, settings.Mode);
        settings.Mode = RenderMode.Rasterized;
        Assert.Equal(RenderMode.Rasterized, settings.Mode);
    }

    [Fact]
    public void ComponentFactories_CopyProfileValues()
    {
        var manual = new RenderSettingsComponent(RenderMode.Rasterized);
        Assert.True(manual.Enabled);
        Assert.Equal(RenderMode.Rasterized, manual.Mode);
        Assert.Equal(RaytracerQualityPreset.Balanced, manual.Preset);
        Assert.False(manual.UsePerformanceProfile);
        Assert.Equal(64, manual.LensingMaxSteps);

        var ultra = RenderSettingsComponent.FromProfile(PerformanceProfileType.Ultra);
        var profile = PerformanceProfiles.Get(PerformanceProfileType.Ultra);
        Assert.True(ultra.UsePerformanceProfile);
        Assert.Equal(PerformanceProfileType.Ultra, ultra.PerformanceProfile);
        Assert.Equal(profile.RaysPerPixel, ultra.RaysPerPixel);
        Assert.Equal(profile.MaxBounces, ultra.MaxBounces);
        Assert.Equal(profile.LensingQuality, ultra.LensingQuality);
        Assert.Equal(profile.ShowErgosphere, ultra.ShowErgosphere);

        var auto = RenderSettingsComponent.AutoDetect();
        Assert.Equal(PerformanceProfileType.Auto, auto.PerformanceProfile);
        Assert.True(auto.RaysPerPixel >= 1);
    }

    [Fact]
    public void System_AppliesFirstEnabledComponent_AndClampsBackendOnly()
    {
        var world = new World();
        var backend = new RecordingGraphicsBackend();
        var system = world.AddSystem(new RenderSettingsSystem());
        Assert.Equal(-52, system.Priority);

        system.Update(0.016f);
        Assert.Equal(RenderMode.Raytraced, backend.RenderSettings.Mode);

        system.SetBackend(backend);

        var disabled = world.CreateEntity();
        var off = new RenderSettingsComponent { Enabled = false, Mode = RenderMode.Rasterized, RaysPerPixel = 9 };
        world.AddComponent(disabled, off);

        var active = world.CreateEntity();
        var settings = new RenderSettingsComponent
        {
            Enabled = true,
            Mode = RenderMode.Rasterized,
            Preset = RaytracerQualityPreset.Fast,
            RaysPerPixel = 9,
            MaxBounces = 9,
            ResetAccumulation = true,
            LensingQuality = LensingQuality.High,
            LensingMaxSteps = 160,
            LensingStepSize = 0.2f,
            LensingBvhCheckInterval = 3,
            LensingMaxDistance = 400f,
            ShowErgosphere = true,
            ErgosphereOpacity = 0.7f,
            ShowPhotonSphere = true,
            PhotonSphereOpacity = 0.4f,
            MaxRaysPerPixelLimit = 3,
            MaxBouncesLimit = 2,
            MaxSamplesPerFrameLimit = 2
        };
        world.AddComponent(active, settings);

        var ignored = world.CreateEntity();
        world.AddComponent(ignored, new RenderSettingsComponent
        {
            Enabled = true,
            Mode = RenderMode.Raytraced,
            Preset = RaytracerQualityPreset.Custom,
            RaysPerPixel = 6
        });

        system.Update(0.016f);

        Assert.Equal(RenderMode.Rasterized, backend.RenderSettings.Mode);
        var applied = world.GetComponent<RenderSettingsComponent>(active);
        Assert.Equal(1, applied.RaysPerPixel);
        Assert.Equal(1, applied.MaxBounces);
        Assert.False(applied.Accumulate);
        Assert.False(applied.Denoise);
        Assert.False(applied.ResetAccumulation);
        Assert.Equal(9, world.GetComponent<RenderSettingsComponent>(disabled).RaysPerPixel);
        Assert.Equal(6, world.GetComponent<RenderSettingsComponent>(ignored).RaysPerPixel);

        Assert.Equal(1, backend.RaytracerSettings.RaysPerPixel);
        Assert.True(backend.RaytracerSettings.ResetAccumulation);
        Assert.Equal(LensingQuality.High, backend.RaytracerSettings.LensingQuality);
        Assert.Equal(160, backend.RaytracerSettings.LensingMaxSteps);
        Assert.True(backend.RaytracerSettings.ShowErgosphere);
        Assert.Equal(0.7f, backend.RaytracerSettings.ErgosphereOpacity);
        Assert.True(backend.RaytracerSettings.ShowPhotonSphere);
        Assert.Equal(0.4f, backend.RaytracerSettings.PhotonSphereOpacity);
    }

    [Fact]
    public void System_CustomPreset_ClampsCopiedSampleCount()
    {
        var world = new World();
        var backend = new RecordingGraphicsBackend();
        var system = world.AddSystem(new RenderSettingsSystem());
        system.SetBackend(backend);
        world.AddComponent(world.CreateEntity(), new RenderSettingsComponent
        {
            Enabled = true,
            Preset = RaytracerQualityPreset.Custom,
            RaysPerPixel = 0,
            MaxBounces = 0,
            SamplesPerFrame = 0,
            Accumulate = false,
            Denoise = true
        });

        system.Update(0.016f);

        var component = world.GetComponent<RenderSettingsComponent>(world.Query<RenderSettingsComponent>().Single());
        Assert.Equal(0, component.RaysPerPixel);
        Assert.Equal(1, backend.RaytracerSettings.RaysPerPixel);
        Assert.Equal(1, backend.RaytracerSettings.MaxBounces);
        Assert.Equal(1, backend.RaytracerSettings.SamplesPerFrame);
        Assert.False(backend.RaytracerSettings.Accumulate);
        Assert.True(backend.RaytracerSettings.Denoise);
    }
}
