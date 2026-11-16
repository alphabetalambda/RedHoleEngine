using System.Numerics;
using System.Security.Cryptography;
using RedHoleEngine.Rendering.PBR;
using Gltf = SharpGLTF.Schema2;

namespace RedHoleEngine.Resources.ModelImport;

/// <summary>
/// Options controlling glTF import.
/// </summary>
public class GltfImportOptions
{
    /// <summary>
    /// Compute smooth vertex normals for primitives that have none
    /// </summary>
    public bool GenerateMissingNormals { get; set; } = true;

    /// <summary>
    /// Compute tangents for primitives that have none
    /// </summary>
    public bool GenerateMissingTangents { get; set; } = true;

    /// <summary>
    /// Write images embedded in the model (GLB buffers, data URIs) to disk so that materials
    /// can reference them by path like any other texture. When false, embedded images are dropped.
    /// </summary>
    public bool ExtractEmbeddedTextures { get; set; } = true;

    /// <summary>
    /// Directory for extracted embedded images. Null uses a cache folder under the system temp path.
    /// </summary>
    public string? EmbeddedTextureDirectory { get; set; }
}

/// <summary>
/// Converts glTF 2.0 (.gltf / .glb) files into engine <see cref="Model"/> resources.
/// Skins, morph targets, animations, cameras and lights are ignored.
/// </summary>
public static class GltfImporter
{
    private static int _inMemoryCounter;

    /// <summary>
    /// Load a .gltf or .glb file. Throws if the file is missing or not valid glTF.
    /// </summary>
    public static Model Load(string path, GltfImportOptions? options = null)
    {
        var settings = new Gltf.ReadSettings
        {
            Validation = SharpGLTF.Validation.ValidationMode.TryFix
        };

        var root = Gltf.ModelRoot.Load(path, settings);
        return Import(root, path, options);
    }

    /// <summary>
    /// Convert an already-parsed glTF document.
    /// </summary>
    /// <param name="root">The glTF document</param>
    /// <param name="sourcePath">File the document came from, used to resolve relative texture paths and name resources</param>
    /// <param name="options">Import options, or null for defaults</param>
    public static Model Import(Gltf.ModelRoot root, string? sourcePath = null, GltfImportOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(root);
        options ??= new GltfImportOptions();
        sourcePath ??= "";

        var name = string.IsNullOrEmpty(sourcePath) ? "Model" : Path.GetFileNameWithoutExtension(sourcePath);
        var resourcePrefix = string.IsNullOrEmpty(sourcePath)
            ? $"{name}@{Interlocked.Increment(ref _inMemoryCounter)}"
            : sourcePath;

        var context = new ImportContext(options, sourcePath);

        var materials = new List<PbrMaterial>(root.LogicalMaterials.Count);
        foreach (var material in root.LogicalMaterials)
            materials.Add(ConvertMaterial(material, context));

        var meshes = new List<ModelMesh>(root.LogicalMeshes.Count);
        foreach (var mesh in root.LogicalMeshes)
            meshes.Add(ConvertMesh(mesh, resourcePrefix, context));

        var nodes = new List<ModelNode>();
        var scene = root.DefaultScene ?? root.LogicalScenes.FirstOrDefault();
        var roots = scene != null
            ? scene.VisualChildren
            : root.LogicalNodes.Where(n => n.VisualParent == null);

        foreach (var node in roots)
            ConvertNode(node, -1, Matrix4x4.Identity, nodes, context);

        return new Model(name, sourcePath, meshes, materials, nodes, context.Warnings);
    }

    #region Nodes

    private static void ConvertNode(
        Gltf.Node node,
        int parentIndex,
        Matrix4x4 parentWorld,
        List<ModelNode> nodes,
        ImportContext context)
    {
        var local = node.LocalMatrix;
        var nodeName = string.IsNullOrEmpty(node.Name) ? $"Node_{node.LogicalIndex}" : node.Name;

        Vector3 scale;
        Quaternion rotation;
        Vector3 translation;

        var transform = node.LocalTransform;
        if (transform.IsSRT)
        {
            scale = transform.Scale;
            rotation = transform.Rotation;
            translation = transform.Translation;
        }
        else if (!Matrix4x4.Decompose(local, out scale, out rotation, out translation))
        {
            context.Warnings.Add($"Node '{nodeName}' has a matrix that cannot be split into translation/rotation/scale; using its translation only.");
            scale = Vector3.One;
            rotation = Quaternion.Identity;
            translation = local.Translation;
            local = Matrix4x4.CreateTranslation(translation);
        }

        int index = nodes.Count;
        var converted = new ModelNode
        {
            Name = nodeName,
            ParentIndex = parentIndex,
            MeshIndex = node.Mesh?.LogicalIndex ?? -1,
            LocalPosition = translation,
            LocalRotation = Quaternion.Normalize(rotation),
            LocalScale = scale,
            LocalMatrix = local,
            WorldMatrix = local * parentWorld
        };
        nodes.Add(converted);

        if (parentIndex >= 0)
            nodes[parentIndex].Children.Add(index);

        foreach (var child in node.VisualChildren)
            ConvertNode(child, index, converted.WorldMatrix, nodes, context);
    }

