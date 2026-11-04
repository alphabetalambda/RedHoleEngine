using System.Numerics;
using RedHoleEngine.Resources;

namespace RedHoleEngine.Tests.Resources;

public class MeshBehaviorTests
{
    [Fact]
    public void EmptyMesh_HasZeroBounds_AndPrimitivesMatchTheirSize()
    {
        var empty = new Mesh("empty");
        empty.RecalculateBounds();
        Assert.Equal(Vector3.Zero, empty.BoundsMin);
        Assert.Equal(0f, empty.BoundingSphereRadius);

        var cube = Mesh.CreateCube(2f);
        Assert.Equal(24, cube.Vertices.Length);
        Assert.Equal(36, cube.Indices.Length);
        Assert.Equal(-Vector3.One, cube.BoundsMin);
        Assert.Equal(Vector3.One, cube.BoundsMax);
        Assert.Equal(MathF.Sqrt(3f), cube.BoundingSphereRadius, 1e-4f);
        Assert.True(cube.Vertices.All(v => v.Normal.Length() > 0.9f));
        Assert.Equal(MarshalSize(), Vertex.SizeInBytes);

        var sphere = Mesh.CreateSphere(1f, segments: 8, rings: 4);
        Assert.True(sphere.Vertices.Length > 8);
        Assert.Equal(0, sphere.Indices.Length % 3);
        Assert.True(sphere.BoundingSphereRadius > 1f);

        var plane = Mesh.CreatePlane(4f, 2f, segmentsX: 2, segmentsZ: 1);
        Assert.Equal(6, plane.Vertices.Length); // (2+1)*(1+1)
        Assert.Equal(new Vector3(0, 1, 0), plane.Vertices[0].Normal);
        plane.Dispose();
        cube.Dispose();
        sphere.Dispose();
    }

    [Fact]
    public void RecalculateNormalsAndTangents_FromASingleTriangle()
    {
        var verts = new[]
        {
            new Vertex(new Vector3(0, 0, 0), Vector3.Zero, new Vector2(0, 0)),
            new Vertex(new Vector3(1, 0, 0), Vector3.Zero, new Vector2(1, 0)),
            new Vertex(new Vector3(0, 1, 0), Vector3.Zero, new Vector2(0, 1))
        };
        var mesh = new Mesh(verts, new uint[] { 0, 1, 2 }, "tri");
        mesh.RecalculateNormals();
        Assert.Equal(Vector3.UnitZ, mesh.Vertices[0].Normal);
        mesh.RecalculateTangents();
        Assert.True(mesh.Vertices[0].Tangent.X > 0.9f);

        var custom = new Vertex(Vector3.One, Vector3.UnitY, new Vector2(0.5f, 0.5f), new Vector4(1, 0, 0, 1));
        Assert.Equal(new Vector4(1, 0, 0, 1), custom.Color);
    }

    private static int MarshalSize() => System.Runtime.InteropServices.Marshal.SizeOf<Vertex>();
}
