using System.Numerics;
using RedHoleEngine.AI;
using RedHoleEngine.AI.BehaviorTrees;
using RedHoleEngine.AI.Navigation;
using RedHoleEngine.AI.Perception;
using RedHoleEngine.AI.Sample;
using RedHoleEngine.Components;
using RedHoleEngine.Core.ECS;
using RedHoleEngine.Physics;

namespace RedHoleEngine.Tests.AI;

internal static class NavTestWorld
{
    public const float Step = 1f / 60f;

    /// <summary>20x20 open grid of 1m cells with its corner at the origin.</summary>
    public static NavGrid OpenGrid() => new(Vector3.Zero, 20, 20, 1f);

    /// <summary>Same grid with a wall along x = 10 and one opening at z = 17.</summary>
    public static NavGrid WalledGrid()
    {
        var grid = OpenGrid();
        for (int z = 0; z < 20; z++)
            grid.SetWalkable(10, z, z == 17);
        return grid;
    }

    public static World Create(NavGrid? grid, out NavigationSystem navigation, bool behaviorTrees = false)
    {
        var world = new World();
        if (behaviorTrees)
            world.AddSystem(new BehaviorTreeSystem());
        navigation = world.AddSystem(new NavigationSystem { Grid = grid });
        return world;
    }

    public static Entity AddAgent(World world, Vector3 position, float speed = 4f)
    {
        var entity = world.CreateEntity();
        world.AddComponent(entity, new TransformComponent(position));
        world.AddComponent(entity, NavAgentComponent.Create(speed));
        return entity;
    }

    public static Entity AddTreeAgent(World world, Vector3 position, BehaviorTree tree, float speed = 4f)
    {
        var entity = AddAgent(world, position, speed);
        world.AddComponent(entity, BehaviorAgentComponent.Create(tree));
        return entity;
    }

    public static Vector3 Position(World world, Entity entity)
        => world.GetComponent<TransformComponent>(entity).Position;

    public static NavAgentComponent Agent(World world, Entity entity)
        => world.GetComponent<NavAgentComponent>(entity);

    public static NodeStatus TreeStatus(World world, Entity entity)
        => world.GetComponent<BehaviorAgentComponent>(entity).LastStatus;

    public static Blackboard Board(World world, Entity entity)
        => world.GetComponent<BehaviorAgentComponent>(entity).Blackboard!;

    /// <summary>Steps the world until the predicate holds. Returns false on timeout.</summary>
    public static bool RunUntil(World world, Func<bool> done, float seconds = 30f, Action? eachFrame = null)
    {
        int frames = (int)(seconds / Step);
        for (int i = 0; i < frames; i++)
        {
            world.Update(Step);
            eachFrame?.Invoke();
            if (done())
                return true;
        }

        return false;
    }

    public static float PlanarDistance(Vector3 a, Vector3 b)
        => new Vector2(a.X - b.X, a.Z - b.Z).Length();
}

public class NavigationSystemTests
{
    [Fact]
    public void Agent_WalksToItsDestination_AndArrivesWithinStoppingDistance()
    {
        using var world = NavTestWorld.Create(NavTestWorld.OpenGrid(), out var navigation);
        var entity = NavTestWorld.AddAgent(world, new Vector3(2.5f, 0f, 2.5f));
        var destination = new Vector3(15.5f, 0f, 9.5f);

        Assert.True(navigation.SetDestination(entity, destination));
        Assert.Equal(NavAgentStatus.Moving, NavTestWorld.Agent(world, entity).Status);

        Assert.True(NavTestWorld.RunUntil(world, () => NavTestWorld.Agent(world, entity).Status == NavAgentStatus.Arrived));

        var agent = NavTestWorld.Agent(world, entity);
        Assert.True(NavTestWorld.PlanarDistance(NavTestWorld.Position(world, entity), destination) <= agent.StoppingDistance + 1e-3f);
        Assert.Equal(Vector3.Zero, agent.Velocity);
    }

