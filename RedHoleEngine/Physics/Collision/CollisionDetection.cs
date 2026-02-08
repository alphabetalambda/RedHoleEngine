using System.Numerics;

namespace RedHoleEngine.Physics.Collision;

/// <summary>
/// Result of a collision detection query
/// </summary>
public struct CollisionContact
{
    /// <summary>Contact point on body A (world space)</summary>
    public Vector3 PointOnA;
    
    /// <summary>Contact point on body B (world space)</summary>
    public Vector3 PointOnB;
    
    /// <summary>Contact normal (points from A to B)</summary>
    public Vector3 Normal;
    
    /// <summary>Penetration depth (positive = overlapping)</summary>
    public float Depth;
}

/// <summary>
/// A collision manifold containing contact information between two bodies
/// </summary>
public class CollisionManifold
{
    public RigidBody BodyA { get; set; } = null!;
    public RigidBody BodyB { get; set; } = null!;
    public Collider ColliderA { get; set; } = null!;
    public Collider ColliderB { get; set; } = null!;
    
    public List<CollisionContact> Contacts { get; } = new(4);
    
    /// <summary>Combined restitution</summary>
    public float Restitution { get; set; }
    
    /// <summary>Combined friction</summary>
    public float Friction { get; set; }

    public void Clear()
    {
        Contacts.Clear();
    }

    public void AddContact(CollisionContact contact)
    {
        // Limit contacts to 4 (typical for a face contact)
        if (Contacts.Count >= 4)
        {
            // Replace the contact with smallest depth
            int minIndex = 0;
            float minDepth = Contacts[0].Depth;
            for (int i = 1; i < Contacts.Count; i++)
            {
                if (Contacts[i].Depth < minDepth)
                {
                    minDepth = Contacts[i].Depth;
                    minIndex = i;
                }
            }
            if (contact.Depth > minDepth)
            {
                Contacts[minIndex] = contact;
            }
        }
        else
        {
            Contacts.Add(contact);
        }
    }
}

/// <summary>
/// Static collision detection algorithms
/// </summary>
public static class CollisionDetection
{
    private const float Epsilon = 0.0001f;

    /// <summary>
    /// Test collision between two colliders
    /// </summary>
    public static bool TestCollision(
        Collider a, Vector3 posA, Quaternion rotA,
        Collider b, Vector3 posB, Quaternion rotB,
        out CollisionManifold manifold)
    {
        manifold = new CollisionManifold
        {
            ColliderA = a,
            ColliderB = b
        };

        bool hasCollision = (a.Type, b.Type) switch
        {
            (ColliderType.Sphere, ColliderType.Sphere) => 
                SphereSphere((SphereCollider)a, posA, rotA, (SphereCollider)b, posB, rotB, manifold),
            
            (ColliderType.Sphere, ColliderType.Box) => 
                SphereBox((SphereCollider)a, posA, rotA, (BoxCollider)b, posB, rotB, manifold),
            (ColliderType.Box, ColliderType.Sphere) => 
                SphereBox((SphereCollider)b, posB, rotB, (BoxCollider)a, posA, rotA, manifold, true),
            
            (ColliderType.Sphere, ColliderType.Plane) => 
                SpherePlane((SphereCollider)a, posA, rotA, (PlaneCollider)b, posB, rotB, manifold),
            (ColliderType.Plane, ColliderType.Sphere) => 
                SpherePlane((SphereCollider)b, posB, rotB, (PlaneCollider)a, posA, rotA, manifold, true),
            
            (ColliderType.Box, ColliderType.Box) => 
                BoxBox((BoxCollider)a, posA, rotA, (BoxCollider)b, posB, rotB, manifold),
            
            (ColliderType.Box, ColliderType.Plane) => 
                BoxPlane((BoxCollider)a, posA, rotA, (PlaneCollider)b, posB, rotB, manifold),
            (ColliderType.Plane, ColliderType.Box) => 
                BoxPlane((BoxCollider)b, posB, rotB, (PlaneCollider)a, posA, rotA, manifold, true),
            
            (ColliderType.Sphere, ColliderType.Capsule) => 
                SphereCapsule((SphereCollider)a, posA, rotA, (CapsuleCollider)b, posB, rotB, manifold),
            (ColliderType.Capsule, ColliderType.Sphere) => 
                SphereCapsule((SphereCollider)b, posB, rotB, (CapsuleCollider)a, posA, rotA, manifold, true),
            
            (ColliderType.Capsule, ColliderType.Capsule) => 
                CapsuleCapsule((CapsuleCollider)a, posA, rotA, (CapsuleCollider)b, posB, rotB, manifold),
            
            (ColliderType.Capsule, ColliderType.Plane) => 
                CapsulePlane((CapsuleCollider)a, posA, rotA, (PlaneCollider)b, posB, rotB, manifold),
            (ColliderType.Plane, ColliderType.Capsule) => 
                CapsulePlane((CapsuleCollider)b, posB, rotB, (PlaneCollider)a, posA, rotA, manifold, true),

            // Plane-Plane: no collision (parallel or same)
            (ColliderType.Plane, ColliderType.Plane) => false,
            
            _ => false
        };

        return hasCollision;
    }

