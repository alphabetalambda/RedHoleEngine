using System.Numerics;
using RedHoleEngine.Components;
using RedHoleEngine.Core.ECS;
using RedHoleEngine.Core.Scene;
using RedHoleEngine.Rendering.PBR;

namespace RedHoleEngine.Resources.ModelImport;

/// <summary>
/// Options for placing a <see cref="Model"/> into a world.
/// </summary>
public class ModelInstantiateOptions
{
    public Vector3 Position { get; set; } = Vector3.Zero;

    public Quaternion Rotation { get; set; } = Quaternion.Identity;

    public Vector3 Scale { get; set; } = Vector3.One;

    /// <summary>
    /// Library to register the model's materials in. Entities then reference them by ID and keep
    /// their textures. When null, entities get inline material factors only (no textures).
    /// </summary>
    public MaterialLibrary? MaterialLibrary { get; set; }

    /// <summary>
    /// Add a <see cref="RaytracerMeshComponent"/> so the meshes are included in the raytraced scene
    /// </summary>
    public bool AddRaytracerComponent { get; set; } = true;

    /// <summary>
    /// When false (default), one entity is created per node and their transforms are parented to
    /// mirror the model hierarchy under a single root entity. When true, only mesh entities are
    /// created, each with its final world transform and no parent; use this when the entities must
    /// survive systems that do not preserve transform parenting (such as scene serialization).
    /// </summary>
    public bool FlattenHierarchy { get; set; }
}

/// <summary>
/// The entities created by <see cref="ModelInstantiator.Instantiate"/>.
/// </summary>
public class ModelInstance
{
    /// <summary>
    /// Entity carrying the root transform; <see cref="Entity.Null"/> when the hierarchy was flattened
    /// </summary>
    public Entity Root { get; init; }

    /// <summary>
    /// Every entity created for the model, including the root
    /// </summary>
    public List<Entity> Entities { get; } = new();

    /// <summary>
    /// The entities that carry a <see cref="MeshComponent"/> (one per primitive per node)
    /// </summary>
    public List<Entity> MeshEntities { get; } = new();

    /// <summary>
    /// Destroy every entity of this instance
    /// </summary>
    public void Destroy(World world)
    {
        foreach (var entity in Entities)
        {
            if (!world.IsAlive(entity))
                continue;

            // Detach so a recycled parent cannot keep stale children alive.
            if (world.HasComponent<TransformComponent>(entity))
                world.GetComponent<TransformComponent>(entity).Transform.SetParent(null, worldPositionStays: false);

            world.DestroyEntity(entity);
        }

        Entities.Clear();
        MeshEntities.Clear();
    }
}

/// <summary>
/// Creates ECS entities from imported models.
/// </summary>
public static class ModelInstantiator
{
    /// <summary>
    /// Create entities for every mesh primitive of the model.
    /// The model's meshes are registered in <paramref name="resources"/> under their
    /// <see cref="ModelPrimitive.ResourceId"/>, so instances of the same model share geometry.
    /// </summary>
    public static ModelInstance Instantiate(
        World world,
        ResourceManager resources,
        Model model,
        ModelInstantiateOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(resources);
        ArgumentNullException.ThrowIfNull(model);
        options ??= new ModelInstantiateOptions();

        var materialIds = RegisterMaterials(model, options.MaterialLibrary);

        return options.FlattenHierarchy
            ? InstantiateFlat(world, resources, model, options, materialIds)
            : InstantiateHierarchy(world, resources, model, options, materialIds);
    }

    /// <summary>
    /// Load a model by path through the resource manager and instantiate it.
    /// Returns null if the model cannot be loaded.
    /// </summary>
    public static ModelInstance? Instantiate(
        World world,
        ResourceManager resources,
        string modelPath,
        ModelInstantiateOptions? options = null)
    {
        var model = resources.LoadModel(modelPath);
        return model == null ? null : Instantiate(world, resources, model, options);
    }

