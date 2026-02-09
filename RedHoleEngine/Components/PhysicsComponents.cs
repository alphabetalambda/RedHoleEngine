using System.Collections.Generic;
using System.Numerics;
using RedHoleEngine.Core.ECS;
using RedHoleEngine.Physics;
using RedHoleEngine.Physics.Constraints;
using RedHoleEngine.Resources;
using RedHoleEngine.Physics.Collision;

namespace RedHoleEngine.Components;

/// <summary>
/// Component for rigid body physics.
/// Entities with this component will be simulated by the physics system.
/// </summary>
public struct RigidBodyComponent : IComponent
{
    /// <summary>Reference to the actual rigid body in the physics world</summary>
    internal RigidBody? Body;
    
    // Configuration (applied to Body when created)
    public RigidBodyType Type;
    public float Mass;
    public float Restitution;
    public float Friction;
    public float LinearDamping;
    public float AngularDamping;
    public bool UseGravity;
    
    // Constraints
    public bool FreezePositionX;
    public bool FreezePositionY;
    public bool FreezePositionZ;
    public bool FreezeRotationX;
    public bool FreezeRotationY;
    public bool FreezeRotationZ;
    
    // Collision filtering
    public uint CollisionLayer;
    public uint CollisionMask;

    /// <summary>
    /// Create a dynamic rigid body component
    /// </summary>
    public static RigidBodyComponent CreateDynamic(float mass = 1f)
    {
        return new RigidBodyComponent
        {
            Type = RigidBodyType.Dynamic,
            Mass = mass,
            Restitution = 0.3f,
            Friction = 0.5f,
            LinearDamping = 0.01f,
            AngularDamping = 0.05f,
            UseGravity = true,
            CollisionLayer = 1,
            CollisionMask = uint.MaxValue
        };
    }

    /// <summary>
    /// Create a static rigid body component (immovable)
    /// </summary>
    public static RigidBodyComponent CreateStatic()
    {
        return new RigidBodyComponent
        {
            Type = RigidBodyType.Static,
            Mass = 0f,
            Restitution = 0.3f,
            Friction = 0.5f,
            LinearDamping = 0f,
            AngularDamping = 0f,
            UseGravity = false,
            CollisionLayer = 1,
            CollisionMask = uint.MaxValue
        };
    }

    /// <summary>
    /// Create a kinematic rigid body component (moved by code, pushes dynamic bodies)
    /// </summary>
    public static RigidBodyComponent CreateKinematic()
    {
        return new RigidBodyComponent
        {
            Type = RigidBodyType.Kinematic,
            Mass = 0f,
            Restitution = 0.3f,
            Friction = 0.5f,
            LinearDamping = 0f,
            AngularDamping = 0f,
            UseGravity = false,
            CollisionLayer = 1,
            CollisionMask = uint.MaxValue
        };
    }

    #region Velocity Access

    public Vector3 LinearVelocity
    {
        readonly get => Body?.LinearVelocity ?? Vector3.Zero;
        set { if (Body != null) Body.LinearVelocity = value; }
    }

    public Vector3 AngularVelocity
    {
        readonly get => Body?.AngularVelocity ?? Vector3.Zero;
        set { if (Body != null) Body.AngularVelocity = value; }
    }

    #endregion

    #region Force Application

    public readonly void ApplyForce(Vector3 force) => Body?.ApplyForce(force);
    
    public readonly void ApplyForceAtPosition(Vector3 force, Vector3 worldPosition) 
        => Body?.ApplyForceAtPosition(force, worldPosition);
    
    public readonly void ApplyImpulse(Vector3 impulse) => Body?.ApplyImpulse(impulse);
    
    public readonly void ApplyImpulseAtPosition(Vector3 impulse, Vector3 worldPosition) 
        => Body?.ApplyImpulseAtPosition(impulse, worldPosition);
    
    public readonly void ApplyTorque(Vector3 torque) => Body?.ApplyTorque(torque);
    
    public readonly void ApplyAngularImpulse(Vector3 angularImpulse) 
        => Body?.ApplyAngularImpulse(angularImpulse);

    #endregion

    #region State

    public readonly bool IsAwake => Body?.IsAwake ?? false;
    
    public readonly void Wake() => Body?.Wake();

    #endregion
}

/// <summary>
/// Component for link constraints between entities
/// </summary>
public struct LinkComponent : IComponent
{
    /// <summary>Reference to the link constraint definition</summary>
    public LinkConstraint? Constraint;

    /// <summary>Whether the LinkSystem should auto-register this link</summary>
    public bool AutoRegister;

    /// <summary>Assigned link ID after registration</summary>
    public int LinkId;

