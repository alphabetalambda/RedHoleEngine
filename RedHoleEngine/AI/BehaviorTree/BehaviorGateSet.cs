namespace RedHoleEngine.AI.BehaviorTrees;

/// <summary>
/// Named behavior unlocks. A node with a gate id fails closed while that gate is locked,
/// which is how tactics come online over the course of a game without editing the tree.
/// Gates that have never been defined are open, so ungated trees keep working.
/// </summary>
public sealed class BehaviorGateSet
{
    private readonly Dictionary<string, bool> _gates = new(StringComparer.Ordinal);

    public void Define(string gateId, bool unlocked)
    {
        ArgumentException.ThrowIfNullOrEmpty(gateId);
        _gates[gateId] = unlocked;
    }

    public void Unlock(string gateId) => Define(gateId, true);

    public void Lock(string gateId) => Define(gateId, false);

    public bool IsDefined(string gateId)
    {
        ArgumentException.ThrowIfNullOrEmpty(gateId);
        return _gates.ContainsKey(gateId);
    }

    /// <summary>
    /// True when the node may run. A null or empty id is always open.
    /// An unknown id is open until something defines it.
    /// </summary>
    public bool IsUnlocked(string? gateId)
    {
        if (string.IsNullOrEmpty(gateId))
            return true;
        return !_gates.TryGetValue(gateId, out var unlocked) || unlocked;
    }

    public IReadOnlyCollection<string> DefinedGates => _gates.Keys;
}
