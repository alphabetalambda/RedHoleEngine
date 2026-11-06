using System.Numerics;
using RedHoleEngine.Physics.Collision;

namespace RedHoleEngine.Tests.Physics;

public class ColliderCoverageTests
{
    private const float Epsilon = 0.001f;

    [Fact]
    public void AABB_CenterExtentsSizeSurfaceAreaAndExpand()
    {
        var aabb = new AABB(new Vector3(-1f, -2f, -3f), new Vector3(1f, 2f, 3f));

        Assert.Equal(Vector3.Zero, aabb.Center);
        Assert.Equal(new Vector3(1f, 2f, 3f), aabb.Extents);
        Assert.Equal(new Vector3(2f, 4f, 6f), aabb.Size);
        Assert.Equal(2f * (2f * 4f + 4f * 6f + 6f * 2f), aabb.SurfaceArea(), Epsilon);

        var expanded = aabb.Expand(0.5f);
        Assert.Equal(new Vector3(-1.5f, -2.5f, -3.5f), expanded.Min);
        Assert.Equal(new Vector3(1.5f, 2.5f, 3.5f), expanded.Max);
    }

    [Fact]
    public void AABB_TouchingBoundary_IntersectsAndContainsEdges()
    {
        var a = new AABB(Vector3.Zero, Vector3.One);
        var touching = new AABB(Vector3.One, new Vector3(2f, 2f, 2f));

        Assert.True(a.Intersects(touching));
        Assert.True(a.Contains(Vector3.One));
        Assert.True(a.Contains(Vector3.Zero));
        Assert.False(a.Contains(new Vector3(1.01f, 0f, 0f)));
    }

    [Fact]
    public void PhysicsMaterial_PresetsHaveDistinctResponse()
    {
        Assert.Equal(0.3f, PhysicsMaterial.Default.Restitution);
        Assert.Equal(0.5f, PhysicsMaterial.Default.StaticFriction);
        Assert.Equal(0.4f, PhysicsMaterial.Default.DynamicFriction);

        Assert.Equal(0.9f, PhysicsMaterial.Bouncy.Restitution);
        Assert.Equal(0.1f, PhysicsMaterial.Bouncy.DynamicFriction);
        Assert.True(PhysicsMaterial.Ice.StaticFriction < PhysicsMaterial.Metal.StaticFriction);
        Assert.True(PhysicsMaterial.Rubber.StaticFriction > PhysicsMaterial.Bouncy.StaticFriction);
        Assert.Equal(0.03f, PhysicsMaterial.Ice.DynamicFriction);
        Assert.Equal(0.8f, PhysicsMaterial.Rubber.Restitution);
        Assert.Equal(0.4f, PhysicsMaterial.Metal.DynamicFriction);
    }

    [Fact]
    public void Sphere_DefaultsClosestPointSupportAndBounds()
    {
        var sphere = new SphereCollider();
        Assert.Equal(ColliderType.Sphere, sphere.Type);
        Assert.Equal(0.5f, sphere.Radius);

        sphere.Radius = 2f;
        var closestToCenter = sphere.GetClosestPoint(Vector3.Zero, Vector3.Zero, Quaternion.Identity);
        var closestToPoint = sphere.GetClosestPoint(new Vector3(10f, 0f, 0f), Vector3.Zero, Quaternion.Identity);
        var support = sphere.GetSupport(Vector3.UnitX, new Vector3(1f, 2f, 3f), Quaternion.Identity);
        var bounds = sphere.GetWorldAABB(new Vector3(1f, 2f, 3f), Quaternion.Identity);

        Assert.Equal(new Vector3(2f, 0f, 0f), closestToCenter);
        Assert.Equal(new Vector3(2f, 0f, 0f), closestToPoint);
        Assert.Equal(new Vector3(3f, 2f, 3f), support);
        Assert.Equal(new Vector3(-1f, 0f, 1f), bounds.Min);
        Assert.Equal(new Vector3(3f, 4f, 5f), bounds.Max);
    }

    [Fact]
    public void Sphere_OffsetIsRotatedIntoWorldBounds()
    {
        var sphere = new SphereCollider(1f) { Offset = new Vector3(2f, 0f, 0f) };
        var rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI / 2f);
        var position = new Vector3(4f, 1f, -3f);

