using System.Numerics;
using RedHoleEngine.Components;
using RedHoleEngine.Core.ECS;
using RedHoleEngine.Engine;
using RedHoleEngine.Physics;
using RedHoleEngine.Rendering;

namespace RedHoleEngine.Tests.Rendering;

public class LaserCoverageTests
{
    [Fact]
    public void EmitterFactories_SetTypeSpecificDefaults()
    {
        var beam = LaserEmitterComponent.CreateBeam(25f, 0.2f, new Vector4(0f, 1f, 0f, 1f));
        Assert.Equal(LaserType.Beam, beam.Type);
        Assert.True(beam.Enabled);
        Assert.True(beam.FireOnStart);
        Assert.Equal(25f, beam.MaxRange);
        Assert.Equal(0.2f, beam.BeamWidth);
        Assert.Equal(0.3f, beam.CoreWidth);
        Assert.Equal(10f, beam.Damage);
        Assert.False(beam.CanRedirect);

        var pulse = LaserEmitterComponent.CreatePulse(40f, 1.5f, 8f);
        Assert.Equal(LaserType.Pulse, pulse.Type);
        Assert.False(pulse.FireOnStart);
        Assert.True(pulse.AutoFire);
        Assert.Equal(40f, pulse.PulseSpeed);
        Assert.Equal(1.5f, pulse.PulseLength);
        Assert.Equal(8f, pulse.FireRate);
        Assert.Equal(3, pulse.MaxBounces);
        Assert.True(pulse.CanRedirect);

        var scan = LaserEmitterComponent.CreateScanning(30f, 60f);
        Assert.Equal(LaserType.Scanning, scan.Type);
        Assert.Equal(30f, scan.ScanSpeed);
        Assert.Equal(60f, scan.ScanAngle);
        Assert.Equal(Vector3.UnitY, scan.ScanAxis);
        Assert.Equal(1, scan.ScanDirection);
    }

    [Fact]
    public void Renderer_BuildsBillboardLayers_AndSkipsDegenerateBeams()
    {
        Assert.Equal(36, LaserVertex.SizeInBytes);

        var world = new World();
        var camera = new Camera(new Vector3(0f, 0f, 10f));
        var renderer = new LaserRenderer();

        renderer.BuildMeshData();
        Assert.Equal(0, renderer.VertexCount);

        renderer.SetWorld(world);
        renderer.BuildMeshData();
        Assert.Equal(0, renderer.VertexCount);

        renderer.SetCamera(camera);
        var degenerate = world.CreateEntity();
        world.AddComponent(degenerate, new LaserSegmentComponent
        {
            Start = Vector3.Zero,
            End = new Vector3(0.0001f, 0f, 0f),
            Width = 1f,
            CoreWidth = 0.5f,
            Intensity = 1f
        });
        renderer.BuildMeshData();
        Assert.Equal(0, renderer.VertexCount);

        world.DestroyEntity(degenerate);
        var color = new Vector4(1f, 0f, 0f, 1f);
        var core = new Vector4(1f, 1f, 1f, 1f);
        world.AddComponent(world.CreateEntity(), new LaserSegmentComponent
        {
            Start = Vector3.Zero,
            End = new Vector3(2f, 0f, 0f),
            Width = 1f,
            BeamColor = color,
            CoreColor = core,
            CoreWidth = 0.5f,
            Intensity = 2f
        });

        renderer.BuildMeshData();
        Assert.Equal(12, renderer.VertexCount);
        Assert.Equal(18, renderer.IndexCount);
        Assert.Equal(12, renderer.GetVertexArray().Length);
        Assert.Equal(18, renderer.GetIndexArray().Length);

        var outer = renderer.Vertices[0];
        Assert.Equal(color.X * 2f, outer.Color.X, precision: 3);
        Assert.Equal(color.W * 2f * 0.3f, outer.Color.W, precision: 3);
        Assert.Equal(0f, outer.UV.X);
        Assert.Equal(0f, outer.UV.Y);
        Assert.True(MathF.Abs(outer.Position.Y) > 0.7f);

        var coreVertex = renderer.Vertices[8];
        Assert.Equal(core.X * 2f, coreVertex.Color.X, precision: 3);
        Assert.True(MathF.Abs(coreVertex.Position.Y) < MathF.Abs(renderer.Vertices[4].Position.Y));

        var raster = renderer.ToRasterVertices();
        Assert.Equal(renderer.Vertices[0].Position, raster[0].Position);
        Assert.Equal(renderer.Vertices[0].Color, raster[0].Color);

        var indices = renderer.Indices;
        Assert.Equal(new uint[] { 0, 1, 2, 0, 2, 3 }, indices.Take(6));
    }

