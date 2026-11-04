using System.Numerics;
using RedHoleEngine.Audio;
using RedHoleEngine.Components;
using RedHoleEngine.Core.ECS;

namespace RedHoleEngine.Tests.Audio;

public class AudioSystemTests
{
    private const float SoundSpeed = 343f;

    [Fact]
    public void Update_IsThrottledToTheQualityRate_AndSkipsIdleSources()
    {
        using var world = new World();
        var system = world.AddSystem<AudioSystem>();
        Assert.Equal(100, system.Priority);
        Assert.Equal(AcousticQualitySettings.Medium.RaysPerSource, system.QualitySettings.RaysPerSource);

        var updates = new List<int>();
        system.OnAudioUpdate += sources => updates.Add(sources.Count);

        var listener = AddListener(world, Vector3.Zero, 1f);
        var source = AddSource(world, new Vector3(10, 0, 0), Playing("tone.wav"));

        system.Update(0.01f);
        Assert.Empty(updates);
        Assert.Empty(system.OutputSources);

        system.Update(1f);
        Assert.Equal(new[] { 1 }, updates);
        var heard = Assert.Single(system.OutputSources);
        Assert.Equal(source.Id, heard.SourceEntityId);
        Assert.Equal("tone.wav", heard.ClipId);
        float expected = SimpleAttenuation(10f);
        Assert.Equal(expected, heard.Volume, 1e-4);
        Assert.Equal(10f / SoundSpeed, heard.Delay, 1e-4);
        Assert.Equal(1f, heard.Pitch, 1e-4);

        world.GetComponent<AudioSourceComponent>(source).Volume = 0f;
        system.Update(0.001f);
        Assert.Equal(new[] { 1 }, updates);
        Assert.Equal(expected, system.OutputSources[0].Volume, 1e-4);

        system.Update(1f);
        Assert.Equal(new[] { 1, 0 }, updates);
        Assert.Equal(0f, system.OutputSources[0].Volume, 1e-4);

        world.GetComponent<AudioSourceComponent>(source).IsPlaying = false;
        system.Update(1f);
        Assert.Empty(system.OutputSources);

        var bare = world.CreateEntity();
        world.AddComponent(bare, Playing("orphan.wav"));
        system.Update(1f);
        Assert.Empty(system.OutputSources);
        _ = listener;
    }

    [Fact]
    public void SetQuality_ChangesHowSoonTheNextMixRuns()
    {
        using var world = new World();
        var system = world.AddSystem<AudioSystem>();
        AddListener(world, Vector3.Zero, 1f);
        AddSource(world, new Vector3(4, 0, 0), Playing("a.wav"));

        int calls = 0;
        system.OnAudioUpdate += _ => calls++;

        system.Update(0.02f);
        Assert.Equal(0, calls);

        var fast = AcousticQualitySettings.Medium;
        fast.UpdateRate = 100f;
        system.SetQuality(fast);
        system.Update(0f);
        Assert.Equal(1, calls);
        Assert.Equal(100f, system.QualitySettings.UpdateRate);
    }

    [Fact]
    public void Listener_MustBeActive_AndTheFirstActiveOneWins()
    {
        using var world = new World();
        var system = world.AddSystem<AudioSystem>();
        int calls = 0;
        system.OnAudioUpdate += _ => calls++;

        AddSource(world, new Vector3(6, 0, 0), Playing("a.wav"));
        system.Update(1f);
        Assert.Equal(0, calls);
        Assert.Empty(system.OutputSources);

        var inactive = world.CreateEntity();
        world.AddComponent(inactive, new TransformComponent(new Vector3(0, 5, 0)));
        world.AddComponent(inactive, new AudioListenerComponent { IsActive = false, Volume = 0.2f });

        var active = world.CreateEntity();
        world.AddComponent(active, new TransformComponent(new Vector3(1, 0, 0)));
        world.AddComponent(active, new AudioListenerComponent
        {
            IsActive = true,
            Volume = 0.4f,
            Velocity = new Vector3(0, 0, 3)
        });

        system.Update(1f);
        Assert.Equal(1, calls);
        Assert.Equal(0.4f, system.Mixer.MasterVolume);
        Assert.Equal(new Vector3(1, 0, 0), system.Mixer.ListenerPosition);
        Assert.Equal(-Vector3.UnitZ, system.Mixer.ListenerForward);
        Assert.Equal(Vector3.UnitY, system.Mixer.ListenerUp);
        Assert.Equal(Vector3.UnitX, system.Mixer.ListenerRight);

        // A source on the listener's right pans positive under the transform's -Z forward.
        var source = world.CreateEntity();
        world.AddComponent(source, new TransformComponent(new Vector3(8, 0, 0)));
        world.AddComponent(source, Playing("right.wav"));
        system.Update(1f);
        var panned = system.OutputSources.Single(s => s.ClipId == "right.wav");
        Assert.True(panned.Pan > 0.9f);
    }

