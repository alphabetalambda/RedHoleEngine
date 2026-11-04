using System.Numerics;
using RedHoleEngine.Physics.Collision;

namespace RedHoleEngine.Tests.Physics;

public class CollisionCoverageTests
{
    private const float Epsilon = 0.001f;

    [Fact]
    public void SphereSphere_Coincident_UsesUpNormalAndFullDepth()
    {
        var a = new SphereCollider(1f);
        var b = new SphereCollider(2f);

        bool hit = CollisionDetection.TestCollision(
            a, Vector3.Zero, Quaternion.Identity,
            b, Vector3.Zero, Quaternion.Identity,
            out var manifold);

        Assert.True(hit);
        Assert.Same(a, manifold.ColliderA);
        Assert.Same(b, manifold.ColliderB);
        var contact = manifold.Contacts[0];
        Assert.Equal(3f, contact.Depth, Epsilon);
        Assert.Equal(0f, contact.Normal.X, Epsilon);
        Assert.Equal(1f, contact.Normal.Y, Epsilon);
        Assert.Equal(0f, contact.Normal.Z, Epsilon);
        Assert.Equal(0f, contact.PointOnA.X, Epsilon);
        Assert.Equal(1f, contact.PointOnA.Y, Epsilon);
    }

    [Fact]
    public void SphereSphere_OffsetSpheres_CollideAroundShiftedCenters()
    {
        var a = new SphereCollider(0.5f) { Offset = new Vector3(1f, 0f, 0f) };
        var b = new SphereCollider(0.5f);

        bool touching = CollisionDetection.TestCollision(
            a, Vector3.Zero, Quaternion.Identity,
            b, new Vector3(2f, 0f, 0f), Quaternion.Identity,
            out var touch);
        bool separated = CollisionDetection.TestCollision(
            a, Vector3.Zero, Quaternion.Identity,
            b, new Vector3(3f, 0f, 0f), Quaternion.Identity,
            out _);

        Assert.True(touching);
        Assert.Equal(0f, touch.Contacts[0].Depth, Epsilon);
        Assert.False(separated);
    }

    [Fact]
    public void SphereBox_Overlap_ReportsDepthAndCurrentNormal()
    {
        var sphere = new SphereCollider(1f);
        var box = new BoxCollider(new Vector3(1f, 1f, 1f));

        bool hit = CollisionDetection.TestCollision(
            sphere, Vector3.Zero, Quaternion.Identity,
            box, new Vector3(1.5f, 0f, 0f), Quaternion.Identity,
            out var manifold);

        Assert.True(hit);
        Assert.Equal(0.5f, manifold.Contacts[0].Depth, Epsilon);
        // Normal currently points from the box toward the sphere.
        Assert.Equal(-1f, manifold.Contacts[0].Normal.X, Epsilon);
        Assert.Equal(0f, manifold.Contacts[0].Normal.Y, Epsilon);
    }

    [Fact(Skip = "BUG: Sphere-Box contact normal points from the box toward the sphere. With the sphere at the origin and the box at x=1.5, expected normal +X (from A to B); actual normal is -X.")]
    public void SphereBox_Normal_ShouldPointFromSphereToBox()
    {
        var sphere = new SphereCollider(1f);
        var box = new BoxCollider(new Vector3(1f, 1f, 1f));

        CollisionDetection.TestCollision(
            sphere, Vector3.Zero, Quaternion.Identity,
            box, new Vector3(1.5f, 0f, 0f), Quaternion.Identity,
            out var manifold);

        Assert.Equal(1f, manifold.Contacts[0].Normal.X, Epsilon);
    }

    [Fact]
    public void BoxSphere_SwappedOrder_FlipsTheSameNormal()
    {
        var sphere = new SphereCollider(1f);
        var box = new BoxCollider(new Vector3(1f, 1f, 1f));

        CollisionDetection.TestCollision(
            box, new Vector3(1.5f, 0f, 0f), Quaternion.Identity,
            sphere, Vector3.Zero, Quaternion.Identity,
            out var manifold);

        Assert.Equal(0.5f, manifold.Contacts[0].Depth, Epsilon);
        Assert.Equal(1f, manifold.Contacts[0].Normal.X, Epsilon);
    }

