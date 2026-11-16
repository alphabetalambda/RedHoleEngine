using System.Numerics;
using RedHoleEngine.AI.Navigation;
using RedHoleEngine.Components;
using RedHoleEngine.Core.ECS;
using RedHoleEngine.Physics;
using RedHoleEngine.Physics.Collision;

namespace RedHoleEngine.Tests.AI;

public class NavGridTests
{
    [Fact]
    public void WorldAndCellCoordinates_RoundTrip()
    {
        var grid = new NavGrid(new Vector3(-5f, 1f, 10f), 10, 4, 0.5f);

        Assert.True(grid.WorldToCell(new Vector3(-4.9f, 0f, 10.6f), out int x, out int z));
        Assert.Equal((0, 1), (x, z));
        Assert.Equal(new Vector3(-4.75f, 1f, 10.75f), grid.CellToWorld(x, z));
    }

    [Fact]
    public void WorldToCell_OutsideTheGrid_ClampsAndReportsFalse()
    {
        var grid = new NavGrid(Vector3.Zero, 4, 4, 1f);

        Assert.False(grid.WorldToCell(new Vector3(-3f, 0f, 9f), out int x, out int z));
        Assert.Equal((0, 3), (x, z));
        Assert.False(grid.IsWalkable(new Vector3(-3f, 0f, 9f)));
        Assert.False(grid.IsWalkable(-1, 0));
    }

    [Fact]
    public void Edits_BumpTheVersion()
    {
        var grid = new NavGrid(Vector3.Zero, 4, 4, 1f);
        int version = grid.Version;

        grid.SetWalkable(1, 1, false);

        Assert.True(grid.Version > version);
        Assert.False(grid.IsWalkable(1, 1));
    }

    [Fact]
    public void SetWalkable_ByWorldRectangle_BlocksCellsWhoseCenterIsInside()
    {
        var grid = new NavGrid(Vector3.Zero, 6, 6, 1f);

        grid.SetWalkable(new Vector3(2f, 0f, 0f), new Vector3(3f, 0f, 6f), walkable: false);

        for (int z = 0; z < 6; z++)
        {
            Assert.True(grid.IsWalkable(1, z));
            Assert.False(grid.IsWalkable(2, z));
            Assert.True(grid.IsWalkable(3, z));
        }
    }

    [Fact]
    public void LineOfSight_IsBlockedByAWall_AndOpenThroughAGap()
    {
        var grid = new NavGrid(Vector3.Zero, 9, 9, 1f);
        for (int z = 0; z < 9; z++)
            grid.SetWalkable(4, z, z == 4);

        Assert.False(grid.HasLineOfSight(new Vector3(1.5f, 0f, 1.5f), new Vector3(7.5f, 0f, 1.5f)));
        Assert.True(grid.HasLineOfSight(new Vector3(1.5f, 0f, 4.5f), new Vector3(7.5f, 0f, 4.5f)));
        Assert.True(grid.HasLineOfSight(new Vector3(1.5f, 0f, 1.5f), new Vector3(1.5f, 0f, 7.5f)));
    }

    [Fact]
    public void LineOfSight_DoesNotSqueezeBetweenDiagonalBlocks()
    {
        var grid = new NavGrid(Vector3.Zero, 4, 4, 1f);
        grid.SetWalkable(1, 2, false);
        grid.SetWalkable(2, 1, false);

        // The segment passes exactly through the shared corner of the two blocked cells.
        Assert.False(grid.HasLineOfSight(grid.CellToWorld(1, 1), grid.CellToWorld(2, 2)));
        Assert.False(grid.CanTraverse(1, 1, 1, 1));
    }

    [Fact]
    public void LineOfSight_IsBlockedByAStepThatIsTooHigh()
    {
        var grid = new NavGrid(Vector3.Zero, 6, 1, 1f) { MaxStepHeight = 0.3f };
        for (int x = 3; x < 6; x++)
            grid.SetHeight(x, 0, 1f);

        Assert.False(grid.HasLineOfSight(grid.CellToWorld(0, 0), grid.CellToWorld(5, 0)));

        grid.MaxStepHeight = 1f;
        Assert.True(grid.HasLineOfSight(grid.CellToWorld(0, 0), grid.CellToWorld(5, 0)));
    }

