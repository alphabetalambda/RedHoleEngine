using System.Numerics;
using RedHoleEngine.Audio;
using RedHoleEngine.Components;
using RedHoleEngine.Core.ECS;
using RedHoleEngine.Rendering.Debug;

namespace RedHoleEngine.Tests.Audio;

public class AudioDebugVisualizerTests
{
    [Fact]
    public void Defaults_DrawSourcesAndTheListener_AndScaleMovesTheMarker()
    {
        using var world = new World();
        var draw = new DebugDrawManager();
        var viz = new AudioDebugVisualizer(world, draw);
        Assert.Equal(AudioDebugFlags.Sources | AudioDebugFlags.Listener | AudioDebugFlags.DirectPaths, viz.Flags);
        Assert.Equal(1f, viz.Scale);
        Assert.True(viz.Enabled);

        var source = AddSource(world, new Vector3(2, 3, 0), playing: false, directivity: 0f, velocity: Vector3.Zero);
        viz.Flags = AudioDebugFlags.Sources;
        viz.Update(0.016f, 0f);
        Assert.Equal(48, draw.LineVertices.Count);
        Assert.Equal(0.5f, MaxDistance(draw, new Vector3(2, 3, 0)), 1e-3);
        Assert.Empty(draw.TextLabels);

        viz.Scale = 3f;
        viz.Flags = AudioDebugFlags.Sources | AudioDebugFlags.Labels;
        draw.Clear();
        viz.Update(0.016f, 0f);
        Assert.Equal(48, draw.LineVertices.Count);
        Assert.Equal(1.5f, MaxDistance(draw, new Vector3(2, 3, 0)), 1e-3);
        var label = Assert.Single(draw.TextLabels);
        Assert.Contains($"Source {source.Id}", label.Text);
        Assert.DoesNotContain("Vol:", label.Text);
        Assert.Equal(3f + 1.5f * 3f, label.WorldPosition.Y, 1e-3);
    }

    [Fact]
    public void PlayingSource_DrawsRangeDirectivityVelocityAndVolume()
    {
        using var world = new World();
        var draw = new DebugDrawManager();
        var viz = new AudioDebugVisualizer(world, draw) { Flags = AudioDebugFlags.Sources };

        var playing = AddSource(world, Vector3.Zero, playing: true, directivity: 0f, velocity: Vector3.Zero);
        viz.Update(0.016f, 0f);
        Assert.Equal(176, draw.LineVertices.Count);

        world.GetComponent<AudioSourceComponent>(playing).IsPlaying = false;
        var directed = AddSource(world, new Vector3(5, 0, 0), playing: true, directivity: 1f, velocity: new Vector3(2, 0, 0));
        world.GetComponent<AudioSourceComponent>(directed).MaxDistance = 10f;
        viz.Flags = AudioDebugFlags.Sources | AudioDebugFlags.Labels;
        draw.Clear();
        viz.Update(0.016f, 0f);

        Assert.True(draw.LineVertices.Count > 48);
        Assert.Contains(draw.TextLabels, text => text.Text.Contains("Vol: 1.00"));
    }

    [Fact]
    public void ListenerFlag_SkipsInactiveListeners_AndDrawsOrientation()
    {
        using var world = new World();
        var draw = new DebugDrawManager();
        var viz = new AudioDebugVisualizer(world, draw) { Flags = AudioDebugFlags.Listener };

        var inactive = world.CreateEntity();
        world.AddComponent(inactive, new TransformComponent(new Vector3(4, 0, 0)));
        world.AddComponent(inactive, new AudioListenerComponent { IsActive = false, Volume = 0.2f });
        viz.Update(0.016f, 0f);
        Assert.Empty(draw.LineVertices);

        var active = world.CreateEntity();
        world.AddComponent(active, new TransformComponent(Vector3.Zero));
        world.AddComponent(active, new AudioListenerComponent
        {
            IsActive = true,
            Volume = 0.5f,
            Velocity = new Vector3(0, 0, 4)
        });
        viz.Flags = AudioDebugFlags.Listener | AudioDebugFlags.Labels;
        viz.Update(0.016f, 0f);

        // Wire sphere (48) plus forward/up/right arrows and the listener velocity arrow (40).
        Assert.Equal(88, draw.LineVertices.Count);
        var label = Assert.Single(draw.TextLabels);
        Assert.Contains("Listener", label.Text);
        Assert.Contains("Vol: 0.50", label.Text);
        Assert.Contains(draw.LineVertices, vertex => Vector3.Distance(vertex.Position, new Vector3(0, 0, -2)) < 0.05f);
    }

