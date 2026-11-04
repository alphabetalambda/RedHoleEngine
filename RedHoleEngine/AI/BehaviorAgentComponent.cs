using RedHoleEngine.AI.BehaviorTrees;
using RedHoleEngine.Core.ECS;

namespace RedHoleEngine.AI;

/// <summary>
/// Marks an entity whose decisions come from a behavior tree.
/// The tree, blackboard, and gate set are reference types owned by this agent.
/// Create one <see cref="BehaviorTree"/> instance per agent; node objects remember
/// which child is running.
/// </summary>
public struct BehaviorAgentComponent : IComponent
{
    public BehaviorTree? Tree;
    public Blackboard? Blackboard;
    public BehaviorGateSet? Gates;
    public bool Enabled;
    public bool RecordTrace;
    public NodeStatus LastStatus;
    public string? LastTrace;

    public static BehaviorAgentComponent Create(BehaviorTree tree, BehaviorGateSet? gates = null, bool recordTrace = false)
    {
        ArgumentNullException.ThrowIfNull(tree);
        return new BehaviorAgentComponent
        {
            Tree = tree,
            Blackboard = new Blackboard(),
            Gates = gates ?? new BehaviorGateSet(),
            Enabled = true,
            RecordTrace = recordTrace,
            LastStatus = NodeStatus.Failure
        };
    }
}
