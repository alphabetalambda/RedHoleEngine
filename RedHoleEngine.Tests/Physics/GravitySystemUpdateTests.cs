using System.Numerics;
using RedHoleEngine.Components;
using RedHoleEngine.Core.ECS;
using RedHoleEngine.Physics;

namespace RedHoleEngine.Tests.Physics;

public class GravitySystemUpdateTests
{
    private const float Epsilon = 0.001f;

    [Fact]
    public void Update_WithoutWorld_DoesNotThrow()
    {
        var gravity = new GravitySystem();

        var ex = Record.Exception(() => gravity.Update(0.016f));

        Assert.Null(ex);
    }

    [Fact]
    public void Priority_RunsBeforePhysicsSystem()
    {
        var gravity = new GravitySystem();
        var physics = new PhysicsSystem();

        Assert.Equal(-150, gravity.Priority);
        Assert.True(gravity.Priority < physics.Priority);
    }

    [Fact]
    public void Update_NoSources_LeavesForceAtZero()
    {
        using var world = new World();
        var physics = world.AddSystem<PhysicsSystem>();
        physics.Settings.Gravity = Vector3.Zero;
        var gravity = world.AddSystem<GravitySystem>();

        var bodyEntity = AddDynamicBody(world, new Vector3(4f, 2f, 0f), 2f);

        physics.Update(1f / 60f);
        gravity.Update(1f / 60f);

        var force = ForceOn(physics, bodyEntity);
        Assert.Equal(Vector3.Zero, force);
    }

    [Fact]
    public void Update_UseGravityFalse_SkipsBody()
    {
        using var world = new World();
        var (physics, gravity, bodyEntity) = AddSingleSource(
            world,
            GravitySourceComponent.CreateUniform(-Vector3.UnitY, 10f),
            Vector3.Zero,
            new Vector3(0f, 5f, 0f),
            mass: 2f,
            useGravity: false);

        physics.Update(1f / 60f);
        gravity.Update(1f / 60f);

        Assert.Equal(Vector3.Zero, ForceOn(physics, bodyEntity));
    }

    [Theory]
    [InlineData(RigidBodyType.Kinematic)]
    [InlineData(RigidBodyType.Static)]
    public void Update_NonDynamic_SkipsEvenWhenUseGravityIsTrue(RigidBodyType type)
    {
        using var world = new World();
        var (physics, gravity, bodyEntity) = AddSingleSource(
            world,
            GravitySourceComponent.CreateUniform(-Vector3.UnitY, 10f),
            Vector3.Zero,
            new Vector3(1f, 2f, 3f),
            mass: 2f,
            type: type,
            useGravity: true);

        physics.Update(1f / 60f);
        gravity.Update(1f / 60f);

        Assert.Equal(Vector3.Zero, ForceOn(physics, bodyEntity));
        Assert.Equal(type, physics.PhysicsWorld.GetBody(bodyEntity.Id)!.Type);
    }

    [Fact]
    public void Update_Uniform_AppliesStrengthTimesMassEverywhere()
    {
        using var world = new World();
        var source = GravitySourceComponent.CreateUniform(new Vector3(0f, -2f, 0f), 9.81f);
        var (physics, gravity, nearBody) = AddSingleSource(
            world, source, Vector3.Zero, new Vector3(0f, 100f, 0f), mass: 2f);
        var farBody = AddDynamicBody(world, new Vector3(40f, -5f, 12f), 2f);

        physics.Update(1f / 60f);
        gravity.Update(1f / 60f);

        var expected = new Vector3(0f, -19.62f, 0f);
        Assert.Equal(expected.X, ForceOn(physics, nearBody).X, Epsilon);
        Assert.Equal(expected.Y, ForceOn(physics, nearBody).Y, Epsilon);
        Assert.Equal(expected.Z, ForceOn(physics, nearBody).Z, Epsilon);
        Assert.Equal(expected.Y, ForceOn(physics, farBody).Y, Epsilon);
    }

    [Fact]
    public void Update_Uniform_DoesNotRenormalizeStoredDirection()
    {
        using var world = new World();
        var source = GravitySourceComponent.CreateUniform(-Vector3.UnitY, 2f);
        source.UniformDirection = new Vector3(0f, -3f, 0f);
        var (physics, gravity, bodyEntity) = AddSingleSource(
            world, source, Vector3.Zero, new Vector3(5f, 5f, 5f), mass: 1f);

        physics.Update(1f / 60f);
        gravity.Update(1f / 60f);

        var force = ForceOn(physics, bodyEntity);
        Assert.Equal(0f, force.X, Epsilon);
        Assert.Equal(-6f, force.Y, Epsilon);
        Assert.Equal(0f, force.Z, Epsilon);
    }

