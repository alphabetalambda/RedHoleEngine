using System.Numerics;

namespace RedHoleEngine.Particles;

/// <summary>
/// Gradient stop for color interpolation
/// </summary>
public struct GradientStop
{
    public float Time;    // 0-1 normalized time
    public Vector4 Color; // RGBA

    public GradientStop(float time, Vector4 color)
    {
        Time = time;
        Color = color;
    }

    public GradientStop(float time, float r, float g, float b, float a = 1f)
    {
        Time = time;
        Color = new Vector4(r, g, b, a);
    }
}

/// <summary>
/// Color gradient for particle color over lifetime
/// </summary>
public class ColorGradient
{
    private readonly List<GradientStop> _stops = new();

    public ColorGradient()
    {
        // Default white to transparent white
        _stops.Add(new GradientStop(0f, Vector4.One));
        _stops.Add(new GradientStop(1f, new Vector4(1f, 1f, 1f, 0f)));
    }

    public ColorGradient(params GradientStop[] stops)
    {
        _stops.AddRange(stops);
        _stops.Sort((a, b) => a.Time.CompareTo(b.Time));
    }

    /// <summary>
    /// Add a color stop to the gradient
    /// </summary>
    public ColorGradient AddStop(float time, Vector4 color)
    {
        _stops.Add(new GradientStop(time, color));
        _stops.Sort((a, b) => a.Time.CompareTo(b.Time));
        return this;
    }

    /// <summary>
    /// Evaluate the gradient at a given time (0-1)
    /// </summary>
    public Vector4 Evaluate(float t)
    {
        t = Math.Clamp(t, 0f, 1f);

        if (_stops.Count == 0)
            return Vector4.One;

        if (_stops.Count == 1)
            return _stops[0].Color;

        // Find surrounding stops
        for (int i = 0; i < _stops.Count - 1; i++)
        {
            if (t >= _stops[i].Time && t <= _stops[i + 1].Time)
            {
                float localT = (t - _stops[i].Time) / (_stops[i + 1].Time - _stops[i].Time);
                return Vector4.Lerp(_stops[i].Color, _stops[i + 1].Color, localT);
            }
        }

        // Beyond range
        return t < _stops[0].Time ? _stops[0].Color : _stops[^1].Color;
    }

    // Presets
    public static ColorGradient White => new(
        new GradientStop(0f, Vector4.One),
        new GradientStop(1f, Vector4.One)
    );

    public static ColorGradient FadeOut => new(
        new GradientStop(0f, Vector4.One),
        new GradientStop(1f, new Vector4(1f, 1f, 1f, 0f))
    );

    public static ColorGradient Fire => new(
        new GradientStop(0f, new Vector4(1f, 1f, 0.3f, 1f)),   // Yellow
        new GradientStop(0.3f, new Vector4(1f, 0.5f, 0f, 1f)), // Orange
        new GradientStop(0.7f, new Vector4(1f, 0.1f, 0f, 0.8f)), // Red
        new GradientStop(1f, new Vector4(0.3f, 0.1f, 0.1f, 0f))  // Dark smoke
    );

    public static ColorGradient Smoke => new(
        new GradientStop(0f, new Vector4(0.5f, 0.5f, 0.5f, 0.8f)),
        new GradientStop(0.5f, new Vector4(0.3f, 0.3f, 0.3f, 0.5f)),
        new GradientStop(1f, new Vector4(0.2f, 0.2f, 0.2f, 0f))
    );

    public static ColorGradient Plasma => new(
        new GradientStop(0f, new Vector4(1f, 0f, 1f, 1f)),     // Magenta
        new GradientStop(0.5f, new Vector4(0f, 1f, 1f, 0.8f)), // Cyan
        new GradientStop(1f, new Vector4(0f, 0f, 1f, 0f))      // Blue fade
    );
}

/// <summary>
/// Animation curve for float values
/// </summary>
public class AnimationCurve
{
    private readonly List<(float time, float value)> _keys = new();

    public AnimationCurve()
    {
        _keys.Add((0f, 1f));
        _keys.Add((1f, 1f));
    }

