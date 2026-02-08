using System.Numerics;

namespace RedHoleEngine.Physics.Collision;

/// <summary>
/// Types of collision shapes
/// </summary>
public enum ColliderType
{
    Sphere,
    Box,
    Capsule,
    Plane
}

/// <summary>
/// Axis-aligned bounding box for broadphase collision detection
/// </summary>
public struct AABB
{
    public Vector3 Min;
    public Vector3 Max;

    public AABB(Vector3 min, Vector3 max)
    {
        Min = min;
        Max = max;
    }

    public Vector3 Center => (Min + Max) * 0.5f;
    public Vector3 Extents => (Max - Min) * 0.5f;
    public Vector3 Size => Max - Min;

    public bool Intersects(AABB other)
    {
        return Min.X <= other.Max.X && Max.X >= other.Min.X &&
               Min.Y <= other.Max.Y && Max.Y >= other.Min.Y &&
               Min.Z <= other.Max.Z && Max.Z >= other.Min.Z;
    }

    public bool Contains(Vector3 point)
    {
        return point.X >= Min.X && point.X <= Max.X &&
               point.Y >= Min.Y && point.Y <= Max.Y &&
               point.Z >= Min.Z && point.Z <= Max.Z;
    }

    public AABB Expand(float margin)
    {
        return new AABB(Min - new Vector3(margin), Max + new Vector3(margin));
    }

    public static AABB Merge(AABB a, AABB b)
    {
        return new AABB(
            Vector3.Min(a.Min, b.Min),
            Vector3.Max(a.Max, b.Max)
        );
    }

    public float SurfaceArea()
    {
        var d = Max - Min;
        return 2f * (d.X * d.Y + d.Y * d.Z + d.Z * d.X);
    }
}

/// <summary>
/// Base class for all collision shapes
/// </summary>
public abstract class Collider
{
    public ColliderType Type { get; protected set; }
    
    /// <summary>Offset from the rigid body center</summary>
    public Vector3 Offset { get; set; } = Vector3.Zero;
    
    /// <summary>Is this collider a trigger (no collision response)?</summary>
    public bool IsTrigger { get; set; }
    
    /// <summary>Material properties override (null = use rigid body values)</summary>
    public PhysicsMaterial? Material { get; set; }
    
    /// <summary>Associated rigid body (set by PhysicsWorld)</summary>
    public RigidBody? Body { get; internal set; }

    /// <summary>
    /// Get world-space AABB for this collider
    /// </summary>
    public abstract AABB GetWorldAABB(Vector3 position, Quaternion rotation);

    /// <summary>
    /// Find the point on this collider closest to a given point
    /// </summary>
    public abstract Vector3 GetClosestPoint(Vector3 point, Vector3 position, Quaternion rotation);

    /// <summary>
    /// Find the farthest point in a given direction (for GJK/EPA)
    /// </summary>
    public abstract Vector3 GetSupport(Vector3 direction, Vector3 position, Quaternion rotation);
}

/// <summary>
/// Physics material properties for collision response
/// </summary>
public class PhysicsMaterial
{
    public float Restitution { get; set; } = 0.3f;
    public float StaticFriction { get; set; } = 0.5f;
    public float DynamicFriction { get; set; } = 0.4f;

    public static readonly PhysicsMaterial Default = new();
    public static readonly PhysicsMaterial Bouncy = new() { Restitution = 0.9f, StaticFriction = 0.2f, DynamicFriction = 0.1f };
    public static readonly PhysicsMaterial Ice = new() { Restitution = 0.1f, StaticFriction = 0.05f, DynamicFriction = 0.03f };
    public static readonly PhysicsMaterial Rubber = new() { Restitution = 0.8f, StaticFriction = 0.9f, DynamicFriction = 0.8f };
    public static readonly PhysicsMaterial Metal = new() { Restitution = 0.2f, StaticFriction = 0.6f, DynamicFriction = 0.4f };
}

/// <summary>
/// Sphere collider
/// </summary>
public class SphereCollider : Collider
{
    public float Radius { get; set; } = 0.5f;

    public SphereCollider()
    {
        Type = ColliderType.Sphere;
    }

    public SphereCollider(float radius) : this()
    {
        Radius = radius;
    }

    public override AABB GetWorldAABB(Vector3 position, Quaternion rotation)
    {
        var center = position + Vector3.Transform(Offset, rotation);
        var extents = new Vector3(Radius);
        return new AABB(center - extents, center + extents);
    }

    public override Vector3 GetClosestPoint(Vector3 point, Vector3 position, Quaternion rotation)
    {
        var center = position + Vector3.Transform(Offset, rotation);
        var dir = point - center;
        var dist = dir.Length();
        
        if (dist < 0.0001f) return center + new Vector3(Radius, 0, 0);
        return center + (dir / dist) * Radius;
    }