    [Fact]
    public void DeactivatingTheListener_LeavesThePreviousMixInPlace()
    {
        using var world = new World();
        var system = world.AddSystem<AudioSystem>();
        var listener = AddListener(world, Vector3.Zero, 1f);
        AddSource(world, new Vector3(10, 0, 0), Playing("a.wav"));
        int calls = 0;
        system.OnAudioUpdate += _ => calls++;

        system.Update(1f);
        float staleVolume = Assert.Single(system.OutputSources).Volume;
        Assert.Equal(1, calls);

        world.GetComponent<AudioListenerComponent>(listener).IsActive = false;
        world.GetComponent<AudioSourceComponent>(world.Query<AudioSourceComponent>().First()).Volume = 0f;
        system.Update(1f);

        Assert.Equal(1, calls);
        Assert.Equal(staleVolume, Assert.Single(system.OutputSources).Volume, 1e-5);
    }

    [Fact(Skip = "BUG: when the active listener disappears, AudioSystem returns before clearing OutputSources and does not raise OnAudioUpdate. Expected: the mix is cleared and listeners are told there is nothing to play.")]
    public void DeactivatingTheListener_ClearsTheMix()
    {
        using var world = new World();
        var system = world.AddSystem<AudioSystem>();
        var listener = AddListener(world, Vector3.Zero, 1f);
        AddSource(world, new Vector3(10, 0, 0), Playing("a.wav"));
        int calls = 0;
        system.OnAudioUpdate += _ => calls++;
        system.Update(1f);

        world.GetComponent<AudioListenerComponent>(listener).IsActive = false;
        system.Update(1f);

        Assert.Equal(2, calls);
        Assert.Empty(system.OutputSources);
    }

    [Fact]
    public void AttenuationModels_ScaleTheSimplePath_AndSilencePastMaxDistance()
    {
        const float distance = 10f;
        const float min = 0f;
        const float max = 20f;
        float simple = SimpleAttenuation(distance);
        float normalized = (distance - min) / (max - min);

        float none = VolumeFor(AttenuationModel.None, distance, min, max);
        float linear = VolumeFor(AttenuationModel.Linear, distance, min, max);
        float logarithmic = VolumeFor(AttenuationModel.Logarithmic, distance, min, max);
        float inverse = VolumeFor(AttenuationModel.InverseSquare, distance, min, max);
        float unknown = VolumeFor((AttenuationModel)99, distance, min, max);

        Assert.Equal(simple, none, 1e-4);
        Assert.Equal(simple * (1f - normalized), linear, 1e-4);
        Assert.Equal(simple * (1f - MathF.Log10(1f + normalized * 9f)), logarithmic, 1e-4);
        Assert.Equal(simple * (1f / (1f + normalized * normalized * 10f)), inverse, 1e-4);
        Assert.Equal(none, unknown, 1e-4);
        Assert.True(logarithmic < linear);

        float atMin = VolumeFor(AttenuationModel.Linear, 2f, 2f, 30f);
        float atMinInverse = VolumeFor(AttenuationModel.InverseSquare, 2f, 2f, 30f);
        Assert.Equal(SimpleAttenuation(2f), atMin, 1e-4);
        Assert.Equal(atMin, atMinInverse, 1e-4);

        Assert.Equal(0f, VolumeFor(AttenuationModel.Linear, 30f, 1f, 30f), 1e-5);
        Assert.Equal(0f, VolumeFor(AttenuationModel.Logarithmic, 40f, 1f, 30f), 1e-5);
        Assert.Equal(0f, VolumeFor(AttenuationModel.InverseSquare, 40f, 1f, 30f), 1e-5);
        Assert.True(VolumeFor(AttenuationModel.None, 40f, 1f, 30f) > 0f);
    }

