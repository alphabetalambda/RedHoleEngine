namespace RedHoleEngine.AI.BehaviorTrees;

/// <summary>
/// Condition guard. Ticks the child only while its predicate is true.
/// When the predicate becomes false the child is aborted and the guard fails,
/// so a sequence that is parked on a running action still notices the condition.
/// </summary>
public sealed class ConditionGuardNode : DecoratorNode
{
    private readonly Func<BehaviorContext, bool> _predicate;

    public ConditionGuardNode(string? name, Func<BehaviorContext, bool> predicate, string? conditionKey = null)
        : base("guard", name)
    {
        _predicate = predicate ?? throw new ArgumentNullException(nameof(predicate));
        ConditionKey = conditionKey;
    }

    /// <summary>Registry id, when this guard was built from data. Null for a code-only predicate.</summary>
    public string? ConditionKey { get; }

    protected override NodeStatus OnTick(BehaviorContext context)
    {
        if (!_predicate(context))
        {
            Child.Abort();
            return NodeStatus.Failure;
        }

        return TickChild(context, Child);
    }
}
