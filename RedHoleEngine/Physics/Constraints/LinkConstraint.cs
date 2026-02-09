using System.Numerics;
using System.Runtime.CompilerServices;

namespace RedHoleEngine.Physics.Constraints;

/// <summary>
/// A distance constraint between two points, which can be on rigid bodies or in world space.
/// Supports rigid, elastic, plastic, and rope behaviors.
/// </summary>
public class LinkConstraint
{
    // ===== Identity =====
    
    /// <summary>
    /// Unique identifier for this constraint
    /// </summary>
    public int Id;
    
    /// <summary>
    /// Chain ID if part of a chain (-1 if standalone)
    /// </summary>
    public int ChainId = -1;
    
    /// <summary>
    /// Mesh ID if part of a mesh (-1 if standalone)
    /// </summary>
    public int MeshId = -1;
    
    /// <summary>
    /// Index within the chain or mesh (-1 if standalone)
    /// </summary>
    public int IndexInCollection = -1;
    
    // ===== Connection Points =====
    
    /// <summary>
    /// Entity ID of the first body (required)
    /// </summary>
    public int EntityA;
    
    /// <summary>
    /// Entity ID of the second body (null = world anchor)
    /// </summary>
    public int? EntityB;
    
    /// <summary>
    /// Anchor point offset from EntityA center in local space
    /// </summary>
    public Vector3 LocalAnchorA;
    
    /// <summary>
    /// Anchor point offset from EntityB center in local space,
    /// or world position if EntityB is null
    /// </summary>
    public Vector3 LocalAnchorB;
    
    // ===== Link Type and State =====
    
    /// <summary>
    /// Type of constraint behavior
    /// </summary>
    public LinkType Type;
    
    /// <summary>
    /// Current state of the constraint
    /// </summary>
    public LinkState State = LinkState.Active;
    
    // ===== Distance Properties =====
    
    /// <summary>
    /// Target rest length of the constraint
    /// </summary>
    public float RestLength;
    
    /// <summary>
    /// Minimum allowed length (0 = no minimum)
    /// For ropes, this is typically 0.
    /// </summary>
    public float MinLength;
    
    /// <summary>
    /// Maximum allowed length (float.MaxValue = no maximum)
    /// </summary>
    public float MaxLength = float.MaxValue;
    
    // ===== Elastic Properties =====
    
    /// <summary>
    /// Spring constant k in N/m. Higher = stiffer spring.
    /// </summary>
    public float Stiffness = 1000f;
    
    /// <summary>
    /// Damping coefficient c. Higher = more energy dissipation.
    /// </summary>
    public float Damping = 10f;
    
    // ===== Plastic Properties =====
    
    /// <summary>
    /// Stretch ratio at which permanent deformation begins.
    /// E.g., 1.1 means 10% stretch triggers yielding.
    /// </summary>
    public float YieldThreshold = 1.1f;
    
    /// <summary>
    /// Rate of plastic deformation (0-1 per second at full yield).
    /// </summary>
    public float PlasticRate = 0.5f;
    
    /// <summary>
    /// Stretch ratio at which the link breaks.
    /// E.g., 1.5 means 50% stretch breaks the link.
    /// </summary>
    public float BreakThreshold = 1.5f;
    
    /// <summary>
    /// Force threshold for breaking (alternative to stretch-based).
    /// If > 0, link breaks when force exceeds this value.
    /// </summary>
    public float BreakForce;
    
    /// <summary>
    /// Current rest length after plastic deformation.
    /// Initialized to RestLength, increases as link yields.
    /// </summary>
    public float CurrentRestLength;
    
    // ===== Rope Properties =====
    
    /// <summary>
    /// For ropes: additional slack before tension applies (ratio, e.g., 0.1 = 10% slack)
    /// </summary>
    public float Slack;
    
    // ===== Angle Constraints (Ragdolls) =====
    
    /// <summary>
    /// Optional angle limits for ragdoll joints
    /// </summary>
    public AngleLimits? AngleLimits;
    
    /// <summary>
    /// Rest orientation for angle constraints (local space of body A)
    /// </summary>
    public Quaternion RestOrientation = Quaternion.Identity;
    
    // ===== Collision =====
    
    /// <summary>
    /// If true, this link segment participates in collision detection.
    /// Useful for rope/chain collision.
    /// </summary>
    public bool CollisionEnabled;
    
    /// <summary>
    /// Collision radius for rope/chain segments
    /// </summary>
    public float CollisionRadius = 0.05f;
    
    // ===== Solver State (Internal) =====
    
    /// <summary>
    /// Cached reference to body A's RigidBody
    /// </summary>
    internal RigidBody? BodyA;
    
    /// <summary>
    /// Cached reference to body B's RigidBody (null for world anchor)
    /// </summary>
    internal RigidBody? BodyB;
    
    /// <summary>
    /// World space position of anchor A
    /// </summary>
    internal Vector3 WorldAnchorA;
    