    [Fact]
    public void NearestWalkable_FindsTheClosestFreeCellWithinRange()
    {
        var grid = new NavGrid(Vector3.Zero, 10, 10, 1f, walkable: false);
        grid.SetWalkable(7, 2, true);
        grid.SetWalkable(2, 4, true);

        Assert.True(grid.TryFindNearestWalkable(new Vector3(2.5f, 0f, 2.5f), 5f, out int x, out int z));
        Assert.Equal((2, 4), (x, z));
        Assert.False(grid.TryFindNearestWalkable(new Vector3(2.5f, 0f, 2.5f), 1f, out _, out _));
    }

    [Fact]
    public void Regions_SeparateDisconnectedAreas_AndFollowEdits()
    {
        var grid = new NavGrid(Vector3.Zero, 7, 3, 1f);
        for (int z = 0; z < 3; z++)
            grid.SetWalkable(3, z, false);

        Assert.True(grid.IsReachable(0, 0, 2, 2));
        Assert.False(grid.IsReachable(0, 0, 6, 2));
        Assert.Equal(0, grid.GetRegion(3, 1));

        grid.SetWalkable(3, 1, true);
        Assert.True(grid.IsReachable(0, 0, 6, 2));
    }
}

public class NavGridBakerTests
{
    [Fact]
    public void Bake_GroundPlaneWithABox_BlocksCellsTheAgentCannotStandOn()
    {
        var physics = new PhysicsWorld();
        AddStatic(physics, 1, new PlaneCollider(Vector3.UnitY, 0f), Vector3.Zero);
        AddStatic(physics, 2, new BoxCollider(new Vector3(1f, 1.5f, 1f)), new Vector3(5f, 1.5f, 5f));

        var settings = new NavBakeSettings { AgentRadius = 0.3f, AgentHeight = 1.8f };
        var grid = NavGridBaker.Bake(physics, Vector3.Zero, new Vector3(10f, 0f, 10f), 0.5f, settings);

        Assert.Equal(20, grid.Width);
        Assert.Equal(20, grid.Depth);

        // Open floor is walkable at ground height.
        Assert.True(grid.IsWalkable(new Vector3(1f, 0f, 1f)));
        Assert.Equal(0f, grid.SampleHeight(new Vector3(1f, 0f, 1f)), 3);

        // Beside the box, closer than the agent radius: blocked.
        Assert.False(grid.IsWalkable(new Vector3(3.8f, 0f, 5f)));

        // A cell and a half away from the box face: free.
        Assert.True(grid.IsWalkable(new Vector3(3.2f, 0f, 5f)));

        // The top of the box is a separate island, not part of the floor.
        Assert.True(grid.WorldToCell(new Vector3(5.1f, 0f, 5.1f), out int topX, out int topZ));
        Assert.Equal(3f, grid.GetHeight(topX, topZ), 3);
        Assert.False(grid.IsReachable(2, 2, topX, topZ));
    }

    [Fact]
    public void Bake_WithoutGround_IsEmptyUnlessGroundIsOptional()
    {
        var physics = new PhysicsWorld();

        var strict = NavGridBaker.Bake(physics, Vector3.Zero, new Vector3(4f, 0f, 4f), 1f);
        var relaxed = NavGridBaker.Bake(physics, Vector3.Zero, new Vector3(4f, 0f, 4f), 1f,
            new NavBakeSettings { RequireGround = false });

        Assert.False(strict.IsWalkable(1, 1));
        Assert.True(relaxed.IsWalkable(1, 1));
    }

    [Fact]
    public void Bake_ReturnsTheWalkableCellCount_AndBumpsTheVersion()
    {
        var physics = new PhysicsWorld();
        AddStatic(physics, 1, new PlaneCollider(Vector3.UnitY, 0f), Vector3.Zero);
        var grid = new NavGrid(Vector3.Zero, 4, 3, 1f, walkable: false);
        int version = grid.Version;

        int walkable = NavGridBaker.Bake(grid, physics);

        Assert.Equal(12, walkable);
        Assert.True(grid.Version > version);
    }