        var bounds = sphere.GetWorldAABB(position, rotation);
        var expectedCenter = position + Vector3.Transform(sphere.Offset, rotation);

        Assert.Equal(expectedCenter.X, bounds.Center.X, Epsilon);
        Assert.Equal(expectedCenter.Y, bounds.Center.Y, Epsilon);
        Assert.Equal(expectedCenter.Z, bounds.Center.Z, Epsilon);
        Assert.Equal(0f, expectedCenter.X - position.X, 0.02f);
        Assert.Equal(2f, MathF.Abs(expectedCenter.Z - position.Z), 0.02f);
        Assert.Equal(1f, bounds.Extents.X, Epsilon);
    }

    [Fact]
    public void Box_ConstructorsClosestPointSupportAndAxes()
    {
        var box = new BoxCollider(1.5f, 2f, 0.25f);
        Assert.Equal(ColliderType.Box, box.Type);
        Assert.Equal(new Vector3(1.5f, 2f, 0.25f), box.HalfExtents);

        var defaults = new BoxCollider();
        Assert.Equal(new Vector3(0.5f), defaults.HalfExtents);

        var fromVector = new BoxCollider(new Vector3(1f, 2f, 3f));
        Assert.Equal(new Vector3(1f, 2f, 3f), fromVector.GetClosestPoint(new Vector3(10f, 10f, 10f), Vector3.Zero, Quaternion.Identity));
        Assert.Equal(new Vector3(0.25f, 0.5f, 0f), fromVector.GetClosestPoint(new Vector3(0.25f, 0.5f, 0f), Vector3.Zero, Quaternion.Identity));
        Assert.Equal(new Vector3(0.5f, 2f, 0f), fromVector.GetClosestPoint(new Vector3(0.5f, 9f, 0f), Vector3.Zero, Quaternion.Identity));

        var support = fromVector.GetSupport(new Vector3(1f, -2f, 0f), new Vector3(5f, 0f, 0f), Quaternion.Identity);
        Assert.Equal(new Vector3(6f, -2f, 3f), support);

        var rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI / 2f);
        fromVector.GetAxes(rotation, out var axisX, out var axisY, out var axisZ);
        Assert.Equal(0f, axisX.X, 0.02f);
        Assert.Equal(1f, MathF.Abs(axisX.Z), 0.02f);
        Assert.Equal(1f, axisY.Y, 0.02f);
        Assert.Equal(1f, MathF.Abs(Vector3.Dot(axisZ, Vector3.UnitX)), 0.02f);
        Assert.Equal(0f, Vector3.Dot(axisX, axisY), 0.02f);
    }

    [Fact]
    public void Box_RotatedBoundsGrowOnTheTurnedAxis()
    {
        var box = new BoxCollider(new Vector3(2f, 0.5f, 0.5f));
        var identity = box.GetWorldAABB(Vector3.Zero, Quaternion.Identity);
        var turned = box.GetWorldAABB(Vector3.Zero, Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI / 4f));

        Assert.Equal(4f, identity.Size.X, Epsilon);
        Assert.True(turned.Size.Z > identity.Size.Z);
        Assert.Equal(1f, turned.Size.Y, Epsilon);
    }

    [Fact]
    public void Capsule_EndpointsClosestPointSupportAndAxes()
    {
        var vertical = new CapsuleCollider();
        Assert.Equal(ColliderType.Capsule, vertical.Type);
        Assert.Equal(0.5f, vertical.Radius);
        Assert.Equal(2f, vertical.Height);
        Assert.Equal(1, vertical.Axis);
        Assert.Equal(0.5f, vertical.HalfHeight, Epsilon);

        var alongX = new CapsuleCollider(1f, 4f, axis: 0);
        alongX.GetEndpoints(new Vector3(2f, 3f, 4f), Quaternion.Identity, out var x0, out var x1);
        Assert.Equal(new Vector3(1f, 3f, 4f), x0);
        Assert.Equal(new Vector3(3f, 3f, 4f), x1);

        var alongZ = new CapsuleCollider(1f, 4f, axis: 2) { Offset = new Vector3(0f, 2f, 0f) };
        alongZ.GetEndpoints(Vector3.Zero, Quaternion.Identity, out var z0, out var z1);
        Assert.Equal(0f, z0.X, Epsilon);
        Assert.Equal(2f, z0.Y, Epsilon);
        Assert.Equal(-1f, z0.Z, Epsilon);
        Assert.Equal(1f, z1.Z, Epsilon);

        var surface = alongX.GetClosestPoint(new Vector3(2f, 8f, 4f), new Vector3(2f, 3f, 4f), Quaternion.Identity);
        Assert.Equal(2f, surface.X, Epsilon);
        Assert.Equal(4f, surface.Y, Epsilon);
        Assert.Equal(4f, surface.Z, Epsilon);

        var support = alongX.GetSupport(Vector3.UnitY, new Vector3(2f, 3f, 4f), Quaternion.Identity);
        Assert.Equal(4f, support.Y, Epsilon);
        Assert.True(support.X >= 2f);

        var bounds = alongX.GetWorldAABB(new Vector3(2f, 3f, 4f), Quaternion.Identity);
        Assert.Equal(new Vector3(0f, 2f, 3f), bounds.Min);
        Assert.Equal(new Vector3(4f, 4f, 5f), bounds.Max);
    }

    [Fact]
    public void Capsule_ZeroHalfHeight_BehavesLikeASphere()
    {
        var capsule = new CapsuleCollider(1f, 2f);
        Assert.Equal(0f, capsule.HalfHeight, Epsilon);
        capsule.GetEndpoints(Vector3.Zero, Quaternion.Identity, out var p0, out var p1);
        Assert.Equal(p0, p1);

        var closest = capsule.GetClosestPoint(new Vector3(4f, 0f, 0f), Vector3.Zero, Quaternion.Identity);
        Assert.Equal(new Vector3(1f, 0f, 0f), closest);
    }

    [Fact]
    public void Plane_NormalizesConstructorAndProjectsPoints()
    {
        var plane = new PlaneCollider(new Vector3(0f, 4f, 0f), 2f);
        Assert.Equal(ColliderType.Plane, plane.Type);
        Assert.Equal(Vector3.UnitY, plane.Normal);
        Assert.Equal(2f, plane.Distance);

        Assert.Equal(3f, plane.GetSignedDistance(new Vector3(5f, 5f, -1f), Vector3.Zero, Quaternion.Identity), Epsilon);
        Assert.Equal(-2f, plane.GetSignedDistance(Vector3.Zero, Vector3.Zero, Quaternion.Identity), Epsilon);

        var closest = plane.GetClosestPoint(new Vector3(1f, 9f, 2f), Vector3.Zero, Quaternion.Identity);
        Assert.Equal(new Vector3(1f, 2f, 2f), closest);

        var onNormal = plane.GetSupport(Vector3.UnitY, Vector3.Zero, Quaternion.Identity);
        Assert.Equal(0f, onNormal.X, Epsilon);
        Assert.Equal(2f, onNormal.Y, Epsilon);

        var alongPlane = plane.GetSupport(Vector3.UnitX, Vector3.Zero, Quaternion.Identity);
        Assert.True(alongPlane.X > 9000f);
        Assert.Equal(2f, alongPlane.Y, Epsilon);

        var bounds = plane.GetWorldAABB(Vector3.Zero, Quaternion.Identity);
        Assert.True(bounds.Contains(new Vector3(1000f, -1000f, 1000f)));
        Assert.True(bounds.SurfaceArea() > 1_000_000f);
    }

    [Fact]
    public void Plane_RotatedNormal_ChangesSignedDistance()
    {
        var plane = new PlaneCollider(Vector3.UnitY, 0f);
        var rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, MathF.PI / 2f);

        float distance = plane.GetSignedDistance(new Vector3(3f, 0f, 0f), Vector3.Zero, rotation);

        // A quarter turn about Z lays the Y normal onto X, so the point is one radius away.
        Assert.Equal(3f, MathF.Abs(distance), 0.05f);
    }
}
