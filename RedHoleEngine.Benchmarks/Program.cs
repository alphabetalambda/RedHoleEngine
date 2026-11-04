using System.Diagnostics;
using System.Numerics;
using BenchmarkDotNet.Running;
using RedHoleEngine.Physics;
using RedHoleEngine.Physics.Collision;
using RedHoleEngine.Rendering.Raytracing;

namespace RedHoleEngine.Benchmarks;

public static class Program
{
    public static void Main(string[] args)
    {
        if (args.Contains("--stats"))
        {
            PerfStats.Run();
            return;
        }

        BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);
    }
}

/// <summary>
/// Single-process before/after numbers. BenchmarkDotNet is the timed source of
/// truth; this prints traversal cost, which the time table does not show.
/// </summary>
internal static class PerfStats
{
    public static void Run()
    {
        var triangles = SceneFactory.Clusters(8, 500, 40f, 2);
        var origins = new Vector3[128];
        var directions = new Vector3[128];
        SceneFactory.Rays(triangles, origins.Length, 2, out origins, out directions);

        var median = TimeBuild(() => LegacyMedianSplitBuilder.Build(triangles), out var medianMesh);
        var sah = TimeBuild(() => RaytracerMeshBuilder.Build(triangles), out var sahMesh);
        var medianCost = Cost(medianMesh, origins, directions);
        var sahCost = Cost(sahMesh, origins, directions);

        Console.WriteLine($"BVH build {triangles.Length} clustered tris: median {median.TotalMilliseconds:F2} ms, SAH {sah.TotalMilliseconds:F2} ms");
        Console.WriteLine($"BVH traverse {origins.Length} rays: median nodes {medianCost.Nodes} tris {medianCost.Tris} in {medianCost.Elapsed.TotalMilliseconds:F2} ms");
        Console.WriteLine($"BVH traverse {origins.Length} rays: SAH    nodes {sahCost.Nodes} tris {sahCost.Tris} in {sahCost.Elapsed.TotalMilliseconds:F2} ms");

        var lopsided = Lopsided(3800, 12);
        var lopOrigins = new Vector3[64];
        var lopDirs = new Vector3[64];
        SceneFactory.Rays(lopsided.AsSpan(0, 3800).ToArray(), lopOrigins.Length, 5, out lopOrigins, out lopDirs);
        var lopMedian = LegacyMedianSplitBuilder.Build(lopsided);
        var lopSah = RaytracerMeshBuilder.Build(lopsided);
        var lopMedianCost = Cost(lopMedian, lopOrigins, lopDirs);
        var lopSahCost = Cost(lopSah, lopOrigins, lopDirs);
        Console.WriteLine($"Lopsided traverse (3800 packed + 12 outliers), {lopOrigins.Length} rays into the pack:");
        Console.WriteLine($"  median nodes {lopMedianCost.Nodes} tris {lopMedianCost.Tris} in {lopMedianCost.Elapsed.TotalMilliseconds:F2} ms");
        Console.WriteLine($"  SAH    nodes {lopSahCost.Nodes} tris {lopSahCost.Tris} in {lopSahCost.Elapsed.TotalMilliseconds:F2} ms");

        var proxies = SceneFactory.SparseProxies(3000, 7);
        var pairs = new List<CollisionPair>();
        var sweep = new SweepAndPruneBroadphase();
        var bruteTime = Time(() => SweepAndPruneBroadphase.CollectPairsBruteForce(proxies, pairs));
        int bruteCount = pairs.Count;
        var sweepTime = Time(() => sweep.CollectPairsSweep(proxies, pairs));
        int sweepCount = pairs.Count;
        Console.WriteLine($"Broadphase {proxies.Length} sparse: brute {bruteTime.TotalMilliseconds:F2} ms ({bruteCount} pairs), sweep {sweepTime.TotalMilliseconds:F2} ms ({sweepCount} pairs)");

        const int boxes = 160;
        var rng = new Random(4);
        var colliders = new BoxCollider[boxes];
        var positions = new Vector3[boxes];
        var rotations = new Quaternion[boxes];
        for (int i = 0; i < boxes; i++)
        {
            colliders[i] = new BoxCollider(new Vector3(0.4f, 0.3f, 0.5f));
            positions[i] = new Vector3(i % 16, (i / 16) % 4, i / 64) * 3f;
            rotations[i] = Quaternion.CreateFromYawPitchRoll((float)rng.NextDouble(), 0.2f, 0.1f);
        }

        var legacyBoxes = Time(() =>
        {
            int hits = 0;
            for (int i = 0; i < boxes; i++)
            {
                var a = SceneFactory.LegacyBoxAabb(colliders[i], positions[i], rotations[i]);
                for (int j = i + 1; j < boxes; j++)
                {
                    var b = SceneFactory.LegacyBoxAabb(colliders[j], positions[j], rotations[j]);
                    if (a.Intersects(b)) hits++;
                }
            }
            return hits;
        });
        var cached = Time(() =>
        {
            var built = new BroadphaseProxy[boxes];
            for (int i = 0; i < boxes; i++)
            {
                built[i] = new BroadphaseProxy(i, colliders[i].GetWorldAABB(positions[i], rotations[i]), i, true, true, 1, 1);
            }
            sweep.CollectPairs(built, pairs);
            return pairs.Count;
        });
        Console.WriteLine($"Box broadphase {boxes}: legacy corner AABBs {legacyBoxes.TotalMilliseconds:F2} ms, cached AABB + sweep {cached.TotalMilliseconds:F2} ms");
    }

