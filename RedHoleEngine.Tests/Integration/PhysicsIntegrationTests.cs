using System.Numerics;
using RedHoleEngine.Audio;
using RedHoleEngine.Components;
using RedHoleEngine.Core.ECS;
using RedHoleEngine.Core.Scene;
using RedHoleEngine.Physics;
using RedHoleEngine.Physics.Collision;

namespace RedHoleEngine.Tests.Integration;

/// <summary>
/// Integration tests for ECS + Physics + Transform systems working together
/// </summary>
public class PhysicsIntegrationTests
{
    #region ECS + Physics System Registration

    [Fact]
    public void PhysicsSystem_CanBeAddedToWorld()
    {
        using var world = new World();
        var physicsSystem = world.AddSystem<PhysicsSystem>();

        Assert.NotNull(physicsSystem);
        Assert.NotNull(physicsSystem.PhysicsWorld);
    }

    [Fact]
    public void PhysicsSystem_HasCorrectPriority()
    {
        using var world = new World();
        var physicsSystem = world.AddSystem<PhysicsSystem>();

        // Physics should run early (negative priority)
        Assert.True(physicsSystem.Priority < 0);
    }

    [Fact]
    public void PhysicsSystem_AutoRegistersEntities()
    {
        using var world = new World();
        var physicsSystem = world.AddSystem<PhysicsSystem>();

        // Create entity with physics components
        var entity = world.CreateEntity();
        world.AddComponent(entity, new TransformComponent());
        world.AddComponent(entity, RigidBodyComponent.CreateDynamic(1f));

        // Update should register the entity
        physicsSystem.Update(0.016f);

        Assert.Equal(1, physicsSystem.PhysicsWorld.BodyCount);
    }

    #endregion

    #region Transform Sync Tests

    [Fact]
    public void DynamicBody_SyncsPositionToTransform()
    {
        using var world = new World();
        var physicsSystem = world.AddSystem<PhysicsSystem>();

        // Create falling ball
        var ball = world.CreateEntity();
        world.AddComponent(ball, new TransformComponent { Position = new Vector3(0, 10, 0) });
        world.AddComponent(ball, RigidBodyComponent.CreateDynamic(1f));
        world.AddComponent(ball, ColliderComponent.CreateSphere(0.5f));

        // Step physics multiple times
        for (int i = 0; i < 10; i++)
        {
            physicsSystem.Update(0.016f);
        }

        // Ball should have fallen due to gravity
        ref var transform = ref world.GetComponent<TransformComponent>(ball);
        Assert.True(transform.Position.Y < 10f, "Ball should have fallen");
    }

    [Fact]
    public void StaticBody_DoesNotMove()
    {
        using var world = new World();
        var physicsSystem = world.AddSystem<PhysicsSystem>();

        // Create static ground
        var ground = world.CreateEntity();
        world.AddComponent(ground, new TransformComponent { Position = Vector3.Zero });
        world.AddComponent(ground, RigidBodyComponent.CreateStatic());
        world.AddComponent(ground, ColliderComponent.CreateGroundPlane());

        // Step physics
        for (int i = 0; i < 10; i++)
        {
            physicsSystem.Update(0.016f);
        }

        // Ground should not have moved
        ref var transform = ref world.GetComponent<TransformComponent>(ground);
        Assert.Equal(Vector3.Zero, transform.Position);
    }

    [Fact]
    public void KinematicBody_SyncsFromTransform()
    {
        using var world = new World();
        var physicsSystem = world.AddSystem<PhysicsSystem>();

        // Create kinematic platform
        var platform = world.CreateEntity();
        world.AddComponent(platform, new TransformComponent { Position = Vector3.Zero });
        world.AddComponent(platform, RigidBodyComponent.CreateKinematic());
        world.AddComponent(platform, ColliderComponent.CreateBox(new Vector3(5, 0.5f, 5)));

        physicsSystem.Update(0.016f);

        // Move transform externally
        ref var transform = ref world.GetComponent<TransformComponent>(platform);
        transform.Position = new Vector3(0, 5, 0);

        physicsSystem.Update(0.016f);

        // Physics body should have followed
        var body = physicsSystem.PhysicsWorld.GetBody(platform.Id);
        Assert.NotNull(body);
        Assert.Equal(new Vector3(0, 5, 0), body.Position);
    }

