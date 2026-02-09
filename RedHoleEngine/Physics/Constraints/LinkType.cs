using System.Numerics;

namespace RedHoleEngine.Physics.Constraints;

/// <summary>
/// Type of link constraint behavior
/// </summary>
public enum LinkType
{
    /// <summary>
    /// Fixed distance, infinite stiffness. 
    /// Used for rigid rods, bones, fixed attachments.
    /// </summary>
    Rigid,
    
    /// <summary>
    /// Spring-damper system that returns to rest length.
    /// Used for bungee cords, soft connections, cloth.
    /// </summary>
    Elastic,
    
    /// <summary>
    /// Elastic with permanent deformation beyond yield threshold.
    /// Can break at break threshold. Used for destructible structures.
    /// </summary>
    Plastic,
    
    /// <summary>
    /// One-way constraint that only resists stretching, not compression.
    /// Used for ropes, cables, chains.
    /// </summary>
    Rope
}

/// <summary>
/// Current state of a link constraint
/// </summary>
public enum LinkState
{
    /// <summary>
    /// Normal operation
    /// </summary>
    Active,
    
    /// <summary>
    /// Plastic link is currently deforming (exceeded yield threshold)
    /// </summary>
    Yielding,
    
    /// <summary>
    /// Link has snapped and is no longer active
    /// </summary>
    Broken,
    
    /// <summary>
    /// Rope is slack (under compression, not applying force)
    /// </summary>
    Slack
}

/// <summary>
/// Flags for angle/rotation constraints (for ragdoll joints)
/// </summary>
[Flags]
public enum AngleConstraintFlags
{
    None = 0,
    LimitTwist = 1,      // Limit rotation around the link axis
    LimitSwing = 2,      // Limit cone angle from rest direction
    LimitSwingX = 4,     // Limit swing in local X direction
    LimitSwingY = 8,     // Limit swing in local Y direction
    All = LimitTwist | LimitSwing
}

/// <summary>
/// Configuration for angle limits on a link (ragdoll joints)
/// </summary>
public struct AngleLimits
{
    /// <summary>
    /// Which angle constraints are active
    /// </summary>
    public AngleConstraintFlags Flags;
    
    /// <summary>
    /// Maximum twist angle in radians (rotation around link axis)
    /// </summary>
    public float MaxTwist;
    
    /// <summary>
    /// Minimum twist angle in radians
    /// </summary>
    public float MinTwist;
    
    /// <summary>
    /// Maximum swing angle in radians (cone constraint)
    /// </summary>
    public float MaxSwing;
    
    /// <summary>
    /// Maximum swing in local X direction (elliptical cone)
    /// </summary>
    public float MaxSwingX;
    
    /// <summary>
    /// Maximum swing in local Y direction (elliptical cone)
    /// </summary>
    public float MaxSwingY;
    
    /// <summary>
    /// Stiffness for soft angle limits (0 = hard limit)
    /// </summary>
    public float Stiffness;
    
    /// <summary>
    /// Damping for soft angle limits
    /// </summary>
    public float Damping;

    /// <summary>
    /// Create a simple cone constraint (ball-and-socket joint)
    /// </summary>
    public static AngleLimits Cone(float maxAngleRadians)
    {
        return new AngleLimits
        {
            Flags = AngleConstraintFlags.LimitSwing,
            MaxSwing = maxAngleRadians
        };
    }

    /// <summary>
    /// Create a twist-limited joint (like a shoulder)
    /// </summary>
    public static AngleLimits TwistAndSwing(float maxTwist, float maxSwing)
    {
        return new AngleLimits
        {
            Flags = AngleConstraintFlags.All,
            MinTwist = -maxTwist,
            MaxTwist = maxTwist,
            MaxSwing = maxSwing
        };
    }

    /// <summary>
    /// Create an elliptical cone constraint (like a knee or elbow)
    /// </summary>
    public static AngleLimits EllipticalCone(float maxSwingX, float maxSwingY, float maxTwist = 0)
    {
        return new AngleLimits
        {
            Flags = AngleConstraintFlags.LimitSwingX | AngleConstraintFlags.LimitSwingY | 
                    (maxTwist > 0 ? AngleConstraintFlags.LimitTwist : 0),
            MaxSwingX = maxSwingX,
            MaxSwingY = maxSwingY,
            MinTwist = -maxTwist,
            MaxTwist = maxTwist
        };
    }

    /// <summary>
    /// Create a hinge constraint (1 DOF rotation)
    /// </summary>
    public static AngleLimits Hinge(float minAngle, float maxAngle)
    {
        return new AngleLimits
        {
            Flags = AngleConstraintFlags.LimitSwingX | AngleConstraintFlags.LimitSwingY | AngleConstraintFlags.LimitTwist,
            MaxSwingX = 0.01f,  // Nearly zero
            MaxSwingY = 0.01f,
            MinTwist = minAngle,
            MaxTwist = maxAngle
        };
    }
}