    [Fact]
    public void Update_Uniform_IgnoresMaxRange()
    {
        using var world = new World();
        var source = GravitySourceComponent.CreateUniform(-Vector3.UnitY, 4f);
        source.MaxRange = 1f;
        var (physics, gravity, bodyEntity) = AddSingleSource(
            world, source, Vector3.Zero, new Vector3(0f, 50f, 0f), mass: 1f);

        physics.Update(1f / 60f);
        gravity.Update(1f / 60f);

        Assert.Equal(-4f, ForceOn(physics, bodyEntity).Y, Epsilon);
    }

    [Fact]
    public void Update_Newtonian_MatchesInverseSquareAndPointsAtSource()
    {
        using var world = new World();
        var (physics, gravity, bodyEntity) = AddSingleSource(
            world,
            GravitySourceComponent.CreateNewtonian(100f),
            Vector3.Zero,
            new Vector3(10f, 0f, 0f),
            mass: 2f);

        physics.Update(1f / 60f);
        gravity.Update(1f / 60f);

        // a = GM/r^2 = 100/100 = 1 toward -X, F = ma = -2
        var force = ForceOn(physics, bodyEntity);
        Assert.Equal(-2f, force.X, Epsilon);
        Assert.Equal(0f, force.Y, Epsilon);
        Assert.Equal(0f, force.Z, Epsilon);
    }

    [Fact]
    public void Update_Newtonian_ScalesWithGravitationalConstant()
    {
        using var world = new World();
        var (physics, gravity, bodyEntity) = AddSingleSource(
            world,
            GravitySourceComponent.CreateNewtonian(100f),
            Vector3.Zero,
            new Vector3(10f, 0f, 0f),
            mass: 1f);
        gravity.GravitationalConstant = 2.5f;

        physics.Update(1f / 60f);
        gravity.Update(1f / 60f);

        Assert.Equal(-2.5f, ForceOn(physics, bodyEntity).X, Epsilon);
    }

    [Fact]
    public void Update_Newtonian_ZeroConstant_AppliesNoForce()
    {
        using var world = new World();
        var (physics, gravity, bodyEntity) = AddSingleSource(
            world,
            GravitySourceComponent.CreateNewtonian(100f),
            Vector3.Zero,
            new Vector3(10f, 0f, 0f),
            mass: 1f);
        gravity.GravitationalConstant = 0f;

        physics.Update(1f / 60f);
        gravity.Update(1f / 60f);

        Assert.Equal(Vector3.Zero, ForceOn(physics, bodyEntity));
    }

    [Fact]
    public void Update_Newtonian_BeyondMaxRange_IsZero_InsideRange_IsNot()
    {
        using var world = new World();
        var (physics, gravity, inside) = AddSingleSource(
            world,
            GravitySourceComponent.CreateNewtonian(50f, maxRange: 10f),
            Vector3.Zero,
            new Vector3(10f, 0f, 0f),
            mass: 1f);
        var outside = AddDynamicBody(world, new Vector3(10.1f, 0f, 0f), 1f);

        physics.Update(1f / 60f);
        gravity.Update(1f / 60f);

        Assert.True(ForceOn(physics, inside).Length() > 0.1f);
        Assert.Equal(Vector3.Zero, ForceOn(physics, outside));
    }

    [Fact]
    public void Update_Newtonian_InsideMinDistance_ScalesByActualOffsetOverMinDistance()
    {
        using var world = new World();
        var (physics, gravity, bodyEntity) = AddSingleSource(
            world,
            GravitySourceComponent.CreateNewtonian(8f),
            Vector3.Zero,
            new Vector3(0.02f, 0f, 0f),
            mass: 1f);
        gravity.MinDistance = 0.1f;

        physics.Update(1f / 60f);
        gravity.Update(1f / 60f);

        // dist is clamped to 0.1 but the unnormalized offset remains:
        // a = (0.02/0.1) * (8 / 0.1^2) = 160 toward -X
        Assert.Equal(-160f, ForceOn(physics, bodyEntity).X, 0.05f);
    }