    [Fact]
    public void Agent_GoesAroundAWall_WithoutEverLeavingWalkableCells()
    {
        var grid = NavTestWorld.WalledGrid();
        using var world = NavTestWorld.Create(grid, out _);
        var entity = NavTestWorld.AddAgent(world, new Vector3(2.5f, 0f, 2.5f));
        var destination = new Vector3(17.5f, 0f, 2.5f);
        world.GetComponent<NavAgentComponent>(entity).SetDestination(destination);

        bool crossedGap = false;
        bool arrived = NavTestWorld.RunUntil(
            world,
            () => NavTestWorld.Agent(world, entity).Status == NavAgentStatus.Arrived,
            eachFrame: () =>
            {
                var position = NavTestWorld.Position(world, entity);
                Assert.True(grid.IsWalkable(position), $"agent stepped onto a blocked cell at {position}");
                grid.WorldToCell(position, out int x, out int z);
                crossedGap |= x == 10 && z == 17;
            });

        Assert.True(arrived);
        Assert.True(crossedGap);
        Assert.True(NavTestWorld.PlanarDistance(NavTestWorld.Position(world, entity), destination) <= 0.11f);
    }

    [Fact]
    public void Agent_AcceleratesFromRest_AndNeverExceedsItsSpeed()
    {
        using var world = NavTestWorld.Create(NavTestWorld.OpenGrid(), out var navigation);
        var entity = NavTestWorld.AddAgent(world, new Vector3(1.5f, 0f, 1.5f), speed: 3f);
        ref var config = ref world.GetComponent<NavAgentComponent>(entity);
        config.Acceleration = 6f;
        navigation.SetDestination(entity, new Vector3(18.5f, 0f, 1.5f));

        world.Update(NavTestWorld.Step);
        float first = NavTestWorld.Agent(world, entity).Velocity.Length();
        Assert.InRange(first, 0.05f, 6f * NavTestWorld.Step + 1e-4f);

        float top = 0f;
        NavTestWorld.RunUntil(world, () => false, seconds: 2f,
            eachFrame: () => top = MathF.Max(top, NavTestWorld.Agent(world, entity).Velocity.Length()));

        Assert.InRange(top, 2.99f, 3.0001f);
    }

    [Fact]
    public void Agent_BrakesBeforeTheGoal_InsteadOfOvershooting()
    {
        using var world = NavTestWorld.Create(NavTestWorld.OpenGrid(), out var navigation);
        var entity = NavTestWorld.AddAgent(world, new Vector3(1.5f, 0f, 5.5f), speed: 5f);
        world.GetComponent<NavAgentComponent>(entity).Acceleration = 5f;
        navigation.SetDestination(entity, new Vector3(11.5f, 0f, 5.5f));

        float furthest = 0f;
        float speedNearGoal = float.MaxValue;
        Assert.True(NavTestWorld.RunUntil(
            world,
            () => NavTestWorld.Agent(world, entity).Status == NavAgentStatus.Arrived,
            eachFrame: () =>
            {
                float x = NavTestWorld.Position(world, entity).X;
                furthest = MathF.Max(furthest, x);
                if (x > 11.2f && NavTestWorld.Agent(world, entity).Status == NavAgentStatus.Moving)
                    speedNearGoal = MathF.Min(speedNearGoal, NavTestWorld.Agent(world, entity).Velocity.Length());
            }));

        Assert.True(furthest <= 11.5f + 1e-3f, $"overshot to x = {furthest}");
        Assert.True(speedNearGoal < 2.5f, $"still doing {speedNearGoal} m/s next to the goal");
    }

    [Fact]
    public void UnreachableDestination_IsReported_AndTheAgentStaysPut()
    {
        var grid = NavTestWorld.OpenGrid();
        for (int z = 0; z < 20; z++)
            grid.SetWalkable(10, z, false);
        using var world = NavTestWorld.Create(grid, out var navigation);
        var start = new Vector3(2.5f, 0f, 2.5f);
        var entity = NavTestWorld.AddAgent(world, start);

        Assert.False(navigation.SetDestination(entity, new Vector3(17.5f, 0f, 2.5f)));
        NavTestWorld.RunUntil(world, () => false, seconds: 0.5f);

        Assert.Equal(NavAgentStatus.Unreachable, NavTestWorld.Agent(world, entity).Status);
        Assert.Equal(start, NavTestWorld.Position(world, entity));
    }

    [Fact]
    public void WithoutAGrid_AnOrderIsUnreachable_UntilAGridIsAssigned()
    {
        using var world = NavTestWorld.Create(null, out var navigation);
        var entity = NavTestWorld.AddAgent(world, new Vector3(2.5f, 0f, 2.5f));
        world.GetComponent<NavAgentComponent>(entity).SetDestination(new Vector3(8.5f, 0f, 2.5f));

        world.Update(NavTestWorld.Step);
        Assert.Equal(NavAgentStatus.Unreachable, NavTestWorld.Agent(world, entity).Status);

        navigation.Grid = NavTestWorld.OpenGrid();
        Assert.True(NavTestWorld.RunUntil(world, () => NavTestWorld.Agent(world, entity).Status == NavAgentStatus.Arrived));
    }

