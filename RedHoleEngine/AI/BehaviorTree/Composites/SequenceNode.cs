namespace RedHoleEngine.AI.BehaviorTrees;

/// <summary>
/// Runs children in order. Fails fast when a child fails, succeeds when all succeed.
/// With memory (the default) a running child is resumed next tick instead of
/// restarting the sequence from the first child.
/// </summary>
public sealed class SequenceNode : BehaviorNode
{
    private int _cursor;

    public SequenceNode(string? name = null, bool memory = true)
        : base("sequence", name)
    {
        Memory = memory;
    }

    public bool Memory { get; }

    protected override bool CanAddChild(int currentCount) => true;

    protected override NodeStatus OnTick(BehaviorContext context)
    {
        if (Children.Count == 0)
            return NodeStatus.Success;

        int start = Memory ? _cursor : 0;
        if (start < 0 || start >= Children.Count)
            start = 0;

        for (int i = start; i < Children.Count; i++)
        {
            var status = TickChild(context, Children[i]);
            if (status == NodeStatus.Running)
            {
                _cursor = Memory ? i : 0;
                AbortExcept(i);
                return NodeStatus.Running;
            }

            if (status == NodeStatus.Failure)
            {
                _cursor = 0;
                AbortExcept(-1);
                return NodeStatus.Failure;
            }
        }

        _cursor = 0;
        return NodeStatus.Success;
    }

    protected override void OnAbort() => _cursor = 0;

    private void AbortExcept(int keep)
    {
        for (int i = 0; i < Children.Count; i++)
        {
            if (i != keep)
                Children[i].Abort();
        }
    }
}