    [Fact]
    public void NoneAttenuation_StillFallsOffWithDistance()
    {
        float near = VolumeFor(AttenuationModel.None, 0f, 0f, 1000f);
        float far = VolumeFor(AttenuationModel.None, 10f, 0f, 1000f);
        Assert.InRange(near, 0.99f, 1.01f);
        Assert.Equal(0.5f, far, 1e-3);
    }

    [Fact(Skip = "BUG: AttenuationModel.None still multiplies by CreateSimplePath's 1/(1+d^2*0.01). Expected: volume stays at base * listener volume at every distance. Actual: a source at 10m is about half as loud as a source at the listener.")]
    public void NoneAttenuation_IgnoresDistance()
    {
        float near = VolumeFor(AttenuationModel.None, 0f, 0f, 1000f);
        float far = VolumeFor(AttenuationModel.None, 10f, 0f, 1000f);
        Assert.Equal(1f, near, 0.02f);
        Assert.Equal(1f, far, 0.02f);
    }

    [Fact]
    public void Doppler_FollowsWhoIsClosingTheDistance()
    {
        float toward = PitchFor(new Vector3(-30, 0, 0), Vector3.Zero);
        float away = PitchFor(new Vector3(30, 0, 0), Vector3.Zero);
        float listenerToward = PitchFor(Vector3.Zero, new Vector3(30, 0, 0));
        float listenerAway = PitchFor(Vector3.Zero, new Vector3(-30, 0, 0));

        Assert.Equal(SoundSpeed / (SoundSpeed - 30f), toward, 1e-4);
        Assert.Equal(SoundSpeed / (SoundSpeed + 30f), away, 1e-4);
        Assert.Equal((SoundSpeed + 30f) / SoundSpeed, listenerToward, 1e-4);
        Assert.Equal((SoundSpeed - 30f) / SoundSpeed, listenerAway, 1e-4);
        Assert.True(toward > 1f);
        Assert.True(away < 1f);
    }

    [Fact]
    public void Doppler_AtTheSpeedOfSound_BecomesInfinite()
    {
        float sonic = PitchFor(new Vector3(-SoundSpeed, 0, 0), Vector3.Zero);
        Assert.True(float.IsPositiveInfinity(sonic));
    }

    [Fact(Skip = "BUG: AudioSystem.CalculateSimpleDoppler uses 0.01 * MathF.Sign(denominator). Sign(0) is 0, so a source speed of exactly c divides by zero. Expected: a finite positive pitch, matching AcousticRaytracer's non-zero epsilon guard.")]
    public void Doppler_AtTheSpeedOfSound_StaysFinite()
    {
        float sonic = PitchFor(new Vector3(-SoundSpeed, 0, 0), Vector3.Zero);
        Assert.True(float.IsFinite(sonic));
        Assert.True(sonic > 1f);
    }

    [Fact]
    public void CoincidentSource_WithVelocity_ProducesNaNPitch()
    {
        using var world = new World();
        var system = world.AddSystem<AudioSystem>();
        AddListener(world, Vector3.Zero, 1f);
        var source = Playing("a.wav");
        source.Velocity = new Vector3(5, 0, 0);
        source.Directivity = 0f;
        AddSource(world, Vector3.Zero, source);

        system.Update(1f);
        Assert.True(float.IsNaN(Assert.Single(system.OutputSources).Pitch));
    }

    [Fact(Skip = "BUG: CreateSimplePath normalizes a zero listener-source vector when either velocity is non-zero, so pitch becomes NaN. Expected: a finite pitch (1 when the positions coincide).")]
    public void CoincidentSource_WithVelocity_KeepsFinitePitch()
    {
        using var world = new World();
        var system = world.AddSystem<AudioSystem>();
        AddListener(world, Vector3.Zero, 1f);
        var source = Playing("a.wav");
        source.Velocity = new Vector3(5, 0, 0);
        AddSource(world, Vector3.Zero, source);
        system.Update(1f);
        Assert.True(float.IsFinite(Assert.Single(system.OutputSources).Pitch));
    }

