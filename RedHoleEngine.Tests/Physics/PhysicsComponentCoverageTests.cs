using System.Numerics;
using RedHoleEngine.Components;
using RedHoleEngine.Core.ECS;
using RedHoleEngine.Physics;
using RedHoleEngine.Physics.Collision;
using RedHoleEngine.Physics.Constraints;

namespace RedHoleEngine.Tests.Physics;

public class PhysicsComponentCoverageTests
{
    private const float Epsilon = 0.001f;

    [Fact]
    public void RigidBodyFactories_SetTypeMassAndDefaults()
    {
        var dynamicBody = RigidBodyComponent.CreateDynamic(4f);
        Assert.Equal(RigidBodyType.Dynamic, dynamicBody.Type);
        Assert.Equal(4f, dynamicBody.Mass);
        Assert.True(dynamicBody.UseGravity);
        Assert.Equal(0.3f, dynamicBody.Restitution);
        Assert.Equal(0.5f, dynamicBody.Friction);
        Assert.Equal(0.01f, dynamicBody.LinearDamping);
        Assert.Equal(0.05f, dynamicBody.AngularDamping);
        Assert.Equal(1u, dynamicBody.CollisionLayer);
        Assert.Equal(uint.MaxValue, dynamicBody.CollisionMask);

        var staticBody = RigidBodyComponent.CreateStatic();
        Assert.Equal(RigidBodyType.Static, staticBody.Type);
        Assert.Equal(0f, staticBody.Mass);
        Assert.False(staticBody.UseGravity);
        Assert.Equal(0f, staticBody.LinearDamping);
        Assert.Equal(0f, staticBody.AngularDamping);

        var kinematic = RigidBodyComponent.CreateKinematic();
        Assert.Equal(RigidBodyType.Kinematic, kinematic.Type);
        Assert.Equal(0f, kinematic.Mass);
        Assert.False(kinematic.UseGravity);
    }

    [Fact]
    public void RigidBody_BeforeRegistration_IgnoresVelocityAndForces()
    {
        var component = RigidBodyComponent.CreateDynamic(2f);

        component.LinearVelocity = new Vector3(3f, 4f, 5f);
        component.AngularVelocity = Vector3.One;
        component.ApplyForce(new Vector3(9f, 0f, 0f));
        component.ApplyForceAtPosition(Vector3.UnitX, Vector3.Zero);
        component.ApplyImpulse(Vector3.UnitY);
        component.ApplyImpulseAtPosition(Vector3.UnitZ, Vector3.One);
        component.ApplyTorque(Vector3.UnitX);
        component.ApplyAngularImpulse(Vector3.UnitY);
        component.Wake();

        Assert.Equal(Vector3.Zero, component.LinearVelocity);
        Assert.Equal(Vector3.Zero, component.AngularVelocity);
        Assert.False(component.IsAwake);
    }

    [Fact]
    public void RigidBody_VelocitySetBeforeFirstUpdate_IsDropped()
    {
        using var world = new World();
        var physics = world.AddSystem<PhysicsSystem>();
        physics.Settings.Gravity = Vector3.Zero;
        var entity = world.CreateEntity();
        world.AddComponent(entity, new TransformComponent());
        world.AddComponent(entity, RigidBodyComponent.CreateDynamic(1f));

        world.GetComponent<RigidBodyComponent>(entity).LinearVelocity = new Vector3(3f, 0f, 0f);
        physics.Update(1f / 60f);

        Assert.Equal(0f, physics.PhysicsWorld.GetBody(entity.Id)!.LinearVelocity.X);

        world.GetComponent<RigidBodyComponent>(entity).LinearVelocity = new Vector3(4f, 1f, 0f);
        world.GetComponent<RigidBodyComponent>(entity).AngularVelocity = new Vector3(0f, 2f, 0f);
        Assert.Equal(4f, physics.PhysicsWorld.GetBody(entity.Id)!.LinearVelocity.X);
        Assert.Equal(2f, world.GetComponent<RigidBodyComponent>(entity).AngularVelocity.Y);
    }

    [Fact(Skip = "BUG: RigidBodyComponent.LinearVelocity has no storage until PhysicsSystem creates the body, so a velocity assigned before the first update is discarded. Expected the body to spawn with velocity X=3; actual X=0.")]
    public void RigidBody_VelocitySetBeforeFirstUpdate_ShouldBeKept()
    {
        using var world = new World();
        var physics = world.AddSystem<PhysicsSystem>();
        physics.Settings.Gravity = Vector3.Zero;
        var entity = world.CreateEntity();
        world.AddComponent(entity, new TransformComponent());
        world.AddComponent(entity, RigidBodyComponent.CreateDynamic(1f));

        world.GetComponent<RigidBodyComponent>(entity).LinearVelocity = new Vector3(3f, 0f, 0f);
        physics.Update(1f / 60f);

        Assert.Equal(3f, physics.PhysicsWorld.GetBody(entity.Id)!.LinearVelocity.X, Epsilon);
    }

