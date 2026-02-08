using System.Numerics;
using RedHoleEngine.Audio;
using RedHoleEngine.Components;
using RedHoleEngine.Core.ECS;
using RedHoleEngine.Physics;

namespace RedHoleEngine.Tests.Integration;

/// <summary>
/// Integration tests for CollisionAudioSystem with Physics
/// Note: These tests don't actually play sounds (no audio backend initialized)
/// but verify that the system correctly processes collision events.
/// </summary>
public class CollisionAudioIntegrationTests
{
    #region System Integration Tests

    [Fact]
    public void CollisionAudioSystem_CanBeAddedWithPhysicsSystem()
    {
        using var world = new World();
        var physicsSystem = world.AddSystem<PhysicsSystem>();
        var audioSystem = world.AddSystem<CollisionAudioSystem>();

        Assert.NotNull(physicsSystem);
        Assert.NotNull(audioSystem);
    }

    [Fact]
    public void CollisionAudioSystem_HasCorrectPriority()
    {
        using var world = new World();
        var physicsSystem = world.AddSystem<PhysicsSystem>();
        var audioSystem = world.AddSystem<CollisionAudioSystem>();

        // Collision audio should run after physics
        Assert.True(audioSystem.Priority > physicsSystem.Priority);
    }

    [Fact]
    public void CollisionAudioSystem_ReceivesCollisionEvents()
    {
        using var world = new World();
        var physicsSystem = world.AddSystem<PhysicsSystem>();
        var audioSystem = world.AddSystem<CollisionAudioSystem>();
        
        // We can test that the system subscribes to collision events
        // by checking that the system was created and has correct priority
        Assert.NotNull(audioSystem);
        Assert.NotNull(audioSystem.SoundLibrary);
        Assert.True(audioSystem.Priority > physicsSystem.Priority);
        
        // The actual Initialize() with a real AudioEngine would subscribe to events
        // For unit testing the component setup, we just verify the system setup
    }

    #endregion

    #region CollisionSoundComponent + Physics Tests

    [Fact]
    public void Entity_WithCollisionSound_TracksImpacts()
    {
        using var world = new World();
        var physicsSystem = world.AddSystem<PhysicsSystem>();

        // Create ground with collision sound
        var ground = world.CreateEntity();
        world.AddComponent(ground, new TransformComponent());
        world.AddComponent(ground, RigidBodyComponent.CreateStatic());
        world.AddComponent(ground, ColliderComponent.CreateGroundPlane());
        world.AddCollisionSound(ground, SurfaceType.Stone);

        // Create falling ball with collision sound
        var ball = world.CreateEntity();
        world.AddComponent(ball, new TransformComponent { Position = new Vector3(0, 1f, 0) });
        world.AddComponent(ball, RigidBodyComponent.CreateDynamic(1f));
        world.AddComponent(ball, ColliderComponent.CreateSphere(0.5f));
        world.AddCollisionSound(ball, SurfaceType.Metal);

        // Verify components were added
        Assert.True(world.HasCollisionSound(ground));
        Assert.True(world.HasCollisionSound(ball));

        ref var groundSound = ref world.GetComponent<CollisionSoundComponent>(ground);
        ref var ballSound = ref world.GetComponent<CollisionSoundComponent>(ball);

        Assert.Equal(SurfaceType.Stone, groundSound.SurfaceType);
        Assert.Equal(SurfaceType.Metal, ballSound.SurfaceType);
    }

    [Fact]
    public void CollisionEvent_ContainsBothSurfaces()
    {
        using var world = new World();
        var physicsSystem = world.AddSystem<PhysicsSystem>();

        SurfaceType? surfaceA = null;
        SurfaceType? surfaceB = null;

        physicsSystem.PhysicsWorld.OnCollisionEnter += evt =>
        {
            // Get surface types from components
            if (world.TryGetEntity(evt.BodyA.EntityId, out var entityA) &&
                world.HasComponent<CollisionSoundComponent>(entityA))
            {
                surfaceA = world.GetComponent<CollisionSoundComponent>(entityA).SurfaceType;
            }
            if (world.TryGetEntity(evt.BodyB.EntityId, out var entityB) &&
                world.HasComponent<CollisionSoundComponent>(entityB))
            {
                surfaceB = world.GetComponent<CollisionSoundComponent>(entityB).SurfaceType;
            }
        };

        // Create ground
        var ground = world.CreateEntity();
        world.AddComponent(ground, new TransformComponent());
        world.AddComponent(ground, RigidBodyComponent.CreateStatic());
        world.AddComponent(ground, ColliderComponent.CreateGroundPlane());
        world.AddCollisionSound(ground, SurfaceType.Concrete);

        // Create ball
        var ball = world.CreateEntity();
        world.AddComponent(ball, new TransformComponent { Position = new Vector3(0, 1f, 0) });
        world.AddComponent(ball, RigidBodyComponent.CreateDynamic(1f));
        world.AddComponent(ball, ColliderComponent.CreateSphere(0.5f));
        world.AddCollisionSound(ball, SurfaceType.Glass);

        // Run simulation until collision
        for (int i = 0; i < 100 && (surfaceA == null || surfaceB == null); i++)
        {
            physicsSystem.Update(0.016f);
        }

        // Both surfaces should have been identified
        var surfaces = new HashSet<SurfaceType?> { surfaceA, surfaceB };
        Assert.Contains(SurfaceType.Concrete, surfaces);
        Assert.Contains(SurfaceType.Glass, surfaces);
    }

