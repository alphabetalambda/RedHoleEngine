using System.Numerics;
using RedHoleEngine.Components;
using RedHoleEngine.Core.ECS;

namespace RedHoleEngine.Particles;

/// <summary>
/// ECS system that updates all particle emitters.
/// Handles emission, simulation, and particle lifecycle.
/// </summary>
public class ParticleSystem : ComponentSystem<ParticleEmitterComponent, TransformComponent>
{
    /// <summary>
    /// Gravity acceleration (default: Earth gravity pointing down)
    /// </summary>
    public Vector3 Gravity { get; set; } = new(0, -9.81f, 0);

    /// <summary>
    /// Global time scale for all particles
    /// </summary>
    public float TimeScale { get; set; } = 1f;

    /// <summary>
    /// Total number of particles across all emitters
    /// </summary>
    public int TotalParticleCount { get; private set; }

    /// <summary>
    /// Camera position for distance-based sorting (set by renderer)
    /// </summary>
    public Vector3 CameraPosition { get; set; }
    
    /// <summary>
    /// Camera forward direction for depth-based sorting (set by renderer)
    /// </summary>
    public Vector3 CameraForward { get; set; } = -Vector3.UnitZ;

    /// <summary>
    /// Called for each entity with ParticleEmitterComponent and TransformComponent
    /// </summary>
    protected override void Process(Entity entity, ref ParticleEmitterComponent emitter, ref TransformComponent transform, float deltaTime)
    {
        float dt = deltaTime * TimeScale * emitter.SimulationSpeed;

        // Ensure pool exists
        emitter.Pool ??= new ParticlePool(emitter.MaxParticles);

        // Handle delay
        if (emitter.ElapsedTime < emitter.StartDelay)
        {
            emitter.ElapsedTime += dt;
            UpdateParticles(ref emitter, dt, transform.Position);
            return;
        }

        // Check duration
        bool withinDuration = emitter.Duration <= 0 || 
                              (emitter.ElapsedTime - emitter.StartDelay) < emitter.Duration;

        if (emitter.IsPlaying && withinDuration)
        {
            // Process bursts
            ProcessBursts(ref emitter, transform.Position);

            // Continuous emission
            ProcessContinuousEmission(ref emitter, dt, transform.Position);
        }
        else if (emitter.Looping && !withinDuration)
        {
            // Reset for looping
            emitter.ElapsedTime = emitter.StartDelay;
            emitter.NextBurstIndex = 0;
        }

        emitter.ElapsedTime += dt;

        // Update existing particles
        UpdateParticles(ref emitter, dt, transform.Position);

        // Sort if needed
        if (emitter.SortMode != ParticleSortMode.None)
        {
            emitter.Pool.Sort(emitter.SortMode, CameraPosition, CameraForward);
        }
    }

    /// <summary>
    /// Process burst emissions
    /// </summary>
    private void ProcessBursts(ref ParticleEmitterComponent emitter, Vector3 emitterPosition)
    {
        float timeSinceStart = emitter.ElapsedTime - emitter.StartDelay;

        while (emitter.NextBurstIndex < emitter.Bursts.Count)
        {
            var burst = emitter.Bursts[emitter.NextBurstIndex];

            if (burst.Time > timeSinceStart)
                break;

            // Check probability
            if (emitter.Random.NextDouble() <= burst.Probability)
            {
                int count = emitter.Random.Next(burst.MinCount, burst.MaxCount + 1);
                EmitParticles(ref emitter, count, emitterPosition);
            }

            emitter.NextBurstIndex++;

            // Handle burst cycles
            if (burst.Cycles > 0 && burst.Interval > 0)
            {
                // Add repeated bursts
                for (int cycle = 1; cycle <= burst.Cycles; cycle++)
                {
                    emitter.Bursts.Add(new ParticleBurst
                    {
                        Time = burst.Time + cycle * burst.Interval,
                        MinCount = burst.MinCount,
                        MaxCount = burst.MaxCount,
                        Probability = burst.Probability,
                        Cycles = 0 // Don't recurse
                    });
                }

                // Re-sort bursts
                emitter.Bursts.Sort((a, b) => a.Time.CompareTo(b.Time));
            }
        }
    }

