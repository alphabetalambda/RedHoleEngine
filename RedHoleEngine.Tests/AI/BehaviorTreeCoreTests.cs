using RedHoleEngine.AI.BehaviorTrees;

namespace RedHoleEngine.Tests.AI;

public class BehaviorTreeCoreTests
{
    [Fact]
    public void Sequence_SucceedsWhenEveryChildSucceeds()
    {
        var sequence = new SequenceNode("all");
        sequence.AddChild(new ActionNode("a", _ => NodeStatus.Success));
        sequence.AddChild(new ActionNode("b", _ => NodeStatus.Success));
        var tree = new BehaviorTree("seq", sequence);

        var status = tree.Tick(new Blackboard());

        Assert.Equal(NodeStatus.Success, status);
    }

    [Fact]
    public void Sequence_FailsFastAndSkipsLaterChildren()
    {
        int later = 0;
        var sequence = new SequenceNode("all");
        sequence.AddChild(new ActionNode("fail", _ => NodeStatus.Failure));
        sequence.AddChild(new ActionNode("later", _ =>
        {
            later++;
            return NodeStatus.Success;
        }));

        var status = new BehaviorTree("seq", sequence).Tick(new Blackboard());

        Assert.Equal(NodeStatus.Failure, status);
        Assert.Equal(0, later);
    }

    [Fact]
    public void Sequence_WithMemory_ResumesTheRunningChild()
    {
        int first = 0;
        int second = 0;
        var sequence = new SequenceNode("steps", memory: true);
        sequence.AddChild(new ActionNode("first", _ =>
        {
            first++;
            return NodeStatus.Success;
        }));
        sequence.AddChild(new ActionNode("second", _ =>
        {
            second++;
            return second < 2 ? NodeStatus.Running : NodeStatus.Success;
        }));
        var tree = new BehaviorTree("seq", sequence);
        var board = new Blackboard();

        Assert.Equal(NodeStatus.Running, tree.Tick(board));
        Assert.Equal(NodeStatus.Success, tree.Tick(board));
        Assert.Equal(1, first);
        Assert.Equal(2, second);
    }

    [Fact]
    public void Sequence_WithoutMemory_RestartsFromTheFirstChild()
    {
        int first = 0;
        var sequence = new SequenceNode("steps", memory: false);
        sequence.AddChild(new ActionNode("first", _ =>
        {
            first++;
            return NodeStatus.Success;
        }));
        sequence.AddChild(new ActionNode("second", _ => NodeStatus.Running));
        var tree = new BehaviorTree("seq", sequence);
        var board = new Blackboard();

        tree.Tick(board);
        tree.Tick(board);

        Assert.Equal(2, first);
    }

    [Fact]
    public void EmptySequence_Succeeds_EmptySelector_Fails()
    {
        Assert.Equal(NodeStatus.Success, new BehaviorTree("s", new SequenceNode("s")).Tick(new Blackboard()));
        Assert.Equal(NodeStatus.Failure, new BehaviorTree("s", new SelectorNode("s")).Tick(new Blackboard()));
    }

    [Fact]
    public void Selector_PicksTheFirstSuccess()
    {
        string? chosen = null;
        var selector = new SelectorNode("pick");
        selector.AddChild(new ActionNode("no", _ => NodeStatus.Failure));
        selector.AddChild(new ActionNode("yes", ctx =>
        {
            chosen = "yes";
            return NodeStatus.Success;
        }));
        selector.AddChild(new ActionNode("later", _ =>
        {
            chosen = "later";
            return NodeStatus.Success;
        }));

        var status = new BehaviorTree("sel", selector).Tick(new Blackboard());

        Assert.Equal(NodeStatus.Success, status);
        Assert.Equal("yes", chosen);
    }