    /// <summary>Assigned chain ID if the link belongs to a chain</summary>
    public int ChainId;

    /// <summary>Assigned mesh ID if the link belongs to a mesh</summary>
    public int MeshId;

    public LinkComponent(LinkConstraint constraint, bool autoRegister = true)
    {
        Constraint = constraint;
        AutoRegister = autoRegister;
        LinkId = -1;
        ChainId = -1;
        MeshId = -1;
    }
}

/// <summary>
/// Component that enables mesh relaxation (elastic/plastic deformation) via link constraints
/// </summary>
public struct MeshRelComponent : IComponent
{
    public bool AutoRegister;
    public LinkType DeformationType;

    // Link properties
    public float Stiffness;
    public float Damping;
    public float YieldThreshold;
    public float BreakThreshold;
    public float PlasticRate;
    public bool IncludeBendLinks;
    public float BendStiffnessMultiplier;

    // Node properties
    public float NodeMass;
    public float NodeLinearDamping;
    public float NodeAngularDamping;
    public bool UseGravity;
    public List<int> PinnedVertexIndices;

    // Mesh update options
    public bool CloneMeshOnStart;
    public bool RecalculateNormals;
    public bool RecalculateTangents;
    public float NormalUpdateInterval;

    // Runtime state
    internal bool Initialized;
    internal int MeshId;
    internal List<int> NodeEntities;
    internal Mesh? RuntimeMesh;
    internal float NormalUpdateTimer;

    public MeshRelComponent(LinkType deformationType = LinkType.Elastic)
    {
        AutoRegister = true;
        DeformationType = deformationType;

        Stiffness = 800f;
        Damping = 10f;
        YieldThreshold = 1.1f;
        BreakThreshold = 1.6f;
        PlasticRate = 0.5f;
        IncludeBendLinks = true;
        BendStiffnessMultiplier = 0.5f;

        NodeMass = 1f;
        NodeLinearDamping = 0.02f;
        NodeAngularDamping = 0.1f;
        UseGravity = false;
        PinnedVertexIndices = new List<int>();

        CloneMeshOnStart = false;
        RecalculateNormals = true;
        RecalculateTangents = false;
        NormalUpdateInterval = 0.1f;

        Initialized = false;
        MeshId = -1;
        NodeEntities = new List<int>();
        RuntimeMesh = null;
        NormalUpdateTimer = 0f;
    }
}

/// <summary>
/// Component for collision shapes.
/// Can be added multiple times to an entity for compound colliders.
/// </summary>
public struct ColliderComponent : IComponent
{
    /// <summary>Reference to the actual collider in the physics world</summary>
    internal Collider? Collider;
    
    /// <summary>Type of collider shape</summary>
    public ColliderType ShapeType;
    
    /// <summary>Offset from entity center</summary>
    public Vector3 Offset;
    
    /// <summary>Is this a trigger (no physics response)?</summary>
    public bool IsTrigger;
    
    // Shape-specific parameters
    public float SphereRadius;
    public Vector3 BoxHalfExtents;
    public float CapsuleRadius;
    public float CapsuleHeight;
    public int CapsuleAxis;
    public Vector3 PlaneNormal;
    public float PlaneDistance;
    
    // Physics material override
    public float? MaterialRestitution;
    public float? MaterialStaticFriction;
    public float? MaterialDynamicFriction;

    /// <summary>
    /// Create a sphere collider component
    /// </summary>
    public static ColliderComponent CreateSphere(float radius, Vector3? offset = null)
    {
        return new ColliderComponent
        {
            ShapeType = ColliderType.Sphere,
            Offset = offset ?? Vector3.Zero,
            SphereRadius = radius
        };
    }

    /// <summary>
    /// Create a box collider component
    /// </summary>
    public static ColliderComponent CreateBox(Vector3 halfExtents, Vector3? offset = null)
    {
        return new ColliderComponent
        {
            ShapeType = ColliderType.Box,
            Offset = offset ?? Vector3.Zero,
            BoxHalfExtents = halfExtents
        };
    }

    /// <summary>
    /// Create a box collider component from full size
    /// </summary>
    public static ColliderComponent CreateBoxFromSize(Vector3 size, Vector3? offset = null)
    {
        return CreateBox(size * 0.5f, offset);
    }

    /// <summary>
    /// Create a capsule collider component
    /// </summary>
    /// <param name="radius">Capsule radius</param>
    /// <param name="height">Total height including hemispherical caps</param>
    /// <param name="axis">0=X, 1=Y, 2=Z</param>
    public static ColliderComponent CreateCapsule(float radius, float height, int axis = 1, Vector3? offset = null)
    {
        return new ColliderComponent
        {
            ShapeType = ColliderType.Capsule,
            Offset = offset ?? Vector3.Zero,
            CapsuleRadius = radius,
            CapsuleHeight = height,
            CapsuleAxis = axis
        };
    }