    #endregion

    #region Geometry

    private static ModelMesh ConvertMesh(Gltf.Mesh mesh, string resourcePrefix, ImportContext context)
    {
        var meshName = string.IsNullOrEmpty(mesh.Name) ? $"Mesh_{mesh.LogicalIndex}" : mesh.Name;
        var primitives = new List<ModelPrimitive>(mesh.Primitives.Count);

        foreach (var primitive in mesh.Primitives)
        {
            var primitiveName = mesh.Primitives.Count == 1 ? meshName : $"{meshName}_{primitive.LogicalIndex}";
            var converted = ConvertPrimitive(primitive, primitiveName, context);
            if (converted == null)
                continue;

            var resourceId = $"{resourcePrefix}#mesh{mesh.LogicalIndex}/prim{primitive.LogicalIndex}";
            primitives.Add(new ModelPrimitive(converted, primitive.Material?.LogicalIndex ?? -1, resourceId));
        }

        return new ModelMesh(meshName, primitives);
    }

    private static Mesh? ConvertPrimitive(Gltf.MeshPrimitive primitive, string name, ImportContext context)
    {
        var type = primitive.DrawPrimitiveType;
        if (type != Gltf.PrimitiveType.TRIANGLES &&
            type != Gltf.PrimitiveType.TRIANGLE_STRIP &&
            type != Gltf.PrimitiveType.TRIANGLE_FAN)
        {
            context.Warnings.Add($"Primitive '{name}' skipped: {type} primitives are not supported.");
            return null;
        }

        var positions = primitive.GetVertexAccessor("POSITION")?.AsVector3Array();
        if (positions == null || positions.Count == 0)
        {
            context.Warnings.Add($"Primitive '{name}' skipped: it has no positions.");
            return null;
        }

        int vertexCount = positions.Count;
        var normals = primitive.GetVertexAccessor("NORMAL")?.AsVector3Array();
        var tangents = primitive.GetVertexAccessor("TANGENT")?.AsVector4Array();
        var texCoords = primitive.GetVertexAccessor("TEXCOORD_0")?.AsVector2Array();
        var colors = primitive.GetVertexAccessor("COLOR_0")?.AsColorArray();

        if (normals != null && normals.Count != vertexCount) normals = null;
        if (tangents != null && tangents.Count != vertexCount) tangents = null;
        if (texCoords != null && texCoords.Count != vertexCount) texCoords = null;
        if (colors != null && colors.Count != vertexCount) colors = null;

        var vertices = new Vertex[vertexCount];
        for (int i = 0; i < vertexCount; i++)
        {
            var vertex = new Vertex(positions[i]);
            if (normals != null) vertex.Normal = SafeNormalize(normals[i], Vector3.UnitY);
            if (texCoords != null) vertex.TexCoord = texCoords[i];
            if (colors != null) vertex.Color = colors[i];
            vertices[i] = vertex;
        }

        // GetTriangleIndices expands strips and fans, and handles non-indexed primitives.
        var indices = new List<uint>();
        foreach (var (a, b, c) in primitive.GetTriangleIndices())
        {
            if ((uint)a >= vertexCount || (uint)b >= vertexCount || (uint)c >= vertexCount)
                continue;

            indices.Add((uint)a);
            indices.Add((uint)b);
            indices.Add((uint)c);
        }

        var mesh = new Mesh(vertices, indices.ToArray(), name);

        if (normals == null && context.Options.GenerateMissingNormals)
            GenerateNormals(mesh);

        if (tangents != null)
        {
            for (int i = 0; i < vertexCount; i++)
            {
                var t = tangents[i];
                var direction = SafeNormalize(new Vector3(t.X, t.Y, t.Z), Perpendicular(vertices[i].Normal));
                vertices[i].Tangent = new Vector4(direction, t.W < 0f ? -1f : 1f);
            }
        }
        else if (context.Options.GenerateMissingTangents)
        {
            GenerateTangents(mesh, hasTexCoords: texCoords != null);
        }

        return mesh;
    }

