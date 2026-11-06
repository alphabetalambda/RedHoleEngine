using System.Numerics;
using RedHoleEngine.Components;
using RedHoleEngine.Core.ECS;
using RedHoleEngine.Engine;
using RedHoleEngine.Rendering;
using RedHoleEngine.Rendering.Rasterization;
using RedHoleEngine.Resources;

namespace RedHoleEngine.Tests.Rendering;

public class RasterizationCoverageTests
{
    [Fact]
    public void RasterMeshData_StartsEmpty()
    {
        var data = new RasterMeshData();
        Assert.Empty(data.Vertices);
        Assert.Empty(data.Indices);
        var vertex = new RasterVertex { Position = Vector3.UnitX, Color = Vector4.One };
        Assert.Equal(Vector3.UnitX, vertex.Position);
    }

    [Fact]
    public void Update_WithoutBackendOrGeometry_UploadsNothing()
    {
        var world = new World();
        var system = world.AddSystem(new RasterMeshSystem());
        Assert.Equal(-51, system.Priority);
        system.Update(0.016f);

        var backend = new RecordingGraphicsBackend();
        system.SetBackend(backend);
        system.Update(0.016f);
        Assert.Empty(backend.RasterUploads);
    }

    [Fact]
    public void Update_RasterizedMode_TransformsMeshAndSkipsHiddenOnes()
    {
        var world = new World();
        var resources = new ResourceManager();
        resources.Add("tri", new Mesh(
            new[]
            {
                new Vertex(Vector3.Zero),
                new Vertex(Vector3.UnitX),
                new Vertex(Vector3.UnitY)
            },
            new uint[] { 0, 1, 2 },
            "tri"));

        var backend = new RecordingGraphicsBackend();
        backend.RenderSettings.Mode = RenderMode.Rasterized;
        var system = world.AddSystem(new RasterMeshSystem());
        system.SetBackend(backend);

        var shown = world.CreateEntity();
        world.AddComponent(shown, new TransformComponent(new Vector3(0f, 3f, 0f)));
        world.AddComponent(shown, new MeshComponent(resources.GetHandle<Mesh>("tri")));
        world.AddComponent(shown, new MaterialComponent { BaseColor = new Vector4(0.1f, 0.2f, 0.3f, 0.4f) });

        var hidden = world.CreateEntity();
        var hiddenMesh = new MeshComponent(resources.GetHandle<Mesh>("tri")) { Visible = false };
        world.AddComponent(hidden, new TransformComponent(Vector3.Zero));
        world.AddComponent(hidden, hiddenMesh);

        system.Update(0.016f);
        system.Update(0.016f);

        Assert.Single(backend.RasterUploads);
        var mesh = backend.RasterUploads[0];
        Assert.Equal(3, mesh.Vertices.Length);
        Assert.Equal(new uint[] { 0, 1, 2 }, mesh.Indices);
        Assert.Equal(new Vector4(0.1f, 0.2f, 0.3f, 0.4f), mesh.Vertices[0].Color);
        Assert.Equal(new Vector3(0f, 3f, 0f), mesh.Vertices[0].Position);
        Assert.Equal(new Vector3(1f, 3f, 0f), mesh.Vertices[1].Position);
        Assert.Equal(new Vector3(0f, 4f, 0f), mesh.Vertices[2].Position);
    }

    [Fact]
    public void Update_DrawsLaserOverlay_InRaytracedMode()
    {
        var world = new World();
        var backend = new RecordingGraphicsBackend();
        backend.RenderSettings.Mode = RenderMode.Raytraced;
        var system = world.AddSystem(new RasterMeshSystem());
        system.SetBackend(backend);
        system.SetCamera(new Camera(new Vector3(0f, 0f, 6f)));

        world.AddComponent(world.CreateEntity(), new LaserSegmentComponent
        {
            Start = Vector3.Zero,
            End = new Vector3(0f, 0f, 0.0001f),
            Width = 1f,
            CoreWidth = 1f,
            Intensity = 1f,
            BeamColor = Vector4.One,
            CoreColor = Vector4.One
        });
        system.Update(0.016f);
        Assert.Empty(backend.RasterUploads);

        world.AddComponent(world.CreateEntity(), new LaserSegmentComponent
        {
            Start = Vector3.Zero,
            End = Vector3.UnitX * 4f,
            Width = 1f,
            BeamColor = new Vector4(0f, 1f, 0f, 1f),
            CoreColor = Vector4.One,
            CoreWidth = 0.25f,
            Intensity = 2f,
            ShowEnergyPulses = false
        });

        system.Update(0.016f);
        var mesh = backend.RasterUploads[0];
        Assert.Equal(12, mesh.Vertices.Length);
        Assert.Equal(18, mesh.Indices.Length);
        Assert.Equal(0f, mesh.Vertices[0].Color.X, precision: 3);
        Assert.Equal(2f * 0.3f, mesh.Vertices[0].Color.W, precision: 3);
        Assert.Equal(2f, mesh.Vertices[8].Color.X, precision: 3);
        Assert.True(MathF.Abs(mesh.Vertices[0].Position.Y) > MathF.Abs(mesh.Vertices[8].Position.Y));
    }

    [Fact]
    public void Update_EnergyRails_AddQuadsBeyondTheBeamLayers()
    {
        var world = new World();
        var backend = new RecordingGraphicsBackend();
        var system = world.AddSystem(new RasterMeshSystem());
        system.SetBackend(backend);
        system.SetCamera(new Camera(new Vector3(0f, 4f, 2f)));
        world.AddComponent(world.CreateEntity(), new LaserSegmentComponent
        {
            Start = Vector3.Zero,
            End = Vector3.UnitX * 5f,
            Width = 0.4f,
            BeamColor = Vector4.One,
            CoreColor = Vector4.One,
            CoreWidth = 0.2f,
            Intensity = 1f,
            ShowEnergyPulses = true,
            EnergyPulseCount = 4,
            EnergyPulseSpeed = 3f,
            EnergyPulseSize = 0.2f,
            EnergyPulseColor = new Vector4(1f, 1f, 0f, 1f)
        });

        system.Update(0.016f);
        var mesh = backend.RasterUploads[0];
        Assert.True(mesh.Vertices.Length > 12);
        Assert.Equal(0, mesh.Vertices.Length % 4);
        Assert.Contains(mesh.Vertices, vertex => vertex.Color == new Vector4(1f, 1f, 0f, 1f));
    }
}
