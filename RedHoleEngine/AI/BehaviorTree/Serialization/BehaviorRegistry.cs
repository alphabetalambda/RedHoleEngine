namespace RedHoleEngine.AI.BehaviorTrees;

/// <summary>
/// Named conditions and actions that a JSON tree is allowed to call.
/// Delegates registered here are shared by every compiled instance, so they
/// must keep per-agent state on the blackboard (or be pure).
/// </summary>
public sealed class BehaviorRegistry
{
    private readonly Dictionary<string, Func<BehaviorContext, bool>> _conditions = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Func<BehaviorContext, NodeStatus>> _actions = new(StringComparer.Ordinal);

    public BehaviorRegistry RegisterCondition(string key, Func<BehaviorContext, bool> condition)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        ArgumentNullException.ThrowIfNull(condition);
        if (!_conditions.TryAdd(key, condition))
            throw new InvalidOperationException($"Condition '{key}' is already registered.");
        return this;
    }

    public BehaviorRegistry RegisterAction(string key, Func<BehaviorContext, NodeStatus> action)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        ArgumentNullException.ThrowIfNull(action);
        if (!_actions.TryAdd(key, action))
            throw new InvalidOperationException($"Action '{key}' is already registered.");
        return this;
    }

    public bool ContainsCondition(string key) => key != null && _conditions.ContainsKey(key);

    public bool ContainsAction(string key) => key != null && _actions.ContainsKey(key);

    public Func<BehaviorContext, bool> GetCondition(string key)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        if (_conditions.TryGetValue(key, out var condition))
            return condition;
        throw new KeyNotFoundException($"No behavior condition is registered for '{key}'.");
    }

    public Func<BehaviorContext, NodeStatus> GetAction(string key)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        if (_actions.TryGetValue(key, out var action))
            return action;
        throw new KeyNotFoundException($"No behavior action is registered for '{key}'.");
    }
}