    [Fact]
    public void ReactiveSelector_PreemptsALowerRunningChild()
    {
        bool highReady = false;
        int lowTicks = 0;
        var selector = new SelectorNode("root", reactive: true);
        selector.AddChild(new ActionNode("high", _ => highReady ? NodeStatus.Success : NodeStatus.Failure));
        selector.AddChild(new ActionNode("low", _ =>
        {
            lowTicks++;
            return NodeStatus.Running;
        }));
        var tree = new BehaviorTree("sel", selector);
        var board = new Blackboard();

        Assert.Equal(NodeStatus.Running, tree.Tick(board));
        highReady = true;
        Assert.Equal(NodeStatus.Success, tree.Tick(board));
        Assert.Equal(1, lowTicks);
    }

    [Fact]
    public void NonReactiveSelector_StaysOnTheRunningChild()
    {
        int phase = 0;
        int highTicks = 0;
        int lowTicks = 0;
        var selector = new SelectorNode("root", reactive: false);
        selector.AddChild(new ActionNode("high", _ =>
        {
            highTicks++;
            return phase == 0 ? NodeStatus.Failure : NodeStatus.Success;
        }));
        selector.AddChild(new ActionNode("low", _ =>
        {
            lowTicks++;
            return lowTicks < 3 ? NodeStatus.Running : NodeStatus.Success;
        }));
        var tree = new BehaviorTree("sel", selector);
        var board = new Blackboard();

        Assert.Equal(NodeStatus.Running, tree.Tick(board));
        phase = 1;
        Assert.Equal(NodeStatus.Running, tree.Tick(board));
        Assert.Equal(NodeStatus.Success, tree.Tick(board));
        Assert.Equal(NodeStatus.Success, tree.Tick(board));

        Assert.Equal(2, highTicks);
        Assert.Equal(3, lowTicks);
    }

    [Fact]
    public void Parallel_RequireAll_LatchesAFinishedSibling()
    {
        int finishedCalls = 0;
        int pendingCalls = 0;
        var parallel = new ParallelNode("both", ParallelPolicy.RequireAll, ParallelPolicy.RequireOne);
        parallel.AddChild(new ActionNode("done", _ =>
        {
            finishedCalls++;
            return NodeStatus.Success;
        }));
        parallel.AddChild(new ActionNode("wait", _ =>
        {
            pendingCalls++;
            return pendingCalls >= 2 ? NodeStatus.Success : NodeStatus.Running;
        }));
        var tree = new BehaviorTree("par", parallel);
        var board = new Blackboard();

        Assert.Equal(NodeStatus.Running, tree.Tick(board));
        Assert.Equal(NodeStatus.Success, tree.Tick(board));
        Assert.Equal(1, finishedCalls);
        Assert.Equal(2, pendingCalls);
    }

    [Fact]
    public void Parallel_RequireOneFailure_FailsWhileASiblingIsStillRunning()
    {
        var parallel = new ParallelNode("either-fail", ParallelPolicy.RequireAll, ParallelPolicy.RequireOne);
        parallel.AddChild(new ActionNode("bad", _ => NodeStatus.Failure));
        parallel.AddChild(new ActionNode("still", _ => NodeStatus.Running));

        Assert.Equal(NodeStatus.Failure, new BehaviorTree("par", parallel).Tick(new Blackboard()));
    }

    [Fact]
    public void Parallel_RequireAllFailure_KeepsRunningAfterASingleFailure()
    {
        var parallel = new ParallelNode("all-fail", ParallelPolicy.RequireAll, ParallelPolicy.RequireAll);
        parallel.AddChild(new ActionNode("bad", _ => NodeStatus.Failure));
        parallel.AddChild(new ActionNode("still", _ => NodeStatus.Running));

        Assert.Equal(NodeStatus.Running, new BehaviorTree("par", parallel).Tick(new Blackboard()));
    }

