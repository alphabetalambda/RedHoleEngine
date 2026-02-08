using System.Numerics;

namespace RedHoleEngine.Physics;

/// <summary>
/// Types of rigid bodies
/// </summary>
public enum RigidBodyType
{
    /// <summary>Affected by forces and collisions</summary>
    Dynamic,
    
    /// <summary>Moved by user code, affects dynamic bodies but not affected by them</summary>
    Kinematic,
    
    /// <summary>Never moves, infinite mass</summary>
    Static
}

/// <summary>
/// Represents a rigid body in the physics simulation.
/// Contains all the physical properties and state needed for simulation.
/// </summary>
public class RigidBody
{
    // Identity
    public int EntityId { get; set; } = -1;
    
    // Type
    public RigidBodyType Type { get; set; } = RigidBodyType.Dynamic;
    
    // Transform state
    public Vector3 Position { get; set; }
    public Quaternion Rotation { get; set; } = Quaternion.Identity;
    
    // Linear motion
    public Vector3 LinearVelocity { get; set; }
    public Vector3 Force { get; set; }
    
    // Angular motion
    public Vector3 AngularVelocity { get; set; }
    public Vector3 Torque { get; set; }
    
    // Mass properties
    private float _mass = 1f;
    private float _inverseMass = 1f;
    
    public float Mass
    {
        get => _mass;
        set
        {
            _mass = value;
            _inverseMass = value > 0 ? 1f / value : 0f;
        }
    }
    
    public float InverseMass => Type == RigidBodyType.Dynamic ? _inverseMass : 0f;
    
    // Inertia tensor (simplified as diagonal for now)
    private Vector3 _inertia = Vector3.One;
    private Vector3 _inverseInertia = Vector3.One;
    
    public Vector3 Inertia
    {
        get => _inertia;
        set
        {
            _inertia = value;
            _inverseInertia = new Vector3(
                value.X > 0 ? 1f / value.X : 0f,
                value.Y > 0 ? 1f / value.Y : 0f,
                value.Z > 0 ? 1f / value.Z : 0f
            );
        }
    }
    
    public Vector3 InverseInertia => Type == RigidBodyType.Dynamic ? _inverseInertia : Vector3.Zero;
    
    // Material properties
    public float Restitution { get; set; } = 0.3f;  // Bounciness (0-1)
    public float Friction { get; set; } = 0.5f;     // Friction coefficient
    public float LinearDamping { get; set; } = 0.01f;
    public float AngularDamping { get; set; } = 0.05f;
    
    // Simulation state
    public bool IsAwake { get; set; } = true;
    public bool UseGravity { get; set; } = true;
    public float SleepThreshold { get; set; } = 0.05f;
    private float _sleepTimer;
    
    // Constraints
    public bool FreezePositionX { get; set; }
    public bool FreezePositionY { get; set; }
    public bool FreezePositionZ { get; set; }
    public bool FreezeRotationX { get; set; }
    public bool FreezeRotationY { get; set; }
    public bool FreezeRotationZ { get; set; }
    
    // Collision filtering
    public uint CollisionLayer { get; set; } = 1;
    public uint CollisionMask { get; set; } = uint.MaxValue;

    /// <summary>
    /// Calculate inertia tensor for a box shape
    /// </summary>
    public void CalculateBoxInertia(Vector3 halfExtents)
    {
        float x2 = halfExtents.X * halfExtents.X * 4f;
        float y2 = halfExtents.Y * halfExtents.Y * 4f;
        float z2 = halfExtents.Z * halfExtents.Z * 4f;
        float factor = _mass / 12f;
        
        Inertia = new Vector3(
            factor * (y2 + z2),
            factor * (x2 + z2),
            factor * (x2 + y2)
        );
    }

    /// <summary>
    /// Calculate inertia tensor for a sphere shape
    /// </summary>
    public void CalculateSphereInertia(float radius)
    {
        float i = 0.4f * _mass * radius * radius;
        Inertia = new Vector3(i, i, i);
    }

    /// <summary>
    /// Apply a force at the center of mass
    /// </summary>
    public void ApplyForce(Vector3 force)
    {
        if (Type != RigidBodyType.Dynamic) return;
        Force += force;
        Wake();
    }

    /// <summary>
    /// Apply a force at a world position (generates torque)
    /// </summary>
    public void ApplyForceAtPosition(Vector3 force, Vector3 worldPosition)
    {
        if (Type != RigidBodyType.Dynamic) return;
        Force += force;
        
        var r = worldPosition - Position;
        Torque += Vector3.Cross(r, force);
        Wake();
    }

