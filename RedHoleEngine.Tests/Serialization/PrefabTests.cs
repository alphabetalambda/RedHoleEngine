using System.Numerics;
using RedHoleEngine.Components;
using RedHoleEngine.Core.ECS;
using RedHoleEngine.Resources;
using RedHoleEngine.Serialization;

namespace RedHoleEngine.Tests.Serialization;

public class PrefabTests : IDisposable
{
    private const float Epsilon = 1e-4f;

    private readonly string _tempDir;
    private readonly World _world;

    public PrefabTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"prefab_tests_{Guid.NewGuid()}");
        Directory.CreateDirectory(_tempDir);
        _world = new World();
    }

    public void Dispose()
    {
        _world.Dispose();
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    #region Helpers

    /// <summary>Test component holding entity references</summary>
    private struct LinkComponent : IComponent
    {
        public Entity Target;
        public Entity Other;
        public int Payload;
    }

    private class LinkComponentSerializer : ComponentSerializer<LinkComponent>
    {
        protected override void Serialize(ref LinkComponent component, BinaryWriter writer)
        {
            WriteEntity(writer, component.Target);
            WriteEntity(writer, component.Other);
            writer.Write(component.Payload);
        }

        protected override LinkComponent Deserialize(BinaryReader reader)
        {
            return new LinkComponent
            {
                Target = ReadEntity(reader),
                Other = ReadEntity(reader),
                Payload = reader.ReadInt32()
            };
        }

        protected override void RemapEntityReferences(ref LinkComponent component, Func<Entity, Entity> remap)
        {
            component.Target = remap(component.Target);
            component.Other = remap(component.Other);
        }
    }

    private static SceneSerializer CreateSerializerWithLinks()
    {
        var serializer = new SceneSerializer();
        serializer.RegisterComponent<LinkComponent>(1000, new LinkComponentSerializer());
        return serializer;
    }

    private Entity CreateEntity(Vector3 position, Entity? parent = null)
    {
        var entity = _world.CreateEntity();
        _world.AddComponent(entity, new TransformComponent(position, Quaternion.Identity, Vector3.One));
        if (parent.HasValue)
        {
            _world.GetComponent<TransformComponent>(entity).Transform.SetParent(
                _world.GetComponent<TransformComponent>(parent.Value).Transform, worldPositionStays: false);
        }
        return entity;
    }

    /// <summary>root(1,2,3) -> child(0,5,0) -> grandchild(0,0,7), plus a second child(-1,0,0)</summary>
    private (Entity root, Entity child, Entity grandchild, Entity child2) CreateHierarchy()
    {
        var root = CreateEntity(new Vector3(1, 2, 3));
        var child = CreateEntity(new Vector3(0, 5, 0), root);
        var grandchild = CreateEntity(new Vector3(0, 0, 7), child);
        var child2 = CreateEntity(new Vector3(-1, 0, 0), root);
        return (root, child, grandchild, child2);
    }

    private TransformComponent TransformOf(Entity entity) => _world.GetComponent<TransformComponent>(entity);

    private static void AssertVector(Vector3 expected, Vector3 actual)
    {
        Assert.True(Vector3.Distance(expected, actual) < Epsilon, $"Expected {expected}, got {actual}");
    }

    private string TempFile(string name = "test.prefab") => Path.Combine(_tempDir, name);

    #endregion

    #region Round-trip

    [Fact]
    public void RoundTrip_ThroughJson_PreservesComponents()
    {
        var rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, 0.5f);
        var source = _world.CreateEntity();
        _world.AddComponent(source, new TransformComponent(new Vector3(1, 2, 3), rotation, new Vector3(2, 3, 4)));
        _world.AddComponent(source, CameraComponent.CreatePerspective(75f, 2f));
        _world.AddComponent(source, RigidBodyComponent.CreateDynamic(12.5f));

        var prefab = PrefabUtility.CreateFromEntity(_world, source, "Thing");
        var restored = Prefab.FromJson(prefab.ToJson());

        using var target = new World();
        var instance = PrefabUtility.Instantiate(target, restored);

        Assert.Equal("Thing", restored.Name);
        Assert.Equal(1, target.EntityCount);

        var transform = target.GetComponent<TransformComponent>(instance);
        AssertVector(new Vector3(1, 2, 3), transform.LocalPosition);
        AssertVector(new Vector3(2, 3, 4), transform.LocalScale);
        Assert.True(MathF.Abs(Quaternion.Dot(rotation, transform.LocalRotation)) > 1f - Epsilon);

        Assert.Equal(75f, target.GetComponent<CameraComponent>(instance).FieldOfView);
        Assert.Equal(12.5f, target.GetComponent<RigidBodyComponent>(instance).Mass);
        Assert.False(target.HasComponent<ColliderComponent>(instance));
    }

    [Fact]
    public void RoundTrip_ThroughFile_PreservesHierarchy()
    {
        var (root, _, _, _) = CreateHierarchy();
        var path = TempFile();

        PrefabUtility.CreateFromEntity(_world, root, "Tree").SaveToFile(path);
        var loaded = Prefab.LoadFromFile(path);

        Assert.Equal("Tree", loaded.Name);
        Assert.Equal(Prefab.CurrentVersion, loaded.Version);
        Assert.Equal(4, loaded.Nodes.Count);

        using var target = new World();
        PrefabUtility.Instantiate(target, loaded);
        Assert.Equal(4, target.EntityCount);
    }

    [Fact]
    public void ToJson_ProducesReadableJsonWithTypeNames()
    {
        var source = CreateEntity(Vector3.Zero);

        var json = PrefabUtility.CreateFromEntity(_world, source).ToJson();

        Assert.Contains("\"format\": \"RedHolePrefab\"", json);
        Assert.Contains("\"type\": \"TransformComponent\"", json);
    }

    [Fact]
    public void SaveToFile_CreatesMissingDirectory()
    {
        var source = CreateEntity(Vector3.Zero);
        var path = Path.Combine(_tempDir, "nested", "dir", "a.prefab");

        PrefabUtility.CreateFromEntity(_world, source).SaveToFile(path);

        Assert.True(File.Exists(path));
    }

    [Fact]
    public void CreateFromEntity_DeadEntity_Throws()
    {
        var entity = _world.CreateEntity();
        _world.DestroyEntity(entity);

        Assert.Throws<ArgumentException>(() => PrefabUtility.CreateFromEntity(_world, entity));
    }

    #endregion

    #region Hierarchy

    [Fact]
    public void CreateFromEntity_CapturesOnlyTheSubtree()
    {
        var (_, child, grandchild, _) = CreateHierarchy();
        CreateEntity(new Vector3(9, 9, 9)); // unrelated

        var prefab = PrefabUtility.CreateFromEntity(_world, child);

        Assert.Equal(2, prefab.Nodes.Count);
        Assert.Equal(child.Id, prefab.Nodes[0].Id);
        Assert.Equal(0, prefab.Nodes[0].Parent);
        Assert.Equal(grandchild.Id, prefab.Nodes[1].Id);
        Assert.Equal(child.Id, prefab.Nodes[1].Parent);
    }

    [Fact]
    public void Instantiate_RebuildsParentChildLinksAndLocalTransforms()
    {
        var (root, _, _, _) = CreateHierarchy();
        var prefab = Prefab.FromJson(PrefabUtility.CreateFromEntity(_world, root).ToJson());

        using var target = new World();
        var entities = PrefabUtility.InstantiateHierarchy(target, prefab);

        Assert.Equal(4, entities.Count);
        // Breadth-first node order: root, child, child2, grandchild
        var newRoot = target.GetComponent<TransformComponent>(entities[0]).Transform;
        var newChild = target.GetComponent<TransformComponent>(entities[1]).Transform;
        var newChild2 = target.GetComponent<TransformComponent>(entities[2]).Transform;
        var newGrandchild = target.GetComponent<TransformComponent>(entities[3]).Transform;

        Assert.Null(newRoot.Parent);
        Assert.Same(newRoot, newChild.Parent);
        Assert.Same(newRoot, newChild2.Parent);
        Assert.Same(newChild, newGrandchild.Parent);
        Assert.Equal(2, newRoot.Children.Count);

        AssertVector(new Vector3(0, 5, 0), newChild.LocalPosition);
        AssertVector(new Vector3(0, 0, 7), newGrandchild.LocalPosition);
        AssertVector(new Vector3(1, 7, 10), newGrandchild.Position);
        AssertVector(new Vector3(0, 2, 3), newChild2.Position);
    }

    [Fact]
    public void Instantiate_DoesNotLinkToSourceHierarchy()
    {
        var (root, child, _, _) = CreateHierarchy();
        var prefab = PrefabUtility.CreateFromEntity(_world, root);

        var instance = PrefabUtility.Instantiate(_world, prefab);

        Assert.NotSame(TransformOf(root).Transform, TransformOf(instance).Transform);
        Assert.Equal(2, TransformOf(root).Transform.Children.Count);
        Assert.Same(TransformOf(root).Transform, TransformOf(child).Transform.Parent);
        Assert.Equal(8, _world.EntityCount);
    }

    #endregion

    #region Multiple instances

    [Fact]
    public void Instantiate_Twice_CreatesIndependentInstances()
    {
        var (root, _, _, _) = CreateHierarchy();
        _world.AddComponent(root, RigidBodyComponent.CreateDynamic(3f));
        var prefab = PrefabUtility.CreateFromEntity(_world, root);

        using var target = new World();
        var first = PrefabUtility.InstantiateHierarchy(target, prefab);
        var second = PrefabUtility.InstantiateHierarchy(target, prefab);

        Assert.Equal(8, target.EntityCount);
        Assert.Empty(first.Intersect(second));

        // Mutating one instance leaves the other (and the prefab) untouched
        target.GetComponent<TransformComponent>(first[0]).Transform.LocalPosition = new Vector3(100, 0, 0);
        target.GetComponent<RigidBodyComponent>(first[0]).Mass = 99f;

        AssertVector(new Vector3(1, 2, 3), target.GetComponent<TransformComponent>(second[0]).LocalPosition);
        AssertVector(new Vector3(1, 7, 10), target.GetComponent<TransformComponent>(second[3]).Position);
        AssertVector(new Vector3(100, 5, 7), target.GetComponent<TransformComponent>(first[3]).Position);
        Assert.Equal(3f, target.GetComponent<RigidBodyComponent>(second[0]).Mass);

        var third = PrefabUtility.Instantiate(target, prefab);
        AssertVector(new Vector3(1, 2, 3), target.GetComponent<TransformComponent>(third).LocalPosition);
        Assert.Equal(3f, target.GetComponent<RigidBodyComponent>(third).Mass);

        // Children of one instance are parented within that instance only
        Assert.Same(
            target.GetComponent<TransformComponent>(second[0]).Transform,
            target.GetComponent<TransformComponent>(second[1]).Transform.Parent);
    }

    [Fact]
    public void Instantiate_DestroyingOneInstance_LeavesOtherIntact()
    {
        var (root, _, _, _) = CreateHierarchy();
        var prefab = PrefabUtility.CreateFromEntity(_world, root);

        using var target = new World();
        var first = PrefabUtility.InstantiateHierarchy(target, prefab);
        var second = PrefabUtility.InstantiateHierarchy(target, prefab);

        foreach (var entity in first)
            target.DestroyEntity(entity);

        Assert.Equal(4, target.EntityCount);
        Assert.All(second, e => Assert.True(target.IsAlive(e)));
        AssertVector(new Vector3(1, 7, 10), target.GetComponent<TransformComponent>(second[3]).Position);
    }

    #endregion

    #region Transform override

    [Fact]
    public void Instantiate_PositionOverride_MovesRootAndChildrenFollow()
    {
        var (root, _, _, _) = CreateHierarchy();
        var prefab = PrefabUtility.CreateFromEntity(_world, root);

        using var target = new World();
        var entities = PrefabUtility.InstantiateHierarchy(target, prefab, position: new Vector3(10, 20, 30));

        AssertVector(new Vector3(10, 20, 30), target.GetComponent<TransformComponent>(entities[0]).Position);
        AssertVector(new Vector3(0, 5, 0), target.GetComponent<TransformComponent>(entities[1]).LocalPosition);
        AssertVector(new Vector3(10, 25, 37), target.GetComponent<TransformComponent>(entities[3]).Position);
    }

    [Fact]
    public void Instantiate_RotationOverride_ReplacesRootRotationOnly()
    {
        var source = _world.CreateEntity();
        _world.AddComponent(source, new TransformComponent(
            new Vector3(1, 2, 3), Quaternion.CreateFromAxisAngle(Vector3.UnitX, 1f), new Vector3(2, 2, 2)));
        var prefab = PrefabUtility.CreateFromEntity(_world, source);
        var rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI / 2f);

        var instance = PrefabUtility.Instantiate(_world, prefab, rotation: rotation);

        var transform = TransformOf(instance);
        Assert.True(MathF.Abs(Quaternion.Dot(rotation, transform.Rotation)) > 1f - Epsilon);
        AssertVector(new Vector3(1, 2, 3), transform.Position);
        AssertVector(new Vector3(2, 2, 2), transform.LocalScale);
    }

    [Fact]
    public void Instantiate_RotationOverride_RotatesChildren()
    {
        var root = CreateEntity(Vector3.Zero);
        CreateEntity(new Vector3(1, 0, 0), root);
        var prefab = PrefabUtility.CreateFromEntity(_world, root);

        using var target = new World();
        var entities = PrefabUtility.InstantiateHierarchy(target, prefab,
            new Vector3(5, 0, 0), Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI / 2f));

        // +X rotated 90 degrees about Y becomes -Z
        AssertVector(new Vector3(5, 0, -1), target.GetComponent<TransformComponent>(entities[1]).Position);
    }

    [Fact]
    public void Instantiate_NoOverride_KeepsStoredTransform()
    {
        var source = CreateEntity(new Vector3(4, 5, 6));
        var prefab = PrefabUtility.CreateFromEntity(_world, source);

        var instance = PrefabUtility.Instantiate(_world, prefab);

        AssertVector(new Vector3(4, 5, 6), TransformOf(instance).Position);
    }

    [Fact]
    public void Instantiate_OverrideOnRootWithoutTransform_AddsTransform()
    {
        var source = _world.CreateEntity();
        _world.AddComponent(source, CameraComponent.CreatePerspective(60f, 1f));
        var prefab = PrefabUtility.CreateFromEntity(_world, source);

        var plain = PrefabUtility.Instantiate(_world, prefab);
        var placed = PrefabUtility.Instantiate(_world, prefab, position: new Vector3(1, 1, 1));

        Assert.False(_world.HasComponent<TransformComponent>(plain));
        AssertVector(new Vector3(1, 1, 1), TransformOf(placed).Position);
    }

    #endregion

    #region Entity reference remapping

    [Fact]
    public void Instantiate_RemapsEntityReferencesToNewEntities()
    {
        var serializer = CreateSerializerWithLinks();
        var outside = CreateEntity(Vector3.Zero);
        var root = CreateEntity(Vector3.Zero);
        var child = CreateEntity(Vector3.UnitX, root);
        _world.AddComponent(root, new LinkComponent { Target = child, Other = outside, Payload = 1 });
        _world.AddComponent(child, new LinkComponent { Target = root, Other = child, Payload = 2 });

        var prefab = Prefab.FromJson(PrefabUtility.CreateFromEntity(_world, root, serializer: serializer).ToJson());

        // Burn some IDs so new entities cannot coincide with the source IDs
        for (int i = 0; i < 5; i++)
            _world.CreateEntity();

        var first = PrefabUtility.InstantiateHierarchy(_world, prefab, serializer: serializer);
        var second = PrefabUtility.InstantiateHierarchy(_world, prefab, serializer: serializer);

        foreach (var instance in new[] { first, second })
        {
            var rootLink = _world.GetComponent<LinkComponent>(instance[0]);
            var childLink = _world.GetComponent<LinkComponent>(instance[1]);

            Assert.Equal(instance[1], rootLink.Target);
            Assert.Equal(instance[0], childLink.Target);
            Assert.Equal(instance[1], childLink.Other);
            Assert.Equal(1, rootLink.Payload);
            Assert.Equal(2, childLink.Payload);

            // A reference to an entity outside the prefab cannot be resolved
            Assert.True(rootLink.Other.IsNull);
        }

        Assert.NotEqual(first[0], second[0]);

        // Source entities keep their original references
        Assert.Equal(child, _world.GetComponent<LinkComponent>(root).Target);
        Assert.Equal(outside, _world.GetComponent<LinkComponent>(root).Other);
    }

    [Fact]
    public void Instantiate_InFreshWorld_RemapsReferences()
    {
        var serializer = CreateSerializerWithLinks();
        for (int i = 0; i < 10; i++)
            _world.CreateEntity();
        var root = CreateEntity(Vector3.Zero);
        var child = CreateEntity(Vector3.UnitX, root);
        _world.AddComponent(child, new LinkComponent { Target = root });

        var prefab = PrefabUtility.CreateFromEntity(_world, root, serializer: serializer);

        using var target = new World();
        var entities = PrefabUtility.InstantiateHierarchy(target, prefab, serializer: serializer);

        var link = target.GetComponent<LinkComponent>(entities[1]);
        Assert.Equal(entities[0], link.Target);
        Assert.True(target.IsAlive(link.Target));
        Assert.True(link.Other.IsNull);
    }

    [Fact]
    public void Instantiate_UnknownComponentType_IsSkipped()
    {
        var serializer = CreateSerializerWithLinks();
        var root = CreateEntity(new Vector3(1, 0, 0));
        _world.AddComponent(root, new LinkComponent { Payload = 5 });
        var prefab = PrefabUtility.CreateFromEntity(_world, root, serializer: serializer);

        // The default serializer does not know LinkComponent
        using var target = new World();
        var instance = PrefabUtility.Instantiate(target, prefab);

        Assert.True(target.HasComponent<TransformComponent>(instance));
        Assert.False(target.HasComponent<LinkComponent>(instance));
    }

    #endregion

    #region Malformed input

    [Theory]
    [InlineData("")]
    [InlineData("not json at all")]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("{}")]
    [InlineData("{\"format\":\"RedHolePrefab\",\"version\":1")]
    [InlineData("{\"format\":\"SomethingElse\",\"version\":1,\"nodes\":[{\"id\":1,\"parent\":0,\"components\":[]}]}")]
    [InlineData("{\"format\":\"RedHolePrefab\",\"version\":999,\"nodes\":[{\"id\":1,\"parent\":0,\"components\":[]}]}")]
    [InlineData("{\"format\":\"RedHolePrefab\",\"version\":1}")]
    [InlineData("{\"format\":\"RedHolePrefab\",\"version\":1,\"nodes\":null}")]
    [InlineData("{\"format\":\"RedHolePrefab\",\"version\":1,\"nodes\":[]}")]
    [InlineData("{\"format\":\"RedHolePrefab\",\"version\":1,\"nodes\":[null]}")]
    [InlineData("{\"format\":\"RedHolePrefab\",\"version\":1,\"nodes\":\"oops\"}")]
    [InlineData("{\"format\":\"RedHolePrefab\",\"version\":1,\"nodes\":[{\"id\":0,\"parent\":0,\"components\":[]}]}")]
    [InlineData("{\"format\":\"RedHolePrefab\",\"version\":1,\"nodes\":[{\"id\":1,\"parent\":2,\"components\":[]}]}")]
    [InlineData("{\"format\":\"RedHolePrefab\",\"version\":1,\"nodes\":[{\"id\":1,\"parent\":0,\"components\":[]},{\"id\":1,\"parent\":1,\"components\":[]}]}")]
    [InlineData("{\"format\":\"RedHolePrefab\",\"version\":1,\"nodes\":[{\"id\":1,\"parent\":0,\"components\":[]},{\"id\":2,\"parent\":7,\"components\":[]}]}")]
    [InlineData("{\"format\":\"RedHolePrefab\",\"version\":1,\"nodes\":[{\"id\":1,\"parent\":0,\"components\":[]},{\"id\":2,\"parent\":2,\"components\":[]}]}")]
    [InlineData("{\"format\":\"RedHolePrefab\",\"version\":1,\"nodes\":[{\"id\":1,\"parent\":0,\"components\":null}]}")]
    [InlineData("{\"format\":\"RedHolePrefab\",\"version\":1,\"nodes\":[{\"id\":1,\"parent\":0,\"components\":[null]}]}")]
    [InlineData("{\"format\":\"RedHolePrefab\",\"version\":1,\"nodes\":[{\"id\":1,\"parent\":0,\"components\":[{\"typeId\":1,\"data\":null}]}]}")]
    [InlineData("{\"format\":\"RedHolePrefab\",\"version\":1,\"nodes\":[{\"id\":1,\"parent\":0,\"components\":[{\"typeId\":1,\"data\":\"!!not base64!!\"}]}]}")]
    [InlineData("{\"format\":\"RedHolePrefab\",\"version\":1,\"nodes\":[{\"id\":\"one\",\"parent\":0,\"components\":[]}]}")]
    public void FromJson_Malformed_ThrowsInvalidData(string json)
    {
        Assert.Throws<InvalidDataException>(() => Prefab.FromJson(json));
    }

    [Fact]
    public void FromJson_MinimalValidDocument_Loads()
    {
        var prefab = Prefab.FromJson(
            "{\"format\":\"RedHolePrefab\",\"version\":1,\"nodes\":[{\"id\":1,\"parent\":0,\"components\":[]}]}");

        var instance = PrefabUtility.Instantiate(_world, prefab);

        Assert.True(_world.IsAlive(instance));
    }

    [Fact]
    public void LoadFromFile_MissingFile_Throws()
    {
        Assert.Throws<FileNotFoundException>(() => Prefab.LoadFromFile(TempFile("missing.prefab")));
    }

    [Fact]
    public void TryLoadFromFile_MissingOrMalformed_ReturnsFalseWithError()
    {
        var garbage = TempFile("garbage.prefab");
        File.WriteAllBytes(garbage, new byte[] { 0x00, 0xFF, 0x13, 0x37, 0x80 });

        Assert.False(Prefab.TryLoadFromFile(TempFile("missing.prefab"), out var missing, out var missingError));
        Assert.Null(missing);
        Assert.NotEmpty(missingError);

        Assert.False(Prefab.TryLoadFromFile(garbage, out var malformed, out var malformedError));
        Assert.Null(malformed);
        Assert.NotEmpty(malformedError);
    }

    [Fact]
    public void Instantiate_TruncatedComponentData_ThrowsAndLeavesWorldUntouched()
    {
        var (root, child, _, _) = CreateHierarchy();
        var prefab = PrefabUtility.CreateFromEntity(_world, root);

        // Corrupt a component on a non-root node so earlier nodes were already created
        var node = prefab.Nodes.Single(n => n.Id == child.Id);
        node.Components[0].Data = node.Components[0].Data.Take(5).ToArray();
        var corrupted = Prefab.FromJson(prefab.ToJson());

        using var target = new World();
        target.CreateEntity();

        Assert.Throws<InvalidDataException>(() => PrefabUtility.Instantiate(target, corrupted));
        Assert.Equal(1, target.EntityCount);
    }

    [Fact]
    public void Instantiate_InvalidPrefabObject_Throws()
    {
        var prefab = Prefab.CreateEmpty("Empty");

        Assert.Throws<InvalidDataException>(() => PrefabUtility.Instantiate(_world, prefab));
        Assert.Throws<InvalidDataException>(() => prefab.SaveToFile(TempFile()));
        Assert.False(File.Exists(TempFile()));
    }

    #endregion

    #region ResourceManager integration

    [Fact]
    public void ResourceManager_LoadsAndCachesPrefabByPath()
    {
        var (root, _, _, _) = CreateHierarchy();
        PrefabUtility.CreateFromEntity(_world, root, "Cached").SaveToFile(TempFile("cached.prefab"));

        using var resources = new ResourceManager { BasePath = _tempDir };
        resources.RegisterLoader(new PrefabLoader());

        var first = resources.Load<Prefab>("cached.prefab");
        var second = resources.Load<Prefab>("cached.prefab");

        Assert.NotNull(first);
        Assert.Same(first, second);
        Assert.True(resources.IsLoaded<Prefab>("cached.prefab"));
        Assert.Equal(2, resources.GetRefCount("cached.prefab"));

        using var target = new World();
        PrefabUtility.Instantiate(target, first!);
        PrefabUtility.Instantiate(target, second!);
        Assert.Equal(8, target.EntityCount);
    }

    [Fact]
    public void ResourceManager_MissingOrMalformedPrefab_ReturnsNullAndIsNotCached()
    {
        File.WriteAllText(TempFile("bad.prefab"), "{ definitely not a prefab");

        using var resources = new ResourceManager { BasePath = _tempDir };
        resources.RegisterLoader(new PrefabLoader());

        Assert.Null(resources.Load<Prefab>("bad.prefab"));
        Assert.Null(resources.Load<Prefab>("missing.prefab"));
        Assert.False(resources.IsLoaded<Prefab>("bad.prefab"));
        Assert.False(resources.IsLoaded<Prefab>("missing.prefab"));
    }

    #endregion

    #region Shared serializer API

    [Fact]
    public void SceneSerializer_SerializeEntity_RoundTripsThroughTryDeserializeComponent()
    {
        var serializer = new SceneSerializer();
        var source = CreateEntity(new Vector3(3, 2, 1));
        _world.AddComponent(source, ColliderComponent.CreateSphere(2.5f));

        var components = serializer.SerializeEntity(_world, source);
        var copy = _world.CreateEntity();
        foreach (var (typeId, data) in components)
            Assert.True(serializer.TryDeserializeComponent(_world, copy, typeId, data));

        Assert.Equal(2, components.Count);
        Assert.Equal("TransformComponent", serializer.GetComponentTypeName(components[0].typeId));
        AssertVector(new Vector3(3, 2, 1), TransformOf(copy).LocalPosition);
        Assert.True(_world.HasComponent<ColliderComponent>(copy));
        Assert.False(serializer.TryDeserializeComponent(_world, copy, 424242, Array.Empty<byte>()));
        Assert.Null(serializer.GetComponentTypeName(424242));
    }

    #endregion
}
