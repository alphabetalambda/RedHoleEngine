using System.Numerics;
using RedHoleEngine.Components;
using RedHoleEngine.Core.ECS;
using RedHoleEngine.Particles;

namespace RedHoleEngine.Tests.Particles;

public class ParticleModuleCoverageTests
{
    [Fact]
    public void ColorGradient_SingleStopAndOutOfRange_ReturnEndpointColors()
    {
        var single = new ColorGradient(new GradientStop(0.4f, new Vector4(0.2f, 0.3f, 0.4f, 1f)));
        Assert.Equal(new Vector4(0.2f, 0.3f, 0.4f, 1f), single.Evaluate(0f));

        var late = new ColorGradient(new GradientStop(0.8f, Vector4.UnitX), new GradientStop(0.9f, Vector4.UnitY));
        Assert.Equal(Vector4.UnitX, late.Evaluate(0f));
        Assert.Equal(Vector4.UnitY, late.Evaluate(1f));
        Assert.Equal(ColorGradient.White.Evaluate(0.5f), Vector4.One);
        Assert.Equal(0f, ColorGradient.FadeOut.Evaluate(1f).W);
    }

    [Fact]
    public void ColorGradient_CurrentBehavior_DuplicateTimesProduceNaN()
    {
        var gradient = new ColorGradient(
            new GradientStop(0.5f, Vector4.UnitX),
            new GradientStop(0.5f, Vector4.UnitY));
        var color = gradient.Evaluate(0.5f);
        Assert.True(float.IsNaN(color.X) || float.IsNaN(color.Y));
    }

    [Fact(Skip = "BUG: expected duplicate gradient stops at the same time to return a finite stop color, actual Evaluate divides by zero and returns NaN")]
    public void ColorGradient_DuplicateTimes_StayFinite()
    {
        var gradient = new ColorGradient(
            new GradientStop(0.5f, Vector4.UnitX),
            new GradientStop(0.5f, Vector4.UnitY));
        var color = gradient.Evaluate(0.5f);
        Assert.True(float.IsFinite(color.X) && float.IsFinite(color.Y));
    }

    [Fact]
    public void AnimationCurve_DuplicateKeys_UseZeroBlend()
    {
        var curve = new AnimationCurve((0.5f, 2f), (0.5f, 8f));
        Assert.Equal(2f, curve.Evaluate(0.5f));
    }

    [Fact]
    public void VelocityOrbitalRadial_AndRepeatedSpeedMultiplier()
    {
        var module = new VelocityOverLifetimeModule
        {
            SpeedMultiplier = AnimationCurve.Constant(0.5f),
            LinearVelocity = Vector3.UnitY,
            OrbitalVelocity = 180f,
            RadialVelocity = AnimationCurve.Constant(2f)
        };
        var particle = Particle.Create(new Vector3(1f, 0f, 0f), Vector3.UnitX, Vector4.One, 1f, 1f);
        module.Apply(ref particle, 1f, Vector3.Zero);

        Assert.Equal(-1f, particle.Position.X, precision: 3);
        Assert.Equal(0f, particle.Position.Z, precision: 3);
        Assert.True(float.IsFinite(particle.Velocity.X));
        Assert.Equal(1f, particle.Velocity.Y, precision: 3);

        var again = Particle.Create(Vector3.Zero, new Vector3(4f, 0f, 0f), Vector4.One, 1f, 1f);
        var halver = new VelocityOverLifetimeModule { SpeedMultiplier = AnimationCurve.Constant(0.5f) };
        halver.Apply(ref again, 0.016f, Vector3.Zero);
        halver.Apply(ref again, 0.016f, Vector3.Zero);
        Assert.Equal(1f, again.Velocity.X, precision: 3);
    }