    public override Vector3 GetSupport(Vector3 direction, Vector3 position, Quaternion rotation)
    {
        var center = position + Vector3.Transform(Offset, rotation);
        return center + Vector3.Normalize(direction) * Radius;
    }
}

/// <summary>
/// Box collider (oriented bounding box)
/// </summary>
public class BoxCollider : Collider
{
    public Vector3 HalfExtents { get; set; } = new(0.5f);

    public BoxCollider()
    {
        Type = ColliderType.Box;
    }

    public BoxCollider(Vector3 halfExtents) : this()
    {
        HalfExtents = halfExtents;
    }

    public BoxCollider(float halfWidth, float halfHeight, float halfDepth) : this()
    {
        HalfExtents = new Vector3(halfWidth, halfHeight, halfDepth);
    }

    public override AABB GetWorldAABB(Vector3 position, Quaternion rotation)
    {
        var center = position + Vector3.Transform(Offset, rotation);
        
        // Transform all 8 corners and find min/max
        var corners = new Vector3[8];
        var he = HalfExtents;
        
        corners[0] = new Vector3(-he.X, -he.Y, -he.Z);
        corners[1] = new Vector3(+he.X, -he.Y, -he.Z);
        corners[2] = new Vector3(-he.X, +he.Y, -he.Z);
        corners[3] = new Vector3(+he.X, +he.Y, -he.Z);
        corners[4] = new Vector3(-he.X, -he.Y, +he.Z);
        corners[5] = new Vector3(+he.X, -he.Y, +he.Z);
        corners[6] = new Vector3(-he.X, +he.Y, +he.Z);
        corners[7] = new Vector3(+he.X, +he.Y, +he.Z);

        var min = new Vector3(float.MaxValue);
        var max = new Vector3(float.MinValue);

        foreach (var corner in corners)
        {
            var worldCorner = center + Vector3.Transform(corner, rotation);
            min = Vector3.Min(min, worldCorner);
            max = Vector3.Max(max, worldCorner);
        }

        return new AABB(min, max);
    }

    public override Vector3 GetClosestPoint(Vector3 point, Vector3 position, Quaternion rotation)
    {
        var center = position + Vector3.Transform(Offset, rotation);
        var invRotation = Quaternion.Conjugate(rotation);
        
        // Transform point to local space
        var localPoint = Vector3.Transform(point - center, invRotation);
        
        // Clamp to box bounds
        var closest = new Vector3(
            Math.Clamp(localPoint.X, -HalfExtents.X, HalfExtents.X),
            Math.Clamp(localPoint.Y, -HalfExtents.Y, HalfExtents.Y),
            Math.Clamp(localPoint.Z, -HalfExtents.Z, HalfExtents.Z)
        );
        
        // Transform back to world space
        return center + Vector3.Transform(closest, rotation);
    }

    public override Vector3 GetSupport(Vector3 direction, Vector3 position, Quaternion rotation)
    {
        var center = position + Vector3.Transform(Offset, rotation);
        var invRotation = Quaternion.Conjugate(rotation);
        
        // Transform direction to local space
        var localDir = Vector3.Transform(direction, invRotation);
        
        // Pick the vertex in that direction
        var result = new Vector3(
            localDir.X >= 0 ? HalfExtents.X : -HalfExtents.X,
            localDir.Y >= 0 ? HalfExtents.Y : -HalfExtents.Y,
            localDir.Z >= 0 ? HalfExtents.Z : -HalfExtents.Z
        );
        
        return center + Vector3.Transform(result, rotation);
    }

    /// <summary>
    /// Get the local axes of the box in world space
    /// </summary>
    public void GetAxes(Quaternion rotation, out Vector3 axisX, out Vector3 axisY, out Vector3 axisZ)
    {
        axisX = Vector3.Transform(Vector3.UnitX, rotation);
        axisY = Vector3.Transform(Vector3.UnitY, rotation);
        axisZ = Vector3.Transform(Vector3.UnitZ, rotation);
    }
}

/// <summary>
/// Capsule collider (cylinder with hemispherical ends)
/// </summary>
public class CapsuleCollider : Collider
{
    public float Radius { get; set; } = 0.5f;
    public float Height { get; set; } = 2f; // Total height including hemispheres
    
    /// <summary>Axis of the capsule (0=X, 1=Y, 2=Z)</summary>
    public int Axis { get; set; } = 1; // Default Y-axis

    public float HalfHeight => (Height - 2f * Radius) * 0.5f;

    public CapsuleCollider()
    {
        Type = ColliderType.Capsule;
    }

