using System.Numerics;
using RedHoleEngine.Particles;
using Xunit;

namespace RedHoleEngine.Tests.Particles;

public class ParticleTests
{
    [Fact]
    public void Particle_Create_SetsAllProperties()
    {
        var position = new Vector3(1, 2, 3);
        var velocity = new Vector3(0, 1, 0);
        var color = new Vector4(1, 0, 0, 1);
        float size = 0.5f;
        float lifetime = 2f;
        float rotation = 1.5f;
        float angularVelocity = 0.2f;

        var particle = Particle.Create(position, velocity, color, size, lifetime, rotation, angularVelocity);

        Assert.Equal(position, particle.Position);
        Assert.Equal(velocity, particle.Velocity);
        Assert.Equal(color, particle.Color);
        Assert.Equal(size, particle.Size);
        Assert.Equal(lifetime, particle.Lifetime);
        Assert.Equal(lifetime, particle.InitialLifetime);
        Assert.Equal(rotation, particle.Rotation);
        Assert.Equal(angularVelocity, particle.AngularVelocity);
        Assert.True(particle.IsAlive);
    }

    [Fact]
    public void Particle_NormalizedAge_ReturnsZeroWhenJustSpawned()
    {
        var particle = Particle.Create(Vector3.Zero, Vector3.Zero, Vector4.One, 1f, 5f);
        
        Assert.Equal(0f, particle.NormalizedAge, 0.001f);
    }

    [Fact]
    public void Particle_NormalizedAge_ReturnsHalfAtMiddleOfLife()
    {
        var particle = Particle.Create(Vector3.Zero, Vector3.Zero, Vector4.One, 1f, 10f);
        particle.Lifetime = 5f; // Half consumed
        
        Assert.Equal(0.5f, particle.NormalizedAge, 0.001f);
    }

    [Fact]
    public void Particle_NormalizedAge_ReturnsOneWhenAboutToDie()
    {
        var particle = Particle.Create(Vector3.Zero, Vector3.Zero, Vector4.One, 1f, 10f);
        particle.Lifetime = 0f;
        
        Assert.Equal(1f, particle.NormalizedAge, 0.001f);
    }

    [Fact]
    public void ParticleRenderData_FromParticle_CopiesCorrectData()
    {
        var position = new Vector3(5, 10, 15);
        var color = new Vector4(0.5f, 0.6f, 0.7f, 0.8f);
        float size = 1.5f;
        var particle = Particle.Create(position, Vector3.UnitY, color, size, 1f);

        var renderData = ParticleRenderData.FromParticle(in particle);

        Assert.Equal(position, renderData.Position);
        Assert.Equal(color, renderData.Color);
        Assert.Equal(size, renderData.Size);
    }
}

public class ParticlePoolTests
{
    [Fact]
    public void ParticlePool_Emit_AddsParticleAndIncrementsCount()
    {
        var pool = new ParticlePool(100);
        var particle = Particle.Create(Vector3.Zero, Vector3.UnitY, Vector4.One, 1f, 1f);

        bool result = pool.Emit(in particle);

        Assert.True(result);
        Assert.Equal(1, pool.AliveCount);
    }

    [Fact]
    public void ParticlePool_Emit_ReturnsFalseWhenFull()
    {
        var pool = new ParticlePool(2);
        var particle = Particle.Create(Vector3.Zero, Vector3.UnitY, Vector4.One, 1f, 1f);

        pool.Emit(in particle);
        pool.Emit(in particle);
        bool result = pool.Emit(in particle);

        Assert.False(result);
        Assert.Equal(2, pool.AliveCount);
        Assert.True(pool.IsFull);
    }

    [Fact]
    public void ParticlePool_RemoveDeadParticles_RemovesDeadOnes()
    {
        var pool = new ParticlePool(10);
        
        for (int i = 0; i < 5; i++)
        {
            var p = Particle.Create(new Vector3(i, 0, 0), Vector3.UnitY, Vector4.One, 1f, 1f);
            pool.Emit(in p);
        }

        Assert.Equal(5, pool.AliveCount);

        // Mark some as dead
        pool.GetParticle(1).IsAlive = false;
        pool.GetParticle(3).Lifetime = 0f;

        pool.RemoveDeadParticles();

        Assert.Equal(3, pool.AliveCount);
    }

