using System.Numerics;
using RedHoleEngine.Components;
using RedHoleEngine.Physics;

namespace RedHoleEngine.Tests.Physics;

public class GravitySystemTests
{
    private const float Epsilon = 0.01f;

    #region GravitySourceComponent Tests

    [Fact]
    public void CreateBlackHole_HasCorrectProperties()
    {
        var source = GravitySourceComponent.CreateBlackHole(10f);
        
        Assert.Equal(10f, source.Mass);
        Assert.Equal(GravityType.Schwarzschild, source.GravityType);
        Assert.Equal(0f, source.SpinParameter);
        Assert.True(source.AffectsLight);
    }

    [Fact]
    public void CreateRotatingBlackHole_HasCorrectProperties()
    {
        var source = GravitySourceComponent.CreateRotatingBlackHole(10f, 0.5f, Vector3.UnitZ);
        
        Assert.Equal(10f, source.Mass);
        Assert.Equal(GravityType.Kerr, source.GravityType);
        Assert.Equal(0.5f, source.SpinParameter);
        Assert.Equal(Vector3.UnitZ, source.SpinAxis);
    }

    [Fact]
    public void CreateNewtonian_HasCorrectProperties()
    {
        var source = GravitySourceComponent.CreateNewtonian(100f, 50f);
        
        Assert.Equal(100f, source.Mass);
        Assert.Equal(GravityType.Newtonian, source.GravityType);
        Assert.Equal(50f, source.MaxRange);
        Assert.False(source.AffectsLight);
    }

    [Fact]
    public void CreateUniform_HasCorrectProperties()
    {
        var source = GravitySourceComponent.CreateUniform(-Vector3.UnitY, 9.81f);
        
        Assert.Equal(GravityType.Uniform, source.GravityType);
        Assert.Equal(-Vector3.UnitY, source.UniformDirection);
        Assert.Equal(9.81f, source.UniformStrength);
    }

    [Fact]
    public void SchwarzschildRadius_CalculatedCorrectly()
    {
        var source = GravitySourceComponent.CreateBlackHole(10f);
        
        // rs = 2M = 20
        Assert.Equal(20f, source.SchwarzschildRadius, Epsilon);
    }

    [Fact]
    public void PhotonSphereRadius_CalculatedCorrectly()
    {
        var source = GravitySourceComponent.CreateBlackHole(10f);
        
        // r_photon = 3M = 30
        Assert.Equal(30f, source.PhotonSphereRadius, Epsilon);
    }

    [Fact]
    public void ISCO_CalculatedCorrectly()
    {
        var source = GravitySourceComponent.CreateBlackHole(10f);
        
        // ISCO = 6M = 60
        Assert.Equal(60f, source.ISCO, Epsilon);
    }

    [Fact]
    public void EventHorizonRadius_Schwarzschild_EqualsSchwarzschildRadius()
    {
        var source = GravitySourceComponent.CreateBlackHole(10f);
        
        Assert.Equal(source.SchwarzschildRadius, source.EventHorizonRadius, Epsilon);
    }

    [Fact]
    public void EventHorizonRadius_Kerr_SmallerThanSchwarzschild()
    {
        var schwarzschild = GravitySourceComponent.CreateBlackHole(10f);
        var kerr = GravitySourceComponent.CreateRotatingBlackHole(10f, 0.5f, Vector3.UnitY);
        
        // Rotating black hole has smaller event horizon
        Assert.True(kerr.EventHorizonRadius < schwarzschild.EventHorizonRadius);
    }

    #endregion

    #region GetAccelerationAt Tests

    [Fact]
    public void GetAccelerationAt_Newtonian_PointsTowardSource()
    {
        var source = GravitySourceComponent.CreateNewtonian(100f);
        var sourcePos = Vector3.Zero;
        var targetPos = new Vector3(10f, 0f, 0f);
        
        var accel = source.GetAccelerationAt(sourcePos, targetPos);
        
        // Should point from target toward source (negative X)
        Assert.True(accel.X < 0);
        Assert.Equal(0f, accel.Y, Epsilon);
        Assert.Equal(0f, accel.Z, Epsilon);
    }