    [Fact(Skip = "BUG: expected a constant 0.5 speed multiplier to leave velocity at half after repeated Apply calls, actual each call multiplies again so two calls quarter the velocity")]
    public void VelocityMultiplier_IsNotCompounded()
    {
        var particle = Particle.Create(Vector3.Zero, new Vector3(4f, 0f, 0f), Vector4.One, 1f, 1f);
        var module = new VelocityOverLifetimeModule { SpeedMultiplier = AnimationCurve.Constant(0.5f) };
        module.Apply(ref particle, 0.016f, Vector3.Zero);
        module.Apply(ref particle, 0.016f, Vector3.Zero);
        Assert.Equal(2f, particle.Velocity.X, precision: 3);
    }

    [Fact]
    public void LimitRotationTextureAndNoise_ModifyParticles()
    {
        var limited = Particle.Create(Vector3.Zero, new Vector3(20f, 0f, 0f), Vector4.One, 1f, 1f);
        new LimitVelocityModule { MaxSpeed = 10f, Damping = 1f }.Apply(ref limited, 1f);
        Assert.Equal(Vector3.Zero, limited.Velocity);

        var undamped = Particle.Create(Vector3.Zero, new Vector3(20f, 0f, 0f), Vector4.One, 1f, 1f);
        new LimitVelocityModule { MaxSpeed = 10f, Damping = 0f }.Apply(ref undamped, 1f);
        Assert.Equal(20f, undamped.Velocity.X);

        var spinning = Particle.Create(Vector3.Zero, Vector3.Zero, Vector4.One, 1f, 1f);
        new RotationOverLifetimeModule { AngularVelocity = AnimationCurve.Constant(180f) }.Apply(ref spinning, 0.5f);
        Assert.Equal(MathF.PI / 2f, spinning.Rotation, precision: 3);

        var sheet = new TextureSheetAnimationModule { TilesX = 4, TilesY = 4, CyclesPerLifetime = 1f };
        var fresh = Particle.Create(Vector3.Zero, Vector3.Zero, Vector4.One, 1f, 2f);
        Assert.Equal(0, sheet.GetFrameIndex(fresh));
        Assert.Equal((Vector2.Zero, new Vector2(0.25f, 0.25f)), sheet.GetUVs(fresh));
        fresh.Lifetime = 1f;
        Assert.Equal(8, sheet.GetFrameIndex(fresh));
        var uvs = sheet.GetUVs(fresh);
        Assert.Equal(0f, uvs.min.X, precision: 3);
        Assert.Equal(0.5f, uvs.min.Y, precision: 3);
        Assert.Equal(0.25f, uvs.max.X, precision: 3);
        Assert.Equal(0.75f, uvs.max.Y, precision: 3);

        var noisy = Particle.Create(new Vector3(1.2f, 0.4f, -0.7f), Vector3.Zero, Vector4.One, 1f, 1f);
        var other = noisy;
        var noise = new NoiseModule { Strength = 3f, Frequency = 2f, ScrollSpeed = Vector3.UnitX, Octaves = 1 };
        var same = new NoiseModule { Strength = 3f, Frequency = 2f, ScrollSpeed = Vector3.UnitX, Octaves = 4 };
        noise.Apply(ref noisy, 0.25f, new Random(1));
        same.Apply(ref other, 0.25f, new Random(1));
        Assert.NotEqual(Vector3.Zero, noisy.Velocity);
        Assert.Equal(noisy.Velocity, other.Velocity);

        var sized = Particle.Create(Vector3.Zero, Vector3.Zero, Vector4.One, 4f, 2f);
        sized.Lifetime = 1f;
        var size = SizeOverLifetimeModule.Grow;
        size.OnSpawn(ref sized);
        size.Apply(ref sized, 4f);
        Assert.Equal(2f, sized.Size, precision: 3);
        Assert.Equal(0.2f, SizeOverLifetimeModule.GrowAndFade.SizeMultiplier.Evaluate(1f), precision: 3);
        Assert.Equal(0f, SizeOverLifetimeModule.Shrink.SizeMultiplier.Evaluate(1f));
    }

