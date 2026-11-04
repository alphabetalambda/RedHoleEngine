namespace RedHoleEngine.AI.BehaviorTrees;

/// <summary>
/// A node with exactly one child. Decorators change that child's result
/// (invert it, delay it, repeat it, or refuse to run it).
/// </summary>
public abstract class DecoratorNode : BehaviorNode
{
    protected DecoratorNode(string nodeType, string? name)
        : base(nodeType, name)
    {
    }

    protected BehaviorNode Child => Children.Count == 1
        ? Children[0]
        : throw new InvalidOperationException($"{NodeType} '{Name}' requires exactly one child.");

    protected override bool CanAddChild(int currentCount) => currentCount == 0;
}