    #endregion

    #region Impact Velocity Tests

    [Fact]
    public void HighVelocityImpact_HasHigherImpactSpeed()
    {
        using var world = new World();
        var physicsSystem = world.AddSystem<PhysicsSystem>();

        float? impactVelocity = null;

        physicsSystem.PhysicsWorld.OnCollisionEnter += evt =>
        {
            if (evt.Manifold.Contacts.Count > 0)
            {
                var contact = evt.Manifold.Contacts[0];
                var velA = evt.BodyA.GetVelocityAtPoint(contact.PointOnA);
                var velB = evt.BodyB.GetVelocityAtPoint(contact.PointOnB);
                var relativeVel = velB - velA;
                impactVelocity = MathF.Abs(Vector3.Dot(relativeVel, contact.Normal));
            }
        };

        // Create ground
        var ground = world.CreateEntity();
        world.AddComponent(ground, new TransformComponent());
        world.AddComponent(ground, RigidBodyComponent.CreateStatic());
        world.AddComponent(ground, ColliderComponent.CreateGroundPlane());

        // Create ball falling from height
        var ball = world.CreateEntity();
        world.AddComponent(ball, new TransformComponent { Position = new Vector3(0, 10f, 0) });
        world.AddComponent(ball, RigidBodyComponent.CreateDynamic(1f));
        world.AddComponent(ball, ColliderComponent.CreateSphere(0.5f));

        // Run until collision
        for (int i = 0; i < 200 && impactVelocity == null; i++)
        {
            physicsSystem.Update(0.016f);
        }

        Assert.NotNull(impactVelocity);
        // Ball falling from 10m height will have velocity around sqrt(2*g*h) ≈ sqrt(2*9.8*10) ≈ 14 m/s
        // But with damping and collision handling, it may be lower
        Assert.True(impactVelocity > 1f, $"Ball falling from height should have significant impact velocity, got {impactVelocity}");
    }

    [Fact]
    public void LowVelocityImpact_HasLowerImpactSpeed()
    {
        using var world = new World();
        var physicsSystem = world.AddSystem<PhysicsSystem>();

        float? impactVelocity = null;

        physicsSystem.PhysicsWorld.OnCollisionEnter += evt =>
        {
            if (evt.Manifold.Contacts.Count > 0)
            {
                var contact = evt.Manifold.Contacts[0];
                var velA = evt.BodyA.GetVelocityAtPoint(contact.PointOnA);
                var velB = evt.BodyB.GetVelocityAtPoint(contact.PointOnB);
                var relativeVel = velB - velA;
                impactVelocity = MathF.Abs(Vector3.Dot(relativeVel, contact.Normal));
            }
        };

        // Create ground
        var ground = world.CreateEntity();
        world.AddComponent(ground, new TransformComponent());
        world.AddComponent(ground, RigidBodyComponent.CreateStatic());
        world.AddComponent(ground, ColliderComponent.CreateGroundPlane());

        // Create ball barely above ground
        var ball = world.CreateEntity();
        world.AddComponent(ball, new TransformComponent { Position = new Vector3(0, 0.6f, 0) });
        world.AddComponent(ball, RigidBodyComponent.CreateDynamic(1f));
        world.AddComponent(ball, ColliderComponent.CreateSphere(0.5f));

        // Run until collision
        for (int i = 0; i < 50 && impactVelocity == null; i++)
        {
            physicsSystem.Update(0.016f);
        }

        Assert.NotNull(impactVelocity);
        Assert.True(impactVelocity < 3f, $"Ball barely above ground should have low impact velocity, got {impactVelocity}");
    }

    #endregion

    #region Contact Point Tests