    [Fact]
    public void Renderer_UsesFallbackAxis_WhenBeamPointsAtCamera_AndOmitsCore()
    {
        var world = new World();
        var renderer = new LaserRenderer();
        renderer.SetWorld(world);
        renderer.SetCamera(new Camera(new Vector3(0f, 0f, 10f)));
        world.AddComponent(world.CreateEntity(), new LaserSegmentComponent
        {
            Start = Vector3.Zero,
            End = new Vector3(0f, 0f, 4f),
            Width = 2f,
            BeamColor = Vector4.One,
            CoreColor = Vector4.One,
            CoreWidth = 0f,
            Intensity = 1f
        });

        renderer.BuildMeshData();
        Assert.Equal(8, renderer.VertexCount);
        Assert.Equal(12, renderer.IndexCount);
        Assert.True(MathF.Abs(renderer.Vertices[0].Position.X) > 1f);
    }

    [Fact]
    public void LaserSystem_TracesStraightBeam_AndIgnoresDisabledEmitters()
    {
        var world = new World();
        var system = world.AddSystem(new LaserSystem());
        Assert.Equal(-40, system.Priority);

        var disabled = world.CreateEntity();
        world.AddComponent(disabled, new TransformComponent(Vector3.Zero));
        world.AddComponent(disabled, LaserEmitterComponent.CreateBeam(10f));
        system.SetEmitterEnabled(disabled, false);

        var idle = world.CreateEntity();
        var idleEmitter = LaserEmitterComponent.CreateBeam(10f);
        idleEmitter.FireOnStart = false;
        world.AddComponent(idle, new TransformComponent(Vector3.Zero));
        world.AddComponent(idle, idleEmitter);

        var beam = world.CreateEntity();
        world.AddComponent(beam, new TransformComponent(new Vector3(1f, 2f, 3f)));
        world.AddComponent(beam, LaserEmitterComponent.CreateBeam(8f, 0.4f));

        system.Update(0.016f);

        var segments = world.Query<LaserSegmentComponent>().ToList();
        Assert.Single(segments);
        var segment = world.GetComponent<LaserSegmentComponent>(segments[0]);
        Assert.Equal(new Vector3(1f, 2f, 3f), segment.Start);
        Assert.Equal(new Vector3(1f, 2f, 3f - 8f), segment.End);
        Assert.Equal(0.4f, segment.Width);
        Assert.Equal(1f, segment.Intensity);
        Assert.True(segment.ShowEnergyPulses);
        Assert.Equal(8, segment.EnergyPulseCount);
        Assert.Equal(beam, segment.EmitterEntity);

        system.Update(0.016f);
        Assert.Single(world.Query<LaserSegmentComponent>());
    }

    [Fact]
    public void LaserSystem_FiresPulses_AndAdvancesThem()
    {
        var world = new World();
        var system = world.AddSystem(new LaserSystem());
        var emitter = world.CreateEntity();
        var pulse = LaserEmitterComponent.CreatePulse(speed: 10f, length: 2f, fireRate: 5f);
        world.AddComponent(emitter, new TransformComponent(Vector3.Zero));
        world.AddComponent(emitter, pulse);

        system.FirePulse(world.CreateEntity());
        system.Update(0.2f);

        var pulses = world.Query<LaserPulseComponent>().ToList();
        Assert.Equal(1, pulses.Count);
        foreach (var entity in pulses)
        {
            var body = world.GetComponent<LaserPulseComponent>(entity);
            Assert.Equal(new Vector3(0f, 0f, -2f), body.Position);
            Assert.Equal(2f, body.DistanceTraveled);
            Assert.True(world.HasComponent<TransformComponent>(entity));
            Assert.Equal(body.Position, world.GetComponent<TransformComponent>(entity).Position);
        }

        var segments = world.Query<LaserSegmentComponent>().ToList();
        Assert.Equal(1, segments.Count);
        var segment = world.GetComponent<LaserSegmentComponent>(segments[0]);
        Assert.Equal(Vector3.Zero, segment.Start);
        Assert.Equal(new Vector3(0f, 0f, -2f), segment.End);
        Assert.Equal(6, segment.EnergyPulseCount);
    }