    [Fact]
    public void Update_Newtonian_AtSamePosition_AppliesZeroForce()
    {
        using var world = new World();
        var position = new Vector3(3f, 4f, 5f);
        var (physics, gravity, bodyEntity) = AddSingleSource(
            world,
            GravitySourceComponent.CreateNewtonian(80f),
            position,
            position,
            mass: 1f);

        physics.Update(1f / 60f);
        gravity.Update(1f / 60f);

        Assert.Equal(Vector3.Zero, ForceOn(physics, bodyEntity));
    }

    [Fact]
    public void Update_TwoSources_SumAccelerations()
    {
        using var world = new World();
        var physics = world.AddSystem<PhysicsSystem>();
        physics.Settings.Gravity = Vector3.Zero;
        var gravity = world.AddSystem<GravitySystem>();

        AddSource(world, GravitySourceComponent.CreateNewtonian(100f), new Vector3(10f, 0f, 0f));
        AddSource(world, GravitySourceComponent.CreateNewtonian(50f), new Vector3(-10f, 0f, 0f));
        var bodyEntity = AddDynamicBody(world, Vector3.Zero, 1f);

        physics.Update(1f / 60f);
        gravity.Update(1f / 60f);

        // +X source pulls +X with a=1, -X source pulls -X with a=0.5, net +0.5
        var force = ForceOn(physics, bodyEntity);
        Assert.Equal(0.5f, force.X, Epsilon);
        Assert.Equal(0f, force.Y, Epsilon);
        Assert.Equal(0f, force.Z, Epsilon);
    }

    [Fact]
    public void Update_ClampsForceToMaxForce_AndKeepsDirection()
    {
        using var world = new World();
        var (physics, gravity, bodyEntity) = AddSingleSource(
            world,
            GravitySourceComponent.CreateNewtonian(1000f),
            Vector3.Zero,
            new Vector3(1f, 0f, 0f),
            mass: 2f);
        gravity.MaxForce = 5f;

        physics.Update(1f / 60f);
        gravity.Update(1f / 60f);

        var force = ForceOn(physics, bodyEntity);
        Assert.Equal(5f, force.Length(), 0.01f);
        Assert.Equal(-5f, force.X, 0.01f);
        Assert.Equal(0f, force.Y, 0.01f);
        Assert.Equal(0f, force.Z, 0.01f);
    }

    [Fact]
    public void Update_Schwarzschild_FarFromHorizon_UsesSquareRootEnhancement()
    {
        using var world = new World();
        var (physics, gravity, bodyEntity) = AddSingleSource(
            world,
            GravitySourceComponent.CreateBlackHole(10f),
            Vector3.Zero,
            new Vector3(40f, 0f, 0f),
            mass: 2f);

        physics.Update(1f / 60f);
        gravity.Update(1f / 60f);

        // r=40, rs=20, r > 1.5 rs. factor = 1/(1-0.5)=2, mild = sqrt(2)
        // newton = 10/1600 = 0.00625; a = 0.00625*sqrt(2); F = 2a
        float expected = -2f * 0.00625f * MathF.Sqrt(2f);
        Assert.Equal(expected, ForceOn(physics, bodyEntity).X, 0.0001f);
    }

    [Fact]
    public void Update_Schwarzschild_JustAboveHorizon_UsesFullEnhancement()
    {
        using var world = new World();
        var (physics, gravity, bodyEntity) = AddSingleSource(
            world,
            GravitySourceComponent.CreateBlackHole(10f),
            Vector3.Zero,
            new Vector3(22f, 0f, 0f),
            mass: 1f);

        physics.Update(1f / 60f);
        gravity.Update(1f / 60f);

        // r=22, rs=20, rs < r <= 1.5 rs. factor = 1/(1-20/22) = 11
        // newton = 10/484, a = newton*11
        float expected = -(10f / 484f) * 11f;
        Assert.Equal(expected, ForceOn(physics, bodyEntity).X, 0.0005f);
    }

