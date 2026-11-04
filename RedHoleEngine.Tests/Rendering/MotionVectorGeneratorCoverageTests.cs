using System.Numerics;
using RedHoleEngine.Rendering.Upscaling;

namespace RedHoleEngine.Tests.Rendering;

public class MotionVectorGeneratorCoverageTests
{
    [Fact]
    public void HaltonJitter_MatchesBase2And3_AndWrapsPhase()
    {
        var first = MotionVectorGenerator.GetHaltonJitter(0);
        Assert.Equal(0f, first.X, precision: 5);
        Assert.Equal(1f / 3f - 0.5f, first.Y, precision: 5);

        var second = MotionVectorGenerator.GetHaltonJitter(1);
        Assert.Equal(0.25f - 0.5f, second.X, precision: 5);
        Assert.Equal(2f / 3f - 0.5f, second.Y, precision: 5);

        var wrapped = MotionVectorGenerator.GetHaltonJitter(8);
        Assert.Equal(first.X, wrapped.X, precision: 5);
        Assert.Equal(first.Y, wrapped.Y, precision: 5);

        for (uint i = 0; i < 16; i++)
        {
            var jitter = MotionVectorGenerator.GetHaltonJitter(i, 8);
            Assert.InRange(jitter.X, -0.5f, 0.5f);
            Assert.InRange(jitter.Y, -0.5f, 0.5f);
        }
    }

    [Fact]
    public void ApplyJitterToProjection_ShiftsByPixelFraction()
    {
        var jittered = MotionVectorGenerator.ApplyJitterToProjection(
            Matrix4x4.Identity,
            new Vector2(1f, -2f),
            100,
            50);

        Assert.Equal(0.02f, jittered.M41, precision: 5);
        Assert.Equal(-0.08f, jittered.M42, precision: 5);
        Assert.Equal(1f, jittered.M11, precision: 5);
    }

    [Fact]
    public void CameraHistory_ResetsOnFirstFrameTeleportAndLargeRotation()
    {
        var generator = new MotionVectorGenerator();
        Assert.True(generator.ShouldResetAccumulation());

        generator.Initialize(200, 100);
        generator.UpdateCamera(Vector3.Zero, Matrix4x4.Identity, Matrix4x4.Identity, new Vector2(0.1f, -0.2f));
        Assert.False(generator.ShouldResetAccumulation());

        var uniforms = generator.GetUniforms(0.1f, 500f);
        Assert.Equal(new Vector2(200, 100), uniforms.Resolution);
        Assert.Equal(new Vector2(1f / 200f, 1f / 100f), uniforms.InvResolution);
        Assert.Equal(new Vector2(0.1f, -0.2f), uniforms.CurrentJitter);
        Assert.Equal(uniforms.CurrentJitter, uniforms.PrevJitter);
        Assert.Equal(Vector3.Zero, uniforms.CurrentCameraPos);
        Assert.Equal(Vector3.Zero, uniforms.PrevCameraPos);
        Assert.Equal(0.1f, uniforms.NearPlane);
        Assert.Equal(500f, uniforms.FarPlane);
        Assert.Equal(Vector2.Zero, generator.CalculateMotionVector(new Vector3(0f, 0f, 1f)));

        generator.UpdateCamera(new Vector3(0f, 0f, 25f), Matrix4x4.Identity, Matrix4x4.Identity, Vector2.Zero);
        Assert.True(generator.ShouldResetAccumulation());

        var stable = new MotionVectorGenerator();
        stable.Initialize(20, 10);
        stable.UpdateCamera(Vector3.Zero, Matrix4x4.Identity, Matrix4x4.Identity, Vector2.Zero);
        var flipped = Matrix4x4.Identity;
        flipped.M33 = -1f;
        stable.UpdateCamera(Vector3.Zero, flipped, Matrix4x4.Identity, Vector2.Zero);
        Assert.True(stable.ShouldResetAccumulation());
    }

    [Fact]
    public void CalculateMotionVector_IncludesProjectionChangeAndJitterDelta()
    {
        var generator = new MotionVectorGenerator();
        generator.Initialize(100, 50);
        generator.UpdateCamera(Vector3.Zero, Matrix4x4.Identity, Matrix4x4.Identity, Vector2.Zero);
        generator.UpdateCamera(
            Vector3.Zero,
            Matrix4x4.Identity,
            Matrix4x4.CreateTranslation(0.2f, -0.4f, 0f),
            new Vector2(1f, 2f));

        var motion = generator.CalculateMotionVector(Vector3.Zero);
        Assert.Equal(0.2f * 0.5f * 100f + 1f, motion.X, precision: 3);
        Assert.Equal(-0.4f * 0.5f * 50f + 2f, motion.Y, precision: 3);
    }

    [Fact]
    public void Resize_ForcesTheNextCameraSampleToBeAFirstFrame()
    {
        var generator = new MotionVectorGenerator();
        generator.Initialize(10, 10);
        generator.UpdateCamera(Vector3.Zero, Matrix4x4.Identity, Matrix4x4.Identity, Vector2.UnitX);
        generator.Resize(20, 10);
        generator.UpdateCamera(new Vector3(1f, 0f, 0f), Matrix4x4.Identity, Matrix4x4.Identity, Vector2.UnitY);

        var uniforms = generator.GetUniforms(1f, 10f);
        Assert.Equal(new Vector2(20, 10), uniforms.Resolution);
        Assert.Equal(new Vector3(1f, 0f, 0f), uniforms.PrevCameraPos);
        Assert.Equal(Vector2.UnitY, uniforms.PrevJitter);
        Assert.False(generator.ShouldResetAccumulation());
    }
}
