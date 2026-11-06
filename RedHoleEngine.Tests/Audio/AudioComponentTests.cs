using System.Numerics;
using RedHoleEngine.Audio;
using RedHoleEngine.Components;
using RedHoleEngine.Core.ECS;

namespace RedHoleEngine.Tests.Audio;

public class AudioComponentTests
{
    [Fact]
    public void AudioSource_DefaultAndFactories_SetSpatialAndAmbientFields()
    {
        var defaults = AudioSourceComponent.Default;
        Assert.Equal("", defaults.ClipId);
        Assert.Equal(1f, defaults.Volume);
        Assert.Equal(1f, defaults.Pitch);
        Assert.False(defaults.IsPlaying);
        Assert.False(defaults.Loop);
        Assert.Equal(1f, defaults.MinDistance);
        Assert.Equal(100f, defaults.MaxDistance);
        Assert.Equal(AttenuationModel.InverseSquare, defaults.Attenuation);
        Assert.True(defaults.UseRaytracing);
        Assert.Equal(64, defaults.RayCount);
        Assert.Equal(3, defaults.MaxBounces);
        Assert.Equal(Vector3.Zero, defaults.Velocity);
        Assert.Equal(0f, defaults.Directivity);
        Assert.Equal(Vector3.UnitZ, defaults.Direction);
        Assert.Equal(0, defaults.Priority);

        var spatial = AudioSourceComponent.Create3D("engine.wav", 2f, 80f);
        Assert.Equal("engine.wav", spatial.ClipId);
        Assert.Equal(2f, spatial.MinDistance);
        Assert.Equal(80f, spatial.MaxDistance);
        Assert.False(spatial.IsPlaying);
        Assert.True(spatial.UseRaytracing);
        Assert.Equal(AttenuationModel.InverseSquare, spatial.Attenuation);
        Assert.Equal(64, spatial.RayCount);

        var ambient = AudioSourceComponent.CreateAmbient("room.wav", 0.4f);
        Assert.Equal("room.wav", ambient.ClipId);
        Assert.Equal(0.4f, ambient.Volume);
        Assert.True(ambient.IsPlaying);
        Assert.True(ambient.Loop);
        Assert.Equal(0f, ambient.MinDistance);
        Assert.Equal(float.MaxValue, ambient.MaxDistance);
        Assert.Equal(AttenuationModel.None, ambient.Attenuation);
        Assert.False(ambient.UseRaytracing);
        Assert.Equal(0, ambient.RayCount);
        Assert.Equal(0, ambient.MaxBounces);
        Assert.Equal(10, ambient.Priority);
    }

    [Fact]
    public void AttenuationModel_DefinesTheFourFalloffKinds()
    {
        Assert.Equal(4, Enum.GetValues<AttenuationModel>().Length);
        Assert.True(Enum.IsDefined(AttenuationModel.InverseSquare));
        Assert.True(Enum.IsDefined(AttenuationModel.Linear));
        Assert.True(Enum.IsDefined(AttenuationModel.Logarithmic));
        Assert.True(Enum.IsDefined(AttenuationModel.None));
    }