    [Fact]
    public void EditingTheGrid_ReplansAPathInProgress()
    {
        var grid = NavTestWorld.OpenGrid();
        using var world = NavTestWorld.Create(grid, out var navigation);
        var entity = NavTestWorld.AddAgent(world, new Vector3(2.5f, 0f, 10.5f));
        var destination = new Vector3(17.5f, 0f, 10.5f);
        navigation.SetDestination(entity, destination);
        NavTestWorld.RunUntil(world, () => false, seconds: 0.5f);
        Assert.Equal(2, NavTestWorld.Agent(world, entity).Path!.Count);

        // Drop a wall across the straight line, leaving a way round at the top.
        for (int z = 0; z < 18; z++)
            grid.SetWalkable(10, z, false);

        bool arrived = NavTestWorld.RunUntil(
            world,
            () => NavTestWorld.Agent(world, entity).Status == NavAgentStatus.Arrived,
            eachFrame: () => Assert.True(grid.IsWalkable(NavTestWorld.Position(world, entity))));

        Assert.True(arrived);
        Assert.True(NavTestWorld.PlanarDistance(NavTestWorld.Position(world, entity), destination) <= 0.11f);
    }

    [Fact]
    public void Stop_BrakesToAHalt_AndClearsTheOrder()
    {
        using var world = NavTestWorld.Create(NavTestWorld.OpenGrid(), out var navigation);
        var entity = NavTestWorld.AddAgent(world, new Vector3(1.5f, 0f, 1.5f));
        navigation.SetDestination(entity, new Vector3(18.5f, 0f, 1.5f));
        NavTestWorld.RunUntil(world, () => false, seconds: 1f);
        Assert.True(NavTestWorld.Agent(world, entity).Velocity.Length() > 1f);

        world.GetComponent<NavAgentComponent>(entity).Stop();
        world.Update(NavTestWorld.Step);
        Assert.True(NavTestWorld.Agent(world, entity).Velocity.Length() > 0f, "braking should take more than one frame");

        NavTestWorld.RunUntil(world, () => false, seconds: 1f);
        var agent = NavTestWorld.Agent(world, entity);
        Assert.Equal(NavAgentStatus.Idle, agent.Status);
        Assert.False(agent.HasDestination);
        Assert.Equal(Vector3.Zero, agent.Velocity);

        var resting = NavTestWorld.Position(world, entity);
        world.Update(NavTestWorld.Step);
        Assert.Equal(resting, NavTestWorld.Position(world, entity));
    }

    [Fact]
    public void DisabledAgent_IsNotMoved()
    {
        using var world = NavTestWorld.Create(NavTestWorld.OpenGrid(), out _);
        var start = new Vector3(1.5f, 0f, 1.5f);
        var entity = NavTestWorld.AddAgent(world, start);
        ref var agent = ref world.GetComponent<NavAgentComponent>(entity);
        agent.Enabled = false;
        agent.SetDestination(new Vector3(8.5f, 0f, 1.5f));

        NavTestWorld.RunUntil(world, () => false, seconds: 0.5f);

        Assert.Equal(start, NavTestWorld.Position(world, entity));
    }

    [Fact]
    public void Agent_FollowsGroundHeight_AndFacesWhereItIsGoing()
    {
        var grid = NavTestWorld.OpenGrid();
        grid.MaxStepHeight = 0.5f;
        for (int z = 0; z < 20; z++)
        {
            for (int x = 10; x < 20; x++)
                grid.SetHeight(x, z, 0.4f);
        }

        using var world = NavTestWorld.Create(grid, out var navigation);
        var entity = NavTestWorld.AddAgent(world, new Vector3(2.5f, 0f, 5.5f));
        world.GetComponent<NavAgentComponent>(entity).BaseOffset = 1f;
        navigation.SetDestination(entity, new Vector3(15.5f, 0f, 5.5f));

        NavTestWorld.RunUntil(world, () => false, seconds: 0.5f);
        var transform = world.GetComponent<TransformComponent>(entity);
        Assert.Equal(1f, transform.Position.Y, 3);
        Assert.True(Vector3.Dot(transform.Forward, Vector3.UnitX) > 0.999f, $"forward was {transform.Forward}");

        Assert.True(NavTestWorld.RunUntil(world, () => NavTestWorld.Agent(world, entity).Status == NavAgentStatus.Arrived));
        Assert.Equal(1.4f, NavTestWorld.Position(world, entity).Y, 3);
    }

