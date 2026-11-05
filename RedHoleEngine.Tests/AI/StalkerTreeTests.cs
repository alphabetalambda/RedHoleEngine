using System.Numerics;
using RedHoleEngine.AI;
using RedHoleEngine.AI.BehaviorTrees;
using RedHoleEngine.AI.Director;
using RedHoleEngine.AI.Perception;
using RedHoleEngine.AI.Sample;
using RedHoleEngine.Components;
using RedHoleEngine.Core.ECS;

namespace RedHoleEngine.Tests.AI;

public class StalkerTreeTests
{
    [Fact]
    public void WithNoStimulus_Patrols()
    {
        using var world = CreateWorld();
        var stalker = AddStalker(world, new Vector3(0f, 0f, 8f), -Vector3.UnitZ, recordTrace: true);

        world.Update(0.1f);

        var agent = world.GetComponent<BehaviorAgentComponent>(stalker);
        Assert.Equal(AgentIntent.Patrol, agent.Blackboard!.Get<AgentIntent>(BlackboardKeys.Intent));
        Assert.Equal(NodeStatus.Success, agent.LastStatus);
        Assert.Contains("action patrol -> Success", agent.LastTrace);
    }

    [Fact]
    public void Noise_IsInvestigated_ThenTheLastKnownPositionIsSearched()
    {
        using var world = CreateWorld(out var perception, out _);
        var stalker = AddStalker(world, Vector3.Zero, Vector3.UnitX);
        var noise = perception.EmitNoise(new Vector3(1f, 0f, 0f), intensity: 6f, tag: "clank");

        world.Update(0.4f);
        Assert.Equal(AgentIntent.Investigate, Intent(world, stalker));
        Assert.Equal(NodeStatus.Running, world.GetComponent<BehaviorAgentComponent>(stalker).LastStatus);

        world.Update(0.4f);
        Assert.Equal(NodeStatus.Success, world.GetComponent<BehaviorAgentComponent>(stalker).LastStatus);
        Assert.False(world.GetComponent<BehaviorAgentComponent>(stalker).Blackboard!.Get<bool>(BlackboardKeys.HeardNoise));
        Assert.Equal(noise.Id, world.GetComponent<BehaviorAgentComponent>(stalker).Blackboard!.Get<int>(BlackboardKeys.ConsumedNoiseId));

        world.Update(0.1f);
        Assert.Equal(AgentIntent.Search, Intent(world, stalker));
        Assert.Equal(noise.Position, world.GetComponent<BehaviorAgentComponent>(stalker).Blackboard!.Get<Vector3>(BlackboardKeys.IntentPosition));
        Assert.False(world.GetComponent<BehaviorAgentComponent>(stalker).Blackboard!.Get<bool>(BlackboardKeys.HasVisualContact));
    }

    [Fact]
    public void VisualContact_Hunts_AndBeatsAConcurrentNoise()
    {
        using var world = CreateWorld(out var perception, out _);
        AddSubject(world, Vector3.Zero);
        var stalker = AddStalker(world, new Vector3(0f, 0f, 8f), -Vector3.UnitZ);
        perception.EmitNoise(new Vector3(0f, 0f, 8f), intensity: 10f);

        world.Update(0.1f);

        Assert.True(world.GetComponent<SensoryComponent>(stalker).HasVisualContact);
        Assert.Equal(AgentIntent.Hunt, Intent(world, stalker));
        Assert.Equal(Vector3.Zero, world.GetComponent<BehaviorAgentComponent>(stalker).Blackboard!.Get<Vector3>(BlackboardKeys.IntentPosition));
    }

    [Fact]
    public void DirectorSearchHint_SteersSearchWithoutAuthorizingAHunt()
    {
        using var world = CreateWorld();
        AddSubject(world, Vector3.Zero);
        var stalker = AddStalker(world, new Vector3(10f, 0f, 0f), Vector3.UnitX);

        world.Update(0.1f);

        var board = world.GetComponent<BehaviorAgentComponent>(stalker).Blackboard!;
        Assert.False(board.Get<bool>(BlackboardKeys.HasVisualContact));
        Assert.Equal(AgentIntent.Search, board.Get<AgentIntent>(BlackboardKeys.Intent));
        Assert.Equal(DirectorHintKind.SearchArea, board.Get<DirectorHintKind>(BlackboardKeys.HintKind));
        Assert.NotEqual(Vector3.Zero, board.Get<Vector3>(BlackboardKeys.HintPosition));
        Assert.Equal(board.Get<Vector3>(BlackboardKeys.HintPosition), board.Get<Vector3>(BlackboardKeys.IntentPosition));
    }

    [Fact]
    public void HighMenace_RetreatsEvenWhileTheStalkerCanStillSee()
    {
        using var world = CreateWorld(out _, out var director);
        AddSubject(world, Vector3.Zero);
        var stalker = AddStalker(world, new Vector3(0f, 0f, 8f), -Vector3.UnitZ);

        world.Update(0.1f);
        Assert.Equal(AgentIntent.Hunt, Intent(world, stalker));

        director.SetMenace(1f);
        world.Update(0.1f);

        var board = world.GetComponent<BehaviorAgentComponent>(stalker).Blackboard!;
        Assert.True(board.Get<bool>(BlackboardKeys.HasVisualContact));
        Assert.Equal(DirectorPhase.Backstage, board.Get<DirectorPhase>(BlackboardKeys.DirectorPhase));
        Assert.Equal(AgentIntent.Retreat, board.Get<AgentIntent>(BlackboardKeys.Intent));
        Assert.Equal(DirectorHintKind.Withdraw, board.Get<DirectorHintKind>(BlackboardKeys.HintKind));
        Assert.Equal(board.Get<Vector3>(BlackboardKeys.HintPosition), board.Get<Vector3>(BlackboardKeys.IntentPosition));
    }