    [Fact]
    public void Paths_FadeWithAge_ExpireOnTheFarSideOfTheLifetime_AndDropPerSource()
    {
        using var world = new World();
        var draw = new DebugDrawManager();
        var viz = new AudioDebugVisualizer(world, draw)
        {
            Flags = AudioDebugFlags.DirectPaths,
            PathLifetime = 0.5f
        };

        var direct = Path(PathType.Direct, doppler: 1f, gravity: 1f);
        viz.AddPath(1, Vector3.Zero, new Vector3(0, 0, 4), direct, currentTime: 0f);
        viz.Update(0.016f, 0.25f);

        Assert.Equal(2, draw.LineVertices.Count);
        Assert.Equal(0.5f, draw.LineVertices[0].Color.A, 1e-4);
        Assert.Contains(draw.LineVertices, vertex => vertex.Position == new Vector3(0, 0, 4));

        draw.Clear();
        viz.Update(0.016f, 0.5f);
        Assert.Equal(2, draw.LineVertices.Count);

        draw.Clear();
        viz.Update(0.016f, 0.51f);
        Assert.Empty(draw.LineVertices);

        viz.AddPath(1, Vector3.Zero, Vector3.UnitX, direct, 1f);
        viz.AddPath(2, Vector3.Zero, Vector3.UnitY, direct, 1f);
        viz.ClearPathsForSource(1);
        draw.Clear();
        viz.Update(0f, 1f);
        Assert.Equal(2, draw.LineVertices.Count);
        Assert.Contains(draw.LineVertices, vertex => vertex.Position == Vector3.UnitY);
        Assert.DoesNotContain(draw.LineVertices, vertex => vertex.Position == Vector3.UnitX);

        viz.Clear();
        draw.Clear();
        viz.Update(0f, 1f);
        Assert.Empty(draw.LineVertices);
    }

    [Fact]
    public void PathKinds_DrawHitsFrequencyBarsAndShiftMarkers()
    {
        using var world = new World();
        var draw = new DebugDrawManager();
        var viz = new AudioDebugVisualizer(world, draw) { PathLifetime = 5f, Flags = AudioDebugFlags.DirectPaths };

        var plain = Path(PathType.Direct, doppler: 1.04f, gravity: 1f);
        viz.AddPath(1, Vector3.Zero, Vector3.UnitX, plain, 0f);
        viz.Update(0f, 0f);
        Assert.Equal(2, draw.LineVertices.Count);

        viz.Clear();
        draw.Clear();
        var shifted = Path(PathType.Direct, doppler: 1.06f, gravity: 1f);
        viz.Flags = AudioDebugFlags.DirectPaths | AudioDebugFlags.RelativisticEffects;
        viz.AddPath(1, Vector3.Zero, Vector3.UnitX, shifted, 0f);
        viz.Update(0f, 0f);
        Assert.Equal(50, draw.LineVertices.Count);

        viz.Clear();
        draw.Clear();
        var reflected = Path(PathType.EarlyReflection, doppler: 1f, gravity: 1f);
        reflected.Hits.Add(new AcousticRayHit { Position = new Vector3(0, 1, 0), Normal = Vector3.UnitY });
        viz.Flags = AudioDebugFlags.ReflectionPaths | AudioDebugFlags.FrequencyData;
        viz.AddPath(1, Vector3.Zero, new Vector3(0, 0, 2), reflected, 0f);
        viz.Update(0f, 0f);
        // Two segments, one normal, and eight frequency bars.
        Assert.Equal(4 + 2 + 16, draw.LineVertices.Count);
        Assert.Single(draw.PointVertices);
        Assert.Contains(draw.LineVertices, vertex => vertex.Color.R > 0.9f && vertex.Color.G > 0.7f);

        viz.Clear();
        draw.Clear();
        var late = Path(PathType.LateReflection, doppler: 1f, gravity: 1f);
        var transmitted = Path(PathType.Transmission, doppler: 1f, gravity: 1f);
        viz.Flags = AudioDebugFlags.ReflectionPaths | AudioDebugFlags.TransmissionPaths;
        viz.AddPath(3, Vector3.Zero, Vector3.UnitX, late, 0f);
        viz.AddPath(3, Vector3.Zero, Vector3.UnitY, transmitted, 0f);
        viz.Update(0f, 0f);
        Assert.Equal(4, draw.LineVertices.Count);

        viz.Clear();
        draw.Clear();
        viz.Flags = AudioDebugFlags.All;
        viz.AddPath(4, Vector3.Zero, Vector3.UnitZ, Path(PathType.Diffraction, 1.2f, 0.8f), 0f);
        viz.Update(0f, 0f);
        Assert.Empty(draw.LineVertices);
    }