    [Fact]
    public void ParticlePool_Clear_RemovesAllParticles()
    {
        var pool = new ParticlePool(100);
        
        for (int i = 0; i < 50; i++)
        {
            var p = Particle.Create(Vector3.Zero, Vector3.UnitY, Vector4.One, 1f, 1f);
            pool.Emit(in p);
        }

        Assert.Equal(50, pool.AliveCount);

        pool.Clear();

        Assert.Equal(0, pool.AliveCount);
    }

    [Fact]
    public void ParticlePool_GetAliveParticles_ReturnsCorrectSpan()
    {
        var pool = new ParticlePool(100);
        
        for (int i = 0; i < 10; i++)
        {
            var p = Particle.Create(new Vector3(i, 0, 0), Vector3.UnitY, Vector4.One, 1f, 1f);
            pool.Emit(in p);
        }

        var particles = pool.GetAliveParticles();

        Assert.Equal(10, particles.Length);
        for (int i = 0; i < 10; i++)
        {
            Assert.Equal(i, particles[i].Position.X);
        }
    }

    [Fact]
    public void ParticlePool_CopyRenderData_CopiesToArray()
    {
        var pool = new ParticlePool(100);
        
        for (int i = 0; i < 5; i++)
        {
            var p = Particle.Create(new Vector3(i, 0, 0), Vector3.UnitY, Vector4.One, (float)i, 1f);
            pool.Emit(in p);
        }

        var renderData = new ParticleRenderData[10];
        int count = pool.CopyRenderData(renderData);

        Assert.Equal(5, count);
        for (int i = 0; i < 5; i++)
        {
            Assert.Equal(i, renderData[i].Position.X);
            Assert.Equal(i, renderData[i].Size);
        }
    }

    [Fact]
    public void ParticlePool_Enumerator_IteratesAllAlive()
    {
        var pool = new ParticlePool(100);
        
        for (int i = 0; i < 5; i++)
        {
            var p = Particle.Create(new Vector3(i, 0, 0), Vector3.UnitY, Vector4.One, 1f, 1f);
            pool.Emit(in p);
        }

        int count = 0;
        foreach (ref var particle in pool)
        {
            Assert.Equal(count, particle.Position.X);
            count++;
        }

        Assert.Equal(5, count);
    }

    [Fact]
    public void ParticlePool_SortByDistance_SortsBackToFront()
    {
        var pool = new ParticlePool(100);
        var cameraPosition = Vector3.Zero;
        
        // Add particles at different distances
        pool.Emit(Particle.Create(new Vector3(1, 0, 0), Vector3.Zero, Vector4.One, 1f, 1f)); // Near
        pool.Emit(Particle.Create(new Vector3(5, 0, 0), Vector3.Zero, Vector4.One, 2f, 1f)); // Far
        pool.Emit(Particle.Create(new Vector3(3, 0, 0), Vector3.Zero, Vector4.One, 3f, 1f)); // Medium

        pool.SortByDistance(cameraPosition);

        var particles = pool.GetAliveParticles();
        // Should be sorted back to front (farthest first)
        Assert.Equal(5f, particles[0].Position.X); // Farthest
        Assert.Equal(3f, particles[1].Position.X); // Medium
        Assert.Equal(1f, particles[2].Position.X); // Nearest
    }

    [Fact]
    public void ParticlePool_SortByDepth_SortsAlongForwardVector()
    {
        var pool = new ParticlePool(100);
        var cameraPosition = Vector3.Zero;
        var cameraForward = Vector3.UnitZ; // Looking along +Z
        
        // Add particles at different depths along Z
        pool.Emit(Particle.Create(new Vector3(0, 0, 1), Vector3.Zero, Vector4.One, 1f, 1f)); // Near
        pool.Emit(Particle.Create(new Vector3(0, 0, 10), Vector3.Zero, Vector4.One, 2f, 1f)); // Far
        pool.Emit(Particle.Create(new Vector3(0, 0, 5), Vector3.Zero, Vector4.One, 3f, 1f)); // Medium

        pool.SortByDepth(cameraPosition, cameraForward);

        var particles = pool.GetAliveParticles();
        // Should be sorted back to front (farthest first)
        Assert.Equal(10f, particles[0].Position.Z); // Farthest
        Assert.Equal(5f, particles[1].Position.Z);  // Medium
        Assert.Equal(1f, particles[2].Position.Z);  // Nearest
    }