    #endregion

    #region Collision Detection Tests

    [Fact]
    public void CollisionEnter_FiresWhenBodiesCollide()
    {
        using var world = new World();
        var physicsSystem = world.AddSystem<PhysicsSystem>();

        bool collisionOccurred = false;
        physicsSystem.PhysicsWorld.OnCollisionEnter += evt => collisionOccurred = true;

        // Create ground
        var ground = world.CreateEntity();
        world.AddComponent(ground, new TransformComponent());
        world.AddComponent(ground, RigidBodyComponent.CreateStatic());
        world.AddComponent(ground, ColliderComponent.CreateGroundPlane());

        // Create falling ball starting close to ground
        var ball = world.CreateEntity();
        world.AddComponent(ball, new TransformComponent { Position = new Vector3(0, 1f, 0) });
        world.AddComponent(ball, RigidBodyComponent.CreateDynamic(1f));
        world.AddComponent(ball, ColliderComponent.CreateSphere(0.5f));

        // Step physics until collision occurs (or timeout)
        for (int i = 0; i < 100 && !collisionOccurred; i++)
        {
            physicsSystem.Update(0.016f);
        }

        Assert.True(collisionOccurred, "Collision should have occurred");
    }

    [Fact]
    public void CollisionEvent_ContainsCorrectEntityIds()
    {
        using var world = new World();
        var physicsSystem = world.AddSystem<PhysicsSystem>();

        int? entityIdA = null;
        int? entityIdB = null;

        physicsSystem.PhysicsWorld.OnCollisionEnter += evt =>
        {
            entityIdA = evt.BodyA.EntityId;
            entityIdB = evt.BodyB.EntityId;
        };

        // Create ground
        var ground = world.CreateEntity();
        world.AddComponent(ground, new TransformComponent());
        world.AddComponent(ground, RigidBodyComponent.CreateStatic());
        world.AddComponent(ground, ColliderComponent.CreateGroundPlane());

        // Create falling ball
        var ball = world.CreateEntity();
        world.AddComponent(ball, new TransformComponent { Position = new Vector3(0, 1f, 0) });
        world.AddComponent(ball, RigidBodyComponent.CreateDynamic(1f));
        world.AddComponent(ball, ColliderComponent.CreateSphere(0.5f));

        // Step until collision
        for (int i = 0; i < 100 && entityIdA == null; i++)
        {
            physicsSystem.Update(0.016f);
        }

        Assert.NotNull(entityIdA);
        Assert.NotNull(entityIdB);

        // One should be ground, one should be ball
        var ids = new HashSet<int> { entityIdA.Value, entityIdB.Value };
        Assert.Contains(ground.Id, ids);
        Assert.Contains(ball.Id, ids);
    }

    [Fact]
    public void CollisionManifold_HasContactPoints()
    {
        using var world = new World();
        var physicsSystem = world.AddSystem<PhysicsSystem>();

        CollisionManifold? manifold = null;

        physicsSystem.PhysicsWorld.OnCollisionEnter += evt =>
        {
            manifold = evt.Manifold;
        };

        // Create ground
        var ground = world.CreateEntity();
        world.AddComponent(ground, new TransformComponent());
        world.AddComponent(ground, RigidBodyComponent.CreateStatic());
        world.AddComponent(ground, ColliderComponent.CreateGroundPlane());

        // Create ball
        var ball = world.CreateEntity();
        world.AddComponent(ball, new TransformComponent { Position = new Vector3(0, 1f, 0) });
        world.AddComponent(ball, RigidBodyComponent.CreateDynamic(1f));
        world.AddComponent(ball, ColliderComponent.CreateSphere(0.5f));

        // Step until collision
        for (int i = 0; i < 100 && manifold == null; i++)
        {
            physicsSystem.Update(0.016f);
        }

        Assert.NotNull(manifold);
        Assert.True(manifold.Contacts.Count > 0, "Should have at least one contact point");
    }