    public AnimationCurve(params (float time, float value)[] keys)
    {
        _keys.AddRange(keys);
        _keys.Sort((a, b) => a.time.CompareTo(b.time));
    }

    public AnimationCurve AddKey(float time, float value)
    {
        _keys.Add((time, value));
        _keys.Sort((a, b) => a.time.CompareTo(b.time));
        return this;
    }

    public float Evaluate(float t)
    {
        t = Math.Clamp(t, 0f, 1f);

        if (_keys.Count == 0)
            return 1f;

        if (_keys.Count == 1)
            return _keys[0].value;

        // Linear interpolation between keys
        for (int i = 0; i < _keys.Count - 1; i++)
        {
            if (t >= _keys[i].time && t <= _keys[i + 1].time)
            {
                float localT = (_keys[i + 1].time - _keys[i].time) > 0
                    ? (t - _keys[i].time) / (_keys[i + 1].time - _keys[i].time)
                    : 0f;
                return _keys[i].value + localT * (_keys[i + 1].value - _keys[i].value);
            }
        }

        return t < _keys[0].time ? _keys[0].value : _keys[^1].value;
    }

    // Presets
    public static AnimationCurve Constant(float value) => new((0f, value), (1f, value));
    public static AnimationCurve Linear => new((0f, 0f), (1f, 1f));
    public static AnimationCurve EaseIn => new((0f, 0f), (0.5f, 0.25f), (1f, 1f));
    public static AnimationCurve EaseOut => new((0f, 0f), (0.5f, 0.75f), (1f, 1f));
    public static AnimationCurve EaseInOut => new((0f, 0f), (0.25f, 0.1f), (0.75f, 0.9f), (1f, 1f));
    public static AnimationCurve FadeOut => new((0f, 1f), (1f, 0f));
    public static AnimationCurve FadeIn => new((0f, 0f), (1f, 1f));
    public static AnimationCurve Pulse => new((0f, 0f), (0.5f, 1f), (1f, 0f));
}

/// <summary>
/// Module that modifies particle color over its lifetime
/// </summary>
public class ColorOverLifetimeModule
{
    /// <summary>
    /// Color gradient over lifetime (0 = spawn, 1 = death)
    /// </summary>
    public ColorGradient Gradient { get; set; } = ColorGradient.FadeOut;

    /// <summary>
    /// Apply this module to a particle
    /// </summary>
    public void Apply(ref Particle particle)
    {
        particle.Color = Gradient.Evaluate(particle.NormalizedAge);
    }
}

/// <summary>
/// Module that modifies particle size over its lifetime
/// </summary>
public class SizeOverLifetimeModule
{
    /// <summary>
    /// Size multiplier curve over lifetime
    /// </summary>
    public AnimationCurve SizeMultiplier { get; set; } = AnimationCurve.Constant(1f);

    /// <summary>
    /// Base size (set at spawn, multiplied by curve)
    /// </summary>
    private float _baseSize;

    /// <summary>
    /// Store the initial size when particle spawns
    /// </summary>
    public void OnSpawn(ref Particle particle)
    {
        _baseSize = particle.Size;
    }

    /// <summary>
    /// Apply this module to a particle
    /// </summary>
    public void Apply(ref Particle particle, float baseSize)
    {
        particle.Size = baseSize * SizeMultiplier.Evaluate(particle.NormalizedAge);
    }

    // Presets
    public static SizeOverLifetimeModule GrowAndFade => new()
    {
        SizeMultiplier = new AnimationCurve((0f, 0.5f), (0.3f, 1f), (1f, 0.2f))
    };

    public static SizeOverLifetimeModule Shrink => new()
    {
        SizeMultiplier = AnimationCurve.FadeOut
    };

    public static SizeOverLifetimeModule Grow => new()
    {
        SizeMultiplier = AnimationCurve.Linear
    };
}

/// <summary>
/// Module that modifies particle velocity over its lifetime
/// </summary>
public class VelocityOverLifetimeModule
{
    /// <summary>
    /// Speed multiplier curve over lifetime
    /// </summary>
    public AnimationCurve SpeedMultiplier { get; set; } = AnimationCurve.Constant(1f);

