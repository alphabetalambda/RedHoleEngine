using System.Numerics;
using RedHoleEngine.Components;
using RedHoleEngine.Core.ECS;
using RedHoleEngine.Core.Scene;
using RedHoleEngine.Physics;
using RedHoleEngine.Physics.Collision;
using RedHoleEngine.Rendering;
using RedHoleEngine.Serialization;

namespace RedHoleEngine.Tests.Serialization;

public class SceneSerializerTests : IDisposable
{
    private readonly string _testFilePath;
    private readonly World _world;
    private readonly SceneSerializer _serializer;

    public SceneSerializerTests()
    {
        _testFilePath = Path.Combine(Path.GetTempPath(), $"test_scene_{Guid.NewGuid()}.rhes");
        _world = new World();
        _serializer = new SceneSerializer();
    }

    public void Dispose()
    {
        _world.Dispose();
        if (File.Exists(_testFilePath))
            File.Delete(_testFilePath);
    }

    #region File Format Tests

    [Fact]
    public void SaveToFile_CreatesFile()
    {
        var entity = _world.CreateEntity();
        _world.AddComponent(entity, new TransformComponent(Vector3.Zero, Quaternion.Identity, Vector3.One));

        _serializer.SaveToFile(_world, _testFilePath);

        Assert.True(File.Exists(_testFilePath));
    }

    [Fact]
    public void SaveToFile_WritesCorrectMagicNumber()
    {
        var entity = _world.CreateEntity();
        _world.AddComponent(entity, new TransformComponent(Vector3.Zero, Quaternion.Identity, Vector3.One));

        _serializer.SaveToFile(_world, _testFilePath);

        using var stream = File.OpenRead(_testFilePath);
        using var reader = new BinaryReader(stream);
        uint magic = reader.ReadUInt32();

        Assert.Equal(0x52484553u, magic); // "RHES"
    }

    [Fact]
    public void SaveToFile_WritesVersionNumber()
    {
        var entity = _world.CreateEntity();
        _world.AddComponent(entity, new TransformComponent(Vector3.Zero, Quaternion.Identity, Vector3.One));

        _serializer.SaveToFile(_world, _testFilePath);

        using var stream = File.OpenRead(_testFilePath);
        using var reader = new BinaryReader(stream);
        reader.ReadUInt32(); // Skip magic
        uint version = reader.ReadUInt32();

        Assert.Equal(1u, version);
    }

    [Fact]
    public void LoadFromFile_ThrowsOnInvalidMagicNumber()
    {
        // Create file with invalid magic number
        using (var stream = File.Create(_testFilePath))
        using (var writer = new BinaryWriter(stream))
        {
            writer.Write(0x12345678u); // Wrong magic
            writer.Write(1u); // Version
            writer.Write(0); // Entity count
        }

        using var loadWorld = new World();
        var exception = Assert.Throws<InvalidDataException>(() => 
            _serializer.LoadFromFile(loadWorld, _testFilePath));
        
        Assert.Contains("Invalid scene file format", exception.Message);
    }

    [Fact]
    public void LoadFromFile_ThrowsOnNewerVersion()
    {
        // Create file with version higher than supported
        using (var stream = File.Create(_testFilePath))
        using (var writer = new BinaryWriter(stream))
        {
            writer.Write(0x52484553u); // Correct magic
            writer.Write(999u); // Future version
            writer.Write(0); // Entity count
        }

        using var loadWorld = new World();
        var exception = Assert.Throws<InvalidDataException>(() => 
            _serializer.LoadFromFile(loadWorld, _testFilePath));
        
        Assert.Contains("newer than supported", exception.Message);
    }

    #endregion

    #region TransformComponent Tests

    [Fact]
    public void RoundTrip_TransformComponent_PreservesData()
    {
        var position = new Vector3(1.5f, 2.5f, 3.5f);
        var rotation = Quaternion.CreateFromYawPitchRoll(0.5f, 0.3f, 0.1f);
        var scale = new Vector3(2f, 3f, 4f);

        var entity = _world.CreateEntity();
        _world.AddComponent(entity, new TransformComponent(position, rotation, scale));

        _serializer.SaveToFile(_world, _testFilePath);

        using var loadWorld = new World();
        _serializer.LoadFromFile(loadWorld, _testFilePath);

        var entities = loadWorld.Query<TransformComponent>().ToList();
        Assert.Single(entities);

        ref var loaded = ref loadWorld.GetComponent<TransformComponent>(entities[0]);
        Assert.Equal(position, loaded.LocalPosition);
        AssertQuaternionEqual(rotation, loaded.LocalRotation);
        Assert.Equal(scale, loaded.LocalScale);
    }

    #endregion

    #region CameraComponent Tests

