using System.Numerics;
using RedHoleEngine.AI.BehaviorTrees;
using RedHoleEngine.Components;
using RedHoleEngine.Core.ECS;

namespace RedHoleEngine.AI.Navigation;

/// <summary>Registry ids of the stock navigation actions. See <see cref="NavigationActions.Register"/>.</summary>
public static class NavigationIds
{
    /// <summary>Walk to <see cref="BlackboardKeys.IntentPosition"/>.</summary>
    public const string MoveToIntent = "nav.moveToIntent";

    /// <summary>Walk to <see cref="BlackboardKeys.LastKnownPosition"/>.</summary>
    public const string MoveToLastKnown = "nav.moveToLastKnown";

    /// <summary>Run from <see cref="BlackboardKeys.LastSeenPosition"/>.</summary>
    public const string FleeLastSeen = "nav.fleeLastSeen";

    /// <summary>Walk the <c>Vector3[]</c> stored under <see cref="NavigationKeys.PatrolPoints"/>, one point per success.</summary>
    public const string Patrol = "nav.patrol";

    /// <summary>Walk to a random reachable point within <see cref="NavigationActions.DefaultWanderRadius"/>.</summary>
    public const string Wander = "nav.wander";

    /// <summary>Drop the destination and brake.</summary>
    public const string Stop = "nav.stop";
}

/// <summary>Blackboard keys used by the navigation actions.</summary>
public static class NavigationKeys
{
    /// <summary><c>Vector3[]</c> route for <see cref="NavigationIds.Patrol"/>.</summary>
    public const string PatrolPoints = "nav.patrolPoints";

    /// <summary><c>int</c> index of the patrol point being walked to.</summary>
    public const string PatrolIndex = "nav.patrolIndex";

    /// <summary><c>Vector3</c> point the wander action is walking to.</summary>
    public const string WanderTarget = "nav.wanderTarget";
}

/// <summary>
/// Stateless navigation leaves for behavior trees. They give orders to the agent's
/// <see cref="NavAgentComponent"/> and report its progress; <see cref="NavigationSystem"/>
/// does the walking. Every action fails when the tree is ticked without a world or the
/// agent has no <see cref="NavAgentComponent"/> and <c>TransformComponent</c>.
/// </summary>
/// <remarks>
/// The methods taking a <see cref="BehaviorContext"/> can be called from your own actions.
/// The factory methods return delegates for <c>BehaviorTreeBuilder.Action</c> or
/// <see cref="BehaviorRegistry.RegisterAction"/>. For leaves that also halt the agent when
/// their branch is preempted, use the node classes (<see cref="MoveToNode"/> and friends).
/// </remarks>
public static class NavigationActions
{
    /// <summary>A target that moved less than this keeps the current path.</summary>
    public const float DefaultRepathDistance = 0.5f;

    public const float DefaultFleeDistance = 8f;
    public const float DefaultSafeDistance = 12f;
    public const float DefaultWanderRadius = 6f;

    private const int WanderAttempts = 8;
    private static readonly float[] FleeAngles = { 0f, 45f, -45f, 90f, -90f, 135f, -135f };

