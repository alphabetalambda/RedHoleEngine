using System.Numerics;
using RedHoleEngine.Components;
using RedHoleEngine.Core.ECS;
using RedHoleEngine.Physics;
using RedHoleEngine.Physics.Collision;
using RedHoleEngine.Rendering.Debug;

namespace RedHoleEngine.Tests.Physics;

public class PhysicsSystemCoverageTests
{
    private const float Epsilon = 0.02f;

    [Fact]
    public void Update_WithoutWorld_DoesNotThrow()
    {
        var physics = new PhysicsSystem();

        var ex = Record.Exception(() => physics.Update(0.016f));

        Assert.Null(ex);
    }

    [Fact]
    public void Settings_AreThePhysicsWorldSettings()
    {
        var physics = new PhysicsSystem();

        Assert.Equal(-100, physics.Priority);
        Assert.Same(physics.PhysicsWorld.Settings, physics.Settings);
        Assert.Equal(PhysicsDebugFlags.None, physics.DebugFlags);
        physics.DebugFlags = PhysicsDebugFlags.Colliders | PhysicsDebugFlags.Velocity;
        Assert.Equal(PhysicsDebugFlags.Colliders | PhysicsDebugFlags.Velocity, physics.DebugFlags);
    }

    [Fact]
    public void Raycast_BeforeAnyBodies_ReturnsFalseAndClearsResult()
    {
        var physics = new PhysicsSystem();

        bool hit = physics.Raycast(Vector3.Zero, Vector3.UnitX, 10f, out var result);

        Assert.False(hit);
        Assert.False(result.Hit);
        Assert.Equal(0f, result.Distance);
        Assert.Empty(physics.RaycastAll(Vector3.Zero, Vector3.UnitX, 10f));
        Assert.Empty(physics.OverlapSphere(Vector3.Zero, 1f));
    }

    [Fact]
    public void Register_RewritesNonPositiveMassAndZeroFilters()
    {
        using var world = new World();
        var physics = world.AddSystem<PhysicsSystem>();
        physics.Settings.Gravity = Vector3.Zero;

        var negative = AddBody(world, Vector3.Zero, mass: -4f, layer: 0, mask: 0);
        var zero = AddBody(world, new Vector3(3f, 0f, 0f), mass: 0f);

        physics.Update(1f / 60f);

        var negativeBody = physics.PhysicsWorld.GetBody(negative.Id)!;
        var zeroBody = physics.PhysicsWorld.GetBody(zero.Id)!;
        Assert.Equal(1f, negativeBody.Mass);
        Assert.Equal(1u, negativeBody.CollisionLayer);
        Assert.Equal(uint.MaxValue, negativeBody.CollisionMask);
        Assert.Equal(1f, zeroBody.Mass);
        Assert.Equal(2, physics.PhysicsWorld.BodyCount);

        physics.Update(1f / 60f);
        Assert.Equal(2, physics.PhysicsWorld.BodyCount);
    }

    [Fact]
    public void Register_CopiesMaterialFreezeDampingAndExplicitFilters()
    {
        using var world = new World();
        var physics = world.AddSystem<PhysicsSystem>();
        physics.Settings.Gravity = new Vector3(0f, -10f, 0f);

        var entity = world.CreateEntity();
        world.AddComponent(entity, new TransformComponent(new Vector3(1f, 4f, 2f)));
        var rb = RigidBodyComponent.CreateDynamic(2f);
        rb.Restitution = 0.8f;
        rb.Friction = 0.15f;
        rb.LinearDamping = 0.4f;
        rb.AngularDamping = 0.2f;
        rb.FreezePositionY = true;
        rb.FreezeRotationZ = true;
        rb.CollisionLayer = 4;
        rb.CollisionMask = 9;
        rb.UseGravity = true;
        world.AddComponent(entity, rb);

        physics.Update(1f / 60f);

        var body = physics.PhysicsWorld.GetBody(entity.Id)!;
        Assert.Equal(2f, body.Mass);
        Assert.Equal(0.8f, body.Restitution);
        Assert.Equal(0.15f, body.Friction);
        Assert.Equal(0.4f, body.LinearDamping);
        Assert.Equal(0.2f, body.AngularDamping);
        Assert.True(body.FreezePositionY);
        Assert.True(body.FreezeRotationZ);
        Assert.Equal(4u, body.CollisionLayer);
        Assert.Equal(9u, body.CollisionMask);
        Assert.Equal(4f, body.Position.Y, Epsilon);

        body.LinearVelocity = new Vector3(2f, 5f, 0f);
        physics.Update(0.1f);

        Assert.Equal(4f, body.Position.Y, Epsilon);
        Assert.Equal(0f, body.LinearVelocity.Y, Epsilon);
        Assert.True(body.Position.X > 1f);
    }