    [Fact]
    public void Directivity_IsFullInFront_HalfBehind_AndNaNOnTopOfTheListener()
    {
        using var world = new World();
        var system = world.AddSystem<AudioSystem>();
        AddListener(world, Vector3.Zero, 1f);
        var audio = Playing("a.wav");
        audio.Directivity = 0.5f;
        audio.Direction = Vector3.UnitZ;
        audio.MinDistance = 1f;
        audio.MaxDistance = 100f;
        var source = AddSource(world, new Vector3(0, 0, -10), audio);

        system.Update(1f);
        float front = system.OutputSources[0].Volume;

        world.GetComponent<AudioSourceComponent>(source).Direction = -Vector3.UnitZ;
        system.Update(1f);
        float behind = system.OutputSources[0].Volume;
        Assert.Equal(front * 0.5f, behind, 1e-4);
        Assert.True(front > behind);

        world.GetComponent<AudioSourceComponent>(source).Directivity = 1f;
        system.Update(1f);
        Assert.Equal(0f, system.OutputSources[0].Volume, 1e-5);

        world.GetComponent<TransformComponent>(source).Position = Vector3.Zero;
        world.GetComponent<AudioSourceComponent>(source).Velocity = Vector3.Zero;
        world.GetComponent<AudioSourceComponent>(source).Direction = Vector3.UnitZ;
        system.Update(1f);
        Assert.True(float.IsNaN(system.OutputSources[0].Volume));
        Assert.Equal(1f, system.OutputSources[0].Pitch, 1e-4);
    }

    [Fact(Skip = "BUG: ApplyDirectivity normalizes a zero vector when the source sits on the listener, so volume becomes NaN. Expected: a finite cardioid gain.")]
    public void Directivity_OnTopOfTheListener_StaysFinite()
    {
        using var world = new World();
        var system = world.AddSystem<AudioSystem>();
        AddListener(world, Vector3.Zero, 1f);
        var audio = Playing("a.wav");
        audio.Directivity = 1f;
        audio.Direction = Vector3.UnitZ;
        AddSource(world, Vector3.Zero, audio);
        system.Update(1f);
        Assert.True(float.IsFinite(Assert.Single(system.OutputSources).Volume));
    }

    [Fact]
    public void VoiceLimit_PrefersPriority_WhileOutputSourcesKeepsEveryPlayingSource()
    {
        using var world = new World();
        var system = world.AddSystem<AudioSystem>();
        system.Mixer.MaxVoices = 1;
        AddListener(world, Vector3.Zero, 1f);

        var loud = Playing("loud.wav");
        loud.Priority = 0;
        loud.Volume = 1f;
        var loudEntity = AddSource(world, new Vector3(8, 0, 0), loud);

        var important = Playing("important.wav");
        important.Priority = 10;
        important.Volume = 0.2f;
        var importantEntity = AddSource(world, new Vector3(8, 0, 0), important);

        IReadOnlyList<ProcessedAudioSource>? delivered = null;
        system.OnAudioUpdate += sources => delivered = sources;
        system.Update(1f);

        Assert.Equal(2, system.OutputSources.Count);
        Assert.Contains(system.OutputSources, s => s.SourceEntityId == loudEntity.Id);
        Assert.Contains(system.OutputSources, s => s.SourceEntityId == importantEntity.Id);
        var winner = Assert.Single(delivered!);
        Assert.Equal(importantEntity.Id, winner.SourceEntityId);
        Assert.True(winner.Volume < system.OutputSources.Single(s => s.SourceEntityId == loudEntity.Id).Volume);
    }

    [Fact]
    public void Raytracing_MatchesTheSimplePathUntilGeometryOrGravityChangesIt()
    {
        using var world = new World();
        var system = world.AddSystem<AudioSystem>();
        AddListener(world, Vector3.Zero, 1f);
        var audio = Playing("a.wav");
        audio.UseRaytracing = false;
        audio.RayCount = 0;
        var source = AddSource(world, new Vector3(10, 0, 0), audio);

        system.Update(1f);
        float simpleVolume = system.OutputSources[0].Volume;
        float simplePitch = system.OutputSources[0].Pitch;
        float simpleDelay = system.OutputSources[0].Delay;

        world.GetComponent<AudioSourceComponent>(source).UseRaytracing = true;
        system.Update(1f);
        Assert.Equal(simpleVolume, system.OutputSources[0].Volume, 1e-4);
        Assert.Equal(simplePitch, system.OutputSources[0].Pitch, 1e-4);
        Assert.Equal(simpleDelay, system.OutputSources[0].Delay, 1e-4);

        var hole = world.CreateEntity();
        world.AddComponent(hole, new TransformComponent(new Vector3(5, 3, 0)));
        world.AddComponent(hole, GravitySourceComponent.CreateBlackHole(1f));
        system.Update(1f);
        Assert.True(system.OutputSources[0].Pitch < simplePitch);
        Assert.True(system.OutputSources[0].Delay < simpleDelay);

        var low = AcousticQualitySettings.Low;
        low.MaxDistance = 100f;
        system.SetQuality(low);
        // Source must sit outside the radius-5 acoustic sphere or the ray misses it.
        world.GetComponent<TransformComponent>(source).Position = new Vector3(20, 0, 0);
        var wall = world.CreateEntity();
        world.AddComponent(wall, new TransformComponent(new Vector3(10, 0, 0)));
        world.AddComponent(wall, AcousticSurfaceComponent.Create(AcousticMaterial.EventHorizon));
        world.GetComponent<AudioSourceComponent>(source).RayCount = 0;
        system.Update(1f);
        Assert.Equal(0f, system.OutputSources[0].Volume, 1e-5);
    }