    [Fact]
    public void Separation_PushesStackedAgentsApart()
    {
        using var world = NavTestWorld.Create(NavTestWorld.OpenGrid(), out var navigation);
        var start = new Vector3(2.5f, 0f, 10.5f);
        var a = NavTestWorld.AddAgent(world, start);
        var b = NavTestWorld.AddAgent(world, start);
        navigation.SetDestination(a, new Vector3(17.5f, 0f, 10.5f));
        navigation.SetDestination(b, new Vector3(17.5f, 0f, 10.5f));

        NavTestWorld.RunUntil(world, () => false, seconds: 1.5f);
        float apart = NavTestWorld.PlanarDistance(NavTestWorld.Position(world, a), NavTestWorld.Position(world, b));
        Assert.True(apart > 0.4f, $"agents are only {apart} apart");

        navigation.EnableSeparation = false;
        var c = NavTestWorld.AddAgent(world, start);
        var d = NavTestWorld.AddAgent(world, start);
        navigation.SetDestination(c, new Vector3(2.5f, 0f, 1.5f));
        navigation.SetDestination(d, new Vector3(2.5f, 0f, 1.5f));
        NavTestWorld.RunUntil(world, () => false, seconds: 1f);
        Assert.Equal(NavTestWorld.Position(world, c), NavTestWorld.Position(world, d));
    }

    [Fact]
    public void DynamicRigidBody_IsDrivenThroughItsVelocity()
    {
        using var world = new World();
        world.AddSystem<PhysicsSystem>();
        var navigation = world.AddSystem(new NavigationSystem { Grid = NavTestWorld.OpenGrid() });

        var entity = world.CreateEntity();
        world.AddComponent(entity, new TransformComponent(new Vector3(2.5f, 1f, 2.5f)));
        var body = RigidBodyComponent.CreateDynamic();
        body.UseGravity = false;
        world.AddComponent(entity, body);
        world.AddComponent(entity, ColliderComponent.CreateSphere(0.4f));
        world.AddComponent(entity, NavAgentComponent.Create(speed: 4f));

        var destination = new Vector3(12.5f, 0f, 6.5f);
        world.Update(NavTestWorld.Step); // physics registers the body
        navigation.SetDestination(entity, destination);

        world.Update(NavTestWorld.Step);
        var velocity = world.GetComponent<RigidBodyComponent>(entity).LinearVelocity;
        Assert.True(velocity.X > 0f && velocity.Z > 0f, $"body velocity was {velocity}");
        Assert.Equal(0f, velocity.Y);

        Assert.True(NavTestWorld.RunUntil(world, () => NavTestWorld.Agent(world, entity).Status == NavAgentStatus.Arrived));
        var position = NavTestWorld.Position(world, entity);
        Assert.True(NavTestWorld.PlanarDistance(position, destination) < 0.3f, $"stopped at {position}");
        Assert.Equal(1f, position.Y, 2);

        world.Update(NavTestWorld.Step);
        var resting = world.GetComponent<RigidBodyComponent>(entity).LinearVelocity;
        Assert.Equal(0f, resting.X, 3);
        Assert.Equal(0f, resting.Z, 3);
    }

    [Fact]
    public void Find_ReturnsTheWorldsNavigationSystem()
    {
        using var world = NavTestWorld.Create(null, out var navigation);
        using var empty = new World();

        Assert.Same(navigation, NavigationSystem.Find(world));
        Assert.Null(NavigationSystem.Find(empty));
    }
}

public class NavigationBehaviorTests
{
    private const string TargetKey = "test.target";

    [Fact]
    public void MoveTo_FailsWithoutAWorld_OrWithoutANavAgent()
    {
        var node = new MoveToNode("go", new Vector3(5f, 0f, 5f));
        Assert.Equal(NodeStatus.Failure, node.Tick(new BehaviorContext(new Blackboard())));

        using var world = NavTestWorld.Create(NavTestWorld.OpenGrid(), out _);
        var entity = world.CreateEntity();
        world.AddComponent(entity, new TransformComponent(Vector3.One));
        var context = new BehaviorContext(world, entity, new Blackboard(), null, NavTestWorld.Step);
        Assert.Equal(NodeStatus.Failure, node.Tick(context));
    }

