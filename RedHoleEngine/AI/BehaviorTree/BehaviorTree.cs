namespace RedHoleEngine.AI.BehaviorTrees;

/// <summary>
/// One agent's compiled tree. Node instances hold running state, so do not
/// put the same instance on two agents. Compile a document (or call a factory)
/// once per agent instead.
/// </summary>
public sealed class BehaviorTree
{
    public BehaviorTree(string name, BehaviorNode root)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("A behavior tree needs a name.", nameof(name));
        Name = name;
        Root = root ?? throw new ArgumentNullException(nameof(root));
    }

    public string Name { get; }

    public BehaviorNode Root { get; }

    public NodeStatus LastStatus { get; private set; } = NodeStatus.Failure;

    /// <summary>When true, the next <see cref="Tick(BehaviorContext)"/> records <see cref="LastTrace"/>.</summary>
    public bool RecordTrace { get; set; }

    public BehaviorTrace? LastTrace { get; private set; }

    public NodeStatus Tick(Blackboard blackboard, BehaviorGateSet? gates = null, float deltaTime = 0f)
    {
        return Tick(new BehaviorContext(blackboard, gates, deltaTime));
    }

    public NodeStatus Tick(BehaviorContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        context.Depth = 0;

        if (RecordTrace)
        {
            var trace = new BehaviorTrace();
            context.Trace = trace;
            LastStatus = Root.Tick(context);
            LastTrace = trace;
        }
        else
        {
            context.Trace = null;
            LastStatus = Root.Tick(context);
            LastTrace = null;
        }

        return LastStatus;
    }

    public void Reset() => Root.Reset();

    /// <summary>Indented trace of the last tick, or just the last status when tracing is off.</summary>
    public string DescribeLastTick() => LastTrace?.ToString() ?? LastStatus.ToString();
}