    [Fact(Skip = "BUG: expected NoiseModule.Octaves to change the sample, actual Apply ignores Octaves so 1 and 4 match")]
    public void NoiseOctaves_ChangeTheSample()
    {
        var a = Particle.Create(new Vector3(1.2f, 0.4f, -0.7f), Vector3.Zero, Vector4.One, 1f, 1f);
        var b = a;
        new NoiseModule { Strength = 3f, Frequency = 2f, Octaves = 1 }.Apply(ref a, 0.25f, new Random(1));
        new NoiseModule { Strength = 3f, Frequency = 2f, Octaves = 4 }.Apply(ref b, 0.25f, new Random(1));
        Assert.NotEqual(a.Velocity, b.Velocity);
    }

    [Fact]
    public void TextureSheet_ZeroTiles_Throws()
    {
        var sheet = new TextureSheetAnimationModule { TilesX = 0, TilesY = 1 };
        var particle = Particle.Create(Vector3.Zero, Vector3.Zero, Vector4.One, 1f, 1f);
        Assert.Throws<DivideByZeroException>(() => sheet.GetFrameIndex(particle));
    }

    [Fact]
    public void EmissionShape_MeshFallsBackToUp()
    {
        var shape = new EmissionShape { Type = EmissionShapeType.Mesh, Radius = 4f };
        shape.Sample(new Random(1), out var position, out var direction);
        Assert.Equal(Vector3.Zero, position);
        Assert.Equal(Vector3.UnitY, direction);
    }
}

public class ParticlePoolCoverageTests
{
    [Fact]
    public void BaseSize_SurvivesRemovalAndSort()
    {
        var pool = new ParticlePool(4);
        Assert.Equal(4, pool.Capacity);
        Assert.False(pool.IsFull);
        pool.Emit(Particle.Create(Vector3.Zero, Vector3.Zero, Vector4.One, 1.5f, 2f));
        pool.Emit(Particle.Create(Vector3.UnitX, Vector3.Zero, Vector4.One, 3f, 4f));
        pool.GetParticle(0).IsAlive = false;
        pool.RemoveDeadParticles();
        Assert.Equal(1, pool.AliveCount);
        Assert.Equal(3f, pool.GetBaseSize(0));
        Assert.Equal(1, pool.GetAliveParticlesReadOnly().Length);

        pool.Emit(Particle.Create(Vector3.UnitY, Vector3.Zero, Vector4.One, 4f, 1f));
        pool.GetParticle(0).Lifetime = 0.2f;
        pool.Sort(ParticleSortMode.ByAgeReverse, Vector3.Zero, Vector3.UnitZ);
        Assert.Equal(4f, pool.GetParticle(0).Size);
        Assert.True(pool.GetParticle(0).NormalizedAge < pool.GetParticle(1).NormalizedAge);
        Assert.Equal(4f, pool.GetBaseSize(0));

        var destination = new ParticleRenderData[1];
        Assert.Equal(1, pool.CopyRenderData(destination.AsSpan()));
        Assert.Equal(pool.GetParticle(0).Position, destination[0].Position);
        Assert.True(pool.CopyRenderData(new ParticleRenderData[1], 5) < 0);

        pool.Sort(ParticleSortMode.None, Vector3.Zero, Vector3.UnitZ);
        Assert.Equal(2, pool.AliveCount);
    }

    [Fact(Skip = "BUG: expected CopyRenderData to return 0 when startIndex is past the destination, actual it returns a negative count")]
    public void CopyRenderData_PastTheEnd_ReturnsZero()
    {
        var pool = new ParticlePool(2);
        pool.Emit(Particle.Create(Vector3.Zero, Vector3.Zero, Vector4.One, 1f, 1f));
        Assert.Equal(0, pool.CopyRenderData(new ParticleRenderData[1], 5));
    }

