using System.Numerics;
using RedHoleEngine.AI.BehaviorTrees;
using RedHoleEngine.Core.ECS;

namespace RedHoleEngine.AI.Navigation;

/// <summary>
/// Base for leaves that send an agent somewhere. On top of what
/// <see cref="NavigationActions"/> does, a node remembers the order it gave and cancels it
/// when its branch is preempted, so an agent does not keep walking to a place the tree has
/// stopped caring about. An order that another node has since replaced is left alone.
/// </summary>
/// <remarks>
/// These nodes are code-only: <see cref="BehaviorTreeDocument"/> cannot export them.
/// For JSON trees register the delegates from <see cref="NavigationActions"/> instead.
/// </remarks>
public abstract class NavigationNode : BehaviorNode
{
    private World? _world;
    private Entity _agent;
    private Vector3 _orderedDestination;
    private bool _orderActive;

    protected NavigationNode(string nodeType, string? name)
        : base(nodeType, name)
    {
    }

    protected sealed override NodeStatus OnTick(BehaviorContext context)
    {
        var status = OnNavigate(context);

        _orderActive = false;
        if (status == NodeStatus.Running && NavigationActions.TryGetAgent(context, out var world))
        {
            _world = world;
            _agent = context.Agent;
            _orderedDestination = world.GetComponent<NavAgentComponent>(context.Agent).Destination;
            _orderActive = true;
        }

        return status;
    }

    protected abstract NodeStatus OnNavigate(BehaviorContext context);

    protected override void OnAbort()
    {
        if (!_orderActive)
            return;

        _orderActive = false;
        var world = _world;
        if (world == null || !world.IsAlive(_agent) || !world.HasComponent<NavAgentComponent>(_agent))
            return;

        // A reactive selector aborts the losing branch after the winner has ticked,
        // so only cancel an order that is still ours.
        ref var agent = ref world.GetComponent<NavAgentComponent>(_agent);
        if (agent.HasDestination && agent.Destination == _orderedDestination)
            agent.Stop();
    }
}

/// <summary>
/// Walks to a fixed position or to the <c>Vector3</c> under a blackboard key.
/// Running until arrival, then success. Fails when the key is missing or there is no path.
/// </summary>
public sealed class MoveToNode : NavigationNode
{
    private readonly Vector3 _position;

    public MoveToNode(string? name, string positionKey, float repathDistance = NavigationActions.DefaultRepathDistance)
        : base("moveTo", name)
    {
        ArgumentException.ThrowIfNullOrEmpty(positionKey);
        PositionKey = positionKey;
        RepathDistance = repathDistance;
    }

    public MoveToNode(string? name, Vector3 position)
        : base("moveTo", name)
    {
        _position = position;
        RepathDistance = NavigationActions.DefaultRepathDistance;
    }

    /// <summary>Blackboard key of the target, or null for a fixed position.</summary>
    public string? PositionKey { get; }

    public float RepathDistance { get; }

    protected override NodeStatus OnNavigate(BehaviorContext context)
    {
        var target = _position;
        if (PositionKey != null && !context.Blackboard.TryGet(PositionKey, out target))
            return NodeStatus.Failure;

        return NavigationActions.MoveTo(context, target, RepathDistance);
    }
}

/// <summary>
/// Runs from a fixed position or from the <c>Vector3</c> under a blackboard key.
/// Success once <see cref="SafeDistance"/> away, failure when cornered.
/// </summary>
public sealed class FleeNode : NavigationNode
{
    private readonly Vector3 _threat;

    public FleeNode(
        string? name,
        string threatKey,
        float fleeDistance = NavigationActions.DefaultFleeDistance,
        float safeDistance = NavigationActions.DefaultSafeDistance)
        : base("flee", name)
    {
        ArgumentException.ThrowIfNullOrEmpty(threatKey);
        ThreatKey = threatKey;
        FleeDistance = fleeDistance;
        SafeDistance = safeDistance;
    }

    public FleeNode(
        string? name,
        Vector3 threat,
        float fleeDistance = NavigationActions.DefaultFleeDistance,
        float safeDistance = NavigationActions.DefaultSafeDistance)
        : base("flee", name)
    {
        _threat = threat;
        FleeDistance = fleeDistance;
        SafeDistance = safeDistance;
    }

    /// <summary>Blackboard key of the threat position, or null for a fixed position.</summary>
    public string? ThreatKey { get; }

