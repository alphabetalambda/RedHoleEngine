using System.Numerics;
using RedHoleEngine.Physics.Constraints;

namespace RedHoleEngine.Tests.Physics;

public class LinkChainBehaviorTests
{
    [Fact]
    public void CreateRope_AndRigid_BuildOrderedLinks()
    {
        int[] nodes = { 1, 2, 3, 4 };
        var rope = LinkChain.CreateRope(nodes, segmentLength: 0.5f, localAnchor: new Vector3(0, 1, 0), stiffness: 10f, damping: 1f, slack: 0.05f);
        Assert.Equal(LinkType.Rope, rope.Type);
        Assert.Equal(nodes, rope.NodeEntities);
        Assert.Equal(3, rope.Links.Count);
        Assert.All(rope.Links, link => Assert.Equal(LinkType.Rope, link.Type));
        Assert.Equal(0, rope.Links[0].IndexInCollection);
        Assert.Equal(2, rope.Links[2].IndexInCollection);
        Assert.Equal(1, rope.Links[0].EntityA);
        Assert.Equal(2, rope.Links[0].EntityB);
        Assert.Equal(0.5f, rope.Links[0].RestLength);

        var rigid = LinkChain.CreateRigidChain(nodes, 1f);
        Assert.Equal(LinkType.Rigid, rigid.Type);
        Assert.Equal(3, rigid.Links.Count);
        Assert.All(rigid.Links, link =>
        {
            Assert.Equal(LinkType.Rigid, link.Type);
            Assert.Equal(float.MaxValue, link.Stiffness);
        });
    }

    [Fact]
    public void CreateFromPath_RequiresMatchingAnchors_AndSupportsEveryLinkType()
    {
        var entities = new[] { 10, 11 };
        var anchors = new[] { Vector3.Zero, Vector3.UnitX };
        Assert.Throws<ArgumentException>(() =>
            LinkChain.CreateFromPath(entities, new[] { Vector3.Zero }, 1f));

        foreach (var type in new[] { LinkType.Rigid, LinkType.Elastic, LinkType.Plastic, LinkType.Rope })
        {
            var chain = LinkChain.CreateFromPath(entities, anchors, 2f, type, stiffness: 8f, damping: 0.2f, slack: 0.1f);
            Assert.Equal(type, chain.Links[0].Type);
            Assert.Equal(anchors[0], chain.Links[0].LocalAnchorA);
            Assert.Equal(anchors[1], chain.Links[0].LocalAnchorB);
        }
    }

    [Fact]
    public void Break_Detach_AndPointsFollowWorldAnchors()
    {
        var chain = LinkChain.CreateRope(new[] { 1, 2, 3 }, 1f);
        chain.Break(-1);
        chain.Break(5);
        Assert.All(chain.Links, link => Assert.Equal(LinkState.Active, link.State));

        chain.DetachEnd(detachA: true);
        Assert.Equal(LinkState.Broken, chain.Links[0].State);
        chain.DetachEnd(detachA: false);
        Assert.Equal(LinkState.Broken, chain.Links[1].State);
        chain.OnLinkBroken(0);

        var empty = new LinkChain();
        empty.DetachEnd(true);
        Assert.Empty(empty.GetPoints());
        Assert.Empty(empty.GetPointsWithTangents());

        chain.Links[0].WorldAnchorA = new Vector3(0, 0, 0);
        chain.Links[0].WorldAnchorB = new Vector3(1, 0, 0);
        chain.Links[1].WorldAnchorB = new Vector3(1, 2, 0);
        var points = chain.GetPoints();
        Assert.Equal(3, points.Count);
        Assert.Equal(new Vector3(1, 2, 0), points[2]);

        var withTangents = chain.GetPointsWithTangents();
        Assert.Equal(Vector3.UnitX, withTangents[0].Tangent);
        Assert.Equal(Vector3.UnitY, withTangents[2].Tangent);
        Assert.True(withTangents[1].Tangent.Length() > 0.9f);

        var single = LinkChain.CreateRope(new[] { 1, 2 }, 1f);
        var one = single.GetPointsWithTangents();
        Assert.Equal(2, one.Count);
    }

    [Fact]
    public void AngleLimitFactories_SetTheFlagsTheyDocument()
    {
        var cone = AngleLimits.Cone(0.4f);
        Assert.Equal(AngleConstraintFlags.LimitSwing, cone.Flags);
        Assert.Equal(0.4f, cone.MaxSwing);

        var twist = AngleLimits.TwistAndSwing(0.2f, 0.8f);
        Assert.Equal(AngleConstraintFlags.All, twist.Flags);
        Assert.Equal(-0.2f, twist.MinTwist);
        Assert.Equal(0.8f, twist.MaxSwing);

        var ellipse = AngleLimits.EllipticalCone(0.3f, 0.6f);
        Assert.Equal(AngleConstraintFlags.LimitSwingX | AngleConstraintFlags.LimitSwingY, ellipse.Flags);
        Assert.Equal(0f, ellipse.MaxTwist);

        var twisted = AngleLimits.EllipticalCone(0.3f, 0.6f, maxTwist: 0.1f);
        Assert.True(twisted.Flags.HasFlag(AngleConstraintFlags.LimitTwist));

        var hinge = AngleLimits.Hinge(-0.5f, 1.2f);
        Assert.True(hinge.Flags.HasFlag(AngleConstraintFlags.LimitTwist));
        Assert.Equal(-0.5f, hinge.MinTwist);
        Assert.Equal(1.2f, hinge.MaxTwist);
        Assert.Equal(0.01f, hinge.MaxSwingX);
    }
}