    /// <summary>
    /// World space position of anchor B
    /// </summary>
    internal Vector3 WorldAnchorB;
    
    /// <summary>
    /// Accumulated impulse for warm starting
    /// </summary>
    internal float AccumulatedImpulse;
    
    /// <summary>
    /// Current length of the constraint
    /// </summary>
    internal float CurrentLength;
    
    /// <summary>
    /// Current force/tension in the constraint
    /// </summary>
    internal float CurrentForce;
    
    /// <summary>
    /// Constraint axis (normalized direction from A to B)
    /// </summary>
    internal Vector3 Axis;
    
    /// <summary>
    /// Effective mass for the constraint
    /// </summary>
    internal float EffectiveMass;
    
    /// <summary>
    /// Bias velocity for position correction
    /// </summary>
    internal float Bias;
    
    // ===== Computed Properties =====
    
    /// <summary>
    /// Current stretch ratio (CurrentLength / CurrentRestLength)
    /// </summary>
    public float StretchRatio => CurrentRestLength > 0 ? CurrentLength / CurrentRestLength : 1f;
    
    /// <summary>
    /// Midpoint of the link in world space
    /// </summary>
    public Vector3 Midpoint => (WorldAnchorA + WorldAnchorB) * 0.5f;
    
    /// <summary>
    /// Stress level as percentage of break threshold (0-1)
    /// </summary>
    public float StressLevel
    {
        get
        {
            if (BreakThreshold <= 1f) return 0f;
            float stretchRange = BreakThreshold - 1f;
            float currentStretch = StretchRatio - 1f;
            return Math.Clamp(currentStretch / stretchRange, 0f, 1f);
        }
    }
    
    // ===== Factory Methods =====
    
    /// <summary>
    /// Create a rigid (fixed distance) constraint
    /// </summary>
    public static LinkConstraint Rigid(int entityA, int? entityB, Vector3 anchorA, Vector3 anchorB, float length)
    {
        return new LinkConstraint
        {
            Type = LinkType.Rigid,
            EntityA = entityA,
            EntityB = entityB,
            LocalAnchorA = anchorA,
            LocalAnchorB = anchorB,
            RestLength = length,
            CurrentRestLength = length,
            Stiffness = float.MaxValue
        };
    }
    
    /// <summary>
    /// Create an elastic (spring-damper) constraint
    /// </summary>
    public static LinkConstraint Elastic(int entityA, int? entityB, Vector3 anchorA, Vector3 anchorB, 
        float length, float stiffness = 1000f, float damping = 10f)
    {
        return new LinkConstraint
        {
            Type = LinkType.Elastic,
            EntityA = entityA,
            EntityB = entityB,
            LocalAnchorA = anchorA,
            LocalAnchorB = anchorB,
            RestLength = length,
            CurrentRestLength = length,
            Stiffness = stiffness,
            Damping = damping
        };
    }
    
    /// <summary>
    /// Create a plastic (deformable/breakable) constraint
    /// </summary>
    public static LinkConstraint Plastic(int entityA, int? entityB, Vector3 anchorA, Vector3 anchorB,
        float length, float stiffness = 1000f, float yieldThreshold = 1.1f, float breakThreshold = 1.5f)
    {
        return new LinkConstraint
        {
            Type = LinkType.Plastic,
            EntityA = entityA,
            EntityB = entityB,
            LocalAnchorA = anchorA,
            LocalAnchorB = anchorB,
            RestLength = length,
            CurrentRestLength = length,
            Stiffness = stiffness,
            YieldThreshold = yieldThreshold,
            BreakThreshold = breakThreshold
        };
    }
    
    /// <summary>
    /// Create a rope constraint (resists stretching only, not compression)
    /// </summary>
    public static LinkConstraint Rope(int entityA, int? entityB, Vector3 anchorA, Vector3 anchorB,
        float length, float stiffness = 5000f, float damping = 50f, float slack = 0f)
    {
        return new LinkConstraint
        {
            Type = LinkType.Rope,
            EntityA = entityA,
            EntityB = entityB,
            LocalAnchorA = anchorA,
            LocalAnchorB = anchorB,
            RestLength = length,
            CurrentRestLength = length,
            Stiffness = stiffness,
            Damping = damping,
            Slack = slack,
            MinLength = 0f
        };
    }
    
    /// <summary>
    /// Create a world anchor (attaches entity to fixed world point)
    /// </summary>
    public static LinkConstraint WorldAnchor(int entity, Vector3 localAnchor, Vector3 worldPoint, 
        LinkType type = LinkType.Rigid, float length = 0f)
    {
        return new LinkConstraint
        {
            Type = type,
            EntityA = entity,
            EntityB = null,
            LocalAnchorA = localAnchor,
            LocalAnchorB = worldPoint,  // Interpreted as world position when EntityB is null
            RestLength = length,
            CurrentRestLength = length
        };
    }
    
