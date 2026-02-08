using System.Numerics;
using RedHoleEngine.Physics;

namespace RedHoleEngine.Tests.Physics;

public class RigidBodyTests
{
    private const float Epsilon = 0.0001f;

    #region Mass and Inertia Tests

    [Fact]
    public void Mass_SetPositiveValue_CalculatesInverseMass()
    {
        var body = new RigidBody { Mass = 2f };
        
        Assert.Equal(2f, body.Mass);
        Assert.Equal(0.5f, body.InverseMass);
    }

    [Fact]
    public void Mass_SetZero_InverseMassIsZero()
    {
        var body = new RigidBody { Mass = 0f };
        
        Assert.Equal(0f, body.InverseMass);
    }

    [Fact]
    public void InverseMass_StaticBody_ReturnsZero()
    {
        var body = new RigidBody
        {
            Type = RigidBodyType.Static,
            Mass = 1f
        };
        
        Assert.Equal(0f, body.InverseMass);
    }

    [Fact]
    public void InverseMass_KinematicBody_ReturnsZero()
    {
        var body = new RigidBody
        {
            Type = RigidBodyType.Kinematic,
            Mass = 1f
        };
        
        Assert.Equal(0f, body.InverseMass);
    }

    [Fact]
    public void CalculateSphereInertia_CorrectFormula()
    {
        var body = new RigidBody { Mass = 10f };
        body.CalculateSphereInertia(2f);
        
        // I = 0.4 * m * r^2 = 0.4 * 10 * 4 = 16
        float expected = 16f;
        Assert.Equal(expected, body.Inertia.X, Epsilon);
        Assert.Equal(expected, body.Inertia.Y, Epsilon);
        Assert.Equal(expected, body.Inertia.Z, Epsilon);
    }

    [Fact]
    public void CalculateBoxInertia_CorrectFormula()
    {
        var body = new RigidBody { Mass = 12f };
        body.CalculateBoxInertia(new Vector3(1f, 1f, 1f)); // 2x2x2 box
        
        // I = m/12 * (h^2 + d^2) for each axis
        // For 2x2x2 box: I = 12/12 * (4 + 4) = 8
        float expected = 8f;
        Assert.Equal(expected, body.Inertia.X, Epsilon);
        Assert.Equal(expected, body.Inertia.Y, Epsilon);
        Assert.Equal(expected, body.Inertia.Z, Epsilon);
    }

    #endregion

    #region Force and Impulse Tests

    [Fact]
    public void ApplyForce_DynamicBody_AccumulatesForce()
    {
        var body = new RigidBody { Type = RigidBodyType.Dynamic };
        
        body.ApplyForce(new Vector3(10f, 0f, 0f));
        body.ApplyForce(new Vector3(5f, 5f, 0f));
        
        Assert.Equal(15f, body.Force.X);
        Assert.Equal(5f, body.Force.Y);
        Assert.Equal(0f, body.Force.Z);
    }

    [Fact]
    public void ApplyForce_StaticBody_DoesNothing()
    {
        var body = new RigidBody { Type = RigidBodyType.Static };
        
        body.ApplyForce(new Vector3(10f, 0f, 0f));
        
        Assert.Equal(Vector3.Zero, body.Force);
    }

    [Fact]
    public void ApplyImpulse_ChangesVelocityImmediately()
    {
        var body = new RigidBody
        {
            Type = RigidBodyType.Dynamic,
            Mass = 2f
        };
        
        body.ApplyImpulse(new Vector3(10f, 0f, 0f));
        
        // v = impulse * inverseMass = 10 * 0.5 = 5
        Assert.Equal(5f, body.LinearVelocity.X);
    }

    [Fact]
    public void ApplyForceAtPosition_GeneratesTorque()
    {
        var body = new RigidBody
        {
            Type = RigidBodyType.Dynamic,
            Position = Vector3.Zero
        };
        
        // Apply force at offset position
        body.ApplyForceAtPosition(new Vector3(0f, 10f, 0f), new Vector3(1f, 0f, 0f));
        
        // Torque = r x F = (1,0,0) x (0,10,0) = (0,0,10)
        Assert.Equal(0f, body.Torque.X, Epsilon);
        Assert.Equal(0f, body.Torque.Y, Epsilon);
        Assert.Equal(10f, body.Torque.Z, Epsilon);
    }

    [Fact]
    public void ApplyTorque_AccumulatesTorque()
    {
        var body = new RigidBody { Type = RigidBodyType.Dynamic };
        
        body.ApplyTorque(new Vector3(1f, 2f, 3f));
        body.ApplyTorque(new Vector3(1f, 2f, 3f));
        
        Assert.Equal(new Vector3(2f, 4f, 6f), body.Torque);
    }

    [Fact]
    public void ApplyAngularImpulse_ChangesAngularVelocity()
    {
        var body = new RigidBody
        {
            Type = RigidBodyType.Dynamic,
            Inertia = new Vector3(2f, 2f, 2f)
        };
        
        body.ApplyAngularImpulse(new Vector3(4f, 0f, 0f));
        
        // omega = angularImpulse * inverseInertia = 4 * 0.5 = 2
        Assert.Equal(2f, body.AngularVelocity.X);
    }