    [Fact]
    public void TwoSpheres_Collide()
    {
        using var world = new World();
        var physicsSystem = world.AddSystem<PhysicsSystem>();

        bool collisionOccurred = false;
        physicsSystem.PhysicsWorld.OnCollisionEnter += evt => collisionOccurred = true;

        // Create two spheres moving toward each other
        var sphere1 = world.CreateEntity();
        world.AddComponent(sphere1, new TransformComponent { Position = new Vector3(-2, 0, 0) });
        var rb1 = RigidBodyComponent.CreateDynamic(1f);
        rb1.UseGravity = false;
        world.AddComponent(sphere1, rb1);
        world.AddComponent(sphere1, ColliderComponent.CreateSphere(1f));

        var sphere2 = world.CreateEntity();
        world.AddComponent(sphere2, new TransformComponent { Position = new Vector3(2, 0, 0) });
        var rb2 = RigidBodyComponent.CreateDynamic(1f);
        rb2.UseGravity = false;
        world.AddComponent(sphere2, rb2);
        world.AddComponent(sphere2, ColliderComponent.CreateSphere(1f));

        // Register and give them velocity toward each other
        physicsSystem.Update(0.001f);
        world.GetComponent<RigidBodyComponent>(sphere1).LinearVelocity = new Vector3(5, 0, 0);
        world.GetComponent<RigidBodyComponent>(sphere2).LinearVelocity = new Vector3(-5, 0, 0);

        // Step until collision
        for (int i = 0; i < 100 && !collisionOccurred; i++)
        {
            physicsSystem.Update(0.016f);
        }

        Assert.True(collisionOccurred, "Two spheres should have collided");
    }

    #endregion

    #region Force Application Tests

    [Fact]
    public void ApplyForce_AffectsVelocity()
    {
        using var world = new World();
        var physicsSystem = world.AddSystem<PhysicsSystem>();

        // Create ball with no gravity
        var ball = world.CreateEntity();
        world.AddComponent(ball, new TransformComponent { Position = Vector3.Zero });
        var rb = RigidBodyComponent.CreateDynamic(1f);
        rb.UseGravity = false;
        world.AddComponent(ball, rb);
        world.AddComponent(ball, ColliderComponent.CreateSphere(0.5f));

        // Register entity
        physicsSystem.Update(0.001f);

        // Apply force
        ref var rbComponent = ref world.GetComponent<RigidBodyComponent>(ball);
        rbComponent.ApplyForce(new Vector3(100, 0, 0));

        // Step physics
        physicsSystem.Update(0.1f);

        // Velocity should have increased in X direction
        Assert.True(rbComponent.LinearVelocity.X > 0, "Ball should be moving right");
    }

    [Fact]
    public void ApplyImpulse_ChangesVelocityInstantly()
    {
        using var world = new World();
        var physicsSystem = world.AddSystem<PhysicsSystem>();

        var ball = world.CreateEntity();
        world.AddComponent(ball, new TransformComponent { Position = Vector3.Zero });
        var rb = RigidBodyComponent.CreateDynamic(1f);
        rb.UseGravity = false;
        world.AddComponent(ball, rb);
        world.AddComponent(ball, ColliderComponent.CreateSphere(0.5f));

        physicsSystem.Update(0.001f);

        ref var rbComponent = ref world.GetComponent<RigidBodyComponent>(ball);
        var velocityBefore = rbComponent.LinearVelocity;

        rbComponent.ApplyImpulse(new Vector3(10, 0, 0));

        // Velocity changes immediately after impulse (before next physics step)
        var velocityAfter = rbComponent.LinearVelocity;
        Assert.True(velocityAfter.X > velocityBefore.X, "Impulse should immediately change velocity");
    }

    #endregion

    #region Raycast Tests

    [Fact]
    public void Raycast_HitsStaticBody()
    {
        using var world = new World();
        var physicsSystem = world.AddSystem<PhysicsSystem>();

        // Create a box in front of ray
        var box = world.CreateEntity();
        world.AddComponent(box, new TransformComponent { Position = new Vector3(5, 0, 0) });
        world.AddComponent(box, RigidBodyComponent.CreateStatic());
        world.AddComponent(box, ColliderComponent.CreateBox(new Vector3(1, 1, 1)));

        physicsSystem.Update(0.001f);

        // Cast ray toward box
        bool hit = physicsSystem.Raycast(
            origin: Vector3.Zero,
            direction: Vector3.UnitX,
            maxDistance: 100f,
            out var result);

        Assert.True(hit);
        Assert.True(result.Distance > 0);
        Assert.True(result.Distance < 10f);
    }