    [Fact]
    public void RoundTrip_CameraComponent_PreservesData()
    {
        var entity = _world.CreateEntity();
        _world.AddComponent(entity, new TransformComponent()); // Required for entity to exist
        _world.AddComponent(entity, new CameraComponent
        {
            ProjectionType = ProjectionType.Orthographic,
            FieldOfView = 75f,
            NearPlane = 0.5f,
            FarPlane = 500f,
            OrthographicSize = 10f,
            AspectRatio = 1.5f,
            IsActive = true,
            Priority = 5
        });

        _serializer.SaveToFile(_world, _testFilePath);

        using var loadWorld = new World();
        _serializer.LoadFromFile(loadWorld, _testFilePath);

        var entities = loadWorld.Query<CameraComponent>().ToList();
        Assert.Single(entities);

        ref var loaded = ref loadWorld.GetComponent<CameraComponent>(entities[0]);
        Assert.Equal(ProjectionType.Orthographic, loaded.ProjectionType);
        Assert.Equal(75f, loaded.FieldOfView);
        Assert.Equal(0.5f, loaded.NearPlane);
        Assert.Equal(500f, loaded.FarPlane);
        Assert.Equal(10f, loaded.OrthographicSize);
        Assert.Equal(1.5f, loaded.AspectRatio);
        Assert.True(loaded.IsActive);
        Assert.Equal(5, loaded.Priority);
    }

    #endregion

    #region GravitySourceComponent Tests

    [Fact]
    public void RoundTrip_GravitySourceComponent_Schwarzschild_PreservesData()
    {
        var entity = _world.CreateEntity();
        _world.AddComponent(entity, new TransformComponent());
        _world.AddComponent(entity, GravitySourceComponent.CreateBlackHole(100f));

        _serializer.SaveToFile(_world, _testFilePath);

        using var loadWorld = new World();
        _serializer.LoadFromFile(loadWorld, _testFilePath);

        var entities = loadWorld.Query<GravitySourceComponent>().ToList();
        Assert.Single(entities);

        ref var loaded = ref loadWorld.GetComponent<GravitySourceComponent>(entities[0]);
        Assert.Equal(GravityType.Schwarzschild, loaded.GravityType);
        Assert.Equal(100f, loaded.Mass);
    }

    [Fact]
    public void RoundTrip_GravitySourceComponent_Kerr_PreservesData()
    {
        var spinAxis = Vector3.Normalize(new Vector3(0.1f, 1f, 0.2f));
        
        var entity = _world.CreateEntity();
        _world.AddComponent(entity, new TransformComponent());
        _world.AddComponent(entity, GravitySourceComponent.CreateRotatingBlackHole(150f, 0.8f, spinAxis));

        _serializer.SaveToFile(_world, _testFilePath);

        using var loadWorld = new World();
        _serializer.LoadFromFile(loadWorld, _testFilePath);

        var entities = loadWorld.Query<GravitySourceComponent>().ToList();
        Assert.Single(entities);

        ref var loaded = ref loadWorld.GetComponent<GravitySourceComponent>(entities[0]);
        Assert.Equal(GravityType.Kerr, loaded.GravityType);
        Assert.Equal(150f, loaded.Mass);
        Assert.Equal(0.8f, loaded.SpinParameter, 0.001f);
    }

    #endregion

    #region RigidBodyComponent Tests

    [Fact]
    public void RoundTrip_RigidBodyComponent_PreservesData()
    {
        var entity = _world.CreateEntity();
        _world.AddComponent(entity, new TransformComponent());
        _world.AddComponent(entity, new RigidBodyComponent
        {
            Type = RigidBodyType.Dynamic,
            Mass = 50f,
            Restitution = 0.7f,
            Friction = 0.3f,
            LinearDamping = 0.1f,
            AngularDamping = 0.2f,
            UseGravity = true,
            FreezePositionX = true,
            FreezePositionY = false,
            FreezePositionZ = true,
            FreezeRotationX = false,
            FreezeRotationY = true,
            FreezeRotationZ = false,
            CollisionLayer = 0x0F,
            CollisionMask = 0xF0
        });

        _serializer.SaveToFile(_world, _testFilePath);

        using var loadWorld = new World();
        _serializer.LoadFromFile(loadWorld, _testFilePath);

        var entities = loadWorld.Query<RigidBodyComponent>().ToList();
        Assert.Single(entities);

        ref var loaded = ref loadWorld.GetComponent<RigidBodyComponent>(entities[0]);
        Assert.Equal(RigidBodyType.Dynamic, loaded.Type);
        Assert.Equal(50f, loaded.Mass);
        Assert.Equal(0.7f, loaded.Restitution);
        Assert.Equal(0.3f, loaded.Friction);
        Assert.Equal(0.1f, loaded.LinearDamping);
        Assert.Equal(0.2f, loaded.AngularDamping);
        Assert.True(loaded.UseGravity);
        Assert.True(loaded.FreezePositionX);
        Assert.False(loaded.FreezePositionY);
        Assert.True(loaded.FreezePositionZ);
        Assert.False(loaded.FreezeRotationX);
        Assert.True(loaded.FreezeRotationY);
        Assert.False(loaded.FreezeRotationZ);
        Assert.Equal(0x0Fu, loaded.CollisionLayer);
        Assert.Equal(0xF0u, loaded.CollisionMask);
    }

    #endregion

    #region ColliderComponent Tests

