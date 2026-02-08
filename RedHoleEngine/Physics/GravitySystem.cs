using System.Numerics;
using RedHoleEngine.Components;
using RedHoleEngine.Core.ECS;

namespace RedHoleEngine.Physics;

/// <summary>
/// ECS system that applies gravitational forces from GravitySourceComponents
/// to RigidBodyComponents. Works with PhysicsSystem to create realistic
/// orbital mechanics and black hole interactions.
/// </summary>
public class GravitySystem : GameSystem
{
    /// <summary>
    /// Gravitational constant (adjust for your scale)
    /// In natural units for black holes, this should be 1.0
    /// For more realistic Newtonian physics, you may want to scale this
    /// </summary>
    public float GravitationalConstant { get; set; } = 1.0f;

    /// <summary>
    /// Whether to use the physics world's default gravity in addition to sources
    /// </summary>
    public bool UseDefaultGravity { get; set; } = false;

    /// <summary>
    /// Minimum distance for gravitational calculations (prevents singularities)
    /// </summary>
    public float MinDistance { get; set; } = 0.1f;

    /// <summary>
    /// Maximum force to apply (prevents extreme accelerations near black holes)
    /// </summary>
    public float MaxForce { get; set; } = 10000f;

    /// <summary>
    /// Priority for execution order (should run before PhysicsSystem)
    /// </summary>
    public override int Priority => -150; // Run before PhysicsSystem (-100)

    public override void Update(float deltaTime)
    {
        if (World == null) return;

        // Get all gravity sources
        var gravitySources = new List<(Vector3 Position, GravitySourceComponent Source)>();
        
        foreach (var entity in World.Query<GravitySourceComponent, TransformComponent>())
        {
            ref var source = ref World.GetComponent<GravitySourceComponent>(entity);
            ref var transform = ref World.GetComponent<TransformComponent>(entity);
            gravitySources.Add((transform.Position, source));
        }

        if (gravitySources.Count == 0) return;

        // Apply gravity to all rigid bodies
        foreach (var entity in World.Query<RigidBodyComponent, TransformComponent>())
        {
            ref var rb = ref World.GetComponent<RigidBodyComponent>(entity);
            ref var transform = ref World.GetComponent<TransformComponent>(entity);

            // Skip if body doesn't use gravity
            if (!rb.UseGravity || rb.Body == null || rb.Type != RigidBodyType.Dynamic)
                continue;

            // Calculate total gravitational acceleration from all sources
            var totalAcceleration = Vector3.Zero;

            foreach (var (sourcePos, source) in gravitySources)
            {
                var acceleration = CalculateGravitationalAcceleration(
                    sourcePos, source, transform.Position);
                totalAcceleration += acceleration;
            }

            // Convert acceleration to force (F = ma)
            var force = totalAcceleration * rb.Mass;

            // Clamp force magnitude
            var forceMag = force.Length();
            if (forceMag > MaxForce)
            {
                force = force / forceMag * MaxForce;
            }

            // Apply the gravitational force
            rb.ApplyForce(force);
        }
    }