    #region Sphere Collisions

    private static bool SphereSphere(
        SphereCollider a, Vector3 posA, Quaternion rotA,
        SphereCollider b, Vector3 posB, Quaternion rotB,
        CollisionManifold manifold)
    {
        var centerA = posA + Vector3.Transform(a.Offset, rotA);
        var centerB = posB + Vector3.Transform(b.Offset, rotB);
        
        var diff = centerB - centerA;
        var distSq = diff.LengthSquared();
        var radiusSum = a.Radius + b.Radius;
        
        if (distSq > radiusSum * radiusSum)
            return false;
        
        var dist = MathF.Sqrt(distSq);
        var normal = dist > Epsilon ? diff / dist : Vector3.UnitY;
        
        manifold.AddContact(new CollisionContact
        {
            PointOnA = centerA + normal * a.Radius,
            PointOnB = centerB - normal * b.Radius,
            Normal = normal,
            Depth = radiusSum - dist
        });
        
        return true;
    }

    private static bool SphereBox(
        SphereCollider sphere, Vector3 posSphere, Quaternion rotSphere,
        BoxCollider box, Vector3 posBox, Quaternion rotBox,
        CollisionManifold manifold, bool flip = false)
    {
        var sphereCenter = posSphere + Vector3.Transform(sphere.Offset, rotSphere);
        var boxCenter = posBox + Vector3.Transform(box.Offset, rotBox);
        
        // Transform sphere center to box's local space
        var invRotBox = Quaternion.Conjugate(rotBox);
        var localSphereCenter = Vector3.Transform(sphereCenter - boxCenter, invRotBox);
        
        // Find closest point on box
        var closest = new Vector3(
            Math.Clamp(localSphereCenter.X, -box.HalfExtents.X, box.HalfExtents.X),
            Math.Clamp(localSphereCenter.Y, -box.HalfExtents.Y, box.HalfExtents.Y),
            Math.Clamp(localSphereCenter.Z, -box.HalfExtents.Z, box.HalfExtents.Z)
        );
        
        var diff = localSphereCenter - closest;
        var distSq = diff.LengthSquared();
        
        if (distSq > sphere.Radius * sphere.Radius)
            return false;
        
        var dist = MathF.Sqrt(distSq);
        Vector3 localNormal;
        
        if (dist > Epsilon)
        {
            localNormal = diff / dist;
        }
        else
        {
            // Sphere center inside box - find closest face
            var penetrations = new Vector3(
                box.HalfExtents.X - MathF.Abs(localSphereCenter.X),
                box.HalfExtents.Y - MathF.Abs(localSphereCenter.Y),
                box.HalfExtents.Z - MathF.Abs(localSphereCenter.Z)
            );
            
            if (penetrations.X <= penetrations.Y && penetrations.X <= penetrations.Z)
                localNormal = localSphereCenter.X >= 0 ? Vector3.UnitX : -Vector3.UnitX;
            else if (penetrations.Y <= penetrations.Z)
                localNormal = localSphereCenter.Y >= 0 ? Vector3.UnitY : -Vector3.UnitY;
            else
                localNormal = localSphereCenter.Z >= 0 ? Vector3.UnitZ : -Vector3.UnitZ;
        }
        
        var worldClosest = boxCenter + Vector3.Transform(closest, rotBox);
        var worldNormal = Vector3.Transform(localNormal, rotBox);
        
        if (flip)
        {
            worldNormal = -worldNormal;
            manifold.AddContact(new CollisionContact
            {
                PointOnA = worldClosest,
                PointOnB = sphereCenter - worldNormal * sphere.Radius,
                Normal = worldNormal,
                Depth = sphere.Radius - dist
            });
        }
        else
        {
            manifold.AddContact(new CollisionContact
            {
                PointOnA = sphereCenter + worldNormal * sphere.Radius,
                PointOnB = worldClosest,
                Normal = worldNormal,
                Depth = sphere.Radius - dist
            });
        }
        
        return true;
    }

