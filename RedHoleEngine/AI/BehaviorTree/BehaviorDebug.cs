using System.Text;

namespace RedHoleEngine.AI.BehaviorTrees;

/// <summary>
/// Text dumps of a tree's shape and of its last tick. Safe to call from a
/// headless test or a debug overlay; nothing here touches rendering.
/// </summary>
public static class BehaviorDebug
{
    public static string Format(BehaviorTree tree)
    {
        ArgumentNullException.ThrowIfNull(tree);
        var builder = new StringBuilder();
        builder.Append(tree.Name);
        builder.Append(" [");
        builder.Append(tree.LastStatus);
        builder.AppendLine("]");
        Write(builder, tree.Root, 0);
        return builder.ToString();
    }

    public static string Format(BehaviorNode node)
    {
        ArgumentNullException.ThrowIfNull(node);
        var builder = new StringBuilder();
        Write(builder, node, 0);
        return builder.ToString();
    }

    private static void Write(StringBuilder builder, BehaviorNode node, int depth)
    {
        builder.Append(' ', depth * 2);
        builder.Append(node.NodeType);
        builder.Append(' ');
        builder.Append(node.Name);
        if (!string.IsNullOrEmpty(node.GateId))
        {
            builder.Append(" gate=");
            builder.Append(node.GateId);
        }

        builder.Append(" [");
        builder.Append(node.LastStatus);
        builder.AppendLine("]");

        for (int i = 0; i < node.Children.Count; i++)
            Write(builder, node.Children[i], depth + 1);
    }
}