    [Fact]
    public void Update_Schwarzschild_AtOneAndAHalfSchwarzschildRadii_UsesStrongBranch()
    {
        using var world = new World();
        var (physics, gravity, bodyEntity) = AddSingleSource(
            world,
            GravitySourceComponent.CreateBlackHole(10f),
            Vector3.Zero,
            new Vector3(30f, 0f, 0f),
            mass: 1f);

        physics.Update(1f / 60f);
        gravity.Update(1f / 60f);

        // r == 1.5*rs is not the mild branch. factor = 1/(1-20/30) = 3
        float strong = -(10f / 900f) * 3f;
        float mild = -(10f / 900f) * MathF.Sqrt(3f);
        float actual = ForceOn(physics, bodyEntity).X;
        Assert.Equal(strong, actual, 0.0001f);
        Assert.True(MathF.Abs(actual - mild) > 0.005f);
    }

    [Fact]
    public void Update_Schwarzschild_ScalesWithGravitationalConstant()
    {
        using var world = new World();
        var (physics, gravity, bodyEntity) = AddSingleSource(
            world,
            GravitySourceComponent.CreateBlackHole(10f),
            Vector3.Zero,
            new Vector3(40f, 0f, 0f),
            mass: 1f);
        gravity.GravitationalConstant = 3f;

        physics.Update(1f / 60f);
        gravity.Update(1f / 60f);

        float expected = -3f * (10f / 1600f) * MathF.Sqrt(2f);
        Assert.Equal(expected, ForceOn(physics, bodyEntity).X, 0.0001f);
    }

    [Fact]
    public void Update_InsideLargeHorizonButOutsideMinDistance_UsesNewtonianMagnitude()
    {
        using var world = new World();
        var (physics, gravity, bodyEntity) = AddSingleSource(
            world,
            GravitySourceComponent.CreateBlackHole(10f),
            Vector3.Zero,
            new Vector3(10f, 0f, 0f),
            mass: 1f);

        physics.Update(1f / 60f);
        gravity.Update(1f / 60f);

        // rs = 20, so r=10 is inside the horizon, but the strong-force branch
        // only runs when the distance is under MinDistance. Current result is GM/r^2.
        Assert.Equal(-0.1f, ForceOn(physics, bodyEntity).X, Epsilon);
    }

    [Fact(Skip = "BUG: inside the event horizon but outside MinDistance uses Newtonian GM/r^2. At r=10, M=10 (rs=20) the acceleration is 0.1; expected the strong inward acceleration MaxForce/Mass = 1000.")]
    public void Update_InsideEventHorizon_ShouldApplyStrongInwardAcceleration()
    {
        using var world = new World();
        var (physics, gravity, bodyEntity) = AddSingleSource(
            world,
            GravitySourceComponent.CreateBlackHole(10f),
            Vector3.Zero,
            new Vector3(10f, 0f, 0f),
            mass: 1f);

        physics.Update(1f / 60f);
        gravity.Update(1f / 60f);

        Assert.Equal(-gravity.MaxForce / 10f, ForceOn(physics, bodyEntity).X, 1f);
    }

    [Fact]
    public void Update_WithinMinDistanceOfBlackHole_AppliesStrongInwardForce()
    {
        using var world = new World();
        var (physics, gravity, bodyEntity) = AddSingleSource(
            world,
            GravitySourceComponent.CreateBlackHole(10f),
            Vector3.Zero,
            new Vector3(0.05f, 0f, 0f),
            mass: 1f);

        physics.Update(1f / 60f);
        gravity.Update(1f / 60f);

        // a = direction * MaxForce / M = -10000/10
        var force = ForceOn(physics, bodyEntity);
        Assert.Equal(-1000f, force.X, 0.1f);
        Assert.Equal(0f, force.Y, 0.1f);
        Assert.Equal(0f, force.Z, 0.1f);
    }

    [Fact]
    public void Update_WithinMinDistanceOfKerrHole_IsRadialAndHasNoFrameDrag()
    {
        using var world = new World();
        var (physics, gravity, bodyEntity) = AddSingleSource(
            world,
            GravitySourceComponent.CreateRotatingBlackHole(10f, 0.5f, Vector3.UnitY),
            Vector3.Zero,
            new Vector3(0.05f, 0f, 0f),
            mass: 1f);

        physics.Update(1f / 60f);
        gravity.Update(1f / 60f);

        var force = ForceOn(physics, bodyEntity);
        Assert.Equal(-1000f, force.X, 0.1f);
        Assert.Equal(0f, force.Z, 0.1f);
    }

