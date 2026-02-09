using System.Numerics;
using RedHoleEngine.Physics;
using RedHoleEngine.Physics.Constraints;

namespace RedHoleEngine.Tests.Physics;

public class LinkConstraintTests
{
    private const float Epsilon = 0.2f;
    private static int _nextEntityId = 1;

    private static RigidBody CreateBody(Vector3 position)
    {
        return new RigidBody
        {
            EntityId = _nextEntityId++,
            Type = RigidBodyType.Dynamic,
            Mass = 1f,
            Position = position,
            UseGravity = false
        };
    }

    private static PhysicsWorld CreateWorld()
    {
        var world = new PhysicsWorld();
        world.Settings.Gravity = Vector3.Zero;
        return world;
    }

    [Fact]
    public void RigidLink_MaintainsDistance()
    {
        var world = CreateWorld();

        var bodyA = CreateBody(Vector3.Zero);
        var bodyB = CreateBody(new Vector3(2f, 0f, 0f));
        bodyB.LinearVelocity = new Vector3(5f, 0f, 0f);

        world.AddBody(bodyA);
        world.AddBody(bodyB);

        var link = LinkConstraint.Rigid(bodyA.EntityId, bodyB.EntityId, Vector3.Zero, Vector3.Zero, 2f);
        world.ConstraintSolver.AddLink(link);

        for (int i = 0; i < 60; i++)
        {
            world.Step(1f / 60f);
        }

        float distance = Vector3.Distance(bodyA.Position, bodyB.Position);
        Assert.InRange(distance, 2f - Epsilon, 2f + Epsilon);
    }

    [Fact]
    public void ElasticLink_PullsTowardRestLength()
    {
        var world = CreateWorld();

        var bodyA = CreateBody(Vector3.Zero);
        var bodyB = CreateBody(new Vector3(3f, 0f, 0f));

        world.AddBody(bodyA);
        world.AddBody(bodyB);

        var link = LinkConstraint.Elastic(bodyA.EntityId, bodyB.EntityId, Vector3.Zero, Vector3.Zero, 2f, 500f, 5f);
        world.ConstraintSolver.AddLink(link);

        for (int i = 0; i < 60; i++)
        {
            world.Step(1f / 60f);
        }

        float distance = Vector3.Distance(bodyA.Position, bodyB.Position);
        Assert.True(distance < 3f);
    }

    [Fact]
    public void PlasticLink_Yields_IncreasesRestLength()
    {
        var world = CreateWorld();

        var bodyA = CreateBody(Vector3.Zero);
        var bodyB = CreateBody(new Vector3(2f, 0f, 0f));

        world.AddBody(bodyA);
        world.AddBody(bodyB);

        var link = LinkConstraint.Plastic(bodyA.EntityId, bodyB.EntityId, Vector3.Zero, Vector3.Zero, 1f, 500f, 1.1f, 3f);
        link.PlasticRate = 1f;
        world.ConstraintSolver.AddLink(link);

        world.Step(1f);

        Assert.True(link.CurrentRestLength > link.RestLength);
        Assert.NotEqual(LinkState.Broken, link.State);
    }

    [Fact]
    public void PlasticLink_Breaks_WhenOverThreshold()
    {
        var world = CreateWorld();

        var bodyA = CreateBody(Vector3.Zero);
        var bodyB = CreateBody(new Vector3(3f, 0f, 0f));

        world.AddBody(bodyA);
        world.AddBody(bodyB);

        var link = LinkConstraint.Plastic(bodyA.EntityId, bodyB.EntityId, Vector3.Zero, Vector3.Zero, 1f, 500f, 1.1f, 1.5f);
        world.ConstraintSolver.AddLink(link);

        world.Step(1f / 30f);

        Assert.Equal(LinkState.Broken, link.State);
    }

    [Fact]
    public void RopeLink_GoesSlack_WhenCompressed()
    {
        var world = CreateWorld();

        var bodyA = CreateBody(Vector3.Zero);
        var bodyB = CreateBody(new Vector3(0.5f, 0f, 0f));

        world.AddBody(bodyA);
        world.AddBody(bodyB);

        var link = LinkConstraint.Rope(bodyA.EntityId, bodyB.EntityId, Vector3.Zero, Vector3.Zero, 1f, 200f, 5f, 0f);
        world.ConstraintSolver.AddLink(link);

        world.Step(1f / 60f);

        Assert.Equal(LinkState.Slack, link.State);
    }

    [Fact]
    public void ChainLink_Breaks_WhenSegmentOverstretched()
    {
        var world = CreateWorld();

        var bodyA = CreateBody(Vector3.Zero);
        var bodyB = CreateBody(new Vector3(2f, 0f, 0f));
        var bodyC = CreateBody(new Vector3(4f, 0f, 0f));

        world.AddBody(bodyA);
        world.AddBody(bodyB);
        world.AddBody(bodyC);

        var nodes = new List<int> { bodyA.EntityId, bodyB.EntityId, bodyC.EntityId };
        var anchors = new List<Vector3> { Vector3.Zero, Vector3.Zero, Vector3.Zero };

        var chain = LinkChain.CreateFromPath(nodes, anchors, 1f, LinkType.Plastic, 400f, 5f, 0f);
        world.ConstraintSolver.AddChain(chain);

        world.Step(1f / 30f);

        Assert.Contains(chain.Links, link => link.State == LinkState.Broken);
    }

    [Fact]
    public void MeshIntegrity_Decreases_WhenLinkBroken()
    {
        var nodes = new List<int> { 1, 2, 3, 4 };
        var mesh = LinkMesh.CreateCloth(nodes, 2, 2, 1f, 200f, 5f);

        int totalLinks = mesh.StructuralLinks.Count + mesh.ShearLinks.Count + mesh.BendLinks.Count;
        Assert.True(totalLinks > 0);

        mesh.StructuralLinks[0].State = LinkState.Broken;
        float expected = (totalLinks - 1) / (float)totalLinks;

        Assert.Equal(expected, mesh.GetIntegrity(), 3);
    }
}