    [Fact]
    public void Manager_RemoveAndBufferLimit()
    {
        var manager = new ParticlePoolManager(2);
        var named = manager.GetOrCreatePool("sparks", 2);
        var anon = manager.CreatePool(2);
        named.Emit(Particle.Create(Vector3.UnitX, Vector3.Zero, Vector4.UnitX, 1f, 1f));
        named.Emit(Particle.Create(Vector3.UnitY, Vector3.Zero, Vector4.UnitY, 1f, 1f));
        anon.Emit(Particle.Create(Vector3.UnitZ, Vector3.Zero, Vector4.UnitZ, 1f, 1f));
        Assert.Equal(3, manager.TotalAliveCount);

        var rendered = manager.CollectRenderData();
        Assert.Equal(2, rendered.Length);
        Assert.Equal(Vector3.UnitX, rendered[0].Position);

        manager.RemovePool(named);
        Assert.Equal(1, manager.TotalAliveCount);
        var replacement = manager.GetOrCreatePool("sparks", 2);
        Assert.NotSame(named, replacement);
        Assert.Equal(0, replacement.AliveCount);
        manager.ClearAll();
        Assert.Equal(0, manager.TotalAliveCount);
    }
}

public class ParticleSystemCoverageTests
{
    [Fact]
    public void DelayGravityLocalSpace_AndFractionalEmission()
    {
        var world = new World();
        var system = world.AddSystem(new ParticleSystem());
        system.Gravity = new Vector3(0f, -10f, 0f);
        system.CameraPosition = new Vector3(0f, 0f, 5f);
        system.CameraForward = -Vector3.UnitZ;

        var delayed = Emitter(new Vector3(5f, 0f, 0f));
        delayed.StartDelay = 1f;
        delayed.EmissionRate = 100f;
        world.AddComponent(world.CreateEntity(), new TransformComponent(new Vector3(5f, 0f, 0f)));
        world.AddComponent(world.Query<TransformComponent>().Single(), delayed);

        system.Update(0.2f);
        Assert.Equal(0, system.TotalParticleCount);

        var local = Emitter(Vector3.Zero);
        local.SimulationSpace = ParticleSimulationSpace.Local;
        local.EmissionRate = 10f;
        local.StartSpeed = new FloatRange(0f);
        local.Lifetime = new FloatRange(2f);
        local.StartSize = new FloatRange(0.4f);
        local.GravityMultiplier = 1f;
        var localEntity = world.CreateEntity();
        world.AddComponent(localEntity, new TransformComponent(new Vector3(9f, 0f, 0f)));
        world.AddComponent(localEntity, local);

        system.TimeScale = 1f;
        system.Update(0.1f);
        Assert.Equal(1, local.Pool!.AliveCount);
        var particle = local.Pool.GetParticle(0);
        Assert.Equal(0f, particle.Position.X, precision: 3);
        Assert.True(particle.Position.Y < 0f);
        Assert.Equal(0.4f, local.Pool.GetBaseSize(0));

        system.TimeScale = 0f;
        var before = local.Pool.AliveCount;
        system.Update(1f);
        Assert.Equal(before, local.Pool.AliveCount);
    }

    [Fact]
    public void LoopingDuration_CurrentBehavior_StallsWhenStepEqualsDuration()
    {
        var world = new World();
        var system = world.AddSystem(new ParticleSystem());
        var emitter = Emitter(Vector3.Zero);
        emitter.Duration = 0.2f;
        emitter.Looping = true;
        emitter.EmissionRate = 10f;
        emitter.Lifetime = new FloatRange(10f);
        emitter.StartSpeed = new FloatRange(0f);
        var entity = world.CreateEntity();
        world.AddComponent(entity, new TransformComponent(Vector3.Zero));
        world.AddComponent(entity, emitter);

        system.Update(0.2f);
        system.Update(0.2f);
        system.Update(0.2f);
        Assert.Equal(2, emitter.Pool!.AliveCount);
    }