    [Fact]
    public void SourceMaxBounces_DoesNotLimitReflections()
    {
        using var world = new World();
        var system = world.AddSystem<AudioSystem>();
        var settings = AcousticQualitySettings.Medium;
        settings.MaxBounces = 4;
        settings.UpdateRate = 1000f;
        system.SetQuality(settings);

        AddListener(world, new Vector3(0, 0, -8), 1f);
        var audio = Playing("a.wav");
        audio.UseRaytracing = true;
        audio.RayCount = 200;
        audio.MaxBounces = 0;
        AddSource(world, Vector3.Zero, audio);
        var mirror = world.CreateEntity();
        world.AddComponent(mirror, new TransformComponent(new Vector3(0, 0, 5.05f)));
        world.AddComponent(mirror, AcousticSurfaceComponent.Create(AcousticMaterial.Concrete));

        system.Update(1f);
        Assert.NotEmpty(system.OutputSources[0].EarlyReflections);
    }

    [Fact(Skip = "BUG: AudioSourceComponent.MaxBounces is never passed to the raytracer. Expected: MaxBounces of 0 produces no early reflections. Actual: AcousticQualitySettings.MaxBounces is used and reflections are still mixed.")]
    public void SourceMaxBounces_Zero_SuppressesReflections()
    {
        using var world = new World();
        var system = world.AddSystem<AudioSystem>();
        var settings = AcousticQualitySettings.Medium;
        settings.MaxBounces = 4;
        settings.UpdateRate = 1000f;
        system.SetQuality(settings);
        AddListener(world, new Vector3(0, 0, -8), 1f);
        var audio = Playing("a.wav");
        audio.UseRaytracing = true;
        audio.RayCount = 200;
        audio.MaxBounces = 0;
        AddSource(world, Vector3.Zero, audio);
        var mirror = world.CreateEntity();
        world.AddComponent(mirror, new TransformComponent(new Vector3(0, 0, 5.05f)));
        world.AddComponent(mirror, AcousticSurfaceComponent.Create(AcousticMaterial.Concrete));
        system.Update(1f);
        Assert.Empty(system.OutputSources[0].EarlyReflections);
    }

    [Fact]
    public void ListenerRayCount_DoesNotChangeTheMix()
    {
        float quiet = MixWithListenerRays(0);
        float busy = MixWithListenerRays(100_000);
        Assert.Equal(quiet, busy, 1e-4);
    }

    [Fact(Skip = "BUG: AudioListenerComponent.RayCount is never read. Expected: a listener ray budget of 0 versus 100000 changes listener-side tracing. Actual: both mixes are identical.")]
    public void ListenerRayCount_ChangesListenerSideTracing()
    {
        Assert.NotEqual(MixWithListenerRays(0), MixWithListenerRays(100_000));
    }

    [Fact]
    public void ReverbZones_DoNotReplaceTheMixerDefault()
    {
        using var world = new World();
        var system = world.AddSystem<AudioSystem>();
        var listener = AddListener(world, Vector3.Zero, 1f);
        world.AddReverbZone(listener, ReverbZoneComponent.LargeHall);
        AddSource(world, new Vector3(4, 0, 0), Playing("a.wav"));
        system.Update(1f);

        Assert.Equal(ReverbZoneComponent.SmallRoom.ReverbTime, system.Mixer.GlobalReverb.ReverbTime);
        Assert.Equal(0.3f, system.OutputSources[0].ReverbSend, 1e-4);
    }

