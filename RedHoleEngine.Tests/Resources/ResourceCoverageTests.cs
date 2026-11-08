using System.Numerics;
using RedHoleEngine.Resources;

namespace RedHoleEngine.Tests.Resources;

public class MeshCoverageTests
{
    [Fact]
    public void EmptyMesh_HasZeroBounds_AndDisposeClearsData()
    {
        var mesh = new Mesh("empty");
        Assert.Equal(64, Vertex.SizeInBytes);
        mesh.RecalculateBounds();
        mesh.RecalculateNormals();
        Assert.Equal(Vector3.Zero, mesh.BoundsMin);
        Assert.Equal(Vector3.Zero, mesh.BoundsMax);
        Assert.Equal(0f, mesh.BoundingSphereRadius);

        mesh.Dispose();
        Assert.Empty(mesh.Vertices);
        Assert.Empty(mesh.Indices);
    }

    [Fact]
    public void Cube_HasClosedBoundsAndUnitNormals()
    {
        var cube = Mesh.CreateCube(2f);
        Assert.Equal("Cube", cube.Name);
        Assert.Equal(24, cube.Vertices.Length);
        Assert.Equal(36, cube.Indices.Length);
        Assert.Equal(new Vector3(-1f, -1f, -1f), cube.BoundsMin);
        Assert.Equal(Vector3.One, cube.BoundsMax);
        Assert.Equal(MathF.Sqrt(3f), cube.BoundingSphereRadius, precision: 4);
        Assert.All(cube.Vertices, vertex => Assert.Equal(1f, vertex.Normal.Length(), precision: 4));
        Assert.Contains(cube.Vertices, vertex => vertex.Normal == Vector3.UnitY);
        Assert.Contains(cube.Indices, index => index == 23);
    }

    [Fact]
    public void SphereAndPlane_GenerateIndexedSurfaces()
    {
        var sphere = Mesh.CreateSphere(2f, segments: 4, rings: 2);
        Assert.Equal(15, sphere.Vertices.Length);
        Assert.Equal(48, sphere.Indices.Length);
        Assert.All(sphere.Vertices, vertex => Assert.Equal(2f, vertex.Position.Length(), precision: 3));
        Assert.True(sphere.BoundsMax.Y <= 2.01f);
        Assert.True(sphere.BoundsMin.Y >= -2.01f);

        var plane = Mesh.CreatePlane(4f, 2f, segmentsX: 2, segmentsZ: 1);
        Assert.Equal(6, plane.Vertices.Length);
        Assert.Equal(12, plane.Indices.Length);
        Assert.Equal(new Vector3(-2f, 0f, -1f), plane.BoundsMin);
        Assert.Equal(new Vector3(2f, 0f, 1f), plane.BoundsMax);
        Assert.All(plane.Vertices, vertex =>
        {
            Assert.Equal(1f, MathF.Abs(vertex.Tangent.W));
            Assert.True(float.IsFinite(vertex.Tangent.X));
        });
    }

    [Fact]
    public void RecalculateNormalsAndTangents_RebuildFaceData()
    {
        var mesh = new Mesh(
            new[]
            {
                new Vertex(Vector3.Zero, Vector3.Zero, Vector2.Zero),
                new Vertex(Vector3.UnitX, Vector3.Zero, Vector2.UnitX),
                new Vertex(Vector3.UnitZ, Vector3.Zero, Vector2.UnitY)
            },
            new uint[] { 0, 1, 2 });
        mesh.Vertices[0].Normal = Vector3.UnitX;

        mesh.RecalculateNormals();
        Assert.Equal(1f, mesh.Vertices[0].Normal.Length(), precision: 4);
        Assert.Equal(mesh.Vertices[0].Normal, mesh.Vertices[1].Normal);

        mesh.RecalculateTangents();
        Assert.Equal(1f, new Vector3(mesh.Vertices[0].Tangent.X, mesh.Vertices[0].Tangent.Y, mesh.Vertices[0].Tangent.Z).Length(), precision: 3);
        Assert.Equal(1f, mesh.Vertices[0].Tangent.W);
    }
}

public class ResourceManagerCoverageTests
{
    [Fact]
    public void AddGetRelease_TracksReferencesAndDisposes()
    {
        var manager = new ResourceManager();
        var mesh = Mesh.CreateCube(1f);
        manager.Add("cube", mesh);

        Assert.True(manager.IsLoaded<Mesh>("cube"));
        Assert.Equal(1, manager.GetRefCount("cube"));
        Assert.Contains("cube", manager.GetLoadedIds<Mesh>());
        Assert.Same(mesh, manager.Get<Mesh>("cube"));
        Assert.Equal(1, manager.GetRefCount("cube"));

        manager.Release<Mesh>("cube");
        Assert.False(manager.IsLoaded<Mesh>("cube"));
        Assert.Empty(mesh.Vertices);
        Assert.Equal(0, manager.GetRefCount("cube"));
        manager.Release<Mesh>("cube");
        Assert.Equal(0, manager.GetRefCount("missing"));
    }