    /// <summary>
    /// Additional linear velocity added over time
    /// </summary>
    public Vector3 LinearVelocity { get; set; } = Vector3.Zero;

    /// <summary>
    /// Orbital velocity around Y axis (degrees/second)
    /// </summary>
    public float OrbitalVelocity { get; set; } = 0f;

    /// <summary>
    /// Radial velocity (towards/away from emitter center)
    /// </summary>
    public AnimationCurve RadialVelocity { get; set; } = AnimationCurve.Constant(0f);

    /// <summary>
    /// Apply this module to a particle
    /// </summary>
    public void Apply(ref Particle particle, float deltaTime, Vector3 emitterPosition)
    {
        // Speed multiplier
        float speedMult = SpeedMultiplier.Evaluate(particle.NormalizedAge);
        particle.Velocity *= speedMult;

        // Linear velocity
        particle.Velocity += LinearVelocity * deltaTime;

        // Orbital velocity
        if (MathF.Abs(OrbitalVelocity) > 0.001f)
        {
            Vector3 offset = particle.Position - emitterPosition;
            float angle = OrbitalVelocity * MathF.PI / 180f * deltaTime;
            float cos = MathF.Cos(angle);
            float sin = MathF.Sin(angle);

            particle.Position = emitterPosition + new Vector3(
                offset.X * cos - offset.Z * sin,
                offset.Y,
                offset.X * sin + offset.Z * cos
            );
        }

        // Radial velocity
        float radial = RadialVelocity.Evaluate(particle.NormalizedAge);
        if (MathF.Abs(radial) > 0.001f)
        {
            Vector3 dir = particle.Position - emitterPosition;
            if (dir.LengthSquared() > 0.0001f)
            {
                dir = Vector3.Normalize(dir);
                particle.Velocity += dir * radial * deltaTime;
            }
        }
    }
}

/// <summary>
/// Module that adds noise/turbulence to particle movement
/// </summary>
public class NoiseModule
{
    /// <summary>
    /// Strength of the noise effect
    /// </summary>
    public float Strength { get; set; } = 1f;

    /// <summary>
    /// Frequency of the noise
    /// </summary>
    public float Frequency { get; set; } = 1f;

    /// <summary>
    /// Number of noise octaves (higher = more detail, more expensive)
    /// </summary>
    public int Octaves { get; set; } = 1;

    /// <summary>
    /// Scroll speed of noise field
    /// </summary>
    public Vector3 ScrollSpeed { get; set; } = Vector3.Zero;

    /// <summary>
    /// Current time offset for noise scrolling
    /// </summary>
    private float _timeOffset;

    /// <summary>
    /// Apply noise to particle velocity
    /// </summary>
    public void Apply(ref Particle particle, float deltaTime, Random random)
    {
        _timeOffset += deltaTime;

        // Simple pseudo-random noise based on position
        // For a real implementation, use Perlin/Simplex noise
        Vector3 samplePos = particle.Position * Frequency + ScrollSpeed * _timeOffset;

        float noiseX = SimplexNoise(samplePos.X, samplePos.Y, samplePos.Z);
        float noiseY = SimplexNoise(samplePos.Y, samplePos.Z, samplePos.X);
        float noiseZ = SimplexNoise(samplePos.Z, samplePos.X, samplePos.Y);

        Vector3 noiseVelocity = new Vector3(noiseX, noiseY, noiseZ) * Strength;
        particle.Velocity += noiseVelocity * deltaTime;
    }