    /// <summary>
    /// Create a ragdoll joint with angle limits
    /// </summary>
    public static LinkConstraint RagdollJoint(int entityA, int entityB, Vector3 anchorA, Vector3 anchorB,
        AngleLimits angleLimits)
    {
        return new LinkConstraint
        {
            Type = LinkType.Rigid,
            EntityA = entityA,
            EntityB = entityB,
            LocalAnchorA = anchorA,
            LocalAnchorB = anchorB,
            RestLength = 0f,  // Zero-length for joints
            CurrentRestLength = 0f,
            AngleLimits = angleLimits
        };
    }
    
    // ===== Methods =====
    
    /// <summary>
    /// Update world anchors from body positions
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal void UpdateWorldAnchors()
    {
        if (BodyA != null)
        {
            WorldAnchorA = BodyA.Position + Vector3.Transform(LocalAnchorA, BodyA.Rotation);
        }
        else
        {
            WorldAnchorA = LocalAnchorA;
        }
        
        if (EntityB.HasValue && BodyB != null)
        {
            WorldAnchorB = BodyB.Position + Vector3.Transform(LocalAnchorB, BodyB.Rotation);
        }
        else
        {
            // World anchor - LocalAnchorB is the world position
            WorldAnchorB = LocalAnchorB;
        }
        
        // Update current length and axis
        Vector3 delta = WorldAnchorB - WorldAnchorA;
        CurrentLength = delta.Length();
        
        if (CurrentLength > 0.0001f)
        {
            Axis = delta / CurrentLength;
        }
        else
        {
            Axis = Vector3.UnitY;
        }
    }
    
    /// <summary>
    /// Calculate effective mass for impulse-based solving
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal void CalculateEffectiveMass()
    {
        float invMassA = BodyA?.InverseMass ?? 0f;
        float invMassB = BodyB?.InverseMass ?? 0f;
        
        // Simple case: just inverse masses
        // TODO: Add angular contributions for more accuracy
        EffectiveMass = invMassA + invMassB;
        
        if (EffectiveMass > 0.0001f)
        {
            EffectiveMass = 1f / EffectiveMass;
        }
        else
        {
            EffectiveMass = 0f;
        }
    }
    
    /// <summary>
    /// Get velocity at anchor point for body A
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal Vector3 GetVelocityAtAnchorA()
    {
        if (BodyA == null) return Vector3.Zero;
        
        Vector3 r = WorldAnchorA - BodyA.Position;
        return BodyA.LinearVelocity + Vector3.Cross(BodyA.AngularVelocity, r);
    }
    
    /// <summary>
    /// Get velocity at anchor point for body B
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal Vector3 GetVelocityAtAnchorB()
    {
        if (BodyB == null) return Vector3.Zero;
        
        Vector3 r = WorldAnchorB - BodyB.Position;
        return BodyB.LinearVelocity + Vector3.Cross(BodyB.AngularVelocity, r);
    }
    
    /// <summary>
    /// Apply an impulse to both bodies along the constraint axis
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal void ApplyImpulse(float lambda)
    {
        Vector3 impulse = Axis * lambda;
        
        BodyA?.ApplyImpulseAtPosition(-impulse, WorldAnchorA);
        BodyB?.ApplyImpulseAtPosition(impulse, WorldAnchorB);
    }
    
    /// <summary>
    /// Apply a force to both bodies along the constraint axis
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal void ApplyForce(float force)
    {
        Vector3 forceVec = Axis * force;
        
        BodyA?.ApplyForceAtPosition(-forceVec, WorldAnchorA);
        BodyB?.ApplyForceAtPosition(forceVec, WorldAnchorB);
        
        CurrentForce = Math.Abs(force);
    }
    
    /// <summary>
    /// Reset the constraint to initial state
    /// </summary>
    public void Reset()
    {
        State = LinkState.Active;
        CurrentRestLength = RestLength;
        AccumulatedImpulse = 0f;
        CurrentForce = 0f;
    }
    
    /// <summary>
    /// Clone this constraint with a new ID
    /// </summary>
    public LinkConstraint Clone(int newId)
    {
        return new LinkConstraint
        {
            Id = newId,
            ChainId = ChainId,
            MeshId = MeshId,
            EntityA = EntityA,
            EntityB = EntityB,
            LocalAnchorA = LocalAnchorA,
            LocalAnchorB = LocalAnchorB,
            Type = Type,
            State = State,
            RestLength = RestLength,
            MinLength = MinLength,
            MaxLength = MaxLength,
            Stiffness = Stiffness,
            Damping = Damping,
            YieldThreshold = YieldThreshold,
            PlasticRate = PlasticRate,
            BreakThreshold = BreakThreshold,
            BreakForce = BreakForce,
            CurrentRestLength = CurrentRestLength,
            Slack = Slack,
            AngleLimits = AngleLimits,
            RestOrientation = RestOrientation,
            CollisionEnabled = CollisionEnabled,
            CollisionRadius = CollisionRadius
        };
    }
}
