using System.Text.Json;
using System.Text.Json.Serialization;

namespace RedHoleEngine.AI.BehaviorTrees;

/// <summary>
/// Designer-facing description of a tree. This is the shareable asset:
/// <see cref="Compile"/> stamps out a fresh stateful <see cref="BehaviorTree"/> per agent.
/// </summary>
public sealed class BehaviorTreeDocument
{
    public string Name { get; set; } = "behavior";

    public BehaviorNodeDocument? Root { get; set; }

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    public string ToJson() => JsonSerializer.Serialize(this, Options);

    public static BehaviorTreeDocument FromJson(string json)
    {
        ArgumentException.ThrowIfNullOrEmpty(json);
        return JsonSerializer.Deserialize<BehaviorTreeDocument>(json, Options)
            ?? throw new InvalidDataException("Behavior tree JSON was empty.");
    }

    public static BehaviorTreeDocument FromTree(BehaviorTree tree)
    {
        ArgumentNullException.ThrowIfNull(tree);
        return new BehaviorTreeDocument
        {
            Name = tree.Name,
            Root = FromNode(tree.Root)
        };
    }

    public BehaviorTree Compile(BehaviorRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);
        if (Root == null)
            throw new InvalidDataException($"Behavior tree '{Name}' has no root.");
        if (string.IsNullOrWhiteSpace(Name))
            throw new InvalidDataException("Behavior tree document has no name.");