    /// <summary>
    /// Simple value noise (placeholder for proper simplex noise)
    /// </summary>
    private static float SimplexNoise(float x, float y, float z)
    {
        // Simple hash-based noise
        int xi = (int)MathF.Floor(x);
        int yi = (int)MathF.Floor(y);
        int zi = (int)MathF.Floor(z);

        float xf = x - xi;
        float yf = y - yi;
        float zf = z - zi;

        // Smoothstep
        float u = xf * xf * (3f - 2f * xf);
        float v = yf * yf * (3f - 2f * yf);
        float w = zf * zf * (3f - 2f * zf);

        // Hash corners
        float n000 = Hash(xi, yi, zi);
        float n001 = Hash(xi, yi, zi + 1);
        float n010 = Hash(xi, yi + 1, zi);
        float n011 = Hash(xi, yi + 1, zi + 1);
        float n100 = Hash(xi + 1, yi, zi);
        float n101 = Hash(xi + 1, yi, zi + 1);
        float n110 = Hash(xi + 1, yi + 1, zi);
        float n111 = Hash(xi + 1, yi + 1, zi + 1);

        // Trilinear interpolation
        float nx00 = Lerp(n000, n100, u);
        float nx01 = Lerp(n001, n101, u);
        float nx10 = Lerp(n010, n110, u);
        float nx11 = Lerp(n011, n111, u);

        float nxy0 = Lerp(nx00, nx10, v);
        float nxy1 = Lerp(nx01, nx11, v);

        return Lerp(nxy0, nxy1, w) * 2f - 1f; // Map to -1..1
    }

    private static float Hash(int x, int y, int z)
    {
        int n = x + y * 57 + z * 113;
        n = (n << 13) ^ n;
        return ((n * (n * n * 15731 + 789221) + 1376312589) & 0x7fffffff) / (float)0x7fffffff;
    }

    private static float Lerp(float a, float b, float t) => a + t * (b - a);
}

/// <summary>
/// Module for limiting particle velocity
/// </summary>
public class LimitVelocityModule
{
    /// <summary>
    /// Maximum speed
    /// </summary>
    public float MaxSpeed { get; set; } = 10f;

    /// <summary>
    /// Damping applied when speed exceeds limit (0-1, 1 = immediate)
    /// </summary>
    public float Damping { get; set; } = 0.1f;

    public void Apply(ref Particle particle, float deltaTime)
    {
        float speed = particle.Velocity.Length();
        if (speed > MaxSpeed)
        {
            float dampFactor = 1f - Damping * deltaTime;
            particle.Velocity *= dampFactor;
        }
    }
}

/// <summary>
/// Module for particle rotation over lifetime
/// </summary>
public class RotationOverLifetimeModule
{
    /// <summary>
    /// Angular velocity curve over lifetime (degrees/second)
    /// </summary>
    public AnimationCurve AngularVelocity { get; set; } = AnimationCurve.Constant(0f);

    public void Apply(ref Particle particle, float deltaTime)
    {
        float angularVel = AngularVelocity.Evaluate(particle.NormalizedAge);
        particle.Rotation += angularVel * MathF.PI / 180f * deltaTime;
    }
}

/// <summary>
/// Module for texture sheet animation
/// </summary>
public class TextureSheetAnimationModule
{
    /// <summary>
    /// Number of tiles in X direction
    /// </summary>
    public int TilesX { get; set; } = 1;

    /// <summary>
    /// Number of tiles in Y direction
    /// </summary>
    public int TilesY { get; set; } = 1;

    /// <summary>
    /// Animation speed (cycles per lifetime)
    /// </summary>
    public float CyclesPerLifetime { get; set; } = 1f;

    /// <summary>
    /// Get the current frame index for a particle
    /// </summary>
    public int GetFrameIndex(in Particle particle)
    {
        int totalFrames = TilesX * TilesY;
        float progress = (particle.NormalizedAge * CyclesPerLifetime) % 1f;
        return (int)(progress * totalFrames) % totalFrames;
    }

    /// <summary>
    /// Get UV coordinates for the current frame
    /// </summary>
    public (Vector2 min, Vector2 max) GetUVs(in Particle particle)
    {
        int frame = GetFrameIndex(particle);
        int x = frame % TilesX;
        int y = frame / TilesX;

        float tileWidth = 1f / TilesX;
        float tileHeight = 1f / TilesY;

        return (
            new Vector2(x * tileWidth, y * tileHeight),
            new Vector2((x + 1) * tileWidth, (y + 1) * tileHeight)
        );
    }
}
