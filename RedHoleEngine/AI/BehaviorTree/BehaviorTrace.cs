using System.Text;

namespace RedHoleEngine.AI.BehaviorTrees;

/// <summary>
/// One line of a recorded tick, in pre-order (parent first, status filled in after children).
/// </summary>
public readonly record struct BehaviorTraceEntry(
    int Depth,
    string NodeType,
    string Name,
    NodeStatus Status,
    bool BlockedByGate);

/// <summary>
/// Optional per-tick log of which nodes ran and what they returned.
/// Allocated only when <see cref="BehaviorTree.RecordTrace"/> is set.
/// </summary>
public sealed class BehaviorTrace
{
    private readonly List<BehaviorTraceEntry> _entries = new();

    public IReadOnlyList<BehaviorTraceEntry> Entries => _entries;

    internal int Begin(int depth, string nodeType, string name)
    {
        _entries.Add(new BehaviorTraceEntry(depth, nodeType, name, NodeStatus.Failure, false));
        return _entries.Count - 1;
    }

    internal void Complete(int index, NodeStatus status, bool blockedByGate)
    {
        if (index < 0 || index >= _entries.Count)
            return;

        var entry = _entries[index];
        _entries[index] = entry with { Status = status, BlockedByGate = blockedByGate };
    }

    public override string ToString()
    {
        var builder = new StringBuilder();
        foreach (var entry in _entries)
        {
            builder.Append(' ', entry.Depth * 2);
            builder.Append(entry.NodeType);
            builder.Append(' ');
            builder.Append(entry.Name);
            builder.Append(" -> ");
            builder.Append(entry.Status);
            if (entry.BlockedByGate)
                builder.Append(" (gated)");
            builder.AppendLine();
        }

        return builder.ToString();
    }
}