    [Fact]
    public void Bake_IgnoresDynamicBodiesAndTriggers_ByDefault()
    {
        var physics = new PhysicsWorld();
        AddStatic(physics, 1, new PlaneCollider(Vector3.UnitY, 0f), Vector3.Zero);
        AddBody(physics, 2, RigidBodyType.Dynamic, new SphereCollider(0.6f), new Vector3(1.5f, 1f, 1.5f));
        AddStatic(physics, 3, new SphereCollider(0.6f) { IsTrigger = true }, new Vector3(4.5f, 1f, 4.5f));

        var ignoring = NavGridBaker.Bake(physics, Vector3.Zero, new Vector3(6f, 0f, 6f), 1f);
        var including = NavGridBaker.Bake(physics, Vector3.Zero, new Vector3(6f, 0f, 6f), 1f,
            new NavBakeSettings { IncludeDynamic = true });

        Assert.Equal(0f, ignoring.GetHeight(1, 1), 3);
        Assert.True(ignoring.IsReachable(0, 0, 1, 1));
        Assert.True(ignoring.IsReachable(0, 0, 4, 4));

        // Included, the ball is an obstacle: its top is a raised cell nobody can step onto.
        Assert.Equal(1.6f, including.GetHeight(1, 1), 3);
        Assert.False(including.IsReachable(0, 0, 1, 1));
        Assert.True(including.IsReachable(0, 0, 4, 4));
    }

    [Fact]
    public void Bake_CapsuleObstacle_BlocksItsCell()
    {
        var physics = new PhysicsWorld();
        AddStatic(physics, 1, new PlaneCollider(Vector3.UnitY, 0f), Vector3.Zero);
        AddStatic(physics, 2, new CapsuleCollider(0.4f, 2f), new Vector3(2.5f, 1f, 2.5f));

        var grid = NavGridBaker.Bake(physics, Vector3.Zero, new Vector3(5f, 0f, 5f), 1f);

        Assert.False(grid.IsWalkable(2, 2));
        Assert.True(grid.IsWalkable(0, 0));
    }

    [Fact]
    public void Bake_LowLedgeIsWalkable_TallLedgeIsNotConnected()
    {
        var physics = new PhysicsWorld();
        AddStatic(physics, 1, new PlaneCollider(Vector3.UnitY, 0f), Vector3.Zero);
        // 0.2 high slab over x in [4, 8], 1.0 high slab over x in [12, 16].
        AddStatic(physics, 2, new BoxCollider(new Vector3(2f, 0.1f, 2f)), new Vector3(6f, 0.1f, 2f));
        AddStatic(physics, 3, new BoxCollider(new Vector3(2f, 0.5f, 2f)), new Vector3(14f, 0.5f, 2f));

        var settings = new NavBakeSettings { AgentRadius = 0.2f, MaxStepHeight = 0.3f };
        var grid = NavGridBaker.Bake(physics, Vector3.Zero, new Vector3(20f, 0f, 4f), 1f, settings);

        Assert.Equal(0.3f, grid.MaxStepHeight);
        Assert.Equal(0.2f, grid.GetHeight(5, 2), 3);
        Assert.True(grid.IsReachable(0, 2, 5, 2));

        Assert.Equal(1f, grid.GetHeight(13, 2), 3);
        Assert.False(grid.IsReachable(0, 2, 13, 2));
    }

    [Fact]
    public void Bake_SteepGround_IsNotWalkable()
    {
        var physics = new PhysicsWorld();
        var steep = Vector3.Normalize(new Vector3(1f, 0.5f, 0f)); // about 63 degrees from flat
        AddStatic(physics, 1, new PlaneCollider(steep, 0f), Vector3.Zero);

        var grid = NavGridBaker.Bake(physics, new Vector3(-2f, 0f, -2f), new Vector3(2f, 0f, 2f), 1f);

        Assert.False(grid.IsWalkable(1, 1));
    }

    [Fact]
    public void Bake_FromEcsColliders_AfterThePhysicsSystemRegisteredThem()
    {
        using var world = new World();
        var physics = world.AddSystem<PhysicsSystem>();

        var ground = world.CreateEntity();
        world.AddComponent(ground, new TransformComponent(Vector3.Zero));
        world.AddComponent(ground, RigidBodyComponent.CreateStatic());
        world.AddComponent(ground, ColliderComponent.CreateGroundPlane());

        var wall = world.CreateEntity();
        world.AddComponent(wall, new TransformComponent(new Vector3(3f, 1f, 3f)));
        world.AddComponent(wall, RigidBodyComponent.CreateStatic());
        world.AddComponent(wall, ColliderComponent.CreateBox(new Vector3(0.5f, 1f, 3f)));

        world.Update(1f / 60f);
        var grid = NavGridBaker.Bake(physics.PhysicsWorld, Vector3.Zero, new Vector3(6f, 0f, 6f), 0.5f,
            new NavBakeSettings { AgentRadius = 0.25f });

        Assert.True(grid.IsWalkable(new Vector3(1f, 0f, 3f)));
        Assert.False(grid.IsWalkable(new Vector3(2.4f, 0f, 3f)));
        Assert.True(grid.IsWalkable(new Vector3(5f, 0f, 3f)));
        Assert.False(grid.IsReachable(2, 6, 10, 6));
    }