    private static bool SpherePlane(
        SphereCollider sphere, Vector3 posSphere, Quaternion rotSphere,
        PlaneCollider plane, Vector3 posPlane, Quaternion rotPlane,
        CollisionManifold manifold, bool flip = false)
    {
        var sphereCenter = posSphere + Vector3.Transform(sphere.Offset, rotSphere);
        var planeNormal = Vector3.Transform(plane.Normal, rotPlane);
        var planePoint = posPlane + planeNormal * plane.Distance;
        
        var dist = Vector3.Dot(sphereCenter - planePoint, planeNormal);
        
        if (dist > sphere.Radius)
            return false;
        
        var normal = flip ? -planeNormal : planeNormal;
        var depth = sphere.Radius - dist;
        
        manifold.AddContact(new CollisionContact
        {
            PointOnA = flip ? sphereCenter - planeNormal * dist : sphereCenter - planeNormal * sphere.Radius,
            PointOnB = flip ? sphereCenter - planeNormal * sphere.Radius : sphereCenter - planeNormal * dist,
            Normal = normal,
            Depth = depth
        });
        
        return true;
    }

    private static bool SphereCapsule(
        SphereCollider sphere, Vector3 posSphere, Quaternion rotSphere,
        CapsuleCollider capsule, Vector3 posCapsule, Quaternion rotCapsule,
        CollisionManifold manifold, bool flip = false)
    {
        var sphereCenter = posSphere + Vector3.Transform(sphere.Offset, rotSphere);
        capsule.GetEndpoints(posCapsule, rotCapsule, out var p0, out var p1);
        
        // Find closest point on capsule line segment
        var segment = p1 - p0;
        var segmentLengthSq = segment.LengthSquared();
        
        Vector3 closestOnSegment;
        if (segmentLengthSq < Epsilon)
        {
            closestOnSegment = p0;
        }
        else
        {
            var t = Math.Clamp(Vector3.Dot(sphereCenter - p0, segment) / segmentLengthSq, 0f, 1f);
            closestOnSegment = p0 + segment * t;
        }
        
        var diff = sphereCenter - closestOnSegment;
        var distSq = diff.LengthSquared();
        var radiusSum = sphere.Radius + capsule.Radius;
        
        if (distSq > radiusSum * radiusSum)
            return false;
        
        var dist = MathF.Sqrt(distSq);
        var normal = dist > Epsilon ? diff / dist : Vector3.UnitY;
        
        if (flip)
        {
            normal = -normal;
            manifold.AddContact(new CollisionContact
            {
                PointOnA = closestOnSegment - normal * capsule.Radius,
                PointOnB = sphereCenter + normal * sphere.Radius,
                Normal = normal,
                Depth = radiusSum - dist
            });
        }
        else
        {
            manifold.AddContact(new CollisionContact
            {
                PointOnA = sphereCenter - normal * sphere.Radius,
                PointOnB = closestOnSegment + normal * capsule.Radius,
                Normal = normal,
                Depth = radiusSum - dist
            });
        }
        
        return true;
    }