    /// <summary>
    /// Area-weighted smooth normals. Vertices not used by any non-degenerate triangle point up.
    /// </summary>
    private static void GenerateNormals(Mesh mesh)
    {
        var vertices = mesh.Vertices;
        var indices = mesh.Indices;
        var sums = new Vector3[vertices.Length];

        for (int i = 0; i + 2 < indices.Length; i += 3)
        {
            uint i0 = indices[i], i1 = indices[i + 1], i2 = indices[i + 2];
            var faceNormal = Vector3.Cross(
                vertices[i1].Position - vertices[i0].Position,
                vertices[i2].Position - vertices[i0].Position);

            sums[i0] += faceNormal;
            sums[i1] += faceNormal;
            sums[i2] += faceNormal;
        }

        for (int i = 0; i < vertices.Length; i++)
            vertices[i].Normal = SafeNormalize(sums[i], Vector3.UnitY);
    }

    private static void GenerateTangents(Mesh mesh, bool hasTexCoords)
    {
        var vertices = mesh.Vertices;

        if (hasTexCoords)
            mesh.RecalculateTangents();

        // Without UVs (or where they are degenerate) any direction in the surface plane will do.
        for (int i = 0; i < vertices.Length; i++)
        {
            var t = vertices[i].Tangent;
            bool valid = hasTexCoords &&
                float.IsFinite(t.X) && float.IsFinite(t.Y) && float.IsFinite(t.Z) &&
                new Vector3(t.X, t.Y, t.Z).LengthSquared() > 1e-8f;

            if (!valid)
                vertices[i].Tangent = new Vector4(Perpendicular(vertices[i].Normal), 1f);
        }
    }

    private static Vector3 SafeNormalize(Vector3 value, Vector3 fallback)
    {
        float lengthSquared = value.LengthSquared();
        if (!float.IsFinite(lengthSquared) || lengthSquared < 1e-20f)
            return fallback;
        return value / MathF.Sqrt(lengthSquared);
    }

    private static Vector3 Perpendicular(Vector3 normal)
    {
        var axis = MathF.Abs(normal.X) < 0.9f ? Vector3.UnitX : Vector3.UnitY;
        return SafeNormalize(axis - normal * Vector3.Dot(normal, axis), Vector3.UnitX);
    }

    #endregion

    #region Materials

    private static PbrMaterial ConvertMaterial(Gltf.Material source, ImportContext context)
    {
        var material = new PbrMaterial
        {
            Name = string.IsNullOrEmpty(source.Name) ? $"Material_{source.LogicalIndex}" : source.Name,
            DoubleSided = source.DoubleSided,
            AlphaCutoff = source.AlphaCutoff,
            AlphaMode = source.Alpha switch
            {
                Gltf.AlphaMode.MASK => AlphaMode.Mask,
                Gltf.AlphaMode.BLEND => AlphaMode.Blend,
                _ => AlphaMode.Opaque
            },
            IndexOfRefraction = source.IndexOfRefraction
        };

        var baseColor = source.FindChannel("BaseColor");
        var metallicRoughness = source.FindChannel("MetallicRoughness");

        if (baseColor != null)
        {
            material.BaseColorFactor = GetVector4(baseColor, Vector4.One, "RGBA");
            material.BaseColorTexturePath = context.ResolveTexture(baseColor.Value.Texture);
            material.MetallicFactor = GetFloat(metallicRoughness, 1f, "MetallicFactor");
            material.RoughnessFactor = GetFloat(metallicRoughness, 1f, "RoughnessFactor");
            material.MetallicRoughnessTexturePath = context.ResolveTexture(metallicRoughness?.Texture);
        }
        else if (source.FindChannel("Diffuse") is { } diffuse)
        {
            // KHR_materials_pbrSpecularGlossiness: approximate as a dielectric.
            var specularGlossiness = source.FindChannel("SpecularGlossiness");
            material.BaseColorFactor = GetVector4(diffuse, Vector4.One, "RGBA");
            material.BaseColorTexturePath = context.ResolveTexture(diffuse.Texture);
            material.MetallicFactor = 0f;
            material.RoughnessFactor = 1f - GetFloat(specularGlossiness, 1f, "GlossinessFactor");
        }

        var normal = source.FindChannel("Normal");
        material.NormalTexturePath = context.ResolveTexture(normal?.Texture);
        material.NormalScale = GetFloat(normal, 1f, "NormalScale");

        var occlusion = source.FindChannel("Occlusion");
        material.OcclusionTexturePath = context.ResolveTexture(occlusion?.Texture);
        material.OcclusionStrength = GetFloat(occlusion, 1f, "OcclusionStrength");

        var emissive = source.FindChannel("Emissive");
        if (emissive != null)
        {
            material.EmissiveFactor = GetVector3(emissive, Vector3.Zero, "RGB");
            material.EmissiveTexturePath = context.ResolveTexture(emissive.Value.Texture);
            material.EmissiveIntensity = GetFloat(emissive, 1f, "EmissiveStrength");
        }

        material.ClearcoatFactor = GetFloat(source.FindChannel("ClearCoat"), 0f, "ClearCoatFactor");
        material.ClearcoatRoughness = GetFloat(source.FindChannel("ClearCoatRoughness"), 0f, "RoughnessFactor");

        material.MetallicFactor = Math.Clamp(material.MetallicFactor, 0f, 1f);
        material.RoughnessFactor = Math.Clamp(material.RoughnessFactor, 0f, 1f);

        return material;
    }

