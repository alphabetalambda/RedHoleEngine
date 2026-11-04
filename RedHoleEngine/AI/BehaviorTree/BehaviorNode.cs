namespace RedHoleEngine.AI.BehaviorTrees;

/// <summary>
/// Base type for every behavior-tree node. Subclasses implement <see cref="OnTick"/>
/// and report <see cref="NodeStatus"/>. Runtime progress (the running child, a cooldown)
/// lives on the node, so a <see cref="BehaviorTree"/> instance belongs to one agent.
/// Share a <see cref="BehaviorTreeDocument"/> across agents and compile
/// a fresh tree per agent.
/// </summary>
public abstract class BehaviorNode
{
    private readonly List<BehaviorNode> _children = new();

    protected BehaviorNode(string nodeType, string? name)
    {
        NodeType = nodeType ?? throw new ArgumentNullException(nameof(nodeType));
        Name = string.IsNullOrWhiteSpace(name) ? nodeType : name;
    }

    /// <summary>Stable kind used by the JSON compiler and the debug trace ("selector", "action", ...).</summary>
    public string NodeType { get; }

    public string Name { get; set; }

    /// <summary>
    /// When set, the node fails immediately while this id is locked on <see cref="BehaviorGateSet"/>.
    /// </summary>
    public string? GateId { get; set; }

    public NodeStatus LastStatus { get; private set; } = NodeStatus.Failure;

    public IReadOnlyList<BehaviorNode> Children => _children;

    public NodeStatus Tick(BehaviorContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        int entry = context.Trace?.Begin(context.Depth, NodeType, Name) ?? -1;

        if (!string.IsNullOrEmpty(GateId) && !context.Gates.IsUnlocked(GateId))
        {
            if (LastStatus == NodeStatus.Running)
                Abort();
            LastStatus = NodeStatus.Failure;
            context.Trace?.Complete(entry, LastStatus, blockedByGate: true);
            return LastStatus;
        }

        LastStatus = OnTick(context);
        context.Trace?.Complete(entry, LastStatus, blockedByGate: false);
        return LastStatus;
    }

    /// <summary>
    /// Stop an in-progress node because a higher-priority branch took over.
    /// Decorator timers such as cooldown survive an abort.
    /// </summary>
    public void Abort()
    {
        for (int i = 0; i < _children.Count; i++)
            _children[i].Abort();

        LastStatus = NodeStatus.Failure;
        OnAbort();
    }

    /// <summary>
    /// Full reset, including decorator timers. Used between encounters and by tests.
    /// </summary>
    public void Reset()
    {
        for (int i = 0; i < _children.Count; i++)
            _children[i].Reset();

        LastStatus = NodeStatus.Failure;
        OnAbort();
        OnReset();
    }

    public void AddChild(BehaviorNode child)
    {
        ArgumentNullException.ThrowIfNull(child);
        if (!CanAddChild(_children.Count))
            throw new InvalidOperationException($"{NodeType} '{Name}' cannot accept another child.");
        _children.Add(child);
    }

    protected abstract NodeStatus OnTick(BehaviorContext context);

    protected virtual bool CanAddChild(int currentCount) => false;

    protected virtual void OnAbort() { }

    protected virtual void OnReset() { }

    protected NodeStatus TickChild(BehaviorContext context, BehaviorNode child)
    {
        context.Depth++;
        try
        {
            return child.Tick(context);
        }
        finally
        {
            context.Depth--;
        }
    }
}
