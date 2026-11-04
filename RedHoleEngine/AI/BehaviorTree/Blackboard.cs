namespace RedHoleEngine.AI.BehaviorTrees;

/// <summary>
/// Per-agent key/value memory shared by the behavior tree, perception, and the director.
/// Values are stored as objects, so a read must request the same type that was written.
/// </summary>
public sealed class Blackboard
{
    private readonly Dictionary<string, object?> _values = new(StringComparer.Ordinal);

    public int Count => _values.Count;

    public IEnumerable<string> Keys => _values.Keys;

    public void Set<T>(string key, T value)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        _values[key] = value;
    }

    public bool Has(string key)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        return _values.ContainsKey(key);
    }

    public bool TryGet<T>(string key, out T value)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        if (_values.TryGetValue(key, out var raw) && raw is T typed)
        {
            value = typed;
            return true;
        }

        value = default!;
        return false;
    }

    public T Get<T>(string key)
    {
        if (!TryGet<T>(key, out var value))
            throw new KeyNotFoundException($"Blackboard has no '{key}' of type {typeof(T).Name}.");
        return value;
    }

    public T GetOr<T>(string key, T fallback)
    {
        return TryGet<T>(key, out var value) ? value : fallback;
    }

    public bool Remove(string key)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        return _values.Remove(key);
    }

    public void Clear() => _values.Clear();
}
