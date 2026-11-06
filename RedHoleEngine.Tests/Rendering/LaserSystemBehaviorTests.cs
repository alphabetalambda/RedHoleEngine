using System.Numerics;
using RedHoleEngine.Components;
using RedHoleEngine.Core.ECS;
using RedHoleEngine.Rendering;

namespace RedHoleEngine.Tests.Rendering;

public class LaserSystemBehaviorTests
{
    [Fact]
    public void BeamPulseAndScan_CreateSegments_AndDisabledEmittersStayQuiet()
    {
        using var world = new World();
        var lasers = world.AddSystem<LaserSystem>();
        Assert.Equal(-40, lasers.Priority);

        var beam = world.CreateEntity();
        world.AddComponent(beam, new TransformComponent(new Vector3(0, 0, 0)));
        world.AddComponent(beam, LaserEmitterComponent.CreateBeam(range: 10f, width: 0.2f));

        var pulse = world.CreateEntity();
        world.AddComponent(pulse, new TransformComponent(new Vector3(1, 0, 0)));
        var pulseEmitter = LaserEmitterComponent.CreatePulse(speed: 20f, length: 0.4f, fireRate: 10f);
        world.AddComponent(pulse, pulseEmitter);

        var scan = world.CreateEntity();
        world.AddComponent(scan, new TransformComponent(Vector3.Zero));
        world.AddComponent(scan, LaserEmitterComponent.CreateScanning(scanSpeed: 90f, scanAngle: 40f));

        var off = world.CreateEntity();
        var disabled = LaserEmitterComponent.CreateBeam();
        disabled.Enabled = false;
        world.AddComponent(off, new TransformComponent());
        world.AddComponent(off, disabled);

        lasers.Update(0.2f);

        var segments = world.Query<LaserSegmentComponent>().ToList();
        Assert.NotEmpty(segments);
        var first = world.GetComponent<LaserSegmentComponent>(segments[0]);
        Assert.True(first.End != first.Start || first.Width >= 0f);

        var pulses = world.Query<LaserPulseComponent>().ToList();
        Assert.NotEmpty(pulses);

        lasers.SetEmitterEnabled(beam, false);
        Assert.False(world.GetComponent<LaserEmitterComponent>(beam).Enabled);
        lasers.FirePulse(pulse);
        lasers.Update(0.05f);
        Assert.True(world.Query<LaserSegmentComponent>().Any());
    }
}