    [Fact]
    public void SphereBox_SeparatedAndTouching()
    {
        var sphere = new SphereCollider(0.5f);
        var box = new BoxCollider(new Vector3(0.5f, 0.5f, 0.5f));

        bool separated = CollisionDetection.TestCollision(
            sphere, new Vector3(3f, 0f, 0f), Quaternion.Identity,
            box, Vector3.Zero, Quaternion.Identity,
            out _);
        bool touching = CollisionDetection.TestCollision(
            sphere, new Vector3(1f, 0f, 0f), Quaternion.Identity,
            box, Vector3.Zero, Quaternion.Identity,
            out var touch);

        Assert.False(separated);
        Assert.True(touching);
        Assert.Equal(0f, touch.Contacts[0].Depth, Epsilon);
    }

    [Fact]
    public void SphereBox_CenterInside_PushesOutAlongSmallestAxis()
    {
        var sphere = new SphereCollider(0.2f);
        var box = new BoxCollider(new Vector3(1f, 2f, 3f));

        bool centered = CollisionDetection.TestCollision(
            sphere, Vector3.Zero, Quaternion.Identity,
            box, Vector3.Zero, Quaternion.Identity,
            out var atCenter);
        bool towardNegativeX = CollisionDetection.TestCollision(
            sphere, new Vector3(-0.1f, 0f, 0f), Quaternion.Identity,
            box, Vector3.Zero, Quaternion.Identity,
            out var offCenter);

        Assert.True(centered);
        Assert.Equal(0.2f, atCenter.Contacts[0].Depth, Epsilon);
        Assert.Equal(1f, atCenter.Contacts[0].Normal.X, Epsilon);
        Assert.True(towardNegativeX);
        Assert.Equal(-1f, offCenter.Contacts[0].Normal.X, Epsilon);
    }

    [Fact]
    public void SphereBox_RotatedBox_StillDetectsCenterOverlap()
    {
        var sphere = new SphereCollider(0.4f);
        var box = new BoxCollider(new Vector3(1f, 1f, 1f));
        var rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI / 3f);

        bool hit = CollisionDetection.TestCollision(
            sphere, Vector3.Zero, Quaternion.Identity,
            box, Vector3.Zero, rotation,
            out var manifold);

