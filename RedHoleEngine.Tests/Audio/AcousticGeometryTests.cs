using System.Numerics;
using RedHoleEngine.Audio;
using RedHoleEngine.Components;
using RedHoleEngine.Core.ECS;
using RedHoleEngine.Resources;

namespace RedHoleEngine.Tests.Audio;

public class AcousticGeometryTests
{
    // Wall in the YZ plane at x=0, spanning y,z in [-1,1]
    private static MeshAcousticShape Wall(float x = 0f) => new(
        new[] { new Vector3(x, -1, -1), new Vector3(x, 1, -1), new Vector3(x, 1, 1), new Vector3(x, -1, 1) },
        new uint[] { 0, 1, 2, 0, 2, 3 });

    [Fact]
    public void Mesh_HitsTriangleAndFacesRay()
    {
        var wall = Wall();
        Assert.True(wall.Raycast(new Vector3(-5, 0.2f, 0.1f), Vector3.UnitX, 0.01f, 100f, out var t, out var n));
        Assert.Equal(5f, t, 1e-4f);
        Assert.Equal(-1f, n.X, 1e-4f);

        Assert.True(wall.Raycast(new Vector3(5, 0, 0), -Vector3.UnitX, 0.01f, 100f, out _, out n));
        Assert.Equal(1f, n.X, 1e-4f);
    }

    [Fact]
    public void Mesh_MissesOutsideTrianglesRangeAndBehind()
    {
        var wall = Wall();
        Assert.False(wall.Raycast(new Vector3(-5, 3, 0), Vector3.UnitX, 0.01f, 100f, out _, out _)); // beside
        Assert.False(wall.Raycast(new Vector3(-5, 0, 0), Vector3.UnitX, 0.01f, 4f, out _, out _));   // too short
        Assert.False(wall.Raycast(new Vector3(-5, 0, 0), -Vector3.UnitX, 0.01f, 100f, out _, out _)); // away
        Assert.False(wall.Raycast(new Vector3(-5, 0, 0), Vector3.UnitY, 0.01f, 100f, out _, out _));  // parallel
    }

    [Fact]
    public void Mesh_Bvh_ReturnsNearestOfManyTriangles()
    {
        var verts = new List<Vector3>();
        var idx = new List<uint>();
        for (int i = 0; i < 40; i++)
        {
            uint b = (uint)verts.Count;
            float x = i * 2f;
            verts.Add(new Vector3(x, -1, -1)); verts.Add(new Vector3(x, 1, -1)); verts.Add(new Vector3(x, 0, 1));
            idx.AddRange(new[] { b, b + 1, b + 2 });
        }
        var shape = new MeshAcousticShape(verts.ToArray(), idx.ToArray());
        Assert.Equal(40, shape.TriangleCount);
        Assert.True(shape.Raycast(new Vector3(-3, 0, 0), Vector3.UnitX, 0.01f, 1000f, out var t, out _));
        Assert.Equal(3f, t, 1e-4f);
        Assert.True(shape.Raycast(new Vector3(100, 0, 0), -Vector3.UnitX, 0.01f, 1000f, out t, out _));
        Assert.Equal(22f, t, 1e-4f); // nearest is x = 78
    }

    [Fact]
    public void Mesh_ConstructedFromMeshAppliesWorldTransform()
    {
        var mesh = new Mesh(new[]
        {
            new Vertex(new Vector3(0, -1, -1)), new Vertex(new Vector3(0, 1, -1)), new Vertex(new Vector3(0, 0, 1))
        }, new uint[] { 0, 1, 2 });
        var shape = new MeshAcousticShape(mesh, Matrix4x4.CreateTranslation(10, 0, 0));
        Assert.True(shape.Raycast(Vector3.Zero, Vector3.UnitX, 0.01f, 100f, out var t, out _));
        Assert.Equal(10f, t, 1e-4f);
    }

    [Fact]
    public void Raytracer_UsesMeshGeometryInsteadOfSphere()
    {
        // A small mesh wall at x=10 does not block a path that passes 3 units above it,
        // whereas the legacy radius-5 sphere would.
        using var world = new World();
        var manager = new ResourceManager();
        manager.Add("wall", new Mesh(new[]
        {
            new Vertex(new Vector3(0, -1, -1)), new Vertex(new Vector3(0, 1, -1)), new Vertex(new Vector3(0, 0, 1))
        }, new uint[] { 0, 1, 2 }));
        var handle = manager.GetHandle<Mesh>("wall");

        var settings = new AcousticQualitySettings
        {
            RaysPerSource = 0, MaxDistance = 100f, EnableTransmission = false,
            EnableRelativisticEffects = false, SpeedOfSound = 343f
        };

        var wall = world.CreateEntity();
        world.AddComponent(wall, new TransformComponent(new Vector3(10, 3, 0)));
        world.AddComponent(wall, new MeshComponent(handle));
        world.AddComponent(wall, AcousticSurfaceComponent.Create(AcousticMaterial.EventHorizon));

        var tracer = new AcousticRaytracer(world, settings);
        tracer.UpdateSceneCache();
        Assert.Single(tracer.TracePaths(Vector3.Zero, Vector3.Zero, new Vector3(20, 0, 0), Vector3.Zero));

        // Move it into the path: now it occludes.
        world.GetComponent<TransformComponent>(wall).Position = new Vector3(10, 0, 0);
        tracer.UpdateSceneCache();
        Assert.Empty(tracer.TracePaths(Vector3.Zero, Vector3.Zero, new Vector3(20, 0, 0), Vector3.Zero));
    }

    [Fact]
    public void Raytracer_UsesBoxColliderAndFallsBackToSphere()
    {
        using var world = new World();
        var settings = new AcousticQualitySettings
        {
            RaysPerSource = 0, MaxDistance = 100f, EnableTransmission = false,
            EnableRelativisticEffects = false, SpeedOfSound = 343f
        };

        var box = world.CreateEntity();
        world.AddComponent(box, new TransformComponent(new Vector3(10, 3, 0)));
        world.AddComponent(box, ColliderComponent.CreateBox(new Vector3(0.5f)));
        world.AddComponent(box, AcousticSurfaceComponent.Create(AcousticMaterial.EventHorizon));

        var tracer = new AcousticRaytracer(world, settings);
        tracer.UpdateSceneCache();
        Assert.Single(tracer.TracePaths(Vector3.Zero, Vector3.Zero, new Vector3(20, 0, 0), Vector3.Zero));

        world.GetComponent<TransformComponent>(box).Position = new Vector3(10, 0, 0);
        tracer.UpdateSceneCache();
        Assert.Empty(tracer.TracePaths(Vector3.Zero, Vector3.Zero, new Vector3(20, 0, 0), Vector3.Zero));

        // No mesh or collider: legacy sphere of radius 5 still blocks a path 3 units off-center.
        using var world2 = new World();
        var plain = world2.CreateEntity();
        world2.AddComponent(plain, new TransformComponent(new Vector3(10, 3, 0)));
        world2.AddComponent(plain, AcousticSurfaceComponent.Create(AcousticMaterial.EventHorizon));
        var tracer2 = new AcousticRaytracer(world2, settings);
        tracer2.UpdateSceneCache();
        Assert.Empty(tracer2.TracePaths(Vector3.Zero, Vector3.Zero, new Vector3(20, 0, 0), Vector3.Zero));
    }
}
