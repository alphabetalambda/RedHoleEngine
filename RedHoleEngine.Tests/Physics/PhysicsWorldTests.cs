using System.Numerics;
using RedHoleEngine.Physics;
using RedHoleEngine.Physics.Collision;

namespace RedHoleEngine.Tests.Physics;

public class PhysicsWorldTests
{
    private const float Epsilon = 0.01f;
    private static int _nextEntityId = 1;
    
    private static RigidBody CreateBody(RigidBodyType type = RigidBodyType.Dynamic, float mass = 1f)
    {
        return new RigidBody
        {
            EntityId = _nextEntityId++,
            Type = type,
            Mass = mass
        };
    }

    #region Basic Simulation Tests

    [Fact]
    public void Step_AppliesGravity_ObjectFalls()
    {
        var world = new PhysicsWorld();
        world.Settings.Gravity = new Vector3(0f, -10f, 0f);
        
        var body = CreateBody();
        body.Position = new Vector3(0f, 10f, 0f);
        body.UseGravity = true;
        
        world.AddBody(body);
        
        // Step for 1 second
        for (int i = 0; i < 60; i++)
        {
            world.Step(1f / 60f);
        }
        
        // Should have fallen due to gravity
        Assert.True(body.Position.Y < 10f);
        Assert.True(body.LinearVelocity.Y < 0f);
    }

    [Fact]
    public void Step_StaticBody_DoesNotMove()
    {
        var world = new PhysicsWorld();
        world.Settings.Gravity = new Vector3(0f, -10f, 0f);
        
        var body = CreateBody(RigidBodyType.Static);
        body.Position = new Vector3(0f, 0f, 0f);
        
        world.AddBody(body);
        
        world.Step(1f);
        
        Assert.Equal(Vector3.Zero, body.Position);
    }

    [Fact]
    public void Step_AppliedForce_Accelerates()
    {
        var world = new PhysicsWorld();
        world.Settings.Gravity = Vector3.Zero; // Disable gravity for this test
        
        var body = CreateBody();
        body.Position = Vector3.Zero;
        
        world.AddBody(body);
        
        body.ApplyForce(new Vector3(10f, 0f, 0f));
        
        world.Step(1f);
        
        // F = ma, so a = 10, after 1 second v = 10, position = 0.5 * a * t^2 = 5
        // (with semi-implicit Euler it might be slightly different)
        Assert.True(body.LinearVelocity.X > 0f);
        Assert.True(body.Position.X > 0f);
    }

    [Fact]
    public void Step_Damping_SlowsDown()
    {
        var world = new PhysicsWorld();
        world.Settings.Gravity = Vector3.Zero;
        
        var body = CreateBody();
        body.LinearVelocity = new Vector3(10f, 0f, 0f);
        body.LinearDamping = 0.1f;
        
        world.AddBody(body);
        
        float initialSpeed = body.LinearVelocity.X;
        
        for (int i = 0; i < 100; i++)
        {
            world.Step(1f / 60f);
        }
        
        // Should have slowed down due to damping
        Assert.True(body.LinearVelocity.X < initialSpeed);
        Assert.True(body.LinearVelocity.X > 0f); // But still moving
    }

    #endregion

    #region Collision Response Tests

    [Fact]
    public void Step_SpheresCollide_Bounce()
    {
        var world = new PhysicsWorld();
        world.Settings.Gravity = Vector3.Zero;
        
        var sphereA = new SphereCollider(1f);
        var sphereB = new SphereCollider(1f);
        
        var bodyA = CreateBody();
        bodyA.Position = new Vector3(-2f, 0f, 0f);
        bodyA.LinearVelocity = new Vector3(5f, 0f, 0f);
        bodyA.Restitution = 1f; // Perfect bounce
        
        var bodyB = CreateBody();
        bodyB.Position = new Vector3(2f, 0f, 0f);
        bodyB.LinearVelocity = new Vector3(-5f, 0f, 0f);
        bodyB.Restitution = 1f;
        
        world.AddBody(bodyA);
        world.AddBody(bodyB);
        world.AddCollider(bodyA, sphereA);
        world.AddCollider(bodyB, sphereB);
        
        // Run simulation
        for (int i = 0; i < 120; i++)
        {
            world.Step(1f / 60f);
        }
        
        // After collision with restitution 1, they should bounce back
        // A should now be moving left (negative), B moving right (positive)
        Assert.True(bodyA.LinearVelocity.X < 0f);
        Assert.True(bodyB.LinearVelocity.X > 0f);
    }

