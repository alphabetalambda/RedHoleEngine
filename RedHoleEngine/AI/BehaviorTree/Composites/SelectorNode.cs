namespace RedHoleEngine.AI.BehaviorTrees;

/// <summary>
/// Tries children in order until one returns success or running.
/// A reactive selector (the default) restarts at the first child every tick,
/// so a newly valid higher-priority branch preempts whatever was running.
/// A non-reactive selector stays on a running child until it finishes.
/// </summary>
public sealed class SelectorNode : BehaviorNode
{
    private int _cursor = -1;

    public SelectorNode(string? name = null, bool reactive = true)
        : base("selector", name)
    {
        Reactive = reactive;
    }

    public bool Reactive { get; }

    protected override bool CanAddChild(int currentCount) => true;

    protected override NodeStatus OnTick(BehaviorContext context)
    {
        if (Children.Count == 0)
            return NodeStatus.Failure;

        if (!Reactive && _cursor >= 0 && _cursor < Children.Count)
        {
            var continued = TickChild(context, Children[_cursor]);
            if (continued == NodeStatus.Running)
                return NodeStatus.Running;

            if (continued == NodeStatus.Success)
            {
                int done = _cursor;
                _cursor = -1;
                AbortExcept(done);
                return NodeStatus.Success;
            }

            int failed = _cursor;
            _cursor = -1;
            Children[failed].Abort();
            return TickFrom(context, failed + 1);
        }

        return TickFrom(context, 0);
    }

    protected override void OnAbort() => _cursor = -1;

    private NodeStatus TickFrom(BehaviorContext context, int start)
    {
        for (int i = start; i < Children.Count; i++)
        {
            var status = TickChild(context, Children[i]);
            if (status == NodeStatus.Failure)
                continue;

            _cursor = !Reactive && status == NodeStatus.Running ? i : -1;
            AbortExcept(i);
            return status;
        }

        _cursor = -1;
        AbortExcept(-1);
        return NodeStatus.Failure;
    }

    private void AbortExcept(int keep)
    {
        for (int i = 0; i < Children.Count; i++)
        {
            if (i != keep)
                Children[i].Abort();
        }
    }
}