    private static ModelInstance InstantiateHierarchy(
        World world,
        ResourceManager resources,
        Model model,
        ModelInstantiateOptions options,
        int[]? materialIds)
    {
        var rootTransform = new Transform(options.Position, options.Rotation, options.Scale);
        var root = world.CreateEntity();
        world.AddComponent(root, new TransformComponent(rootTransform));

        var instance = new ModelInstance { Root = root };
        instance.Entities.Add(root);

        // Parents precede children in Model.Nodes, so their transforms already exist.
        var nodeTransforms = new Transform[model.Nodes.Count];
        for (int i = 0; i < model.Nodes.Count; i++)
        {
            var node = model.Nodes[i];
            var transform = new Transform(node.LocalPosition, node.LocalRotation, node.LocalScale);
            transform.SetParent(node.ParentIndex >= 0 ? nodeTransforms[node.ParentIndex] : rootTransform, worldPositionStays: false);
            nodeTransforms[i] = transform;

            var entity = world.CreateEntity();
            world.AddComponent(entity, new TransformComponent(transform));
            instance.Entities.Add(entity);

            if (node.MeshIndex < 0)
                continue;

            var primitives = model.Meshes[node.MeshIndex].Primitives;
            for (int p = 0; p < primitives.Count; p++)
            {
                // The first primitive lives on the node entity; the rest get child entities.
                var target = entity;
                if (p > 0)
                {
                    var childTransform = new Transform();
                    childTransform.SetParent(transform, worldPositionStays: false);
                    target = world.CreateEntity();
                    world.AddComponent(target, new TransformComponent(childTransform));
                    instance.Entities.Add(target);
                }

                AddMeshComponents(world, resources, target, model, primitives[p], options, materialIds);
                instance.MeshEntities.Add(target);
            }
        }

        return instance;
    }

    private static ModelInstance InstantiateFlat(
        World world,
        ResourceManager resources,
        Model model,
        ModelInstantiateOptions options,
        int[]? materialIds)
    {
        var rootMatrix =
            Matrix4x4.CreateScale(options.Scale) *
            Matrix4x4.CreateFromQuaternion(options.Rotation) *
            Matrix4x4.CreateTranslation(options.Position);

        var instance = new ModelInstance { Root = Entity.Null };

        foreach (var node in model.Nodes)
        {
            if (node.MeshIndex < 0)
                continue;

            // Decompose is lossy for sheared results (non-uniform scale under a rotated parent).
            var world4x4 = node.WorldMatrix * rootMatrix;
            if (!Matrix4x4.Decompose(world4x4, out var scale, out var rotation, out var translation))
            {
                scale = Vector3.One;
                rotation = Quaternion.Identity;
                translation = world4x4.Translation;
            }

            foreach (var primitive in model.Meshes[node.MeshIndex].Primitives)
            {
                var entity = world.CreateEntity();
                world.AddComponent(entity, new TransformComponent(translation, rotation, scale));
                AddMeshComponents(world, resources, entity, model, primitive, options, materialIds);

                instance.Entities.Add(entity);
                instance.MeshEntities.Add(entity);
            }
        }

        return instance;
    }

    private static void AddMeshComponents(
        World world,
        ResourceManager resources,
        Entity entity,
        Model model,
        ModelPrimitive primitive,
        ModelInstantiateOptions options,
        int[]? materialIds)
    {
        // Re-register if the ID is free or held by a mesh from an earlier load of the same file.
        if (!resources.IsLoaded<Mesh>(primitive.ResourceId) ||
            !ReferenceEquals(resources.Get<Mesh>(primitive.ResourceId), primitive.Mesh))
        {
            resources.Add(primitive.ResourceId, primitive.Mesh);
        }

        world.AddComponent(entity, new MeshComponent(resources.GetHandle<Mesh>(primitive.ResourceId)));
        world.AddComponent(entity, CreateMaterialComponent(model, primitive.MaterialIndex, materialIds));

        if (options.AddRaytracerComponent)
            world.AddComponent(entity, new RaytracerMeshComponent(true));
    }

    /// <summary>
    /// Build the material component for a model material: a library reference when
    /// <paramref name="materialIds"/> is given, inline factors otherwise.
    /// </summary>
    private static MaterialComponent CreateMaterialComponent(Model model, int materialIndex, int[]? materialIds)
    {
        if (materialIndex < 0 || materialIndex >= model.Materials.Count)
            return MaterialComponent.Default;

        var material = model.Materials[materialIndex];
        var component = MaterialComponent.Default;
        component.BaseColor = material.BaseColorFactor;
        component.Metallic = material.MetallicFactor;
        component.Roughness = material.RoughnessFactor;
        component.EmissiveColor = material.EmissiveFactor * material.EmissiveIntensity;

        if (materialIds != null)
            component.PbrMaterialId = materialIds[materialIndex];

        return component;
    }

    /// <summary>
    /// Add the model's materials to the library, reusing entries from earlier instantiations.
    /// </summary>
    private static int[]? RegisterMaterials(Model model, MaterialLibrary? library)
    {
        if (library == null)
            return null;

        var ids = new int[model.Materials.Count];
        for (int i = 0; i < ids.Length; i++)
        {
            var material = model.Materials[i];
            ids[i] = ReferenceEquals(library.GetMaterial(material.Id), material)
                ? material.Id
                : library.AddMaterial(material);
        }
        return ids;
    }
}