    [Fact]
    public void LaserSystem_ScanningReversesAtSweepLimit()
    {
        var world = new World();
        var system = world.AddSystem(new LaserSystem());
        var entity = world.CreateEntity();
        world.AddComponent(entity, new TransformComponent(Vector3.Zero));
        world.AddComponent(entity, LaserEmitterComponent.CreateScanning(scanSpeed: 90f, scanAngle: 90f));

        system.Update(0.5f);
        var emitter = world.GetComponent<LaserEmitterComponent>(entity);
        Assert.Equal(45f, emitter.ScanAngleCurrent, precision: 3);
        Assert.Equal(-1, emitter.ScanDirection);
        Assert.Single(world.Query<LaserSegmentComponent>());

        system.Update(1.2f);
        emitter = world.GetComponent<LaserEmitterComponent>(entity);
        Assert.Equal(-45f, emitter.ScanAngleCurrent, precision: 3);
        Assert.Equal(1, emitter.ScanDirection);
    }

    [Fact]
    public void LaserSystem_GravityBendsBeamOffTheForwardAxis()
    {
        var world = new World();
        var system = world.AddSystem(new LaserSystem());
        var emitter = world.CreateEntity();
        var beam = LaserEmitterComponent.CreateBeam(range: 12f, width: 0.1f);
        beam.UseRaycast = false;
        world.AddComponent(emitter, new TransformComponent(Vector3.Zero));
        world.AddComponent(emitter, beam);

        // rs used for absorption is Mass * 50, so a heavy nearby hole swallows the beam
        // at the emitter. A lighter hole still inside the bend radius pulls the beam off axis.
        var hole = world.CreateEntity();
        world.AddComponent(hole, new TransformComponent(new Vector3(3f, 0f, -6f)));
        world.AddComponent(hole, new GravitySourceComponent
        {
            Mass = 0.15f,
            GravityType = GravityType.Schwarzschild
        });

        system.Update(0.016f);
        var segments = world.Query<LaserSegmentComponent>().Select(e => world.GetComponent<LaserSegmentComponent>(e)).ToList();
        Assert.NotEmpty(segments);
        Assert.Contains(segments, segment => MathF.Abs(segment.End.X) > 0.05f || MathF.Abs(segment.Start.X) > 0.05f);
    }

    [Fact]
    public void LaserSystem_DestroysPulseThatExceedsRange()
    {
        var world = new World();
        var system = world.AddSystem(new LaserSystem());
        var emitter = world.CreateEntity();
        var pulse = LaserEmitterComponent.CreatePulse(speed: 100f, length: 1f, fireRate: 1f);
        pulse.MaxRange = 5f;
        pulse.AutoFire = false;
        world.AddComponent(emitter, new TransformComponent(Vector3.Zero));
        world.AddComponent(emitter, pulse);

        system.FirePulse(emitter);
        Assert.Single(world.Query<LaserPulseComponent>());
        system.Update(0.1f);
        Assert.Empty(world.Query<LaserPulseComponent>());
    }

    [Fact]
    public void LaserRenderSystem_BuildsFromWorldSegments()
    {
        var world = new World();
        var render = world.AddSystem(new LaserRenderSystem());
        Assert.Equal(-45, render.Priority);
        render.SetCamera(new Camera(new Vector3(0f, 5f, 0f)));
        world.AddComponent(world.CreateEntity(), new LaserSegmentComponent
        {
            Start = Vector3.Zero,
            End = Vector3.UnitX,
            Width = 0.2f,
            BeamColor = Vector4.One,
            CoreColor = Vector4.One,
            CoreWidth = 0.25f,
            Intensity = 1f
        });

        render.Update(0.016f);
        Assert.Equal(12, render.Renderer.VertexCount);
        Assert.Equal(18, render.Renderer.IndexCount);
    }

    [Fact]
    public void RedirectComponent_ExposesMirrorRefractSplitAndAbsorb()
    {
        var redirect = new LaserRedirectComponent
        {
            Type = LaserRedirectComponent.RedirectType.Refract,
            Efficiency = 0.5f,
            RefractAngle = 15f,
            SplitCount = 2,
            TintColor = new Vector4(1f, 0f, 0f, 1f)
        };
        Assert.Equal(LaserRedirectComponent.RedirectType.Refract, redirect.Type);
        Assert.Equal(0.5f, redirect.Efficiency);
        Assert.True(Enum.IsDefined(LaserRedirectComponent.RedirectType.Absorb));
        Assert.True(Enum.IsDefined(LaserRedirectComponent.RedirectType.Split));
        Assert.True(Enum.IsDefined(LaserRedirectComponent.RedirectType.Mirror));
    }
}