    [Fact]
    public void MoveTo_BlackboardKey_FailsWhileMissing_RunsUntilArrival_ThenSucceeds()
    {
        using var world = NavTestWorld.Create(NavTestWorld.OpenGrid(), out _, behaviorTrees: true);
        var tree = new BehaviorTreeBuilder("move").MoveTo("go", TargetKey).Build();
        var entity = NavTestWorld.AddTreeAgent(world, new Vector3(2.5f, 0f, 2.5f), tree);

        world.Update(NavTestWorld.Step);
        Assert.Equal(NodeStatus.Failure, NavTestWorld.TreeStatus(world, entity));

        var target = new Vector3(9.5f, 0f, 6.5f);
        NavTestWorld.Board(world, entity).Set(TargetKey, target);
        world.Update(NavTestWorld.Step);
        Assert.Equal(NodeStatus.Running, NavTestWorld.TreeStatus(world, entity));

        Assert.True(NavTestWorld.RunUntil(world, () => NavTestWorld.TreeStatus(world, entity) != NodeStatus.Running));
        Assert.Equal(NodeStatus.Success, NavTestWorld.TreeStatus(world, entity));
        Assert.True(NavTestWorld.PlanarDistance(NavTestWorld.Position(world, entity), target) <= 0.11f);

        // Still there on the next tick: success again, no new walk.
        world.Update(NavTestWorld.Step);
        Assert.Equal(NodeStatus.Success, NavTestWorld.TreeStatus(world, entity));
    }

    [Fact]
    public void MoveTo_UnreachableTarget_FailsOnTheFirstTick()
    {
        var grid = NavTestWorld.OpenGrid();
        for (int z = 0; z < 20; z++)
            grid.SetWalkable(10, z, false);
        using var world = NavTestWorld.Create(grid, out _, behaviorTrees: true);
        var tree = new BehaviorTreeBuilder("move").MoveTo("go", new Vector3(17.5f, 0f, 2.5f)).Build();
        var start = new Vector3(2.5f, 0f, 2.5f);
        var entity = NavTestWorld.AddTreeAgent(world, start, tree);

        world.Update(NavTestWorld.Step);
        Assert.Equal(NodeStatus.Failure, NavTestWorld.TreeStatus(world, entity));

        world.Update(NavTestWorld.Step);
        Assert.Equal(NodeStatus.Failure, NavTestWorld.TreeStatus(world, entity));
        Assert.Equal(start, NavTestWorld.Position(world, entity));

        // Opening the wall makes the same order succeed without the tree doing anything new.
        grid.SetWalkable(10, 2, true);
        Assert.True(NavTestWorld.RunUntil(world, () => NavTestWorld.TreeStatus(world, entity) == NodeStatus.Success));
    }

    [Fact]
    public void MoveTo_FollowsATargetThatMoves()
    {
        using var world = NavTestWorld.Create(NavTestWorld.OpenGrid(), out _, behaviorTrees: true);
        var tree = new BehaviorTreeBuilder("move").MoveTo("go", TargetKey).Build();
        var entity = NavTestWorld.AddTreeAgent(world, new Vector3(2.5f, 0f, 2.5f), tree);
        NavTestWorld.Board(world, entity).Set(TargetKey, new Vector3(15.5f, 0f, 2.5f));
        NavTestWorld.RunUntil(world, () => false, seconds: 0.5f);

        var moved = new Vector3(4.5f, 0f, 15.5f);
        NavTestWorld.Board(world, entity).Set(TargetKey, moved);

        Assert.True(NavTestWorld.RunUntil(world, () => NavTestWorld.TreeStatus(world, entity) == NodeStatus.Success));
        Assert.True(NavTestWorld.PlanarDistance(NavTestWorld.Position(world, entity), moved) <= 0.11f);
    }

    [Fact]
    public void MoveToNode_StopsTheAgent_WhenAHigherPriorityBranchTakesOver()
    {
        using var world = NavTestWorld.Create(NavTestWorld.OpenGrid(), out _, behaviorTrees: true);
        var tree = new BehaviorTreeBuilder("preempt")
            .Selector("root")
                .Guard("alarm", context => context.Blackboard.GetOr("alarm", false))
                    .Action("freeze", _ => NodeStatus.Running)
                .End()
                .MoveTo("go", new Vector3(17.5f, 0f, 2.5f))
            .End()
            .Build();
        var entity = NavTestWorld.AddTreeAgent(world, new Vector3(2.5f, 0f, 2.5f), tree);

        NavTestWorld.RunUntil(world, () => false, seconds: 0.5f);
        Assert.Equal(NavAgentStatus.Moving, NavTestWorld.Agent(world, entity).Status);

        NavTestWorld.Board(world, entity).Set("alarm", true);
        world.Update(NavTestWorld.Step);
        Assert.False(NavTestWorld.Agent(world, entity).HasDestination);

        NavTestWorld.RunUntil(world, () => false, seconds: 1f);
        Assert.Equal(Vector3.Zero, NavTestWorld.Agent(world, entity).Velocity);
        Assert.True(NavTestWorld.Position(world, entity).X < 10f);
    }

