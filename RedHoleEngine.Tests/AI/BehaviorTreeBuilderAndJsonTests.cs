using RedHoleEngine.AI.BehaviorTrees;

namespace RedHoleEngine.Tests.AI;

public class BehaviorTreeBuilderAndJsonTests
{
    [Fact]
    public void Builder_AssemblesATreeThatTicks()
    {
        var tree = new BehaviorTreeBuilder("demo")
            .Selector("root")
                .Sequence("needs-flag")
                    .Condition("flagged", ctx => ctx.Blackboard.TryGet("go", out bool go) && go)
                    .Action("go", _ => NodeStatus.Success)
                .End()
                .Action("idle", _ => NodeStatus.Failure)
            .End()
            .Build();

        var board = new Blackboard();
        Assert.Equal(NodeStatus.Failure, tree.Tick(board));
        board.Set("go", true);
        Assert.Equal(NodeStatus.Success, tree.Tick(board));
    }

    [Fact]
    public void Builder_RejectsAnUnclosedBranchAndAStrayEnd()
    {
        var open = new BehaviorTreeBuilder("demo").Selector("root");
        Assert.Throws<InvalidOperationException>(() => open.Build());

        var closed = new BehaviorTreeBuilder("demo").Action("only", _ => NodeStatus.Success);
        Assert.Throws<InvalidOperationException>(() => closed.End());
    }

    [Fact]
    public void Json_RoundTripsStructureAndBehavior()
    {
        var registry = new BehaviorRegistry()
            .RegisterCondition("demo.ready", ctx => ctx.Blackboard.TryGet("ready", out bool ready) && ready)
            .RegisterAction("demo.act", _ => NodeStatus.Success)
            .RegisterAction("demo.wait", _ => NodeStatus.Running);

        var built = new BehaviorTreeBuilder("demo", registry)
            .Selector("root", reactive: true)
                .Sequence("work", memory: true).Gate("work")
                    .Condition("ready", "demo.ready")
                    .Cooldown(1.5f, "hold", startOnSuccess: true, startOnFailure: false)
                        .Action("act", "demo.act")
                    .End()
                .End()
                .Repeat(-1, "keep-waiting")
                    .Action("wait", "demo.wait")
                .End()
            .End()
            .Build();

        var json = BehaviorTreeDocument.FromTree(built).ToJson();
        var compiled = BehaviorTreeDocument.FromJson(json).Compile(registry);

        Assert.Equal("demo", compiled.Name);
        Assert.Equal("selector", compiled.Root.NodeType);
        var selector = Assert.IsType<SelectorNode>(compiled.Root);
        Assert.True(selector.Reactive);
        Assert.Equal("work", selector.Children[0].GateId);

        var board = new Blackboard();
        Assert.Equal(NodeStatus.Running, compiled.Tick(board));
        board.Set("ready", true);
        var gates = new BehaviorGateSet();
        gates.Lock("work");
        Assert.Equal(NodeStatus.Running, compiled.Tick(board, gates));
        gates.Unlock("work");
        Assert.Equal(NodeStatus.Success, compiled.Tick(board, gates));
    }

    [Fact]
    public void Json_ParallelPoliciesRoundTrip()
    {
        const string json = """
            {
              "name": "together",
              "root": {
                "type": "parallel",
                "name": "both",
                "successPolicy": "requireOne",
                "failurePolicy": "requireAll",
                "children": [
                  { "type": "action", "name": "a", "action": "demo.ok" },
                  { "type": "action", "name": "b", "action": "demo.no" }
                ]
              }
            }
            """;

        var registry = new BehaviorRegistry()
            .RegisterAction("demo.ok", _ => NodeStatus.Success)
            .RegisterAction("demo.no", _ => NodeStatus.Failure);
        var tree = BehaviorTreeDocument.FromJson(json).Compile(registry);
        var parallel = Assert.IsType<ParallelNode>(tree.Root);

        Assert.Equal(ParallelPolicy.RequireOne, parallel.SuccessPolicy);
        Assert.Equal(ParallelPolicy.RequireAll, parallel.FailurePolicy);
        Assert.Equal(NodeStatus.Success, tree.Tick(new Blackboard()));
    }

    [Fact]
    public void Json_RejectsUnknownTypesAndMissingLeaves()
    {
        var registry = new BehaviorRegistry();
        var unknown = BehaviorTreeDocument.FromJson("""{"name":"x","root":{"type":"utility","name":"nope"}}""");
        Assert.Throws<InvalidDataException>(() => unknown.Compile(registry));

        var missing = BehaviorTreeDocument.FromJson("""{"name":"x","root":{"type":"action","name":"a"}}""");
        Assert.Throws<InvalidDataException>(() => missing.Compile(registry));
    }

    [Fact]
    public void Export_RejectsALambdaLeaf()
    {
        var tree = new BehaviorTree("code", new ActionNode("only", _ => NodeStatus.Success));
        Assert.Throws<InvalidOperationException>(() => BehaviorTreeDocument.FromTree(tree));
    }

    [Fact]
    public void Registry_RejectsDuplicateKeys()
    {
        var registry = new BehaviorRegistry().RegisterAction("a", _ => NodeStatus.Success);
        Assert.Throws<InvalidOperationException>(() => registry.RegisterAction("a", _ => NodeStatus.Failure));
        Assert.Throws<KeyNotFoundException>(() => registry.GetCondition("missing"));
    }
}