    #endregion

    #region Box Collisions

    private static bool BoxBox(
        BoxCollider a, Vector3 posA, Quaternion rotA,
        BoxCollider b, Vector3 posB, Quaternion rotB,
        CollisionManifold manifold)
    {
        // SAT (Separating Axis Theorem) for OBB vs OBB
        var centerA = posA + Vector3.Transform(a.Offset, rotA);
        var centerB = posB + Vector3.Transform(b.Offset, rotB);
        
        a.GetAxes(rotA, out var axisAX, out var axisAY, out var axisAZ);
        b.GetAxes(rotB, out var axisBX, out var axisBY, out var axisBZ);
        
        var axesA = new[] { axisAX, axisAY, axisAZ };
        var axesB = new[] { axisBX, axisBY, axisBZ };
        
        var extentsA = new[] { a.HalfExtents.X, a.HalfExtents.Y, a.HalfExtents.Z };
        var extentsB = new[] { b.HalfExtents.X, b.HalfExtents.Y, b.HalfExtents.Z };
        
        var t = centerB - centerA;
        
        float minPenetration = float.MaxValue;
        Vector3 minAxis = Vector3.Zero;
        
        // Test 15 axes: 3 from A, 3 from B, 9 cross products
        
        // Test A's axes
        for (int i = 0; i < 3; i++)
        {
            var axis = axesA[i];
            var projA = extentsA[i];
            var projB = ProjectBoxOntoAxis(extentsB, axesB, axis);
            var dist = MathF.Abs(Vector3.Dot(t, axis));
            var penetration = projA + projB - dist;
            
            if (penetration < 0) return false;
            if (penetration < minPenetration)
            {
                minPenetration = penetration;
                minAxis = Vector3.Dot(t, axis) < 0 ? -axis : axis;
            }
        }
        
        // Test B's axes
        for (int i = 0; i < 3; i++)
        {
            var axis = axesB[i];
            var projA = ProjectBoxOntoAxis(extentsA, axesA, axis);
            var projB = extentsB[i];
            var dist = MathF.Abs(Vector3.Dot(t, axis));
            var penetration = projA + projB - dist;
            
            if (penetration < 0) return false;
            if (penetration < minPenetration)
            {
                minPenetration = penetration;
                minAxis = Vector3.Dot(t, axis) < 0 ? -axis : axis;
            }
        }
        
        // Test cross product axes
        for (int i = 0; i < 3; i++)
        {
            for (int j = 0; j < 3; j++)
            {
                var axis = Vector3.Cross(axesA[i], axesB[j]);
                var lengthSq = axis.LengthSquared();
                if (lengthSq < Epsilon) continue; // Parallel axes
                
                axis /= MathF.Sqrt(lengthSq);
                
                var projA = ProjectBoxOntoAxis(extentsA, axesA, axis);
                var projB = ProjectBoxOntoAxis(extentsB, axesB, axis);
                var dist = MathF.Abs(Vector3.Dot(t, axis));
                var penetration = projA + projB - dist;
                
                if (penetration < 0) return false;
                if (penetration < minPenetration)
                {
                    minPenetration = penetration;
                    minAxis = Vector3.Dot(t, axis) < 0 ? -axis : axis;
                }
            }
        }
        
        // Generate contact point(s)
        // For simplicity, use the closest points between the boxes
        var contactPoint = (centerA + centerB) * 0.5f;
        
        manifold.AddContact(new CollisionContact
        {
            PointOnA = contactPoint - minAxis * (minPenetration * 0.5f),
            PointOnB = contactPoint + minAxis * (minPenetration * 0.5f),
            Normal = minAxis,
            Depth = minPenetration
        });
        
        return true;
    }

