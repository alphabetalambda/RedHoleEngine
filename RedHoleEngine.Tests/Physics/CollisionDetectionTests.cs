using System.Numerics;
using RedHoleEngine.Physics;
using RedHoleEngine.Physics.Collision;

namespace RedHoleEngine.Tests.Physics;

public class CollisionDetectionTests
{
    private const float Epsilon = 0.001f;

    #region Sphere-Sphere Tests

    [Fact]
    public void SphereSphere_Overlapping_ReturnsTrue()
    {
        var sphereA = new SphereCollider(1f);
        var sphereB = new SphereCollider(1f);
        
        var posA = Vector3.Zero;
        var posB = new Vector3(1.5f, 0f, 0f); // Distance = 1.5, combined radii = 2
        
        bool result = CollisionDetection.TestCollision(
            sphereA, posA, Quaternion.Identity,
            sphereB, posB, Quaternion.Identity,
            out var manifold);
        
        Assert.True(result);
        Assert.Single(manifold.Contacts);
        Assert.True(manifold.Contacts[0].Depth > 0);
    }

    [Fact]
    public void SphereSphere_NotOverlapping_ReturnsFalse()
    {
        var sphereA = new SphereCollider(1f);
        var sphereB = new SphereCollider(1f);
        
        var posA = Vector3.Zero;
        var posB = new Vector3(3f, 0f, 0f); // Distance = 3, combined radii = 2
        
        bool result = CollisionDetection.TestCollision(
            sphereA, posA, Quaternion.Identity,
            sphereB, posB, Quaternion.Identity,
            out _);
        
        Assert.False(result);
    }

    [Fact]
    public void SphereSphere_JustTouching_ReturnsTrue()
    {
        var sphereA = new SphereCollider(1f);
        var sphereB = new SphereCollider(1f);
        
        var posA = Vector3.Zero;
        var posB = new Vector3(2f, 0f, 0f); // Exactly touching
        
        bool result = CollisionDetection.TestCollision(
            sphereA, posA, Quaternion.Identity,
            sphereB, posB, Quaternion.Identity,
            out var manifold);
        
        Assert.True(result);
        Assert.Equal(0f, manifold.Contacts[0].Depth, Epsilon);
    }

    [Fact]
    public void SphereSphere_ContactNormalPointsFromAToB()
    {
        var sphereA = new SphereCollider(1f);
        var sphereB = new SphereCollider(1f);
        
        var posA = Vector3.Zero;
        var posB = new Vector3(1f, 0f, 0f);
        
        CollisionDetection.TestCollision(
            sphereA, posA, Quaternion.Identity,
            sphereB, posB, Quaternion.Identity,
            out var manifold);
        
        // Normal should point from A to B (positive X)
        Assert.Equal(1f, manifold.Contacts[0].Normal.X, Epsilon);
        Assert.Equal(0f, manifold.Contacts[0].Normal.Y, Epsilon);
        Assert.Equal(0f, manifold.Contacts[0].Normal.Z, Epsilon);
    }

    #endregion

    #region Sphere-Plane Tests

    [Fact]
    public void SpherePlane_SphereAbovePlane_NoCollision()
    {
        var sphere = new SphereCollider(1f);
        var plane = new PlaneCollider(Vector3.UnitY, 0f); // Pointing up
        
        var spherePos = new Vector3(0f, 2f, 0f); // 2 units above, radius 1
        var planePos = Vector3.Zero;
        
        bool result = CollisionDetection.TestCollision(
            sphere, spherePos, Quaternion.Identity,
            plane, planePos, Quaternion.Identity,
            out _);
        
        Assert.False(result);
    }

    [Fact]
    public void SpherePlane_SpherePenetratingPlane_Collision()
    {
        var sphere = new SphereCollider(1f);
        var plane = new PlaneCollider(Vector3.UnitY, 0f);
        
        var spherePos = new Vector3(0f, 0.5f, 0f); // Center 0.5 above plane, radius 1 = penetrating 0.5
        var planePos = Vector3.Zero;
        
        bool result = CollisionDetection.TestCollision(
            sphere, spherePos, Quaternion.Identity,
            plane, planePos, Quaternion.Identity,
            out var manifold);
        
        Assert.True(result);
        Assert.Equal(0.5f, manifold.Contacts[0].Depth, Epsilon);
    }

    [Fact]
    public void SpherePlane_SphereOnPlane_Collision()
    {
        var sphere = new SphereCollider(1f);
        var plane = new PlaneCollider(Vector3.UnitY, 0f);
        
        var spherePos = new Vector3(0f, 1f, 0f); // Just resting on plane
        var planePos = Vector3.Zero;
        
        bool result = CollisionDetection.TestCollision(
            sphere, spherePos, Quaternion.Identity,
            plane, planePos, Quaternion.Identity,
            out var manifold);
        
        Assert.True(result);
        Assert.Equal(0f, manifold.Contacts[0].Depth, Epsilon);
    }

    #endregion

    #region Box-Box Tests

    [Fact]
    public void BoxBox_Overlapping_ReturnsTrue()
    {
        var boxA = new BoxCollider(new Vector3(1f, 1f, 1f));
        var boxB = new BoxCollider(new Vector3(1f, 1f, 1f));
        
        var posA = Vector3.Zero;
        var posB = new Vector3(1.5f, 0f, 0f); // Overlap of 0.5
        
        bool result = CollisionDetection.TestCollision(
            boxA, posA, Quaternion.Identity,
            boxB, posB, Quaternion.Identity,
            out var manifold);
        
        Assert.True(result);
    }