    [Fact]
    public void GetAccelerationAt_Newtonian_InverseSquare()
    {
        var source = GravitySourceComponent.CreateNewtonian(100f);
        var sourcePos = Vector3.Zero;
        
        var target1 = new Vector3(10f, 0f, 0f);
        var target2 = new Vector3(20f, 0f, 0f);
        
        var accel1 = source.GetAccelerationAt(sourcePos, target1).Length();
        var accel2 = source.GetAccelerationAt(sourcePos, target2).Length();
        
        // At 2x distance, should be 1/4 the acceleration
        Assert.Equal(accel1 / 4f, accel2, accel2 * 0.01f);
    }

    [Fact]
    public void GetAccelerationAt_Uniform_ConstantDirection()
    {
        var source = GravitySourceComponent.CreateUniform(-Vector3.UnitY, 10f);
        
        var accel1 = source.GetAccelerationAt(Vector3.Zero, new Vector3(0f, 100f, 0f));
        var accel2 = source.GetAccelerationAt(Vector3.Zero, new Vector3(50f, 0f, 50f));
        
        // Uniform gravity should be the same everywhere
        Assert.Equal(accel1, accel2);
        Assert.Equal(new Vector3(0f, -10f, 0f), accel1);
    }

    [Fact]
    public void GetAccelerationAt_BeyondMaxRange_ReturnsZero()
    {
        var source = GravitySourceComponent.CreateNewtonian(100f, 50f);
        var sourcePos = Vector3.Zero;
        var targetPos = new Vector3(100f, 0f, 0f); // Beyond 50 unit range
        
        var accel = source.GetAccelerationAt(sourcePos, targetPos);
        
        Assert.Equal(Vector3.Zero, accel);
    }

    [Fact]
    public void GetAccelerationAt_AtSamePosition_ReturnsZero()
    {
        var source = GravitySourceComponent.CreateNewtonian(100f);
        var pos = new Vector3(5f, 5f, 5f);
        
        var accel = source.GetAccelerationAt(pos, pos);
        
        Assert.Equal(Vector3.Zero, accel);
    }

    #endregion

    #region Orbital Velocity Tests

    [Fact]
    public void CalculateOrbitalVelocity_CircularOrbit()
    {
        var source = GravitySourceComponent.CreateNewtonian(1000f);
        var sourcePos = Vector3.Zero;
        var orbitPos = new Vector3(100f, 0f, 0f);
        var orbitNormal = Vector3.UnitY; // Orbit in XZ plane
        
        var velocity = GravitySystem.CalculateOrbitalVelocity(
            sourcePos, source, orbitPos, orbitNormal, 1f);
        
        // Expected: v = sqrt(GM/r) = sqrt(1000/100) = sqrt(10) ≈ 3.16
        float expectedSpeed = MathF.Sqrt(10f);
        Assert.Equal(expectedSpeed, velocity.Length(), expectedSpeed * 0.01f);
        
        // Velocity should be perpendicular to radius (in Z direction for this setup)
        Assert.Equal(0f, velocity.X, 0.1f);
        Assert.Equal(0f, velocity.Y, 0.1f);
    }

    [Fact]
    public void CalculateEscapeVelocity_CorrectFormula()
    {
        var source = GravitySourceComponent.CreateNewtonian(1000f);
        var sourcePos = Vector3.Zero;
        var position = new Vector3(100f, 0f, 0f);
        
        var escapeVel = GravitySystem.CalculateEscapeVelocity(
            sourcePos, source, position, 1f);
        
        // v_escape = sqrt(2GM/r) = sqrt(2000/100) = sqrt(20) ≈ 4.47
        float expectedSpeed = MathF.Sqrt(20f);
        Assert.Equal(expectedSpeed, escapeVel, expectedSpeed * 0.01f);
    }

    [Fact]
    public void EscapeVelocity_IsRoot2TimesOrbitalVelocity()
    {
        var source = GravitySourceComponent.CreateNewtonian(1000f);
        var sourcePos = Vector3.Zero;
        var position = new Vector3(100f, 0f, 0f);
        
        var orbitalVel = GravitySystem.CalculateOrbitalVelocity(
            sourcePos, source, position, Vector3.UnitY, 1f).Length();
        var escapeVel = GravitySystem.CalculateEscapeVelocity(
            sourcePos, source, position, 1f);
        
        // v_escape = sqrt(2) * v_orbital
        Assert.Equal(MathF.Sqrt(2f) * orbitalVel, escapeVel, escapeVel * 0.01f);
    }

    #endregion
}