    [Fact(Skip = "BUG: ReverbZoneComponent is never sampled by AudioSystem or AudioEngine. Expected: a listener inside LargeHall adopts that zone. Actual: Mixer.GlobalReverb stays on SmallRoom and the send stays at the no-late-reflection default.")]
    public void ReverbZones_DriveTheMixer()
    {
        using var world = new World();
        var system = world.AddSystem<AudioSystem>();
        var listener = AddListener(world, Vector3.Zero, 1f);
        world.AddReverbZone(listener, ReverbZoneComponent.LargeHall);
        AddSource(world, new Vector3(4, 0, 0), Playing("a.wav"));
        system.Update(1f);
        Assert.Equal(ReverbZoneComponent.LargeHall.ReverbTime, system.Mixer.GlobalReverb.ReverbTime);
    }

    [Fact]
    public void OnDestroy_DropsTheMixerCache()
    {
        using var world = new World();
        var system = world.AddSystem<AudioSystem>();
        AddListener(world, Vector3.Zero, 1f);
        AddSource(world, new Vector3(3, 0, 0), Playing("a.wav"));
        system.Update(1f);
        var first = system.OutputSources[0];

        system.OnDestroy();
        system.Update(1f);
        Assert.NotSame(first, system.OutputSources[0]);
        Assert.Equal("a.wav", system.OutputSources[0].ClipId);
    }

    private static float VolumeFor(AttenuationModel model, float distance, float min, float max)
    {
        using var world = new World();
        var system = world.AddSystem<AudioSystem>();
        AddListener(world, Vector3.Zero, 1f);
        var audio = Playing("a.wav");
        audio.Attenuation = model;
        audio.MinDistance = min;
        audio.MaxDistance = max;
        AddSource(world, new Vector3(distance, 0, 0), audio);
        system.Update(1f);
        return Assert.Single(system.OutputSources).Volume;
    }

    private static float PitchFor(Vector3 sourceVelocity, Vector3 listenerVelocity)
    {
        using var world = new World();
        var system = world.AddSystem<AudioSystem>();
        var listener = AddListener(world, Vector3.Zero, 1f);
        world.GetComponent<AudioListenerComponent>(listener).Velocity = listenerVelocity;
        var audio = Playing("a.wav");
        audio.Velocity = sourceVelocity;
        audio.Pitch = 1f;
        AddSource(world, new Vector3(10, 0, 0), audio);
        system.Update(1f);
        return Assert.Single(system.OutputSources).Pitch;
    }

    private static float MixWithListenerRays(int rayCount)
    {
        using var world = new World();
        var system = world.AddSystem<AudioSystem>();
        var listener = AddListener(world, Vector3.Zero, 1f);
        world.GetComponent<AudioListenerComponent>(listener).RayCount = rayCount;
        var audio = Playing("a.wav");
        audio.UseRaytracing = true;
        audio.RayCount = 0;
        AddSource(world, new Vector3(6, 0, 0), audio);
        system.Update(1f);
        return system.OutputSources[0].Volume;
    }

    private static float SimpleAttenuation(float distance) => 1f / (1f + distance * distance * 0.01f);

    private static AudioSourceComponent Playing(string clip)
    {
        var source = AudioSourceComponent.Create3D(clip, 1f, 100f);
        source.IsPlaying = true;
        source.UseRaytracing = false;
        source.RayCount = 0;
        source.Volume = 1f;
        source.Pitch = 1f;
        source.Velocity = Vector3.Zero;
        source.Directivity = 0f;
        // None keeps the model gain at 1 so tests see CreateSimplePath's own falloff directly.
        source.Attenuation = AttenuationModel.None;
        return source;
    }

    private static Entity AddListener(World world, Vector3 position, float volume)
    {
        var entity = world.CreateEntity();
        world.AddComponent(entity, new TransformComponent(position));
        world.AddComponent(entity, new AudioListenerComponent
        {
            IsActive = true,
            Volume = volume,
            Velocity = Vector3.Zero,
            RayCount = 128
        });
        return entity;
    }

    private static Entity AddSource(World world, Vector3 position, AudioSourceComponent source)
    {
        var entity = world.CreateEntity();
        world.AddComponent(entity, new TransformComponent(position));
        world.AddComponent(entity, source);
        return entity;
    }
}