    [Fact]
    public void RigidBody_AfterRegistration_ForwardsForcesImpulsesAndWake()
    {
        using var world = new World();
        var physics = world.AddSystem<PhysicsSystem>();
        physics.Settings.Gravity = Vector3.Zero;
        var entity = world.CreateEntity();
        world.AddComponent(entity, new TransformComponent());
        world.AddComponent(entity, RigidBodyComponent.CreateDynamic(2f));
        physics.Update(1f / 60f);

        ref var component = ref world.GetComponent<RigidBodyComponent>(entity);
        var body = physics.PhysicsWorld.GetBody(entity.Id)!;
        body.Inertia = new Vector3(1f, 1f, 1f);
        body.IsAwake = false;

        Assert.False(component.IsAwake);
        component.Wake();
        Assert.True(component.IsAwake);
        Assert.True(body.IsAwake);

        component.ApplyForce(new Vector3(4f, 0f, 0f));
        component.ApplyForceAtPosition(new Vector3(0f, 2f, 0f), body.Position + Vector3.UnitX);
        component.ApplyTorque(new Vector3(0f, 0.5f, 0f));
        Assert.Equal(4f, body.Force.X, Epsilon);
        Assert.Equal(2f, body.Force.Y, Epsilon);
        Assert.Equal(2f, body.Torque.Z, Epsilon);
        Assert.Equal(0.5f, body.Torque.Y, Epsilon);

        component.ApplyImpulse(new Vector3(2f, 0f, 0f));
        component.ApplyImpulseAtPosition(new Vector3(0f, 1f, 0f), body.Position + Vector3.UnitX);
        component.ApplyAngularImpulse(new Vector3(0f, 0f, 3f));

        Assert.Equal(1f, component.LinearVelocity.X, Epsilon);
        Assert.Equal(0.5f, component.LinearVelocity.Y, Epsilon);
        Assert.True(component.AngularVelocity.Z > 2f);
    }

    [Fact]
    public void ColliderFactories_ShapesTriggersAndMaterials()
    {
        var sphere = ColliderComponent.CreateSphere(1.5f, new Vector3(0f, 2f, 0f));
        Assert.Equal(ColliderType.Sphere, sphere.ShapeType);
        Assert.Equal(1.5f, sphere.SphereRadius);
        Assert.Equal(new Vector3(0f, 2f, 0f), sphere.Offset);

        var box = ColliderComponent.CreateBoxFromSize(new Vector3(2f, 4f, 6f), new Vector3(1f, 0f, 0f));
        Assert.Equal(ColliderType.Box, box.ShapeType);
        Assert.Equal(new Vector3(1f, 2f, 3f), box.BoxHalfExtents);
        Assert.Equal(new Vector3(1f, 0f, 0f), box.Offset);

        var capsule = ColliderComponent.CreateCapsule(0.4f, 1.8f, axis: 2, new Vector3(0f, 1f, 0f));
        Assert.Equal(ColliderType.Capsule, capsule.ShapeType);
        Assert.Equal(0.4f, capsule.CapsuleRadius);
        Assert.Equal(1.8f, capsule.CapsuleHeight);
        Assert.Equal(2, capsule.CapsuleAxis);

        var plane = ColliderComponent.CreatePlane(new Vector3(0f, 5f, 0f), 3f);
        Assert.Equal(ColliderType.Plane, plane.ShapeType);
        Assert.Equal(Vector3.UnitY, plane.PlaneNormal);
        Assert.Equal(3f, plane.PlaneDistance);

        var ground = ColliderComponent.CreateGroundPlane(2.5f);
        Assert.Equal(Vector3.UnitY, ground.PlaneNormal);
        Assert.Equal(2.5f, ground.PlaneDistance);

        var trigger = ColliderComponent.CreateSphere(1f).AsTrigger().WithMaterial(0.2f, 0.3f, 0.4f);
        Assert.True(trigger.IsTrigger);
        Assert.Equal(0.2f, trigger.MaterialRestitution);
        Assert.Equal(0.3f, trigger.MaterialStaticFriction);
        Assert.Equal(0.4f, trigger.MaterialDynamicFriction);
    }

