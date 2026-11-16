using System.Numerics;
using System.Runtime.InteropServices;
using RedHoleEngine.Components;
using RedHoleEngine.Core.ECS;
using RedHoleEngine.Rendering.PBR;
using RedHoleEngine.Resources;
using RedHoleEngine.Resources.ModelImport;
using SharpGLTF.Geometry;
using SharpGLTF.Geometry.VertexTypes;
using SharpGLTF.Materials;
using SharpGLTF.Scenes;
using SharpGLTF.Schema2;
using EngineAlphaMode = RedHoleEngine.Rendering.PBR.AlphaMode;
using EngineMesh = RedHoleEngine.Resources.Mesh;

namespace RedHoleEngine.Tests.Resources;

public class GltfImportTests : IDisposable
{
    private readonly string _tempDir;

    public GltfImportTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "RedHoleGltfTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, true); } catch (IOException) { }
    }

    #region Builders

    private static MaterialBuilder Material(string name) =>
        new MaterialBuilder(name).WithMetallicRoughnessShader();

    /// <summary>
    /// Unit quad in the XY plane facing +Z, with normals and UVs.
    /// </summary>
    private static MeshBuilder<VertexPositionNormal, VertexTexture1, VertexEmpty> Quad(string name, MaterialBuilder material)
    {
        var mesh = new MeshBuilder<VertexPositionNormal, VertexTexture1, VertexEmpty>(name);
        AddQuad(mesh, material, 0f);
        return mesh;
    }

    private static void AddQuad(MeshBuilder<VertexPositionNormal, VertexTexture1, VertexEmpty> mesh, MaterialBuilder material, float z)
    {
        VertexBuilder<VertexPositionNormal, VertexTexture1, VertexEmpty> V(float x, float y) =>
            new(new VertexPositionNormal(new Vector3(x, y, z), Vector3.UnitZ), new VertexTexture1(new Vector2(x, 1f - y)));

        var primitive = mesh.UsePrimitive(material);
        primitive.AddTriangle(V(0, 0), V(1, 0), V(1, 1));
        primitive.AddTriangle(V(0, 0), V(1, 1), V(0, 1));
    }

    /// <summary>
    /// Encode a 2x2 solid-colour RGBA PNG by hand (the engine has no image encoder).
    /// </summary>
    private static byte[] CreatePng(byte r, byte g, byte b)
    {
        const int size = 2;
        var raw = new byte[size * (1 + size * 4)];
        for (int y = 0; y < size; y++)
        {
            int row = y * (1 + size * 4);
            raw[row] = 0; // filter: none
            for (int x = 0; x < size; x++)
            {
                int o = row + 1 + x * 4;
                raw[o] = r; raw[o + 1] = g; raw[o + 2] = b; raw[o + 3] = 255;
            }
        }

        using var compressed = new MemoryStream();
        using (var zlib = new System.IO.Compression.ZLibStream(compressed, System.IO.Compression.CompressionLevel.Optimal, leaveOpen: true))
            zlib.Write(raw);

        using var stream = new MemoryStream();
        stream.Write(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A });
        WritePngChunk(stream, "IHDR", new byte[] { 0, 0, 0, size, 0, 0, 0, size, 8, 6, 0, 0, 0 });
        WritePngChunk(stream, "IDAT", compressed.ToArray());
        WritePngChunk(stream, "IEND", Array.Empty<byte>());
        return stream.ToArray();
    }

    private static uint Crc32(ReadOnlySpan<byte> data)
    {
        uint crc = 0xFFFFFFFF;
        foreach (byte value in data)
        {
            crc ^= value;
            for (int bit = 0; bit < 8; bit++)
                crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320 : crc >> 1;
        }
        return ~crc;
    }

    private static void WritePngChunk(Stream stream, string type, byte[] data)
    {
        var body = new byte[4 + data.Length];
        System.Text.Encoding.ASCII.GetBytes(type, body);
        data.CopyTo(body, 4);

        Span<byte> word = stackalloc byte[4];
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(word, (uint)data.Length);
        stream.Write(word);
        stream.Write(body);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(word, Crc32(body));
        stream.Write(word);
    }

    /// <summary>
    /// Write a minimal .gltf with one primitive holding only positions (and optional indices),
    /// with the buffer embedded as a base64 data URI.
    /// </summary>
    private string WriteRawGltf(string fileName, Vector3[] positions, ushort[]? indices, int mode = 4, string nodeJson = "{\"mesh\":0}")
    {
        var positionBytes = MemoryMarshal.AsBytes(positions.AsSpan()).ToArray();
        var indexBytes = indices == null ? Array.Empty<byte>() : MemoryMarshal.AsBytes(indices.AsSpan()).ToArray();
        var buffer = positionBytes.Concat(indexBytes).ToArray();

        var min = positions.Aggregate(Vector3.Min);
        var max = positions.Aggregate(Vector3.Max);
        FormattableString bounds = $"\"min\":[{min.X},{min.Y},{min.Z}],\"max\":[{max.X},{max.Y},{max.Z}]";

        var views = $"{{\"buffer\":0,\"byteOffset\":0,\"byteLength\":{positionBytes.Length},\"target\":34962}}";
        var accessors = $"{{\"bufferView\":0,\"componentType\":5126,\"count\":{positions.Length},\"type\":\"VEC3\",{FormattableString.Invariant(bounds)}}}";
        var primitive = "\"attributes\":{\"POSITION\":0}";

        if (indices != null)
        {
            views += $",{{\"buffer\":0,\"byteOffset\":{positionBytes.Length},\"byteLength\":{indexBytes.Length},\"target\":34963}}";
            accessors += $",{{\"bufferView\":1,\"componentType\":5123,\"count\":{indices.Length},\"type\":\"SCALAR\"}}";
            primitive += ",\"indices\":1";
        }

        var json =
            "{\"asset\":{\"version\":\"2.0\"},\"scene\":0,\"scenes\":[{\"nodes\":[0]}]," +
            $"\"nodes\":[{nodeJson}]," +
            $"\"meshes\":[{{\"primitives\":[{{{primitive},\"mode\":{mode}}}]}}]," +
            $"\"accessors\":[{accessors}],\"bufferViews\":[{views}]," +
            $"\"buffers\":[{{\"byteLength\":{buffer.Length},\"uri\":\"data:application/octet-stream;base64,{Convert.ToBase64String(buffer)}\"}}]}}";

        var path = Path.Combine(_tempDir, fileName);
        File.WriteAllText(path, json);
        return path;
    }

    private static void AssertClose(Vector3 expected, Vector3 actual, float tolerance = 1e-4f)
    {
        Assert.True(Vector3.Distance(expected, actual) <= tolerance, $"Expected {expected}, got {actual}");
    }

    private static Vector3 WorldPosition(World world, Entity entity, Vector3 local)
    {
        return world.GetComponent<TransformComponent>(entity).Transform.TransformPoint(local);
    }

    #endregion

    #region Geometry

    [Fact]
    public void Import_ReadsPositionsNormalsUvsAndIndices()
    {
        var scene = new SceneBuilder();
        scene.AddRigidMesh(Quad("quad", Material("m")), Matrix4x4.Identity);

        using var model = GltfImporter.Import(scene.ToGltf2());

        var primitive = Assert.Single(Assert.Single(model.Meshes).Primitives);
        var mesh = primitive.Mesh;

        Assert.Equal("quad", model.Meshes[0].Name);
        Assert.Equal(4, mesh.Vertices.Length);
        Assert.Equal(6, mesh.Indices.Length);
        Assert.Equal(2, model.TriangleCount);
        Assert.All(mesh.Indices, i => Assert.InRange(i, 0u, 3u));

        Assert.Equal(Vector3.Zero, mesh.BoundsMin);
        Assert.Equal(new Vector3(1, 1, 0), mesh.BoundsMax);
        Assert.Equal(new Vector3(1, 1, 0), model.BoundsMax);

        foreach (var vertex in mesh.Vertices)
        {
            AssertClose(Vector3.UnitZ, vertex.Normal);
            Assert.Equal(vertex.Position.X, vertex.TexCoord.X, 5);
            Assert.Equal(1f - vertex.Position.Y, vertex.TexCoord.Y, 5);
            Assert.Equal(Vector4.One, vertex.Color);
        }

        // Winding is preserved: every triangle faces +Z like its normals.
        for (int i = 0; i < mesh.Indices.Length; i += 3)
        {
            var a = mesh.Vertices[mesh.Indices[i]].Position;
            var b = mesh.Vertices[mesh.Indices[i + 1]].Position;
            var c = mesh.Vertices[mesh.Indices[i + 2]].Position;
            Assert.True(Vector3.Cross(b - a, c - a).Z > 0f);
        }
    }

    [Fact]
    public void Import_GeneratesTangentsFromUvs_WhenMissing()
    {
        var scene = new SceneBuilder();
        scene.AddRigidMesh(Quad("quad", Material("m")), Matrix4x4.Identity);

        using var model = GltfImporter.Import(scene.ToGltf2());

        // U grows with +X on this quad, so the tangent is +X.
        foreach (var vertex in model.Meshes[0].Primitives[0].Mesh.Vertices)
        {
            AssertClose(Vector3.UnitX, new Vector3(vertex.Tangent.X, vertex.Tangent.Y, vertex.Tangent.Z));
            Assert.Equal(1f, MathF.Abs(vertex.Tangent.W));
        }
    }

    [Fact]
    public void Import_ReadsExplicitTangents()
    {
        var mesh = new MeshBuilder<VertexPositionNormalTangent, VertexEmpty, VertexEmpty>("t");
        var tangent = new Vector4(0, 1, 0, -1);
        VertexPositionNormalTangent V(float x, float y) => new(new Vector3(x, y, 0), Vector3.UnitZ, tangent);
        mesh.UsePrimitive(Material("m")).AddTriangle(V(0, 0), V(1, 0), V(0, 1));

        var scene = new SceneBuilder();
        scene.AddRigidMesh(mesh, Matrix4x4.Identity);

        using var model = GltfImporter.Import(scene.ToGltf2());

        Assert.All(model.Meshes[0].Primitives[0].Mesh.Vertices, v => Assert.Equal(tangent, v.Tangent));
    }

    [Fact]
    public void Import_GeneratesNormals_WhenMissing()
    {
        var path = WriteRawGltf("no_normals.gltf",
            new[] { new Vector3(0, 0, 0), new Vector3(1, 0, 0), new Vector3(0, 0, -1), new Vector3(5, 5, 5) },
            new ushort[] { 0, 1, 2 });

        using var model = GltfImporter.Load(path);
        var mesh = model.Meshes[0].Primitives[0].Mesh;

        Assert.Equal(new uint[] { 0, 1, 2 }, mesh.Indices);
        for (int i = 0; i < 3; i++)
            AssertClose(Vector3.UnitY, mesh.Vertices[i].Normal);

        // The unreferenced vertex still gets a usable normal, and no tangent is NaN or parallel to its normal.
        foreach (var vertex in mesh.Vertices)
        {
            Assert.Equal(1f, vertex.Normal.Length(), 4);
            var tangent = new Vector3(vertex.Tangent.X, vertex.Tangent.Y, vertex.Tangent.Z);
            Assert.Equal(1f, tangent.Length(), 4);
            Assert.Equal(0f, Vector3.Dot(tangent, vertex.Normal), 4);
        }
    }

    [Fact]
    public void Import_LeavesDefaultNormals_WhenGenerationDisabled()
    {
        var path = WriteRawGltf("no_normals_off.gltf",
            new[] { new Vector3(0, 0, 0), new Vector3(1, 0, 0), new Vector3(0, 1, 0) },
            new ushort[] { 0, 1, 2 });

        using var model = GltfImporter.Load(path, new GltfImportOptions { GenerateMissingNormals = false });

        // The face points +Z; the untouched default is +Y.
        Assert.All(model.Meshes[0].Primitives[0].Mesh.Vertices, v => Assert.Equal(Vector3.UnitY, v.Normal));
    }

    [Fact]
    public void Import_HandlesNonIndexedAndStripPrimitives()
    {
        var corners = new[] { new Vector3(0, 0, 0), new Vector3(1, 0, 0), new Vector3(0, 1, 0), new Vector3(1, 1, 0) };

        using var nonIndexed = GltfImporter.Load(WriteRawGltf("soup.gltf", corners.Take(3).ToArray(), null));
        Assert.Equal(new uint[] { 0, 1, 2 }, nonIndexed.Meshes[0].Primitives[0].Mesh.Indices);

        using var strip = GltfImporter.Load(WriteRawGltf("strip.gltf", corners, null, mode: 5));
        var mesh = strip.Meshes[0].Primitives[0].Mesh;
        Assert.Equal(6, mesh.Indices.Length);

        // Strip expansion keeps a consistent facing for both triangles.
        Assert.All(mesh.Vertices, v => AssertClose(Vector3.UnitZ, v.Normal));
    }

    [Fact]
    public void Import_SkipsNonTrianglePrimitives_WithWarning()
    {
        var path = WriteRawGltf("points.gltf",
            new[] { new Vector3(0, 0, 0), new Vector3(1, 0, 0), new Vector3(0, 1, 0) }, null, mode: 0);

        using var model = GltfImporter.Load(path);

        Assert.Empty(Assert.Single(model.Meshes).Primitives);
        Assert.Contains(model.Warnings, w => w.Contains("POINTS"));
        Assert.Equal(0, model.TriangleCount);
    }

    [Fact]
    public void Import_SplitsMeshIntoOnePrimitivePerMaterial()
    {
        var red = Material("red").WithBaseColor(new Vector4(1, 0, 0, 1));
        var blue = Material("blue").WithBaseColor(new Vector4(0, 0, 1, 1));

        var mesh = Quad("two", red);
        AddQuad(mesh, blue, 2f);

        var other = Quad("other", blue);

        var scene = new SceneBuilder();
        scene.AddRigidMesh(mesh, Matrix4x4.Identity);
        scene.AddRigidMesh(other, Matrix4x4.CreateTranslation(10, 0, 0));

        using var model = GltfImporter.Import(scene.ToGltf2());

        Assert.Equal(2, model.Meshes.Count);
        Assert.Equal(2, model.Materials.Count);

        var two = model.Meshes.Single(m => m.Name == "two");
        Assert.Equal(2, two.Primitives.Count);
        Assert.NotEqual(two.Primitives[0].MaterialIndex, two.Primitives[1].MaterialIndex);
        Assert.NotEqual(two.Primitives[0].ResourceId, two.Primitives[1].ResourceId);

        foreach (var primitive in two.Primitives)
        {
            var material = model.Materials[primitive.MaterialIndex];
            float expectedZ = material.Name == "blue" ? 2f : 0f;
            Assert.All(primitive.Mesh.Vertices, v => Assert.Equal(expectedZ, v.Position.Z));
        }

        Assert.Equal(6, model.TriangleCount);
        Assert.Equal(new Vector3(11, 1, 2), model.BoundsMax);
    }

    #endregion

    #region Transforms

    private static SceneBuilder BuildHierarchyScene(out Matrix4x4 expectedChildWorld)
    {
        var parent = new NodeBuilder("parent");
        parent.LocalTransform = Matrix4x4.CreateScale(2f) *
                                Matrix4x4.CreateFromQuaternion(Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI / 2f)) *
                                Matrix4x4.CreateTranslation(10, 0, 0);

        var child = parent.CreateNode("child");
        child.LocalTransform = Matrix4x4.CreateTranslation(1, 2, 3);

        expectedChildWorld = child.LocalMatrix * parent.LocalMatrix;

        var scene = new SceneBuilder();
        scene.AddRigidMesh(Quad("quad", Material("m")), child);
        return scene;
    }

    [Fact]
    public void Import_PreservesNodeHierarchyAndTransforms()
    {
        var scene = BuildHierarchyScene(out var expectedChildWorld);

        using var model = GltfImporter.Import(scene.ToGltf2());

        Assert.Equal(2, model.Nodes.Count);
        var parentIndex = Assert.Single(model.RootNodes);
        var parent = model.Nodes[parentIndex];
        var child = model.Nodes[Assert.Single(parent.Children)];

        Assert.Equal("parent", parent.Name);
        Assert.Equal("child", child.Name);
        Assert.Equal(parentIndex, child.ParentIndex);
        Assert.Equal(-1, parent.MeshIndex);
        Assert.Equal(0, child.MeshIndex);

        AssertClose(new Vector3(10, 0, 0), parent.LocalPosition);
        AssertClose(new Vector3(2f), parent.LocalScale);
        AssertClose(new Vector3(1, 2, 3), child.LocalPosition);
        AssertClose(Vector3.One, child.LocalScale);

        // Local (1,2,3) scaled by 2 and rotated 90 degrees about Y, then moved by (10,0,0).
        AssertClose(new Vector3(16, 4, -2), child.WorldMatrix.Translation);
        AssertClose(Vector3.Transform(new Vector3(1, 1, 0), expectedChildWorld),
            Vector3.Transform(new Vector3(1, 1, 0), child.WorldMatrix));

        // Model bounds are measured with node transforms applied.
        AssertClose(new Vector3(16, 4, -4), model.BoundsMin, 1e-3f);
        AssertClose(new Vector3(16, 6, -2), model.BoundsMax, 1e-3f);
    }

    [Fact]
    public void Import_ReadsMatrixNodes()
    {
        // Column-major glTF matrix: scale (1,2,3) with translation (4,5,6).
        var path = WriteRawGltf("matrix.gltf",
            new[] { new Vector3(0, 0, 0), new Vector3(1, 0, 0), new Vector3(0, 1, 0) },
            new ushort[] { 0, 1, 2 },
            nodeJson: "{\"mesh\":0,\"matrix\":[1,0,0,0, 0,2,0,0, 0,0,3,0, 4,5,6,1]}");

        using var model = GltfImporter.Load(path);
        var node = Assert.Single(model.Nodes);

        AssertClose(new Vector3(4, 5, 6), node.LocalPosition);
        AssertClose(new Vector3(1, 2, 3), node.LocalScale);
        AssertClose(new Vector3(5, 7, 9), Vector3.Transform(Vector3.One, node.WorldMatrix));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Instantiate_PlacesMeshEntitiesAtNodeWorldTransforms(bool flatten)
    {
        var scene = BuildHierarchyScene(out var childWorld);
        using var model = GltfImporter.Import(scene.ToGltf2());
        using var world = new World();
        using var resources = new ResourceManager();

        var options = new ModelInstantiateOptions
        {
            Position = new Vector3(0, 100, 0),
            Scale = new Vector3(0.5f),
            FlattenHierarchy = flatten
        };
        var instance = ModelInstantiator.Instantiate(world, resources, model, options);

        var meshEntity = Assert.Single(instance.MeshEntities);
        Assert.True(world.HasComponent<MeshComponent>(meshEntity));
        Assert.True(world.HasComponent<MaterialComponent>(meshEntity));
        Assert.True(world.HasComponent<RaytracerMeshComponent>(meshEntity));

        var local = new Vector3(1, 1, 0);
        var expected = Vector3.Transform(local, childWorld) * 0.5f + new Vector3(0, 100, 0);
        AssertClose(expected, WorldPosition(world, meshEntity, local), 1e-3f);

        if (flatten)
        {
            Assert.True(instance.Root.IsNull);
            Assert.Equal(instance.MeshEntities, instance.Entities);
            Assert.Null(world.GetComponent<TransformComponent>(meshEntity).Transform.Parent);
        }
        else
        {
            // Root + parent node + child node.
            Assert.Equal(3, instance.Entities.Count);
            Assert.Equal(instance.Entities[0], instance.Root);

            // Moving the root carries the whole hierarchy.
            world.GetComponent<TransformComponent>(instance.Root).Position = new Vector3(0, 200, 0);
            AssertClose(expected + new Vector3(0, 100, 0), WorldPosition(world, meshEntity, local), 1e-3f);
        }

        int before = world.EntityCount;
        int created = instance.Entities.Count;
        instance.Destroy(world);
        Assert.Equal(before - created, world.EntityCount);
        Assert.False(world.IsAlive(meshEntity));
    }

    #endregion

    #region Materials

    [Fact]
    public void Import_ConvertsMetallicRoughnessFactors()
    {
        var source = Material("painted")
            .WithBaseColor(new Vector4(0.8f, 0.4f, 0.2f, 0.5f))
            .WithMetallicRoughness(0.25f, 0.75f)
            .WithEmissive(new Vector3(0.1f, 0.2f, 0.3f), 4f)
            .WithAlpha(SharpGLTF.Materials.AlphaMode.MASK, 0.3f)
            .WithDoubleSide(true);

        var scene = new SceneBuilder();
        scene.AddRigidMesh(Quad("quad", source), Matrix4x4.Identity);

        using var model = GltfImporter.Import(scene.ToGltf2());
        var material = Assert.Single(model.Materials);

        Assert.Equal("painted", material.Name);
        Assert.Equal(new Vector4(0.8f, 0.4f, 0.2f, 0.5f), material.BaseColorFactor);
        Assert.Equal(0.25f, material.MetallicFactor, 5);
        Assert.Equal(0.75f, material.RoughnessFactor, 5);
        AssertClose(new Vector3(0.1f, 0.2f, 0.3f), material.EmissiveFactor);
        Assert.Equal(4f, material.EmissiveIntensity, 5);
        Assert.Equal(EngineAlphaMode.Mask, material.AlphaMode);
        Assert.Equal(0.3f, material.AlphaCutoff, 5);
        Assert.True(material.DoubleSided);

        Assert.Null(material.BaseColorTexturePath);
        Assert.Null(material.MetallicRoughnessTexturePath);
        Assert.Null(material.NormalTexturePath);
        Assert.Null(material.OcclusionTexturePath);
        Assert.Null(material.EmissiveTexturePath);

        Assert.Equal(0, model.Meshes[0].Primitives[0].MaterialIndex);
    }

    [Fact]
    public void Import_UsesGltfDefaults_ForUnsetFactors()
    {
        var scene = new SceneBuilder();
        scene.AddRigidMesh(Quad("quad", Material("plain")), Matrix4x4.Identity);

        using var model = GltfImporter.Import(scene.ToGltf2());
        var material = Assert.Single(model.Materials);

        // glTF defaults: white, fully metallic, fully rough, opaque, single sided, no emission.
        Assert.Equal(Vector4.One, material.BaseColorFactor);
        Assert.Equal(1f, material.MetallicFactor);
        Assert.Equal(1f, material.RoughnessFactor);
        Assert.Equal(Vector3.Zero, material.EmissiveFactor);
        Assert.Equal(1f, material.EmissiveIntensity);
        Assert.Equal(1f, material.NormalScale);
        Assert.Equal(1f, material.OcclusionStrength);
        Assert.Equal(EngineAlphaMode.Opaque, material.AlphaMode);
        Assert.False(material.DoubleSided);
        Assert.Equal(1.5f, material.IndexOfRefraction);
    }

    [Fact]
    public void Import_ExtractsEmbeddedTextures_FromGlb()
    {
        var baseColor = CreatePng(255, 0, 0);
        var normal = CreatePng(128, 128, 255);

        var source = Material("textured")
            .WithChannelImage(KnownChannel.BaseColor, ImageBuilder.From(baseColor))
            .WithChannelImage(KnownChannel.Normal, ImageBuilder.From(normal))
            .WithChannelParam(KnownChannel.Normal, KnownProperty.NormalScale, 0.5f);

        var scene = new SceneBuilder();
        scene.AddRigidMesh(Quad("quad", source), Matrix4x4.Identity);

        var glbPath = Path.Combine(_tempDir, "textured.glb");
        scene.ToGltf2().SaveGLB(glbPath);

        var textureDir = Path.Combine(_tempDir, "extracted");
        using var model = GltfImporter.Load(glbPath, new GltfImportOptions { EmbeddedTextureDirectory = textureDir });
        var material = Assert.Single(model.Materials);

        Assert.NotNull(material.BaseColorTexturePath);
        Assert.NotNull(material.NormalTexturePath);
        Assert.NotEqual(material.BaseColorTexturePath, material.NormalTexturePath);
        Assert.Equal(0.5f, material.NormalScale, 5);
        Assert.Null(material.MetallicRoughnessTexturePath);

        Assert.StartsWith(textureDir, material.BaseColorTexturePath);
        Assert.Equal(".png", Path.GetExtension(material.BaseColorTexturePath));
        Assert.Equal(baseColor, File.ReadAllBytes(material.BaseColorTexturePath!));
        Assert.Equal(normal, File.ReadAllBytes(material.NormalTexturePath!));

        // The extracted file works with the engine's texture pipeline.
        using var textures = new TextureLibrary();
        var texture = textures.LoadTexture(material.BaseColorTexturePath!, PbrTextureType.BaseColor);
        Assert.NotNull(texture);
        Assert.Equal(2, texture!.Width);
        Assert.Equal(new byte[] { 255, 0, 0, 255 }, texture.PixelData.Take(4).ToArray());

        // Importing again reuses the same files.
        using var again = GltfImporter.Load(glbPath, new GltfImportOptions { EmbeddedTextureDirectory = textureDir });
        Assert.Equal(material.BaseColorTexturePath, again.Materials[0].BaseColorTexturePath);
        Assert.Equal(2, Directory.GetFiles(textureDir).Length);
    }

    [Fact]
    public void Import_DropsEmbeddedTextures_WhenExtractionDisabled()
    {
        var source = Material("textured").WithChannelImage(KnownChannel.BaseColor, ImageBuilder.From(CreatePng(0, 255, 0)));
        var scene = new SceneBuilder();
        scene.AddRigidMesh(Quad("quad", source), Matrix4x4.Identity);

        using var model = GltfImporter.Import(scene.ToGltf2(), null, new GltfImportOptions { ExtractEmbeddedTextures = false });

        Assert.Null(model.Materials[0].BaseColorTexturePath);
        Assert.Contains(model.Warnings, w => w.Contains("Embedded texture"));
    }

    [Fact]
    public void Import_ResolvesExternalTextureFiles_NextToTheGltf()
    {
        var png = CreatePng(0, 0, 255);
        var source = Material("textured").WithChannelImage(KnownChannel.BaseColor, ImageBuilder.From(png));
        var scene = new SceneBuilder();
        scene.AddRigidMesh(Quad("quad", source), Matrix4x4.Identity);

        var modelDir = Path.Combine(_tempDir, "external");
        Directory.CreateDirectory(modelDir);
        var gltfPath = Path.Combine(modelDir, "model.gltf");
        scene.ToGltf2().SaveGLTF(gltfPath);

        // SaveGLTF writes the image as a separate file referenced by URI.
        Assert.Contains(Directory.GetFiles(modelDir), f => f.EndsWith(".png"));

        var unusedDir = Path.Combine(_tempDir, "unused");
        using var model = GltfImporter.Load(gltfPath, new GltfImportOptions { EmbeddedTextureDirectory = unusedDir });

        var texturePath = model.Materials[0].BaseColorTexturePath;
        Assert.NotNull(texturePath);
        Assert.True(Path.IsPathRooted(texturePath));
        Assert.Equal(Path.GetFullPath(modelDir), Path.GetDirectoryName(texturePath));
        Assert.Equal(png, File.ReadAllBytes(texturePath!));
        Assert.False(Directory.Exists(unusedDir));
    }

    [Fact]
    public void Instantiate_RegistersMaterialsInLibrary_Once()
    {
        var red = Material("red").WithBaseColor(new Vector4(1, 0, 0, 1)).WithMetallicRoughness(0f, 0.6f);
        var glow = Material("glow").WithEmissive(new Vector3(1, 0.5f, 0), 2f);
        var mesh = Quad("two", red);
        AddQuad(mesh, glow, 1f);

        var scene = new SceneBuilder();
        scene.AddRigidMesh(mesh, Matrix4x4.Identity);

        using var model = GltfImporter.Import(scene.ToGltf2());
        using var world = new World();
        using var resources = new ResourceManager();
        var library = new MaterialLibrary();
        int baseline = library.Count;

        var options = new ModelInstantiateOptions { MaterialLibrary = library, AddRaytracerComponent = false };
        var first = ModelInstantiator.Instantiate(world, resources, model, options);
        var second = ModelInstantiator.Instantiate(world, resources, model, options);

        Assert.Equal(baseline + 2, library.Count);
        Assert.Equal(2, first.MeshEntities.Count);

        for (int i = 0; i < 2; i++)
        {
            var component = world.GetComponent<MaterialComponent>(first.MeshEntities[i]);
            Assert.True(component.UsesPbrMaterial);
            Assert.Same(model.Materials[model.Meshes[0].Primitives[i].MaterialIndex], library.GetMaterial(component.PbrMaterialId));
            Assert.Equal(component.PbrMaterialId, world.GetComponent<MaterialComponent>(second.MeshEntities[i]).PbrMaterialId);
            Assert.False(world.HasComponent<RaytracerMeshComponent>(first.MeshEntities[i]));
        }

        // The GPU material carries the imported factors.
        var redId = library.GetMaterialId("red");
        var gpu = library.GetGpuMaterials()[redId];
        Assert.Equal(new Vector4(1, 0, 0, 1), gpu.BaseColorFactor);
        Assert.Equal(0.6f, gpu.RoughnessFactor, 5);

        // A cleared library gets the materials again instead of stale IDs.
        library.Clear();
        var third = ModelInstantiator.Instantiate(world, resources, model, options);
        var id = world.GetComponent<MaterialComponent>(third.MeshEntities[0]).PbrMaterialId;
        Assert.NotNull(library.GetMaterial(id));
        Assert.Contains(library.GetMaterial(id), model.Materials);
    }

    [Fact]
    public void Instantiate_UsesInlineMaterialFactors_WithoutLibrary()
    {
        var glow = Material("glow")
            .WithBaseColor(new Vector4(0.2f, 0.4f, 0.6f, 1f))
            .WithMetallicRoughness(0.1f, 0.9f)
            .WithEmissive(new Vector3(1, 0.5f, 0), 2f);

        var scene = new SceneBuilder();
        scene.AddRigidMesh(Quad("quad", glow), Matrix4x4.Identity);

        using var model = GltfImporter.Import(scene.ToGltf2());
        using var world = new World();
        using var resources = new ResourceManager();

        var instance = ModelInstantiator.Instantiate(world, resources, model);
        var component = world.GetComponent<MaterialComponent>(instance.MeshEntities[0]);

        Assert.False(component.UsesPbrMaterial);
        Assert.Equal(new Vector4(0.2f, 0.4f, 0.6f, 1f), component.BaseColor);
        Assert.Equal(0.1f, component.Metallic, 5);
        Assert.Equal(0.9f, component.Roughness, 5);
        AssertClose(new Vector3(2, 1, 0), component.EmissiveColor);
    }

    #endregion

    #region Resource manager

    [Fact]
    public void ResourceManager_LoadsModelsByPath_AndCachesThem()
    {
        var scene = new SceneBuilder();
        scene.AddRigidMesh(Quad("quad", Material("m")), Matrix4x4.CreateTranslation(1, 2, 3));
        Directory.CreateDirectory(Path.Combine(_tempDir, "Models"));
        scene.ToGltf2().SaveGLB(Path.Combine(_tempDir, "Models", "quad.glb"));
        scene.ToGltf2().SaveGLTF(Path.Combine(_tempDir, "Models", "quad.gltf"));

        using var resources = new ResourceManager { BasePath = _tempDir };
        var relative = Path.Combine("Models", "quad.glb");

        var model = resources.LoadModel(relative);
        Assert.NotNull(model);
        Assert.Equal("quad", model!.Name);
        Assert.Equal(Path.Combine(_tempDir, relative), model.SourcePath);
        Assert.Same(model, resources.LoadModel(relative));
        Assert.Same(model, resources.GetHandle<Model>(relative).Get());
        Assert.True(resources.IsLoaded<Model>(relative));

        // The .gltf + .bin form of the same scene imports identically.
        var gltf = resources.Load<Model>(Path.Combine("Models", "quad.gltf"));
        Assert.NotNull(gltf);
        Assert.Equal(model.TriangleCount, gltf!.TriangleCount);
        Assert.Equal(model.BoundsMin, gltf.BoundsMin);
        Assert.Equal(new Vector3(1, 2, 3), gltf.BoundsMin);

        Assert.Null(resources.LoadModel("Models/missing.glb"));
    }

    [Fact]
    public void ModelLoader_RejectsUnsupportedAndCorruptFiles()
    {
        var obj = Path.Combine(_tempDir, "model.obj");
        var corrupt = Path.Combine(_tempDir, "corrupt.glb");
        File.WriteAllText(obj, "v 0 0 0");
        File.WriteAllText(corrupt, "not a glb");

        var loader = new ModelLoader();
        Assert.Null(loader.Load(obj));
        Assert.Null(loader.Load(corrupt));

        Assert.True(ModelLoader.IsSupported("a/b/Ship.GLB"));
        Assert.True(ModelLoader.IsSupported("ship.gltf"));
        Assert.False(ModelLoader.IsSupported("ship.fbx"));

        Assert.ThrowsAny<Exception>(() => GltfImporter.Load(corrupt));
    }

    [Fact]
    public void Instantiate_ByPath_SharesMeshResourcesBetweenInstances()
    {
        var scene = new SceneBuilder();
        var quad = Quad("quad", Material("m"));
        scene.AddRigidMesh(quad, Matrix4x4.Identity);
        scene.AddRigidMesh(quad, Matrix4x4.CreateTranslation(5, 0, 0));
        scene.ToGltf2().SaveGLB(Path.Combine(_tempDir, "pair.glb"));

        using var world = new World();
        using var resources = new ResourceManager { BasePath = _tempDir };

        var first = ModelInstantiator.Instantiate(world, resources, "pair.glb");
        var second = ModelInstantiator.Instantiate(world, resources, "pair.glb",
            new ModelInstantiateOptions { Position = new Vector3(0, 10, 0) });

        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.Equal(2, first!.MeshEntities.Count);

        // One glTF mesh used by two nodes and two instances is a single engine mesh resource.
        var handles = first.MeshEntities.Concat(second!.MeshEntities)
            .Select(e => world.GetComponent<MeshComponent>(e).MeshHandle)
            .ToList();
        Assert.Single(handles.Select(h => h.Id).Distinct());
        Assert.Single(resources.GetLoadedIds<EngineMesh>());

        var mesh = handles[0].Get();
        Assert.NotNull(mesh);
        Assert.Same(resources.LoadModel("pair.glb")!.Meshes[0].Primitives[0].Mesh, mesh);
        Assert.Equal(6, mesh!.Indices.Length);

        var positions = first.MeshEntities.Select(e => WorldPosition(world, e, Vector3.Zero)).OrderBy(p => p.X).ToList();
        AssertClose(Vector3.Zero, positions[0]);
        AssertClose(new Vector3(5, 0, 0), positions[1]);
        Assert.Contains(second.MeshEntities, e => Vector3.Distance(WorldPosition(world, e, Vector3.Zero), new Vector3(5, 10, 0)) < 1e-4f);

        Assert.Null(ModelInstantiator.Instantiate(world, resources, "missing.glb"));
    }

    #endregion
}