    [Fact]
    public void Raycast_MissesWhenNoObstacle()
    {
        using var world = new World();
        var physicsSystem = world.AddSystem<PhysicsSystem>();

        physicsSystem.Update(0.001f);

        // Cast ray into empty space
        bool hit = physicsSystem.Raycast(
            origin: Vector3.Zero,
            direction: Vector3.UnitX,
            maxDistance: 100f,
            out var result);

        Assert.False(hit);
    }

    [Fact]
    public void Raycast_ReturnsCorrectEntity()
    {
        using var world = new World();
        var physicsSystem = world.AddSystem<PhysicsSystem>();

        var box = world.CreateEntity();
        world.AddComponent(box, new TransformComponent { Position = new Vector3(5, 0, 0) });
        world.AddComponent(box, RigidBodyComponent.CreateStatic());
        world.AddComponent(box, ColliderComponent.CreateBox(new Vector3(1, 1, 1)));

        physicsSystem.Update(0.001f);

        physicsSystem.Raycast(Vector3.Zero, Vector3.UnitX, 100f, out var result);

        Assert.Equal(box.Id, result.Entity.Id);
    }

    #endregion

    #region Entity Lifecycle Tests

    [Fact]
    public void DestroyEntity_RemovesFromPhysics()
    {
        using var world = new World();
        var physicsSystem = world.AddSystem<PhysicsSystem>();

        var entity = world.CreateEntity();
        world.AddComponent(entity, new TransformComponent());
        world.AddComponent(entity, RigidBodyComponent.CreateDynamic(1f));

        physicsSystem.Update(0.016f);
        Assert.Equal(1, physicsSystem.PhysicsWorld.BodyCount);

        // Remove the rigid body component
        world.RemoveComponent<RigidBodyComponent>(entity);

        physicsSystem.Update(0.016f);
        Assert.Equal(0, physicsSystem.PhysicsWorld.BodyCount);
    }

    [Fact]
    public void MultipleEntities_AllSimulated()
    {
        using var world = new World();
        var physicsSystem = world.AddSystem<PhysicsSystem>();

        // Create multiple falling balls
        var entities = new List<Entity>();
        for (int i = 0; i < 10; i++)
        {
            var ball = world.CreateEntity();
            world.AddComponent(ball, new TransformComponent { Position = new Vector3(i * 2, 10, 0) });
            world.AddComponent(ball, RigidBodyComponent.CreateDynamic(1f));
            world.AddComponent(ball, ColliderComponent.CreateSphere(0.5f));
            entities.Add(ball);
        }

        physicsSystem.Update(0.016f);
        Assert.Equal(10, physicsSystem.PhysicsWorld.BodyCount);

        // All should fall
        for (int i = 0; i < 10; i++)
        {
            physicsSystem.Update(0.016f);
        }

        foreach (var entity in entities)
        {
            ref var transform = ref world.GetComponent<TransformComponent>(entity);
            Assert.True(transform.Position.Y < 10f, "Each ball should have fallen");
        }
    }

    #endregion

    #region Transform Hierarchy + Physics Tests

    [Fact]
    public void Transform_WorldPosition_MatchesPhysicsPosition()
    {
        using var world = new World();
        var physicsSystem = world.AddSystem<PhysicsSystem>();

        var ball = world.CreateEntity();
        var initialPos = new Vector3(5, 10, 3);
        world.AddComponent(ball, new TransformComponent { Position = initialPos });
        world.AddComponent(ball, RigidBodyComponent.CreateDynamic(1f));
        world.AddComponent(ball, ColliderComponent.CreateSphere(0.5f));

        physicsSystem.Update(0.016f);

        ref var transform = ref world.GetComponent<TransformComponent>(ball);
        var body = physicsSystem.PhysicsWorld.GetBody(ball.Id);

        // Initial positions should match
        Assert.NotNull(body);
        Assert.Equal(transform.Position.X, body.Position.X, 3);
        Assert.Equal(transform.Position.Z, body.Position.Z, 3);
    }

    #endregion
}
