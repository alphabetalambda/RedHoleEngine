namespace RedHoleEngine.AI.BehaviorTrees;

/// <summary>Leaf that succeeds or fails with no running state.</summary>
public sealed class ConditionNode : BehaviorNode
{
    private readonly Func<BehaviorContext, bool> _predicate;

    public ConditionNode(string? name, Func<BehaviorContext, bool> predicate, string? conditionKey = null)
        : base("condition", name)
    {
        _predicate = predicate ?? throw new ArgumentNullException(nameof(predicate));
        ConditionKey = conditionKey;
    }

    /// <summary>Registry id, when this condition was built from data.</summary>
    public string? ConditionKey { get; }

    protected override NodeStatus OnTick(BehaviorContext context)
    {
        return _predicate(context) ? NodeStatus.Success : NodeStatus.Failure;
    }
}
