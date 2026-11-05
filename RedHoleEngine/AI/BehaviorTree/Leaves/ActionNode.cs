namespace RedHoleEngine.AI.BehaviorTrees;

/// <summary>
/// Leaf that does the work. The callback must be stateless if the same registration
/// is shared by many agents; put per-agent progress on the <see cref="Blackboard"/>
/// or on a custom node subclass.
/// </summary>
public sealed class ActionNode : BehaviorNode
{
    private readonly Func<BehaviorContext, NodeStatus> _action;

    public ActionNode(string? name, Func<BehaviorContext, NodeStatus> action, string? actionKey = null)
        : base("action", name)
    {
        _action = action ?? throw new ArgumentNullException(nameof(action));
        ActionKey = actionKey;
    }

    /// <summary>Registry id, when this action was built from data.</summary>
    public string? ActionKey { get; }

    protected override NodeStatus OnTick(BehaviorContext context) => _action(context);
}