    [Fact]
    public void ClearForces_ResetsForceAndTorque()
    {
        var body = new RigidBody { Type = RigidBodyType.Dynamic };
        body.ApplyForce(new Vector3(10f, 10f, 10f));
        body.ApplyTorque(new Vector3(5f, 5f, 5f));
        
        body.ClearForces();
        
        Assert.Equal(Vector3.Zero, body.Force);
        Assert.Equal(Vector3.Zero, body.Torque);
    }

    #endregion

    #region Sleep Tests

    [Fact]
    public void Wake_SetsIsAwakeTrue()
    {
        var body = new RigidBody { IsAwake = false };
        
        body.Wake();
        
        Assert.True(body.IsAwake);
    }

    [Fact]
    public void ApplyForce_WakesBody()
    {
        var body = new RigidBody
        {
            Type = RigidBodyType.Dynamic,
            IsAwake = false
        };
        
        body.ApplyForce(new Vector3(1f, 0f, 0f));
        
        Assert.True(body.IsAwake);
    }

    [Fact]
    public void UpdateSleep_LowMotion_EventuallySleeps()
    {
        var body = new RigidBody
        {
            Type = RigidBodyType.Dynamic,
            LinearVelocity = Vector3.Zero,
            AngularVelocity = Vector3.Zero,
            SleepThreshold = 0.05f,
            IsAwake = true
        };
        
        // Simulate multiple frames with no motion
        for (int i = 0; i < 60; i++) // 1 second at 60fps
        {
            body.UpdateSleep(1f / 60f);
        }
        
        Assert.False(body.IsAwake);
    }

    [Fact]
    public void UpdateSleep_HighMotion_StaysAwake()
    {
        var body = new RigidBody
        {
            Type = RigidBodyType.Dynamic,
            LinearVelocity = new Vector3(10f, 0f, 0f),
            IsAwake = true
        };
        
        body.UpdateSleep(1f);
        
        Assert.True(body.IsAwake);
    }

    [Fact]
    public void UpdateSleep_StaticBody_AlwaysSleeps()
    {
        var body = new RigidBody
        {
            Type = RigidBodyType.Static,
            IsAwake = true
        };
        
        body.UpdateSleep(0.016f);
        
        Assert.False(body.IsAwake);
    }

    #endregion

    #region Constraint Tests

    [Fact]
    public void ApplyConstraints_FreezePositionX_ZerosXVelocity()
    {
        var body = new RigidBody
        {
            LinearVelocity = new Vector3(5f, 10f, 15f),
            FreezePositionX = true
        };
        
        body.ApplyConstraints();
        
        Assert.Equal(0f, body.LinearVelocity.X);
        Assert.Equal(10f, body.LinearVelocity.Y);
        Assert.Equal(15f, body.LinearVelocity.Z);
    }

    [Fact]
    public void ApplyConstraints_FreezeRotationY_ZerosYAngularVelocity()
    {
        var body = new RigidBody
        {
            AngularVelocity = new Vector3(1f, 2f, 3f),
            FreezeRotationY = true
        };
        
        body.ApplyConstraints();
        
        Assert.Equal(1f, body.AngularVelocity.X);
        Assert.Equal(0f, body.AngularVelocity.Y);
        Assert.Equal(3f, body.AngularVelocity.Z);
    }

    #endregion

    #region Transform Tests

    [Fact]
    public void LocalToWorld_NoRotation_AddsPosition()
    {
        var body = new RigidBody
        {
            Position = new Vector3(10f, 20f, 30f),
            Rotation = Quaternion.Identity
        };
        
        var result = body.LocalToWorld(new Vector3(1f, 2f, 3f));
        
        Assert.Equal(11f, result.X, Epsilon);
        Assert.Equal(22f, result.Y, Epsilon);
        Assert.Equal(33f, result.Z, Epsilon);
    }

    [Fact]
    public void WorldToLocal_NoRotation_SubtractsPosition()
    {
        var body = new RigidBody
        {
            Position = new Vector3(10f, 20f, 30f),
            Rotation = Quaternion.Identity
        };
        
        var result = body.WorldToLocal(new Vector3(11f, 22f, 33f));
        
        Assert.Equal(1f, result.X, Epsilon);
        Assert.Equal(2f, result.Y, Epsilon);
        Assert.Equal(3f, result.Z, Epsilon);
    }

    [Fact]
    public void GetVelocityAtPoint_IncludesAngularContribution()
    {
        var body = new RigidBody
        {
            Position = Vector3.Zero,
            LinearVelocity = new Vector3(1f, 0f, 0f),
            AngularVelocity = new Vector3(0f, 1f, 0f) // Rotating around Y
        };
        
        // Point at (1, 0, 0) with Y-axis rotation should have Z velocity
        var velocity = body.GetVelocityAtPoint(new Vector3(1f, 0f, 0f));
        
        Assert.Equal(1f, velocity.X, Epsilon); // Linear contribution
        Assert.Equal(0f, velocity.Y, Epsilon);
        Assert.Equal(-1f, velocity.Z, Epsilon); // Angular contribution: omega x r
    }

    #endregion
}
