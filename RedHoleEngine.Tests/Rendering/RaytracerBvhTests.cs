using System.Numerics;
using System.Runtime.CompilerServices;
using RedHoleEngine.Rendering.Raytracing;

namespace RedHoleEngine.Tests.Rendering;

public class RaytracerBvhTests
{
    [Fact]
    public void Build_Empty_ReturnsEmptyMesh()
    {
        var mesh = RaytracerMeshBuilder.Build(Array.Empty<RaytracerTriangle>());
        Assert.Empty(mesh.Nodes);
        Assert.Empty(mesh.Triangles);
    }

    [Fact]
    public void NodeAndTriangleLayouts_MatchShader()
    {
        Assert.Equal(32, Unsafe.SizeOf<RaytracerBvhNode>());
        Assert.Equal(144, Unsafe.SizeOf<RaytracerTriangle>());
    }

    [Fact]
    public void Build_SingleTriangle_IsOneLeaf()
    {
        var tri = Triangle(Vector3.Zero, Vector3.UnitX, Vector3.UnitY);
        var mesh = RaytracerMeshBuilder.Build(new[] { tri });

        Assert.Single(mesh.Nodes);
        Assert.Equal(1, mesh.Nodes[0].TriCount);
        Assert.Equal(0, mesh.Nodes[0].LeftFirst);
        Assert.Equal(tri.V0, mesh.Triangles[0].V0);
    }

