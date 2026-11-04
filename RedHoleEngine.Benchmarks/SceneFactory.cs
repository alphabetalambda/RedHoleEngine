using System.Numerics;
using RedHoleEngine.Physics;
using RedHoleEngine.Physics.Collision;
using RedHoleEngine.Rendering.Raytracing;

namespace RedHoleEngine.Benchmarks;

internal static class SceneFactory
{
    public static RaytracerTriangle[] Clusters(int clusters, int perCluster, float separation, int seed)
    {
        var rng = new Random(seed);
        var tris = new RaytracerTriangle[clusters * perCluster];
        int n = 0;
        for (int c = 0; c < clusters; c++)
        {
            var origin = new Vector3(c * separation, (c % 2) * 4f, 0f);
            for (int i = 0; i < perCluster; i++)
            {
                var a = origin + new Vector3(Next(rng), Next(rng), Next(rng));
                tris[n++] = new RaytracerTriangle
                {
                    V0 = a,
                    V1 = a + new Vector3(Next(rng, 0.05f, 0.25f), Next(rng, 0f, 0.2f), 0.02f),
                    V2 = a + new Vector3(0.02f, Next(rng, 0.05f, 0.25f), Next(rng, 0.05f, 0.2f))
                };
            }
        }
        return tris;
    }

    public static void Rays(RaytracerTriangle[] triangles, int count, int seed, out Vector3[] origins, out Vector3[] directions)
    {
        var rng = new Random(seed);
        origins = new Vector3[count];
        directions = new Vector3[count];
        for (int i = 0; i < count; i++)
        {
            var tri = triangles[rng.Next(triangles.Length)];
            var centroid = (tri.V0 + tri.V1 + tri.V2) / 3f;
            origins[i] = centroid + new Vector3(0f, 8f, 3f);
            directions[i] = Vector3.Normalize(centroid - origins[i]);
        }
    }

    public static BroadphaseProxy[] SparseProxies(int count, int seed)
    {
        var rng = new Random(seed);
        var proxies = new BroadphaseProxy[count];
        for (int i = 0; i < count; i++)
        {
            float x = Next(rng, -200f, 200f);
            float y = Next(rng, 0f, 40f);
            float z = Next(rng, -200f, 200f);
            proxies[i] = new BroadphaseProxy(
                i,
                new AABB(new Vector3(x, y, z), new Vector3(x + 0.8f, y + 0.8f, z + 0.8f)),
                i,
                true,
                true,
                1,
                1);
        }
        return proxies;
    }

    public static AABB LegacyBoxAabb(BoxCollider box, Vector3 position, Quaternion rotation)
    {
        var center = position + Vector3.Transform(box.Offset, rotation);
        var he = box.HalfExtents;
        var corners = new Vector3[8];
        corners[0] = new Vector3(-he.X, -he.Y, -he.Z);
        corners[1] = new Vector3(+he.X, -he.Y, -he.Z);
        corners[2] = new Vector3(-he.X, +he.Y, -he.Z);
        corners[3] = new Vector3(+he.X, +he.Y, -he.Z);
        corners[4] = new Vector3(-he.X, -he.Y, +he.Z);
        corners[5] = new Vector3(+he.X, -he.Y, +he.Z);
        corners[6] = new Vector3(-he.X, +he.Y, +he.Z);
        corners[7] = new Vector3(+he.X, +he.Y, +he.Z);

        var min = new Vector3(float.MaxValue);
        var max = new Vector3(float.MinValue);
        foreach (var corner in corners)
        {
            var world = center + Vector3.Transform(corner, rotation);
            min = Vector3.Min(min, world);
            max = Vector3.Max(max, world);
        }
        return new AABB(min, max);
    }

    private static float Next(Random rng) => (float)rng.NextDouble();

    private static float Next(Random rng, float min, float max)
        => min + (float)rng.NextDouble() * (max - min);
}