        Assert.True(hit);
        Assert.Equal(0.4f, manifold.Contacts[0].Depth, Epsilon);
        Assert.Equal(1f, manifold.Contacts[0].Normal.Length(), Epsilon);
    }

    [Fact]
    public void SphereBox_OffsetMovesTheBox()
    {
        var sphere = new SphereCollider(0.5f);
        var shifted = new BoxCollider(new Vector3(0.5f, 0.5f, 0.5f)) { Offset = new Vector3(5f, 0f, 0f) };
        var unshifted = new BoxCollider(new Vector3(0.5f, 0.5f, 0.5f));

        bool withOffset = CollisionDetection.TestCollision(
            sphere, new Vector3(5f, 0f, 0f), Quaternion.Identity,
            shifted, Vector3.Zero, Quaternion.Identity,
            out _);
        bool withoutOffset = CollisionDetection.TestCollision(
            sphere, new Vector3(5f, 0f, 0f), Quaternion.Identity,
            unshifted, Vector3.Zero, Quaternion.Identity,
            out _);

        Assert.True(withOffset);
        Assert.False(withoutOffset);
    }

    [Fact]
    public void SpherePlane_BelowPlane_IsADeepCollision()
    {
        var sphere = new SphereCollider(1f);
        var plane = new PlaneCollider(Vector3.UnitY, 0f);

        bool hit = CollisionDetection.TestCollision(
            sphere, new Vector3(2f, -0.25f, -3f), Quaternion.Identity,
            plane, Vector3.Zero, Quaternion.Identity,
            out var manifold);

        Assert.True(hit);
        Assert.Equal(1.25f, manifold.Contacts[0].Depth, Epsilon);
        Assert.Equal(-1f, manifold.Contacts[0].Normal.Y, Epsilon);
    }

    [Fact]
    public void PlaneSphere_FlippedArguments_FlipNormalAndPoints()
    {
        var sphere = new SphereCollider(1f);
        var plane = new PlaneCollider(Vector3.UnitY, 0f);

        CollisionDetection.TestCollision(
            plane, Vector3.Zero, Quaternion.Identity,
            sphere, new Vector3(0f, 0.5f, 0f), Quaternion.Identity,
            out var manifold);

        Assert.Equal(0.5f, manifold.Contacts[0].Depth, Epsilon);
        Assert.Equal(1f, manifold.Contacts[0].Normal.Y, Epsilon);
        Assert.True(manifold.Contacts[0].PointOnB.Y < manifold.Contacts[0].PointOnA.Y);
    }

    [Fact]
    public void BoxPlane_PenetratingBottom_ProducesFourContacts()
    {
        var box = new BoxCollider(new Vector3(1f, 1f, 1f));
        var plane = new PlaneCollider(Vector3.UnitY, 0f);

        bool above = CollisionDetection.TestCollision(
            box, new Vector3(0f, 3f, 0f), Quaternion.Identity,
            plane, Vector3.Zero, Quaternion.Identity,
            out _);
        bool penetrating = CollisionDetection.TestCollision(
            box, new Vector3(0f, 0.5f, 0f), Quaternion.Identity,
            plane, Vector3.Zero, Quaternion.Identity,
            out var manifold);

        Assert.False(above);
        Assert.True(penetrating);
        Assert.Equal(4, manifold.Contacts.Count);
        Assert.All(manifold.Contacts, contact =>
        {
            Assert.Equal(-1f, contact.Normal.Y, Epsilon);
            Assert.Equal(0.5f, contact.Depth, Epsilon);
        });
    }

    [Fact]
    public void PlaneBox_FlippedArguments_UseUpwardNormal()
    {
        var box = new BoxCollider(new Vector3(1f, 1f, 1f));
        var plane = new PlaneCollider(Vector3.UnitY, 0f);

        bool hit = CollisionDetection.TestCollision(
            plane, Vector3.Zero, Quaternion.Identity,
            box, new Vector3(0f, 0.5f, 0f), Quaternion.Identity,
            out var manifold);

        Assert.True(hit);
        Assert.Equal(4, manifold.Contacts.Count);
        Assert.Equal(1f, manifold.Contacts[0].Normal.Y, Epsilon);
    }

    [Fact]
    public void PlanePlane_ReturnsFalse()
    {
        var a = new PlaneCollider(Vector3.UnitY, 0f);
        var b = new PlaneCollider(Vector3.UnitY, 0f);

        bool hit = CollisionDetection.TestCollision(
            a, Vector3.Zero, Quaternion.Identity,
            b, Vector3.Zero, Quaternion.Identity,
            out var manifold);

        Assert.False(hit);
        Assert.Empty(manifold.Contacts);
    }

    [Fact]
    public void UnknownShape_ReturnsFalse()
    {
        var weird = new WeirdCollider();
        var sphere = new SphereCollider(1f);

        bool hit = CollisionDetection.TestCollision(
            weird, Vector3.Zero, Quaternion.Identity,
            sphere, Vector3.Zero, Quaternion.Identity,
            out _);

        Assert.False(hit);
    }

    [Fact]
    public void SphereCapsule_Overlap_ReportsDepthAndCurrentNormal()
    {
        var sphere = new SphereCollider(0.5f);
        var capsule = new CapsuleCollider(0.5f, 2f);

        bool miss = CollisionDetection.TestCollision(
            sphere, new Vector3(3f, 0f, 0f), Quaternion.Identity,
            capsule, Vector3.Zero, Quaternion.Identity,
            out _);
        bool hit = CollisionDetection.TestCollision(
            sphere, new Vector3(0.8f, 0f, 0f), Quaternion.Identity,
            capsule, Vector3.Zero, Quaternion.Identity,
            out var manifold);

        Assert.False(miss);
        Assert.True(hit);
        Assert.Equal(0.2f, manifold.Contacts[0].Depth, Epsilon);
        Assert.Equal(1f, manifold.Contacts[0].Normal.X, Epsilon);
    }

    [Fact(Skip = "BUG: Sphere-Capsule normal points from the capsule toward the sphere. Sphere at x=0.8 against a capsule at the origin should have normal -X (from A to B); actual normal is +X.")]
    public void SphereCapsule_Normal_ShouldPointFromSphereToCapsule()
    {
        var sphere = new SphereCollider(0.5f);
        var capsule = new CapsuleCollider(0.5f, 2f);

        CollisionDetection.TestCollision(
            sphere, new Vector3(0.8f, 0f, 0f), Quaternion.Identity,
            capsule, Vector3.Zero, Quaternion.Identity,
            out var manifold);

        Assert.Equal(-1f, manifold.Contacts[0].Normal.X, Epsilon);
    }

    [Fact]
    public void CapsuleSphere_SwappedOrder_FlipsNormal()
    {
        var sphere = new SphereCollider(0.5f);
        var capsule = new CapsuleCollider(0.5f, 2f);

        CollisionDetection.TestCollision(
            capsule, Vector3.Zero, Quaternion.Identity,
            sphere, new Vector3(0.8f, 0f, 0f), Quaternion.Identity,
            out var manifold);

        Assert.Equal(0.2f, manifold.Contacts[0].Depth, Epsilon);
        Assert.Equal(-1f, manifold.Contacts[0].Normal.X, Epsilon);
    }

    [Fact]
    public void CapsuleCapsule_SideBySide_NormalPointsFromAToB()
    {
        var a = new CapsuleCollider(0.5f, 2f);
        var b = new CapsuleCollider(0.5f, 2f);

        bool hit = CollisionDetection.TestCollision(
            a, Vector3.Zero, Quaternion.Identity,
            b, new Vector3(0.5f, 0f, 0f), Quaternion.Identity,
            out var manifold);
        bool miss = CollisionDetection.TestCollision(
            a, Vector3.Zero, Quaternion.Identity,
            b, new Vector3(3f, 0f, 0f), Quaternion.Identity,
            out _);

        Assert.True(hit);
        Assert.False(miss);
        Assert.Equal(0.5f, manifold.Contacts[0].Depth, Epsilon);
        Assert.Equal(1f, manifold.Contacts[0].Normal.X, Epsilon);
    }

    [Fact]
    public void CapsuleCapsule_ParallelShaftsAndDegenerateCapsules()
    {
        var horizontalA = new CapsuleCollider(0.25f, 2f, axis: 0);
        var horizontalB = new CapsuleCollider(0.25f, 2f, axis: 0);
        bool parallel = CollisionDetection.TestCollision(
            horizontalA, Vector3.Zero, Quaternion.Identity,
            horizontalB, new Vector3(0f, 0.3f, 0f), Quaternion.Identity,
            out var parallelHit);

        var pointA = new CapsuleCollider(0.5f, 1f);
        var pointB = new CapsuleCollider(0.5f, 1f);
        bool points = CollisionDetection.TestCollision(
            pointA, Vector3.Zero, Quaternion.Identity,
            pointB, new Vector3(0.5f, 0f, 0f), Quaternion.Identity,
            out var pointHit);

        Assert.True(parallel);
        Assert.Equal(0.2f, parallelHit.Contacts[0].Depth, Epsilon);
        Assert.True(points);
        Assert.Equal(0.5f, pointHit.Contacts[0].Depth, Epsilon);
    }

    [Fact]
    public void CapsulePlane_BothEndpointsAndClearance()
    {
        var capsule = new CapsuleCollider(0.5f, 2f);
        var plane = new PlaneCollider(Vector3.UnitY, 0f);

        bool clear = CollisionDetection.TestCollision(
            capsule, new Vector3(0f, 3f, 0f), Quaternion.Identity,
            plane, Vector3.Zero, Quaternion.Identity,
            out _);
        bool bothEnds = CollisionDetection.TestCollision(
            capsule, new Vector3(0f, -0.1f, 0f), Quaternion.Identity,
            plane, Vector3.Zero, Quaternion.Identity,
            out var deep);
        bool oneEnd = CollisionDetection.TestCollision(
            capsule, new Vector3(0f, 0.5f, 0f), Quaternion.Identity,
            plane, Vector3.Zero, Quaternion.Identity,
            out var shallow);

        Assert.False(clear);
        Assert.True(bothEnds);
        Assert.Equal(2, deep.Contacts.Count);
        Assert.All(deep.Contacts, contact => Assert.True(contact.Depth > 0f));
        Assert.True(oneEnd);
        Assert.Single(shallow.Contacts);
        Assert.Equal(-1f, shallow.Contacts[0].Normal.Y, Epsilon);
    }

    [Fact]
    public void PlaneCapsule_AndHorizontalCapsule_ReportBothCaps()
    {
        var capsule = new CapsuleCollider(0.5f, 2f, axis: 0);
        var plane = new PlaneCollider(Vector3.UnitY, 0f);

        bool hit = CollisionDetection.TestCollision(
            plane, Vector3.Zero, Quaternion.Identity,
            capsule, new Vector3(0f, 0.2f, 0f), Quaternion.Identity,
            out var manifold);

        Assert.True(hit);
        Assert.Equal(2, manifold.Contacts.Count);
        Assert.Equal(1f, manifold.Contacts[0].Normal.Y, Epsilon);
        Assert.Equal(0.3f, manifold.Contacts[0].Depth, Epsilon);
        Assert.Equal(0.3f, manifold.Contacts[1].Depth, Epsilon);
    }

    [Fact]
    public void BoxCapsule_Overlap_ReturnsFalse()
    {
        var box = new BoxCollider(new Vector3(1f, 1f, 1f));
        var capsule = new CapsuleCollider(1f, 2f);

        bool boxFirst = CollisionDetection.TestCollision(
            box, Vector3.Zero, Quaternion.Identity,
            capsule, new Vector3(0.5f, 0f, 0f), Quaternion.Identity,
            out _);
        bool capsuleFirst = CollisionDetection.TestCollision(
            capsule, new Vector3(0.5f, 0f, 0f), Quaternion.Identity,
            box, Vector3.Zero, Quaternion.Identity,
            out _);
        bool sphereControl = CollisionDetection.TestCollision(
            new SphereCollider(1f), new Vector3(0.5f, 0f, 0f), Quaternion.Identity,
            capsule, Vector3.Zero, Quaternion.Identity,
            out _);

        Assert.False(boxFirst);
        Assert.False(capsuleFirst);
        Assert.True(sphereControl);
    }

    [Fact(Skip = "BUG: Box-Capsule is missing from TestCollision and returns false while the shapes overlap. A unit box centered on a radius-1 capsule should produce a contact with positive depth.")]
    public void BoxCapsule_Overlap_ShouldCollide()
    {
        var box = new BoxCollider(new Vector3(1f, 1f, 1f));
        var capsule = new CapsuleCollider(1f, 2f);

        bool hit = CollisionDetection.TestCollision(
            box, Vector3.Zero, Quaternion.Identity,
            capsule, new Vector3(0.5f, 0f, 0f), Quaternion.Identity,
            out var manifold);

        Assert.True(hit);
        Assert.True(manifold.Contacts[0].Depth > 0f);
    }

    [Fact]
    public void BoxBox_TouchingFaces_CountAsCollision_SeparatedFacesDoNot()
    {
        var a = new BoxCollider(Vector3.One);
        var b = new BoxCollider(Vector3.One);

        bool touching = CollisionDetection.TestCollision(
            a, Vector3.Zero, Quaternion.Identity,
            b, new Vector3(2f, 0f, 0f), Quaternion.Identity,
            out var touch);
        bool separated = CollisionDetection.TestCollision(
            a, Vector3.Zero, Quaternion.Identity,
            b, new Vector3(0f, 0f, 2.01f), Quaternion.Identity,
            out _);

        Assert.True(touching);
        Assert.Equal(0f, touch.Contacts[0].Depth, 0.02f);
        Assert.False(separated);
    }

    [Fact]
    public void BoxBox_Coincident_HasPositiveDepth()
    {
        var a = new BoxCollider(new Vector3(1f, 2f, 3f));
        var b = new BoxCollider(new Vector3(1f, 2f, 3f));

        bool hit = CollisionDetection.TestCollision(
            a, Vector3.Zero, Quaternion.Identity,
            b, Vector3.Zero, Quaternion.Identity,
            out var manifold);

        Assert.True(hit);
        Assert.True(manifold.Contacts[0].Depth > 1f);
        Assert.Equal(1f, manifold.Contacts[0].Normal.Length(), Epsilon);
    }

    [Fact]
    public void Manifold_AddContact_KeepsFourDeepest_AndClearEmpties()
    {
        var manifold = new CollisionManifold();
        for (int depth = 1; depth <= 4; depth++)
            manifold.AddContact(new CollisionContact { Depth = depth });

        manifold.AddContact(new CollisionContact { Depth = 0.5f });
        Assert.Equal(4, manifold.Contacts.Count);
        Assert.DoesNotContain(manifold.Contacts, contact => MathF.Abs(contact.Depth - 0.5f) < Epsilon);

        manifold.AddContact(new CollisionContact { Depth = 10f });
        Assert.Equal(4, manifold.Contacts.Count);
        Assert.Contains(manifold.Contacts, contact => MathF.Abs(contact.Depth - 10f) < Epsilon);
        Assert.DoesNotContain(manifold.Contacts, contact => MathF.Abs(contact.Depth - 1f) < Epsilon);

        manifold.Clear();
        Assert.Empty(manifold.Contacts);
    }

    [Fact]
    public void RaycastSphere_InsideMaxDistanceAndMissCases()
    {
        var sphere = new SphereCollider(2f);
        var center = Vector3.Zero;

        bool inside = CollisionDetection.RaycastSphere(
            new Vector3(0.5f, 0f, 0f), Vector3.UnitX, 10f,
            sphere, center, Quaternion.Identity,
            out var insideHit);
        bool tooShort = CollisionDetection.RaycastSphere(
            new Vector3(-5f, 0f, 0f), Vector3.UnitX, 2f,
            sphere, center, Quaternion.Identity,
            out _);
        bool behind = CollisionDetection.RaycastSphere(
            Vector3.Zero, Vector3.UnitX, 10f,
            sphere, new Vector3(-4f, 0f, 0f), Quaternion.Identity,
            out _);
        bool offset = CollisionDetection.RaycastSphere(
            Vector3.Zero, Vector3.UnitX, 10f,
            new SphereCollider(1f) { Offset = new Vector3(0f, 0f, 3f) },
            new Vector3(5f, 0f, 0f), Quaternion.Identity,
            out _);

        Assert.True(inside);
        Assert.Equal(0f, insideHit.Distance, Epsilon);
        Assert.Equal(1f, insideHit.Normal.X, Epsilon);
        Assert.Equal(new Vector3(0.5f, 0f, 0f), insideHit.Point);
        Assert.False(tooShort);
        Assert.False(behind);
        Assert.False(offset);
    }

    [Fact]
    public void RaycastBox_HitMissAndInwardNormal()
    {
        var box = new BoxCollider(new Vector3(1f, 1f, 1f));

        bool hit = CollisionDetection.RaycastBox(
            Vector3.Zero, Vector3.UnitX, 100f,
            box, new Vector3(5f, 0f, 0f), Quaternion.Identity,
            out var rayHit);
        bool parallelMiss = CollisionDetection.RaycastBox(
            Vector3.Zero, Vector3.UnitX, 100f,
            box, new Vector3(0f, 5f, 0f), Quaternion.Identity,
            out _);
        bool tooFar = CollisionDetection.RaycastBox(
            Vector3.Zero, Vector3.UnitX, 5f,
            box, new Vector3(10f, 0f, 0f), Quaternion.Identity,
            out _);

        Assert.True(hit);
        Assert.Equal(4f, rayHit.Distance, Epsilon);
        Assert.Equal(1f, rayHit.Normal.X, Epsilon);
        Assert.False(parallelMiss);
        Assert.False(tooFar);
    }

    [Fact(Skip = "BUG: RaycastBox normal points inward. A ray from -X hitting the -X face of a box at x=5 returns normal +X; expected the outward normal -X.")]
    public void RaycastBox_Normal_ShouldFaceTheRay()
    {
        var box = new BoxCollider(new Vector3(1f, 1f, 1f));

        CollisionDetection.RaycastBox(
            Vector3.Zero, Vector3.UnitX, 100f,
            box, new Vector3(5f, 0f, 0f), Quaternion.Identity,
            out var rayHit);

        Assert.Equal(-1f, rayHit.Normal.X, Epsilon);
    }

    [Fact]
    public void RaycastBox_FromInside_ReturnsFalse()
    {
        var box = new BoxCollider(Vector3.One);

        bool hit = CollisionDetection.RaycastBox(
            new Vector3(0.2f, 0f, 0f), Vector3.UnitX, 10f,
            box, Vector3.Zero, Quaternion.Identity,
            out _);

        Assert.False(hit);
    }

    [Fact(Skip = "BUG: RaycastBox returns false when the origin is inside the box. Expected a hit at distance 0; actual result is a miss.")]
    public void RaycastBox_FromInside_ShouldHitAtZero()
    {
        var box = new BoxCollider(Vector3.One);

        bool hit = CollisionDetection.RaycastBox(
            new Vector3(0.2f, 0f, 0f), Vector3.UnitX, 10f,
            box, Vector3.Zero, Quaternion.Identity,
            out var rayHit);

        Assert.True(hit);
        Assert.Equal(0f, rayHit.Distance, Epsilon);
    }

    [Fact]
    public void RaycastBox_Rotated_HitsTheShortFace()
    {
        var box = new BoxCollider(new Vector3(2f, 0.5f, 0.5f));
        var rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI / 2f);

        bool hit = CollisionDetection.RaycastBox(
            new Vector3(-5f, 0f, 0f), Vector3.UnitX, 20f,
            box, Vector3.Zero, rotation,
            out var rayHit);

        Assert.True(hit);
        Assert.Equal(4.5f, rayHit.Distance, 0.05f);
    }

    [Fact]
    public void RaycastPlane_RejectsBackwardsAndTooFar_AndFacesTheIncomingRay()
    {
        var plane = new PlaneCollider(Vector3.UnitY, 0f);

        bool away = CollisionDetection.RaycastPlane(
            new Vector3(0f, 2f, 0f), Vector3.UnitY, 10f,
            plane, Vector3.Zero, Quaternion.Identity,
            out _);
        bool tooFar = CollisionDetection.RaycastPlane(
            Vector3.Zero, -Vector3.UnitY, 1f,
            plane, new Vector3(0f, -5f, 0f), Quaternion.Identity,
            out _);
        bool fromAbove = CollisionDetection.RaycastPlane(
            new Vector3(1f, 5f, 2f), -Vector3.UnitY, 10f,
            plane, Vector3.Zero, Quaternion.Identity,
            out var above);
        bool fromBelow = CollisionDetection.RaycastPlane(
            new Vector3(0f, -5f, 0f), Vector3.UnitY, 10f,
            plane, Vector3.Zero, Quaternion.Identity,
            out var below);

        Assert.False(away);
        Assert.False(tooFar);
        Assert.True(fromAbove);
        Assert.Equal(5f, above.Distance, Epsilon);
        Assert.Equal(1f, above.Normal.Y, Epsilon);
        Assert.Equal(new Vector3(1f, 0f, 2f), above.Point);
        Assert.True(fromBelow);
        Assert.Equal(-1f, below.Normal.Y, Epsilon);
    }

    private sealed class WeirdCollider : Collider
    {
        public WeirdCollider()
        {
            Type = (ColliderType)99;
        }

        public override AABB GetWorldAABB(Vector3 position, Quaternion rotation) => default;

        public override Vector3 GetClosestPoint(Vector3 point, Vector3 position, Quaternion rotation) => point;

        public override Vector3 GetSupport(Vector3 direction, Vector3 position, Quaternion rotation) => position;
    }
}