    [Fact]
    public void ParticlePool_SortByAge_SortsOldestFirst()
    {
        var pool = new ParticlePool(100);
        
        // Add particles with different ages (different initial lifetimes, same remaining)
        var p1 = Particle.Create(Vector3.Zero, Vector3.Zero, Vector4.One, 1f, 10f);
        p1.Lifetime = 5f; // 50% through life
        pool.Emit(in p1);
        
        var p2 = Particle.Create(Vector3.UnitX, Vector3.Zero, Vector4.One, 2f, 10f);
        p2.Lifetime = 2f; // 80% through life (oldest)
        pool.Emit(in p2);
        
        var p3 = Particle.Create(Vector3.UnitY, Vector3.Zero, Vector4.One, 3f, 10f);
        p3.Lifetime = 8f; // 20% through life (youngest)
        pool.Emit(in p3);

        pool.SortByAge(reverse: false);

        var particles = pool.GetAliveParticles();
        // Oldest first (highest NormalizedAge)
        Assert.Equal(Vector3.UnitX, particles[0].Position); // 80% through
        Assert.Equal(Vector3.Zero, particles[1].Position);  // 50% through
        Assert.Equal(Vector3.UnitY, particles[2].Position); // 20% through
    }

    [Fact]
    public void ParticlePool_Sort_WithSortMode_CallsCorrectMethod()
    {
        var pool = new ParticlePool(100);
        
        pool.Emit(Particle.Create(new Vector3(1, 0, 0), Vector3.Zero, Vector4.One, 1f, 1f));
        pool.Emit(Particle.Create(new Vector3(5, 0, 0), Vector3.Zero, Vector4.One, 2f, 1f));

        // Should not throw for any mode
        pool.Sort(ParticleSortMode.None, Vector3.Zero, Vector3.UnitZ);
        pool.Sort(ParticleSortMode.ByDistance, Vector3.Zero, Vector3.UnitZ);
        pool.Sort(ParticleSortMode.ByDepth, Vector3.Zero, Vector3.UnitZ);
        pool.Sort(ParticleSortMode.ByAge, Vector3.Zero, Vector3.UnitZ);
        pool.Sort(ParticleSortMode.ByAgeReverse, Vector3.Zero, Vector3.UnitZ);

        Assert.Equal(2, pool.AliveCount);
    }

    [Fact]
    public void ParticlePool_SortByDistance_PreservesParticleData()
    {
        var pool = new ParticlePool(100);
        
        // Add particles with distinct sizes to track them
        pool.Emit(Particle.Create(new Vector3(1, 0, 0), Vector3.Zero, new Vector4(1, 0, 0, 1), 1f, 1f));
        pool.Emit(Particle.Create(new Vector3(5, 0, 0), Vector3.Zero, new Vector4(0, 1, 0, 1), 2f, 1f));
        pool.Emit(Particle.Create(new Vector3(3, 0, 0), Vector3.Zero, new Vector4(0, 0, 1, 1), 3f, 1f));

        pool.SortByDistance(Vector3.Zero);

        var particles = pool.GetAliveParticles();
        
        // Verify data integrity after sort
        Assert.Equal(5f, particles[0].Position.X);
        Assert.Equal(new Vector4(0, 1, 0, 1), particles[0].Color); // Green was at x=5
        Assert.Equal(2f, particles[0].Size);

        Assert.Equal(3f, particles[1].Position.X);
        Assert.Equal(new Vector4(0, 0, 1, 1), particles[1].Color); // Blue was at x=3
        Assert.Equal(3f, particles[1].Size);

        Assert.Equal(1f, particles[2].Position.X);
        Assert.Equal(new Vector4(1, 0, 0, 1), particles[2].Color); // Red was at x=1
        Assert.Equal(1f, particles[2].Size);
    }

