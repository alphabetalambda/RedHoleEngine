using System.Numerics;

namespace RedHoleEngine.AI.Perception;

/// <summary>
/// Pure sight and hearing tests. No physics queries, so they stay deterministic in headless tests.
/// Line of sight that needs the world is a separate <see cref="IVisionQuery"/>.
/// </summary>
public static class SensoryMath
{
    public static bool InViewCone(
        Vector3 origin,
        Vector3 forward,
        Vector3 target,
        float viewDistance,
        float viewAngleDegrees)
    {
        var to = target - origin;
        float distanceSquared = to.LengthSquared();
        if (distanceSquared <= 1e-8f)
            return true;

        float distance = MathF.Sqrt(distanceSquared);
        if (viewDistance <= 0f || distance > viewDistance)
            return false;

        var facing = forward.LengthSquared() <= 1e-8f ? -Vector3.UnitZ : Vector3.Normalize(forward);
        float angle = Math.Clamp(viewAngleDegrees, 0f, 360f);
        float minimumDot = MathF.Cos(angle * 0.5f * (MathF.PI / 180f));
        float dot = Vector3.Dot(facing, to / distance);
        return dot >= minimumDot - 1e-5f;
    }

    /// <summary>
    /// A noise is heard when the listener is inside a radius of <paramref name="intensity"/>
    /// times <paramref name="hearingSensitivity"/>. Intensity is a radius in world units at sensitivity 1.
    /// </summary>
    public static bool CanHear(Vector3 origin, Vector3 noisePosition, float intensity, float hearingSensitivity)
    {
        float radius = intensity * hearingSensitivity;
        if (radius <= 0f)
            return false;
        float reach = radius * radius;
        return Vector3.DistanceSquared(origin, noisePosition) <= reach;
    }
}
