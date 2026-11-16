using System.Numerics;
using RedHoleEngine.Components;
using RedHoleEngine.Core.ECS;
using RedHoleEngine.Physics.Collision;
using RedHoleEngine.Rendering;

namespace RedHoleEngine.Editor.Viewport;

/// <summary>
/// A bounding sphere in world space
/// </summary>
public readonly record struct BoundingSphere(Vector3 Center, float Radius);

/// <summary>
/// Position and orientation for the editor's yaw/pitch camera
/// </summary>
public readonly record struct CameraPose(Vector3 Position, float Yaw, float Pitch);

/// <summary>
/// Pure math for framing entities in the editor viewport
/// </summary>
public static class CameraFraming
{
    /// <summary>Radius used when an entity has nothing that defines its size</summary>
    public const float DefaultRadius = 1f;

    /// <summary>Smallest radius used for framing so tiny objects are not zoomed into</summary>
    public const float MinRadius = 0.5f;

    /// <summary>Pitch limit of <see cref="Engine.Camera"/></summary>
    public const float MaxPitch = 89f;

    /// <summary>
    /// Compute the world-space bounds of an entity from its mesh, collider and black-hole components.
    /// Returns null if the entity has no transform.
    /// </summary>
    public static BoundingSphere? ComputeEntityBounds(World world, Entity entity)
    {
        if (!world.IsAlive(entity) || !world.TryGetComponent<TransformComponent>(entity, out var transform))
            return null;

        var position = transform.Position;
        var rotation = transform.Rotation;
        var scale = transform.LocalScale;
        var maxScale = MathF.Max(MathF.Abs(scale.X), MathF.Max(MathF.Abs(scale.Y), MathF.Abs(scale.Z)));

        BoundingSphere? bounds = null;

        // Mesh bounds (if the mesh resource is loaded)
        if (world.TryGetComponent<MeshComponent>(entity, out var meshComponent) &&
            meshComponent.MeshHandle.TryGet(out var mesh) && mesh != null)
        {
            bounds = Merge(bounds, MeshBounds(mesh.BoundsMin, mesh.BoundsMax, position, rotation, scale));
        }

        // Collider bounds
        if (world.TryGetComponent<ColliderComponent>(entity, out var collider))
        {
            var localRadius = ColliderRadius(collider);
            if (localRadius.HasValue)
            {
                var center = position + Vector3.Transform(collider.Offset * scale, rotation);
                bounds = Merge(bounds, new BoundingSphere(center, localRadius.Value * maxScale));
            }
        }

        // Black holes: frame the visually interesting region around the event horizon
        if (world.TryGetComponent<GravitySourceComponent>(entity, out var gravity))
        {
            float schwarzschildRadius = gravity.Mass * 0.5f;
            bounds = Merge(bounds, new BoundingSphere(position, schwarzschildRadius * 3f));
        }

        if (world.TryGetComponent<AccretionDiskComponent>(entity, out var disk))
        {
            bounds = Merge(bounds, new BoundingSphere(position, disk.OuterRadius));
        }

        var result = bounds ?? new BoundingSphere(position, DefaultRadius);
        return result with { Radius = MathF.Max(result.Radius, MinRadius) };
    }

    /// <summary>
    /// Local (unscaled) bounding radius of a collider, or null for unbounded shapes such as planes
    /// </summary>
    public static float? ColliderRadius(in ColliderComponent collider)
    {
        return collider.ShapeType switch
        {
            ColliderType.Sphere => collider.SphereRadius,
            ColliderType.Box => collider.BoxHalfExtents.Length(),
            ColliderType.Capsule => collider.CapsuleRadius + collider.CapsuleHeight * 0.5f,
            _ => null
        };
    }

    /// <summary>
    /// World-space bounding sphere of a local axis-aligned box after scale, rotation and translation
    /// </summary>
    public static BoundingSphere MeshBounds(Vector3 localMin, Vector3 localMax, Vector3 position, Quaternion rotation, Vector3 scale)
    {
        var localCenter = (localMin + localMax) * 0.5f;
        var halfExtents = (localMax - localMin) * 0.5f * Vector3.Abs(scale);
        var center = position + Vector3.Transform(localCenter * scale, rotation);
        return new BoundingSphere(center, halfExtents.Length());
    }

    /// <summary>
    /// Smallest sphere containing both spheres (null-tolerant)
    /// </summary>
    public static BoundingSphere Merge(BoundingSphere? a, BoundingSphere b)
    {
        if (a is not { } first)
            return b;

        var offset = b.Center - first.Center;
        var distance = offset.Length();

        if (distance + b.Radius <= first.Radius) return first;
        if (distance + first.Radius <= b.Radius) return b;

        var radius = (distance + first.Radius + b.Radius) * 0.5f;
        var center = first.Center + offset / distance * (radius - first.Radius);
        return new BoundingSphere(center, radius);
    }