    [Fact]
    public void ParticlePool_Sort_HandlesSingleParticle()
    {
        var pool = new ParticlePool(100);
        pool.Emit(Particle.Create(new Vector3(5, 0, 0), Vector3.Zero, Vector4.One, 1f, 1f));

        pool.SortByDistance(Vector3.Zero);

        Assert.Equal(1, pool.AliveCount);
        Assert.Equal(5f, pool.GetParticle(0).Position.X);
    }

    [Fact]
    public void ParticlePool_Sort_HandlesEmptyPool()
    {
        var pool = new ParticlePool(100);

        // Should not throw
        pool.SortByDistance(Vector3.Zero);
        pool.SortByDepth(Vector3.Zero, Vector3.UnitZ);
        pool.SortByAge();

        Assert.Equal(0, pool.AliveCount);
    }

    [Fact]
    public void ParticlePool_SortByDistance_HandlesLargePool()
    {
        var pool = new ParticlePool(10000);
        var random = new Random(42);
        
        // Add many particles at random distances
        for (int i = 0; i < 1000; i++)
        {
            float dist = (float)random.NextDouble() * 100f;
            pool.Emit(Particle.Create(new Vector3(dist, 0, 0), Vector3.Zero, Vector4.One, 1f, 1f));
        }

        pool.SortByDistance(Vector3.Zero);

        // Verify sorted in descending order (back to front)
        var particles = pool.GetAliveParticles();
        for (int i = 0; i < particles.Length - 1; i++)
        {
            Assert.True(particles[i].Position.X >= particles[i + 1].Position.X,
                $"Particle {i} (dist={particles[i].Position.X}) should be >= particle {i + 1} (dist={particles[i + 1].Position.X})");
        }
    }
}

public class EmissionShapeTests
{
    [Fact]
    public void EmissionShape_Point_ReturnsZeroPosition()
    {
        var shape = EmissionShape.Point;
        var random = new Random(42);

        for (int i = 0; i < 10; i++)
        {
            shape.Sample(random, out Vector3 position, out Vector3 direction);
            Assert.Equal(Vector3.Zero, position);
            // Direction should be normalized
            Assert.Equal(1f, direction.Length(), 0.001f);
        }
    }

    [Fact]
    public void EmissionShape_Sphere_ReturnsPositionWithinRadius()
    {
        var shape = EmissionShape.Sphere(5f);
        var random = new Random(42);

        for (int i = 0; i < 100; i++)
        {
            shape.Sample(random, out Vector3 position, out Vector3 direction);
            Assert.True(position.Length() <= 5f + 0.001f);
            Assert.Equal(1f, direction.Length(), 0.001f);
        }
    }

    [Fact]
    public void EmissionShape_SphereSurface_ReturnsPositionOnSurface()
    {
        var shape = EmissionShape.Sphere(3f, surface: true);
        var random = new Random(42);

        for (int i = 0; i < 100; i++)
        {
            shape.Sample(random, out Vector3 position, out Vector3 direction);
            Assert.Equal(3f, position.Length(), 0.01f);
        }
    }

    [Fact]
    public void EmissionShape_Hemisphere_ReturnsPositionAboveHorizon()
    {
        var shape = EmissionShape.Hemisphere(5f);
        var random = new Random(42);

        for (int i = 0; i < 100; i++)
        {
            shape.Sample(random, out Vector3 position, out Vector3 direction);
            Assert.True(position.Length() <= 5f + 0.001f);
            Assert.True(direction.Y >= 0);
        }
    }

    [Fact]
    public void EmissionShape_Box_ReturnsPositionWithinExtents()
    {
        var extents = new Vector3(2, 3, 4);
        var shape = EmissionShape.Box(extents);
        var random = new Random(42);

        for (int i = 0; i < 100; i++)
        {
            shape.Sample(random, out Vector3 position, out Vector3 direction);
            Assert.True(Math.Abs(position.X) <= extents.X + 0.001f);
            Assert.True(Math.Abs(position.Y) <= extents.Y + 0.001f);
            Assert.True(Math.Abs(position.Z) <= extents.Z + 0.001f);
        }
    }

