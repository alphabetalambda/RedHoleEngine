using System.Numerics;
using RedHoleEngine.Physics;
using RedHoleEngine.Physics.Constraints;

namespace RedHoleEngine.Tests.Physics;

public class ChainSplitAndAngularMassTests
{
    private static int _nextId = 5000;

    private static RigidBody Body(Vector3 position) => new()
    {
        EntityId = _nextId++,
        Type = RigidBodyType.Dynamic,
        Mass = 1f,
        Position = position,
        UseGravity = false,
        LinearDamping = 0f,
        AngularDamping = 0f
    };

    [Fact]
    public void BreakingMiddleLink_SplitsChainIntoTwoNewChains()
    {
        var world = new PhysicsWorld();
        world.Settings.Gravity = Vector3.Zero;
        // 5 nodes, 4 links; the gap between node 2 and node 3 is 4x the rest length.
        float[] xs = { 0f, 1f, 2f, 6f, 7f };
        var bodies = xs.Select(x => Body(new Vector3(x, 0, 0))).ToList();
        foreach (var b in bodies) world.AddBody(b);

        var chain = LinkChain.CreateFromPath(
            bodies.Select(b => b.EntityId).ToList(),
            Enumerable.Repeat(Vector3.Zero, 5).ToList(),
            1f, LinkType.Plastic, 100f, 0f);
        foreach (var l in chain.Links) l.BreakThreshold = 2f;
        world.ConstraintSolver.AddChain(chain);
        int originalId = chain.Id;

        var events = new List<ChainBreakEvent>();
        world.ConstraintSolver.OnChainBreak += events.Add;
        world.Step(1f / 60f);

        var evt = Assert.Single(events);
        Assert.Equal(originalId, evt.OriginalChainId);
        Assert.Equal(2, evt.BreakIndex);
        Assert.True(evt.NewChainIdA > 0);
        Assert.True(evt.NewChainIdB > 0);
        Assert.NotEqual(evt.NewChainIdA, evt.NewChainIdB);

        var a = world.ConstraintSolver.GetChain(evt.NewChainIdA)!;
        var b2 = world.ConstraintSolver.GetChain(evt.NewChainIdB)!;
        Assert.Equal(2, a.Links.Count);
        Assert.Single(b2.Links);
        Assert.Equal(new[] { bodies[0].EntityId, bodies[1].EntityId, bodies[2].EntityId }, a.NodeEntities);
        Assert.Equal(new[] { bodies[3].EntityId, bodies[4].EntityId }, b2.NodeEntities);
        Assert.All(a.Links, l => Assert.Equal(a.Id, l.ChainId));
        Assert.All(b2.Links, l => Assert.Equal(b2.Id, l.ChainId));
        Assert.Equal(new[] { 0, 1 }, a.Links.Select(l => l.IndexInCollection));

        var original = world.ConstraintSolver.GetChain(originalId)!;
        Assert.Single(original.Links);
        Assert.Equal(LinkState.Broken, original.Links[0].State);
    }

    [Fact]
    public void BreakingEndLink_LeavesSingleNewChain()
    {
        var world = new PhysicsWorld();
        world.Settings.Gravity = Vector3.Zero;
        float[] xs = { 0f, 1f, 5f };
        var bodies = xs.Select(x => Body(new Vector3(x, 0, 0))).ToList();
        foreach (var b in bodies) world.AddBody(b);
        var chain = LinkChain.CreateFromPath(bodies.Select(b => b.EntityId).ToList(),
            Enumerable.Repeat(Vector3.Zero, 3).ToList(), 1f, LinkType.Plastic, 100f, 0f);
        foreach (var l in chain.Links) l.BreakThreshold = 2f;
        world.ConstraintSolver.AddChain(chain);

        var events = new List<ChainBreakEvent>();
        world.ConstraintSolver.OnChainBreak += events.Add;
        world.Step(1f / 60f);

        var evt = Assert.Single(events);
        Assert.True(evt.NewChainIdA > 0);
        Assert.Equal(-1, evt.NewChainIdB);
    }

    [Fact]
    public void EffectiveMass_IncludesAngularContribution()
    {
        var a = Body(Vector3.Zero);
        var b = Body(new Vector3(0, 0, 4));
        var link = LinkConstraint.Rigid(a.EntityId, b.EntityId, new Vector3(1, 0, 0), Vector3.Zero, 4f);
        link.BodyA = a;
        link.BodyB = b;
        link.UpdateWorldAnchors();
        link.CalculateEffectiveMass();

        // Anchor A is offset perpendicular to the axis (r x n != 0), adding rotational freedom.
        Assert.True(link.EffectiveMass > 0f);
        Assert.True(link.EffectiveMass < 0.5f); // linear-only would be exactly 0.5
    }

    [Fact]
    public void EffectiveMass_CenteredAnchorsMatchLinearOnly()
    {
        var a = Body(Vector3.Zero);
        var b = Body(new Vector3(0, 0, 4));
        var link = LinkConstraint.Rigid(a.EntityId, b.EntityId, Vector3.Zero, Vector3.Zero, 4f);
        link.BodyA = a;
        link.BodyB = b;
        link.UpdateWorldAnchors();
        link.CalculateEffectiveMass();
        Assert.Equal(0.5f, link.EffectiveMass, 1e-5f);
    }
}