    public CapsuleCollider(float radius, float height, int axis = 1) : this()
    {
        Radius = radius;
        Height = height;
        Axis = axis;
    }

    /// <summary>
    /// Get the two endpoint centers of the capsule's line segment
    /// </summary>
    public void GetEndpoints(Vector3 position, Quaternion rotation, out Vector3 p0, out Vector3 p1)
    {
        var center = position + Vector3.Transform(Offset, rotation);
        var halfH = HalfHeight;
        
        var localAxis = Axis switch
        {
            0 => Vector3.UnitX,
            1 => Vector3.UnitY,
            _ => Vector3.UnitZ
        };
        
        var worldAxis = Vector3.Transform(localAxis, rotation);
        p0 = center - worldAxis * halfH;
        p1 = center + worldAxis * halfH;
    }

    public override AABB GetWorldAABB(Vector3 position, Quaternion rotation)
    {
        GetEndpoints(position, rotation, out var p0, out var p1);
        
        var min = Vector3.Min(p0, p1) - new Vector3(Radius);
        var max = Vector3.Max(p0, p1) + new Vector3(Radius);
        
        return new AABB(min, max);
    }

    public override Vector3 GetClosestPoint(Vector3 point, Vector3 position, Quaternion rotation)
    {
        GetEndpoints(position, rotation, out var p0, out var p1);
        
        // Find closest point on line segment
        var segment = p1 - p0;
        var segmentLengthSq = segment.LengthSquared();
        
        Vector3 closestOnSegment;
        if (segmentLengthSq < 0.0001f)
        {
            closestOnSegment = p0;
        }
        else
        {
            var t = Math.Clamp(Vector3.Dot(point - p0, segment) / segmentLengthSq, 0f, 1f);
            closestOnSegment = p0 + segment * t;
        }
        
        // Extend by radius toward the point
        var dir = point - closestOnSegment;
        var dist = dir.Length();
        
        if (dist < 0.0001f) return closestOnSegment + Vector3.UnitY * Radius;
        return closestOnSegment + (dir / dist) * Radius;
    }

    public override Vector3 GetSupport(Vector3 direction, Vector3 position, Quaternion rotation)
    {
        GetEndpoints(position, rotation, out var p0, out var p1);
        
        // Pick the endpoint that's farthest in the direction
        var dot0 = Vector3.Dot(p0, direction);
        var dot1 = Vector3.Dot(p1, direction);
        
        var bestEndpoint = dot0 > dot1 ? p0 : p1;
        
        // Add radius in the direction
        return bestEndpoint + Vector3.Normalize(direction) * Radius;
    }
}

/// <summary>
/// Infinite plane collider (useful for ground, walls)
/// </summary>
public class PlaneCollider : Collider
{
    public Vector3 Normal { get; set; } = Vector3.UnitY;
    public float Distance { get; set; } = 0f; // Distance from origin along normal

    public PlaneCollider()
    {
        Type = ColliderType.Plane;
    }

    public PlaneCollider(Vector3 normal, float distance) : this()
    {
        Normal = Vector3.Normalize(normal);
        Distance = distance;
    }

    public override AABB GetWorldAABB(Vector3 position, Quaternion rotation)
    {
        // Planes are infinite, but we return a large AABB for broadphase
        const float size = 10000f;
        return new AABB(new Vector3(-size), new Vector3(size));
    }

    public override Vector3 GetClosestPoint(Vector3 point, Vector3 position, Quaternion rotation)
    {
        var worldNormal = Vector3.Transform(Normal, rotation);
        var planePoint = position + worldNormal * Distance;
        
        // Project point onto plane
        var dist = Vector3.Dot(point - planePoint, worldNormal);
        return point - worldNormal * dist;
    }

    public override Vector3 GetSupport(Vector3 direction, Vector3 position, Quaternion rotation)
    {
        // Planes don't really have support points in the traditional sense
        // Return a point far in the direction, on the plane
        var worldNormal = Vector3.Transform(Normal, rotation);
        var planePoint = position + worldNormal * Distance;
        
        // Project direction onto plane
        var projDir = direction - worldNormal * Vector3.Dot(direction, worldNormal);
        if (projDir.LengthSquared() < 0.0001f)
        {
            return planePoint;
        }
        
        return planePoint + Vector3.Normalize(projDir) * 10000f;
    }

    /// <summary>
    /// Get signed distance from a point to the plane (positive = in front of normal)
    /// </summary>
    public float GetSignedDistance(Vector3 point, Vector3 position, Quaternion rotation)
    {
        var worldNormal = Vector3.Transform(Normal, rotation);
        var planePoint = position + worldNormal * Distance;
        return Vector3.Dot(point - planePoint, worldNormal);
    }
}
