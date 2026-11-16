using System.Numerics;
using RedHoleEngine.Rendering.PBR;

namespace RedHoleEngine.Resources.ModelImport;

/// <summary>
/// One drawable part of a <see cref="ModelMesh"/>: geometry plus the material it uses.
/// </summary>
public class ModelPrimitive
{
    /// <summary>
    /// Geometry for this primitive, in the local space of the node that references it
    /// </summary>
    public Mesh Mesh { get; }

    /// <summary>
    /// Index into <see cref="Model.Materials"/>, or -1 when the primitive has no material
    /// </summary>
    public int MaterialIndex { get; }

    /// <summary>
    /// ID under which <see cref="Mesh"/> is registered in a <see cref="ResourceManager"/>
    /// </summary>
    public string ResourceId { get; }

    public ModelPrimitive(Mesh mesh, int materialIndex, string resourceId)
    {
        Mesh = mesh;
        MaterialIndex = materialIndex;
        ResourceId = resourceId;
    }
}

/// <summary>
/// A named group of primitives, shared by every node that references it.
/// </summary>
public class ModelMesh
{
    public string Name { get; }

    public IReadOnlyList<ModelPrimitive> Primitives { get; }

    public ModelMesh(string name, IReadOnlyList<ModelPrimitive> primitives)
    {
        Name = name;
        Primitives = primitives;
    }
}

/// <summary>
/// A node of the imported scene hierarchy.
/// </summary>
public class ModelNode
{
    public string Name { get; init; } = "Node";

    /// <summary>
    /// Index of the parent in <see cref="Model.Nodes"/>, or -1 for a root node.
    /// Parents always precede their children in the list.
    /// </summary>
    public int ParentIndex { get; init; } = -1;

    /// <summary>
    /// Index into <see cref="Model.Meshes"/>, or -1 when the node has no mesh
    /// </summary>
    public int MeshIndex { get; init; } = -1;

    public Vector3 LocalPosition { get; init; }

    public Quaternion LocalRotation { get; init; } = Quaternion.Identity;

    public Vector3 LocalScale { get; init; } = Vector3.One;

    /// <summary>
    /// Transform relative to the parent node
    /// </summary>
    public Matrix4x4 LocalMatrix { get; init; } = Matrix4x4.Identity;

    /// <summary>
    /// Transform relative to the model root (all ancestors applied)
    /// </summary>
    public Matrix4x4 WorldMatrix { get; init; } = Matrix4x4.Identity;

    public List<int> Children { get; } = new();
}

/// <summary>
/// An imported 3D model: meshes, PBR materials and the node hierarchy that places them.
/// </summary>
public class Model : IDisposable
{
    public string Name { get; set; }

    /// <summary>
    /// Path the model was loaded from, or empty when it was built in memory
    /// </summary>
    public string SourcePath { get; }

    public IReadOnlyList<ModelMesh> Meshes { get; }

    public IReadOnlyList<PbrMaterial> Materials { get; }

    public IReadOnlyList<ModelNode> Nodes { get; }

    /// <summary>
    /// Indices into <see cref="Nodes"/> of the nodes without a parent
    /// </summary>
    public IReadOnlyList<int> RootNodes { get; }

    /// <summary>
    /// Non-fatal problems found during import (skipped primitives, approximated transforms, ...)
    /// </summary>
    public IReadOnlyList<string> Warnings { get; }

    /// <summary>
    /// Minimum corner of the bounding box of all placed geometry, in model space
    /// </summary>
    public Vector3 BoundsMin { get; }

    /// <summary>
    /// Maximum corner of the bounding box of all placed geometry, in model space
    /// </summary>
    public Vector3 BoundsMax { get; }

    public Model(
        string name,
        string sourcePath,
        IReadOnlyList<ModelMesh> meshes,
        IReadOnlyList<PbrMaterial> materials,
        IReadOnlyList<ModelNode> nodes,
        IReadOnlyList<string>? warnings = null)
    {
        Name = name;
        SourcePath = sourcePath;
        Meshes = meshes;
        Materials = materials;
        Nodes = nodes;
        Warnings = warnings ?? Array.Empty<string>();

        var roots = new List<int>();
        for (int i = 0; i < nodes.Count; i++)
        {
            if (nodes[i].ParentIndex < 0)
                roots.Add(i);
        }
        RootNodes = roots;

        (BoundsMin, BoundsMax) = CalculateBounds();
    }

    /// <summary>
    /// Total triangle count over every node that references a mesh
    /// </summary>
    public int TriangleCount
    {
        get
        {
            int count = 0;
            foreach (var node in Nodes)
            {
                if (node.MeshIndex < 0) continue;
                foreach (var primitive in Meshes[node.MeshIndex].Primitives)
                    count += primitive.Mesh.Indices.Length / 3;
            }
            return count;
        }
    }

    private (Vector3 Min, Vector3 Max) CalculateBounds()
    {
        var min = new Vector3(float.MaxValue);
        var max = new Vector3(float.MinValue);
        bool any = false;

        foreach (var node in Nodes)
        {
            if (node.MeshIndex < 0) continue;

            foreach (var primitive in Meshes[node.MeshIndex].Primitives)
            {
                foreach (var vertex in primitive.Mesh.Vertices)
                {
                    var p = Vector3.Transform(vertex.Position, node.WorldMatrix);
                    min = Vector3.Min(min, p);
                    max = Vector3.Max(max, p);
                    any = true;
                }
            }
        }

        return any ? (min, max) : (Vector3.Zero, Vector3.Zero);
    }

    public void Dispose()
    {
        foreach (var mesh in Meshes)
        {
            foreach (var primitive in mesh.Primitives)
                primitive.Mesh.Dispose();
        }
    }
}