    /// <summary>Adds the stock actions listed in <see cref="NavigationIds"/> to a registry.</summary>
    public static BehaviorRegistry Register(BehaviorRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);
        return registry
            .RegisterAction(NavigationIds.MoveToIntent, MoveToKey(BlackboardKeys.IntentPosition))
            .RegisterAction(NavigationIds.MoveToLastKnown, MoveToKey(BlackboardKeys.LastKnownPosition))
            .RegisterAction(NavigationIds.FleeLastSeen, FleeFromKey(BlackboardKeys.LastSeenPosition))
            .RegisterAction(NavigationIds.Patrol, PatrolFromBlackboard)
            .RegisterAction(NavigationIds.Wander, WanderWithin())
            .RegisterAction(NavigationIds.Stop, Stop);
    }

    /// <summary>Action that walks to the <c>Vector3</c> stored under a blackboard key. Fails while the key is missing.</summary>
    public static Func<BehaviorContext, NodeStatus> MoveToKey(string positionKey, float repathDistance = DefaultRepathDistance)
    {
        ArgumentException.ThrowIfNullOrEmpty(positionKey);
        return context => context.Blackboard.TryGet(positionKey, out Vector3 target)
            ? MoveTo(context, target, repathDistance)
            : NodeStatus.Failure;
    }

    /// <summary>Action that walks to a fixed position.</summary>
    public static Func<BehaviorContext, NodeStatus> MoveToPosition(Vector3 position)
        => context => MoveTo(context, position);

    /// <summary>Action that runs from the <c>Vector3</c> stored under a blackboard key. Fails while the key is missing.</summary>
    public static Func<BehaviorContext, NodeStatus> FleeFromKey(
        string threatKey,
        float fleeDistance = DefaultFleeDistance,
        float safeDistance = DefaultSafeDistance)
    {
        ArgumentException.ThrowIfNullOrEmpty(threatKey);
        return context => context.Blackboard.TryGet(threatKey, out Vector3 threat)
            ? Flee(context, threat, fleeDistance, safeDistance)
            : NodeStatus.Failure;
    }

    /// <summary>Action that walks a fixed route. The current index is kept on the blackboard.</summary>
    public static Func<BehaviorContext, NodeStatus> PatrolPoints(IReadOnlyList<Vector3> points, string indexKey = NavigationKeys.PatrolIndex)
    {
        ArgumentNullException.ThrowIfNull(points);
        ArgumentException.ThrowIfNullOrEmpty(indexKey);
        return context => Patrol(context, points, indexKey);
    }

    /// <summary>Action that walks to random points around the agent (or around <paramref name="center"/>).</summary>
    public static Func<BehaviorContext, NodeStatus> WanderWithin(float radius = DefaultWanderRadius, Vector3? center = null, Random? random = null)
        => context => Wander(context, radius, center, random);

    /// <summary>
    /// Orders the agent to <paramref name="target"/> and reports progress:
    /// <see cref="NodeStatus.Running"/> on the way, <see cref="NodeStatus.Success"/> on arrival,
    /// <see cref="NodeStatus.Failure"/> when there is no path. A blocked target is replaced by the
    /// nearest walkable point within <see cref="NavPathfinder.SnapDistance"/>.
    /// Safe to call every tick: the path is only replanned when the target moves by more than
    /// <paramref name="repathDistance"/>.
    /// </summary>
    public static NodeStatus MoveTo(BehaviorContext context, Vector3 target, float repathDistance = DefaultRepathDistance)
    {
        if (!TryGetAgent(context, out var world))
            return NodeStatus.Failure;

        ref var agent = ref world.GetComponent<NavAgentComponent>(context.Agent);
        if (!agent.Enabled)
            return NodeStatus.Failure;

        bool sameOrder = agent.HasDestination
            && NavMath.PlanarDistanceSquared(agent.Destination, target) <= repathDistance * repathDistance;
        if (!sameOrder)
        {
            // Plan now when possible, so an unreachable target fails on this tick.
            var navigation = NavigationSystem.Find(world);
            if (navigation != null)
                navigation.SetDestination(context.Agent, target);
            else
                agent.SetDestination(target);
        }

        return agent.Status switch
        {
            NavAgentStatus.Arrived => NodeStatus.Success,
            NavAgentStatus.Pending or NavAgentStatus.Moving => NodeStatus.Running,
            _ => NodeStatus.Failure
        };
    }

    /// <summary>
    /// Runs away from <paramref name="threat"/>. Succeeds once the agent is at least
    /// <paramref name="safeDistance"/> away, runs while it is gaining distance, and fails when
    /// no reachable point within <paramref name="fleeDistance"/> is further from the threat.
    /// </summary>
    public static NodeStatus Flee(
        BehaviorContext context,
        Vector3 threat,
        float fleeDistance = DefaultFleeDistance,
        float safeDistance = DefaultSafeDistance)
    {
        if (!TryGetAgent(context, out var world))
            return NodeStatus.Failure;

        var position = world.GetComponent<TransformComponent>(context.Agent).Position;
        float current = NavMath.PlanarDistance(position, threat);
        if (current >= safeDistance)
            return NodeStatus.Success;

        ref var agent = ref world.GetComponent<NavAgentComponent>(context.Agent);
        if (!agent.Enabled)
            return NodeStatus.Failure;

        // Keep the current escape route while it still leads away.
        const float gain = 0.25f;
        if (agent.Status == NavAgentStatus.Moving
            && NavMath.PlanarDistance(agent.Destination, threat) > current + gain)
            return NodeStatus.Running;

        var navigation = NavigationSystem.Find(world);
        var grid = navigation?.Grid;
        if (navigation == null || grid == null
            || !grid.TryFindNearestWalkable(position, navigation.Pathfinder.SnapDistance, out int fromX, out int fromZ))
            return NodeStatus.Failure;

        var away = new Vector3(position.X - threat.X, 0f, position.Z - threat.Z);
        away = away.LengthSquared() > 1e-8f ? Vector3.Normalize(away) : Vector3.UnitX;

        bool found = false;
        float best = current + gain;
        var escape = position;
        for (int i = 0; i < FleeAngles.Length; i++)
        {
            float radians = FleeAngles[i] * (MathF.PI / 180f);
            float cos = MathF.Cos(radians);
            float sin = MathF.Sin(radians);
            var direction = new Vector3(away.X * cos - away.Z * sin, 0f, away.X * sin + away.Z * cos);
            var probe = position + direction * fleeDistance;
            if (!grid.TrySamplePosition(probe, navigation.Pathfinder.SnapDistance, out var candidate))
                continue;

            grid.WorldToCell(candidate, out int toX, out int toZ);
            if (!grid.IsReachable(fromX, fromZ, toX, toZ))
                continue;

            float distance = NavMath.PlanarDistance(candidate, threat);
            if (distance > best)
            {
                best = distance;
                escape = candidate;
                found = true;
            }
        }

        if (!found || !navigation.SetDestination(context.Agent, escape))
            return NodeStatus.Failure;

        return NodeStatus.Running;
    }

    /// <summary>
    /// Walks to the current point of a route. Succeeds on arrival and advances to the next point,
    /// so wrap it in a repeat (and a cooldown, to pause at each stop). An unreachable point is
    /// skipped with a failure.
    /// </summary>
    public static NodeStatus Patrol(BehaviorContext context, IReadOnlyList<Vector3> points, string indexKey = NavigationKeys.PatrolIndex)
    {
        ArgumentNullException.ThrowIfNull(points);
        if (points.Count == 0)
            return NodeStatus.Failure;

        int index = context.Blackboard.GetOr(indexKey, 0);
        index = ((index % points.Count) + points.Count) % points.Count;

        var status = MoveTo(context, points[index], repathDistance: 0.05f);
        if (status != NodeStatus.Running && TryGetAgent(context, out _))
            context.Blackboard.Set(indexKey, (index + 1) % points.Count);
        return status;
    }

    /// <summary><see cref="Patrol"/> over the <c>Vector3[]</c> stored under <see cref="NavigationKeys.PatrolPoints"/>.</summary>
    public static NodeStatus PatrolFromBlackboard(BehaviorContext context)
    {
        return context.Blackboard.TryGet(NavigationKeys.PatrolPoints, out Vector3[] points)
            ? Patrol(context, points)
            : NodeStatus.Failure;
    }

    /// <summary>
    /// Walks to a random reachable point within <paramref name="radius"/> of the agent
    /// (or of <paramref name="center"/>). Succeeds on arrival; the next tick picks a new point.
    /// </summary>
    public static NodeStatus Wander(BehaviorContext context, float radius = DefaultWanderRadius, Vector3? center = null, Random? random = null)
    {
        var board = context.Blackboard;
        if (!board.TryGet(NavigationKeys.WanderTarget, out Vector3 target))
        {
            if (!TryPickWanderTarget(context, radius, center, random, out target))
                return NodeStatus.Failure;
            board.Set(NavigationKeys.WanderTarget, target);
        }

        var status = MoveTo(context, target, repathDistance: 0.05f);
        if (status != NodeStatus.Running)
            board.Remove(NavigationKeys.WanderTarget);
        return status;
    }

    /// <summary>Picks a random walkable point the agent can reach. Used by the wander action and node.</summary>
    public static bool TryPickWanderTarget(BehaviorContext context, float radius, Vector3? center, Random? random, out Vector3 target)
    {
        target = default;
        if (!TryGetAgent(context, out var world))
            return false;

        var navigation = NavigationSystem.Find(world);
        var grid = navigation?.Grid;
        var position = world.GetComponent<TransformComponent>(context.Agent).Position;
        if (navigation == null || grid == null
            || !grid.TryFindNearestWalkable(position, navigation.Pathfinder.SnapDistance, out int fromX, out int fromZ))
            return false;

        random ??= Random.Shared;
        var origin = center ?? position;
        for (int attempt = 0; attempt < WanderAttempts; attempt++)
        {
            // Square root keeps the points uniform over the disc instead of bunched at the middle.
            float distance = MathF.Max(0f, radius) * MathF.Sqrt(random.NextSingle());
            float angle = random.NextSingle() * MathF.Tau;
            var probe = origin + new Vector3(MathF.Cos(angle) * distance, 0f, MathF.Sin(angle) * distance);
            if (!grid.WorldToCell(probe, out int x, out int z) || !grid.IsReachable(fromX, fromZ, x, z))
                continue;

            target = new Vector3(probe.X, grid.GetHeight(x, z), probe.Z);
            return true;
        }

        return false;
    }

    /// <summary>Drops the agent's destination. Always succeeds when the agent can navigate.</summary>
    public static NodeStatus Stop(BehaviorContext context)
    {
        if (!TryGetAgent(context, out var world))
            return NodeStatus.Failure;

        ref var agent = ref world.GetComponent<NavAgentComponent>(context.Agent);
        if (agent.HasDestination)
            agent.Stop();
        return NodeStatus.Success;
    }

    /// <summary>
    /// Walks to <see cref="BlackboardKeys.IntentPosition"/>, or stops when the intent has no place.
    /// Lets a tree that only writes intents (the sample stalker) drive a body.
    /// </summary>
    public static NodeStatus FollowIntent(BehaviorContext context)
    {
        return context.Blackboard.TryGet(BlackboardKeys.IntentPosition, out Vector3 target)
            ? MoveTo(context, target)
            : Stop(context);
    }

    internal static bool TryGetAgent(BehaviorContext context, out World world)
    {
        world = context.World!;
        return world != null
            && world.IsAlive(context.Agent)
            && world.HasComponent<NavAgentComponent>(context.Agent)
            && world.HasComponent<TransformComponent>(context.Agent);
    }
}
