using System.Numerics;
using RedHoleEngine.Engine;

namespace RedHoleEngine.Tests.Engine;

public class CameraTests
{
    private const float Epsilon = 0.001f;

    [Fact]
    public void Constructor_DefaultOrientation_LooksDownNegativeZ()
    {
        var camera = new Camera(new Vector3(1f, 2f, 3f));

        Assert.Equal(new Vector3(1f, 2f, 3f), camera.Position);
        Assert.Equal(-90f, camera.Yaw);
        Assert.Equal(0f, camera.Pitch);
        Assert.Equal(5f, camera.MovementSpeed);
        Assert.Equal(0.1f, camera.MouseSensitivity);
        Assert.Equal(60f, camera.FieldOfView);
        Assert.Equal(0f, camera.Forward.X, Epsilon);
        Assert.Equal(0f, camera.Forward.Y, Epsilon);
        Assert.Equal(-1f, camera.Forward.Z, Epsilon);
        Assert.Equal(Vector3.UnitX, Round(camera.Right));
        Assert.Equal(Vector3.UnitY, Round(camera.Up));
        Assert.Equal(1f, camera.Forward.Length(), Epsilon);
        Assert.Equal(0f, Vector3.Dot(camera.Forward, camera.Right), Epsilon);
        Assert.Equal(0f, Vector3.Dot(camera.Right, camera.Up), Epsilon);
    }

    [Fact]
    public void PitchAndRotate_ClampPitchAndLeaveYawFree()
    {
        var camera = new Camera(Vector3.Zero, yaw: 0f, pitch: 0f);

        camera.Pitch = 120f;
        Assert.Equal(89f, camera.Pitch);
        camera.Pitch = -100f;
        Assert.Equal(-89f, camera.Pitch);
        camera.Yaw = 400f;
        Assert.Equal(400f, camera.Yaw);

        camera.Pitch = 0f;
        camera.Yaw = -90f;
        camera.Rotate(1000f, -1000f);
        Assert.Equal(10f, camera.Yaw, Epsilon);
        Assert.Equal(89f, camera.Pitch);
        camera.Rotate(0f, 10000f);
        Assert.Equal(-89f, camera.Pitch);
        Assert.Equal(1f, camera.Forward.Length(), Epsilon);
    }

    [Fact]
    public void Move_UsesSpeedDeltaAndBasisVectors()
    {
        var camera = new Camera(Vector3.Zero);
        camera.MovementSpeed = 2f;

        camera.Move(Vector3.UnitX, 0.5f);
        Assert.Equal(new Vector3(1f, 0f, 0f), camera.Position);

        camera.Position = Vector3.Zero;
        camera.MoveForward(1f, forward: true);
        Assert.Equal(0f, camera.Position.X, Epsilon);
        Assert.Equal(-2f, camera.Position.Z, Epsilon);

        camera.MoveForward(1f, forward: false);
        Assert.Equal(0f, camera.Position.Z, Epsilon);

        camera.MoveRight(1f, right: true);
        Assert.Equal(2f, camera.Position.X, Epsilon);
        camera.MoveRight(1f, right: false);
        Assert.Equal(0f, camera.Position.X, Epsilon);

        camera.MoveUp(1f, up: true);
        Assert.Equal(2f, camera.Position.Y, Epsilon);
        camera.MoveUp(1f, up: false);
        Assert.Equal(0f, camera.Position.Y, Epsilon);
        Assert.Equal(-90f, camera.Yaw);
    }

    [Fact]
    public void ViewMatrix_InvertsBackToIdentity()
    {
        var camera = new Camera(new Vector3(2f, 3f, 4f), yaw: -45f, pitch: 15f);
        var view = camera.GetViewMatrix();
        var inverse = camera.GetInverseViewMatrix();
        var product = view * inverse;

        Assert.Equal(1f, product.M11, Epsilon);
        Assert.Equal(1f, product.M22, Epsilon);
        Assert.Equal(1f, product.M33, Epsilon);
        Assert.Equal(1f, product.M44, Epsilon);
        Assert.Equal(0f, product.M12, Epsilon);
        Assert.Equal(0f, product.M13, Epsilon);
        Assert.Equal(0f, product.M21, Epsilon);
        Assert.Equal(0f, product.M23, Epsilon);
        Assert.Equal(0f, product.M31, Epsilon);
        Assert.Equal(0f, product.M32, Epsilon);
        Assert.Equal(0f, product.M41, Epsilon);
        Assert.Equal(0f, product.M42, Epsilon);
        Assert.Equal(0f, product.M43, Epsilon);
    }

    private static Vector3 Round(Vector3 value)
    {
        return new Vector3(
            MathF.Round(value.X),
            MathF.Round(value.Y),
            MathF.Round(value.Z));
    }
}