    /// <summary>
    /// Create a plane collider component (infinite plane)
    /// </summary>
    public static ColliderComponent CreatePlane(Vector3 normal, float distance = 0)
    {
        return new ColliderComponent
        {
            ShapeType = ColliderType.Plane,
            PlaneNormal = Vector3.Normalize(normal),
            PlaneDistance = distance
        };
    }

    /// <summary>
    /// Create a ground plane at Y=0
    /// </summary>
    public static ColliderComponent CreateGroundPlane(float height = 0)
    {
        return CreatePlane(Vector3.UnitY, height);
    }

    /// <summary>
    /// Set this collider as a trigger (no physics response)
    /// </summary>
    public ColliderComponent AsTrigger()
    {
        IsTrigger = true;
        return this;
    }

    /// <summary>
    /// Set custom physics material
    /// </summary>
    public ColliderComponent WithMaterial(float restitution, float staticFriction, float dynamicFriction)
    {
        MaterialRestitution = restitution;
        MaterialStaticFriction = staticFriction;
        MaterialDynamicFriction = dynamicFriction;
        return this;
    }

    /// <summary>
    /// Create the actual Collider object from this component's configuration
    /// </summary>
    internal Collider CreateCollider()
    {
        Collider collider = ShapeType switch
        {
            ColliderType.Sphere => new SphereCollider(SphereRadius),
            ColliderType.Box => new BoxCollider(BoxHalfExtents),
            ColliderType.Capsule => new CapsuleCollider(CapsuleRadius, CapsuleHeight, CapsuleAxis),
            ColliderType.Plane => new PlaneCollider(PlaneNormal, PlaneDistance),
            _ => throw new ArgumentException($"Unknown collider type: {ShapeType}")
        };

        collider.Offset = Offset;
        collider.IsTrigger = IsTrigger;

        if (MaterialRestitution.HasValue || MaterialStaticFriction.HasValue || MaterialDynamicFriction.HasValue)
        {
            collider.Material = new PhysicsMaterial
            {
                Restitution = MaterialRestitution ?? 0.3f,
                StaticFriction = MaterialStaticFriction ?? 0.5f,
                DynamicFriction = MaterialDynamicFriction ?? 0.4f
            };
        }

        return collider;
    }
}

/// <summary>
/// Extension methods for adding physics components to entities
/// </summary>
public static class PhysicsComponentExtensions
{
    /// <summary>
    /// Add a dynamic rigid body to an entity
    /// </summary>
    public static void AddRigidBody(this World world, Entity entity, float mass = 1f)
    {
        world.AddComponent(entity, RigidBodyComponent.CreateDynamic(mass));
    }

    /// <summary>
    /// Add a static rigid body to an entity
    /// </summary>
    public static void AddStaticBody(this World world, Entity entity)
    {
        world.AddComponent(entity, RigidBodyComponent.CreateStatic());
    }

    /// <summary>
    /// Add a kinematic rigid body to an entity
    /// </summary>
    public static void AddKinematicBody(this World world, Entity entity)
    {
        world.AddComponent(entity, RigidBodyComponent.CreateKinematic());
    }

    /// <summary>
    /// Add a sphere collider to an entity
    /// </summary>
    public static void AddSphereCollider(this World world, Entity entity, float radius, Vector3? offset = null)
    {
        world.AddComponent(entity, ColliderComponent.CreateSphere(radius, offset));
    }

    /// <summary>
    /// Add a box collider to an entity
    /// </summary>
    public static void AddBoxCollider(this World world, Entity entity, Vector3 halfExtents, Vector3? offset = null)
    {
        world.AddComponent(entity, ColliderComponent.CreateBox(halfExtents, offset));
    }

    /// <summary>
    /// Add a capsule collider to an entity
    /// </summary>
    public static void AddCapsuleCollider(this World world, Entity entity, float radius, float height, int axis = 1, Vector3? offset = null)
    {
        world.AddComponent(entity, ColliderComponent.CreateCapsule(radius, height, axis, offset));
    }

    /// <summary>
    /// Add a ground plane collider to an entity
    /// </summary>
    public static void AddGroundPlane(this World world, Entity entity, float height = 0)
    {
        world.AddComponent(entity, RigidBodyComponent.CreateStatic());
        world.AddComponent(entity, ColliderComponent.CreateGroundPlane(height));
    }
}