    /// <summary>
    /// Process continuous emission based on rate
    /// </summary>
    private void ProcessContinuousEmission(ref ParticleEmitterComponent emitter, float deltaTime, Vector3 emitterPosition)
    {
        if (emitter.EmissionRate <= 0)
            return;

        float particlesPerSecond = emitter.EmissionRate;
        emitter.EmissionAccumulator += particlesPerSecond * deltaTime;

        int particlesToEmit = (int)emitter.EmissionAccumulator;
        emitter.EmissionAccumulator -= particlesToEmit;

        EmitParticles(ref emitter, particlesToEmit, emitterPosition);
    }

    /// <summary>
    /// Emit a number of particles
    /// </summary>
    private void EmitParticles(ref ParticleEmitterComponent emitter, int count, Vector3 emitterPosition)
    {
        var pool = emitter.Pool!;

        for (int i = 0; i < count && !pool.IsFull; i++)
        {
            // Sample position and direction from shape
            emitter.Shape.Sample(emitter.Random, out Vector3 localPos, out Vector3 direction);

            // Convert to world space
            Vector3 worldPosition = emitter.SimulationSpace == ParticleSimulationSpace.World
                ? emitterPosition + localPos
                : localPos;

            // Calculate velocity
            float speed = emitter.StartSpeed.Evaluate(emitter.Random);
            Vector3 velocity = direction * speed;

            // Create particle
            var particle = Particle.Create(
                position: worldPosition,
                velocity: velocity,
                color: emitter.StartColor,
                size: emitter.StartSize.Evaluate(emitter.Random),
                lifetime: emitter.Lifetime.Evaluate(emitter.Random),
                rotation: emitter.StartRotation.Evaluate(emitter.Random) * MathF.PI / 180f,
                angularVelocity: emitter.AngularVelocity.Evaluate(emitter.Random) * MathF.PI / 180f
            );

            pool.Emit(in particle);
        }
    }

    /// <summary>
    /// Update all particles in the emitter
    /// </summary>
    private void UpdateParticles(ref ParticleEmitterComponent emitter, float deltaTime, Vector3 emitterPosition)
    {
        var pool = emitter.Pool!;
        var particles = pool.GetAliveParticles();

        for (int i = 0; i < particles.Length; i++)
        {
            ref var p = ref particles[i];

            // Update lifetime
            p.Lifetime -= deltaTime;
            if (p.Lifetime <= 0)
            {
                p.IsAlive = false;
                continue;
            }

            // Apply gravity
            if (MathF.Abs(emitter.GravityMultiplier) > 0.001f)
            {
                p.Velocity += Gravity * emitter.GravityMultiplier * deltaTime;
            }

            // Apply modules
            if (emitter.VelocityOverLifetime != null)
            {
                emitter.VelocityOverLifetime.Apply(ref p, deltaTime, emitterPosition);
            }

            if (emitter.Noise != null)
            {
                emitter.Noise.Apply(ref p, deltaTime, emitter.Random);
            }

            // Update position
            p.Position += p.Velocity * deltaTime;

            // Update rotation
            p.Rotation += p.AngularVelocity * deltaTime;

            // Apply color over lifetime
            if (emitter.ColorOverLifetime != null)
            {
                emitter.ColorOverLifetime.Apply(ref p);
            }

            // Apply size over lifetime
            if (emitter.SizeOverLifetime != null)
            {
                float baseSize = pool.GetBaseSize(i);
                emitter.SizeOverLifetime.Apply(ref p, baseSize);
            }
        }

        // Remove dead particles
        pool.RemoveDeadParticles();
    }

    /// <summary>
    /// Called after Update to track total particle count
    /// </summary>
    public override void Update(float deltaTime)
    {
        base.Update(deltaTime);

        // Count total particles
        TotalParticleCount = 0;
        var pool = World.GetPool<ParticleEmitterComponent>();
        foreach (var entityId in pool.GetEntityIds())
        {
            ref var emitter = ref pool.Get(entityId);
            TotalParticleCount += emitter.Pool?.AliveCount ?? 0;
        }
    }
}

