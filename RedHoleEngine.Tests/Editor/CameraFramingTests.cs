using System.Numerics;
using RedHoleEngine.Components;
using RedHoleEngine.Core.ECS;
using RedHoleEngine.Editor.Viewport;
using RedHoleEngine.Engine;

namespace RedHoleEngine.Tests.Editor;

public class CameraFramingTests
{
    private const float Tolerance = 1e-3f;

    [Fact]
    public void FramingDistance_FitsSphereInFieldOfView()
    {
        // 90 degree FOV: sin(45) = 0.7071, radius 10 => 14.14, times margin 1.5
        var distance = CameraFraming.FramingDistance(10f, 90f, margin: 1.5f, minDistance: 0f);
        Assert.Equal(10f / MathF.Sin(MathF.PI / 4f) * 1.5f, distance, Tolerance);
    }

    [Fact]
    public void FramingDistance_RespectsMinimum()
    {
        Assert.Equal(2f * 1.5f, CameraFraming.FramingDistance(0.01f, 60f), Tolerance);
    }

    [Fact]
    public void LookAtAngles_MatchCameraForward()
    {
        var from = new Vector3(3, 4, -2);
        var to = new Vector3(-5, 1, 7);
        var (yaw, pitch) = CameraFraming.LookAtAngles(from, to);

        var camera = new Camera(from, yaw, pitch);
        var expected = Vector3.Normalize(to - from);
        Assert.True(Vector3.Distance(expected, camera.Forward) < Tolerance, $"{camera.Forward} != {expected}");
    }

    [Fact]
    public void LookAtAngles_ClampsPitch()
    {
        var (_, pitch) = CameraFraming.LookAtAngles(Vector3.Zero, new Vector3(0, -10, 0.0001f));
        Assert.Equal(-CameraFraming.MaxPitch, pitch, Tolerance);
    }

    [Fact]
    public void Frame_KeepsViewDirectionAndLooksAtCenter()
    {
        var bounds = new BoundingSphere(new Vector3(10, 0, 0), 2f);
        var cameraPos = new Vector3(0, 0, 0);
        var pose = CameraFraming.Frame(bounds, cameraPos, -Vector3.UnitZ, 60f);

        var expectedDistance = CameraFraming.FramingDistance(2f, 60f);
        Assert.Equal(expectedDistance, Vector3.Distance(pose.Position, bounds.Center), Tolerance);
        // Camera stays on the line from its old position to the target
        Assert.Equal(0f, pose.Position.Y, Tolerance);
        Assert.Equal(0f, pose.Position.Z, Tolerance);
        Assert.True(pose.Position.X < bounds.Center.X);

        var camera = new Camera(pose.Position, pose.Yaw, pose.Pitch);
        Assert.True(Vector3.Distance(camera.Forward, Vector3.UnitX) < Tolerance);
    }

    [Fact]
    public void Frame_WhenCameraIsAtTarget_UsesForward()
    {
        var bounds = new BoundingSphere(Vector3.Zero, 1f);
        var pose = CameraFraming.Frame(bounds, Vector3.Zero, Vector3.UnitX, 60f);
        Assert.True(pose.Position.X < 0f);
        Assert.Equal(0f, pose.Position.Y, Tolerance);
    }

    [Fact]
    public void Frame_FromDirectlyAbove_AvoidsDegeneratePitch()
    {
        var bounds = new BoundingSphere(Vector3.Zero, 1f);
        var pose = CameraFraming.Frame(bounds, new Vector3(0, 50, 0), -Vector3.UnitY, 60f);

        Assert.InRange(pose.Pitch, -CameraFraming.MaxPitch, CameraFraming.MaxPitch);
        var camera = new Camera(pose.Position, pose.Yaw, pose.Pitch);
        var toCenter = Vector3.Normalize(bounds.Center - pose.Position);
        Assert.True(Vector3.Dot(camera.Forward, toCenter) > 0.999f);
    }

    [Fact]
    public void Merge_ContainsBothSpheres()
    {
        var a = new BoundingSphere(Vector3.Zero, 1f);
        var b = new BoundingSphere(new Vector3(10, 0, 0), 2f);
        var merged = CameraFraming.Merge(a, b);

        Assert.Equal(6.5f, merged.Radius, Tolerance);
        Assert.Equal(new Vector3(5.5f, 0, 0).X, merged.Center.X, Tolerance);
        Assert.Equal(b, CameraFraming.Merge(null, b));
        Assert.Equal(b, CameraFraming.Merge(new BoundingSphere(new Vector3(10, 0, 0), 0.5f), b));
    }