    /// <summary>How far ahead each escape point is picked.</summary>
    public float FleeDistance { get; }

    public float SafeDistance { get; }

    protected override NodeStatus OnNavigate(BehaviorContext context)
    {
        var threat = _threat;
        if (ThreatKey != null && !context.Blackboard.TryGet(ThreatKey, out threat))
            return NodeStatus.Failure;

        return NavigationActions.Flee(context, threat, FleeDistance, SafeDistance);
    }
}

/// <summary>
/// Walks a route one point per success: running on the way, success on arrival (the next
/// tick heads for the next point), failure for a point that cannot be reached (it is skipped).
/// The route position lives on the node and restarts on <see cref="BehaviorNode.Reset"/>.
/// </summary>
public sealed class PatrolNode : NavigationNode
{
    private readonly Vector3[] _points;
    private int _index;

    public PatrolNode(string? name, IEnumerable<Vector3> points)
        : base("patrol", name)
    {
        ArgumentNullException.ThrowIfNull(points);
        _points = points.ToArray();
        if (_points.Length == 0)
            throw new ArgumentException("A patrol needs at least one point.", nameof(points));
    }

    public IReadOnlyList<Vector3> Points => _points;

    /// <summary>Index of the point the agent is walking to.</summary>
    public int CurrentIndex => _index;

    protected override NodeStatus OnNavigate(BehaviorContext context)
    {
        var status = NavigationActions.MoveTo(context, _points[_index], repathDistance: 0.05f);
        if (status != NodeStatus.Running && NavigationActions.TryGetAgent(context, out _))
            _index = (_index + 1) % _points.Length;
        return status;
    }

    protected override void OnReset() => _index = 0;
}

/// <summary>
/// Walks to a random reachable point within a radius of the agent (or of a fixed center).
/// Success on arrival; the next tick picks a new point. Fails when no point can be found.
/// </summary>
public sealed class WanderNode : NavigationNode
{
    private readonly Random _random;
    private Vector3 _target;
    private bool _hasTarget;

    public WanderNode(string? name, float radius = NavigationActions.DefaultWanderRadius, Vector3? center = null, int? seed = null)
        : base("wander", name)
    {
        Radius = radius;
        Center = center;
        _random = seed.HasValue ? new Random(seed.Value) : new Random();
    }

    public float Radius { get; }

    /// <summary>Fixed middle of the wander area, or null to wander around wherever the agent is.</summary>
    public Vector3? Center { get; }

    protected override NodeStatus OnNavigate(BehaviorContext context)
    {
        if (!_hasTarget)
        {
            if (!NavigationActions.TryPickWanderTarget(context, Radius, Center, _random, out _target))
                return NodeStatus.Failure;
            _hasTarget = true;
        }

        var status = NavigationActions.MoveTo(context, _target, repathDistance: 0.05f);
        if (status != NodeStatus.Running)
            _hasTarget = false;
        return status;
    }

    protected override void OnAbort()
    {
        base.OnAbort();
        _hasTarget = false;
    }
}

/// <summary>Fluent-builder shortcuts for the navigation nodes.</summary>
public static class NavigationBuilderExtensions
{
    public static BehaviorTreeBuilder MoveTo(this BehaviorTreeBuilder builder, string name, string positionKey)
        => builder.Leaf(new MoveToNode(name, positionKey));

    public static BehaviorTreeBuilder MoveTo(this BehaviorTreeBuilder builder, string name, Vector3 position)
        => builder.Leaf(new MoveToNode(name, position));

    public static BehaviorTreeBuilder Flee(
        this BehaviorTreeBuilder builder,
        string name,
        string threatKey,
        float fleeDistance = NavigationActions.DefaultFleeDistance,
        float safeDistance = NavigationActions.DefaultSafeDistance)
        => builder.Leaf(new FleeNode(name, threatKey, fleeDistance, safeDistance));

    public static BehaviorTreeBuilder Patrol(this BehaviorTreeBuilder builder, string name, params Vector3[] points)
        => builder.Leaf(new PatrolNode(name, points));

    public static BehaviorTreeBuilder Wander(
        this BehaviorTreeBuilder builder,
        string name,
        float radius = NavigationActions.DefaultWanderRadius,
        Vector3? center = null,
        int? seed = null)
        => builder.Leaf(new WanderNode(name, radius, center, seed));
}
