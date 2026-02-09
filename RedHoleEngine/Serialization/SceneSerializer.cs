using System.Numerics;
using System.Reflection;
using RedHoleEngine.Components;
using RedHoleEngine.Core.ECS;

namespace RedHoleEngine.Serialization;

/// <summary>
/// Binary scene serialization format.
/// Saves and loads entire ECS worlds to/from binary files.
/// </summary>
public class SceneSerializer
{
    // Magic number and version for file format validation
    private const uint MagicNumber = 0x52484553; // "RHES" - RedHole Engine Scene
    private const uint FormatVersion = 1;

    // Component type registry for serialization
    private readonly Dictionary<Type, int> _typeToId = new();
    private readonly Dictionary<int, Type> _idToType = new();
    private readonly Dictionary<Type, IComponentSerializer> _serializers = new();

    public SceneSerializer()
    {
        RegisterBuiltInComponents();
    }

    private void RegisterBuiltInComponents()
    {
        // Register all built-in component types with serializers
        RegisterComponent<TransformComponent>(1, new TransformComponentSerializer());
        RegisterComponent<CameraComponent>(2, new CameraComponentSerializer());
        RegisterComponent<GravitySourceComponent>(3, new GravitySourceComponentSerializer());
        RegisterComponent<RigidBodyComponent>(4, new RigidBodyComponentSerializer());
        RegisterComponent<ColliderComponent>(5, new ColliderComponentSerializer());
        RegisterComponent<MeshComponent>(6, new MeshComponentSerializer());
        RegisterComponent<MaterialComponent>(7, new MaterialComponentSerializer());
        RegisterComponent<RenderSettingsComponent>(8, new RenderSettingsComponentSerializer());
        RegisterComponent<RaytracerMeshComponent>(9, new RaytracerMeshComponentSerializer());
    }

    /// <summary>
    /// Register a component type for serialization
    /// </summary>
    public void RegisterComponent<T>(int typeId, IComponentSerializer serializer) where T : IComponent
    {
        var type = typeof(T);
        _typeToId[type] = typeId;
        _idToType[typeId] = type;
        _serializers[type] = serializer;
    }

    /// <summary>
    /// Save a world to a binary file
    /// </summary>
    public void SaveToFile(World world, string filePath)
    {
        using var stream = File.Create(filePath);
        Save(world, stream);
    }

    /// <summary>
    /// Load a world from a binary file
    /// </summary>
    public void LoadFromFile(World world, string filePath)
    {
        using var stream = File.OpenRead(filePath);
        Load(world, stream);
    }

    /// <summary>
    /// Save a world to a stream
    /// </summary>
    public void Save(World world, Stream stream)
    {
        using var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true);

        // Write header
        writer.Write(MagicNumber);
        writer.Write(FormatVersion);

        // Collect all entities with their components
        var entityData = CollectEntityData(world);

        // Write entity count
        writer.Write(entityData.Count);