    [Fact]
    public void Handle_LoadsThroughTheRegisteredLoader()
    {
        var dir = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "res-" + Guid.NewGuid().ToString("N"))).FullName;
        try
        {
            var file = Path.Combine(dir, "shader.comp.spv");
            File.WriteAllBytes(file, new byte[] { 1, 2, 3, 4 });

            var manager = new ResourceManager { BasePath = dir };
            Assert.Throws<InvalidOperationException>(() => manager.Load<ShaderResource>("shader.comp.spv"));

            var loader = new RecordingShaderLoader();
            manager.RegisterLoader(loader);
            Assert.Same(loader, manager.GetLoader<ShaderResource>());

            var handle = manager.GetHandle<ShaderResource>("shader.comp.spv");
            Assert.True(handle.IsValid);
            Assert.False(handle.IsLoaded);
            Assert.False(handle.TryGet(out _));
            Assert.Equal("shader.comp.spv", handle.Id);

            var loaded = handle.Get();
            Assert.NotNull(loaded);
            Assert.Equal(file, loader.LastPath);
            Assert.Equal(new byte[] { 1, 2, 3, 4 }, loaded!.SpirVCode);
            Assert.True(handle.TryGet(out var again));
            Assert.Same(loaded, again);
            Assert.Equal(handle, manager.GetHandle<ShaderResource>("shader.comp.spv"));
            Assert.NotEqual(default, handle);

            var asyncLoaded = manager.LoadAsync<ShaderResource>("shader.comp.spv").GetAwaiter().GetResult();
            Assert.Same(loaded, asyncLoaded);
            Assert.True(manager.GetRefCount("shader.comp.spv") >= 2);

            manager.UnloadAll<ShaderResource>();
            Assert.False(manager.IsLoaded<ShaderResource>("shader.comp.spv"));
            manager.Dispose();
            Assert.Null(manager.GetLoader<ShaderResource>());
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void DefaultHandle_IsInvalid()
    {
        ResourceHandle<Mesh> handle = default;
        Assert.False(handle.IsValid);
        Assert.False(handle.IsLoaded);
        Assert.Null(handle.Get());
        Assert.False(handle.TryGet(out _));
        Assert.Equal(handle, default);
        Assert.Contains("Mesh", handle.ToString());
    }

    private sealed class RecordingShaderLoader : ResourceLoader<ShaderResource>
    {
        public string? LastPath { get; private set; }

        public override ShaderResource? Load(string path)
        {
            LastPath = path;
            if (!File.Exists(path))
                return null;
            return new ShaderResource(ShaderStage.Compute, File.ReadAllBytes(path), path, "main");
        }
    }
}

public class ShaderResourceCoverageTests
{
    [Fact]
    public void Loader_ReadsSpirv_InfersStage_AndRefusesGlslCompile()
    {
        var dir = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "shaders-" + Guid.NewGuid().ToString("N"))).FullName;
        try
        {
            var loader = new ShaderLoader();
            Assert.Null(loader.Load(Path.Combine(dir, "missing.comp")));

            var cases = new (string Name, ShaderStage Stage)[]
            {
                ("light.vert.spv", ShaderStage.Vertex),
                ("light.frag.spv", ShaderStage.Fragment),
                ("trace.comp.spv", ShaderStage.Compute),
                ("shape.geom.spv", ShaderStage.Geometry),
                ("hull.tesc.spv", ShaderStage.TessellationControl),
                ("domain.tese.spv", ShaderStage.TessellationEvaluation),
                ("untagged.spv", ShaderStage.Compute)
            };

            foreach (var (name, stage) in cases)
            {
                var path = Path.Combine(dir, name);
                File.WriteAllBytes(path, new byte[] { 7, 8, (byte)stage });
                var shader = loader.Load(path);
                Assert.NotNull(shader);
                Assert.Equal(stage, shader!.Stage);
                Assert.Equal(new byte[] { 7, 8, (byte)stage }, shader.SpirVCode);
                Assert.Equal("main", shader.EntryPoint);
                shader.Dispose();
            }

            var source = Path.Combine(dir, "plain.vert");
            File.WriteAllText(source, "void main() {}");
            Assert.Null(loader.Load(source));

            File.WriteAllBytes(source + ".spv", new byte[] { 9 });
            var sibling = loader.Load(source);
            Assert.NotNull(sibling);
            Assert.Equal(ShaderStage.Vertex, sibling!.Stage);
            Assert.Equal(new byte[] { 9 }, sibling.SpirVCode);
            Assert.Equal(source, sibling.SourcePath);

            loader.CompileGlsl = true;
            Assert.Null(loader.Load(source)); // invalid GLSL: error result, no throw
            Assert.False(string.IsNullOrEmpty(loader.LastError));

            File.WriteAllText(source, "#version 450\nvoid main() { gl_Position = vec4(0.0); }\n");
            var compiled = loader.Load(source);
            if (compiled != null)
            {
                Assert.Equal(ShaderStage.Vertex, compiled.Stage);
                Assert.True(compiled.SpirVCode.Length > 4);
                Assert.Equal(0x07230203u, BitConverter.ToUInt32(compiled.SpirVCode, 0));
                Assert.Null(loader.LastError);
            }
            else
            {
                Assert.Contains("unavailable", loader.LastError); // native shaderc not present
            }

            var custom = new ShaderResource(ShaderStage.Fragment, new byte[] { 1 }, "mem", "frag_main");
            Assert.Equal("frag_main", custom.EntryPoint);
            custom.Dispose();
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }
}