    [Fact]
    public void ListenerSurfaceAndReverbPresets_ExposeTheirDocumentedValues()
    {
        var listener = AudioListenerComponent.Default;
        Assert.Equal(1f, listener.Volume);
        Assert.True(listener.IsActive);
        Assert.Equal(Vector3.Zero, listener.Velocity);
        Assert.Equal(128, listener.RayCount);

        var concrete = AcousticSurfaceComponent.Create(AcousticMaterial.Concrete);
        Assert.Equal("Concrete", concrete.Material.Name);
        Assert.True(concrete.AffectsDirectSound);
        Assert.True(concrete.GeneratesReflections);
        Assert.False(concrete.AllowsTransmission); // transmission average is 0.01, not greater
        Assert.Equal(0f, concrete.ThicknessOverride);

        var glass = AcousticSurfaceComponent.Create(AcousticMaterial.Glass);
        Assert.True(glass.AllowsTransmission);
        var air = AcousticSurfaceComponent.Create(AcousticMaterial.Air);
        Assert.True(air.AllowsTransmission);
        var carpet = AcousticSurfaceComponent.Create(AcousticMaterial.Carpet);
        Assert.False(carpet.AllowsTransmission);

        Assert.Equal(0.4f, ReverbZoneComponent.SmallRoom.ReverbTime);
        Assert.Equal(10f, ReverbZoneComponent.SmallRoom.PreDelay);
        Assert.Equal(10f, ReverbZoneComponent.SmallRoom.Radius);

        Assert.Equal(2.5f, ReverbZoneComponent.LargeHall.ReverbTime);
        Assert.Equal(40f, ReverbZoneComponent.LargeHall.PreDelay);
        Assert.Equal(50f, ReverbZoneComponent.LargeHall.Radius);
        Assert.Equal(2f, ReverbZoneComponent.LargeHall.LowFrequencyGain);

        Assert.Equal(1.2f, ReverbZoneComponent.SpaceStation.ReverbTime);
        Assert.Equal(30f, ReverbZoneComponent.SpaceStation.Radius);
        Assert.Equal(-3f, ReverbZoneComponent.SpaceStation.LowFrequencyGain);

        var vacuum = ReverbZoneComponent.Vacuum;
        Assert.Equal(0f, vacuum.ReverbTime);
        Assert.Equal(0f, vacuum.PreDelay);
        Assert.Equal(-100f, vacuum.EarlyReflectionLevel);
        Assert.Equal(-100f, vacuum.LateReverbLevel);
        Assert.Equal(1f, vacuum.HighFrequencyDamping);
        Assert.Equal(-100f, vacuum.LowFrequencyGain);
        Assert.Equal(0f, vacuum.Diffusion);
        Assert.Equal(float.MaxValue, vacuum.Radius);
        Assert.Equal(0f, vacuum.BlendDistance);
        Assert.Equal(-1000, vacuum.Priority);
    }

    [Fact]
    public void AudioExtensions_AttachListenerSourceSurfaceAndReverb()
    {
        using var world = new World();

        var listener = world.CreateEntity();
        world.AddAudioListener(listener, 0.7f);
        ref var listenerComponent = ref world.GetComponent<AudioListenerComponent>(listener);
        Assert.True(listenerComponent.IsActive);
        Assert.Equal(0.7f, listenerComponent.Volume);
        Assert.Equal(128, listenerComponent.RayCount);
        Assert.True(world.HasComponent<AudioListenerComponent>(listener));

        var source = world.CreateEntity();
        world.AddComponent(source, new TransformComponent());
        world.AddAudioSource(source, "alarm.wav", autoPlay: true);
        ref var sourceComponent = ref world.GetComponent<AudioSourceComponent>(source);
        Assert.Equal("alarm.wav", sourceComponent.ClipId);
        Assert.True(sourceComponent.IsPlaying);
        Assert.True(sourceComponent.UseRaytracing);
        Assert.Equal(1f, sourceComponent.MinDistance);
        Assert.Equal(50f, sourceComponent.MaxDistance);

        var quiet = world.CreateEntity();
        world.AddAudioSource(quiet, "blip.wav");
        Assert.False(world.GetComponent<AudioSourceComponent>(quiet).IsPlaying);

        var ambient = world.CreateEntity();
        world.AddAmbientAudio(ambient, "hum.wav", 0.3f);
        ref var ambientComponent = ref world.GetComponent<AudioSourceComponent>(ambient);
        Assert.Equal("hum.wav", ambientComponent.ClipId);
        Assert.Equal(0.3f, ambientComponent.Volume);
        Assert.Equal(AttenuationModel.None, ambientComponent.Attenuation);
        Assert.False(ambientComponent.UseRaytracing);
        Assert.True(ambientComponent.Loop);
        Assert.Equal(10, ambientComponent.Priority);

        var wall = world.CreateEntity();
        world.AddAcousticSurface(wall, AcousticMaterial.Wood);
        ref var surface = ref world.GetComponent<AcousticSurfaceComponent>(wall);
        Assert.Equal("Wood", surface.Material.Name);
        Assert.True(surface.GeneratesReflections);

        var zone = world.CreateEntity();
        world.AddReverbZone(zone, ReverbZoneComponent.SpaceStation);
        Assert.Equal(1.2f, world.GetComponent<ReverbZoneComponent>(zone).ReverbTime);
    }
}