    private static float ProjectBoxOntoAxis(float[] extents, Vector3[] axes, Vector3 axis)
    {
        return extents[0] * MathF.Abs(Vector3.Dot(axes[0], axis)) +
               extents[1] * MathF.Abs(Vector3.Dot(axes[1], axis)) +
               extents[2] * MathF.Abs(Vector3.Dot(axes[2], axis));
    }

    private static bool BoxPlane(
        BoxCollider box, Vector3 posBox, Quaternion rotBox,
        PlaneCollider plane, Vector3 posPlane, Quaternion rotPlane,
        CollisionManifold manifold, bool flip = false)
    {
        var boxCenter = posBox + Vector3.Transform(box.Offset, rotBox);
        var planeNormal = Vector3.Transform(plane.Normal, rotPlane);
        var planePoint = posPlane + planeNormal * plane.Distance;
        
        box.GetAxes(rotBox, out var axisX, out var axisY, out var axisZ);
        
        // Project box extents onto plane normal
        var projRadius = 
            box.HalfExtents.X * MathF.Abs(Vector3.Dot(axisX, planeNormal)) +
            box.HalfExtents.Y * MathF.Abs(Vector3.Dot(axisY, planeNormal)) +
            box.HalfExtents.Z * MathF.Abs(Vector3.Dot(axisZ, planeNormal));
        
        var centerDist = Vector3.Dot(boxCenter - planePoint, planeNormal);
        
        if (centerDist > projRadius)
            return false;
        
        // Generate contacts for vertices below the plane
        var corners = new Vector3[8];
        var he = box.HalfExtents;
        
        corners[0] = new Vector3(-he.X, -he.Y, -he.Z);
        corners[1] = new Vector3(+he.X, -he.Y, -he.Z);
        corners[2] = new Vector3(-he.X, +he.Y, -he.Z);
        corners[3] = new Vector3(+he.X, +he.Y, -he.Z);
        corners[4] = new Vector3(-he.X, -he.Y, +he.Z);
        corners[5] = new Vector3(+he.X, -he.Y, +he.Z);
        corners[6] = new Vector3(-he.X, +he.Y, +he.Z);
        corners[7] = new Vector3(+he.X, +he.Y, +he.Z);
        
        var normal = flip ? -planeNormal : planeNormal;
        
        foreach (var localCorner in corners)
        {
            var worldCorner = boxCenter + Vector3.Transform(localCorner, rotBox);
            var dist = Vector3.Dot(worldCorner - planePoint, planeNormal);
            
            if (dist < 0)
            {
                manifold.AddContact(new CollisionContact
                {
                    PointOnA = flip ? worldCorner - planeNormal * dist : worldCorner,
                    PointOnB = flip ? worldCorner : worldCorner - planeNormal * dist,
                    Normal = normal,
                    Depth = -dist
                });
            }
        }
        
        return manifold.Contacts.Count > 0;
    }

    #endregion

    #region Capsule Collisions

    private static bool CapsuleCapsule(
        CapsuleCollider a, Vector3 posA, Quaternion rotA,
        CapsuleCollider b, Vector3 posB, Quaternion rotB,
        CollisionManifold manifold)
    {
        a.GetEndpoints(posA, rotA, out var a0, out var a1);
        b.GetEndpoints(posB, rotB, out var b0, out var b1);
        
        // Find closest points between the two line segments
        ClosestPointsOnSegments(a0, a1, b0, b1, out var closestA, out var closestB);
        
        var diff = closestB - closestA;
        var distSq = diff.LengthSquared();
        var radiusSum = a.Radius + b.Radius;
        
        if (distSq > radiusSum * radiusSum)
            return false;
        
        var dist = MathF.Sqrt(distSq);
        var normal = dist > Epsilon ? diff / dist : Vector3.UnitY;
        
        manifold.AddContact(new CollisionContact
        {
            PointOnA = closestA + normal * a.Radius,
            PointOnB = closestB - normal * b.Radius,
            Normal = normal,
            Depth = radiusSum - dist
        });
        
        return true;
    }

