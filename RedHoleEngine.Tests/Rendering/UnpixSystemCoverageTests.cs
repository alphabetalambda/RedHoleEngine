using System.Numerics;
using RedHoleEngine.Components;
using RedHoleEngine.Core.ECS;
using RedHoleEngine.Physics;
using RedHoleEngine.Rendering;
using RedHoleEngine.Resources;

namespace RedHoleEngine.Tests.Rendering;

public class UnpixSystemCoverageTests
{
    [Fact]
    public void KinematicPieces_SpawnOnTheSurface_Move_Shrink_AndExpire()
    {
        var world = new World();
        var resources = new ResourceManager();
        var system = world.AddSystem(new UnpixSystem());
        system.Initialize(resources);
        Assert.True(resources.IsLoaded<Mesh>("mesh_unpix_cube"));

        var source = world.CreateEntity();
        resources.Add("wedge", TriangleMesh());
        world.AddComponent(source, new TransformComponent(new Vector3(10f, 0f, 0f), Quaternion.Identity, Vector3.One));
        world.AddComponent(source, new MeshComponent(resources.GetHandle<Mesh>("wedge")));
        world.AddComponent(source, new MaterialComponent { BaseColor = new Vector4(0.2f, 0.4f, 0.6f, 1f) });
        world.AddComponent(source, new UnpixComponent(cubeSize: 1f, dissolveDuration: 1f)
        {
            SpawnOnStart = true,
            HideSource = true,
            UsePhysics = false,
            VelocityScale = 2f,
            StartDelay = 0f,
            MaxCubes = 8
        });

        system.Update(0.1f);
        Assert.False(world.HasComponent<MeshComponent>(source));
        var pieces = world.Query<UnpixPieceComponent>().ToList();
        Assert.NotEmpty(pieces);
        Assert.True(pieces.Count <= 8);
        var first = pieces[0];
        var piece = world.GetComponent<UnpixPieceComponent>(first);
        var transform = world.GetComponent<TransformComponent>(first);
        Assert.False(piece.UsePhysics);
        // Pieces spawn after the piece update, so the spawn frame does not age them.
        Assert.Equal(0f, piece.Age);
        Assert.Equal(piece.StartScale, transform.LocalScale);
        Assert.True(transform.Position.X > 9f);
        Assert.Equal(new Vector4(0.2f, 0.4f, 0.6f, 1f), world.GetComponent<MaterialComponent>(first).BaseColor);
        Assert.True(world.HasComponent<RaytracerMeshComponent>(first));

        var position = transform.Position;
        system.Update(0.1f);
        piece = world.GetComponent<UnpixPieceComponent>(first);
        transform = world.GetComponent<TransformComponent>(first);
        Assert.Equal(0.1f, piece.Age, precision: 3);
        Assert.Equal(piece.StartScale.X * 0.9f, transform.LocalScale.X, precision: 3);
        Assert.NotEqual(position, transform.Position);

        system.Update(1f);
        Assert.Empty(world.Query<UnpixPieceComponent>());
    }

    [Fact]
    public void StartDelay_AndEmptyMesh_DoNotSpawn()
    {
        var world = new World();
        var resources = new ResourceManager();
        var system = world.AddSystem(new UnpixSystem());
        system.Initialize(resources);

        var delayed = world.CreateEntity();
        resources.Add("wedge", TriangleMesh());
        world.AddComponent(delayed, new TransformComponent(Vector3.Zero));
        world.AddComponent(delayed, new MeshComponent(resources.GetHandle<Mesh>("wedge")));
        world.AddComponent(delayed, new UnpixComponent(1f, 1f) { StartDelay = 1f, HideSource = false });

        var empty = world.CreateEntity();
        resources.Add("empty", new Mesh("empty"));
        world.AddComponent(empty, new TransformComponent(Vector3.Zero));
        world.AddComponent(empty, new MeshComponent(resources.GetHandle<Mesh>("empty")));
        world.AddComponent(empty, new UnpixComponent(1f, 1f));

        system.Update(0.2f);
        Assert.Empty(world.Query<UnpixPieceComponent>());
        Assert.True(world.HasComponent<MeshComponent>(delayed));

        system.Update(1f);
        Assert.NotEmpty(world.Query<UnpixPieceComponent>());
        Assert.True(world.HasComponent<MeshComponent>(delayed));
        Assert.True(world.HasComponent<MeshComponent>(empty));
    }

    [Fact]
    public void PhysicsPieces_GainBodies_AndClearTheirStoredImpulse()
    {
        var world = new World();
        var resources = new ResourceManager();
        var system = world.AddSystem(new UnpixSystem());
        system.Initialize(resources);

        var source = world.CreateEntity();
        resources.Add("wedge", TriangleMesh());
        world.AddComponent(source, new TransformComponent(Vector3.Zero));
        world.AddComponent(source, new MeshComponent(resources.GetHandle<Mesh>("wedge")));
        world.AddComponent(source, new UnpixComponent(1f, 2f)
        {
            UsePhysics = true,
            HideSource = false,
            CubeMass = 1.5f,
            CubeRestitution = 0.2f,
            CubeFriction = 0.4f,
            InitialImpulseScale = 3f,
            VelocityScale = 1f
        });

        system.Update(0.05f);
        var pieces = world.Query<UnpixPieceComponent>().ToList();
        Assert.NotEmpty(pieces);
        foreach (var entity in pieces)
        {
            var piece = world.GetComponent<UnpixPieceComponent>(entity);
            Assert.True(piece.UsePhysics);
            Assert.Equal(0f, piece.Age);
            Assert.True(piece.Velocity.LengthSquared() > 0f);
            Assert.True(world.HasComponent<RigidBodyComponent>(entity));
            Assert.Equal(1.5f, world.GetComponent<RigidBodyComponent>(entity).Mass);
            Assert.True(world.HasComponent<ColliderComponent>(entity));
        }
        system.Update(0.05f);
        foreach (var entity in pieces)
        {
            Assert.Equal(Vector3.Zero, world.GetComponent<UnpixPieceComponent>(entity).Velocity);
        }
        Assert.True(world.HasComponent<MeshComponent>(source));
    }

    [Fact]
    public void SpawnOnStartFalse_LeavesTheSourceUntouched()
    {
        var world = new World();
        var resources = new ResourceManager();
        var system = world.AddSystem(new UnpixSystem());
        var source = world.CreateEntity();
        resources.Add("wedge", TriangleMesh());
        world.AddComponent(source, new TransformComponent(Vector3.Zero));
        world.AddComponent(source, new MeshComponent(resources.GetHandle<Mesh>("wedge")));
        world.AddComponent(source, new UnpixComponent(1f, 1f) { SpawnOnStart = false });

        system.Update(1f);
        Assert.Empty(world.Query<UnpixPieceComponent>());
        Assert.True(world.HasComponent<MeshComponent>(source));
    }

    private static Mesh TriangleMesh()
    {
        return new Mesh(
            new[]
            {
                new Vertex(new Vector3(0f, 0f, 0f)),
                new Vertex(new Vector3(1f, 0f, 0f)),
                new Vertex(new Vector3(0f, 1f, 0f))
            },
            new uint[] { 0, 1, 2 },
            "wedge");
    }
}