    [Fact]
    public void Inverter_SwapsTerminalStatusAndPassesRunningThrough()
    {
        Assert.Equal(NodeStatus.Failure, TickInverter(NodeStatus.Success));
        Assert.Equal(NodeStatus.Success, TickInverter(NodeStatus.Failure));
        Assert.Equal(NodeStatus.Running, TickInverter(NodeStatus.Running));
    }

    [Fact]
    public void Cooldown_BlocksTheChildUntilTheDurationElapses()
    {
        int calls = 0;
        var cooldown = new CooldownNode("cd", seconds: 1f);
        cooldown.AddChild(new ActionNode("act", _ =>
        {
            calls++;
            return NodeStatus.Success;
        }));
        var tree = new BehaviorTree("cd", cooldown);
        var board = new Blackboard();

        Assert.Equal(NodeStatus.Success, tree.Tick(board, deltaTime: 0.1f));
        Assert.Equal(1f, cooldown.Remaining);
        Assert.Equal(NodeStatus.Failure, tree.Tick(board, deltaTime: 0.25f));
        Assert.Equal(0.75f, cooldown.Remaining);
        Assert.Equal(1, calls);

        Assert.Equal(NodeStatus.Failure, tree.Tick(board, deltaTime: 0.75f));
        Assert.Equal(0f, cooldown.Remaining);
        Assert.Equal(NodeStatus.Success, tree.Tick(board, deltaTime: 0.1f));
        Assert.Equal(2, calls);
    }

    [Fact]
    public void Cooldown_SurvivesAbort_AndClearsOnReset()
    {
        var cooldown = new CooldownNode("cd", seconds: 2f);
        cooldown.AddChild(new ActionNode("act", _ => NodeStatus.Success));
        var board = new Blackboard();
        cooldown.Tick(new BehaviorContext(board));

        cooldown.Abort();
        Assert.Equal(2f, cooldown.Remaining);

        cooldown.Reset();
        Assert.Equal(0f, cooldown.Remaining);
    }

    [Fact]
    public void Repeat_CompletesAfterTheRequestedSuccesses()
    {
        int calls = 0;
        var repeat = new RepeatNode("thrice", count: 3);
        repeat.AddChild(new ActionNode("hit", _ =>
        {
            calls++;
            return NodeStatus.Success;
        }));
        var tree = new BehaviorTree("rep", repeat);
        var board = new Blackboard();

        Assert.Equal(NodeStatus.Running, tree.Tick(board));
        Assert.Equal(NodeStatus.Running, tree.Tick(board));
        Assert.Equal(NodeStatus.Success, tree.Tick(board));
        Assert.Equal(3, calls);
        Assert.Equal(3, repeat.Successes);
    }

    [Fact]
    public void Repeat_UntilFailure_StopsWhenTheChildFails()
    {
        int calls = 0;
        var repeat = new RepeatNode("forever", count: -1);
        repeat.AddChild(new ActionNode("hit", _ =>
        {
            calls++;
            return calls < 3 ? NodeStatus.Success : NodeStatus.Failure;
        }));
        var tree = new BehaviorTree("rep", repeat);
        var board = new Blackboard();

        Assert.Equal(NodeStatus.Running, tree.Tick(board));
        Assert.Equal(NodeStatus.Running, tree.Tick(board));
        Assert.Equal(NodeStatus.Failure, tree.Tick(board));
        Assert.Equal(3, calls);
    }

    [Fact]
    public void Repeat_Abort_RestartsTheSuccessCount()
    {
        var repeat = new RepeatNode("thrice", count: 3);
        repeat.AddChild(new ActionNode("hit", _ => NodeStatus.Success));
        var context = new BehaviorContext(new Blackboard());

        repeat.Tick(context);
        Assert.Equal(1, repeat.Successes);
        repeat.Abort();
        Assert.Equal(0, repeat.Successes);
    }