    [Fact]
    public void EmissionShape_Circle_ReturnsPositionInCircle()
    {
        var shape = EmissionShape.Circle(4f);
        var random = new Random(42);

        for (int i = 0; i < 100; i++)
        {
            shape.Sample(random, out Vector3 position, out Vector3 direction);
            Assert.Equal(0, position.Y, 0.001f);
            float dist = MathF.Sqrt(position.X * position.X + position.Z * position.Z);
            Assert.True(dist <= 4f + 0.001f);
        }
    }

    [Fact]
    public void EmissionShape_Cone_ReturnsDirectionWithinAngle()
    {
        var shape = EmissionShape.Cone(30f); // 30 degree half-angle
        var random = new Random(42);

        for (int i = 0; i < 100; i++)
        {
            shape.Sample(random, out Vector3 position, out Vector3 direction);
            
            // Direction should be within cone (Y >= cos(angle))
            float cosAngle = MathF.Cos(30f * MathF.PI / 180f);
            Assert.True(direction.Y >= cosAngle - 0.001f);
        }
    }

    [Fact]
    public void EmissionShape_Edge_ReturnsPositionOnLine()
    {
        var shape = EmissionShape.Edge(10f);
        var random = new Random(42);

        for (int i = 0; i < 100; i++)
        {
            shape.Sample(random, out Vector3 position, out Vector3 direction);
            Assert.Equal(0, position.Y, 0.001f);
            Assert.Equal(0, position.Z, 0.001f);
            Assert.True(Math.Abs(position.X) <= 5f + 0.001f);
        }
    }
}

public class ColorGradientTests
{
    [Fact]
    public void ColorGradient_Evaluate_ReturnsStartColorAtZero()
    {
        var gradient = new ColorGradient(
            new GradientStop(0f, Vector4.One),
            new GradientStop(1f, Vector4.Zero)
        );

        var color = gradient.Evaluate(0f);

        Assert.Equal(Vector4.One, color);
    }

    [Fact]
    public void ColorGradient_Evaluate_ReturnsEndColorAtOne()
    {
        var gradient = new ColorGradient(
            new GradientStop(0f, Vector4.One),
            new GradientStop(1f, Vector4.Zero)
        );

        var color = gradient.Evaluate(1f);

        Assert.Equal(Vector4.Zero, color);
    }

    [Fact]
    public void ColorGradient_Evaluate_InterpolatesCorrectly()
    {
        var gradient = new ColorGradient(
            new GradientStop(0f, new Vector4(0, 0, 0, 1)),
            new GradientStop(1f, new Vector4(1, 1, 1, 1))
        );

        var color = gradient.Evaluate(0.5f);

        Assert.Equal(0.5f, color.X, 0.001f);
        Assert.Equal(0.5f, color.Y, 0.001f);
        Assert.Equal(0.5f, color.Z, 0.001f);
    }

    [Fact]
    public void ColorGradient_Evaluate_HandlesMultipleStops()
    {
        var gradient = new ColorGradient(
            new GradientStop(0f, new Vector4(1, 0, 0, 1)),   // Red
            new GradientStop(0.5f, new Vector4(0, 1, 0, 1)), // Green
            new GradientStop(1f, new Vector4(0, 0, 1, 1))    // Blue
        );

        var atQuarter = gradient.Evaluate(0.25f);
        var atHalf = gradient.Evaluate(0.5f);
        var atThreeQuarters = gradient.Evaluate(0.75f);

        // At 0.25, should be between red and green
        Assert.True(atQuarter.X > 0 && atQuarter.Y > 0);
        
        // At 0.5, should be pure green
        Assert.Equal(0, atHalf.X, 0.001f);
        Assert.Equal(1, atHalf.Y, 0.001f);
        Assert.Equal(0, atHalf.Z, 0.001f);
        
        // At 0.75, should be between green and blue
        Assert.True(atThreeQuarters.Y > 0 && atThreeQuarters.Z > 0);
    }