    /// <summary>
    /// Distance from which a sphere of the given radius fits in a perspective view
    /// </summary>
    /// <param name="radius">Sphere radius</param>
    /// <param name="fovDegrees">Vertical field of view in degrees</param>
    /// <param name="margin">Multiplier for breathing room around the object</param>
    /// <param name="minDistance">Lower bound on the result</param>
    public static float FramingDistance(float radius, float fovDegrees, float margin = 1.5f, float minDistance = 2f)
    {
        var halfFov = Math.Clamp(fovDegrees, 1f, 179f) * MathF.PI / 360f;
        var distance = radius / MathF.Sin(halfFov);
        return MathF.Max(distance, minDistance) * margin;
    }

    /// <summary>
    /// Yaw and pitch (degrees, matching <see cref="Engine.Camera"/>) that look from one point toward another
    /// </summary>
    public static (float Yaw, float Pitch) LookAtAngles(Vector3 from, Vector3 to)
    {
        var direction = to - from;
        if (direction.LengthSquared() < 1e-12f)
            return (-90f, 0f);

        direction = Vector3.Normalize(direction);
        var yaw = MathF.Atan2(direction.Z, direction.X) * 180f / MathF.PI;
        var pitch = MathF.Asin(Math.Clamp(direction.Y, -1f, 1f)) * 180f / MathF.PI;
        return (yaw, Math.Clamp(pitch, -MaxPitch, MaxPitch));
    }

    /// <summary>
    /// Camera pose that frames the given bounds, keeping the current viewing direction where possible
    /// </summary>
    public static CameraPose Frame(BoundingSphere bounds, Vector3 cameraPosition, Vector3 cameraForward, float fovDegrees)
    {
        var distance = FramingDistance(bounds.Radius, fovDegrees);

        var direction = bounds.Center - cameraPosition;
        if (direction.LengthSquared() < 1e-4f)
            direction = cameraForward;
        if (direction.LengthSquared() < 1e-12f)
            direction = -Vector3.UnitZ;
        direction = Vector3.Normalize(direction);

        // Avoid looking straight up/down where the yaw/pitch camera is degenerate
        var maxY = MathF.Sin(MaxPitch * MathF.PI / 180f);
        if (MathF.Abs(direction.Y) > maxY)
        {
            var horizontal = new Vector3(direction.X, 0f, direction.Z);
            if (horizontal.LengthSquared() < 1e-8f)
                horizontal = -Vector3.UnitZ;
            horizontal = Vector3.Normalize(horizontal) * MathF.Sqrt(1f - maxY * maxY);
            direction = new Vector3(horizontal.X, MathF.CopySign(maxY, direction.Y), horizontal.Z);
        }

        var position = bounds.Center - direction * distance;
        var (yaw, pitch) = LookAtAngles(position, bounds.Center);
        return new CameraPose(position, yaw, pitch);
    }
}

/// <summary>
/// Smoothly interpolates the editor camera from one pose to another
/// </summary>
public sealed class CameraFocusAnimation
{
    private readonly CameraPose _from;
    private readonly CameraPose _to;
    private readonly float _duration;
    private float _elapsed;

    public CameraFocusAnimation(CameraPose from, CameraPose to, float duration = 0.35f)
    {
        _from = from;
        _to = to;
        _duration = MathF.Max(duration, 0f);
    }

    /// <summary>Target pose</summary>
    public CameraPose Target => _to;

    /// <summary>Whether the animation reached its target</summary>
    public bool IsComplete => _elapsed >= _duration;

    /// <summary>
    /// Advance the animation and return the current pose
    /// </summary>
    public CameraPose Step(float deltaTime)
    {
        _elapsed = MathF.Min(_elapsed + MathF.Max(deltaTime, 0f), _duration);
        if (IsComplete)
            return _to;

        var t = _elapsed / _duration;
        t = t * t * (3f - 2f * t); // smoothstep

        return new CameraPose(
            Vector3.Lerp(_from.Position, _to.Position, t),
            LerpAngle(_from.Yaw, _to.Yaw, t),
            _from.Pitch + (_to.Pitch - _from.Pitch) * t);
    }

    /// <summary>
    /// Interpolate between angles in degrees along the shortest arc
    /// </summary>
    public static float LerpAngle(float from, float to, float t)
    {
        var delta = ((to - from) % 360f + 540f) % 360f - 180f;
        return from + delta * t;
    }
}
