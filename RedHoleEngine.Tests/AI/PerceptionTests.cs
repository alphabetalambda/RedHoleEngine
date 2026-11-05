using System.Numerics;
using RedHoleEngine.AI;
using RedHoleEngine.AI.Perception;
using RedHoleEngine.Components;
using RedHoleEngine.Core.ECS;
using RedHoleEngine.AI.Sample;

namespace RedHoleEngine.Tests.AI;

public class PerceptionTests
{
    [Fact]
    public void InViewCone_AcceptsTargetsInsideAndRejectsAngleAndDistance()
    {
        var origin = Vector3.Zero;
        var forward = Vector3.UnitX;

        Assert.True(SensoryMath.InViewCone(origin, forward, new Vector3(5f, 0f, 0f), 10f, 90f));
        Assert.False(SensoryMath.InViewCone(origin, forward, new Vector3(0f, 0f, 5f), 10f, 90f));
        Assert.False(SensoryMath.InViewCone(origin, forward, new Vector3(15f, 0f, 0f), 10f, 90f));
        Assert.True(SensoryMath.InViewCone(origin, forward, new Vector3(10f, 0f, 0f), 10f, 90f));
    }

    [Fact]
    public void CanHear_UsesIntensityAsRadius()
    {
        Assert.True(SensoryMath.CanHear(Vector3.Zero, new Vector3(4f, 0f, 0f), intensity: 5f, hearingSensitivity: 1f));
        Assert.False(SensoryMath.CanHear(Vector3.Zero, new Vector3(6f, 0f, 0f), intensity: 5f, hearingSensitivity: 1f));
        Assert.True(SensoryMath.CanHear(Vector3.Zero, new Vector3(6f, 0f, 0f), intensity: 5f, hearingSensitivity: 2f));
        Assert.False(SensoryMath.CanHear(Vector3.Zero, Vector3.Zero, intensity: 0f, hearingSensitivity: 1f));
    }

    [Fact]
    public void System_SeesATargetAlongTheViewCone_AndIgnoresOneOutsideIt()
    {
        using var world = new World();
        world.AddSystem(new PerceptionSystem());
        var target = world.CreateEntity();
        world.AddComponent(target, new TransformComponent(new Vector3(0f, 0f, -5f)));

        var seen = world.CreateEntity();
        var sensor = SensoryComponent.Create(viewDistance: 20f, viewAngleDegrees: 70f);
        sensor.Facing = -Vector3.UnitZ;
        sensor.StimulusSource = target;
        world.AddComponent(seen, new TransformComponent(Vector3.Zero));
        world.AddComponent(seen, sensor);

        world.Update(0.1f);
        Assert.True(world.GetComponent<SensoryComponent>(seen).HasVisualContact);

        ref var facingAway = ref world.GetComponent<SensoryComponent>(seen);
        facingAway.Facing = Vector3.UnitZ;
        world.Update(0.1f);
        ref var missed = ref world.GetComponent<SensoryComponent>(seen);
        Assert.False(missed.HasVisualContact);
        Assert.True(missed.TimeSinceSeen > 0f);
    }

    [Fact]
    public void System_UsesTransformForwardWhenFacingIsUnset()
    {
        using var world = new World();
        world.AddSystem(new PerceptionSystem());
        var watcher = world.CreateEntity();
        var watcherTransform = new TransformComponent(new Vector3(0f, 0f, 4f));
        var target = world.CreateEntity();
        world.AddComponent(target, new TransformComponent(watcherTransform.Position + watcherTransform.Forward * 3f));

        var sensor = SensoryComponent.Create(viewDistance: 20f, viewAngleDegrees: 50f);
        sensor.StimulusSource = target;
        world.AddComponent(watcher, watcherTransform);
        world.AddComponent(watcher, sensor);

        world.Update(0.016f);

        Assert.True(world.GetComponent<SensoryComponent>(watcher).HasVisualContact);
    }

    [Fact]
    public void System_OcclusionBlocksVision()
    {
        using var world = new World();
        var perception = world.AddSystem(new PerceptionSystem());
        perception.Vision = new BlockAllVision();

        var target = world.CreateEntity();
        world.AddComponent(target, new TransformComponent(new Vector3(2f, 0f, 0f)));
        var watcher = world.CreateEntity();
        var sensor = SensoryComponent.Create();
        sensor.Facing = Vector3.UnitX;
        sensor.StimulusSource = target;
        world.AddComponent(watcher, new TransformComponent(Vector3.Zero));
        world.AddComponent(watcher, sensor);

        world.Update(0.1f);

        Assert.False(world.GetComponent<SensoryComponent>(watcher).HasVisualContact);
    }

    [Fact]
    public void System_HearsTheLouderInRangeNoise_AndCopiesItToTheBlackboard()
    {
        using var world = new World();
        var perception = world.AddSystem(new PerceptionSystem());
        var agent = world.CreateEntity();
        var sensor = SensoryComponent.Create();
        sensor.Position = Vector3.Zero;
        world.AddComponent(agent, sensor);
        world.AddComponent(agent, StalkerTree.CreateAgent());

        perception.EmitNoise(new Vector3(1f, 0f, 0f), intensity: 4f, tag: "footstep");
        var loud = perception.EmitNoise(new Vector3(3f, 0f, 0f), intensity: 10f, tag: "gunshot");
        perception.EmitNoise(new Vector3(30f, 0f, 0f), intensity: 2f, tag: "far");

        world.Update(0.1f);

        ref var sensed = ref world.GetComponent<SensoryComponent>(agent);
        Assert.True(sensed.HeardNoise);
        Assert.Equal(loud.Id, sensed.HeardNoiseId);
        Assert.Equal(10f, sensed.LastHeardIntensity);

        var board = world.GetComponent<BehaviorAgentComponent>(agent).Blackboard!;
        Assert.True(board.Get<bool>(BlackboardKeys.HeardNoise));
        Assert.Equal(loud.Id, board.Get<int>(BlackboardKeys.NoiseId));
        Assert.Equal(loud.Position, board.Get<Vector3>(BlackboardKeys.NoisePosition));
        Assert.False(board.Get<bool>(BlackboardKeys.HasVisualContact));
    }

    [Fact]
    public void System_IgnoresANoiseTheAgentAlreadyConsumed()
    {
        using var world = new World();
        var perception = world.AddSystem(new PerceptionSystem());
        var agent = world.CreateEntity();
        var sensor = SensoryComponent.Create();
        world.AddComponent(agent, sensor);
        world.AddComponent(agent, StalkerTree.CreateAgent());
        world.GetComponent<BehaviorAgentComponent>(agent).Blackboard!.Set(BlackboardKeys.ConsumedNoiseId, 0);

        var first = perception.EmitNoise(Vector3.Zero, intensity: 5f);
        world.Update(0.1f);
        Assert.Equal(first.Id, world.GetComponent<SensoryComponent>(agent).HeardNoiseId);

        world.GetComponent<BehaviorAgentComponent>(agent).Blackboard!.Set(BlackboardKeys.ConsumedNoiseId, first.Id);
        world.Update(0.1f);
        Assert.False(world.GetComponent<SensoryComponent>(agent).HeardNoise);

        var second = perception.EmitNoise(Vector3.Zero, intensity: 5f);
        world.Update(0.1f);
        Assert.True(world.GetComponent<SensoryComponent>(agent).HeardNoise);
        Assert.Equal(second.Id, world.GetComponent<SensoryComponent>(agent).HeardNoiseId);
    }

    private sealed class BlockAllVision : IVisionQuery
    {
        public bool IsOccluded(Vector3 from, Vector3 to) => true;
    }
}
