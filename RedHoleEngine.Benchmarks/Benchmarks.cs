using System.Numerics;
using BenchmarkDotNet.Attributes;
using RedHoleEngine.Core.ECS;
using RedHoleEngine.Physics;
using RedHoleEngine.Physics.Collision;
using RedHoleEngine.Rendering.Raytracing;

namespace RedHoleEngine.Benchmarks;

[Config(typeof(BenchConfig))]
public class BvhBuildBenchmarks
{
    private RaytracerTriangle[] _triangles = null!;

    [GlobalSetup]
    public void Setup() => _triangles = SceneFactory.Clusters(6, 800, 30f, 1);

    [Benchmark(Baseline = true)]
    public RaytracerMeshData MedianSplit() => LegacyMedianSplitBuilder.Build(_triangles);

    [Benchmark]
    public RaytracerMeshData SurfaceAreaHeuristic() => RaytracerMeshBuilder.Build(_triangles);
}

[Config(typeof(BenchConfig))]
public class BvhTraverseBenchmarks
{
    private RaytracerMeshData _median = null!;
    private RaytracerMeshData _sah = null!;
    private Vector3[] _origins = null!;
    private Vector3[] _directions = null!;

    [GlobalSetup]
    public void Setup()
    {
        var triangles = SceneFactory.Clusters(8, 400, 40f, 2);
        _median = LegacyMedianSplitBuilder.Build(triangles);
        _sah = RaytracerMeshBuilder.Build(triangles);
        SceneFactory.Rays(triangles, 64, 2, out _origins, out _directions);
    }

    [Benchmark(Baseline = true)]
    public int MedianSplit() => Trace(_median);

    [Benchmark]
    public int SurfaceAreaHeuristic() => Trace(_sah);

    private int Trace(RaytracerMeshData mesh)
    {
        int visited = 0;
        for (int i = 0; i < _origins.Length; i++)
        {
            RaytracerBvhQuery.TryIntersect(mesh, _origins[i], _directions[i], 1e6f, out _, out int nodes, out _);
            visited += nodes;
        }
        return visited;
    }
}

[Config(typeof(BenchConfig))]
public class BroadphaseBenchmarks
{
    private BroadphaseProxy[] _sparse = null!;
    private readonly SweepAndPruneBroadphase _sweep = new();
    private readonly List<CollisionPair> _pairs = new();

    [GlobalSetup]
    public void Setup() => _sparse = SceneFactory.SparseProxies(2000, 7);

    [Benchmark(Baseline = true)]
    public int BruteForce()
    {
        SweepAndPruneBroadphase.CollectPairsBruteForce(_sparse, _pairs);
        return _pairs.Count;
    }

    [Benchmark]
    public int SweepAndPrune()
    {
        _sweep.CollectPairsSweep(_sparse, _pairs);
        return _pairs.Count;
    }
}

[Config(typeof(BenchConfig))]
public class BoxBroadphaseBenchmarks
{
    private BoxCollider[] _boxes = null!;
    private Vector3[] _positions = null!;
    private Quaternion[] _rotations = null!;
    private readonly SweepAndPruneBroadphase _sweep = new();
    private readonly List<CollisionPair> _pairs = new();

    [GlobalSetup]
    public void Setup()
    {
        const int n = 180;
        var rng = new Random(4);
        _boxes = new BoxCollider[n];
        _positions = new Vector3[n];
        _rotations = new Quaternion[n];
        for (int i = 0; i < n; i++)
        {
            _boxes[i] = new BoxCollider(new Vector3(0.4f, 0.3f, 0.5f));
            _positions[i] = new Vector3(i % 18, (i / 18) % 5, i / 90) * 3f;
            _rotations[i] = Quaternion.CreateFromYawPitchRoll(
                (float)rng.NextDouble(), (float)rng.NextDouble(), (float)rng.NextDouble());
        }
    }

    [Benchmark(Baseline = true)]
    public int LegacyCornerAabbs()
    {
        int hits = 0;
        for (int i = 0; i < _boxes.Length; i++)
        {
            var a = SceneFactory.LegacyBoxAabb(_boxes[i], _positions[i], _rotations[i]);
            for (int j = i + 1; j < _boxes.Length; j++)
            {
                var b = SceneFactory.LegacyBoxAabb(_boxes[j], _positions[j], _rotations[j]);
                if (a.Intersects(b))
                    hits++;
            }
        }
        return hits;
    }

    [Benchmark]
    public int CachedAabbSweep()
    {
        var proxies = new BroadphaseProxy[_boxes.Length];
        for (int i = 0; i < _boxes.Length; i++)
        {
            proxies[i] = new BroadphaseProxy(
                i,
                _boxes[i].GetWorldAABB(_positions[i], _rotations[i]),
                i,
                true,
                true,
                1,
                1);
        }
        _sweep.CollectPairs(proxies, _pairs);
        return _pairs.Count;
    }
}

[Config(typeof(BenchConfig))]
public class EcsQueryBenchmarks
{
    private World _world = null!;

    [GlobalSetup]
    public void Setup()
    {
        _world = new World();
        for (int i = 0; i < 4000; i++)
        {
            var entity = _world.CreateEntity();
            _world.AddComponent(entity, new BenchComponent { Value = i });
        }
    }

    [GlobalCleanup]
    public void Cleanup() => _world.Dispose();

    [Benchmark(Baseline = true)]
    public int YieldEnumerator()
    {
        int sum = 0;
        var pool = _world.GetPool<BenchComponent>();
        foreach (var id in pool.GetEntityIds())
            sum += id;
        return sum;
    }

    [Benchmark]
    public int StructQuery()
    {
        int sum = 0;
        foreach (var entity in _world.Query<BenchComponent>())
            sum += entity.Id;
        return sum;
    }
}

public struct BenchComponent : IComponent
{
    public int Value;
}