    [Fact]
    public void ColorGradient_Fire_ReturnsValidColors()
    {
        var gradient = ColorGradient.Fire;

        for (float t = 0; t <= 1; t += 0.1f)
        {
            var color = gradient.Evaluate(t);
            Assert.True(color.X >= 0 && color.X <= 1);
            Assert.True(color.Y >= 0 && color.Y <= 1);
            Assert.True(color.Z >= 0 && color.Z <= 1);
            Assert.True(color.W >= 0 && color.W <= 1);
        }
    }
}

public class AnimationCurveTests
{
    [Fact]
    public void AnimationCurve_Constant_ReturnsSameValue()
    {
        var curve = AnimationCurve.Constant(0.5f);

        Assert.Equal(0.5f, curve.Evaluate(0f), 0.001f);
        Assert.Equal(0.5f, curve.Evaluate(0.5f), 0.001f);
        Assert.Equal(0.5f, curve.Evaluate(1f), 0.001f);
    }

    [Fact]
    public void AnimationCurve_Linear_InterpolatesLinearly()
    {
        var curve = AnimationCurve.Linear;

        Assert.Equal(0f, curve.Evaluate(0f), 0.001f);
        Assert.Equal(0.5f, curve.Evaluate(0.5f), 0.001f);
        Assert.Equal(1f, curve.Evaluate(1f), 0.001f);
    }

    [Fact]
    public void AnimationCurve_Custom_HandlesMultipleKeys()
    {
        var curve = new AnimationCurve(
            (0f, 0f),
            (0.25f, 1f),
            (0.5f, 0.5f),
            (1f, 0f)
        );

        Assert.Equal(0f, curve.Evaluate(0f), 0.001f);
        Assert.Equal(1f, curve.Evaluate(0.25f), 0.001f);
        Assert.Equal(0.5f, curve.Evaluate(0.5f), 0.001f);
        Assert.Equal(0f, curve.Evaluate(1f), 0.001f);
    }
}

public class FloatRangeTests
{
    [Fact]
    public void FloatRange_SingleValue_AlwaysReturnsThatValue()
    {
        var range = new FloatRange(5f);
        var random = new Random(42);

        for (int i = 0; i < 10; i++)
        {
            Assert.Equal(5f, range.Evaluate(random));
        }
    }

    [Fact]
    public void FloatRange_MinMax_ReturnsValuesInRange()
    {
        var range = new FloatRange(0f, 10f);
        var random = new Random(42);

        for (int i = 0; i < 100; i++)
        {
            float value = range.Evaluate(random);
            Assert.True(value >= 0f && value <= 10f);
        }
    }

    [Fact]
    public void FloatRange_ImplicitConversion_Works()
    {
        FloatRange range = 7f;
        var random = new Random(42);

        Assert.Equal(7f, range.Evaluate(random));
    }
}

public class ParticleModuleTests
{
    [Fact]
    public void ColorOverLifetimeModule_Apply_ChangesColor()
    {
        var module = new ColorOverLifetimeModule
        {
            Gradient = new ColorGradient(
                new GradientStop(0f, new Vector4(1, 0, 0, 1)),
                new GradientStop(1f, new Vector4(0, 0, 1, 1))
            )
        };

        var particle = Particle.Create(Vector3.Zero, Vector3.Zero, Vector4.One, 1f, 10f);
        particle.Lifetime = 5f; // 50% through life

        module.Apply(ref particle);

        // Should be purple-ish (between red and blue)
        Assert.True(particle.Color.X > 0.4f && particle.Color.X < 0.6f);
        Assert.True(particle.Color.Z > 0.4f && particle.Color.Z < 0.6f);
    }

    [Fact]
    public void SizeOverLifetimeModule_Apply_ChangesSize()
    {
        var module = new SizeOverLifetimeModule
        {
            SizeMultiplier = AnimationCurve.FadeOut
        };

        var particle = Particle.Create(Vector3.Zero, Vector3.Zero, Vector4.One, 2f, 10f);
        particle.Lifetime = 5f; // 50% through life

        module.Apply(ref particle, 2f);

        // Size should be about 1.0 (2 * 0.5)
        Assert.Equal(1f, particle.Size, 0.1f);
    }