    [Fact]
    public void BoxBox_NotOverlapping_ReturnsFalse()
    {
        var boxA = new BoxCollider(new Vector3(1f, 1f, 1f));
        var boxB = new BoxCollider(new Vector3(1f, 1f, 1f));
        
        var posA = Vector3.Zero;
        var posB = new Vector3(3f, 0f, 0f); // Gap of 1
        
        bool result = CollisionDetection.TestCollision(
            boxA, posA, Quaternion.Identity,
            boxB, posB, Quaternion.Identity,
            out _);
        
        Assert.False(result);
    }

    [Fact]
    public void BoxBox_RotatedOverlap_ReturnsTrue()
    {
        var boxA = new BoxCollider(new Vector3(1f, 1f, 1f));
        var boxB = new BoxCollider(new Vector3(1f, 1f, 1f));
        
        var posA = Vector3.Zero;
        var posB = new Vector3(1.8f, 0f, 0f);
        var rotB = Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI / 4f); // 45 degree rotation
        
        // Rotated box has diagonal ~1.41, so should still overlap
        bool result = CollisionDetection.TestCollision(
            boxA, posA, Quaternion.Identity,
            boxB, posB, rotB,
            out _);
        
        Assert.True(result);
    }

    #endregion

    #region AABB Tests

    [Fact]
    public void AABB_Overlapping_ReturnsTrue()
    {
        var aabbA = new AABB(new Vector3(-1f, -1f, -1f), new Vector3(1f, 1f, 1f));
        var aabbB = new AABB(new Vector3(0f, 0f, 0f), new Vector3(2f, 2f, 2f));
        
        Assert.True(aabbA.Intersects(aabbB));
    }

    [Fact]
    public void AABB_NotOverlapping_ReturnsFalse()
    {
        var aabbA = new AABB(new Vector3(-1f, -1f, -1f), new Vector3(1f, 1f, 1f));
        var aabbB = new AABB(new Vector3(2f, 2f, 2f), new Vector3(3f, 3f, 3f));
        
        Assert.False(aabbA.Intersects(aabbB));
    }

    [Fact]
    public void AABB_ContainsPoint_ReturnsTrue()
    {
        var aabb = new AABB(new Vector3(-1f, -1f, -1f), new Vector3(1f, 1f, 1f));
        
        Assert.True(aabb.Contains(Vector3.Zero));
        Assert.True(aabb.Contains(new Vector3(0.5f, 0.5f, 0.5f)));
    }

    [Fact]
    public void AABB_DoesNotContainPoint_ReturnsFalse()
    {
        var aabb = new AABB(new Vector3(-1f, -1f, -1f), new Vector3(1f, 1f, 1f));
        
        Assert.False(aabb.Contains(new Vector3(2f, 0f, 0f)));
    }

    [Fact]
    public void AABB_Merge_ExpandsToContainBoth()
    {
        var aabbA = new AABB(new Vector3(0f, 0f, 0f), new Vector3(1f, 1f, 1f));
        var aabbB = new AABB(new Vector3(2f, 2f, 2f), new Vector3(3f, 3f, 3f));
        
        var merged = AABB.Merge(aabbA, aabbB);
        
        Assert.Equal(new Vector3(0f, 0f, 0f), merged.Min);
        Assert.Equal(new Vector3(3f, 3f, 3f), merged.Max);
    }

    #endregion

    #region Raycast Tests

    [Fact]
    public void RaycastSphere_HitsCenter_ReturnsTrue()
    {
        var sphere = new SphereCollider(1f);
        var pos = new Vector3(5f, 0f, 0f);
        
        var rayOrigin = Vector3.Zero;
        var rayDir = Vector3.UnitX;
        
        bool hit = CollisionDetection.RaycastSphere(
            rayOrigin, rayDir, 100f,
            sphere, pos, Quaternion.Identity,
            out var raycastHit);
        
        Assert.True(hit);
        Assert.Equal(4f, raycastHit.Distance, Epsilon); // 5 - 1 = 4
        Assert.Equal(-1f, raycastHit.Normal.X, Epsilon); // Normal points back at ray
    }

    [Fact]
    public void RaycastSphere_Misses_ReturnsFalse()
    {
        var sphere = new SphereCollider(1f);
        var pos = new Vector3(5f, 3f, 0f); // Offset in Y
        
        var rayOrigin = Vector3.Zero;
        var rayDir = Vector3.UnitX;
        
        bool hit = CollisionDetection.RaycastSphere(
            rayOrigin, rayDir, 100f,
            sphere, pos, Quaternion.Identity,
            out _);
        
        Assert.False(hit);
    }

    [Fact]
    public void RaycastPlane_HitsPlane_ReturnsTrue()
    {
        var plane = new PlaneCollider(Vector3.UnitY, 0f);
        var planePos = new Vector3(0f, -5f, 0f);
        
        var rayOrigin = Vector3.Zero;
        var rayDir = -Vector3.UnitY; // Pointing down
        
        bool hit = CollisionDetection.RaycastPlane(
            rayOrigin, rayDir, 100f,
            plane, planePos, Quaternion.Identity,
            out var raycastHit);
        
        Assert.True(hit);
        Assert.Equal(5f, raycastHit.Distance, Epsilon);
    }

    [Fact]
    public void RaycastPlane_ParallelRay_ReturnsFalse()
    {
        var plane = new PlaneCollider(Vector3.UnitY, 0f);
        var planePos = Vector3.Zero;
        
        var rayOrigin = new Vector3(0f, 1f, 0f);
        var rayDir = Vector3.UnitX; // Parallel to plane
        
        bool hit = CollisionDetection.RaycastPlane(
            rayOrigin, rayDir, 100f,
            plane, planePos, Quaternion.Identity,
            out _);
        
        Assert.False(hit);
    }

    #endregion
}