    [Fact]
    public void MeshBounds_AppliesScaleRotationAndOffsetCenter()
    {
        var rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI / 2f);
        var bounds = CameraFraming.MeshBounds(new Vector3(0, -1, -1), new Vector3(2, 1, 1), new Vector3(5, 0, 0), rotation, new Vector3(2f));

        // Local center (1,0,0) scaled to (2,0,0), rotated 90 deg about Y to (0,0,-2)
        Assert.True(Vector3.Distance(new Vector3(5, 0, -2), bounds.Center) < Tolerance, bounds.Center.ToString());
        Assert.Equal(new Vector3(2, 2, 2).Length(), bounds.Radius, Tolerance);
    }

    [Fact]
    public void ComputeEntityBounds_UsesColliderAndScale()
    {
        var world = new World();
        var entity = world.CreateEntity();
        world.AddComponent(entity, new TransformComponent(new Vector3(1, 2, 3), Quaternion.Identity, new Vector3(3f)));
        world.AddComponent(entity, ColliderComponent.CreateSphere(2f));

        var bounds = CameraFraming.ComputeEntityBounds(world, entity);

        Assert.NotNull(bounds);
        Assert.Equal(new Vector3(1, 2, 3), bounds!.Value.Center);
        Assert.Equal(6f, bounds.Value.Radius, Tolerance);
    }

    [Fact]
    public void ComputeEntityBounds_FallsBackToPositionWithDefaultRadius()
    {
        var world = new World();
        var entity = world.CreateEntity();
        world.AddComponent(entity, new TransformComponent(new Vector3(4, 0, 0)));

        var bounds = CameraFraming.ComputeEntityBounds(world, entity);

        Assert.Equal(new BoundingSphere(new Vector3(4, 0, 0), CameraFraming.DefaultRadius), bounds);
    }

    [Fact]
    public void ComputeEntityBounds_GroundPlaneIsNotUnbounded()
    {
        var world = new World();
        var entity = world.CreateEntity();
        world.AddComponent(entity, new TransformComponent(Vector3.Zero));
        world.AddComponent(entity, ColliderComponent.CreateGroundPlane());

        var bounds = CameraFraming.ComputeEntityBounds(world, entity);
        Assert.Equal(CameraFraming.DefaultRadius, bounds!.Value.Radius, Tolerance);
    }

    [Fact]
    public void ComputeEntityBounds_IncludesAccretionDisk()
    {
        var world = new World();
        var entity = world.CreateEntity();
        world.AddComponent(entity, new TransformComponent(Vector3.Zero));
        var disk = AccretionDiskComponent.CreateForBlackHole(2f);
        world.AddComponent(entity, disk);

        var bounds = CameraFraming.ComputeEntityBounds(world, entity);
        Assert.True(bounds!.Value.Radius >= disk.OuterRadius - Tolerance);
    }

    [Fact]
    public void ComputeEntityBounds_ReturnsNullWithoutTransformOrForDeadEntity()
    {
        var world = new World();
        var entity = world.CreateEntity();
        Assert.Null(CameraFraming.ComputeEntityBounds(world, entity));

        world.AddComponent(entity, new TransformComponent());
        world.DestroyEntity(entity);
        Assert.Null(CameraFraming.ComputeEntityBounds(world, entity));
    }

    [Fact]
    public void Animation_InterpolatesAndCompletes()
    {
        var from = new CameraPose(Vector3.Zero, 0f, 0f);
        var to = new CameraPose(new Vector3(10, 0, 0), 90f, 30f);
        var animation = new CameraFocusAnimation(from, to, duration: 1f);

        var mid = animation.Step(0.5f);
        Assert.False(animation.IsComplete);
        Assert.Equal(5f, mid.Position.X, Tolerance); // smoothstep(0.5) = 0.5
        Assert.Equal(45f, mid.Yaw, Tolerance);

        var end = animation.Step(10f);
        Assert.True(animation.IsComplete);
        Assert.Equal(to, end);
    }

    [Fact]
    public void Animation_ZeroDurationSnaps()
    {
        var to = new CameraPose(Vector3.One, 10f, 5f);
        var animation = new CameraFocusAnimation(new CameraPose(Vector3.Zero, 0, 0), to, duration: 0f);
        Assert.Equal(to, animation.Step(0f));
        Assert.True(animation.IsComplete);
    }

    [Theory]
    [InlineData(350f, 10f, 0.5f, 360f)]
    [InlineData(10f, 350f, 0.5f, 0f)]
    [InlineData(-90f, 80f, 1f, 80f)]
    [InlineData(-450f, 0f, 1f, -360f)]
    public void LerpAngle_TakesShortestArc(float from, float to, float t, float expected)
    {
        var result = CameraFocusAnimation.LerpAngle(from, to, t);
        Assert.Equal(expected, result, Tolerance);
    }
}