    [Fact]
    public void LockedHunt_FallsThroughToSearch()
    {
        using var world = CreateWorld();
        AddSubject(world, Vector3.Zero);
        var stalker = AddStalker(world, new Vector3(0f, 0f, 8f), -Vector3.UnitZ);
        world.GetComponent<BehaviorAgentComponent>(stalker).Gates!.Lock(StalkerGates.Hunt);

        world.Update(0.1f);

        var board = world.GetComponent<BehaviorAgentComponent>(stalker).Blackboard!;
        Assert.True(board.Get<bool>(BlackboardKeys.HasVisualContact));
        Assert.Equal(AgentIntent.Search, board.Get<AgentIntent>(BlackboardKeys.Intent));
    }

    [Fact]
    public void LockedInvestigate_FallsThroughToSearchingTheNoise()
    {
        using var world = CreateWorld(out var perception, out _);
        var stalker = AddStalker(world, Vector3.Zero, Vector3.UnitX);
        world.GetComponent<BehaviorAgentComponent>(stalker).Gates!.Lock(StalkerGates.Investigate);
        perception.EmitNoise(new Vector3(2f, 0f, 0f), intensity: 8f);

        world.Update(0.1f);

        Assert.Equal(AgentIntent.Search, Intent(world, stalker));
        Assert.Equal(new Vector3(2f, 0f, 0f), world.GetComponent<BehaviorAgentComponent>(stalker).Blackboard!.Get<Vector3>(BlackboardKeys.IntentPosition));
    }

    [Fact]
    public void UnlockingAmbush_TakesPriorityOverHunt()
    {
        using var world = CreateWorld();
        AddSubject(world, Vector3.Zero);
        var stalker = AddStalker(world, new Vector3(0f, 0f, 8f), -Vector3.UnitZ);
        ref var agent = ref world.GetComponent<BehaviorAgentComponent>(stalker);
        agent.Gates!.Unlock(StalkerGates.Ambush);
        agent.Blackboard!.Set(BlackboardKeys.PreferAmbush, true);

        world.Update(0.1f);

        Assert.Equal(AgentIntent.Ambush, Intent(world, stalker));
    }

    [Fact]
    public void DisabledAgent_IsNotTicked()
    {
        using var world = CreateWorld();
        var stalker = AddStalker(world, Vector3.Zero, Vector3.UnitX);
        world.GetComponent<BehaviorAgentComponent>(stalker).Enabled = false;

        world.Update(0.1f);

        Assert.False(world.GetComponent<BehaviorAgentComponent>(stalker).Blackboard!.Has(BlackboardKeys.Intent));
    }

    [Fact]
    public void JsonSample_CompilesToTheSameDecision()
    {
        var json = StalkerTree.CreateJson();
        var tree = BehaviorTreeDocument.FromJson(json).Compile(StalkerBehaviors.SharedRegistry);
        var board = new Blackboard();
        board.Set(BlackboardKeys.HasVisualContact, true);
        board.Set(BlackboardKeys.LastSeenPosition, new Vector3(1f, 2f, 3f));
        board.Set(BlackboardKeys.HeardNoise, true);
        board.Set(BlackboardKeys.NoiseId, 4);

        var status = tree.Tick(board, StalkerGates.CreateDefaultSet(), deltaTime: 0.1f);

        Assert.Equal(NodeStatus.Running, status);
        Assert.Equal(AgentIntent.Hunt, board.Get<AgentIntent>(BlackboardKeys.Intent));
        Assert.Contains("\"gate\": \"ambush\"", json);
        Assert.Contains("\"action\": \"stalker.patrol\"", json);
    }

    private static World CreateWorld()
    {
        return CreateWorld(out _, out _);
    }

    private static World CreateWorld(out PerceptionSystem perception, out DirectorSystem director)
    {
        var world = new World();
        perception = world.AddSystem(new PerceptionSystem());
        director = world.AddSystem(new DirectorSystem());
        world.AddSystem(new BehaviorTreeSystem());
        return world;
    }

    private static Entity AddSubject(World world, Vector3 position)
    {
        var entity = world.CreateEntity();
        world.AddComponent(entity, new TransformComponent(position));
        world.AddComponent(entity, new DirectorSubjectComponent());
        return entity;
    }

    private static Entity AddStalker(World world, Vector3 position, Vector3 facing, bool recordTrace = false)
    {
        var entity = world.CreateEntity();
        world.AddComponent(entity, new TransformComponent(position));
        var sensor = SensoryComponent.Create();
        sensor.Facing = facing;
        world.AddComponent(entity, sensor);
        world.AddComponent(entity, new DirectorStalkerComponent());
        world.AddComponent(entity, StalkerTree.CreateAgent(recordTrace));
        return entity;
    }

    private static AgentIntent Intent(World world, Entity entity)
    {
        return world.GetComponent<BehaviorAgentComponent>(entity).Blackboard!.Get<AgentIntent>(BlackboardKeys.Intent);
    }
}