    private static bool CapsulePlane(
        CapsuleCollider capsule, Vector3 posCapsule, Quaternion rotCapsule,
        PlaneCollider plane, Vector3 posPlane, Quaternion rotPlane,
        CollisionManifold manifold, bool flip = false)
    {
        capsule.GetEndpoints(posCapsule, rotCapsule, out var p0, out var p1);
        var planeNormal = Vector3.Transform(plane.Normal, rotPlane);
        var planePoint = posPlane + planeNormal * plane.Distance;
        
        var normal = flip ? -planeNormal : planeNormal;
        bool hasContact = false;
        
        // Check both endpoints
        var dist0 = Vector3.Dot(p0 - planePoint, planeNormal);
        var dist1 = Vector3.Dot(p1 - planePoint, planeNormal);
        
        if (dist0 < capsule.Radius)
        {
            hasContact = true;
            manifold.AddContact(new CollisionContact
            {
                PointOnA = flip ? p0 - planeNormal * dist0 : p0 - planeNormal * capsule.Radius,
                PointOnB = flip ? p0 - planeNormal * capsule.Radius : p0 - planeNormal * dist0,
                Normal = normal,
                Depth = capsule.Radius - dist0
            });
        }
        
        if (dist1 < capsule.Radius)
        {
            hasContact = true;
            manifold.AddContact(new CollisionContact
            {
                PointOnA = flip ? p1 - planeNormal * dist1 : p1 - planeNormal * capsule.Radius,
                PointOnB = flip ? p1 - planeNormal * capsule.Radius : p1 - planeNormal * dist1,
                Normal = normal,
                Depth = capsule.Radius - dist1
            });
        }
        
        return hasContact;
    }

    #endregion

    #region Utility

    private static void ClosestPointsOnSegments(
        Vector3 a0, Vector3 a1, Vector3 b0, Vector3 b1,
        out Vector3 closestA, out Vector3 closestB)
    {
        var d1 = a1 - a0;
        var d2 = b1 - b0;
        var r = a0 - b0;
        
        var len1Sq = d1.LengthSquared();
        var len2Sq = d2.LengthSquared();
        var f = Vector3.Dot(d2, r);
        
        float s, t;
        
        if (len1Sq < Epsilon && len2Sq < Epsilon)
        {
            // Both segments are points
            closestA = a0;
            closestB = b0;
            return;
        }
        
        if (len1Sq < Epsilon)
        {
            // First segment is a point
            s = 0;
            t = Math.Clamp(f / len2Sq, 0, 1);
        }
        else
        {
            var c = Vector3.Dot(d1, r);
            if (len2Sq < Epsilon)
            {
                // Second segment is a point
                t = 0;
                s = Math.Clamp(-c / len1Sq, 0, 1);
            }
            else
            {
                // General case
                var b = Vector3.Dot(d1, d2);
                var denom = len1Sq * len2Sq - b * b;
                
                if (MathF.Abs(denom) > Epsilon)
                {
                    s = Math.Clamp((b * f - c * len2Sq) / denom, 0, 1);
                }
                else
                {
                    s = 0;
                }
                
                t = (b * s + f) / len2Sq;
                
                if (t < 0)
                {
                    t = 0;
                    s = Math.Clamp(-c / len1Sq, 0, 1);
                }
                else if (t > 1)
                {
                    t = 1;
                    s = Math.Clamp((b - c) / len1Sq, 0, 1);
                }
            }
        }
        
        closestA = a0 + d1 * s;
        closestB = b0 + d2 * t;
    }

    #endregion

    #region Raycasting

    /// <summary>
    /// Result of a raycast query
    /// </summary>
    public struct RaycastHit
    {
        public bool Hit;
        public float Distance;
        public Vector3 Point;
        public Vector3 Normal;
        public Collider Collider;
        public RigidBody Body;
    }