    private Vector3 CalculateGravitationalAcceleration(
        Vector3 sourcePosition, 
        GravitySourceComponent source,
        Vector3 targetPosition)
    {
        if (source.GravityType == GravityType.Uniform)
        {
            return source.UniformDirection * source.UniformStrength;
        }

        var toSource = sourcePosition - targetPosition;
        float distSq = toSource.LengthSquared();
        
        // Enforce minimum distance to prevent singularities
        float minDistSq = MinDistance * MinDistance;
        if (distSq < minDistSq)
        {
            distSq = minDistSq;
            // Special handling when inside event horizon
            if (source.GravityType == GravityType.Schwarzschild || 
                source.GravityType == GravityType.Kerr)
            {
                float horizonCheckDist = MathF.Sqrt(distSq);
                if (horizonCheckDist < source.EventHorizonRadius)
                {
                    // Inside event horizon - apply very strong inward force
                    // (In reality nothing escapes, but for gameplay we limit it)
                    var dir = Vector3.Normalize(toSource);
                    return dir * MaxForce / source.Mass;
                }
            }
        }

        float dist = MathF.Sqrt(distSq);
        
        // Check max range
        if (source.MaxRange > 0 && dist > source.MaxRange)
            return Vector3.Zero;

        float accelMag;
        
        switch (source.GravityType)
        {
            case GravityType.Newtonian:
                // a = GM/r^2
                accelMag = GravitationalConstant * source.Mass / distSq;
                break;

            case GravityType.Schwarzschild:
                // Enhanced near the event horizon (pseudo-relativistic)
                // Includes an approximation of the Schwarzschild correction
                float rs = source.SchwarzschildRadius;
                float r = dist;
                
                // Newtonian base
                accelMag = GravitationalConstant * source.Mass / distSq;
                
                // Relativistic enhancement factor (increases as r approaches rs)
                // This is an approximation - real GR would require solving geodesics
                if (r > rs * 1.5f)
                {
                    float factor = 1f / (1f - rs / r);
                    accelMag *= MathF.Sqrt(factor); // Mild enhancement
                }
                else if (r > rs)
                {
                    // Very close to horizon - strong enhancement
                    float factor = 1f / (1f - rs / r);
                    accelMag *= factor;
                }
                break;

            case GravityType.Kerr:
            {
                // Rotating black hole - add frame dragging effect
                float rsKerr = source.SchwarzschildRadius;
                float a = source.SpinParameter * source.Mass; // Kerr parameter
                
                // Base Newtonian
                accelMag = GravitationalConstant * source.Mass / distSq;
                
                // Frame dragging adds a tangential component
                var radial = Vector3.Normalize(toSource);
                var spinAxis = source.SpinAxis;
                var tangential = Vector3.Cross(spinAxis, radial);
                
                if (tangential.LengthSquared() > 0.0001f)
                {
                    tangential = Vector3.Normalize(tangential);
                    
                    // Frame dragging magnitude (decreases with distance)
                    float frameDrag = 2f * a * source.Mass / (dist * dist * dist);
                    
                    // Add tangential velocity tendency (not acceleration, but we approximate)
                    return toSource / dist * accelMag + tangential * frameDrag;
                }
                break;
            }

            default:
                return Vector3.Zero;
        }

        return toSource / dist * accelMag;
    }

    /// <summary>
    /// Calculate the orbital velocity needed for a circular orbit around a gravity source
    /// </summary>
    public static Vector3 CalculateOrbitalVelocity(
        Vector3 sourcePosition,
        GravitySourceComponent source,
        Vector3 orbitPosition,
        Vector3 orbitNormal,
        float gravitationalConstant = 1f)
    {
        var toSource = sourcePosition - orbitPosition;
        var dist = toSource.Length();
        
        if (dist < 0.001f) return Vector3.Zero;

        // v = sqrt(GM/r)
        var orbitalSpeed = MathF.Sqrt(gravitationalConstant * source.Mass / dist);
        
        // Direction is perpendicular to both the radial direction and orbit normal
        var radial = Vector3.Normalize(toSource);
        var tangent = Vector3.Cross(orbitNormal, radial);
        
        if (tangent.LengthSquared() < 0.0001f)
        {
            // Radial and normal are parallel, pick arbitrary perpendicular
            tangent = Vector3.Cross(radial, Vector3.UnitX);
            if (tangent.LengthSquared() < 0.0001f)
                tangent = Vector3.Cross(radial, Vector3.UnitY);
        }
        
        return Vector3.Normalize(tangent) * orbitalSpeed;
    }

    /// <summary>
    /// Calculate escape velocity from a gravity source at a given position
    /// </summary>
    public static float CalculateEscapeVelocity(
        Vector3 sourcePosition,
        GravitySourceComponent source,
        Vector3 position,
        float gravitationalConstant = 1f)
    {
        var dist = Vector3.Distance(sourcePosition, position);
        if (dist < 0.001f) return float.MaxValue;
        
        // v_escape = sqrt(2GM/r)
        return MathF.Sqrt(2f * gravitationalConstant * source.Mass / dist);
    }
}