    [Fact(Skip = "BUG: DrawPaths handles PathType.Diffraction, but Update never calls it. Expected: AudioDebugFlags.All draws a diffraction path. Actual: a diffraction-only path produces no vertices.")]
    public void DiffractionPaths_AreDrawn()
    {
        using var world = new World();
        var draw = new DebugDrawManager();
        var viz = new AudioDebugVisualizer(world, draw) { Flags = AudioDebugFlags.All };
        viz.AddPath(1, Vector3.Zero, Vector3.UnitX, Path(PathType.Diffraction, 1f, 1f), 0f);
        viz.Update(0f, 0f);
        Assert.True(draw.LineVertices.Count > 0);
    }

    [Fact]
    public void SceneMarkers_DrawSurfacesGravityReverbAndWaves_AndDisabledSkipsThem()
    {
        using var world = new World();
        var draw = new DebugDrawManager();
        var viz = new AudioDebugVisualizer(world, draw) { Flags = AudioDebugFlags.AcousticSurfaces | AudioDebugFlags.Labels };

        var wall = world.CreateEntity();
        world.AddComponent(wall, new TransformComponent(Vector3.Zero));
        world.AddComponent(wall, AcousticSurfaceComponent.Create(AcousticMaterial.Wood));
        viz.Update(0.016f, 0f);
        Assert.Equal(24, draw.LineVertices.Count);
        Assert.Contains("Wood", Assert.Single(draw.TextLabels).Text);

        draw.Clear();
        viz.Flags = AudioDebugFlags.GravitySources;
        var newton = world.CreateEntity();
        world.AddComponent(newton, new TransformComponent(new Vector3(3, 0, 0)));
        world.AddComponent(newton, GravitySourceComponent.CreateNewtonian(5f));
        viz.Update(0.016f, 0f);
        Assert.Empty(draw.LineVertices);

        var hole = world.CreateEntity();
        world.AddComponent(hole, new TransformComponent(new Vector3(6, 0, 0)));
        world.AddComponent(hole, GravitySourceComponent.CreateBlackHole(2f));
        viz.Flags = AudioDebugFlags.GravitySources | AudioDebugFlags.Labels;
        viz.Update(0.016f, 0f);
        Assert.True(draw.LineVertices.Count > 100);
        Assert.Contains(draw.TextLabels, text => text.Text.Contains("Black Hole") && text.Text.Contains("M=2.0"));

        draw.Clear();
        viz.Flags = AudioDebugFlags.ReverbZones | AudioDebugFlags.Labels;
        var zone = world.CreateEntity();
        world.AddComponent(zone, new TransformComponent(new Vector3(0, 4, 0)));
        world.AddComponent(zone, ReverbZoneComponent.LargeHall);
        viz.Update(0.016f, 0f);
        Assert.Equal(192, draw.LineVertices.Count);
        Assert.Contains(draw.TextLabels, text => text.Text.Contains("RT60: 2.50s"));

        draw.Clear();
        viz.Flags = AudioDebugFlags.WavePropagation;
        AddSource(world, Vector3.Zero, playing: false, directivity: 0f, velocity: Vector3.Zero);
        var playing = AddSource(world, new Vector3(1, 0, 0), playing: true, directivity: 0f, velocity: Vector3.Zero);
        world.GetComponent<AudioSourceComponent>(playing).MaxDistance = 8f;
        viz.Update(0.2f, 0f);
        Assert.Equal(144, draw.LineVertices.Count);

        viz.Enabled = false;
        draw.Clear();
        viz.Flags = AudioDebugFlags.All;
        viz.Update(1f, 10f);
        Assert.False(draw.HasContent);
    }

    private static float MaxDistance(DebugDrawManager draw, Vector3 origin) =>
        draw.LineVertices.Max(vertex => Vector3.Distance(vertex.Position, origin));

    private static Entity AddSource(World world, Vector3 position, bool playing, float directivity, Vector3 velocity)
    {
        var entity = world.CreateEntity();
        world.AddComponent(entity, new TransformComponent(position));
        var source = AudioSourceComponent.Create3D("tone.wav", 1f, 6f);
        source.IsPlaying = playing;
        source.Volume = 1f;
        source.Directivity = directivity;
        source.Direction = Vector3.UnitZ;
        source.Velocity = velocity;
        world.AddComponent(entity, source);
        return entity;
    }

    private static AcousticPath Path(PathType type, float doppler, float gravity) => new()
    {
        Type = type,
        IsDirect = type == PathType.Direct,
        TotalDistance = 4f,
        TotalTime = 0.01f,
        FinalResponse = FrequencyResponse.Uniform(0.5f),
        DopplerShift = doppler,
        GravitationalShift = gravity
    };
}