    [Fact]
    public void RoundTrip_ColliderComponent_Sphere_PreservesData()
    {
        var entity = _world.CreateEntity();
        _world.AddComponent(entity, new TransformComponent());
        _world.AddComponent(entity, new ColliderComponent
        {
            ShapeType = ColliderType.Sphere,
            Offset = new Vector3(1f, 2f, 3f),
            IsTrigger = true,
            SphereRadius = 2.5f,
            MaterialRestitution = 0.8f,
            MaterialStaticFriction = 0.5f,
            MaterialDynamicFriction = 0.3f
        });

        _serializer.SaveToFile(_world, _testFilePath);

        using var loadWorld = new World();
        _serializer.LoadFromFile(loadWorld, _testFilePath);

        var entities = loadWorld.Query<ColliderComponent>().ToList();
        Assert.Single(entities);

        ref var loaded = ref loadWorld.GetComponent<ColliderComponent>(entities[0]);
        Assert.Equal(ColliderType.Sphere, loaded.ShapeType);
        Assert.Equal(new Vector3(1f, 2f, 3f), loaded.Offset);
        Assert.True(loaded.IsTrigger);
        Assert.Equal(2.5f, loaded.SphereRadius);
        Assert.Equal(0.8f, loaded.MaterialRestitution);
        Assert.Equal(0.5f, loaded.MaterialStaticFriction);
        Assert.Equal(0.3f, loaded.MaterialDynamicFriction);
    }

    [Fact]
    public void RoundTrip_ColliderComponent_Box_PreservesData()
    {
        var entity = _world.CreateEntity();
        _world.AddComponent(entity, new TransformComponent());
        _world.AddComponent(entity, new ColliderComponent
        {
            ShapeType = ColliderType.Box,
            BoxHalfExtents = new Vector3(1f, 2f, 3f),
            IsTrigger = false
        });

        _serializer.SaveToFile(_world, _testFilePath);

        using var loadWorld = new World();
        _serializer.LoadFromFile(loadWorld, _testFilePath);

        var entities = loadWorld.Query<ColliderComponent>().ToList();
        Assert.Single(entities);

        ref var loaded = ref loadWorld.GetComponent<ColliderComponent>(entities[0]);
        Assert.Equal(ColliderType.Box, loaded.ShapeType);
        Assert.Equal(new Vector3(1f, 2f, 3f), loaded.BoxHalfExtents);
        Assert.False(loaded.IsTrigger);
    }

    #endregion

    #region MaterialComponent Tests

    [Fact]
    public void RoundTrip_MaterialComponent_PreservesData()
    {
        var entity = _world.CreateEntity();
        _world.AddComponent(entity, new TransformComponent());
        _world.AddComponent(entity, new MaterialComponent
        {
            BaseColor = new Vector4(0.5f, 0.6f, 0.7f, 1f),
            Metallic = 0.8f,
            Roughness = 0.3f,
            EmissiveColor = new Vector3(1f, 0.5f, 0f),
            UseRaytracing = true
        });

        _serializer.SaveToFile(_world, _testFilePath);

        using var loadWorld = new World();
        _serializer.LoadFromFile(loadWorld, _testFilePath);

        var entities = loadWorld.Query<MaterialComponent>().ToList();
        Assert.Single(entities);

        ref var loaded = ref loadWorld.GetComponent<MaterialComponent>(entities[0]);
        Assert.Equal(new Vector4(0.5f, 0.6f, 0.7f, 1f), loaded.BaseColor);
        Assert.Equal(0.8f, loaded.Metallic);
        Assert.Equal(0.3f, loaded.Roughness);
        Assert.Equal(new Vector3(1f, 0.5f, 0f), loaded.EmissiveColor);
        Assert.True(loaded.UseRaytracing);
    }

    #endregion

    #region RenderSettingsComponent Tests

    [Fact]
    public void RoundTrip_RenderSettingsComponent_PreservesData()
    {
        var entity = _world.CreateEntity();
        _world.AddComponent(entity, new TransformComponent());
        _world.AddComponent(entity, new RenderSettingsComponent
        {
            Enabled = true,
            Mode = RenderMode.Raytraced,
            Preset = RaytracerQualityPreset.Quality,
            RaysPerPixel = 4,
            MaxBounces = 8,
            SamplesPerFrame = 2,
            Accumulate = true,
            Denoise = true,
            LensingQuality = LensingQuality.High,
            LensingMaxSteps = 200,
            LensingStepSize = 0.05f,
            LensingBvhCheckInterval = 5,
            LensingMaxDistance = 100f,
            ShowErgosphere = true,
            ErgosphereOpacity = 0.3f,
            ShowPhotonSphere = true,
            PhotonSphereOpacity = 0.5f
        });

        _serializer.SaveToFile(_world, _testFilePath);

        using var loadWorld = new World();
        _serializer.LoadFromFile(loadWorld, _testFilePath);

        var entities = loadWorld.Query<RenderSettingsComponent>().ToList();
        Assert.Single(entities);

        ref var loaded = ref loadWorld.GetComponent<RenderSettingsComponent>(entities[0]);
        Assert.True(loaded.Enabled);
        Assert.Equal(RenderMode.Raytraced, loaded.Mode);
        Assert.Equal(RaytracerQualityPreset.Quality, loaded.Preset);
        Assert.Equal(4, loaded.RaysPerPixel);
        Assert.Equal(8, loaded.MaxBounces);
        Assert.Equal(2, loaded.SamplesPerFrame);
        Assert.True(loaded.Accumulate);
        Assert.True(loaded.Denoise);
        Assert.Equal(LensingQuality.High, loaded.LensingQuality);
        Assert.Equal(200, loaded.LensingMaxSteps);
        Assert.Equal(0.05f, loaded.LensingStepSize);
        Assert.Equal(5, loaded.LensingBvhCheckInterval);
        Assert.Equal(100f, loaded.LensingMaxDistance);
        Assert.True(loaded.ShowErgosphere);
        Assert.Equal(0.3f, loaded.ErgosphereOpacity);
        Assert.True(loaded.ShowPhotonSphere);
        Assert.Equal(0.5f, loaded.PhotonSphereOpacity);
    }

