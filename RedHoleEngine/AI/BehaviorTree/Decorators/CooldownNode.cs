namespace RedHoleEngine.AI.BehaviorTrees;

/// <summary>
/// After the child reaches a terminal status, refuses to tick it again until
/// <see cref="Seconds"/> have elapsed. While cooling down the decorator returns failure.
/// The timer survives <see cref="BehaviorNode.Abort"/> so a preempted branch does not
/// forget that the ability was just used. <see cref="BehaviorNode.Reset"/> clears it.
/// Time is checked at the start of the tick: a large delta that finishes the timer
/// makes the child available on the following tick.
/// </summary>
public sealed class CooldownNode : DecoratorNode
{
    private float _remaining;

    public CooldownNode(string? name, float seconds, bool startOnSuccess = true, bool startOnFailure = false)
        : base("cooldown", name)
    {
        if (seconds < 0f)
            throw new ArgumentOutOfRangeException(nameof(seconds), "Cooldown duration cannot be negative.");
        Seconds = seconds;
        StartOnSuccess = startOnSuccess;
        StartOnFailure = startOnFailure;
    }

    public float Seconds { get; }

    public bool StartOnSuccess { get; }

    public bool StartOnFailure { get; }

    public float Remaining => _remaining;

    protected override NodeStatus OnTick(BehaviorContext context)
    {
        if (_remaining > 0f)
        {
            _remaining = MathF.Max(0f, _remaining - context.DeltaTime);
            return NodeStatus.Failure;
        }

        var status = TickChild(context, Child);
        if ((status == NodeStatus.Success && StartOnSuccess) ||
            (status == NodeStatus.Failure && StartOnFailure))
        {
            _remaining = Seconds;
        }

        return status;
    }

    protected override void OnReset() => _remaining = 0f;
}