    [Fact]
    public void VelocityOverLifetimeModule_Apply_AddsLinearVelocity()
    {
        var module = new VelocityOverLifetimeModule
        {
            LinearVelocity = new Vector3(1, 0, 0)
        };

        var particle = Particle.Create(Vector3.Zero, Vector3.Zero, Vector4.One, 1f, 1f);

        module.Apply(ref particle, 1f, Vector3.Zero);

        Assert.Equal(1f, particle.Velocity.X, 0.001f);
    }

    [Fact]
    public void NoiseModule_Apply_ModifiesVelocity()
    {
        var module = new NoiseModule
        {
            Strength = 1f,
            Frequency = 1f
        };

        var particle = Particle.Create(new Vector3(1, 2, 3), Vector3.Zero, Vector4.One, 1f, 1f);
        var originalVelocity = particle.Velocity;

        module.Apply(ref particle, 0.1f, new Random(42));

        // Velocity should be different
        Assert.NotEqual(originalVelocity, particle.Velocity);
    }
}

public class ParticleEmitterComponentTests
{
    [Fact]
    public void ParticleEmitterComponent_DefaultValues_AreValid()
    {
        var emitter = new ParticleEmitterComponent();

        Assert.True(emitter.IsPlaying);
        Assert.Equal(10f, emitter.EmissionRate);
        Assert.Equal(1000, emitter.MaxParticles);
        Assert.True(emitter.Looping);
    }

    [Fact]
    public void ParticleEmitterComponent_Reset_DoesNotThrow()
    {
        var emitter = new ParticleEmitterComponent();
        
        // Reset should work even when pool is null
        var exception = Record.Exception(() => emitter.Reset());
        Assert.Null(exception);
    }

    [Fact]
    public void ParticleEmitterComponent_PlayStop_ChangesState()
    {
        var emitter = new ParticleEmitterComponent();

        Assert.True(emitter.IsPlaying);
        
        emitter.Stop();
        Assert.False(emitter.IsPlaying);
        
        emitter.Play();
        Assert.True(emitter.IsPlaying);
    }

    [Fact]
    public void ParticleEmitterComponent_AliveCount_ReturnsZeroWithoutPool()
    {
        var emitter = new ParticleEmitterComponent();

        // Without a pool, AliveCount should return 0
        Assert.Equal(0, emitter.AliveCount);
    }

    [Fact]
    public void ParticleEmitterComponent_Clear_StopsPlaying()
    {
        var emitter = new ParticleEmitterComponent();
        Assert.True(emitter.IsPlaying);

        emitter.Clear();

        Assert.False(emitter.IsPlaying);
    }

    [Fact]
    public void ParticleEmitterComponent_Emit_AddsBurstToList()
    {
        var emitter = new ParticleEmitterComponent();
        int initialBurstCount = emitter.Bursts.Count;

        emitter.Emit(50);

        Assert.Equal(initialBurstCount + 1, emitter.Bursts.Count);
    }
}

public class ParticleBurstTests
{
    [Fact]
    public void ParticleBurst_Create_SetsCorrectValues()
    {
        var burst = ParticleBurst.Create(1.5f, 10);

        Assert.Equal(1.5f, burst.Time);
        Assert.Equal(10, burst.MinCount);
        Assert.Equal(10, burst.MaxCount);
        Assert.Equal(1f, burst.Probability);
    }

    [Fact]
    public void ParticleBurst_CreateWithRange_SetsCorrectValues()
    {
        var burst = ParticleBurst.Create(2f, 5, 15);

        Assert.Equal(2f, burst.Time);
        Assert.Equal(5, burst.MinCount);
        Assert.Equal(15, burst.MaxCount);
    }
}

public class ParticlePresetsTests
{
    [Fact]
    public void ParticlePresets_Fire_CreatesValidEmitter()
    {
        var emitter = ParticlePresets.Fire();

        Assert.True(emitter.EmissionRate > 0);
        Assert.True(emitter.MaxParticles > 0);
        Assert.NotNull(emitter.ColorOverLifetime);
        Assert.NotNull(emitter.SizeOverLifetime);
    }

