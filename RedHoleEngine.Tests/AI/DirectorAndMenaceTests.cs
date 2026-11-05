using System.Numerics;
using RedHoleEngine.AI;
using RedHoleEngine.AI.Director;
using RedHoleEngine.AI.Perception;
using RedHoleEngine.Components;
using RedHoleEngine.Core.ECS;

namespace RedHoleEngine.Tests.AI;

public class DirectorAndMenaceTests
{
    private readonly DirectorConfig _config = new();

    [Fact]
    public void Menace_RisesNearTheSubject_AndFasterWithContact()
    {
        var near = new MenaceStimulus(true, Distance: 0f, VisualContact: false, Backstage: false);
        var contact = near with { VisualContact = true };
        var far = near with { Distance = 100f };

        float risen = MenaceModel.Step(0f, 1f, near, _config);
        float risenContact = MenaceModel.Step(0f, 1f, contact, _config);
        float fallen = MenaceModel.Step(0.5f, 1f, far, _config);

        Assert.Equal(_config.NearRisePerSecond, MenaceModel.Rate(near, _config));
        Assert.Equal(_config.NearRisePerSecond + _config.ContactRisePerSecond, MenaceModel.Rate(contact, _config));
        Assert.True(risenContact > risen);
        Assert.True(fallen < 0.5f);
        Assert.Equal(-_config.FarFallPerSecond, MenaceModel.Rate(far, _config));
    }

    [Fact]
    public void Menace_FallsWhileBackstage_EvenAtPointBlankContact()
    {
        var stimulus = new MenaceStimulus(true, 0f, VisualContact: true, Backstage: true);
        Assert.Equal(-_config.BackstageFallPerSecond, MenaceModel.Rate(stimulus, _config));
        Assert.True(MenaceModel.Step(0.9f, 1f, stimulus, _config) < 0.9f);
    }

    [Fact]
    public void Phase_Hysteresis_DoesNotFlipBackImmediately()
    {
        var phase = DirectorPhase.Frontstage;
        phase = MenaceModel.NextPhase(phase, 0.79f, _config);
        Assert.Equal(DirectorPhase.Frontstage, phase);
        phase = MenaceModel.NextPhase(phase, _config.BackstageThreshold, _config);
        Assert.Equal(DirectorPhase.Backstage, phase);

        phase = MenaceModel.NextPhase(phase, 0.5f, _config);
        Assert.Equal(DirectorPhase.Backstage, phase);
        phase = MenaceModel.NextPhase(phase, _config.FrontstageReenterThreshold, _config);
        Assert.Equal(DirectorPhase.Frontstage, phase);
    }

    [Fact]
    public void Phase_RejectsThresholdsWithoutAGap()
    {
        var config = new DirectorConfig
        {
            BackstageThreshold = 0.4f,
            FrontstageReenterThreshold = 0.4f
        };
        Assert.Throws<InvalidOperationException>(() => MenaceModel.NextPhase(DirectorPhase.Frontstage, 0f, config));
    }

    [Fact]
    public void Hints_AreOffsetFromTheSubject_AndWithdrawMovesAway()
    {
        var subject = Vector3.Zero;
        var stalker = new Vector3(10f, 0f, 0f);
        var search = MenaceModel.SearchAreaCenter(subject, stalker, radius: 8f);
        var withdraw = MenaceModel.WithdrawPoint(stalker, subject, distance: 30f);

        Assert.NotEqual(subject, search);
        Assert.Equal(new Vector3(0f, 0f, 2f), search);
        Assert.Equal(new Vector3(40f, 0f, 0f), withdraw);
    }

    [Fact]
    public void System_RaisesMenaceFromTheStalkerWhoHasContact()
    {
        using var world = new World();
        var director = world.AddSystem(new DirectorSystem());
        world.AddSystem(new PerceptionSystem());
        AddSubject(world, Vector3.Zero);

        var far = AddStalker(world, new Vector3(100f, 0f, 0f), Vector3.UnitX);
        var close = AddStalker(world, new Vector3(4f, 0f, 0f), -Vector3.UnitX);

        world.Update(1f);

        Assert.Equal(close, director.FocusStalker);
        Assert.True(world.GetComponent<SensoryComponent>(close).HasVisualContact);
        Assert.False(world.GetComponent<SensoryComponent>(far).HasVisualContact);
        Assert.True(director.Menace > 0f);
        Assert.Equal(DirectorPhase.Frontstage, director.Phase);

        var board = world.GetComponent<BehaviorAgentComponent>(close).Blackboard!;
        Assert.Equal(director.Menace, board.Get<float>(BlackboardKeys.Menace));
        Assert.Equal(DirectorPhase.Frontstage, board.Get<DirectorPhase>(BlackboardKeys.DirectorPhase));
        Assert.True(board.Get<bool>(BlackboardKeys.HasVisualContact));
    }

