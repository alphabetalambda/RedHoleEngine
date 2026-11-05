namespace RedHoleEngine.AI.BehaviorTrees;

/// <summary>
/// Runs the child again after each success.
/// <see cref="Count"/> successes complete the decorator. A negative count never
/// completes on success (it stops only when the child fails). Zero completes immediately.
/// Each success aborts the child so the next repetition starts clean, without
/// wiping decorator timers such as cooldown.
/// </summary>
public sealed class RepeatNode : DecoratorNode
{
    private int _successes;

    public RepeatNode(string? name, int count)
        : base("repeat", name)
    {
        Count = count;
    }

    /// <summary>Successes required. Negative means "until the child fails".</summary>
    public int Count { get; }

    public int Successes => _successes;

    protected override NodeStatus OnTick(BehaviorContext context)
    {
        if (Count == 0)
            return NodeStatus.Success;

        var status = TickChild(context, Child);
        if (status == NodeStatus.Running)
            return NodeStatus.Running;
        if (status == NodeStatus.Failure)
            return NodeStatus.Failure;

        _successes++;
        Child.Abort();
        if (Count > 0 && _successes >= Count)
            return NodeStatus.Success;
        return NodeStatus.Running;
    }

    protected override void OnAbort() => _successes = 0;
}
