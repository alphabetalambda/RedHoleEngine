namespace RedHoleEngine.AI.BehaviorTrees;

/// <summary>
/// How many children must succeed or fail before a parallel node resolves.
/// </summary>
public enum ParallelPolicy
{
    RequireOne,
    RequireAll
}

/// <summary>
/// Ticks every child each frame. Children that already succeeded or failed stay
/// latched so a one-shot action is not repeated while a sibling is still running.
/// If both policies are satisfied on the same tick, failure wins.
/// </summary>
public sealed class ParallelNode : BehaviorNode
{
    private NodeStatus[] _latched = Array.Empty<NodeStatus>();
    private bool[] _latchedValid = Array.Empty<bool>();

    public ParallelNode(
        string? name = null,
        ParallelPolicy successPolicy = ParallelPolicy.RequireAll,
        ParallelPolicy failurePolicy = ParallelPolicy.RequireOne)
        : base("parallel", name)
    {
        SuccessPolicy = successPolicy;
        FailurePolicy = failurePolicy;
    }

    public ParallelPolicy SuccessPolicy { get; }

    public ParallelPolicy FailurePolicy { get; }

    protected override bool CanAddChild(int currentCount) => true;

    protected override NodeStatus OnTick(BehaviorContext context)
    {
        if (Children.Count == 0)
            return NodeStatus.Success;

        EnsureLatch();

        int successes = 0;
        int failures = 0;
        for (int i = 0; i < Children.Count; i++)
        {
            NodeStatus status;
            if (_latchedValid[i])
            {
                status = _latched[i];
            }
            else
            {
                status = TickChild(context, Children[i]);
                if (status != NodeStatus.Running)
                {
                    _latched[i] = status;
                    _latchedValid[i] = true;
                }
            }

            if (status == NodeStatus.Success)
                successes++;
            else if (status == NodeStatus.Failure)
                failures++;
        }

        if (IsMet(FailurePolicy, failures, Children.Count))
            return Finish(NodeStatus.Failure);
        if (IsMet(SuccessPolicy, successes, Children.Count))
            return Finish(NodeStatus.Success);
        return NodeStatus.Running;
    }

    protected override void OnAbort() => ClearLatches();

    private NodeStatus Finish(NodeStatus status)
    {
        for (int i = 0; i < Children.Count; i++)
            Children[i].Abort();
        ClearLatches();
        return status;
    }

    private void EnsureLatch()
    {
        if (_latched.Length == Children.Count)
            return;
        _latched = new NodeStatus[Children.Count];
        _latchedValid = new bool[Children.Count];
    }

    private void ClearLatches()
    {
        if (_latchedValid.Length > 0)
            Array.Clear(_latchedValid, 0, _latchedValid.Length);
    }

    private static bool IsMet(ParallelPolicy policy, int count, int total)
    {
        return policy == ParallelPolicy.RequireAll ? count == total : count > 0;
    }
}