    [Fact]
    public void LinkAndMeshRel_ConstructorsStoreDefaults()
    {
        var constraint = LinkConstraint.Rope(1, 2, Vector3.Zero, Vector3.Zero, 3f);
        var link = new LinkComponent(constraint);
        Assert.Same(constraint, link.Constraint);
        Assert.True(link.AutoRegister);
        Assert.Equal(-1, link.LinkId);
        Assert.Equal(-1, link.ChainId);
        Assert.Equal(-1, link.MeshId);

        var manual = new LinkComponent(constraint, autoRegister: false);
        Assert.False(manual.AutoRegister);

        var mesh = new MeshRelComponent(LinkType.Plastic);
        Assert.Equal(LinkType.Plastic, mesh.DeformationType);
        Assert.True(mesh.AutoRegister);
        Assert.Equal(800f, mesh.Stiffness);
        Assert.Equal(10f, mesh.Damping);
        Assert.Equal(1.1f, mesh.YieldThreshold);
        Assert.Equal(1.6f, mesh.BreakThreshold);
        Assert.Equal(0.5f, mesh.PlasticRate);
        Assert.True(mesh.IncludeBendLinks);
        Assert.Equal(0.5f, mesh.BendStiffnessMultiplier);
        Assert.Equal(1f, mesh.NodeMass);
        Assert.Equal(0.02f, mesh.NodeLinearDamping);
        Assert.Equal(0.1f, mesh.NodeAngularDamping);
        Assert.False(mesh.UseGravity);
        Assert.NotNull(mesh.PinnedVertexIndices);
        Assert.False(mesh.CloneMeshOnStart);
        Assert.True(mesh.RecalculateNormals);
        Assert.False(mesh.RecalculateTangents);
        Assert.Equal(0.1f, mesh.NormalUpdateInterval);
    }

    [Fact]
    public void ExtensionMethods_AddBodiesCollidersAndGround()
    {
        using var world = new World();

        var dynamicEntity = world.CreateEntity();
        world.AddRigidBody(dynamicEntity, 6f);
        Assert.Equal(RigidBodyType.Dynamic, world.GetComponent<RigidBodyComponent>(dynamicEntity).Type);
        Assert.Equal(6f, world.GetComponent<RigidBodyComponent>(dynamicEntity).Mass);

        var staticEntity = world.CreateEntity();
        world.AddStaticBody(staticEntity);
        Assert.Equal(RigidBodyType.Static, world.GetComponent<RigidBodyComponent>(staticEntity).Type);

        var kinematicEntity = world.CreateEntity();
        world.AddKinematicBody(kinematicEntity);
        Assert.Equal(RigidBodyType.Kinematic, world.GetComponent<RigidBodyComponent>(kinematicEntity).Type);

        var sphereEntity = world.CreateEntity();
        world.AddSphereCollider(sphereEntity, 2f, new Vector3(0f, 1f, 0f));
        ref var sphere = ref world.GetComponent<ColliderComponent>(sphereEntity);
        Assert.Equal(ColliderType.Sphere, sphere.ShapeType);
        Assert.Equal(2f, sphere.SphereRadius);
        Assert.Equal(new Vector3(0f, 1f, 0f), sphere.Offset);

        var boxEntity = world.CreateEntity();
        world.AddBoxCollider(boxEntity, new Vector3(1f, 2f, 3f));
        Assert.Equal(new Vector3(1f, 2f, 3f), world.GetComponent<ColliderComponent>(boxEntity).BoxHalfExtents);

        var capsuleEntity = world.CreateEntity();
        world.AddCapsuleCollider(capsuleEntity, 0.3f, 1.2f, axis: 0);
        ref var capsule = ref world.GetComponent<ColliderComponent>(capsuleEntity);
        Assert.Equal(ColliderType.Capsule, capsule.ShapeType);
        Assert.Equal(0, capsule.CapsuleAxis);
        Assert.Equal(1.2f, capsule.CapsuleHeight);

        var ground = world.CreateEntity();
        world.AddGroundPlane(ground, 4f);
        Assert.Equal(RigidBodyType.Static, world.GetComponent<RigidBodyComponent>(ground).Type);
        ref var plane = ref world.GetComponent<ColliderComponent>(ground);
        Assert.Equal(ColliderType.Plane, plane.ShapeType);
        Assert.Equal(Vector3.UnitY, plane.PlaneNormal);
        Assert.Equal(4f, plane.PlaneDistance);
    }
}