        // Write each entity
        foreach (var (entityId, components) in entityData)
        {
            writer.Write(entityId);
            writer.Write(components.Count);

            foreach (var (typeId, componentData) in components)
            {
                writer.Write(typeId);
                writer.Write(componentData.Length);
                writer.Write(componentData);
            }
        }
    }

    /// <summary>
    /// Load a world from a stream (clears existing world first)
    /// </summary>
    public void Load(World world, Stream stream)
    {
        using var reader = new BinaryReader(stream, System.Text.Encoding.UTF8, leaveOpen: true);

        // Read and validate header
        uint magic = reader.ReadUInt32();
        if (magic != MagicNumber)
            throw new InvalidDataException("Invalid scene file format");

        uint version = reader.ReadUInt32();
        if (version > FormatVersion)
            throw new InvalidDataException($"Scene file version {version} is newer than supported version {FormatVersion}");

        // Clear existing world
        ClearWorld(world);

        // Read entity count
        int entityCount = reader.ReadInt32();

        // Entity ID remapping (old ID -> new entity)
        var entityMap = new Dictionary<int, Entity>();

        // First pass: create all entities
        for (int i = 0; i < entityCount; i++)
        {
            int oldEntityId = reader.ReadInt32();
            int componentCount = reader.ReadInt32();

            var entity = world.CreateEntity();
            entityMap[oldEntityId] = entity;

            // Read components
            for (int j = 0; j < componentCount; j++)
            {
                int typeId = reader.ReadInt32();
                int dataLength = reader.ReadInt32();
                byte[] data = reader.ReadBytes(dataLength);

                if (_idToType.TryGetValue(typeId, out var componentType) &&
                    _serializers.TryGetValue(componentType, out var serializer))
                {
                    serializer.Deserialize(world, entity, data);
                }
                // Skip unknown component types (forward compatibility)
            }
        }
    }

    private List<(int entityId, List<(int typeId, byte[] data)> components)> CollectEntityData(World world)
    {
        var result = new List<(int, List<(int, byte[])>)>();
        var processedEntities = new HashSet<int>();

        // Iterate through all registered component types
        foreach (var (type, typeId) in _typeToId)
        {
            if (!_serializers.TryGetValue(type, out var serializer))
                continue;

            var entityIds = serializer.GetEntityIds(world);
            foreach (var entityId in entityIds)
            {
                if (processedEntities.Contains(entityId))
                    continue;

                // Found a new entity, collect all its components
                if (world.TryGetEntity(entityId, out var entity))
                {
                    var components = new List<(int, byte[])>();

                    foreach (var (compType, compTypeId) in _typeToId)
                    {
                        if (_serializers.TryGetValue(compType, out var compSerializer))
                        {
                            var data = compSerializer.TrySerialize(world, entity);
                            if (data != null)
                            {
                                components.Add((compTypeId, data));
                            }
                        }
                    }

                    if (components.Count > 0)
                    {
                        result.Add((entityId, components));
                        processedEntities.Add(entityId);
                    }
                }
            }
        }

        return result;
    }

    private void ClearWorld(World world)
    {
        // Collect all entities first to avoid modification during iteration
        var entities = new List<Entity>();
        foreach (var (type, _) in _typeToId)
        {
            if (_serializers.TryGetValue(type, out var serializer))
            {
                foreach (var entityId in serializer.GetEntityIds(world))
                {
                    if (world.TryGetEntity(entityId, out var entity))
                    {
                        entities.Add(entity);
                    }
                }
            }
        }

        // Destroy unique entities
        foreach (var entity in entities.Distinct())
        {
            world.DestroyEntity(entity);
        }
    }
}

/// <summary>
/// Interface for component-specific serialization
/// </summary>
public interface IComponentSerializer
{
    IEnumerable<int> GetEntityIds(World world);
    byte[]? TrySerialize(World world, Entity entity);
    void Deserialize(World world, Entity entity, byte[] data);
}

/// <summary>
/// Base class for component serializers with common binary read/write helpers
/// </summary>
public abstract class ComponentSerializer<T> : IComponentSerializer where T : IComponent
{
    public IEnumerable<int> GetEntityIds(World world)
    {
        return world.GetPool<T>().GetEntityIds();
    }

    public byte[]? TrySerialize(World world, Entity entity)
    {
        if (!world.HasComponent<T>(entity))
            return null;

        ref var component = ref world.GetComponent<T>(entity);
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        Serialize(ref component, writer);
        return stream.ToArray();
    }

    public void Deserialize(World world, Entity entity, byte[] data)
    {
        using var stream = new MemoryStream(data);
        using var reader = new BinaryReader(stream);
        var component = Deserialize(reader);
        world.AddComponent(entity, component);
    }

    protected abstract void Serialize(ref T component, BinaryWriter writer);
    protected abstract T Deserialize(BinaryReader reader);

    // Helper methods for common types
    protected static void WriteVector3(BinaryWriter w, Vector3 v)
    {
        w.Write(v.X);
        w.Write(v.Y);
        w.Write(v.Z);
    }

    protected static Vector3 ReadVector3(BinaryReader r)
    {
        return new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
    }

    protected static void WriteVector4(BinaryWriter w, Vector4 v)
    {
        w.Write(v.X);
        w.Write(v.Y);
        w.Write(v.Z);
        w.Write(v.W);
    }

    protected static Vector4 ReadVector4(BinaryReader r)
    {
        return new Vector4(r.ReadSingle(), r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
    }

    protected static void WriteQuaternion(BinaryWriter w, Quaternion q)
    {
        w.Write(q.X);
        w.Write(q.Y);
        w.Write(q.Z);
        w.Write(q.W);
    }

    protected static Quaternion ReadQuaternion(BinaryReader r)
    {
        return new Quaternion(r.ReadSingle(), r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
    }

    protected static void WriteString(BinaryWriter w, string? s)
    {
        if (s == null)
        {
            w.Write(-1);
        }
        else
        {
            w.Write(s.Length);
            w.Write(s.ToCharArray());
        }
    }

    protected static string? ReadString(BinaryReader r)
    {
        int length = r.ReadInt32();
        if (length < 0) return null;
        return new string(r.ReadChars(length));
    }
}