    [Fact]
    public void Step_SphereHitsGround_Stops()
    {
        var world = new PhysicsWorld();
        world.Settings.Gravity = new Vector3(0f, -10f, 0f);
        
        var sphere = new SphereCollider(1f);
        var ground = new PlaneCollider(Vector3.UnitY, 0f);
        
        var ball = CreateBody();
        ball.Position = new Vector3(0f, 5f, 0f);
        ball.Restitution = 0f; // No bounce
        ball.UseGravity = true;
        ball.LinearDamping = 0.5f; // Add damping to help settle
        
        var floor = CreateBody(RigidBodyType.Static);
        floor.Position = Vector3.Zero;
        
        world.AddBody(ball);
        world.AddBody(floor);
        world.AddCollider(ball, sphere);
        world.AddCollider(floor, ground);
        
        // Run simulation for 5 seconds
        for (int i = 0; i < 300; i++)
        {
            world.Step(1f / 60f);
        }
        
        // Ball should rest on ground (Y position ~= radius of 1)
        Assert.True(ball.Position.Y >= 0.5f && ball.Position.Y <= 2.0f, 
            $"Ball Y position was {ball.Position.Y}, expected near 1.0");
    }

    #endregion

    #region Body Management Tests

    [Fact]
    public void AddBody_IncreasesBodyCount()
    {
        var world = new PhysicsWorld();
        
        Assert.Equal(0, world.BodyCount);
        
        world.AddBody(CreateBody());
        Assert.Equal(1, world.BodyCount);
        
        world.AddBody(CreateBody());
        Assert.Equal(2, world.BodyCount);
    }

    [Fact]
    public void RemoveBody_DecreasesBodyCount()
    {
        var world = new PhysicsWorld();
        var body = CreateBody();
        
        world.AddBody(body);
        Assert.Equal(1, world.BodyCount);
        
        world.RemoveBody(body);
        Assert.Equal(0, world.BodyCount);
    }

    [Fact]
    public void Clear_RemovesAllBodies()
    {
        var world = new PhysicsWorld();
        world.AddBody(CreateBody());
        world.AddBody(CreateBody());
        world.AddBody(CreateBody());
        
        world.Clear();
        
        Assert.Equal(0, world.BodyCount);
    }

    #endregion

    #region Collision Events Tests

    [Fact]
    public void OnCollisionEnter_FiresWhenBodiesCollide()
    {
        var world = new PhysicsWorld();
        world.Settings.Gravity = Vector3.Zero;
        
        bool collisionDetected = false;
        world.OnCollisionEnter += (evt) => collisionDetected = true;
        
        var sphereA = new SphereCollider(1f);
        var sphereB = new SphereCollider(1f);
        
        var bodyA = CreateBody();
        bodyA.Position = new Vector3(-1f, 0f, 0f);
        bodyA.LinearVelocity = new Vector3(10f, 0f, 0f);
        
        var bodyB = CreateBody();
        bodyB.Position = new Vector3(1f, 0f, 0f);
        
        world.AddBody(bodyA);
        world.AddBody(bodyB);
        world.AddCollider(bodyA, sphereA);
        world.AddCollider(bodyB, sphereB);
        
        // Bodies should collide after a few steps
        for (int i = 0; i < 30; i++)
        {
            world.Step(1f / 60f);
            if (collisionDetected) break;
        }
        
        Assert.True(collisionDetected);
    }

    #endregion

    #region Raycast Tests

    [Fact]
    public void Raycast_HitsSphere_ReturnsTrue()
    {
        var world = new PhysicsWorld();
        
        var sphere = new SphereCollider(1f);
        var body = CreateBody(RigidBodyType.Static);
        body.Position = new Vector3(5f, 0f, 0f);
        
        world.AddBody(body);
        world.AddCollider(body, sphere);
        
        bool hit = world.Raycast(
            Vector3.Zero,
            Vector3.UnitX,
            100f,
            out var raycastHit);
        
        Assert.True(hit);
        Assert.Equal(body, raycastHit.Body);
        Assert.True(raycastHit.Distance > 0f && raycastHit.Distance < 5f);
    }

    [Fact]
    public void Raycast_MissesAllBodies_ReturnsFalse()
    {
        var world = new PhysicsWorld();
        
        var sphere = new SphereCollider(1f);
        var body = CreateBody(RigidBodyType.Static);
        body.Position = new Vector3(5f, 5f, 0f); // Offset from ray
        
        world.AddBody(body);
        world.AddCollider(body, sphere);
        
        bool hit = world.Raycast(
            Vector3.Zero,
            Vector3.UnitX,
            100f,
            out _);
        
        Assert.False(hit);
    }

    #endregion
}