    [Fact]
    public void Build_PreservesEveryTriangleExactlyOnce()
    {
        var source = ClusterScene(clusters: 4, perCluster: 40, separation: 25f, seed: 3);
        var mesh = RaytracerMeshBuilder.Build(source);

        Assert.Equal(source.Length, mesh.Triangles.Length);
        var expected = source.Select(Key).OrderBy(k => k).ToArray();
        var actual = mesh.Triangles.Select(Key).OrderBy(k => k).ToArray();
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void Build_BoundsContainTriangles_AndChildren_AndShaderStackCanWalk()
    {
        var source = ClusterScene(clusters: 6, perCluster: 80, separation: 40f, seed: 9);
        var mesh = RaytracerMeshBuilder.Build(source, maxLeafSize: 4);
        var nodes = mesh.Nodes;

        var seen = new bool[mesh.Triangles.Length];
        Walk(nodes, mesh.Triangles, 0, seen);

        Assert.All(seen, present => Assert.True(present));
        Assert.True(ShaderWalkReachesEveryNode(nodes));

        for (int i = 0; i < nodes.Length; i++)
        {
            var node = nodes[i];
            if (node.TriCount > 0)
            {
                Assert.InRange(node.TriCount, 1, 4);
                for (int t = 0; t < node.TriCount; t++)
                {
                    var tri = mesh.Triangles[node.LeftFirst + t];
                    AssertContains(node, tri.V0);
                    AssertContains(node, tri.V1);
                    AssertContains(node, tri.V2);
                }
            }
            else
            {
                var left = nodes[node.LeftFirst];
                var right = nodes[node.LeftFirst + 1];
                AssertContains(node, left.BoundsMin);
                AssertContains(node, left.BoundsMax);
                AssertContains(node, right.BoundsMin);
                AssertContains(node, right.BoundsMax);
            }
        }
    }

    [Fact]
    public void Build_LongChain_DoesNotExceedShaderStack()
    {
        var tris = new RaytracerTriangle[2000];
        for (int i = 0; i < tris.Length; i++)
        {
            var p = new Vector3(i * 3f, (i % 7) * 0.1f, 0f);
            tris[i] = Triangle(p, p + new Vector3(0.4f, 0.2f, 0f), p + new Vector3(0.1f, 0.5f, 0.2f));
        }

        var mesh = RaytracerMeshBuilder.Build(tris);
        Assert.True(ShaderWalkReachesEveryNode(mesh.Nodes));
    }

    [Fact]
    public void Query_MatchesBruteForceLeaf()
    {
        var source = ClusterScene(clusters: 3, perCluster: 30, separation: 15f, seed: 11);
        var sah = RaytracerMeshBuilder.Build(source, maxLeafSize: 4);
        var brute = RaytracerMeshBuilder.Build(source, maxLeafSize: source.Length);

        var rng = new Random(11);
        for (int i = 0; i < source.Length; i += 3)
        {
            var tri = source[i];
            var centroid = (tri.V0 + tri.V1 + tri.V2) / 3f;
            var normal = Vector3.Normalize(Vector3.Cross(tri.V1 - tri.V0, tri.V2 - tri.V0));
            if (float.IsNaN(normal.X))
                continue;

            var origin = centroid + normal * 3f;
            var direction = -normal;
            AssertSameHit(brute, sah, origin, direction);
        }

        for (int r = 0; r < 40; r++)
        {
            var origin = new Vector3(Next(rng, -5f, 40f), Next(rng, -5f, 20f), Next(rng, -20f, 20f));
            var direction = Vector3.Normalize(new Vector3(Next(rng, -1f, 1f), Next(rng, -1f, 1f), Next(rng, -1f, 1f)));
            AssertSameHit(brute, sah, origin, direction);
        }
    }

    [Fact]
    public void Build_DegenerateCentroids_StillContainsEveryTriangle()
    {
        var tri = Triangle(Vector3.Zero, Vector3.UnitX, Vector3.UnitY);
        var source = Enumerable.Repeat(tri, 12).ToArray();
        var mesh = RaytracerMeshBuilder.Build(source);

        Assert.Equal(12, mesh.Triangles.Length);
        Assert.True(ShaderWalkReachesEveryNode(mesh.Nodes));
        Assert.All(mesh.Nodes, node =>
        {
            if (node.TriCount > 0)
                Assert.InRange(node.TriCount, 1, 4);
        });
    }

    private static void AssertSameHit(RaytracerMeshData brute, RaytracerMeshData sah, Vector3 origin, Vector3 direction)
    {
        bool bruteHit = RaytracerBvhQuery.TryIntersect(brute, origin, direction, 1e6f, out var bruteResult);
        bool sahHit = RaytracerBvhQuery.TryIntersect(sah, origin, direction, 1e6f, out var sahResult);
        Assert.Equal(bruteHit, sahHit);
        if (!bruteHit)
            return;

        // Ties may report a different triangle. The distance is the observable result.
        Assert.True(MathF.Abs(bruteResult.T - sahResult.T) < 1e-3f,
            $"closest t {bruteResult.T} vs {sahResult.T}");
    }

    private static void Walk(RaytracerBvhNode[] nodes, RaytracerTriangle[] tris, int index, bool[] seen)
    {
        var node = nodes[index];
        if (node.TriCount > 0)
        {
            for (int i = 0; i < node.TriCount; i++)
            {
                int tri = node.LeftFirst + i;
                Assert.InRange(tri, 0, tris.Length - 1);
                Assert.False(seen[tri]);
                seen[tri] = true;
            }
            return;
        }

        int left = node.LeftFirst;
        int right = left + 1;
        Assert.InRange(left, 0, nodes.Length - 1);
        Assert.InRange(right, 0, nodes.Length - 1);
        Walk(nodes, tris, left, seen);
        Walk(nodes, tris, right, seen);
    }

    private static bool ShaderWalkReachesEveryNode(RaytracerBvhNode[] nodes)
    {
        if (nodes.Length == 0)
            return true;

        var seen = new bool[nodes.Length];
        Span<int> stack = stackalloc int[64];
        int stackPtr = 0;
        stack[stackPtr++] = 0;
        int visits = 0;

        while (stackPtr > 0)
        {
            int nodeIndex = stack[--stackPtr];
            if ((uint)nodeIndex >= (uint)nodes.Length || seen[nodeIndex])
                return false;

            seen[nodeIndex] = true;
            visits++;
            var node = nodes[nodeIndex];
            if (node.TriCount > 0)
                continue;

            int left = node.LeftFirst;
            int right = left + 1;
            if ((uint)right >= (uint)nodes.Length || stackPtr >= 63)
                return false;
            stack[stackPtr++] = right;
            if ((uint)left >= (uint)nodes.Length || stackPtr >= 63)
                return false;
            stack[stackPtr++] = left;
        }

        return visits == nodes.Length;
    }

    private static void AssertContains(RaytracerBvhNode node, Vector3 point)
    {
        const float slack = 1e-4f;
        Assert.True(point.X >= node.BoundsMin.X - slack && point.X <= node.BoundsMax.X + slack);
        Assert.True(point.Y >= node.BoundsMin.Y - slack && point.Y <= node.BoundsMax.Y + slack);
        Assert.True(point.Z >= node.BoundsMin.Z - slack && point.Z <= node.BoundsMax.Z + slack);
    }

    private static RaytracerTriangle[] ClusterScene(int clusters, int perCluster, float separation, int seed)
    {
        var rng = new Random(seed);
        var tris = new RaytracerTriangle[clusters * perCluster];
        int n = 0;
        for (int c = 0; c < clusters; c++)
        {
            var origin = new Vector3(c * separation, (c % 2) * 3f, (c % 3) * 2f);
            for (int i = 0; i < perCluster; i++)
            {
                var a = origin + new Vector3(Next(rng, 0f, 1f), Next(rng, 0f, 1f), Next(rng, 0f, 1f));
                tris[n++] = Triangle(
                    a,
                    a + new Vector3(Next(rng, 0.05f, 0.3f), Next(rng, -0.1f, 0.2f), Next(rng, -0.05f, 0.05f)),
                    a + new Vector3(Next(rng, -0.05f, 0.05f), Next(rng, 0.05f, 0.3f), Next(rng, 0.05f, 0.2f)));
            }
        }
        return tris;
    }

    private static RaytracerTriangle Triangle(Vector3 a, Vector3 b, Vector3 c)
    {
        return new RaytracerTriangle
        {
            V0 = a,
            V1 = b,
            V2 = c,
            Normal = Vector3.Normalize(Vector3.Cross(b - a, c - a))
        };
    }

    private static string Key(RaytracerTriangle tri)
    {
        static string F(Vector3 v) => $"{v.X:R},{v.Y:R},{v.Z:R}";
        var parts = new[] { F(tri.V0), F(tri.V1), F(tri.V2) };
        Array.Sort(parts, StringComparer.Ordinal);
        return string.Join("|", parts);
    }

    private static float Next(Random rng, float min, float max)
        => min + (float)rng.NextDouble() * (max - min);
}