    [Fact]
    public void Register_ComputesInertiaFromSphereBoxCapsuleAndLeavesDefaultWithoutCollider()
    {
        using var world = new World();
        var physics = world.AddSystem<PhysicsSystem>();
        physics.Settings.Gravity = Vector3.Zero;

        var sphere = AddBody(world, Vector3.Zero, mass: 10f);
        world.AddComponent(sphere, ColliderComponent.CreateSphere(2f));

        var box = AddBody(world, new Vector3(5f, 0f, 0f), mass: 12f);
        world.AddComponent(box, ColliderComponent.CreateBox(new Vector3(1f, 2f, 3f)));

        var capsule = AddBody(world, new Vector3(0f, 5f, 0f), mass: 2f);
        world.AddComponent(capsule, ColliderComponent.CreateCapsule(0.4f, 2f));

        var bare = AddBody(world, new Vector3(0f, 0f, 5f), mass: 3f);

        physics.Update(1f / 60f);

        var sphereInertia = physics.PhysicsWorld.GetBody(sphere.Id)!.Inertia;
        Assert.Equal(16f, sphereInertia.X, 0.001f);
        Assert.Equal(16f, sphereInertia.Y, 0.001f);
        Assert.Equal(16f, sphereInertia.Z, 0.001f);
        var boxInertia = physics.PhysicsWorld.GetBody(box.Id)!.Inertia;
        Assert.Equal(52f, boxInertia.X, 0.001f);
        Assert.Equal(40f, boxInertia.Y, 0.001f);
        Assert.Equal(20f, boxInertia.Z, 0.001f);
        float capsuleInertia = 0.4f * 2f * 0.6f * 0.6f;
        Assert.Equal(capsuleInertia, physics.PhysicsWorld.GetBody(capsule.Id)!.Inertia.X, 0.0001f);
        Assert.Equal(Vector3.One, physics.PhysicsWorld.GetBody(bare.Id)!.Inertia);
    }

    [Fact]
    public void Register_NegativeMass_UsesUnitMassForSphereInertia()
    {
        using var world = new World();
        var physics = world.AddSystem<PhysicsSystem>();
        var entity = AddBody(world, Vector3.Zero, mass: -8f);
        world.AddComponent(entity, ColliderComponent.CreateSphere(2f));

        physics.Update(1f / 60f);

        // Mass is rewritten to 1 before inertia: I = 0.4 * 1 * 4
        Assert.Equal(1.6f, physics.PhysicsWorld.GetBody(entity.Id)!.Inertia.Y, 0.0001f);
    }

    [Fact]
    public void Register_TriggerMaterialAndOffset_AreVisibleToRaycasts()
    {
        using var world = new World();
        var physics = world.AddSystem<PhysicsSystem>();
        physics.Settings.Gravity = Vector3.Zero;

        var entity = world.CreateEntity();
        world.AddComponent(entity, new TransformComponent(new Vector3(5f, 0f, 0f)));
        world.AddComponent(entity, RigidBodyComponent.CreateStatic());
        world.AddComponent(entity, ColliderComponent.CreateSphere(2f, new Vector3(1f, 0f, 0f))
            .AsTrigger()
            .WithMaterial(0.15f, 0.25f, 0.35f));

        physics.Update(1f / 60f);

        bool hit = physics.PhysicsWorld.Raycast(Vector3.Zero, Vector3.UnitX, 100f, out var rayHit);

        Assert.True(hit);
        Assert.Equal(4f, rayHit.Distance, Epsilon);
        Assert.True(rayHit.Collider.IsTrigger);
        Assert.Equal(new Vector3(1f, 0f, 0f), rayHit.Collider.Offset);
        Assert.NotNull(rayHit.Collider.Material);
        Assert.Equal(0.15f, rayHit.Collider.Material!.Restitution);
        Assert.Equal(0.25f, rayHit.Collider.Material.StaticFriction);
        Assert.Equal(0.35f, rayHit.Collider.Material.DynamicFriction);
    }