    [Fact]
    public void Update_CoincidentWithSchwarzschildSource_ProducesNaNForce()
    {
        using var world = new World();
        var position = new Vector3(1f, 2f, 3f);
        var (physics, gravity, bodyEntity) = AddSingleSource(
            world,
            GravitySourceComponent.CreateBlackHole(10f),
            position,
            position,
            mass: 1f);

        physics.Update(1f / 60f);
        gravity.Update(1f / 60f);

        Assert.True(float.IsNaN(ForceOn(physics, bodyEntity).X));
    }

    [Fact(Skip = "BUG: a dynamic body coincident with a Schwarzschild source gets a NaN force from Vector3.Normalize(Vector3.Zero). Expected a finite force whose magnitude is at most MaxForce.")]
    public void Update_CoincidentWithBlackHole_ShouldProduceFiniteClampedForce()
    {
        using var world = new World();
        var position = new Vector3(1f, 2f, 3f);
        var (physics, gravity, bodyEntity) = AddSingleSource(
            world,
            GravitySourceComponent.CreateBlackHole(10f),
            position,
            position,
            mass: 1f);

        physics.Update(1f / 60f);
        gravity.Update(1f / 60f);

        var force = ForceOn(physics, bodyEntity);
        Assert.False(float.IsNaN(force.X) || float.IsNaN(force.Y) || float.IsNaN(force.Z));
        Assert.InRange(force.Length(), 0f, gravity.MaxForce + 0.01f);
    }

    [Fact]
    public void Update_KerrOffAxis_AddsTangentialFrameDrag()
    {
        using var world = new World();
        var (physics, gravity, bodyEntity) = AddSingleSource(
            world,
            GravitySourceComponent.CreateRotatingBlackHole(10f, 0.5f, Vector3.UnitY),
            Vector3.Zero,
            new Vector3(10f, 0f, 0f),
            mass: 1f);

        physics.Update(1f / 60f);
        gravity.Update(1f / 60f);

        // radial a = M/r^2 = 0.1 toward -X
        // a_spin = 0.5*M = 5, frame drag = 2*a*M/r^3 = 0.1 along +Z
        var force = ForceOn(physics, bodyEntity);
        Assert.Equal(-0.1f, force.X, Epsilon);
        Assert.Equal(0f, force.Y, Epsilon);
        Assert.Equal(0.1f, force.Z, Epsilon);
    }

    [Fact]
    public void Update_KerrOnSpinAxis_HasNoTangentialComponent()
    {
        using var world = new World();
        var (physics, gravity, bodyEntity) = AddSingleSource(
            world,
            GravitySourceComponent.CreateRotatingBlackHole(10f, 0.5f, Vector3.UnitY),
            Vector3.Zero,
            new Vector3(0f, 10f, 0f),
            mass: 1f);

        physics.Update(1f / 60f);
        gravity.Update(1f / 60f);

        var force = ForceOn(physics, bodyEntity);
        Assert.Equal(0f, force.X, Epsilon);
        Assert.Equal(-0.1f, force.Y, Epsilon);
        Assert.Equal(0f, force.Z, Epsilon);
    }

    [Fact]
    public void Update_KerrWithZeroSpin_MatchesNewtonianRadialForce()
    {
        using var world = new World();
        var physics = world.AddSystem<PhysicsSystem>();
        physics.Settings.Gravity = Vector3.Zero;
        var gravity = world.AddSystem<GravitySystem>();

        var kerr = GravitySourceComponent.CreateRotatingBlackHole(10f, 0.5f, Vector3.UnitY);
        kerr.SpinParameter = 0f;
        AddSource(world, kerr, Vector3.Zero);
        var bodyEntity = AddDynamicBody(world, new Vector3(10f, 0f, 0f), 1f);

        physics.Update(1f / 60f);
        gravity.Update(1f / 60f);

        var force = ForceOn(physics, bodyEntity);
        Assert.Equal(-0.1f, force.X, Epsilon);
        Assert.Equal(0f, force.Z, Epsilon);
    }

    [Fact]
    public void Update_KerrFrameDrag_DoesNotScaleWithGravitationalConstant()
    {
        using var world = new World();
        var (physics, gravity, bodyEntity) = AddSingleSource(
            world,
            GravitySourceComponent.CreateRotatingBlackHole(10f, 0.5f, Vector3.UnitY),
            Vector3.Zero,
            new Vector3(10f, 0f, 0f),
            mass: 1f);
        gravity.GravitationalConstant = 2f;

        physics.Update(1f / 60f);
        gravity.Update(1f / 60f);

        var force = ForceOn(physics, bodyEntity);
        Assert.Equal(-0.2f, force.X, Epsilon);
        Assert.Equal(0.1f, force.Z, Epsilon);
    }