    private static void AddStatic(PhysicsWorld physics, int id, Collider collider, Vector3 position)
        => AddBody(physics, id, RigidBodyType.Static, collider, position);

    private static void AddBody(PhysicsWorld physics, int id, RigidBodyType type, Collider collider, Vector3 position)
    {
        var body = new RigidBody { EntityId = id, Type = type, Position = position };
        physics.AddBody(body);
        physics.AddCollider(body, collider);
    }
}

public class NavPathfinderTests
{
    [Fact]
    public void OpenGround_SmoothsToASingleStraightSegment()
    {
        var grid = new NavGrid(Vector3.Zero, 20, 20, 1f);
        var pathfinder = new NavPathfinder();
        var path = new List<Vector3>();
        var start = new Vector3(1.2f, 0f, 1.7f);
        var goal = new Vector3(17.3f, 0f, 9.1f);

        var result = pathfinder.FindPath(grid, start, goal, path);

        Assert.Equal(NavPathResult.Success, result);
        Assert.Equal(2, path.Count);
        Assert.Equal(start, path[0]);
        Assert.Equal(goal, path[1]);
    }

    [Fact]
    public void Wall_IsWalkedAroundThroughTheGap()
    {
        var grid = WallWithGap(out var start, out var goal);
        var pathfinder = new NavPathfinder();
        var path = new List<Vector3>();

        Assert.Equal(NavPathResult.Success, pathfinder.FindPath(grid, start, goal, path));

        Assert.Equal(start, path[0]);
        Assert.Equal(goal, path[^1]);
        AssertWalkable(grid, path);
        // Both turns sit in the row of the opening, one on each side of the wall.
        Assert.Contains(path, point => point.X < 10f && point.Z is >= 17f and < 18f);
        Assert.Contains(path, point => point.X > 11f && point.Z is >= 17f and < 18f);
    }

    [Fact]
    public void Smoothing_RemovesWaypoints_AndNeverLengthensThePath()
    {
        var grid = WallWithGap(out var start, out var goal);
        var pathfinder = new NavPathfinder();
        var raw = new List<Vector3>();
        var smooth = new List<Vector3>();

        pathfinder.FindPath(grid, start, goal, raw, smooth: false);
        pathfinder.FindPath(grid, start, goal, smooth);

        Assert.True(smooth.Count < raw.Count, $"{smooth.Count} smoothed vs {raw.Count} raw");
        Assert.True(smooth.Count <= 4, $"expected at most two turns, got {smooth.Count} points");
        Assert.True(NavPathfinder.PathLength(smooth) <= NavPathfinder.PathLength(raw) + 1e-3f);
        AssertWalkable(grid, raw);
        AssertWalkable(grid, smooth);
    }

    [Fact]
    public void Smooth_KeepsACornerThatLineOfSightCannotSkip()
    {
        var grid = new NavGrid(Vector3.Zero, 5, 5, 1f);
        grid.SetWalkable(new Vector3(1f, 0f, 1f), new Vector3(5f, 0f, 5f), walkable: false);
        var corner = new[] { grid.CellToWorld(0, 4), grid.CellToWorld(0, 2), grid.CellToWorld(0, 0), grid.CellToWorld(2, 0), grid.CellToWorld(4, 0) };
        var output = new List<Vector3>();

        NavPathfinder.Smooth(grid, corner, output);

        Assert.Equal(new[] { corner[0], corner[2], corner[4] }, output);
    }

    [Fact]
    public void EnclosedGoal_IsUnreachable_WithoutSearching()
    {
        var grid = new NavGrid(Vector3.Zero, 12, 12, 1f);
        for (int i = 6; i <= 10; i++)
        {
            grid.SetWalkable(i, 6, false);
            grid.SetWalkable(i, 10, false);
            grid.SetWalkable(6, i, false);
            grid.SetWalkable(10, i, false);
        }

        var pathfinder = new NavPathfinder();
        var path = new List<Vector3> { Vector3.One };

        var result = pathfinder.FindPath(grid, new Vector3(1.5f, 0f, 1.5f), new Vector3(8.5f, 0f, 8.5f), path);

        Assert.Equal(NavPathResult.Unreachable, result);
        Assert.Empty(path);
        Assert.Equal(0, pathfinder.LastExpandedCount);
    }