    private static object? FindParameter(Gltf.MaterialChannel? channel, string name)
    {
        if (channel == null)
            return null;

        foreach (var parameter in channel.Value.Parameters)
        {
            if (string.Equals(parameter.Name, name, StringComparison.OrdinalIgnoreCase))
                return parameter.Value;
        }
        return null;
    }

    private static float GetFloat(Gltf.MaterialChannel? channel, float fallback, string name)
    {
        return FindParameter(channel, name) is float value ? value : fallback;
    }

    private static Vector3 GetVector3(Gltf.MaterialChannel? channel, Vector3 fallback, string name)
    {
        return FindParameter(channel, name) switch
        {
            Vector3 v => v,
            Vector4 v => new Vector3(v.X, v.Y, v.Z),
            _ => fallback
        };
    }

    private static Vector4 GetVector4(Gltf.MaterialChannel? channel, Vector4 fallback, string name)
    {
        return FindParameter(channel, name) switch
        {
            Vector4 v => v,
            Vector3 v => new Vector4(v, 1f),
            _ => fallback
        };
    }

    #endregion

    /// <summary>
    /// Per-import state: options, warnings and the image-to-path cache.
    /// </summary>
    private sealed class ImportContext
    {
        private readonly Dictionary<int, string?> _imagePaths = new();
        private readonly string _modelDirectory;

        public GltfImportOptions Options { get; }
        public List<string> Warnings { get; } = new();

        public ImportContext(GltfImportOptions options, string sourcePath)
        {
            Options = options;
            _modelDirectory = string.IsNullOrEmpty(sourcePath)
                ? ""
                : Path.GetDirectoryName(Path.GetFullPath(sourcePath)) ?? "";
        }

        /// <summary>
        /// Path a material can use to load the texture's image, or null when there is none.
        /// </summary>
        public string? ResolveTexture(Gltf.Texture? texture)
        {
            var image = texture?.PrimaryImage;
            if (image == null)
                return null;

            if (_imagePaths.TryGetValue(image.LogicalIndex, out var cached))
                return cached;

            var path = ResolveImage(image);
            _imagePaths[image.LogicalIndex] = path;
            return path;
        }

        private string? ResolveImage(Gltf.Image image)
        {
            var content = image.Content;

            // Images stored as separate files keep the path they were read from.
            var sourcePath = content.SourcePath;
            if (!string.IsNullOrEmpty(sourcePath))
            {
                return Path.IsPathRooted(sourcePath) || string.IsNullOrEmpty(_modelDirectory)
                    ? sourcePath
                    : Path.GetFullPath(Path.Combine(_modelDirectory, sourcePath));
            }

            var imageName = string.IsNullOrEmpty(image.Name) ? $"image {image.LogicalIndex}" : image.Name;

            if (!content.IsValid)
            {
                Warnings.Add($"Texture '{imageName}' has no readable image data.");
                return null;
            }

            if (!Options.ExtractEmbeddedTextures)
            {
                Warnings.Add($"Embedded texture '{imageName}' dropped (ExtractEmbeddedTextures is off).");
                return null;
            }

            try
            {
                var directory = Options.EmbeddedTextureDirectory
                    ?? Path.Combine(Path.GetTempPath(), "RedHoleEngine", "ModelTextures");
                Directory.CreateDirectory(directory);

                // Content-addressed, so re-importing a model reuses the files already written.
                var bytes = content.Content;
                var hash = Convert.ToHexString(SHA256.HashData(bytes.Span), 0, 8).ToLowerInvariant();
                var path = Path.GetFullPath(Path.Combine(directory, $"{hash}.{content.FileExtension}"));

                if (!File.Exists(path))
                    File.WriteAllBytes(path, bytes.ToArray());

                return path;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Warnings.Add($"Embedded texture '{imageName}' could not be extracted: {ex.Message}");
                return null;
            }
        }
    }
}