    #endregion

    #region RaytracerMeshComponent Tests

    [Fact]
    public void RoundTrip_RaytracerMeshComponent_PreservesData()
    {
        var entity = _world.CreateEntity();
        _world.AddComponent(entity, new TransformComponent());
        _world.AddComponent(entity, new RaytracerMeshComponent(true) { StaticOnly = true });

        _serializer.SaveToFile(_world, _testFilePath);

        using var loadWorld = new World();
        _serializer.LoadFromFile(loadWorld, _testFilePath);

        var entities = loadWorld.Query<RaytracerMeshComponent>().ToList();
        Assert.Single(entities);

        ref var loaded = ref loadWorld.GetComponent<RaytracerMeshComponent>(entities[0]);
        Assert.True(loaded.Enabled);
        Assert.True(loaded.StaticOnly);
    }

    #endregion

    #region MeshComponent Tests

    [Fact]
    public void RoundTrip_MeshComponent_PreservesFlags()
    {
        var entity = _world.CreateEntity();
        _world.AddComponent(entity, new TransformComponent());
        _world.AddComponent(entity, new MeshComponent
        {
            CastShadows = true,
            ReceiveShadows = false,
            LayerMask = 0xFF,
            Visible = true
        });

        _serializer.SaveToFile(_world, _testFilePath);

        using var loadWorld = new World();
        _serializer.LoadFromFile(loadWorld, _testFilePath);

        var entities = loadWorld.Query<MeshComponent>().ToList();
        Assert.Single(entities);

        ref var loaded = ref loadWorld.GetComponent<MeshComponent>(entities[0]);
        Assert.True(loaded.CastShadows);
        Assert.False(loaded.ReceiveShadows);
        Assert.Equal(0xFFu, loaded.LayerMask);
        Assert.True(loaded.Visible);
        // Note: MeshHandle is not preserved - needs post-load resolution
    }

    #endregion

    #region Multiple Entity Tests

    [Fact]
    public void RoundTrip_MultipleEntities_PreservesAll()
    {
        // Create 3 entities with different components
        var entity1 = _world.CreateEntity();
        _world.AddComponent(entity1, new TransformComponent(new Vector3(1, 0, 0), Quaternion.Identity, Vector3.One));
        _world.AddComponent(entity1, new MaterialComponent { BaseColor = new Vector4(1, 0, 0, 1) });

        var entity2 = _world.CreateEntity();
        _world.AddComponent(entity2, new TransformComponent(new Vector3(0, 1, 0), Quaternion.Identity, Vector3.One));
        _world.AddComponent(entity2, new CameraComponent { FieldOfView = 60f });

        var entity3 = _world.CreateEntity();
        _world.AddComponent(entity3, new TransformComponent(new Vector3(0, 0, 1), Quaternion.Identity, Vector3.One));
        _world.AddComponent(entity3, GravitySourceComponent.CreateBlackHole(50f));

        _serializer.SaveToFile(_world, _testFilePath);

        using var loadWorld = new World();
        _serializer.LoadFromFile(loadWorld, _testFilePath);

        // Verify all entities loaded
        var transforms = loadWorld.Query<TransformComponent>().ToList();
        Assert.Equal(3, transforms.Count);

        // Verify each type of component
        var materials = loadWorld.Query<MaterialComponent>().ToList();
        Assert.Single(materials);
        Assert.Equal(new Vector4(1, 0, 0, 1), loadWorld.GetComponent<MaterialComponent>(materials[0]).BaseColor);

        var cameras = loadWorld.Query<CameraComponent>().ToList();
        Assert.Single(cameras);
        Assert.Equal(60f, loadWorld.GetComponent<CameraComponent>(cameras[0]).FieldOfView);

        var gravitySources = loadWorld.Query<GravitySourceComponent>().ToList();
        Assert.Single(gravitySources);
        Assert.Equal(50f, loadWorld.GetComponent<GravitySourceComponent>(gravitySources[0]).Mass);
    }

