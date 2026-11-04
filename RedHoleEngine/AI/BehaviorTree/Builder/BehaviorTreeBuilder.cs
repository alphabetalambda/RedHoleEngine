namespace RedHoleEngine.AI.BehaviorTrees;

/// <summary>
/// Fluent construction of a tree. Call <see cref="End"/> once for every composite
/// or decorator you open. <c>Gate</c> applies to the node most recently created.
/// </summary>
/// <remarks>
/// Pass a <see cref="BehaviorRegistry"/> when leaves are referenced by id so the
/// finished tree can be exported back to JSON. Lambdas are code-only.
/// </remarks>
public sealed class BehaviorTreeBuilder
{
    private readonly string _name;
    private readonly BehaviorRegistry? _registry;
    private readonly Stack<BehaviorNode> _open = new();
    private BehaviorNode? _root;
    private BehaviorNode? _last;

    public BehaviorTreeBuilder(string name, BehaviorRegistry? registry = null)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("A behavior tree needs a name.", nameof(name));
        _name = name;
        _registry = registry;
    }

    public BehaviorTreeBuilder Selector(string name, bool reactive = true)
        => Open(new SelectorNode(name, reactive));

    public BehaviorTreeBuilder Sequence(string name, bool memory = true)
        => Open(new SequenceNode(name, memory));

    public BehaviorTreeBuilder Parallel(
        string name,
        ParallelPolicy successPolicy = ParallelPolicy.RequireAll,
        ParallelPolicy failurePolicy = ParallelPolicy.RequireOne)
        => Open(new ParallelNode(name, successPolicy, failurePolicy));

    public BehaviorTreeBuilder Inverter(string name) => Open(new InverterNode(name));

    public BehaviorTreeBuilder Cooldown(float seconds, string name, bool startOnSuccess = true, bool startOnFailure = false)
        => Open(new CooldownNode(name, seconds, startOnSuccess, startOnFailure));

    public BehaviorTreeBuilder Repeat(int count, string name) => Open(new RepeatNode(name, count));

    public BehaviorTreeBuilder Guard(string name, Func<BehaviorContext, bool> predicate)
        => Open(new ConditionGuardNode(name, predicate));

    public BehaviorTreeBuilder Guard(string name, string conditionKey)
        => Open(new ConditionGuardNode(name, ResolveCondition(conditionKey), conditionKey));

    public BehaviorTreeBuilder Condition(string name, Func<BehaviorContext, bool> predicate)
        => Attach(new ConditionNode(name, predicate));

    public BehaviorTreeBuilder Condition(string name, string conditionKey)
        => Attach(new ConditionNode(name, ResolveCondition(conditionKey), conditionKey));

    public BehaviorTreeBuilder Action(string name, Func<BehaviorContext, NodeStatus> action)
        => Attach(new ActionNode(name, action));

    public BehaviorTreeBuilder Action(string name, string actionKey)
        => Attach(new ActionNode(name, ResolveAction(actionKey), actionKey));

    /// <summary>Assigns a behavior-unlock id to the node most recently created.</summary>
    public BehaviorTreeBuilder Gate(string gateId)
    {
        ArgumentException.ThrowIfNullOrEmpty(gateId);
        if (_last == null)
            throw new InvalidOperationException("Gate() needs a node to attach to.");
        _last.GateId = gateId;
        return this;
    }

    /// <summary>Closes the innermost open composite or decorator.</summary>
    public BehaviorTreeBuilder End()
    {
        if (_open.Count == 0)
            throw new InvalidOperationException("End() has no open composite or decorator.");

        var node = _open.Pop();
        if (node is DecoratorNode && node.Children.Count != 1)
            throw new InvalidOperationException($"Decorator '{node.Name}' requires exactly one child.");
        _last = node;
        return this;
    }

    public BehaviorTree Build()
    {
        if (_open.Count > 0)
            throw new InvalidOperationException($"Unclosed node '{_open.Peek().Name}'. Call End() for each composite and decorator.");
        if (_root == null)
            throw new InvalidOperationException("Behavior tree has no root.");

        Validate(_root);
        return new BehaviorTree(_name, _root);
    }

    private BehaviorTreeBuilder Open(BehaviorNode node)
    {
        Attach(node);
        _open.Push(node);
        return this;
    }

    private BehaviorTreeBuilder Attach(BehaviorNode node)
    {
        if (_open.Count == 0)
        {
            if (_root != null)
                throw new InvalidOperationException("A behavior tree can only have one root. Close the previous branch with End().");
            _root = node;
        }
        else
        {
            _open.Peek().AddChild(node);
        }

        _last = node;
        return this;
    }

    private Func<BehaviorContext, bool> ResolveCondition(string conditionKey)
    {
        if (_registry == null)
            throw new InvalidOperationException($"Condition '{conditionKey}' requires a BehaviorRegistry on the builder.");
        return _registry.GetCondition(conditionKey);
    }

    private Func<BehaviorContext, NodeStatus> ResolveAction(string actionKey)
    {
        if (_registry == null)
            throw new InvalidOperationException($"Action '{actionKey}' requires a BehaviorRegistry on the builder.");
        return _registry.GetAction(actionKey);
    }

    private static void Validate(BehaviorNode node)
    {
        if (node is DecoratorNode && node.Children.Count != 1)
            throw new InvalidOperationException($"Decorator '{node.Name}' requires exactly one child.");
        for (int i = 0; i < node.Children.Count; i++)
            Validate(node.Children[i]);
    }
}