    /// <summary>
    /// Cast a ray against a sphere
    /// </summary>
    public static bool RaycastSphere(
        Vector3 origin, Vector3 direction, float maxDistance,
        SphereCollider sphere, Vector3 position, Quaternion rotation,
        out RaycastHit hit)
    {
        hit = default;
        
        var center = position + Vector3.Transform(sphere.Offset, rotation);
        var m = origin - center;
        
        var b = Vector3.Dot(m, direction);
        var c = Vector3.Dot(m, m) - sphere.Radius * sphere.Radius;
        
        if (c > 0 && b > 0) return false;
        
        var discriminant = b * b - c;
        if (discriminant < 0) return false;
        
        var t = -b - MathF.Sqrt(discriminant);
        if (t < 0) t = 0;
        if (t > maxDistance) return false;
        
        hit.Hit = true;
        hit.Distance = t;
        hit.Point = origin + direction * t;
        hit.Normal = Vector3.Normalize(hit.Point - center);
        hit.Collider = sphere;
        
        return true;
    }

    /// <summary>
    /// Cast a ray against a box
    /// </summary>
    public static bool RaycastBox(
        Vector3 origin, Vector3 direction, float maxDistance,
        BoxCollider box, Vector3 position, Quaternion rotation,
        out RaycastHit hit)
    {
        hit = default;
        
        var center = position + Vector3.Transform(box.Offset, rotation);
        var invRot = Quaternion.Conjugate(rotation);
        
        var localOrigin = Vector3.Transform(origin - center, invRot);
        var localDir = Vector3.Transform(direction, invRot);
        
        var he = box.HalfExtents;
        
        float tMin = 0, tMax = maxDistance;
        int normalAxis = -1;
        float normalSign = 1;
        
        for (int i = 0; i < 3; i++)
        {
            float o = i == 0 ? localOrigin.X : (i == 1 ? localOrigin.Y : localOrigin.Z);
            float d = i == 0 ? localDir.X : (i == 1 ? localDir.Y : localDir.Z);
            float e = i == 0 ? he.X : (i == 1 ? he.Y : he.Z);
            
            if (MathF.Abs(d) < Epsilon)
            {
                if (o < -e || o > e) return false;
            }
            else
            {
                var invD = 1f / d;
                var t1 = (-e - o) * invD;
                var t2 = (e - o) * invD;
                
                var sign = 1f;
                if (t1 > t2)
                {
                    (t1, t2) = (t2, t1);
                    sign = -1f;
                }
                
                if (t1 > tMin)
                {
                    tMin = t1;
                    normalAxis = i;
                    normalSign = sign;
                }
                tMax = MathF.Min(tMax, t2);
                
                if (tMin > tMax) return false;
            }
        }
        
        if (normalAxis < 0) return false;
        
        var localNormal = normalAxis == 0 ? Vector3.UnitX * normalSign :
                         (normalAxis == 1 ? Vector3.UnitY * normalSign : Vector3.UnitZ * normalSign);
        
        hit.Hit = true;
        hit.Distance = tMin;
        hit.Point = origin + direction * tMin;
        hit.Normal = Vector3.Transform(localNormal, rotation);
        hit.Collider = box;
        
        return true;
    }

    /// <summary>
    /// Cast a ray against a plane
    /// </summary>
    public static bool RaycastPlane(
        Vector3 origin, Vector3 direction, float maxDistance,
        PlaneCollider plane, Vector3 position, Quaternion rotation,
        out RaycastHit hit)
    {
        hit = default;
        
        var planeNormal = Vector3.Transform(plane.Normal, rotation);
        var planePoint = position + planeNormal * plane.Distance;
        
        var denom = Vector3.Dot(planeNormal, direction);
        if (MathF.Abs(denom) < Epsilon) return false;
        
        var t = Vector3.Dot(planePoint - origin, planeNormal) / denom;
        if (t < 0 || t > maxDistance) return false;
        
        hit.Hit = true;
        hit.Distance = t;
        hit.Point = origin + direction * t;
        hit.Normal = denom < 0 ? planeNormal : -planeNormal;
        hit.Collider = plane;
        
        return true;
    }

    #endregion
}
