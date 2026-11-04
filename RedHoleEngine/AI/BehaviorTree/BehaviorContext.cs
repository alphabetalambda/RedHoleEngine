using RedHoleEngine.Core.ECS;

namespace RedHoleEngine.AI.BehaviorTrees;

/// <summary>
/// Everything a node is allowed to see during a tick: the agent's memory,
/// which behaviors are unlocked, and how much time passed.
/// The world is optional so the tree can be unit-tested without an ECS.
/// </summary>
public sealed class BehaviorContext
{
    public World? World { get; }
    public Entity Agent { get; }
    public Blackboard Blackboard { get; }
    public BehaviorGateSet Gates { get; }
    public float DeltaTime { get; }

    /// <summary>Set by <see cref="BehaviorTree"/> while a trace is being recorded.</summary>
    public BehaviorTrace? Trace { get; set; }

    /// <summary>Nesting depth of the node currently being ticked. Root is 0.</summary>
    public int Depth { get; set; }

    public BehaviorContext(Blackboard blackboard, BehaviorGateSet? gates = null, float deltaTime = 0f)
    {
        Blackboard = blackboard ?? throw new ArgumentNullException(nameof(blackboard));
        Gates = gates ?? new BehaviorGateSet();
        DeltaTime = deltaTime;
        Agent = Entity.Null;
    }

    public BehaviorContext(World world, Entity agent, Blackboard blackboard, BehaviorGateSet? gates, float deltaTime)
        : this(blackboard, gates, deltaTime)
    {
        World = world ?? throw new ArgumentNullException(nameof(world));
        Agent = agent;
    }
}