    [Fact]
    public void RoundTrip_EntityWithManyComponents_PreservesAll()
    {
        var entity = _world.CreateEntity();
        _world.AddComponent(entity, new TransformComponent(new Vector3(5, 5, 5), Quaternion.Identity, Vector3.One * 2));
        _world.AddComponent(entity, new CameraComponent { FieldOfView = 90f, IsActive = true });
        _world.AddComponent(entity, new MaterialComponent { BaseColor = new Vector4(0.5f, 0.5f, 0.5f, 1f), Metallic = 1f });
        _world.AddComponent(entity, new RigidBodyComponent { Mass = 10f, Type = RigidBodyType.Dynamic });
        _world.AddComponent(entity, new ColliderComponent { ShapeType = ColliderType.Sphere, SphereRadius = 1f });

        _serializer.SaveToFile(_world, _testFilePath);

        using var loadWorld = new World();
        _serializer.LoadFromFile(loadWorld, _testFilePath);

        var entities = loadWorld.Query<TransformComponent>().ToList();
        Assert.Single(entities);
        var loadedEntity = entities[0];

        Assert.True(loadWorld.HasComponent<TransformComponent>(loadedEntity));
        Assert.True(loadWorld.HasComponent<CameraComponent>(loadedEntity));
        Assert.True(loadWorld.HasComponent<MaterialComponent>(loadedEntity));
        Assert.True(loadWorld.HasComponent<RigidBodyComponent>(loadedEntity));
        Assert.True(loadWorld.HasComponent<ColliderComponent>(loadedEntity));

        Assert.Equal(new Vector3(5, 5, 5), loadWorld.GetComponent<TransformComponent>(loadedEntity).LocalPosition);
        Assert.Equal(90f, loadWorld.GetComponent<CameraComponent>(loadedEntity).FieldOfView);
        Assert.Equal(1f, loadWorld.GetComponent<MaterialComponent>(loadedEntity).Metallic);
        Assert.Equal(10f, loadWorld.GetComponent<RigidBodyComponent>(loadedEntity).Mass);
        Assert.Equal(1f, loadWorld.GetComponent<ColliderComponent>(loadedEntity).SphereRadius);
    }

    #endregion

    #region Forward Compatibility Tests

    [Fact]
    public void Load_SkipsUnknownComponentTypes()
    {
        // Create a valid file manually with an unknown component type
        using (var stream = File.Create(_testFilePath))
        using (var writer = new BinaryWriter(stream))
        {
            writer.Write(0x52484553u); // Magic
            writer.Write(1u); // Version
            writer.Write(1); // Entity count

            // Entity with unknown component
            writer.Write(1); // Entity ID
            writer.Write(2); // Component count

            // Known component: TransformComponent (type ID 1)
            writer.Write(1); // Type ID
            var transformData = new byte[40]; // Position(12) + Quaternion(16) + Scale(12)
            BitConverter.GetBytes(1.0f).CopyTo(transformData, 0); // Position X
            BitConverter.GetBytes(2.0f).CopyTo(transformData, 4); // Position Y
            BitConverter.GetBytes(3.0f).CopyTo(transformData, 8); // Position Z
            BitConverter.GetBytes(0.0f).CopyTo(transformData, 12); // Rotation X
            BitConverter.GetBytes(0.0f).CopyTo(transformData, 16); // Rotation Y
            BitConverter.GetBytes(0.0f).CopyTo(transformData, 20); // Rotation Z
            BitConverter.GetBytes(1.0f).CopyTo(transformData, 24); // Rotation W
            BitConverter.GetBytes(1.0f).CopyTo(transformData, 28); // Scale X
            BitConverter.GetBytes(1.0f).CopyTo(transformData, 32); // Scale Y
            BitConverter.GetBytes(1.0f).CopyTo(transformData, 36); // Scale Z
            writer.Write(transformData.Length);
            writer.Write(transformData);

            // Unknown component (type ID 999)
            writer.Write(999); // Unknown type ID
            var unknownData = new byte[] { 1, 2, 3, 4, 5 };
            writer.Write(unknownData.Length);
            writer.Write(unknownData);
        }

        using var loadWorld = new World();
        // Should not throw
        _serializer.LoadFromFile(loadWorld, _testFilePath);

        // Should have loaded the known component
        var entities = loadWorld.Query<TransformComponent>().ToList();
        Assert.Single(entities);
        
        ref var transform = ref loadWorld.GetComponent<TransformComponent>(entities[0]);
        Assert.Equal(new Vector3(1, 2, 3), transform.LocalPosition);
    }

    #endregion

    #region Load Clears Existing World Tests

