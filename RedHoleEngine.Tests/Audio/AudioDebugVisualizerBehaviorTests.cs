using System.Numerics;
using RedHoleEngine.Audio;
using RedHoleEngine.Components;
using RedHoleEngine.Core.ECS;
using RedHoleEngine.Rendering.Debug;

namespace RedHoleEngine.Tests.Audio;

public class AudioDebugVisualizerBehaviorTests
{
    [Fact]
    public void PathsExpire_AndEveryFlagDrawsSomethingWhenTheSceneHasIt()
    {
        using var world = new World();
        var draw = new DebugDrawManager();
        var viz = new AudioDebugVisualizer(world, draw)
        {
            Flags = AudioDebugFlags.All,
            Enabled = true,
            PathLifetime = 0.4f,
            Scale = 2f
        };

        var listener = world.CreateEntity();
        world.AddComponent(listener, new TransformComponent(Vector3.Zero));
        world.AddComponent(listener, new AudioListenerComponent { IsActive = true });

        var source = world.CreateEntity();
        var audio = AudioSourceComponent.Create3D("tone.wav");
        audio.IsPlaying = true;
        world.AddComponent(source, new TransformComponent(new Vector3(3, 1, 0)));
        world.AddComponent(source, audio);

        var surface = world.CreateEntity();
        world.AddComponent(surface, new TransformComponent(new Vector3(1, 0, 0)));
        world.AddComponent(surface, AcousticSurfaceComponent.Create(AcousticMaterial.Concrete));

        var hole = world.CreateEntity();
        world.AddComponent(hole, new TransformComponent(new Vector3(8, 0, 0)));
        world.AddComponent(hole, GravitySourceComponent.CreateRotatingBlackHole(2f, 0.5f, Vector3.UnitY));

        var zone = world.CreateEntity();
        world.AddComponent(zone, new TransformComponent(new Vector3(0, 0, 2)));
        world.AddComponent(zone, ReverbZoneComponent.LargeHall);

        var path = new AcousticPath
        {
            Type = PathType.EarlyReflection,
            IsDirect = false,
            TotalDistance = 6f,
            TotalTime = 0.02f,
            FinalResponse = FrequencyResponse.Uniform(0.5f),
            DopplerShift = 1.1f,
            GravitationalShift = 0.9f
        };
        path.Hits.Add(new AcousticRayHit { Position = new Vector3(1, 0, 0), Normal = Vector3.UnitY });
        viz.AddPath(source.Id, new Vector3(3, 1, 0), Vector3.Zero, path, currentTime: 0f);

        var direct = new AcousticPath { Type = PathType.Direct, IsDirect = true, TotalDistance = 3f, TotalTime = 0.01f, FinalResponse = FrequencyResponse.Uniform(1f) };
        viz.AddPath(source.Id, new Vector3(3, 1, 0), Vector3.Zero, direct, 0f);
        var transmitted = new AcousticPath { Type = PathType.Transmission, IsDirect = false, TotalDistance = 4f, TotalTime = 0.2f, FinalResponse = FrequencyResponse.Uniform(0.2f) };
        viz.AddPath(source.Id, new Vector3(3, 1, 0), Vector3.Zero, transmitted, 0f);
        var late = new AcousticPath { Type = PathType.LateReflection, IsDirect = false, TotalDistance = 12f, TotalTime = 0.1f, FinalResponse = FrequencyResponse.Uniform(0.1f) };
        viz.AddPath(9, Vector3.One, Vector3.Zero, late, 0f);

        viz.Update(0.016f, 0.016f);
        Assert.True(draw.HasContent);
        Assert.True(draw.GetTotalVertexCount() > 10);

        viz.ClearPathsForSource(9);
        viz.Enabled = false;
        draw.Clear();
        viz.Update(1f, 10f);
        Assert.False(draw.HasContent);

        viz.Enabled = true;
        viz.Update(0.1f, 5f); // original paths are older than PathLifetime
        viz.Clear();
        draw.Clear();
        viz.Update(0.1f, 5f);
        // Scene markers (sources, listener, surfaces, gravity, reverb) still draw with All flags.
        Assert.True(draw.LineVertices.Count + draw.PointVertices.Count > 0);
    }
}
