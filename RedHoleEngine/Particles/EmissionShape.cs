using System.Numerics;

namespace RedHoleEngine.Particles;

/// <summary>
/// Type of emission shape
/// </summary>
public enum EmissionShapeType
{
    Point,
    Sphere,
    Hemisphere,
    Cone,
    Box,
    Circle,
    Edge,
    Mesh // Future: emit from mesh surface
}

/// <summary>
/// Defines the shape from which particles are emitted
/// </summary>
public struct EmissionShape
{
    /// <summary>
    /// Type of emission shape
    /// </summary>
    public EmissionShapeType Type;
    
    /// <summary>
    /// Radius for sphere, hemisphere, cone, circle shapes
    /// </summary>
    public float Radius;
    
    /// <summary>
    /// Angle for cone shape (in degrees, half-angle from center)
    /// </summary>
    public float Angle;
    
    /// <summary>
    /// Half-extents for box shape
    /// </summary>
    public Vector3 BoxExtents;
    
    /// <summary>
    /// Length for edge shape
    /// </summary>
    public float EdgeLength;
    
    /// <summary>
    /// Whether to emit from the surface only (vs volume)
    /// </summary>
    public bool EmitFromSurface;
    
    /// <summary>
    /// Whether to randomize the initial direction
    /// </summary>
    public bool RandomizeDirection;

    // Presets
    public static EmissionShape Point => new() { Type = EmissionShapeType.Point };
    
    public static EmissionShape Sphere(float radius, bool surface = false) => new()
    {
        Type = EmissionShapeType.Sphere,
        Radius = radius,
        EmitFromSurface = surface
    };
    
    public static EmissionShape Hemisphere(float radius, bool surface = false) => new()
    {
        Type = EmissionShapeType.Hemisphere,
        Radius = radius,
        EmitFromSurface = surface
    };
    
    public static EmissionShape Cone(float angle, float radius = 1f) => new()
    {
        Type = EmissionShapeType.Cone,
        Angle = angle,
        Radius = radius
    };
    
    public static EmissionShape Box(Vector3 extents) => new()
    {
        Type = EmissionShapeType.Box,
        BoxExtents = extents
    };
    
    public static EmissionShape Circle(float radius) => new()
    {
        Type = EmissionShapeType.Circle,
        Radius = radius
    };
    
    public static EmissionShape Edge(float length) => new()
    {
        Type = EmissionShapeType.Edge,
        EdgeLength = length
    };

    /// <summary>
    /// Get a random position and direction from this shape
    /// </summary>
    public readonly void Sample(Random random, out Vector3 position, out Vector3 direction)
    {
        switch (Type)
        {
            case EmissionShapeType.Point:
                position = Vector3.Zero;
                direction = RandomDirection(random);
                break;

            case EmissionShapeType.Sphere:
                SampleSphere(random, out position, out direction);
                break;

            case EmissionShapeType.Hemisphere:
                SampleHemisphere(random, out position, out direction);
                break;

            case EmissionShapeType.Cone:
                SampleCone(random, out position, out direction);
                break;

            case EmissionShapeType.Box:
                SampleBox(random, out position, out direction);
                break;

            case EmissionShapeType.Circle:
                SampleCircle(random, out position, out direction);
                break;

            case EmissionShapeType.Edge:
                SampleEdge(random, out position, out direction);
                break;

            default:
                position = Vector3.Zero;
                direction = Vector3.UnitY;
                break;
        }
    }

    private readonly void SampleSphere(Random random, out Vector3 position, out Vector3 direction)
    {
        direction = RandomDirection(random);
        
        if (EmitFromSurface)
        {
            position = direction * Radius;
        }
        else
        {
            float r = Radius * MathF.Cbrt((float)random.NextDouble());
            position = direction * r;
        }

        if (RandomizeDirection)
            direction = RandomDirection(random);
    }

    private readonly void SampleHemisphere(Random random, out Vector3 position, out Vector3 direction)
    {
        // Sample sphere and flip if below horizon
        direction = RandomDirection(random);
        if (direction.Y < 0)
            direction.Y = -direction.Y;

        if (EmitFromSurface)
        {
            position = direction * Radius;
        }
        else
        {
            float r = Radius * MathF.Cbrt((float)random.NextDouble());
            position = direction * r;
        }

        if (RandomizeDirection)
            direction = RandomDirection(random);
    }

    private readonly void SampleCone(Random random, out Vector3 position, out Vector3 direction)
    {
        // Random angle within cone
        float angleRad = Angle * MathF.PI / 180f;
        float cosAngle = MathF.Cos(angleRad);
        
        // Random direction within cone (cosine-weighted)
        float z = cosAngle + (float)random.NextDouble() * (1f - cosAngle);
        float phi = (float)random.NextDouble() * MathF.PI * 2f;
        float sinTheta = MathF.Sqrt(1f - z * z);
        
        direction = new Vector3(
            sinTheta * MathF.Cos(phi),
            z, // Y is up
            sinTheta * MathF.Sin(phi)
        );

        // Position at base of cone
        float r = (float)random.NextDouble() * Radius;
        float posAngle = (float)random.NextDouble() * MathF.PI * 2f;
        position = new Vector3(
            r * MathF.Cos(posAngle),
            0,
            r * MathF.Sin(posAngle)
        );
    }

    private readonly void SampleBox(Random random, out Vector3 position, out Vector3 direction)
    {
        position = new Vector3(
            ((float)random.NextDouble() * 2f - 1f) * BoxExtents.X,
            ((float)random.NextDouble() * 2f - 1f) * BoxExtents.Y,
            ((float)random.NextDouble() * 2f - 1f) * BoxExtents.Z
        );
        
        direction = Vector3.UnitY;
        if (RandomizeDirection)
            direction = RandomDirection(random);
    }

    private readonly void SampleCircle(Random random, out Vector3 position, out Vector3 direction)
    {
        float angle = (float)random.NextDouble() * MathF.PI * 2f;
        float r = Radius * MathF.Sqrt((float)random.NextDouble());
        
        position = new Vector3(
            r * MathF.Cos(angle),
            0,
            r * MathF.Sin(angle)
        );
        
        direction = Vector3.UnitY;
        if (RandomizeDirection)
            direction = RandomDirection(random);
    }

    private readonly void SampleEdge(Random random, out Vector3 position, out Vector3 direction)
    {
        float t = (float)random.NextDouble() - 0.5f;
        position = new Vector3(t * EdgeLength, 0, 0);
        direction = Vector3.UnitY;
        
        if (RandomizeDirection)
            direction = RandomDirection(random);
    }

    private static Vector3 RandomDirection(Random random)
    {
        // Uniform random direction on unit sphere
        float z = (float)random.NextDouble() * 2f - 1f;
        float phi = (float)random.NextDouble() * MathF.PI * 2f;
        float r = MathF.Sqrt(1f - z * z);
        
        return new Vector3(
            r * MathF.Cos(phi),
            r * MathF.Sin(phi),
            z
        );
    }
}