    [Fact]
    public void Load_ClearsExistingEntities()
    {
        // Create entities in original world
        var entity = _world.CreateEntity();
        _world.AddComponent(entity, new TransformComponent(Vector3.One, Quaternion.Identity, Vector3.One));
        
        _serializer.SaveToFile(_world, _testFilePath);

        // Create a world with existing entities
        using var loadWorld = new World();
        var existingEntity = loadWorld.CreateEntity();
        loadWorld.AddComponent(existingEntity, new TransformComponent(Vector3.Zero, Quaternion.Identity, Vector3.One));
        loadWorld.AddComponent(existingEntity, new CameraComponent());

        // Load should clear existing
        _serializer.LoadFromFile(loadWorld, _testFilePath);

        // Should only have one entity with position Vector3.One (from file)
        var entities = loadWorld.Query<TransformComponent>().ToList();
        Assert.Single(entities);
        Assert.Equal(Vector3.One, loadWorld.GetComponent<TransformComponent>(entities[0]).LocalPosition);

        // Camera component should not exist (was on pre-existing entity)
        var cameras = loadWorld.Query<CameraComponent>().ToList();
        Assert.Empty(cameras);
    }

    #endregion

    #region Empty World Tests

    [Fact]
    public void RoundTrip_EmptyWorld_Succeeds()
    {
        // Don't add any entities
        _serializer.SaveToFile(_world, _testFilePath);

        using var loadWorld = new World();
        // Should not throw
        _serializer.LoadFromFile(loadWorld, _testFilePath);

        Assert.Equal(0, loadWorld.EntityCount);
    }

    #endregion

    #region Stream Tests

    [Fact]
    public void SaveAndLoad_ViaStream_Works()
    {
        var entity = _world.CreateEntity();
        _world.AddComponent(entity, new TransformComponent(new Vector3(7, 8, 9), Quaternion.Identity, Vector3.One));

        using var memoryStream = new MemoryStream();
        
        _serializer.Save(_world, memoryStream);
        
        memoryStream.Position = 0;
        
        using var loadWorld = new World();
        _serializer.Load(loadWorld, memoryStream);

        var entities = loadWorld.Query<TransformComponent>().ToList();
        Assert.Single(entities);
        Assert.Equal(new Vector3(7, 8, 9), loadWorld.GetComponent<TransformComponent>(entities[0]).LocalPosition);
    }

    #endregion

    #region Additional Collider Tests

    [Fact]
    public void RoundTrip_ColliderComponent_Capsule_PreservesData()
    {
        var entity = _world.CreateEntity();
        _world.AddComponent(entity, new TransformComponent());
        _world.AddComponent(entity, new ColliderComponent
        {
            ShapeType = ColliderType.Capsule,
            CapsuleRadius = 0.5f,
            CapsuleHeight = 2f,
            CapsuleAxis = 1, // Y-axis
            Offset = new Vector3(0, 1, 0)
        });

        _serializer.SaveToFile(_world, _testFilePath);

        using var loadWorld = new World();
        _serializer.LoadFromFile(loadWorld, _testFilePath);

        var entities = loadWorld.Query<ColliderComponent>().ToList();
        Assert.Single(entities);

        ref var loaded = ref loadWorld.GetComponent<ColliderComponent>(entities[0]);
        Assert.Equal(ColliderType.Capsule, loaded.ShapeType);
        Assert.Equal(0.5f, loaded.CapsuleRadius);
        Assert.Equal(2f, loaded.CapsuleHeight);
        Assert.Equal(1, loaded.CapsuleAxis);
    }

    [Fact]
    public void RoundTrip_ColliderComponent_Plane_PreservesData()
    {
        var entity = _world.CreateEntity();
        _world.AddComponent(entity, new TransformComponent());
        _world.AddComponent(entity, new ColliderComponent
        {
            ShapeType = ColliderType.Plane,
            PlaneNormal = Vector3.UnitY,
            PlaneDistance = 5f
        });

        _serializer.SaveToFile(_world, _testFilePath);

        using var loadWorld = new World();
        _serializer.LoadFromFile(loadWorld, _testFilePath);

        var entities = loadWorld.Query<ColliderComponent>().ToList();
        Assert.Single(entities);

        ref var loaded = ref loadWorld.GetComponent<ColliderComponent>(entities[0]);
        Assert.Equal(ColliderType.Plane, loaded.ShapeType);
        Assert.Equal(Vector3.UnitY, loaded.PlaneNormal);
        Assert.Equal(5f, loaded.PlaneDistance);
    }

    [Fact]
    public void RoundTrip_ColliderComponent_NullMaterialOverrides_PreservesNulls()
    {
        var entity = _world.CreateEntity();
        _world.AddComponent(entity, new TransformComponent());
        _world.AddComponent(entity, new ColliderComponent
        {
            ShapeType = ColliderType.Sphere,
            SphereRadius = 1f,
            MaterialRestitution = null,
            MaterialStaticFriction = null,
            MaterialDynamicFriction = null
        });

        _serializer.SaveToFile(_world, _testFilePath);

        using var loadWorld = new World();
        _serializer.LoadFromFile(loadWorld, _testFilePath);

        var entities = loadWorld.Query<ColliderComponent>().ToList();
        Assert.Single(entities);

        ref var loaded = ref loadWorld.GetComponent<ColliderComponent>(entities[0]);
        Assert.Null(loaded.MaterialRestitution);
        Assert.Null(loaded.MaterialStaticFriction);
        Assert.Null(loaded.MaterialDynamicFriction);
    }

    #endregion

    #region Additional GravitySource Tests

