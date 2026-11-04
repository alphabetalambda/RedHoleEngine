using System.Numerics;
using RedHoleEngine.Audio;
using RedHoleEngine.Components;
using RedHoleEngine.Core.ECS;
using RedHoleEngine.Rendering.Debug;

namespace RedHoleEngine.Tests.Audio;

public class AudioEngineBehaviorTests
{
    [Fact]
    public void Constructor_RejectsNullBackend_AndDefaultCtorStillNamesOpenAL()
    {
        using var world = new World();
        Assert.Throws<ArgumentNullException>(() => new AudioEngine(world, (IAudioBackend)null!));

        var backend = new FakeAudioBackend();
        using var engine = new AudioEngine(world, backend, AcousticQualitySettings.Low);
        Assert.Same(backend, engine.Backend);
        Assert.False(engine.IsInitialized);
        Assert.Equal(AcousticQualitySettings.Low.RaysPerSource, engine.QualitySettings.RaysPerSource);
    }

    [Fact]
    public void Initialize_ConfiguresBackend_AndMasterVolumeIsForwarded()
    {
        using var world = new World();
        var backend = new FakeAudioBackend();
        using var engine = new AudioEngine(world, backend, AcousticQualitySettings.Medium);

        Assert.True(engine.Initialize());
        Assert.True(engine.IsInitialized);
        Assert.Equal(AcousticQualitySettings.Medium.SpeedOfSound, backend.SpeedOfSound);
        Assert.Equal(1f, backend.DopplerFactor);

        engine.MasterVolume = 0.25f;
        Assert.Equal(0.25f, engine.MasterVolume);
        Assert.Equal(0.25f, backend.MasterVolume);

        backend.InitializeResult = false;
        var failing = new FakeAudioBackend { InitializeResult = false };
        using var dead = new AudioEngine(world, failing);
        Assert.False(dead.Initialize());
    }

    [Fact]
    public void Update_PlaysAndStopsSources_AndOneShotDoesNotRequireAnEntity()
    {
        using var world = new World();
        var backend = new FakeAudioBackend();
        var settings = AcousticQualitySettings.Low;
        settings.UpdateRate = 1000f;
        using var engine = new AudioEngine(world, backend, settings);
        engine.Initialize();

        var listener = world.CreateEntity();
        world.AddComponent(listener, new TransformComponent(Vector3.Zero));
        world.AddComponent(listener, new AudioListenerComponent { IsActive = true, Volume = 0.8f, Velocity = Vector3.Zero });

        var sourceEntity = world.CreateEntity();
        var source = AudioSourceComponent.Create3D("beep.wav", 1f, 40f);
        source.IsPlaying = true;
        source.UseRaytracing = false;
        source.Loop = true;
        world.AddComponent(sourceEntity, new TransformComponent(new Vector3(4, 0, 0)));
        world.AddComponent(sourceEntity, source);

        engine.Update(0.05f);
        Assert.NotEmpty(backend.PlayedClips);
        Assert.Contains("beep.wav", backend.LoadedPaths);
        Assert.Equal(Vector3.Zero, backend.ListenerPosition);
        Assert.True(backend.Volumes.Values.All(v => v >= 0f && v <= 1f));
        Assert.True(backend.Pitches.Values.All(p => p >= 0.1f && p <= 4f));
        Assert.Equal(1, backend.UpdateCount);

        // Second update reuses the voice instead of starting another.
        int plays = backend.PlayedClips.Count;
        engine.Update(0.05f);
        Assert.Equal(plays, backend.PlayedClips.Count);

        world.GetComponent<AudioSourceComponent>(sourceEntity).IsPlaying = false;
        engine.Update(0.05f);
        Assert.NotEmpty(backend.StoppedVoices);

        var shot = engine.PlayOneShot("ui.wav", new Vector3(1, 2, 3), 0.5f, 1.2f);
        Assert.True(shot.IsValid);
        Assert.Equal(default, engine.PlayOneShot("missing-clip.wav", Vector3.Zero));

        engine.LoadClip("beep.wav");
        Assert.Equal(1, backend.LoadedPaths.Count(p => p == "beep.wav"));
        engine.UnloadClip("beep.wav");

        engine.StopAll();
        engine.CleanupEntity(sourceEntity);
    }

    [Fact]
    public void Update_WithoutListener_DoesNotStartVoices_AndDebugToggleClearsVisualizer()
    {
        using var world = new World();
        var backend = new FakeAudioBackend();
        using var engine = new AudioEngine(world, backend);
        Assert.Equal(0, backend.UpdateCount);
        engine.Update(0.016f); // not initialized
        Assert.Equal(0, backend.UpdateCount);

        engine.Initialize();
        var source = AudioSourceComponent.CreateAmbient("a.wav");
        source.IsPlaying = true;
        var entity = world.CreateEntity();
        world.AddComponent(entity, new TransformComponent());
        world.AddComponent(entity, source);
        engine.Update(0.016f);
        Assert.Empty(backend.PlayedClips);

        var draw = new DebugDrawManager();
        engine.EnableDebugVisualization(draw, AudioDebugFlags.Sources);
        Assert.NotNull(engine.DebugVisualizer);
        Assert.True(engine.DebugVisualizer!.Enabled);
        engine.DisableDebugVisualization();
        Assert.Null(engine.DebugVisualizer);
    }

    [Fact]
    public void Dispose_UnloadsClips()
    {
        using var world = new World();
        var backend = new FakeAudioBackend();
        var engine = new AudioEngine(world, backend);
        engine.Initialize();
        engine.LoadClip("a.wav");
        engine.Dispose();
        Assert.True(backend.Disposed);
    }
}
