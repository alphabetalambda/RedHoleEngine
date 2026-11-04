using System.Numerics;
using RedHoleEngine.Engine;

namespace RedHoleEngine.Tests.Engine;

public class CameraMotionTests
{
    [Fact]
    public void DefaultYaw_LooksDownNegativeZ_AndPitchIsClamped()
    {
        var camera = new Camera(new Vector3(1, 2, 3));
        Assert.Equal(-90f, camera.Yaw);
        Assert.Equal(0f, camera.Pitch);
        Assert.Equal(-1f, camera.Forward.Z, 1e-4f);
        Assert.Equal(0f, camera.Forward.Y, 1e-4f);
        Assert.True(MathF.Abs(Vector3.Dot(camera.Forward, camera.Right)) < 1e-4f);
        Assert.True(MathF.Abs(Vector3.Dot(camera.Forward, camera.Up)) < 1e-4f);

        camera.Pitch = 120f;
        Assert.Equal(89f, camera.Pitch);
        camera.Pitch = -120f;
        Assert.Equal(-89f, camera.Pitch);
    }

    [Fact]
    public void Rotate_ChangesYawAndClampsPitch()
    {
        var camera = new Camera(Vector3.Zero, yaw: 0f, pitch: 0f) { MouseSensitivity = 1f };
        camera.Rotate(10f, -5f);
        Assert.Equal(10f, camera.Yaw, 1e-4f);
        Assert.Equal(5f, camera.Pitch, 1e-4f);

        camera.Rotate(0f, -1000f);
        Assert.Equal(89f, camera.Pitch);
    }

    [Fact]
    public void Move_UsesSpeedAndDelta_AndViewMatrixInverts()
    {
        var camera = new Camera(Vector3.Zero) { MovementSpeed = 2f };
        var before = camera.Position;
        camera.Move(Vector3.UnitY, 0.5f);
        Assert.Equal(before + new Vector3(0, 1, 0), camera.Position);

        camera.MoveForward(0.5f, forward: true);
        Assert.True(camera.Position.Z < 0f);
        var afterForward = camera.Position;
        camera.MoveForward(0.5f, forward: false);
        Assert.True(camera.Position.Z > afterForward.Z);

        var rightBefore = camera.Position;
        camera.MoveRight(1f, right: true);
        Assert.NotEqual(rightBefore, camera.Position);
        camera.MoveUp(1f, up: false);
        Assert.True(camera.Position.Y < 1f);

        var view = camera.GetViewMatrix();
        var inverse = camera.GetInverseViewMatrix();
        var identity = view * inverse;
        Assert.Equal(1f, identity.M11, 1e-3f);
        Assert.Equal(1f, identity.M22, 1e-3f);
        Assert.Equal(1f, identity.M33, 1e-3f);
    }
}