    [Fact(Skip = "BUG: expected a looping emitter whose duration equals the frame step to emit again after the loop reset, actual ElapsedTime is advanced in the same frame and emission never resumes")]
    public void LoopingDuration_EmitsAgainAfterReset()
    {
        var world = new World();
        var system = world.AddSystem(new ParticleSystem());
        var emitter = Emitter(Vector3.Zero);
        emitter.Duration = 0.2f;
        emitter.Looping = true;
        emitter.EmissionRate = 10f;
        emitter.Lifetime = new FloatRange(10f);
        emitter.StartSpeed = new FloatRange(0f);
        world.AddComponent(world.CreateEntity(), new TransformComponent(Vector3.Zero));
        world.AddComponent(world.Query<TransformComponent>().Single(), emitter);

        system.Update(0.2f);
        system.Update(0.2f);
        system.Update(0.2f);
        Assert.True(emitter.Pool!.AliveCount > 2);
    }

    [Fact]
    public void StoppedEmitter_DoesNotEmit_AndColorModuleFades()
    {
        var world = new World();
        var system = world.AddSystem(new ParticleSystem());
        var emitter = Emitter(Vector3.Zero);
        emitter.Stop();
        emitter.EmissionRate = 50f;
        emitter.ColorOverLifetime = new ColorOverLifetimeModule { Gradient = ColorGradient.FadeOut };
        emitter.SizeOverLifetime = SizeOverLifetimeModule.Shrink;
        world.AddComponent(world.CreateEntity(), new TransformComponent(Vector3.Zero));
        world.AddComponent(world.Query<TransformComponent>().Single(), emitter);
        emitter.Emit(1);
        emitter.Play();
        emitter.EmissionRate = 0f;
        emitter.Lifetime = new FloatRange(1f);
        emitter.StartSpeed = new FloatRange(0f);
        emitter.StartSize = new FloatRange(2f);

        system.Update(0.016f);
        Assert.Equal(1, emitter.Pool!.AliveCount);
        system.Update(0.5f);
        var particle = emitter.Pool.GetParticle(0);
        Assert.True(particle.Color.W < 1f);
        Assert.True(particle.Size < 2f);

        emitter.Clear();
        Assert.False(emitter.IsPlaying);
        Assert.Equal(0, emitter.AliveCount);
    }

    [Fact]
    public void Presets_RainSnowDustAndPlasma_UseTheirModules()
    {
        var rain = ParticlePresets.Rain();
        Assert.Equal(1f, rain.GravityMultiplier);
        Assert.Equal(ParticleRenderMode.StretchedBillboard, rain.RenderMode);
        Assert.Equal(EmissionShapeType.Box, rain.Shape.Type);

        var snow = ParticlePresets.Snow();
        Assert.Equal(0.1f, snow.GravityMultiplier);
        Assert.NotNull(snow.Noise);
        Assert.Equal(0.3f, snow.Noise!.Strength);

        var dust = ParticlePresets.Dust();
        Assert.NotNull(dust.ColorOverLifetime);
        Assert.Equal(EmissionShapeType.Sphere, dust.Shape.Type);

        var plasma = ParticlePresets.Plasma();
        Assert.Equal(180f, plasma.VelocityOverLifetime!.OrbitalVelocity);
        Assert.Equal(ParticleSortMode.ByDepth, plasma.SortMode);
        Assert.True(plasma.Shape.EmitFromSurface);
        Assert.Equal(0f, plasma.SizeOverLifetime!.SizeMultiplier.Evaluate(1f));
    }

    private static ParticleEmitterComponent Emitter(Vector3 position)
    {
        return new ParticleEmitterComponent
        {
            IsPlaying = true,
            EmissionRate = 0f,
            Duration = 0f,
            Looping = false,
            StartDelay = 0f,
            MaxParticles = 32,
            Lifetime = new FloatRange(1f),
            StartSpeed = new FloatRange(0f),
            StartSize = new FloatRange(1f),
            StartRotation = new FloatRange(0f),
            Shape = EmissionShape.Point,
            SimulationSpace = ParticleSimulationSpace.World,
            GravityMultiplier = 0f
        };
    }
}