    [Fact]
    public void ParticlePresets_Smoke_CreatesValidEmitter()
    {
        var emitter = ParticlePresets.Smoke();

        Assert.True(emitter.EmissionRate > 0);
        Assert.NotNull(emitter.ColorOverLifetime);
        Assert.NotNull(emitter.Noise);
    }

    [Fact]
    public void ParticlePresets_Explosion_UsesBurstEmission()
    {
        var emitter = ParticlePresets.Explosion();

        Assert.Equal(0f, emitter.EmissionRate);
        Assert.True(emitter.Bursts.Count > 0);
        Assert.False(emitter.Looping);
    }

    [Fact]
    public void ParticlePresets_AllPresets_ArePlayable()
    {
        var presets = new[]
        {
            ParticlePresets.Fire(),
            ParticlePresets.Smoke(),
            ParticlePresets.Explosion(),
            ParticlePresets.Sparkles(),
            ParticlePresets.Rain(),
            ParticlePresets.Snow(),
            ParticlePresets.Dust(),
            ParticlePresets.Plasma()
        };

        foreach (var emitter in presets)
        {
            Assert.True(emitter.IsPlaying);
            Assert.True(emitter.MaxParticles > 0);
            Assert.True(emitter.Lifetime.Max > 0);
        }
    }
}

public class ParticlePoolManagerTests
{
    [Fact]
    public void ParticlePoolManager_GetOrCreatePool_CreatesNewPool()
    {
        var manager = new ParticlePoolManager();

        var pool = manager.GetOrCreatePool("test", 100);

        Assert.NotNull(pool);
        Assert.Equal(100, pool.Capacity);
    }

    [Fact]
    public void ParticlePoolManager_GetOrCreatePool_ReturnsSamePool()
    {
        var manager = new ParticlePoolManager();

        var pool1 = manager.GetOrCreatePool("test", 100);
        var pool2 = manager.GetOrCreatePool("test", 200); // Different capacity ignored

        Assert.Same(pool1, pool2);
    }

    [Fact]
    public void ParticlePoolManager_TotalAliveCount_SumsAllPools()
    {
        var manager = new ParticlePoolManager();
        var pool1 = manager.GetOrCreatePool("pool1", 100);
        var pool2 = manager.GetOrCreatePool("pool2", 100);

        pool1.Emit(Particle.Create(Vector3.Zero, Vector3.Zero, Vector4.One, 1f, 1f));
        pool1.Emit(Particle.Create(Vector3.Zero, Vector3.Zero, Vector4.One, 1f, 1f));
        pool2.Emit(Particle.Create(Vector3.Zero, Vector3.Zero, Vector4.One, 1f, 1f));

        Assert.Equal(3, manager.TotalAliveCount);
    }

    [Fact]
    public void ParticlePoolManager_ClearAll_ClearsAllPools()
    {
        var manager = new ParticlePoolManager();
        var pool1 = manager.GetOrCreatePool("pool1", 100);
        var pool2 = manager.GetOrCreatePool("pool2", 100);

        pool1.Emit(Particle.Create(Vector3.Zero, Vector3.Zero, Vector4.One, 1f, 1f));
        pool2.Emit(Particle.Create(Vector3.Zero, Vector3.Zero, Vector4.One, 1f, 1f));

        manager.ClearAll();

        Assert.Equal(0, manager.TotalAliveCount);
    }

    [Fact]
    public void ParticlePoolManager_CollectRenderData_CombinesAllPools()
    {
        var manager = new ParticlePoolManager();
        var pool1 = manager.GetOrCreatePool("pool1", 100);
        var pool2 = manager.GetOrCreatePool("pool2", 100);

        pool1.Emit(Particle.Create(new Vector3(1, 0, 0), Vector3.Zero, Vector4.One, 1f, 1f));
        pool2.Emit(Particle.Create(new Vector3(2, 0, 0), Vector3.Zero, Vector4.One, 1f, 1f));
        pool2.Emit(Particle.Create(new Vector3(3, 0, 0), Vector3.Zero, Vector4.One, 1f, 1f));

        var renderData = manager.CollectRenderData();

        Assert.Equal(3, renderData.Length);
    }
}