    [Fact(Skip = "BUG: Kerr frame-drag acceleration ignores GravitationalConstant. With G=2, M=10, r=10, spin=0.5, expected tangential acceleration 0.2; actual tangential component stays 0.1 while the radial term doubles.")]
    public void Update_KerrFrameDrag_ShouldScaleWithGravitationalConstant()
    {
        using var world = new World();
        var (physics, gravity, bodyEntity) = AddSingleSource(
            world,
            GravitySourceComponent.CreateRotatingBlackHole(10f, 0.5f, Vector3.UnitY),
            Vector3.Zero,
            new Vector3(10f, 0f, 0f),
            mass: 1f);
        gravity.GravitationalConstant = 2f;

        physics.Update(1f / 60f);
        gravity.Update(1f / 60f);

        Assert.Equal(0.2f, ForceOn(physics, bodyEntity).Z, Epsilon);
    }

    [Fact]
    public void Update_UseDefaultGravityTrue_DoesNotChangeSourceForce()
    {
        using var world = new World();
        var (physics, gravity, bodyEntity) = AddSingleSource(
            world,
            GravitySourceComponent.CreateUniform(-Vector3.UnitY, 10f),
            Vector3.Zero,
            new Vector3(0f, 3f, 0f),
            mass: 1f);
        gravity.UseDefaultGravity = true;

        physics.Update(1f / 60f);
        gravity.Update(1f / 60f);

        Assert.Equal(-10f, ForceOn(physics, bodyEntity).Y, Epsilon);
    }

    [Fact(Skip = "BUG: GravitySystem.UseDefaultGravity is never read. With the flag true, expected the applied force to also include PhysicsSettings default gravity (0, -9.81, 0); actual force is only the source term (0, -10, 0).")]
    public void Update_UseDefaultGravityTrue_ShouldAddDefaultGravity()
    {
        using var world = new World();
        var (physics, gravity, bodyEntity) = AddSingleSource(
            world,
            GravitySourceComponent.CreateUniform(-Vector3.UnitY, 10f),
            Vector3.Zero,
            new Vector3(0f, 3f, 0f),
            mass: 1f);
        gravity.UseDefaultGravity = true;

        physics.Update(1f / 60f);
        gravity.Update(1f / 60f);

        var defaults = new PhysicsSettings();
        Assert.Equal(-10f + defaults.Gravity.Y, ForceOn(physics, bodyEntity).Y, Epsilon);
    }

    [Fact]
    public void Update_WakesSleepingDynamicBody()
    {
        using var world = new World();
        var (physics, gravity, bodyEntity) = AddSingleSource(
            world,
            GravitySourceComponent.CreateUniform(-Vector3.UnitY, 8f),
            Vector3.Zero,
            new Vector3(0f, 2f, 0f),
            mass: 1f);

        physics.Update(1f / 60f);
        var body = physics.PhysicsWorld.GetBody(bodyEntity.Id)!;
        body.IsAwake = false;

        gravity.Update(1f / 60f);

        Assert.True(body.IsAwake);
        Assert.Equal(-8f, body.Force.Y, Epsilon);
    }

    [Fact]
    public void Update_BeforeBodyIsRegistered_DropsTheForce()
    {
        using var world = new World();
        var (physics, gravity, bodyEntity) = AddSingleSource(
            world,
            GravitySourceComponent.CreateNewtonian(1000f),
            Vector3.Zero,
            new Vector3(10f, 0f, 0f),
            mass: 1f);

        gravity.Update(0.1f);
        physics.Update(0.1f);

        var body = physics.PhysicsWorld.GetBody(bodyEntity.Id)!;
        Assert.Equal(10f, body.Position.X, 0.01f);

        gravity.Update(0.1f);
        physics.Update(0.1f);

        Assert.True(body.Position.X < 9.95f);
    }

    [Fact]
    public void WorldUpdate_PullsDynamicBodyTowardNewtonianSource()
    {
        using var world = new World();
        var physics = world.AddSystem<PhysicsSystem>();
        physics.Settings.Gravity = Vector3.Zero;
        world.AddSystem<GravitySystem>();

        AddSource(world, GravitySourceComponent.CreateNewtonian(1000f), Vector3.Zero);
        var bodyEntity = AddDynamicBody(world, new Vector3(10f, 0f, 0f), 1f);

        for (int i = 0; i < 30; i++)
            world.Update(1f / 60f);

        var body = physics.PhysicsWorld.GetBody(bodyEntity.Id)!;
        Assert.True(body.Position.X < 9f);
        Assert.Equal(0f, body.Position.Y, 0.05f);
        Assert.True(body.LinearVelocity.X < 0f);
    }