    [Fact]
    public void MoveToNode_DoesNotCancelTheOrderOfTheBranchThatPreemptedIt()
    {
        using var world = NavTestWorld.Create(NavTestWorld.OpenGrid(), out _, behaviorTrees: true);
        var urgent = new Vector3(2.5f, 0f, 15.5f);
        var tree = new BehaviorTreeBuilder("preempt")
            .Selector("root")
                .Guard("alarm", context => context.Blackboard.GetOr("alarm", false))
                    .MoveTo("urgent", urgent)
                .End()
                .MoveTo("routine", new Vector3(17.5f, 0f, 2.5f))
            .End()
            .Build();
        var entity = NavTestWorld.AddTreeAgent(world, new Vector3(2.5f, 0f, 2.5f), tree);
        NavTestWorld.RunUntil(world, () => false, seconds: 0.5f);

        NavTestWorld.Board(world, entity).Set("alarm", true);
        world.Update(NavTestWorld.Step);

        var agent = NavTestWorld.Agent(world, entity);
        Assert.True(agent.HasDestination);
        Assert.Equal(urgent, agent.Destination);
        Assert.True(NavTestWorld.RunUntil(world, () => NavTestWorld.TreeStatus(world, entity) == NodeStatus.Success));
        Assert.True(NavTestWorld.PlanarDistance(NavTestWorld.Position(world, entity), urgent) <= 0.11f);
    }

    [Fact]
    public void Flee_RunsFromTheThreat_UntilItIsSafe()
    {
        using var world = NavTestWorld.Create(new NavGrid(Vector3.Zero, 40, 40, 1f), out _, behaviorTrees: true);
        var threat = new Vector3(18.5f, 0f, 20.5f);
        var tree = new BehaviorTreeBuilder("flee").Flee("run", "threat", fleeDistance: 6f, safeDistance: 10f).Build();
        var entity = NavTestWorld.AddTreeAgent(world, new Vector3(20.5f, 0f, 20.5f), tree);

        world.Update(NavTestWorld.Step);
        Assert.Equal(NodeStatus.Failure, NavTestWorld.TreeStatus(world, entity));

        NavTestWorld.Board(world, entity).Set("threat", threat);
        world.Update(NavTestWorld.Step);
        Assert.Equal(NodeStatus.Running, NavTestWorld.TreeStatus(world, entity));

        float previous = 0f;
        bool safe = NavTestWorld.RunUntil(
            world,
            () => NavTestWorld.TreeStatus(world, entity) == NodeStatus.Success,
            eachFrame: () =>
            {
                float distance = NavTestWorld.PlanarDistance(NavTestWorld.Position(world, entity), threat);
                Assert.True(distance >= previous - 1e-3f, "the agent moved back toward the threat");
                previous = distance;
            });

        Assert.True(safe);
        Assert.True(NavTestWorld.PlanarDistance(NavTestWorld.Position(world, entity), threat) >= 10f);
    }

    [Fact]
    public void Flee_FailsWhenCornered()
    {
        // A 3x3 room with the agent in the far corner from the threat.
        using var world = NavTestWorld.Create(new NavGrid(Vector3.Zero, 3, 3, 1f), out _, behaviorTrees: true);
        var tree = new BehaviorTreeBuilder("flee").Leaf(new FleeNode("run", new Vector3(0.5f, 0f, 0.5f), 6f, 10f)).Build();
        var entity = NavTestWorld.AddTreeAgent(world, new Vector3(2.5f, 0f, 2.5f), tree);

        world.Update(NavTestWorld.Step);

        Assert.Equal(NodeStatus.Failure, NavTestWorld.TreeStatus(world, entity));
    }

