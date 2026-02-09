using System.Numerics;
using RedHoleEngine.Core.ECS;

namespace RedHoleEngine.Particles;

/// <summary>
/// Simulation space for particles
/// </summary>
public enum ParticleSimulationSpace
{
    /// <summary>
    /// Particles move in world space (unaffected by emitter movement)
    /// </summary>
    World,
    
    /// <summary>
    /// Particles move relative to emitter (follow the entity)
    /// </summary>
    Local
}

/// <summary>
/// Render mode for particles
/// </summary>
public enum ParticleRenderMode
{
    Billboard,           // Always face camera
    StretchedBillboard,  // Stretch in velocity direction
    HorizontalBillboard, // Face up
    VerticalBillboard,   // Face camera but stay vertical
    Mesh                 // Render as 3D mesh
}

/// <summary>
/// Configuration for particle bursts
/// </summary>
public struct ParticleBurst
{
    /// <summary>
    /// Time after emitter starts to trigger burst
    /// </summary>
    public float Time;
    
    /// <summary>
    /// Minimum number of particles to emit
    /// </summary>
    public int MinCount;
    
    /// <summary>
    /// Maximum number of particles to emit
    /// </summary>
    public int MaxCount;
    
    /// <summary>
    /// Number of times to repeat the burst (0 = once)
    /// </summary>
    public int Cycles;
    
    /// <summary>
    /// Interval between cycles
    /// </summary>
    public float Interval;
    
    /// <summary>
    /// Probability of burst occurring (0-1)
    /// </summary>
    public float Probability;

    public static ParticleBurst Create(float time, int count) => new()
    {
        Time = time,
        MinCount = count,
        MaxCount = count,
        Cycles = 0,
        Interval = 0,
        Probability = 1f
    };

    public static ParticleBurst Create(float time, int minCount, int maxCount) => new()
    {
        Time = time,
        MinCount = minCount,
        MaxCount = maxCount,
        Cycles = 0,
        Interval = 0,
        Probability = 1f
    };
}

/// <summary>
/// Range for random float values
/// </summary>
public struct FloatRange
{
    public float Min;
    public float Max;

    public FloatRange(float value)
    {
        Min = Max = value;
    }

    public FloatRange(float min, float max)
    {
        Min = min;
        Max = max;
    }

    public readonly float Evaluate(Random random)
    {
        return Min + (float)random.NextDouble() * (Max - Min);
    }

    public static implicit operator FloatRange(float value) => new(value);
}

/// <summary>
/// ECS component for particle emitters.
/// Attach to an entity to emit particles from that entity's position.
/// </summary>
public class ParticleEmitterComponent : IComponent
{
    // ===== Emission Settings =====
    
    /// <summary>
    /// Whether the emitter is currently active
    /// </summary>
    public bool IsPlaying = true;
    
    /// <summary>
    /// Particles emitted per second
    /// </summary>
    public float EmissionRate = 10f;
    
    /// <summary>
    /// Burst configurations
    /// </summary>
    public List<ParticleBurst> Bursts = new();
    
    /// <summary>
    /// Maximum number of particles this emitter can have alive
    /// </summary>
    public int MaxParticles = 1000;
    
    /// <summary>
    /// Duration of the emitter (set to 0 for infinite)
    /// </summary>
    public float Duration = 5f;
    
    /// <summary>
    /// Whether to loop after duration
    /// </summary>
    public bool Looping = true;
    
    /// <summary>
    /// Delay before starting emission
    /// </summary>
    public float StartDelay = 0f;
    
    // ===== Shape =====
    
    /// <summary>
    /// Shape from which particles are emitted
    /// </summary>
    public EmissionShape Shape = EmissionShape.Point;
    
    // ===== Particle Properties =====
    
    /// <summary>
    /// Particle lifetime range
    /// </summary>
    public FloatRange Lifetime = new(1f, 2f);
    
    /// <summary>
    /// Initial speed range
    /// </summary>
    public FloatRange StartSpeed = new(1f, 3f);
    
    /// <summary>
    /// Initial size range
    /// </summary>
    public FloatRange StartSize = new(0.1f, 0.2f);
    