    private static RaytracerTriangle[] Lopsided(int packed, int outliers)
    {
        var tris = SceneFactory.Clusters(1, packed, 1f, 5);
        var extra = new RaytracerTriangle[packed + outliers];
        Array.Copy(tris, extra, packed);
        for (int i = 0; i < outliers; i++)
        {
            var p = new Vector3(200f + i * 30f, 40f, -20f);
            extra[packed + i] = new RaytracerTriangle
            {
                V0 = p,
                V1 = p + new Vector3(8f, 0.2f, 0f),
                V2 = p + new Vector3(0.2f, 6f, 0.2f)
            };
        }
        return extra;
    }

    private static TimeSpan TimeBuild(Func<RaytracerMeshData> build, out RaytracerMeshData mesh)
    {
        build();
        var sw = Stopwatch.StartNew();
        mesh = build();
        for (int i = 0; i < 2; i++)
            mesh = build();
        sw.Stop();
        return sw.Elapsed / 3;
    }

    private static (int Nodes, int Tris, TimeSpan Elapsed) Cost(RaytracerMeshData mesh, Vector3[] origins, Vector3[] directions)
    {
        int nodes = 0;
        int tris = 0;
        RaytracerBvhQuery.TryIntersect(mesh, origins[0], directions[0], 1e6f, out _, out _, out _);
        var sw = Stopwatch.StartNew();
        for (int n = 0; n < 5; n++)
        {
            nodes = 0;
            tris = 0;
            for (int i = 0; i < origins.Length; i++)
            {
                RaytracerBvhQuery.TryIntersect(mesh, origins[i], directions[i], 1e6f, out _, out int nv, out int tt);
                nodes += nv;
                tris += tt;
            }
        }
        sw.Stop();
        return (nodes, tris, sw.Elapsed / 5);
    }

    private static TimeSpan Time(Action action)
    {
        action();
        var sw = Stopwatch.StartNew();
        for (int i = 0; i < 5; i++)
            action();
        sw.Stop();
        return sw.Elapsed / 5;
    }

    private static TimeSpan Time(Func<int> action)
    {
        action();
        var sw = Stopwatch.StartNew();
        for (int i = 0; i < 5; i++)
            action();
        sw.Stop();
        return sw.Elapsed / 5;
    }
}
