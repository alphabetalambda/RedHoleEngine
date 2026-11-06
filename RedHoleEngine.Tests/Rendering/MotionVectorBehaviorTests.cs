using System.Numerics;
using RedHoleEngine.Rendering.Upscaling;

namespace RedHoleEngine.Tests.Rendering;

public class MotionVectorBehaviorTests
{
    [Fact]
    public void HaltonJitter_IsDeterministic_AndInsideTheUnitSquare()
    {
        var a = MotionVectorGenerator.GetHaltonJitter(0);
        var b = MotionVectorGenerator.GetHaltonJitter(8); // phase wraps
        Assert.Equal(a, b);
        var c = MotionVectorGenerator.GetHaltonJitter(1);
        Assert.NotEqual(a, c);
        Assert.InRange(a.X, -0.5f, 0.5f);
        Assert.InRange(a.Y, -0.5f, 0.5f);
    }

    [Fact]
    public void StationaryCamera_HasZeroMotion_AndMovingCameraDoesNot()
    {
        var gen = new MotionVectorGenerator();
        gen.Initialize(640, 360);
        var view = Matrix4x4.CreateLookAt(new Vector3(0, 0, 5), Vector3.Zero, Vector3.UnitY);
        var proj = Matrix4x4.CreatePerspectiveFieldOfView(MathF.PI / 3f, 16f / 9f, 0.1f, 100f);
        Assert.True(gen.ShouldResetAccumulation());
        gen.UpdateCamera(new Vector3(0, 0, 5), view, proj, Vector2.Zero);
        Assert.False(gen.ShouldResetAccumulation());

        var uniforms = gen.GetUniforms(0.1f, 100f);
        Assert.Equal(640, uniforms.Resolution.X);
        Assert.Equal(0.1f, uniforms.NearPlane);
        Assert.Equal(new Vector2(1f / 640f, 1f / 360f), uniforms.InvResolution);

        gen.UpdateCamera(new Vector3(0, 0, 5), view, proj, Vector2.Zero);
        var still = gen.CalculateMotionVector(Vector3.Zero);
        Assert.Equal(0f, still.Length(), 1e-3f);

        gen.UpdateCamera(new Vector3(30, 0, 5), view, proj, Vector2.Zero);
        Assert.True(gen.ShouldResetAccumulation());

        var movedView = Matrix4x4.CreateLookAt(new Vector3(1, 0, 5), Vector3.Zero, Vector3.UnitY);
        gen.UpdateCamera(new Vector3(1, 0, 5), movedView, proj, new Vector2(0.2f, -0.1f));
        var moved = gen.CalculateMotionVector(Vector3.Zero);
        Assert.True(moved.Length() > 0.1f);

        var jittered = MotionVectorGenerator.ApplyJitterToProjection(proj, new Vector2(0.5f, -0.25f), 640, 360);
        Assert.NotEqual(proj, jittered);

        gen.Resize(320, 180);
        var resized = gen.GetUniforms(0.1f, 50f);
        Assert.Equal(320, resized.Resolution.X);
        Assert.Equal(50f, resized.FarPlane);
    }
}
