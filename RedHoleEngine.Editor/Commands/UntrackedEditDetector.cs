using RedHoleEngine.Audio;
using RedHoleEngine.Components;
using RedHoleEngine.Core.ECS;

namespace RedHoleEngine.Editor.Commands;

/// <summary>
/// Value snapshot of the components an editor panel can modify on one entity.
/// Two snapshots compare equal when the same components are present with equal field values.
/// </summary>
public readonly struct EntityStateSnapshot : IEquatable<EntityStateSnapshot>
{
    private static readonly Func<World, Entity, object?>[] Readers =
    {
        ReadTransform,
        Read<RigidBodyComponent>,
        Read<ColliderComponent>,
        Read<CameraComponent>,
        Read<GravitySourceComponent>,
        Read<AccretionDiskComponent>,
        Read<AudioSourceComponent>,
        Read<CollisionSoundComponent>,
        Read<RenderSettingsComponent>,
        Read<MeshComponent>,
        Read<MaterialComponent>,
    };

    private readonly object?[]? _components;
    private readonly bool _alive;

    private EntityStateSnapshot(bool alive, object?[]? components)
    {
        _alive = alive;
        _components = components;
    }

    /// <summary>
    /// Capture the editable components of an entity
    /// </summary>
    public static EntityStateSnapshot Capture(World world, Entity entity)
    {
        if (!world.IsAlive(entity))
            return new EntityStateSnapshot(false, null);

        var values = new object?[Readers.Length];
        for (int i = 0; i < Readers.Length; i++)
        {
            values[i] = Readers[i](world, entity);
        }
        return new EntityStateSnapshot(true, values);
    }

    private static object? Read<T>(World world, Entity entity) where T : IComponent
    {
        // Boxing copies the struct, so later edits to the component do not affect the snapshot
        return world.TryGetComponent<T>(entity, out var component) ? component : null;
    }

    private static object? ReadTransform(World world, Entity entity)
    {
        // TransformComponent wraps a reference-type Transform, so capture its values explicitly
        if (!world.TryGetComponent<TransformComponent>(entity, out var transform))
            return null;
        return (transform.LocalPosition, transform.LocalRotation, transform.LocalScale);
    }

    public bool Equals(EntityStateSnapshot other)
    {
        if (_alive != other._alive) return false;
        if (_components == null || other._components == null) return _components == other._components;

        for (int i = 0; i < _components.Length; i++)
        {
            if (!Equals(_components[i], other._components[i]))
                return false;
        }
        return true;
    }

    public override bool Equals(object? obj) => obj is EntityStateSnapshot other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(_alive, _components?.Length ?? 0);

    public static bool operator ==(EntityStateSnapshot left, EntityStateSnapshot right) => left.Equals(right);
    public static bool operator !=(EntityStateSnapshot left, EntityStateSnapshot right) => !left.Equals(right);
}

/// <summary>
/// Detects scene edits that did not go through the undo/redo system.
/// A panel calls <see cref="BeginFrame"/> before drawing its widgets and <see cref="EndFrame"/> after.
/// While a widget is held (e.g. a drag in progress) changes are accumulated; once interaction ends,
/// the change is reported as untracked only if no undo command was recorded since it started.
/// </summary>
public sealed class UntrackedEditDetector
{
    private EntityStateSnapshot _frameStart;
    private long _frameStartVersion;
    private bool _pending;
    private long _pendingVersion;

    /// <summary>
    /// Capture state before the panel draws its widgets
    /// </summary>
    public void BeginFrame(EntityStateSnapshot snapshot, long undoVersion)
    {
        _frameStart = snapshot;
        _frameStartVersion = undoVersion;
    }

    /// <summary>
    /// Compare with the state after drawing.
    /// </summary>
    /// <param name="snapshot">Snapshot after the widgets were drawn</param>
    /// <param name="undoVersion">Current undo state version</param>
    /// <param name="interactionActive">Whether a widget is still being interacted with (e.g. ImGui.IsAnyItemActive)</param>
    /// <returns>True when an edit finished that the undo system did not record</returns>
    public bool EndFrame(EntityStateSnapshot snapshot, long undoVersion, bool interactionActive)
    {
        if (snapshot != _frameStart && !_pending)
        {
            _pending = true;
            _pendingVersion = _frameStartVersion;
        }

        if (!_pending || interactionActive)
            return false;

        _pending = false;
        return undoVersion == _pendingVersion;
    }

    /// <summary>
    /// Forget any in-progress change (e.g. when the scene is replaced)
    /// </summary>
    public void Reset()
    {
        _pending = false;
    }
}