/// <summary>
/// Helper class with common particle effect presets
/// </summary>
public static class ParticlePresets
{
    /// <summary>
    /// Fire effect
    /// </summary>
    public static ParticleEmitterComponent Fire()
    {
        return new ParticleEmitterComponent
        {
            EmissionRate = 50f,
            MaxParticles = 500,
            Lifetime = new FloatRange(0.5f, 1.5f),
            StartSpeed = new FloatRange(2f, 4f),
            StartSize = new FloatRange(0.1f, 0.3f),
            StartColor = new Vector4(1f, 0.8f, 0.2f, 1f),
            GravityMultiplier = -0.5f, // Rise up
            Shape = EmissionShape.Cone(15f, 0.2f),
            SortMode = ParticleSortMode.ByDepth, // Needed for overlapping flames
            ColorOverLifetime = new ColorOverLifetimeModule
            {
                Gradient = ColorGradient.Fire
            },
            SizeOverLifetime = SizeOverLifetimeModule.GrowAndFade
        };
    }

    /// <summary>
    /// Smoke effect
    /// </summary>
    public static ParticleEmitterComponent Smoke()
    {
        return new ParticleEmitterComponent
        {
            EmissionRate = 20f,
            MaxParticles = 200,
            Lifetime = new FloatRange(2f, 4f),
            StartSpeed = new FloatRange(0.5f, 1.5f),
            StartSize = new FloatRange(0.3f, 0.6f),
            StartColor = new Vector4(0.5f, 0.5f, 0.5f, 0.6f),
            GravityMultiplier = -0.1f,
            Shape = EmissionShape.Circle(0.3f),
            SortMode = ParticleSortMode.ByDepth, // Important for overlapping smoke
            ColorOverLifetime = new ColorOverLifetimeModule
            {
                Gradient = ColorGradient.Smoke
            },
            SizeOverLifetime = new SizeOverLifetimeModule
            {
                SizeMultiplier = new AnimationCurve((0f, 0.5f), (0.5f, 1f), (1f, 2f))
            },
            Noise = new NoiseModule
            {
                Strength = 0.5f,
                Frequency = 0.5f
            }
        };
    }

    /// <summary>
    /// Explosion effect (burst-based)
    /// </summary>
    public static ParticleEmitterComponent Explosion()
    {
        return new ParticleEmitterComponent
        {
            EmissionRate = 0f, // Burst only
            MaxParticles = 100,
            Duration = 0.1f,
            Looping = false,
            Lifetime = new FloatRange(0.3f, 0.8f),
            StartSpeed = new FloatRange(5f, 15f),
            StartSize = new FloatRange(0.1f, 0.3f),
            StartColor = new Vector4(1f, 0.6f, 0.1f, 1f),
            GravityMultiplier = 1f,
            Shape = EmissionShape.Sphere(0.1f, surface: true),
            SortMode = ParticleSortMode.ByDistance, // Fast sorting for burst particles
            Bursts = new List<ParticleBurst>
            {
                ParticleBurst.Create(0f, 50, 100)
            },
            ColorOverLifetime = new ColorOverLifetimeModule
            {
                Gradient = new ColorGradient(
                    new GradientStop(0f, new Vector4(1f, 1f, 0.5f, 1f)),
                    new GradientStop(0.2f, new Vector4(1f, 0.5f, 0.1f, 1f)),
                    new GradientStop(0.5f, new Vector4(0.8f, 0.2f, 0.05f, 0.8f)),
                    new GradientStop(1f, new Vector4(0.3f, 0.1f, 0.05f, 0f))
                )
            },
            SizeOverLifetime = SizeOverLifetimeModule.Shrink
        };
    }

    /// <summary>
    /// Sparkle effect
    /// </summary>
    public static ParticleEmitterComponent Sparkles()
    {
        return new ParticleEmitterComponent
        {
            EmissionRate = 30f,
            MaxParticles = 300,
            Lifetime = new FloatRange(0.5f, 1f),
            StartSpeed = new FloatRange(1f, 3f),
            StartSize = new FloatRange(0.02f, 0.05f),
            StartColor = new Vector4(1f, 1f, 1f, 1f),
            GravityMultiplier = 0.3f,
            Shape = EmissionShape.Sphere(0.5f),
            ColorOverLifetime = new ColorOverLifetimeModule
            {
                Gradient = new ColorGradient(
                    new GradientStop(0f, new Vector4(1f, 1f, 1f, 1f)),
                    new GradientStop(0.5f, new Vector4(1f, 0.9f, 0.5f, 1f)),
                    new GradientStop(1f, new Vector4(1f, 0.8f, 0.2f, 0f))
                )
            }
        };
    }

