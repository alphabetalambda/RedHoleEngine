using System.Numerics;
using RedHoleEngine.Physics;
using RedHoleEngine.Physics.Collision;
using RedHoleEngine.Physics.Constraints;

namespace RedHoleEngine.Tests.Physics;

public class PhysicsWorldCoverageTests
{
    private const float Epsilon = 0.02f;
    private static int _nextEntityId = 1;

    private static RigidBody CreateBody(RigidBodyType type = RigidBodyType.Dynamic, float mass = 1f)
    {
        return new RigidBody
        {
            EntityId = _nextEntityId++,
            Type = type,
            Mass = mass,
            LinearDamping = 0f,
            AngularDamping = 0f,
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
    public void Settings_HaveDocumentedDefaults()
    {
        var settings = new PhysicsSettings();

        Assert.Equal(new Vector3(0f, -9.81f, 0f), settings.Gravity);
        Assert.Equal(8, settings.VelocityIterations);
        Assert.Equal(3, settings.PositionIterations);
        Assert.Equal(0.2f, settings.BaumgarteScale);
        Assert.Equal(500f, settings.MaxLinearVelocity);
        Assert.Equal(50f, settings.MaxAngularVelocity);
        Assert.Equal(0.01f, settings.AllowedPenetration);
    }

    [Fact]
    public void CollisionPair_OrdersIndicesAndComparesByValue()
    {
        var swapped = new CollisionPair(5, 2);
        var ordered = new CollisionPair(2, 5);

        Assert.Equal(2, swapped.BodyAIndex);
        Assert.Equal(5, swapped.BodyBIndex);
        Assert.Equal(ordered, swapped);
        Assert.Equal(ordered.GetHashCode(), swapped.GetHashCode());
        Assert.True(ordered.Equals((object)swapped));
        Assert.False(ordered.Equals(null));
        Assert.False(ordered.Equals(new CollisionPair(1, 2)));
        Assert.False(ordered.Equals("pair"));
    }

    [Fact]
    public void AddBody_DuplicateEntity_KeepsTheFirstBody()
    {
        var world = CreateWorld();
        var first = new RigidBody { EntityId = 7, Position = Vector3.Zero };
        var second = new RigidBody { EntityId = 7, Position = new Vector3(3f, 0f, 0f) };

        world.AddBody(first);
        world.AddBody(second);

        Assert.Equal(1, world.BodyCount);
        Assert.Same(first, world.GetBody(7));
        Assert.Same(first, world.GetBodyByEntityId(7));
    }

    [Fact]
    public void GetBody_UnknownEntity_ReturnsNull()
    {
        var world = CreateWorld();

        Assert.Null(world.GetBody(12345));
        Assert.Null(world.GetBodyByEntityId(12345));
    }

    [Fact]
    public void RemoveBody_UnknownBody_IsNoOp()
    {
        var world = CreateWorld();
        world.AddBody(CreateBody());

        world.RemoveBody(CreateBody());

        Assert.Equal(1, world.BodyCount);
    }

    [Fact]
    public void RemoveBody_SwapRemove_PreservesRemainingLookupsAndColliders()
    {
        var world = CreateWorld();
        var first = CreateBody();
        var middle = CreateBody();
        var last = CreateBody();
        world.AddBody(first);
        world.AddBody(middle);
        world.AddBody(last);
        world.AddCollider(first, new SphereCollider(1f));
        world.AddCollider(last, new SphereCollider(1f) { });
        last.Position = new Vector3(5f, 0f, 0f);

        world.RemoveBody(first);

        Assert.Equal(2, world.BodyCount);
        Assert.Equal(1, world.ColliderCount);
        Assert.Null(world.GetBody(first.EntityId));
        Assert.Same(middle, world.GetBody(middle.EntityId));
        Assert.Same(last, world.GetBody(last.EntityId));
        Assert.True(world.Raycast(Vector3.Zero, Vector3.UnitX, 100f, out var hit));
        Assert.Same(last, hit.Body);
    }

    [Fact]
    public void AddCollider_WithoutAddingBody_StillRaycasts_AndRemoveDoesNotDropIt()
    {
        var world = CreateWorld();
        var orphan = new RigidBody
        {
            EntityId = 42,
            Position = new Vector3(5f, 0f, 0f),
            Type = RigidBodyType.Static
        };
        world.AddCollider(orphan, new SphereCollider(1f));

        Assert.Equal(0, world.BodyCount);
        Assert.Equal(1, world.ColliderCount);
        Assert.Same(orphan, world.Raycast(Vector3.Zero, Vector3.UnitX, 100f, out var hit) ? hit.Body : null);
        Assert.Equal(4f, hit.Distance, Epsilon);

        world.RemoveBody(orphan);
        Assert.Equal(1, world.ColliderCount);
    }

    [Fact]
    public void Clear_RemovesBodiesCollidersAndConstraints()
    {
        var world = CreateWorld();
        var body = CreateBody();
        world.AddBody(body);
        world.AddCollider(body, new SphereCollider(1f));
        world.ConstraintSolver.AddLink(LinkConstraint.Rigid(body.EntityId, null, Vector3.Zero, Vector3.Zero, 1f));

        world.Clear();

        Assert.Equal(0, world.BodyCount);
        Assert.Equal(0, world.ColliderCount);
        Assert.Equal(0, world.ConstraintSolver.LinkCount);
        Assert.False(world.Raycast(Vector3.Zero, Vector3.UnitX, 10f, out _));
    }

    [Theory]
    [InlineData(0f)]
    [InlineData(-0.5f)]
    public void Step_NonPositiveDelta_DoesNotIntegrateOrClearForces(float dt)
    {
        var world = CreateWorld();
        var body = CreateBody();
        body.ApplyForce(new Vector3(6f, 0f, 0f));
        body.LinearVelocity = new Vector3(2f, 0f, 0f);
        world.AddBody(body);

        world.Step(dt);

        Assert.Equal(6f, body.Force.X);
        Assert.Equal(2f, body.LinearVelocity.X);
        Assert.Equal(Vector3.Zero, body.Position);
        Assert.True(body.IsAwake);
    }

    [Fact]
    public void Step_UseGravityFalse_DoesNotFall()
    {
        var world = CreateWorld();
        world.Settings.Gravity = new Vector3(0f, -10f, 0f);
        var body = CreateBody();
        body.UseGravity = false;
        body.Position = new Vector3(0f, 4f, 0f);
        world.AddBody(body);

        world.Step(1f);

        Assert.Equal(4f, body.Position.Y, Epsilon);
        Assert.Equal(0f, body.LinearVelocity.Y, Epsilon);
    }

    [Fact]
    public void Step_SleepingDynamicWithLargeVelocity_DoesNotMoveThenWakes()
    {
        var world = CreateWorld();
        var body = CreateBody();
        body.IsAwake = false;
        body.LinearVelocity = new Vector3(5f, 0f, 0f);
        world.AddBody(body);

        world.Step(0.1f);

        Assert.Equal(0f, body.Position.X, Epsilon);
        Assert.Equal(5f, body.LinearVelocity.X, Epsilon);
        Assert.True(body.IsAwake);

        world.Step(0.1f);
        Assert.True(body.Position.X > 0.4f);
    }

    [Fact]
    public void Step_StaticBodyWithVelocity_NeverIntegratesAndSleeps()
    {
        var world = CreateWorld();
        var body = CreateBody(RigidBodyType.Static);
        body.LinearVelocity = new Vector3(5f, 0f, 0f);
        body.IsAwake = true;
        world.AddBody(body);

        world.Step(1f);

        Assert.Equal(Vector3.Zero, body.Position);
        Assert.False(body.IsAwake);
    }

    [Fact]
    public void Step_KinematicVelocity_IntegratesOnceThenSleeps()
    {
        var world = CreateWorld();
        var body = CreateBody(RigidBodyType.Kinematic);
        body.LinearVelocity = new Vector3(1f, 0f, 0f);
        world.AddBody(body);

        world.Step(1f);

        Assert.Equal(1f, body.Position.X, Epsilon);
        Assert.False(body.IsAwake);

        world.Step(1f);
        Assert.Equal(1f, body.Position.X, Epsilon);
    }

    [Fact(Skip = "BUG: kinematic LinearVelocity integrates for a single step, then UpdateSleep forces IsAwake false so later steps do nothing. Expected x=2 after two 1-second steps; actual x=1 and the body is asleep.")]
    public void Step_KinematicVelocity_ShouldKeepIntegrating()
    {
        var world = CreateWorld();
        var body = CreateBody(RigidBodyType.Kinematic);
        body.LinearVelocity = new Vector3(1f, 0f, 0f);
        world.AddBody(body);

        world.Step(1f);
        world.Step(1f);

        Assert.Equal(2f, body.Position.X, Epsilon);
        Assert.True(body.IsAwake);
    }

    [Fact]
    public void Step_ClampsLinearAndAngularVelocity()
    {
        var world = CreateWorld();
        world.Settings.MaxLinearVelocity = 100f;
        world.Settings.MaxAngularVelocity = 10f;
        var body = CreateBody();
        body.LinearVelocity = new Vector3(1000f, 0f, 0f);
        body.AngularVelocity = new Vector3(0f, 80f, 0f);
        world.AddBody(body);

        world.Step(1f / 60f);

        Assert.Equal(100f, body.LinearVelocity.Length(), 0.05f);
        Assert.Equal(10f, body.AngularVelocity.Length(), 0.05f);
    }

    [Fact]
    public void Step_TorqueChangesAngularVelocityAndRotation()
    {
        var world = CreateWorld();
        var body = CreateBody();
        body.Inertia = new Vector3(2f, 2f, 2f);
        body.ApplyTorque(new Vector3(0f, 10f, 0f));
        world.AddBody(body);

        world.Step(0.1f);

        Assert.Equal(0.5f, body.AngularVelocity.Y, 0.02f);
        Assert.True(body.Rotation.Y > 0.01f);
        Assert.Equal(Vector3.Zero, body.Torque);
    }

    [Fact]
    public void Step_FreezePosition_ZerosThatAxisInsideTheWorldStep()
    {
        var world = CreateWorld();
        var body = CreateBody();
        body.FreezePositionX = true;
        body.LinearVelocity = new Vector3(5f, 3f, 0f);
        world.AddBody(body);

        world.Step(0.1f);

        Assert.Equal(0f, body.LinearVelocity.X, Epsilon);
        Assert.Equal(3f, body.LinearVelocity.Y, Epsilon);
        Assert.Equal(0f, body.Position.X, Epsilon);
        Assert.True(body.Position.Y > 0.2f);
    }

    [Fact]
    public void Step_SameBodyColliders_DoNotSelfCollide()
    {
        var world = CreateWorld();
        var body = CreateBody();
        world.AddBody(body);
        world.AddCollider(body, new SphereCollider(1f));
        world.AddCollider(body, new SphereCollider(1f) { Offset = new Vector3(0.5f, 0f, 0f) });
        int events = 0;
        world.OnCollisionEnter += _ => events++;

        world.Step(1f / 60f);

        Assert.Equal(0, events);
        Assert.Equal(2, world.ColliderCount);
    }

    [Fact]
    public void Step_DisjointCollisionMasks_SkipThePair()
    {
        var world = CreateWorld();
        var left = CreateBody();
        left.Position = new Vector3(-0.5f, 0f, 0f);
        left.LinearVelocity = new Vector3(5f, 0f, 0f);
        left.CollisionLayer = 1;
        left.CollisionMask = 1;
        var right = CreateBody();
        right.Position = new Vector3(0.5f, 0f, 0f);
        right.CollisionLayer = 2;
        right.CollisionMask = 2;
        world.AddBody(left);
        world.AddBody(right);
        world.AddCollider(left, new SphereCollider(1f));
        world.AddCollider(right, new SphereCollider(1f));
        int events = 0;
        world.OnCollisionEnter += _ => events++;

        world.Step(1f / 60f);

        Assert.Equal(0, events);
        Assert.True(left.LinearVelocity.X > 4f);
    }

    [Fact]
    public void Step_OneSidedMaskMatch_StillCollides()
    {
        var world = CreateWorld();
        var left = CreateBody();
        left.Position = new Vector3(-0.5f, 0f, 0f);
        left.CollisionLayer = 1;
        left.CollisionMask = 0;
        var right = CreateBody();
        right.Position = new Vector3(0.5f, 0f, 0f);
        right.CollisionLayer = 2;
        right.CollisionMask = 1;
        world.AddBody(left);
        world.AddBody(right);
        world.AddCollider(left, new SphereCollider(1f));
        world.AddCollider(right, new SphereCollider(1f));
        int events = 0;
        world.OnCollisionEnter += _ => events++;

        world.Step(1f / 60f);

        Assert.Equal(1, events);
    }

    [Fact]
    public void Step_TwoKinematics_DoNotCollide()
    {
        var world = CreateWorld();
        var a = CreateBody(RigidBodyType.Kinematic);
        var b = CreateBody(RigidBodyType.Static);
        b.Position = new Vector3(0.5f, 0f, 0f);
        world.AddBody(a);
        world.AddBody(b);
        world.AddCollider(a, new SphereCollider(1f));
        world.AddCollider(b, new SphereCollider(1f));
        int events = 0;
        world.OnCollisionEnter += _ => events++;
        world.OnTriggerEnter += _ => events++;

        world.Step(1f / 60f);

        Assert.Equal(0, events);
    }

    [Fact]
    public void Step_TriggerOverlap_FiresTriggerEnterAndDoesNotChangeVelocity()
    {
        var world = CreateWorld();
        var mover = CreateBody();
        mover.Position = Vector3.Zero;
        mover.LinearVelocity = new Vector3(5f, 0f, 0f);
        mover.Restitution = 1f;
        var sensor = CreateBody(RigidBodyType.Static);
        sensor.Position = new Vector3(0.2f, 0f, 0f);
        world.AddBody(mover);
        world.AddBody(sensor);
        world.AddCollider(mover, new SphereCollider(0.5f));
        world.AddCollider(sensor, new SphereCollider(10f) { IsTrigger = true });

        int collisions = 0;
        int triggers = 0;
        world.OnCollisionEnter += _ => collisions++;
        world.OnTriggerEnter += evt =>
        {
            triggers++;
            Assert.True(evt.ColliderA.IsTrigger || evt.ColliderB.IsTrigger);
            Assert.Same(mover, evt.BodyA == mover ? evt.BodyA : evt.BodyB);
        };

        world.Step(1f / 60f);

        Assert.Equal(0, collisions);
        Assert.Equal(1, triggers);
        Assert.Equal(5f, mover.LinearVelocity.X, Epsilon);
    }

    [Fact]
    public void Step_CollisionStayThenExit_FireInOrder()
    {
        var world = CreateWorld();
        var a = CreateBody();
        var b = CreateBody();
        b.Position = new Vector3(1f, 0f, 0f);
        world.AddBody(a);
        world.AddBody(b);
        world.AddCollider(a, new SphereCollider(1f));
        world.AddCollider(b, new SphereCollider(1f));

        int enters = 0, stays = 0, exits = 0;
        world.OnCollisionEnter += _ => enters++;
        world.OnCollisionStay += evt =>
        {
            stays++;
            Assert.True(evt.Manifold.Contacts.Count > 0);
        };
        world.OnCollisionExit += evt =>
        {
            exits++;
            Assert.Same(a, evt.BodyA);
            Assert.Same(b, evt.BodyB);
        };

        world.Step(1f / 60f);
        world.Step(1f / 60f);
        b.Position = new Vector3(10f, 0f, 0f);
        b.LinearVelocity = Vector3.Zero;
        world.Step(1f / 60f);

        Assert.Equal(1, enters);
        Assert.Equal(1, stays);
        Assert.Equal(1, exits);
    }

    [Fact]
    public void Step_TriggerExit_FiresWhenOverlapEnds()
    {
        var world = CreateWorld();
        var a = CreateBody();
        var b = CreateBody(RigidBodyType.Static);
        b.Position = new Vector3(0.5f, 0f, 0f);
        world.AddBody(a);
        world.AddBody(b);
        world.AddCollider(a, new SphereCollider(1f));
        world.AddCollider(b, new SphereCollider(1f) { IsTrigger = true });
        int exits = 0;
        world.OnTriggerExit += _ => exits++;
        world.OnCollisionExit += _ => exits += 100;

        world.Step(1f / 60f);
        a.Position = new Vector3(20f, 0f, 0f);
        a.LinearVelocity = Vector3.Zero;
        world.Step(1f / 60f);

        Assert.Equal(1, exits);
    }

    [Fact]
    public void Step_PositionCorrection_SeparatesRestingOverlap()
    {
        var world = CreateWorld();
        var a = CreateBody();
        var b = CreateBody();
        b.Position = new Vector3(1f, 0f, 0f);
        a.Restitution = 0f;
        b.Restitution = 0f;
        world.AddBody(a);
        world.AddBody(b);
        world.AddCollider(a, new SphereCollider(1f));
        world.AddCollider(b, new SphereCollider(1f));

        for (int i = 0; i < 20; i++)
            world.Step(1f / 60f);

        float distance = Vector3.Distance(a.Position, b.Position);
        Assert.InRange(distance, 1.7f, 2.3f);
    }

    [Fact]
    public void Step_FrictionSlowsSlidingContact_ZeroFrictionDoesNot()
    {
        float withFriction = SlideOnPlane(friction: 1f);
        float withoutFriction = SlideOnPlane(friction: 0f);

        Assert.InRange(withoutFriction, 9.5f, 10.1f);
        Assert.True(withFriction < 8f);
    }

    [Fact]
    public void Raycast_UnnormalizedDirection_HitsNearestSphere()
    {
        var world = CreateWorld();
        var far = CreateBody(RigidBodyType.Static);
        far.Position = new Vector3(8f, 0f, 0f);
        var near = CreateBody(RigidBodyType.Static);
        near.Position = new Vector3(4f, 0f, 0f);
        world.AddBody(far);
        world.AddBody(near);
        world.AddCollider(far, new SphereCollider(1f));
        world.AddCollider(near, new SphereCollider(1f));

        bool hit = world.Raycast(Vector3.Zero, new Vector3(3f, 0f, 0f), 100f, out var rayHit);

        Assert.True(hit);
        Assert.Same(near, rayHit.Body);
        Assert.Equal(3f, rayHit.Distance, Epsilon);
        Assert.Equal(-1f, rayHit.Normal.X, Epsilon);
    }

    [Fact]
    public void Raycast_HitsBoxAndPlane()
    {
        var world = CreateWorld();
        var boxBody = CreateBody(RigidBodyType.Static);
        boxBody.Position = new Vector3(0f, 5f, 0f);
        var planeBody = CreateBody(RigidBodyType.Static);
        planeBody.Position = new Vector3(0f, -2f, 0f);
        world.AddBody(boxBody);
        world.AddBody(planeBody);
        world.AddCollider(boxBody, new BoxCollider(new Vector3(1f, 1f, 1f)));
        world.AddCollider(planeBody, new PlaneCollider(Vector3.UnitY, 0f));

        Assert.True(world.Raycast(new Vector3(0f, 5f, -5f), Vector3.UnitZ, 20f, out var boxHit));
        Assert.Same(boxBody, boxHit.Body);
        Assert.Equal(4f, boxHit.Distance, Epsilon);

        Assert.True(world.Raycast(Vector3.Zero, -Vector3.UnitY, 20f, out var planeHit));
        Assert.Same(planeBody, planeHit.Body);
        Assert.Equal(2f, planeHit.Distance, Epsilon);
    }

    [Fact]
    public void Raycast_MissesCapsuleEvenWhenOverlapSphereFindsIt()
    {
        var world = CreateWorld();
        var body = CreateBody(RigidBodyType.Static);
        body.Position = new Vector3(5f, 0f, 0f);
        var capsule = new CapsuleCollider(1f, 2f);
        world.AddBody(body);
        world.AddCollider(body, capsule);

        bool rayHit = world.Raycast(Vector3.Zero, Vector3.UnitX, 100f, out _);
        var overlaps = world.OverlapSphere(new Vector3(5f, 0f, 0f), 0.5f);

        Assert.False(rayHit);
        Assert.Contains(capsule, overlaps);
    }

    [Fact(Skip = "BUG: PhysicsWorld.Raycast and RaycastAll ignore Capsule colliders. A ray along +X through a capsule at x=5 with radius 1 returns false; expected a hit near distance 4.")]
    public void Raycast_ShouldHitCapsule()
    {
        var world = CreateWorld();
        var body = CreateBody(RigidBodyType.Static);
        body.Position = new Vector3(5f, 0f, 0f);
        world.AddBody(body);
        world.AddCollider(body, new CapsuleCollider(1f, 2f));

        bool hit = world.Raycast(Vector3.Zero, Vector3.UnitX, 100f, out var rayHit);

        Assert.True(hit);
        Assert.InRange(rayHit.Distance, 3.5f, 4.5f);
        Assert.Same(body, rayHit.Body);
    }

    [Fact]
    public void RaycastAll_ReturnsEveryHitSortedByDistance()
    {
        var world = CreateWorld();
        var far = CreateBody(RigidBodyType.Static);
        far.Position = new Vector3(9f, 0f, 0f);
        var near = CreateBody(RigidBodyType.Static);
        near.Position = new Vector3(3f, 0f, 0f);
        world.AddBody(far);
        world.AddBody(near);
        world.AddCollider(far, new SphereCollider(0.5f));
        world.AddCollider(near, new SphereCollider(0.5f));

        var hits = world.RaycastAll(Vector3.Zero, new Vector3(0f, 2f, 0f), 2f);
        Assert.Empty(hits);

        hits = world.RaycastAll(Vector3.Zero, Vector3.UnitX, 100f);

        Assert.Equal(2, hits.Count);
        Assert.Same(near, hits[0].Body);
        Assert.Same(far, hits[1].Body);
        Assert.True(hits[0].Distance < hits[1].Distance);
    }

    [Fact]
    public void OverlapSphereAndOverlapBox_ReturnOnlyOverlappingColliders()
    {
        var world = CreateWorld();
        var near = CreateBody(RigidBodyType.Static);
        near.Position = new Vector3(1f, 0f, 0f);
        var far = CreateBody(RigidBodyType.Static);
        far.Position = new Vector3(10f, 0f, 0f);
        var nearSphere = new SphereCollider(0.5f);
        var farSphere = new SphereCollider(0.5f);
        world.AddBody(near);
        world.AddBody(far);
        world.AddCollider(near, nearSphere);
        world.AddCollider(far, farSphere);

        var sphereHits = world.OverlapSphere(Vector3.Zero, 2f);
        var boxHits = world.OverlapBox(Vector3.Zero, new Vector3(2f, 2f, 2f), Quaternion.Identity);
        var rotatedMiss = world.OverlapBox(
            new Vector3(0f, 6f, 0f),
            new Vector3(1f, 1f, 1f),
            Quaternion.CreateFromAxisAngle(Vector3.UnitZ, 0.4f));

        Assert.Contains(nearSphere, sphereHits);
        Assert.DoesNotContain(farSphere, sphereHits);
        Assert.Contains(nearSphere, boxHits);
        Assert.DoesNotContain(farSphere, boxHits);
        Assert.Empty(rotatedMiss);
    }

    private static float SlideOnPlane(float friction)
    {
        var world = CreateWorld();
        world.Settings.Gravity = new Vector3(0f, -20f, 0f);
        var ball = CreateBody();
        ball.Position = new Vector3(0f, 0.9f, 0f);
        ball.LinearVelocity = new Vector3(10f, 0f, 0f);
        ball.UseGravity = true;
        ball.Friction = friction;
        ball.Restitution = 0f;
        var floor = CreateBody(RigidBodyType.Static);
        floor.Friction = friction;
        floor.Restitution = 0f;
        world.AddBody(ball);
        world.AddBody(floor);
        world.AddCollider(ball, new SphereCollider(1f));
        world.AddCollider(floor, new PlaneCollider(Vector3.UnitY, 0f));

        for (int i = 0; i < 20; i++)
            world.Step(1f / 60f);

        return ball.LinearVelocity.X;
    }
}
