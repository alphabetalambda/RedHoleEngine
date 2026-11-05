namespace RedHoleEngine.AI.BehaviorTrees;

/// <summary>Swaps success and failure. Running passes through unchanged.</summary>
public sealed class InverterNode : DecoratorNode
{
    public InverterNode(string? name = null)
        : base("inverter", name)
    {
    }

    protected override NodeStatus OnTick(BehaviorContext context)
    {
        return TickChild(context, Child) switch
        {
            NodeStatus.Success => NodeStatus.Failure,
            NodeStatus.Failure => NodeStatus.Success,
            _ => NodeStatus.Running
        };
    }
}