    [Fact]
    public void System_SearchHintDoesNotGrantVision_AndStaysPutBetweenIntervals()
    {
        using var world = new World();
        world.AddSystem(new PerceptionSystem());
        var director = world.AddSystem(new DirectorSystem());
        director.Config.HintInterval = 10f;

        var subject = AddSubject(world, Vector3.Zero);
        var stalker = AddStalker(world, new Vector3(10f, 0f, 0f), Vector3.UnitX);

        world.Update(0.1f);
        var first = world.GetComponent<DirectorStalkerComponent>(stalker);
        Assert.Equal(DirectorHintKind.SearchArea, first.HintKind);
        Assert.NotEqual(Vector3.Zero, first.HintPosition);
        Assert.False(world.GetComponent<SensoryComponent>(stalker).HasVisualContact);

        var board = world.GetComponent<BehaviorAgentComponent>(stalker).Blackboard!;
        Assert.Equal(first.HintPosition, board.Get<Vector3>(BlackboardKeys.HintPosition));
        Assert.Equal(DirectorHintKind.SearchArea, board.Get<DirectorHintKind>(BlackboardKeys.HintKind));
        Assert.False(board.Get<bool>(BlackboardKeys.HasVisualContact));

        world.GetComponent<TransformComponent>(subject).Position = new Vector3(0f, 0f, 40f);
        world.Update(1f);
        Assert.Equal(first.HintPosition, world.GetComponent<DirectorStalkerComponent>(stalker).HintPosition);

        world.Update(10f);
        Assert.NotEqual(first.HintPosition, world.GetComponent<DirectorStalkerComponent>(stalker).HintPosition);
    }

    [Fact]
    public void System_BackstageWritesAWithdrawHintAndSpendsMenace()
    {
        using var world = new World();
        world.AddSystem(new PerceptionSystem());
        var director = world.AddSystem(new DirectorSystem());
        AddSubject(world, Vector3.Zero);
        var stalker = AddStalker(world, new Vector3(10f, 0f, 0f), -Vector3.UnitX);

        director.SetMenace(1f);
        Assert.Equal(DirectorPhase.Backstage, director.Phase);
        float before = director.Menace;
        world.Update(1f);

        var hint = world.GetComponent<DirectorStalkerComponent>(stalker);
        Assert.Equal(DirectorHintKind.Withdraw, hint.HintKind);
        Assert.Equal(new Vector3(40f, 0f, 0f), hint.HintPosition);
        Assert.True(director.Menace < before);
        Assert.True(world.GetComponent<SensoryComponent>(stalker).HasVisualContact);

        var board = world.GetComponent<BehaviorAgentComponent>(stalker).Blackboard!;
        Assert.Equal(DirectorPhase.Backstage, board.Get<DirectorPhase>(BlackboardKeys.DirectorPhase));
        Assert.True(board.Get<bool>(BlackboardKeys.HasVisualContact));
    }

    [Fact]
    public void System_WithoutASubject_ClearsHintsAndLetsMenaceFall()
    {
        using var world = new World();
        var director = world.AddSystem(new DirectorSystem());
        var stalker = AddStalker(world, Vector3.Zero, Vector3.UnitX);
        director.SetMenace(0.5f);

        world.Update(1f);

        Assert.Equal(DirectorHintKind.None, world.GetComponent<DirectorStalkerComponent>(stalker).HintKind);
        Assert.True(director.Menace < 0.5f);
        Assert.Equal(Entity.Null, director.FocusStalker);
    }

    private static Entity AddSubject(World world, Vector3 position)
    {
        var entity = world.CreateEntity();
        world.AddComponent(entity, new TransformComponent(position));
        world.AddComponent(entity, new DirectorSubjectComponent());
        return entity;
    }

    private static Entity AddStalker(World world, Vector3 position, Vector3 facing)
    {
        var entity = world.CreateEntity();
        world.AddComponent(entity, new TransformComponent(position));
        var sensor = SensoryComponent.Create(viewDistance: 20f, viewAngleDegrees: 70f);
        sensor.Facing = facing;
        world.AddComponent(entity, sensor);
        world.AddComponent(entity, new DirectorStalkerComponent());
        world.AddComponent(entity, new BehaviorAgentComponent
        {
            Blackboard = new RedHoleEngine.AI.BehaviorTrees.Blackboard(),
            Enabled = true
        });
        return entity;
    }
}
