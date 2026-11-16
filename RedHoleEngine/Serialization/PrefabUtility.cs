using System.Numerics;
using RedHoleEngine.Components;
using RedHoleEngine.Core.ECS;
using RedHoleEngine.Core.Scene;
using RedHoleEngine.Resources;

namespace RedHoleEngine.Serialization;

/// <summary>
/// Creates prefabs from live entities and instantiates them into a world.
/// Component data goes through the <see cref="SceneSerializer"/> component registry,
/// so any component that can be saved in a scene can be stored in a prefab.
/// </summary>
public static class PrefabUtility
{
    /// <summary>
    /// Serializer used when none is passed explicitly (built-in component types only).
    /// Register additional component serializers here to make them available to prefabs.
    /// </summary>
    public static SceneSerializer DefaultSerializer { get; } = new();

    /// <summary>
    /// Capture an entity and all of its transform children as a prefab.
    /// The root keeps its local position/rotation/scale; node IDs are the source entity IDs.
    /// </summary>
    public static Prefab CreateFromEntity(World world, Entity root, string? name = null, SceneSerializer? serializer = null)
    {
        if (!world.IsAlive(root))
            throw new ArgumentException($"Entity {root} is not alive", nameof(root));

        serializer ??= DefaultSerializer;

        var prefab = Prefab.CreateEmpty(string.IsNullOrWhiteSpace(name) ? $"Entity {root.Id}" : name);

        // Children are found through the transform hierarchy, so map transforms back to entities
        var transformOwners = new Dictionary<Transform, Entity>(ReferenceEqualityComparer.Instance);
        foreach (var entity in world.Query<TransformComponent>())
        {
            transformOwners[world.GetComponent<TransformComponent>(entity).Transform] = entity;
        }

        // Breadth-first so parents always precede their children
        var pending = new Queue<(Entity entity, int parentId)>();
        var visited = new HashSet<int>();
        pending.Enqueue((root, 0));

        while (pending.Count > 0)
        {
            var (entity, parentId) = pending.Dequeue();
            if (!visited.Add(entity.Id))
                continue;

            var node = new PrefabNode { Id = entity.Id, Parent = parentId };
            foreach (var (typeId, data) in serializer.SerializeEntity(world, entity))
            {
                node.Components.Add(new PrefabComponentData
                {
                    TypeId = typeId,
                    Type = serializer.GetComponentTypeName(typeId),
                    Data = data
                });
            }
            prefab.Nodes.Add(node);

            if (world.TryGetComponent<TransformComponent>(entity, out var transformComponent))
            {
                foreach (var childTransform in transformComponent.Transform.Children)
                {
                    if (transformOwners.TryGetValue(childTransform, out var child))
                        pending.Enqueue((child, entity.Id));
                }
            }
        }

        return prefab;
    }

    /// <summary>
    /// Spawn a new, independent copy of a prefab and return its root entity.
    /// </summary>
    /// <param name="world">World to create the entities in</param>
    /// <param name="prefab">Prefab to instantiate</param>
    /// <param name="position">Overrides the root position when set</param>
    /// <param name="rotation">Overrides the root rotation when set</param>
    /// <param name="serializer">Component registry to use (defaults to <see cref="DefaultSerializer"/>)</param>
    public static Entity Instantiate(World world, Prefab prefab, Vector3? position = null, Quaternion? rotation = null,
        SceneSerializer? serializer = null)
    {
        return InstantiateHierarchy(world, prefab, position, rotation, serializer)[0];
    }

    /// <summary>
    /// Like <see cref="Instantiate"/>, but returns every created entity (root first, in prefab node order).
    /// Either the whole prefab is created or, if it is invalid, nothing is and
    /// <see cref="InvalidDataException"/> is thrown.
    /// </summary>
    public static IReadOnlyList<Entity> InstantiateHierarchy(World world, Prefab prefab, Vector3? position = null,
        Quaternion? rotation = null, SceneSerializer? serializer = null)
    {
        serializer ??= DefaultSerializer;
        prefab.Validate();

        var created = new List<Entity>(prefab.Nodes.Count);
        var entityByNodeId = new Dictionary<int, Entity>(prefab.Nodes.Count);

        try
        {
            // Pass 1: create entities and their components
            foreach (var node in prefab.Nodes)
            {
                var entity = world.CreateEntity();
                created.Add(entity);
                entityByNodeId[node.Id] = entity;

                foreach (var component in node.Components)
                {
                    // Unknown component types are skipped (forward compatibility)
                    serializer.TryDeserializeComponent(world, entity, component.TypeId, component.Data);
                }
            }

            // Pass 2: rebuild the transform hierarchy (stored transforms are local)
            for (int i = 1; i < prefab.Nodes.Count; i++)
            {
                var child = created[i];
                var parent = entityByNodeId[prefab.Nodes[i].Parent];

                if (world.HasComponent<TransformComponent>(child) && world.HasComponent<TransformComponent>(parent))
                {
                    world.GetComponent<TransformComponent>(child).Transform.SetParent(
                        world.GetComponent<TransformComponent>(parent).Transform, worldPositionStays: false);
                }
            }

            // Pass 3: point entity references at the new entities.
            // References to entities outside the prefab cannot be resolved and become Entity.Null.
            Entity Remap(Entity old) => entityByNodeId.GetValueOrDefault(old.Id, Entity.Null);
            foreach (var entity in created)
            {
                serializer.RemapEntityReferences(world, entity, Remap);
            }
        }
        catch (Exception ex) when (ex is IOException or FormatException or ArgumentException)
        {
            // Truncated or corrupt component payloads surface as reader exceptions; leave no partial instance behind
            foreach (var entity in created)
            {
                world.DestroyEntity(entity);
            }
            throw new InvalidDataException($"Prefab '{prefab.Name}' contains invalid component data: {ex.Message}", ex);
        }

        ApplyRootOverride(world, created[0], position, rotation);
        return created;
    }

    private static void ApplyRootOverride(World world, Entity root, Vector3? position, Quaternion? rotation)
    {
        if (position == null && rotation == null)
            return;

        if (!world.HasComponent<TransformComponent>(root))
            world.AddComponent(root, new TransformComponent());

        ref var transform = ref world.GetComponent<TransformComponent>(root);
        if (position.HasValue)
            transform.Position = position.Value;
        if (rotation.HasValue)
            transform.Rotation = rotation.Value;
    }
}