    [Fact]
    public void ConditionGuard_SkipsTheChildUntilThePredicateIsTrue()
    {
        bool open = false;
        int calls = 0;
        var guard = new ConditionGuardNode("gate", _ => open);
        guard.AddChild(new ActionNode("act", _ =>
        {
            calls++;
            return NodeStatus.Running;
        }));
        var tree = new BehaviorTree("g", guard);
        var board = new Blackboard();

        Assert.Equal(NodeStatus.Failure, tree.Tick(board));
        Assert.Equal(0, calls);

        open = true;
        Assert.Equal(NodeStatus.Running, tree.Tick(board));
        open = false;
        Assert.Equal(NodeStatus.Failure, tree.Tick(board));
        Assert.Equal(1, calls);
    }

    [Fact]
    public void Gate_FailsClosedUntilUnlocked()
    {
        int calls = 0;
        var action = new ActionNode("hidden", _ =>
        {
            calls++;
            return NodeStatus.Success;
        })
        {
            GateId = "vent-search"
        };
        var gates = new BehaviorGateSet();
        gates.Lock("vent-search");
        var tree = new BehaviorTree("g", action);
        var board = new Blackboard();

        Assert.Equal(NodeStatus.Failure, tree.Tick(board, gates));
        Assert.Equal(0, calls);

        gates.Unlock("vent-search");
        Assert.Equal(NodeStatus.Success, tree.Tick(board, gates));
        Assert.Equal(1, calls);
    }

    [Fact]
    public void UndefinedGate_IsOpen()
    {
        var gates = new BehaviorGateSet();
        Assert.True(gates.IsUnlocked(null));
        Assert.True(gates.IsUnlocked("never-defined"));
        gates.Lock("never-defined");
        Assert.False(gates.IsUnlocked("never-defined"));
    }

    [Fact]
    public void Blackboard_StoresAndRejectsMismatchedTypes()
    {
        var board = new Blackboard();
        board.Set("n", 3);
        board.Set("flag", true);

        Assert.True(board.TryGet("n", out int number));
        Assert.Equal(3, number);
        Assert.False(board.TryGet("n", out float _));
        Assert.Equal(7, board.GetOr("missing", 7));
        Assert.True(board.Remove("flag"));
        Assert.False(board.Has("flag"));
    }

    [Fact]
    public void Trace_RecordsPreOrderStatusAndAClosedGate()
    {
        var root = new SelectorNode("root");
        root.AddChild(new ActionNode("locked", _ => NodeStatus.Success) { GateId = "locked" });
        root.AddChild(new ActionNode("fallback", _ => NodeStatus.Success));
        var tree = new BehaviorTree("traced", root) { RecordTrace = true };
        var gates = new BehaviorGateSet();
        gates.Lock("locked");

        tree.Tick(new Blackboard(), gates);
        var text = tree.DescribeLastTick();

        Assert.Contains("selector root -> Success", text);
        Assert.Contains("action locked -> Failure (gated)", text);
        Assert.Contains("action fallback -> Success", text);
        Assert.Contains("locked", BehaviorDebug.Format(tree));
    }

    [Fact]
    public void SeparateTreeInstances_DoNotShareRunningState()
    {
        BehaviorTree Make()
        {
            int ticks = 0;
            var node = new ActionNode("run", _ =>
            {
                ticks++;
                return ticks < 2 ? NodeStatus.Running : NodeStatus.Success;
            });
            return new BehaviorTree("t", node);
        }

        var first = Make();
        var second = Make();
        Assert.Equal(NodeStatus.Running, first.Tick(new Blackboard()));
        Assert.Equal(NodeStatus.Running, second.Tick(new Blackboard()));
        Assert.Equal(NodeStatus.Success, first.Tick(new Blackboard()));
    }

    private static NodeStatus TickInverter(NodeStatus childStatus)
    {
        var inverter = new InverterNode("not");
        inverter.AddChild(new ActionNode("child", _ => childStatus));
        return new BehaviorTree("inv", inverter).Tick(new Blackboard());
    }
}