    [Fact]
    public void CalculateOrbitalVelocity_ParallelNormal_PicksFallbackTangent()
    {
        var source = GravitySourceComponent.CreateNewtonian(100f);
        var velocity = GravitySystem.CalculateOrbitalVelocity(
            Vector3.Zero,
            source,
            new Vector3(10f, 0f, 0f),
            Vector3.UnitX,
            1f);

        Assert.Equal(MathF.Sqrt(10f), velocity.Length(), 0.01f);
        Assert.Equal(0f, velocity.X, 0.01f);
        Assert.Equal(0f, velocity.Y, 0.01f);
        Assert.True(MathF.Abs(velocity.Z) > 1f);
    }

    [Fact]
    public void CalculateOrbitalVelocity_AtSource_ReturnsZero()
    {
        var source = GravitySourceComponent.CreateNewtonian(100f);
        var velocity = GravitySystem.CalculateOrbitalVelocity(
            Vector3.Zero, source, Vector3.Zero, Vector3.UnitY, 1f);

        Assert.Equal(Vector3.Zero, velocity);
    }

    [Fact]
    public void CalculateOrbitalVelocity_ScalesWithGravitationalConstant()
    {
        var source = GravitySourceComponent.CreateNewtonian(100f);
        var slow = GravitySystem.CalculateOrbitalVelocity(
            Vector3.Zero, source, new Vector3(10f, 0f, 0f), Vector3.UnitY, 1f).Length();
        var fast = GravitySystem.CalculateOrbitalVelocity(
            Vector3.Zero, source, new Vector3(10f, 0f, 0f), Vector3.UnitY, 4f).Length();

        Assert.Equal(slow * 2f, fast, 0.01f);
    }

    [Fact]
    public void CalculateEscapeVelocity_AtSource_ReturnsMaxValue()
    {
        var source = GravitySourceComponent.CreateNewtonian(100f);
        var escape = GravitySystem.CalculateEscapeVelocity(Vector3.One, source, Vector3.One, 1f);

        Assert.Equal(float.MaxValue, escape);
    }

    private static (PhysicsSystem physics, GravitySystem gravity, Entity body) AddSingleSource(
        World world,
        GravitySourceComponent source,
        Vector3 sourcePosition,
        Vector3 bodyPosition,
        float mass,
        RigidBodyType type = RigidBodyType.Dynamic,
        bool useGravity = true)
    {
        var physics = world.AddSystem<PhysicsSystem>();
        physics.Settings.Gravity = Vector3.Zero;
        var gravity = world.AddSystem<GravitySystem>();
        AddSource(world, source, sourcePosition);
        var body = AddBody(world, bodyPosition, mass, type, useGravity);
        return (physics, gravity, body);
    }

    private static void AddSource(World world, GravitySourceComponent source, Vector3 position)
    {
        var entity = world.CreateEntity();
        world.AddComponent(entity, new TransformComponent(position));
        world.AddComponent(entity, source);
    }

    private static Entity AddDynamicBody(World world, Vector3 position, float mass)
    {
        return AddBody(world, position, mass, RigidBodyType.Dynamic, true);
    }

    private static Entity AddBody(World world, Vector3 position, float mass, RigidBodyType type, bool useGravity)
    {
        var entity = world.CreateEntity();
        world.AddComponent(entity, new TransformComponent(position));
        RigidBodyComponent body = type switch
        {
            RigidBodyType.Static => RigidBodyComponent.CreateStatic(),
            RigidBodyType.Kinematic => RigidBodyComponent.CreateKinematic(),
            _ => RigidBodyComponent.CreateDynamic(mass)
        };
        body.UseGravity = useGravity;
        if (type == RigidBodyType.Dynamic)
            body.Mass = mass;
        world.AddComponent(entity, body);
        return entity;
    }

    private static Vector3 ForceOn(PhysicsSystem physics, Entity entity)
    {
        var body = physics.PhysicsWorld.GetBody(entity.Id);
        Assert.NotNull(body);
        return body!.Force;
    }
}