    [Fact]
    public void RoundTrip_GravitySourceComponent_Uniform_PreservesData()
    {
        var entity = _world.CreateEntity();
        _world.AddComponent(entity, new TransformComponent());
        _world.AddComponent(entity, new GravitySourceComponent
        {
            GravityType = GravityType.Uniform,
            UniformDirection = new Vector3(0, -1, 0),
            UniformStrength = 9.81f,
            AffectsLight = false
        });

        _serializer.SaveToFile(_world, _testFilePath);

        using var loadWorld = new World();
        _serializer.LoadFromFile(loadWorld, _testFilePath);

        var entities = loadWorld.Query<GravitySourceComponent>().ToList();
        Assert.Single(entities);

        ref var loaded = ref loadWorld.GetComponent<GravitySourceComponent>(entities[0]);
        Assert.Equal(GravityType.Uniform, loaded.GravityType);
        Assert.Equal(new Vector3(0, -1, 0), loaded.UniformDirection);
        Assert.Equal(9.81f, loaded.UniformStrength);
        Assert.False(loaded.AffectsLight);
    }

    [Fact]
    public void RoundTrip_GravitySourceComponent_Newtonian_PreservesData()
    {
        var entity = _world.CreateEntity();
        _world.AddComponent(entity, new TransformComponent());
        _world.AddComponent(entity, new GravitySourceComponent
        {
            GravityType = GravityType.Newtonian,
            Mass = 1000f,
            MaxRange = 500f,
            AffectsLight = false
        });

        _serializer.SaveToFile(_world, _testFilePath);

        using var loadWorld = new World();
        _serializer.LoadFromFile(loadWorld, _testFilePath);

        var entities = loadWorld.Query<GravitySourceComponent>().ToList();
        Assert.Single(entities);

        ref var loaded = ref loadWorld.GetComponent<GravitySourceComponent>(entities[0]);
        Assert.Equal(GravityType.Newtonian, loaded.GravityType);
        Assert.Equal(1000f, loaded.Mass);
        Assert.Equal(500f, loaded.MaxRange);
    }

    #endregion

    #region Additional RigidBody Tests

    [Fact]
    public void RoundTrip_RigidBodyComponent_Static_PreservesData()
    {
        var entity = _world.CreateEntity();
        _world.AddComponent(entity, new TransformComponent());
        _world.AddComponent(entity, new RigidBodyComponent
        {
            Type = RigidBodyType.Static,
            Mass = 0f,
            UseGravity = false
        });

        _serializer.SaveToFile(_world, _testFilePath);

        using var loadWorld = new World();
        _serializer.LoadFromFile(loadWorld, _testFilePath);

        var entities = loadWorld.Query<RigidBodyComponent>().ToList();
        Assert.Single(entities);

        ref var loaded = ref loadWorld.GetComponent<RigidBodyComponent>(entities[0]);
        Assert.Equal(RigidBodyType.Static, loaded.Type);
        Assert.False(loaded.UseGravity);
    }

    [Fact]
    public void RoundTrip_RigidBodyComponent_Kinematic_PreservesData()
    {
        var entity = _world.CreateEntity();
        _world.AddComponent(entity, new TransformComponent());
        _world.AddComponent(entity, new RigidBodyComponent
        {
            Type = RigidBodyType.Kinematic,
            Mass = 1f,
            UseGravity = false
        });

        _serializer.SaveToFile(_world, _testFilePath);

        using var loadWorld = new World();
        _serializer.LoadFromFile(loadWorld, _testFilePath);

        var entities = loadWorld.Query<RigidBodyComponent>().ToList();
        Assert.Single(entities);

        ref var loaded = ref loadWorld.GetComponent<RigidBodyComponent>(entities[0]);
        Assert.Equal(RigidBodyType.Kinematic, loaded.Type);
    }

    #endregion

    #region Edge Case Tests

    [Fact]
    public void RoundTrip_TransformComponent_ExtremeValues_PreservesData()
    {
        var entity = _world.CreateEntity();
        _world.AddComponent(entity, new TransformComponent(
            new Vector3(float.MaxValue, float.MinValue, 0),
            Quaternion.Identity,
            new Vector3(float.Epsilon, float.Epsilon, float.Epsilon)));

        _serializer.SaveToFile(_world, _testFilePath);

        using var loadWorld = new World();
        _serializer.LoadFromFile(loadWorld, _testFilePath);

        var entities = loadWorld.Query<TransformComponent>().ToList();
        Assert.Single(entities);

        ref var loaded = ref loadWorld.GetComponent<TransformComponent>(entities[0]);
        Assert.Equal(float.MaxValue, loaded.LocalPosition.X);
        Assert.Equal(float.MinValue, loaded.LocalPosition.Y);
        Assert.Equal(float.Epsilon, loaded.LocalScale.X);
    }