        return new BehaviorTree(Name, CompileNode(Root, registry, Name));
    }

    private static BehaviorNodeDocument FromNode(BehaviorNode node)
    {
        var doc = new BehaviorNodeDocument
        {
            Type = node.NodeType,
            Name = node.Name,
            Gate = node.GateId
        };

        switch (node)
        {
            case SelectorNode selector:
                doc.Reactive = selector.Reactive;
                doc.Children = ExportChildren(node);
                break;
            case SequenceNode sequence:
                doc.Memory = sequence.Memory;
                doc.Children = ExportChildren(node);
                break;
            case ParallelNode parallel:
                doc.SuccessPolicy = parallel.SuccessPolicy;
                doc.FailurePolicy = parallel.FailurePolicy;
                doc.Children = ExportChildren(node);
                break;
            case InverterNode:
                doc.Child = FromNode(OnlyChild(node));
                break;
            case CooldownNode cooldown:
                doc.Seconds = cooldown.Seconds;
                doc.StartOnSuccess = cooldown.StartOnSuccess;
                doc.StartOnFailure = cooldown.StartOnFailure;
                doc.Child = FromNode(OnlyChild(node));
                break;
            case RepeatNode repeat:
                doc.Count = repeat.Count;
                doc.Child = FromNode(OnlyChild(node));
                break;
            case ConditionGuardNode guard:
                doc.Condition = RequireKey(guard.ConditionKey, guard, "condition");
                doc.Child = FromNode(OnlyChild(node));
                break;
            case ConditionNode condition:
                doc.Condition = RequireKey(condition.ConditionKey, condition, "condition");
                break;
            case ActionNode action:
                doc.Action = RequireKey(action.ActionKey, action, "action");
                break;
            default:
                throw new InvalidOperationException(
                    $"Cannot export node '{node.Name}' of type {node.GetType().Name}. JSON export covers the built-in node set.");
        }

        return doc;
    }

    private static BehaviorNode CompileNode(BehaviorNodeDocument doc, BehaviorRegistry registry, string path)
    {
        if (doc == null)
            throw new InvalidDataException($"{path}: missing node.");
        if (string.IsNullOrWhiteSpace(doc.Type))
            throw new InvalidDataException($"{path}: node is missing a type.");

        var type = doc.Type.Trim().ToLowerInvariant();
        BehaviorNode node = type switch
        {
            "selector" => new SelectorNode(doc.Name, doc.Reactive ?? true),
            "sequence" => new SequenceNode(doc.Name, doc.Memory ?? true),
            "parallel" => new ParallelNode(
                doc.Name,
                doc.SuccessPolicy ?? ParallelPolicy.RequireAll,
                doc.FailurePolicy ?? ParallelPolicy.RequireOne),
            "inverter" => new InverterNode(doc.Name),
            "cooldown" => new CooldownNode(
                doc.Name,
                doc.Seconds ?? throw new InvalidDataException($"{path}: cooldown requires 'seconds'."),
                doc.StartOnSuccess ?? true,
                doc.StartOnFailure ?? false),
            "repeat" => new RepeatNode(
                doc.Name,
                doc.Count ?? throw new InvalidDataException($"{path}: repeat requires 'count' (-1 repeats until failure).")),
            "guard" => new ConditionGuardNode(
                doc.Name,
                registry.GetCondition(Require(doc.Condition, path, "condition")),
                doc.Condition),
            "condition" => new ConditionNode(
                doc.Name,
                registry.GetCondition(Require(doc.Condition, path, "condition")),
                doc.Condition),
            "action" => new ActionNode(
                doc.Name,
                registry.GetAction(Require(doc.Action, path, "action")),
                doc.Action),
            _ => throw new InvalidDataException($"{path}: unknown node type '{doc.Type}'.")
        };

        node.GateId = string.IsNullOrWhiteSpace(doc.Gate) ? null : doc.Gate;

        switch (type)
        {
            case "selector":
            case "sequence":
            case "parallel":
                if (doc.Child != null)
                    throw new InvalidDataException($"{path}: composite '{type}' uses 'children', not 'child'.");
                if (doc.Children != null)
                {
                    for (int i = 0; i < doc.Children.Count; i++)
                    {
                        var childPath = $"{path}/{doc.Children[i].Name ?? doc.Children[i].Type ?? i.ToString()}";
                        node.AddChild(CompileNode(doc.Children[i], registry, childPath));
                    }
                }
                break;
            case "inverter":
            case "cooldown":
            case "repeat":
            case "guard":
                var childDoc = RequireChild(doc, path);
                node.AddChild(CompileNode(childDoc, registry, $"{path}/{childDoc.Name ?? childDoc.Type}"));
                break;
            case "condition":
            case "action":
                if (doc.Child != null || (doc.Children is { Count: > 0 }))
                    throw new InvalidDataException($"{path}: leaf '{type}' cannot have children.");
                break;
        }

        return node;
    }

    private static BehaviorNodeDocument RequireChild(BehaviorNodeDocument doc, string path)
    {
        if (doc.Child != null)
        {
            if (doc.Children is { Count: > 0 })
                throw new InvalidDataException($"{path}: specify either 'child' or 'children' for a decorator, not both.");
            return doc.Child;
        }

        if (doc.Children is { Count: 1 })
            return doc.Children[0];

        throw new InvalidDataException($"{path}: decorator '{doc.Type}' requires exactly one child.");
    }

    private static List<BehaviorNodeDocument> ExportChildren(BehaviorNode node)
    {
        var children = new List<BehaviorNodeDocument>(node.Children.Count);
        for (int i = 0; i < node.Children.Count; i++)
            children.Add(FromNode(node.Children[i]));
        return children;
    }

    private static BehaviorNode OnlyChild(BehaviorNode node)
    {
        if (node.Children.Count != 1)
            throw new InvalidOperationException($"Decorator '{node.Name}' requires exactly one child to export.");
        return node.Children[0];
    }

    private static string RequireKey(string? key, BehaviorNode node, string slot)
    {
        if (string.IsNullOrWhiteSpace(key))
            throw new InvalidOperationException(
                $"Node '{node.Name}' has no {slot} registry id. JSON export needs leaves created from a BehaviorRegistry.");
        return key;
    }

    private static string Require(string? value, string path, string slot)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidDataException($"{path}: missing '{slot}'.");
        return value;
    }
}

/// <summary>One node in a <see cref="BehaviorTreeDocument"/>.</summary>
public sealed class BehaviorNodeDocument
{
    public string? Type { get; set; }
    public string? Name { get; set; }
    public string? Gate { get; set; }
    public bool? Memory { get; set; }
    public bool? Reactive { get; set; }
    public ParallelPolicy? SuccessPolicy { get; set; }
    public ParallelPolicy? FailurePolicy { get; set; }
    public float? Seconds { get; set; }
    public bool? StartOnSuccess { get; set; }
    public bool? StartOnFailure { get; set; }
    public int? Count { get; set; }
    public string? Condition { get; set; }
    public string? Action { get; set; }
    public List<BehaviorNodeDocument>? Children { get; set; }
    public BehaviorNodeDocument? Child { get; set; }
}