    /// <summary>
    /// Rain effect
    /// </summary>
    public static ParticleEmitterComponent Rain()
    {
        return new ParticleEmitterComponent
        {
            EmissionRate = 200f,
            MaxParticles = 2000,
            Lifetime = new FloatRange(1f, 2f),
            StartSpeed = new FloatRange(8f, 12f),
            StartSize = new FloatRange(0.02f, 0.03f),
            StartColor = new Vector4(0.7f, 0.7f, 0.8f, 0.5f),
            GravityMultiplier = 1f,
            Shape = EmissionShape.Box(new Vector3(10f, 0f, 10f)),
            SimulationSpace = ParticleSimulationSpace.World,
            RenderMode = ParticleRenderMode.StretchedBillboard
        };
    }

    /// <summary>
    /// Snow effect
    /// </summary>
    public static ParticleEmitterComponent Snow()
    {
        return new ParticleEmitterComponent
        {
            EmissionRate = 50f,
            MaxParticles = 1000,
            Lifetime = new FloatRange(5f, 8f),
            StartSpeed = new FloatRange(0.5f, 1f),
            StartSize = new FloatRange(0.02f, 0.05f),
            StartRotation = new FloatRange(0f, 360f),
            AngularVelocity = new FloatRange(-45f, 45f),
            StartColor = new Vector4(1f, 1f, 1f, 0.8f),
            GravityMultiplier = 0.1f,
            Shape = EmissionShape.Box(new Vector3(10f, 0f, 10f)),
            Noise = new NoiseModule
            {
                Strength = 0.3f,
                Frequency = 0.2f,
                ScrollSpeed = new Vector3(0.1f, 0, 0.1f)
            }
        };
    }

    /// <summary>
    /// Dust/debris effect
    /// </summary>
    public static ParticleEmitterComponent Dust()
    {
        return new ParticleEmitterComponent
        {
            EmissionRate = 10f,
            MaxParticles = 100,
            Lifetime = new FloatRange(2f, 5f),
            StartSpeed = new FloatRange(0.1f, 0.5f),
            StartSize = new FloatRange(0.01f, 0.03f),
            StartColor = new Vector4(0.6f, 0.5f, 0.4f, 0.3f),
            GravityMultiplier = 0.05f,
            Shape = EmissionShape.Sphere(1f),
            ColorOverLifetime = new ColorOverLifetimeModule
            {
                Gradient = ColorGradient.FadeOut
            },
            Noise = new NoiseModule
            {
                Strength = 0.2f,
                Frequency = 0.3f
            }
        };
    }

    /// <summary>
    /// Plasma/magic effect
    /// </summary>
    public static ParticleEmitterComponent Plasma()
    {
        return new ParticleEmitterComponent
        {
            EmissionRate = 40f,
            MaxParticles = 400,
            Lifetime = new FloatRange(0.5f, 1.2f),
            StartSpeed = new FloatRange(2f, 4f),
            StartSize = new FloatRange(0.1f, 0.2f),
            StartColor = new Vector4(1f, 0f, 1f, 1f),
            GravityMultiplier = 0f,
            Shape = EmissionShape.Sphere(0.2f, surface: true),
            SortMode = ParticleSortMode.ByDepth, // Needed for swirling particles
            ColorOverLifetime = new ColorOverLifetimeModule
            {
                Gradient = ColorGradient.Plasma
            },
            VelocityOverLifetime = new VelocityOverLifetimeModule
            {
                OrbitalVelocity = 180f, // Spin around emitter
                RadialVelocity = new AnimationCurve((0f, 1f), (0.5f, 0f), (1f, -0.5f))
            },
            SizeOverLifetime = new SizeOverLifetimeModule
            {
                SizeMultiplier = new AnimationCurve((0f, 0.3f), (0.3f, 1f), (1f, 0f))
            }
        };
    }
}