    [Fact]
    public void BlockedGoal_SnapsToTheNearestWalkableCell_WithinSnapDistance()
    {
        var grid = new NavGrid(Vector3.Zero, 12, 12, 1f);
        grid.SetWalkable(new Vector3(6f, 0f, 0f), new Vector3(12f, 0f, 12f), walkable: false);
        var pathfinder = new NavPathfinder { SnapDistance = 2f };
        var path = new List<Vector3>();

        var near = pathfinder.FindPath(grid, new Vector3(1.5f, 0f, 5.5f), new Vector3(7.2f, 0f, 5.5f), path);
        Assert.Equal(NavPathResult.Success, near);
        Assert.Equal(grid.CellToWorld(5, 5), path[^1]);

        var far = pathfinder.FindPath(grid, new Vector3(1.5f, 0f, 5.5f), new Vector3(10.5f, 0f, 5.5f), path);
        Assert.Equal(NavPathResult.InvalidGoal, far);
        Assert.Empty(path);
    }

    [Fact]
    public void StartOffTheGrid_IsInvalid_WhenNothingWalkableIsNear()
    {
        var grid = new NavGrid(Vector3.Zero, 4, 4, 1f, walkable: false);
        grid.SetWalkable(3, 3, true);
        var pathfinder = new NavPathfinder { SnapDistance = 1f };

        var result = pathfinder.FindPath(grid, new Vector3(0.5f, 0f, 0.5f), new Vector3(3.5f, 0f, 3.5f), new List<Vector3>());

        Assert.Equal(NavPathResult.InvalidStart, result);
    }

    [Fact]
    public void StartAndGoalInTheSameCell_GiveATwoPointPath()
    {
        var grid = new NavGrid(Vector3.Zero, 4, 4, 1f);
        var path = new List<Vector3>();

        var result = new NavPathfinder().FindPath(grid, new Vector3(1.1f, 0f, 1.1f), new Vector3(1.9f, 0f, 1.8f), path);

        Assert.Equal(NavPathResult.Success, result);
        Assert.Equal(new[] { new Vector3(1.1f, 0f, 1.1f), new Vector3(1.9f, 0f, 1.8f) }, path);
    }

    [Fact]
    public void CellPath_NeverCutsACorner()
    {
        var grid = new NavGrid(Vector3.Zero, 3, 3, 1f);
        grid.SetWalkable(1, 0, false);
        grid.SetWalkable(1, 1, false);
        var cells = new List<int>();

        var result = new NavPathfinder().FindCellPath(grid, 0, 0, 2, 0, cells);

        Assert.Equal(NavPathResult.Success, result);
        // Around the top of the wall with no diagonal past its corner:
        // (0,0) (0,1) (0,2) (1,2) (2,2) (2,1) (2,0).
        Assert.Equal(7, cells.Count);
        AssertContiguous(grid, cells);
    }

    [Fact]
    public void Cliff_SeparatesCells_UntilTheStepHeightAllowsIt()
    {
        var grid = new NavGrid(Vector3.Zero, 6, 2, 1f) { MaxStepHeight = 0.25f };
        for (int z = 0; z < 2; z++)
        {
            for (int x = 3; x < 6; x++)
                grid.SetHeight(x, z, 2f);
        }

        var pathfinder = new NavPathfinder();
        var path = new List<Vector3>();
        var start = new Vector3(0.5f, 0f, 0.5f);
        var goal = new Vector3(5.5f, 0f, 0.5f);

        Assert.Equal(NavPathResult.Unreachable, pathfinder.FindPath(grid, start, goal, path));

        grid.MaxStepHeight = 2f;
        Assert.Equal(NavPathResult.Success, pathfinder.FindPath(grid, start, goal, path));
        Assert.Equal(2f, path[^1].Y);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(7)]
    [InlineData(42)]
    [InlineData(1234)]
    public void CellPath_CostMatchesDijkstra_OnRandomGrids(int seed)
    {
        var random = new Random(seed);
        var grid = new NavGrid(Vector3.Zero, 24, 18, 1f);
        for (int z = 0; z < grid.Depth; z++)
        {
            for (int x = 0; x < grid.Width; x++)
            {
                if (random.NextDouble() < 0.28)
                    grid.SetWalkable(x, z, false);
            }
        }

        var pathfinder = new NavPathfinder();
        var cells = new List<int>();
        int compared = 0;
        for (int query = 0; query < 40; query++)
        {
            int sx = random.Next(grid.Width), sz = random.Next(grid.Depth);
            int gx = random.Next(grid.Width), gz = random.Next(grid.Depth);
            if (!grid.IsWalkable(sx, sz) || !grid.IsWalkable(gx, gz))
                continue;

            float expected = Dijkstra(grid, sx, sz, gx, gz);
            var result = pathfinder.FindCellPath(grid, sx, sz, gx, gz, cells);

            if (float.IsPositiveInfinity(expected))
            {
                Assert.Equal(NavPathResult.Unreachable, result);
                Assert.Empty(cells);
                continue;
            }

            Assert.Equal(NavPathResult.Success, result);
            Assert.Equal(grid.IndexOf(sx, sz), cells[0]);
            Assert.Equal(grid.IndexOf(gx, gz), cells[^1]);
            Assert.Equal(expected, AssertContiguous(grid, cells), 3);
            compared++;
        }

        Assert.True(compared > 5, "the random grid should produce reachable queries");
    }