    [Fact]
    public void Patrol_VisitsEachPointInOrder_OnePointPerSuccess()
    {
        using var world = NavTestWorld.Create(NavTestWorld.OpenGrid(), out _, behaviorTrees: true);
        var points = new[] { new Vector3(6.5f, 0f, 2.5f), new Vector3(6.5f, 0f, 6.5f), new Vector3(2.5f, 0f, 6.5f) };
        var patrol = new PatrolNode("rounds", points);
        var entity = NavTestWorld.AddTreeAgent(world, new Vector3(2.5f, 0f, 2.5f), new BehaviorTree("patrol", patrol));

        for (int lap = 0; lap < 4; lap++)
        {
            int expected = lap % points.Length;
            Assert.Equal(expected, patrol.CurrentIndex);
            world.Update(NavTestWorld.Step);
            Assert.Equal(NodeStatus.Running, NavTestWorld.TreeStatus(world, entity));

            Assert.True(NavTestWorld.RunUntil(world, () => NavTestWorld.TreeStatus(world, entity) == NodeStatus.Success));
            Assert.True(NavTestWorld.PlanarDistance(NavTestWorld.Position(world, entity), points[expected]) <= 0.11f);
        }

        patrol.Reset();
        Assert.Equal(0, patrol.CurrentIndex);
    }

    [Fact]
    public void Patrol_SkipsAPointItCannotReach()
    {
        var grid = NavTestWorld.OpenGrid();
        for (int z = 0; z < 20; z++)
            grid.SetWalkable(10, z, false);
        using var world = NavTestWorld.Create(grid, out _, behaviorTrees: true);
        var patrol = new PatrolNode("rounds", new[] { new Vector3(15.5f, 0f, 2.5f), new Vector3(5.5f, 0f, 2.5f) });
        var entity = NavTestWorld.AddTreeAgent(world, new Vector3(2.5f, 0f, 2.5f), new BehaviorTree("patrol", patrol));

        world.Update(NavTestWorld.Step);
        Assert.Equal(NodeStatus.Failure, NavTestWorld.TreeStatus(world, entity));
        Assert.Equal(1, patrol.CurrentIndex);

        world.Update(NavTestWorld.Step);
        Assert.Equal(NodeStatus.Running, NavTestWorld.TreeStatus(world, entity));
    }

    [Fact]
    public void Wander_PicksReachablePointsInsideTheRadius()
    {
        var grid = NavTestWorld.WalledGrid();
        using var world = NavTestWorld.Create(grid, out _, behaviorTrees: true);
        var center = new Vector3(5.5f, 0f, 10.5f);
        var tree = new BehaviorTreeBuilder("wander").Wander("roam", radius: 4f, center: center, seed: 3).Build();
        var entity = NavTestWorld.AddTreeAgent(world, center, tree);

        for (int trip = 0; trip < 5; trip++)
        {
            world.Update(NavTestWorld.Step);
            var agent = NavTestWorld.Agent(world, entity);
            Assert.True(agent.HasDestination);
            Assert.True(NavTestWorld.PlanarDistance(agent.Destination, center) <= 4f + 1e-3f);
            Assert.True(grid.IsWalkable(agent.Destination));

            Assert.True(NavTestWorld.RunUntil(world, () => NavTestWorld.TreeStatus(world, entity) == NodeStatus.Success));
            Assert.True(NavTestWorld.PlanarDistance(NavTestWorld.Position(world, entity), agent.Destination) <= 0.11f);
        }
    }

    [Fact]
    public void RegisteredActions_WorkInAJsonTree()
    {
        const string json = """
            {
              "name": "walker",
              "root": {
                "type": "sequence",
                "name": "go-then-halt",
                "children": [
                  { "type": "action", "name": "go", "action": "nav.moveToIntent" },
                  { "type": "action", "name": "halt", "action": "nav.stop" }
                ]
              }
            }
            """;
        var registry = NavigationActions.Register(new BehaviorRegistry());
        var tree = BehaviorTreeDocument.FromJson(json).Compile(registry);

        using var world = NavTestWorld.Create(NavTestWorld.OpenGrid(), out _, behaviorTrees: true);
        var entity = NavTestWorld.AddTreeAgent(world, new Vector3(2.5f, 0f, 2.5f), tree);
        var target = new Vector3(8.5f, 0f, 4.5f);
        NavTestWorld.Board(world, entity).Set(BlackboardKeys.IntentPosition, target);

        world.Update(NavTestWorld.Step);
        Assert.Equal(NodeStatus.Running, NavTestWorld.TreeStatus(world, entity));

        Assert.True(NavTestWorld.RunUntil(world, () => NavTestWorld.TreeStatus(world, entity) == NodeStatus.Success));
        Assert.True(NavTestWorld.PlanarDistance(NavTestWorld.Position(world, entity), target) <= 0.11f);
        Assert.False(NavTestWorld.Agent(world, entity).HasDestination);

        foreach (var id in new[] { NavigationIds.MoveToLastKnown, NavigationIds.FleeLastSeen, NavigationIds.Patrol, NavigationIds.Wander })
            Assert.True(registry.ContainsAction(id), id);
    }