    /// <summary>
    /// Initial rotation range (degrees)
    /// </summary>
    public FloatRange StartRotation = new(0f, 360f);
    
    /// <summary>
    /// Angular velocity range (degrees/second)
    /// </summary>
    public FloatRange AngularVelocity = new(0f);
    
    /// <summary>
    /// Start color
    /// </summary>
    public Vector4 StartColor = Vector4.One;
    
    /// <summary>
    /// Gravity multiplier (1 = full gravity, 0 = no gravity)
    /// </summary>
    public float GravityMultiplier = 0f;
    
    // ===== Simulation =====
    
    /// <summary>
    /// Simulation space
    /// </summary>
    public ParticleSimulationSpace SimulationSpace = ParticleSimulationSpace.World;
    
    /// <summary>
    /// Speed multiplier for the entire simulation
    /// </summary>
    public float SimulationSpeed = 1f;
    
    // ===== Rendering =====
    
    /// <summary>
    /// How particles are rendered
    /// </summary>
    public ParticleRenderMode RenderMode = ParticleRenderMode.Billboard;
    
    /// <summary>
    /// Texture/material identifier (for renderer)
    /// </summary>
    public string? TextureId;
    
    /// <summary>
    /// Sorting mode for correct transparency rendering.
    /// ByDepth is most accurate, ByDistance is faster, None is fastest.
    /// </summary>
    public ParticleSortMode SortMode = ParticleSortMode.None;
    
    /// <summary>
    /// Legacy property - use SortMode instead
    /// </summary>
    [Obsolete("Use SortMode instead")]
    public bool SortByDistance 
    { 
        get => SortMode == ParticleSortMode.ByDistance;
        set => SortMode = value ? ParticleSortMode.ByDistance : ParticleSortMode.None;
    }
    
    // ===== Modules (optional particle modifiers) =====
    
    /// <summary>
    /// Color gradient over particle lifetime
    /// </summary>
    public ColorOverLifetimeModule? ColorOverLifetime;
    
    /// <summary>
    /// Size curve over particle lifetime
    /// </summary>
    public SizeOverLifetimeModule? SizeOverLifetime;
    
    /// <summary>
    /// Velocity modifications over lifetime
    /// </summary>
    public VelocityOverLifetimeModule? VelocityOverLifetime;
    
    /// <summary>
    /// Noise/turbulence module
    /// </summary>
    public NoiseModule? Noise;
    
    // ===== Runtime State (managed by ParticleSystem) =====
    
    /// <summary>
    /// Current time since emitter started
    /// </summary>
    internal float ElapsedTime;
    
    /// <summary>
    /// Accumulated emission (for fractional particles)
    /// </summary>
    internal float EmissionAccumulator;
    
    /// <summary>
    /// Index of next burst to check
    /// </summary>
    internal int NextBurstIndex;
    
    /// <summary>
    /// Random number generator for this emitter
    /// </summary>
    internal Random Random = new();
    
    /// <summary>
    /// Particle pool for this emitter
    /// </summary>
    internal ParticlePool? Pool;

    /// <summary>
    /// Reset emitter to initial state
    /// </summary>
    public void Reset()
    {
        ElapsedTime = 0f;
        EmissionAccumulator = 0f;
        NextBurstIndex = 0;
        Pool?.Clear();
    }

    /// <summary>
    /// Start playing the emitter
    /// </summary>
    public void Play()
    {
        IsPlaying = true;
    }

    /// <summary>
    /// Stop emission (existing particles continue)
    /// </summary>
    public void Stop()
    {
        IsPlaying = false;
    }

    /// <summary>
    /// Stop and clear all particles
    /// </summary>
    public void Clear()
    {
        IsPlaying = false;
        Pool?.Clear();
    }

    /// <summary>
    /// Emit a burst of particles immediately
    /// </summary>
    public void Emit(int count)
    {
        // Will be processed by ParticleSystem on next update
        Bursts.Add(new ParticleBurst
        {
            Time = ElapsedTime,
            MinCount = count,
            MaxCount = count,
            Probability = 1f
        });
    }

    /// <summary>
    /// Get the number of currently alive particles
    /// </summary>
    public int AliveCount => Pool?.AliveCount ?? 0;
}