    [Fact]
    public void RepeatedQueries_DoNotAllocate_OnceBuffersAreWarm()
    {
        var grid = WallWithGap(out var start, out var goal);
        var pathfinder = new NavPathfinder();
        var path = new List<Vector3>(64);
        for (int i = 0; i < 4; i++)
            pathfinder.FindPath(grid, start, goal, path);

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 16; i++)
            pathfinder.FindPath(grid, start, goal, path);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Equal(0, allocated);
    }

    /// <summary>20x20 grid, wall along x = 10 with one opening at z = 17.</summary>
    private static NavGrid WallWithGap(out Vector3 start, out Vector3 goal)
    {
        var grid = new NavGrid(Vector3.Zero, 20, 20, 1f);
        for (int z = 0; z < 20; z++)
            grid.SetWalkable(10, z, z == 17);
        start = new Vector3(2.5f, 0f, 2.5f);
        goal = new Vector3(17.5f, 0f, 2.5f);
        return grid;
    }

    private static void AssertWalkable(NavGrid grid, List<Vector3> path)
    {
        Assert.True(path.Count >= 2);
        for (int i = 1; i < path.Count; i++)
        {
            Assert.True(grid.HasLineOfSight(path[i - 1], path[i]),
                $"segment {i - 1}->{i} ({path[i - 1]} -> {path[i]}) crosses a blocked cell");
        }
    }

    /// <summary>Checks every step is a legal neighbour move and returns the path cost in cells.</summary>
    private static float AssertContiguous(NavGrid grid, List<int> cells)
    {
        float cost = 0f;
        for (int i = 1; i < cells.Count; i++)
        {
            int x = cells[i - 1] % grid.Width, z = cells[i - 1] / grid.Width;
            int dx = cells[i] % grid.Width - x, dz = cells[i] / grid.Width - z;
            Assert.InRange(Math.Abs(dx), 0, 1);
            Assert.InRange(Math.Abs(dz), 0, 1);
            Assert.True(grid.CanTraverse(x, z, dx, dz), $"illegal move from ({x}, {z}) by ({dx}, {dz})");
            cost += dx != 0 && dz != 0 ? MathF.Sqrt(2f) : 1f;
        }

        return cost;
    }

    private static float Dijkstra(NavGrid grid, int startX, int startZ, int goalX, int goalZ)
    {
        var distance = new float[grid.CellCount];
        Array.Fill(distance, float.PositiveInfinity);
        var queue = new PriorityQueue<int, float>();
        distance[grid.IndexOf(startX, startZ)] = 0f;
        queue.Enqueue(grid.IndexOf(startX, startZ), 0f);

        while (queue.TryDequeue(out int current, out float priority))
        {
            if (priority > distance[current])
                continue;

            int x = current % grid.Width, z = current / grid.Width;
            for (int dz = -1; dz <= 1; dz++)
            {
                for (int dx = -1; dx <= 1; dx++)
                {
                    if ((dx == 0 && dz == 0) || !grid.CanTraverse(x, z, dx, dz))
                        continue;

                    int next = grid.IndexOf(x + dx, z + dz);
                    float cost = priority + (dx != 0 && dz != 0 ? MathF.Sqrt(2f) : 1f);
                    if (cost < distance[next])
                    {
                        distance[next] = cost;
                        queue.Enqueue(next, cost);
                    }
                }
            }
        }

        return distance[grid.IndexOf(goalX, goalZ)];
    }
}