    [Fact]
    public void RoundTrip_MaterialComponent_ZeroValues_PreservesData()
    {
        var entity = _world.CreateEntity();
        _world.AddComponent(entity, new TransformComponent());
        _world.AddComponent(entity, new MaterialComponent
        {
            BaseColor = Vector4.Zero,
            Metallic = 0f,
            Roughness = 0f,
            EmissiveColor = Vector3.Zero,
            UseRaytracing = false
        });

        _serializer.SaveToFile(_world, _testFilePath);

        using var loadWorld = new World();
        _serializer.LoadFromFile(loadWorld, _testFilePath);

        var entities = loadWorld.Query<MaterialComponent>().ToList();
        Assert.Single(entities);

        ref var loaded = ref loadWorld.GetComponent<MaterialComponent>(entities[0]);
        Assert.Equal(Vector4.Zero, loaded.BaseColor);
        Assert.Equal(0f, loaded.Metallic);
        Assert.Equal(0f, loaded.Roughness);
    }

    #endregion

    #region Custom Component Registration Tests

    [Fact]
    public void RegisterComponent_AllowsCustomSerializer()
    {
        // This test verifies the registration API works
        var customSerializer = new SceneSerializer();
        
        // Re-registering with same ID should overwrite
        customSerializer.RegisterComponent<TransformComponent>(1, new TransformComponentSerializer());
        
        var entity = _world.CreateEntity();
        _world.AddComponent(entity, new TransformComponent(new Vector3(1, 2, 3), Quaternion.Identity, Vector3.One));

        customSerializer.SaveToFile(_world, _testFilePath);

        using var loadWorld = new World();
        customSerializer.LoadFromFile(loadWorld, _testFilePath);

        var entities = loadWorld.Query<TransformComponent>().ToList();
        Assert.Single(entities);
    }

    #endregion

    #region Corrupted File Tests

    [Fact]
    public void LoadFromFile_ThrowsOnTruncatedHeader()
    {
        // Create file with truncated header (only magic, no version)
        using (var stream = File.Create(_testFilePath))
        using (var writer = new BinaryWriter(stream))
        {
            writer.Write(0x52484553u); // Magic only, no version
        }

        using var loadWorld = new World();
        Assert.ThrowsAny<Exception>(() => _serializer.LoadFromFile(loadWorld, _testFilePath));
    }

    [Fact]
    public void LoadFromFile_ThrowsOnEmptyFile()
    {
        // Create empty file
        File.WriteAllBytes(_testFilePath, Array.Empty<byte>());

        using var loadWorld = new World();
        Assert.ThrowsAny<Exception>(() => _serializer.LoadFromFile(loadWorld, _testFilePath));
    }

    #endregion

    #region Large Scene Tests

    [Fact]
    public void RoundTrip_ManyEntities_PreservesAll()
    {
        const int entityCount = 100;

        for (int i = 0; i < entityCount; i++)
        {
            var entity = _world.CreateEntity();
            _world.AddComponent(entity, new TransformComponent(
                new Vector3(i, i * 2, i * 3),
                Quaternion.Identity,
                Vector3.One));
        }

        _serializer.SaveToFile(_world, _testFilePath);

        using var loadWorld = new World();
        _serializer.LoadFromFile(loadWorld, _testFilePath);

        var entities = loadWorld.Query<TransformComponent>().ToList();
        Assert.Equal(entityCount, entities.Count);

        // Verify positions are all different (data integrity)
        var positions = entities.Select(e => loadWorld.GetComponent<TransformComponent>(e).LocalPosition).ToList();
        Assert.Equal(entityCount, positions.Distinct().Count());
    }

    #endregion

    #region RaytracerMeshComponent Edge Cases

    [Fact]
    public void RoundTrip_RaytracerMeshComponent_Disabled_PreservesData()
    {
        var entity = _world.CreateEntity();
        _world.AddComponent(entity, new TransformComponent());
        _world.AddComponent(entity, new RaytracerMeshComponent(false) { StaticOnly = false });

        _serializer.SaveToFile(_world, _testFilePath);

        using var loadWorld = new World();
        _serializer.LoadFromFile(loadWorld, _testFilePath);

        var entities = loadWorld.Query<RaytracerMeshComponent>().ToList();
        Assert.Single(entities);

        ref var loaded = ref loadWorld.GetComponent<RaytracerMeshComponent>(entities[0]);
        Assert.False(loaded.Enabled);
        Assert.False(loaded.StaticOnly);
    }

    #endregion

    #region Helper Methods

    private static void AssertQuaternionEqual(Quaternion expected, Quaternion actual, float tolerance = 0.0001f)
    {
        // Quaternions can represent the same rotation with opposite signs
        var sameSigns = Math.Abs(expected.X - actual.X) < tolerance &&
                        Math.Abs(expected.Y - actual.Y) < tolerance &&
                        Math.Abs(expected.Z - actual.Z) < tolerance &&
                        Math.Abs(expected.W - actual.W) < tolerance;

        var oppositeSigns = Math.Abs(expected.X + actual.X) < tolerance &&
                           Math.Abs(expected.Y + actual.Y) < tolerance &&
                           Math.Abs(expected.Z + actual.Z) < tolerance &&
                           Math.Abs(expected.W + actual.W) < tolerance;

        Assert.True(sameSigns || oppositeSigns, 
            $"Quaternions not equal. Expected: {expected}, Actual: {actual}");
    }

    #endregion
}