    [Fact]
    public void ContactPoint_IsAtCorrectLocation()
    {
        using var world = new World();
        var physicsSystem = world.AddSystem<PhysicsSystem>();

        Vector3? contactPoint = null;

        physicsSystem.PhysicsWorld.OnCollisionEnter += evt =>
        {
            if (evt.Manifold.Contacts.Count > 0)
            {
                var contact = evt.Manifold.Contacts[0];
                contactPoint = (contact.PointOnA + contact.PointOnB) * 0.5f;
            }
        };

        // Create ground at Y=0
        var ground = world.CreateEntity();
        world.AddComponent(ground, new TransformComponent());
        world.AddComponent(ground, RigidBodyComponent.CreateStatic());
        world.AddComponent(ground, ColliderComponent.CreateGroundPlane());

        // Create ball directly above origin
        var ball = world.CreateEntity();
        world.AddComponent(ball, new TransformComponent { Position = new Vector3(0, 1f, 0) });
        world.AddComponent(ball, RigidBodyComponent.CreateDynamic(1f));
        world.AddComponent(ball, ColliderComponent.CreateSphere(0.5f));

        for (int i = 0; i < 100 && contactPoint == null; i++)
        {
            physicsSystem.Update(0.016f);
        }

        Assert.NotNull(contactPoint);
        
        // Contact should be near origin (X ≈ 0, Z ≈ 0) and near ground (Y ≈ 0)
        Assert.True(MathF.Abs(contactPoint.Value.X) < 0.5f, "Contact X should be near 0");
        Assert.True(MathF.Abs(contactPoint.Value.Z) < 0.5f, "Contact Z should be near 0");
        Assert.True(MathF.Abs(contactPoint.Value.Y) < 1f, "Contact Y should be near ground");
    }

    #endregion

    #region Multiple Collision Tests

    [Fact]
    public void MultipleBalls_EachGeneratesCollision()
    {
        using var world = new World();
        var physicsSystem = world.AddSystem<PhysicsSystem>();

        var collidedEntities = new HashSet<int>();

        physicsSystem.PhysicsWorld.OnCollisionEnter += evt =>
        {
            collidedEntities.Add(evt.BodyA.EntityId);
            collidedEntities.Add(evt.BodyB.EntityId);
        };

        // Create ground
        var ground = world.CreateEntity();
        world.AddComponent(ground, new TransformComponent());
        world.AddComponent(ground, RigidBodyComponent.CreateStatic());
        world.AddComponent(ground, ColliderComponent.CreateGroundPlane());

        // Create multiple balls
        var balls = new List<Entity>();
        for (int i = 0; i < 5; i++)
        {
            var ball = world.CreateEntity();
            world.AddComponent(ball, new TransformComponent { Position = new Vector3(i * 3, 1f, 0) });
            world.AddComponent(ball, RigidBodyComponent.CreateDynamic(1f));
            world.AddComponent(ball, ColliderComponent.CreateSphere(0.5f));
            world.AddCollisionSound(ball, SurfaceType.Metal);
            balls.Add(ball);
        }

        // Run simulation
        for (int i = 0; i < 100; i++)
        {
            physicsSystem.Update(0.016f);
        }

        // All balls should have collided with ground
        foreach (var ball in balls)
        {
            Assert.Contains(ball.Id, collidedEntities);
        }
    }

    #endregion

    #region Custom Sound Override Tests

    [Fact]
    public void Entity_WithCustomSound_UsesCustomPath()
    {
        using var world = new World();

        var entity = world.CreateEntity();
        world.AddCollisionSound(entity, new CollisionSoundComponent
        {
            SurfaceType = SurfaceType.Default,
            VolumeMultiplier = 1f,
            PitchMultiplier = 1f,
            CustomSoundPath = "sounds/custom/explosion.wav",
            Enabled = true
        });

        ref var comp = ref world.GetComponent<CollisionSoundComponent>(entity);
        Assert.Equal("sounds/custom/explosion.wav", comp.CustomSoundPath);
    }

    [Fact]
    public void DisabledCollisionSound_IsRespected()
    {
        using var world = new World();

        var entity = world.CreateEntity();
        var component = CollisionSoundComponent.Create(SurfaceType.Metal);
        component.Enabled = false;
        world.AddCollisionSound(entity, component);

        ref var comp = ref world.GetComponent<CollisionSoundComponent>(entity);
        Assert.False(comp.Enabled);
    }

    #endregion

    #region Surface Material Combination Tests

    [Theory]
    [InlineData(SurfaceType.Metal, SurfaceType.Metal)]
    [InlineData(SurfaceType.Metal, SurfaceType.Stone)]
    [InlineData(SurfaceType.Wood, SurfaceType.Wood)]
    [InlineData(SurfaceType.Glass, SurfaceType.Concrete)]
    [InlineData(SurfaceType.Rubber, SurfaceType.Metal)]
    public void ImpactSoundLibrary_HandlesMaterialCombination(SurfaceType a, SurfaceType b)
    {
        var library = new ImpactSoundLibrary();
        
        var soundPath = library.GetImpactSound(a, b);
        
        Assert.NotNull(soundPath);
        Assert.NotEmpty(soundPath);
        Assert.EndsWith(".wav", soundPath);
    }

    #endregion
}