    [Fact]
    public void PatrolAction_KeepsItsRouteIndexOnTheBlackboard()
    {
        using var world = NavTestWorld.Create(NavTestWorld.OpenGrid(), out _, behaviorTrees: true);
        var registry = NavigationActions.Register(new BehaviorRegistry());
        var tree = new BehaviorTreeBuilder("patrol", registry).Action("rounds", NavigationIds.Patrol).Build();
        var entity = NavTestWorld.AddTreeAgent(world, new Vector3(2.5f, 0f, 2.5f), tree);

        world.Update(NavTestWorld.Step);
        Assert.Equal(NodeStatus.Failure, NavTestWorld.TreeStatus(world, entity));

        var points = new[] { new Vector3(5.5f, 0f, 2.5f), new Vector3(5.5f, 0f, 5.5f) };
        NavTestWorld.Board(world, entity).Set(NavigationKeys.PatrolPoints, points);
        Assert.True(NavTestWorld.RunUntil(world, () => NavTestWorld.TreeStatus(world, entity) == NodeStatus.Success));
        Assert.Equal(1, NavTestWorld.Board(world, entity).Get<int>(NavigationKeys.PatrolIndex));

        world.Update(NavTestWorld.Step);
        Assert.Equal(points[1], NavTestWorld.Agent(world, entity).Destination);
    }

    [Fact]
    public void SampleStalker_WithANavAgent_WalksToTheNoiseItInvestigates()
    {
        using var world = new World();
        var perception = world.AddSystem(new PerceptionSystem());
        world.AddSystem(new BehaviorTreeSystem());
        world.AddSystem(new NavigationSystem { Grid = NavTestWorld.OpenGrid() });

        var stalker = world.CreateEntity();
        var start = new Vector3(4.5f, 0f, 4.5f);
        world.AddComponent(stalker, new TransformComponent(start));
        var sensor = SensoryComponent.Create();
        sensor.Facing = Vector3.UnitX;
        world.AddComponent(stalker, sensor);
        world.AddComponent(stalker, StalkerTree.CreateAgent());
        world.AddComponent(stalker, NavAgentComponent.Create(speed: 4f));

        // Patrolling with no route: the body stays where it is.
        world.Update(0.1f);
        Assert.Equal(AgentIntent.Patrol, NavTestWorld.Board(world, stalker).Get<AgentIntent>(BlackboardKeys.Intent));
        Assert.Equal(start, NavTestWorld.Position(world, stalker));

        var noise = perception.EmitNoise(new Vector3(7.5f, 0f, 4.5f), intensity: 6f);
        world.Update(0.1f);
        Assert.Equal(AgentIntent.Investigate, NavTestWorld.Board(world, stalker).Get<AgentIntent>(BlackboardKeys.Intent));
        Assert.Equal(noise.Position, NavTestWorld.Agent(world, stalker).Destination);

        for (int i = 0; i < 30; i++)
            world.Update(0.1f);

        Assert.True(NavTestWorld.PlanarDistance(NavTestWorld.Position(world, stalker), noise.Position) <= 0.11f,
            $"stalker ended at {NavTestWorld.Position(world, stalker)}");
    }

    [Fact]
    public void SampleStalker_Patrol_WalksARouteFromTheBlackboard()
    {
        using var world = new World();
        world.AddSystem(new BehaviorTreeSystem());
        world.AddSystem(new NavigationSystem { Grid = NavTestWorld.OpenGrid() });

        var stalker = world.CreateEntity();
        world.AddComponent(stalker, new TransformComponent(new Vector3(2.5f, 0f, 2.5f)));
        world.AddComponent(stalker, StalkerTree.CreateAgent());
        world.AddComponent(stalker, NavAgentComponent.Create(speed: 4f));
        var first = new Vector3(6.5f, 0f, 2.5f);
        NavTestWorld.Board(world, stalker).Set(NavigationKeys.PatrolPoints, new[] { first, new Vector3(6.5f, 0f, 6.5f) });

        world.Update(NavTestWorld.Step);
        Assert.Equal(NodeStatus.Success, NavTestWorld.TreeStatus(world, stalker));
        Assert.Equal(first, NavTestWorld.Agent(world, stalker).Destination);

        Assert.True(NavTestWorld.RunUntil(world, () => NavTestWorld.Board(world, stalker).GetOr(NavigationKeys.PatrolIndex, 0) == 1));
        Assert.True(NavTestWorld.PlanarDistance(NavTestWorld.Position(world, stalker), first) <= 0.11f);
    }
}