    /// <summary>
    /// Apply an impulse at the center of mass
    /// </summary>
    public void ApplyImpulse(Vector3 impulse)
    {
        if (Type != RigidBodyType.Dynamic) return;
        LinearVelocity += impulse * InverseMass;
        Wake();
    }

    /// <summary>
    /// Apply an impulse at a world position (generates angular velocity change)
    /// </summary>
    public void ApplyImpulseAtPosition(Vector3 impulse, Vector3 worldPosition)
    {
        if (Type != RigidBodyType.Dynamic) return;
        LinearVelocity += impulse * InverseMass;
        
        var r = worldPosition - Position;
        var angularImpulse = Vector3.Cross(r, impulse);
        AngularVelocity += angularImpulse * InverseInertia;
        Wake();
    }

    /// <summary>
    /// Apply torque
    /// </summary>
    public void ApplyTorque(Vector3 torque)
    {
        if (Type != RigidBodyType.Dynamic) return;
        Torque += torque;
        Wake();
    }

    /// <summary>
    /// Apply an angular impulse (instant torque)
    /// </summary>
    public void ApplyAngularImpulse(Vector3 angularImpulse)
    {
        if (Type != RigidBodyType.Dynamic) return;
        AngularVelocity += angularImpulse * InverseInertia;
        Wake();
    }

    /// <summary>
    /// Wake the body from sleep
    /// </summary>
    public void Wake()
    {
        IsAwake = true;
        _sleepTimer = 0f;
    }

    /// <summary>
    /// Update sleep state based on motion
    /// </summary>
    public void UpdateSleep(float deltaTime)
    {
        if (Type != RigidBodyType.Dynamic)
        {
            IsAwake = false;
            return;
        }

        float motion = LinearVelocity.LengthSquared() + AngularVelocity.LengthSquared();
        
        if (motion < SleepThreshold * SleepThreshold)
        {
            _sleepTimer += deltaTime;
            if (_sleepTimer > 0.5f) // Sleep after 0.5 seconds of low motion
            {
                IsAwake = false;
                LinearVelocity = Vector3.Zero;
                AngularVelocity = Vector3.Zero;
            }
        }
        else
        {
            _sleepTimer = 0f;
            IsAwake = true;
        }
    }

    /// <summary>
    /// Clear accumulated forces and torques
    /// </summary>
    public void ClearForces()
    {
        Force = Vector3.Zero;
        Torque = Vector3.Zero;
    }

    /// <summary>
    /// Apply constraint freezes to velocity
    /// </summary>
    public void ApplyConstraints()
    {
        if (FreezePositionX) LinearVelocity = new Vector3(0, LinearVelocity.Y, LinearVelocity.Z);
        if (FreezePositionY) LinearVelocity = new Vector3(LinearVelocity.X, 0, LinearVelocity.Z);
        if (FreezePositionZ) LinearVelocity = new Vector3(LinearVelocity.X, LinearVelocity.Y, 0);
        if (FreezeRotationX) AngularVelocity = new Vector3(0, AngularVelocity.Y, AngularVelocity.Z);
        if (FreezeRotationY) AngularVelocity = new Vector3(AngularVelocity.X, 0, AngularVelocity.Z);
        if (FreezeRotationZ) AngularVelocity = new Vector3(AngularVelocity.X, AngularVelocity.Y, 0);
    }

    /// <summary>
    /// Get velocity at a world point (includes angular contribution)
    /// </summary>
    public Vector3 GetVelocityAtPoint(Vector3 worldPoint)
    {
        var r = worldPoint - Position;
        return LinearVelocity + Vector3.Cross(AngularVelocity, r);
    }

    /// <summary>
    /// Transform a point from local to world space
    /// </summary>
    public Vector3 LocalToWorld(Vector3 localPoint)
    {
        return Position + Vector3.Transform(localPoint, Rotation);
    }

    /// <summary>
    /// Transform a point from world to local space
    /// </summary>
    public Vector3 WorldToLocal(Vector3 worldPoint)
    {
        return Vector3.Transform(worldPoint - Position, Quaternion.Conjugate(Rotation));
    }

    /// <summary>
    /// Transform a direction from local to world space
    /// </summary>
    public Vector3 LocalToWorldDirection(Vector3 localDirection)
    {
        return Vector3.Transform(localDirection, Rotation);
    }

    /// <summary>
    /// Transform a direction from world to local space
    /// </summary>
    public Vector3 WorldToLocalDirection(Vector3 worldDirection)
    {
        return Vector3.Transform(worldDirection, Quaternion.Conjugate(Rotation));
    }
}