    [Fact]
    public void StaticBody_FollowsLaterTransformChanges()
    {
        using var world = new World();
        var physics = world.AddSystem<PhysicsSystem>();
        var entity = world.CreateEntity();
        world.AddComponent(entity, new TransformComponent(Vector3.Zero));
        world.AddComponent(entity, RigidBodyComponent.CreateStatic());
        world.AddComponent(entity, ColliderComponent.CreateBox(Vector3.One));

        physics.Update(1f / 60f);
        ref var transform = ref world.GetComponent<TransformComponent>(entity);
        transform.Position = new Vector3(3f, 4f, 5f);
        transform.Rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI / 2f);
        physics.Update(1f / 60f);

        var body = physics.PhysicsWorld.GetBody(entity.Id)!;
        Assert.Equal(new Vector3(3f, 4f, 5f), body.Position);
        Assert.Equal(transform.Rotation.Y, body.Rotation.Y, 0.001f);
        Assert.Equal(transform.Rotation.W, body.Rotation.W, 0.001f);
    }

    [Fact]
    public void DynamicBody_OverwritesExternalTransformEdits()
    {
        using var world = new World();
        var physics = world.AddSystem<PhysicsSystem>();
        physics.Settings.Gravity = Vector3.Zero;
        var entity = AddBody(world, Vector3.Zero);
        world.AddComponent(entity, ColliderComponent.CreateSphere(0.5f));

        physics.Update(1f / 60f);
        world.GetComponent<TransformComponent>(entity).Position = new Vector3(50f, 0f, 0f);
        physics.Update(1f / 60f);

        Assert.Equal(0f, world.GetComponent<TransformComponent>(entity).Position.X, Epsilon);
        Assert.Equal(0f, physics.PhysicsWorld.GetBody(entity.Id)!.Position.X, Epsilon);
    }

    [Fact]
    public void DynamicRotation_SyncsBackToTransform()
    {
        using var world = new World();
        var physics = world.AddSystem<PhysicsSystem>();
        physics.Settings.Gravity = Vector3.Zero;
        var entity = AddBody(world, Vector3.Zero);

        physics.Update(1f / 60f);
        var body = physics.PhysicsWorld.GetBody(entity.Id)!;
        body.AngularDamping = 0f;
        body.AngularVelocity = new Vector3(0f, 2f, 0f);
        physics.Update(0.1f);

        Assert.True(MathF.Abs(world.GetComponent<TransformComponent>(entity).Rotation.Y) > 0.05f);
    }

    [Fact]
    public void EntityWithoutTransform_IsNotSimulated_UntilTransformExists()
    {
        using var world = new World();
        var physics = world.AddSystem<PhysicsSystem>();
        var entity = world.CreateEntity();
        world.AddComponent(entity, RigidBodyComponent.CreateDynamic(1f));

        physics.Update(1f / 60f);
        Assert.Equal(0, physics.PhysicsWorld.BodyCount);

        world.AddComponent(entity, new TransformComponent(new Vector3(2f, 0f, 0f)));
        physics.Update(1f / 60f);

        Assert.Equal(1, physics.PhysicsWorld.BodyCount);
        Assert.Equal(2f, physics.PhysicsWorld.GetBody(entity.Id)!.Position.X);
    }

    [Fact]
    public void DestroyEntity_UnregistersTheBody()
    {
        using var world = new World();
        var physics = world.AddSystem<PhysicsSystem>();
        var entity = AddBody(world, Vector3.Zero);
        physics.Update(1f / 60f);
        Assert.Equal(1, physics.PhysicsWorld.BodyCount);

        world.DestroyEntity(entity);
        physics.Update(1f / 60f);

        Assert.Equal(0, physics.PhysicsWorld.BodyCount);
        Assert.Null(physics.PhysicsWorld.GetBody(entity.Id));
    }

    [Fact]
    public void OnDestroy_ClearsThePhysicsWorld()
    {
        using var world = new World();
        var physics = world.AddSystem<PhysicsSystem>();
        AddBody(world, Vector3.Zero);
        physics.Update(1f / 60f);

        world.RemoveSystem<PhysicsSystem>();

        Assert.Equal(0, physics.PhysicsWorld.BodyCount);
    }

    [Fact]
    public void CollisionAndTriggerCallbacks_ExposeEntityIds()
    {
        using var world = new World();
        var physics = world.AddSystem<PhysicsSystem>();
        physics.Settings.Gravity = Vector3.Zero;

        var solid = world.CreateEntity();
        world.AddComponent(solid, new TransformComponent(Vector3.Zero));
        var solidRb = RigidBodyComponent.CreateDynamic(1f);
        solidRb.UseGravity = false;
        world.AddComponent(solid, solidRb);
        world.AddComponent(solid, ColliderComponent.CreateSphere(1f));

        var sensor = world.CreateEntity();
        world.AddComponent(sensor, new TransformComponent(new Vector3(0.5f, 0f, 0f)));
        world.AddComponent(sensor, RigidBodyComponent.CreateStatic());
        world.AddComponent(sensor, ColliderComponent.CreateSphere(1f).AsTrigger());

        int enters = 0, triggerEnters = 0, exits = 0, triggerExits = 0;
        physics.CollisionEnter += (a, b, manifold) =>
        {
            enters++;
            Assert.True(manifold.Contacts.Count > 0);
        };
        physics.TriggerEnter += (a, b) =>
        {
            triggerEnters++;
            var ids = new HashSet<int> { a.Id, b.Id };
            Assert.Contains(solid.Id, ids);
            Assert.Contains(sensor.Id, ids);
        };
        physics.CollisionExit += (_, _) => exits++;
        physics.TriggerExit += (_, _) => triggerExits++;

        physics.Update(1f / 60f);
        var body = physics.PhysicsWorld.GetBody(solid.Id)!;
        body.Position = new Vector3(30f, 0f, 0f);
        body.LinearVelocity = Vector3.Zero;
        physics.Update(1f / 60f);

        Assert.Equal(0, enters);
        Assert.Equal(1, triggerEnters);
        Assert.Equal(0, exits);
        Assert.Equal(1, triggerExits);
    }

    [Fact]
    public void RaycastAllAndOverlapSphere_ReturnEntitiesInRange()
    {
        using var world = new World();
        var physics = world.AddSystem<PhysicsSystem>();
        var near = AddStaticBox(world, new Vector3(4f, 0f, 0f));
        var far = AddStaticBox(world, new Vector3(12f, 0f, 0f));
        physics.Update(1f / 60f);

        var hits = physics.RaycastAll(Vector3.Zero, Vector3.UnitX, 100f);
        var overlaps = physics.OverlapSphere(new Vector3(4f, 0f, 0f), 2f);
        bool missed = physics.Raycast(new Vector3(0f, 10f, 0f), Vector3.UnitX, 5f, out var miss);

        Assert.Equal(2, hits.Count);
        Assert.True(hits[0].Distance < hits[1].Distance);
        Assert.Equal(near.Id, hits[0].Entity.Id);
        Assert.Equal(far.Id, hits[1].Entity.Id);
        Assert.True(hits[0].Hit);
        Assert.Contains(overlaps, entity => entity.Id == near.Id);
        Assert.DoesNotContain(overlaps, entity => entity.Id == far.Id);
        Assert.False(missed);
        Assert.False(miss.Hit);
    }

    [Fact]
    public void DebugDraw_NoneOrMissingDrawer_ProducesNoPrimitives()
    {
        using var world = new World();
        var physics = world.AddSystem<PhysicsSystem>();
        physics.Settings.Gravity = Vector3.Zero;
        AddBody(world, Vector3.Zero);
        var debug = new DebugDrawManager();
        physics.SetDebugDraw(debug);

        physics.Update(1f / 60f);
        Assert.Empty(debug.LineVertices);

        physics.DebugFlags = PhysicsDebugFlags.All;
        physics.SetDebugDraw(null);
        var ex = Record.Exception(() => physics.Update(1f / 60f));
        Assert.Null(ex);
        Assert.Empty(debug.LineVertices);
    }

    [Fact]
    public void DebugDraw_ColliderShapesAabbVelocityAndSleep_EmitPrimitives()
    {
        using var world = new World();
        var physics = world.AddSystem<PhysicsSystem>();
        physics.Settings.Gravity = Vector3.Zero;
        var debug = new DebugDrawManager();
        physics.SetDebugDraw(debug);

        var sphere = world.CreateEntity();
        world.AddComponent(sphere, new TransformComponent(Vector3.Zero));
        var sphereRb = RigidBodyComponent.CreateDynamic(1f);
        sphereRb.UseGravity = false;
        sphereRb.LinearDamping = 0f;
        sphereRb.AngularDamping = 0f;
        world.AddComponent(sphere, sphereRb);
        world.AddComponent(sphere, ColliderComponent.CreateSphere(0.5f, new Vector3(10f, 0f, 0f)));

        var box = world.CreateEntity();
        world.AddComponent(box, new TransformComponent(new Vector3(0f, 3f, 0f)));
        world.AddComponent(box, RigidBodyComponent.CreateStatic());
        world.AddComponent(box, ColliderComponent.CreateBox(new Vector3(0.5f, 0.5f, 0.5f)));

        var capsule = world.CreateEntity();
        world.AddComponent(capsule, new TransformComponent(new Vector3(0f, 6f, 0f)));
        world.AddComponent(capsule, RigidBodyComponent.CreateStatic());
        world.AddComponent(capsule, ColliderComponent.CreateCapsule(0.4f, 1.6f));

        var plane = world.CreateEntity();
        world.AddComponent(plane, new TransformComponent(new Vector3(0f, -1f, 0f)));
        world.AddComponent(plane, RigidBodyComponent.CreateStatic());
        world.AddComponent(plane, ColliderComponent.CreateGroundPlane());

        physics.DebugFlags = PhysicsDebugFlags.Colliders | PhysicsDebugFlags.AABB | PhysicsDebugFlags.CenterOfMass;
        physics.Update(1f / 60f);

        Assert.True(debug.LineVertices.Count > 20);
        Assert.True(debug.PointVertices.Count >= 4);
        Assert.Contains(debug.LineVertices, vertex => vertex.Position.X > 8f);

        debug.Clear();
        var body = physics.PhysicsWorld.GetBody(sphere.Id)!;
        body.LinearVelocity = new Vector3(10f, 0f, 0f);
        body.AngularVelocity = new Vector3(0f, 4f, 0f);
        physics.DebugFlags = PhysicsDebugFlags.Velocity | PhysicsDebugFlags.AngularVelocity;
        physics.Update(1f / 60f);

        Assert.True(debug.LineVertices.Count > 4);

        debug.Clear();
        physics.DebugFlags = PhysicsDebugFlags.SleepState;
        for (int i = 0; i < 8; i++)
        {
            body.LinearVelocity = Vector3.Zero;
            body.AngularVelocity = Vector3.Zero;
            physics.Update(0.1f);
        }

        Assert.Contains(debug.TextLabels, label => label.Text == "Zzz");
    }

    private static Entity AddBody(World world, Vector3 position, float mass = 1f, uint layer = 1, uint mask = uint.MaxValue)
    {
        var entity = world.CreateEntity();
        world.AddComponent(entity, new TransformComponent(position));
        var component = RigidBodyComponent.CreateDynamic(mass);
        component.UseGravity = false;
        component.Mass = mass;
        component.CollisionLayer = layer;
        component.CollisionMask = mask;
        world.AddComponent(entity, component);
        return entity;
    }

    private static Entity AddStaticBox(World world, Vector3 position)
    {
        var entity = world.CreateEntity();
        world.AddComponent(entity, new TransformComponent(position));
        world.AddComponent(entity, RigidBodyComponent.CreateStatic());
        world.AddComponent(entity, ColliderComponent.CreateBox(Vector3.One));
        return entity;
    }
}
