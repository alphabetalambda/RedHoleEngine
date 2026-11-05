using RedHoleEngine.Physics.Collision;

namespace RedHoleEngine.Physics;

/// <summary>
/// One collider entered into the broadphase. <see cref="Index"/> is the collider
/// slot stored on <see cref="CollisionPair"/> and, for stable solver ordering, must
/// equal this proxy's position in the span passed to the collector.
/// </summary>
public readonly struct BroadphaseProxy
{
    public readonly int Index;
    public readonly AABB Bounds;
    public readonly int BodyId;
    public readonly bool HasBody;
    public readonly bool IsDynamic;
    public readonly uint Layer;
    public readonly uint Mask;

    public BroadphaseProxy(int index, AABB bounds, int bodyId, bool hasBody, bool isDynamic, uint layer, uint mask)
    {
        Index = index;
        Bounds = bounds;
        BodyId = bodyId;
        HasBody = hasBody;
        IsDynamic = isDynamic;
        Layer = layer;
        Mask = mask;
    }

    public static BroadphaseProxy Inactive(int index)
        => new(index, default, 0, false, false, 0, 0);
}

/// <summary>
/// Sweep-and-prune broadphase. Produces the same pairs, in the same order, as the
/// previous i &lt; j AABB loop: inactive colliders, two shapes on one body, pairs with
/// no dynamic body, and non-overlapping layers are skipped.
/// Objects whose X extent exceeds <see cref="LargeExtent"/> (infinite planes) are
/// tested against everyone else so they do not collapse the sweep into n².
/// Dense scenes, where the average object is a large fraction of the world, stay
/// on the nested loop because the sweep window would be the whole set.
/// </summary>
public sealed class SweepAndPruneBroadphase
{
    public const float LargeExtent = 5000f;
    public const int BruteForceCutoff = 48;

    private readonly List<int> _small = new();
    private readonly List<int> _large = new();
    private readonly MinXComparer _minXComparer = new();
    private float[] _minX = Array.Empty<float>();

    /// <summary>
    /// Chooses sweep-and-prune or the nested loop. Both fill <paramref name="pairs"/>
    /// with the same ordered result as <see cref="CollectPairsBruteForce"/>.
    /// </summary>
    public void CollectPairs(ReadOnlySpan<BroadphaseProxy> proxies, List<CollisionPair> pairs)
    {
        if (proxies.Length <= BruteForceCutoff || !SceneIsSparse(proxies))
        {
            CollectPairsBruteForce(proxies, pairs);
            return;
        }

        CollectPairsSweep(proxies, pairs);
    }

    /// <summary>
    /// Always runs sweep-and-prune, including scenes where the nested loop is faster.
    /// Used by tests and benchmarks. Output order matches the brute-force loop.
    /// </summary>
    public void CollectPairsSweep(ReadOnlySpan<BroadphaseProxy> proxies, List<CollisionPair> pairs)
    {
        pairs.Clear();
        _small.Clear();
        _large.Clear();

        if (_minX.Length < proxies.Length)
            _minX = new float[Math.Max(proxies.Length, 16)];

        for (int i = 0; i < proxies.Length; i++)
        {
            if (!proxies[i].HasBody)
                continue;

            float extent = proxies[i].Bounds.Max.X - proxies[i].Bounds.Min.X;
            _minX[i] = proxies[i].Bounds.Min.X;
            if (extent >= LargeExtent)
                _large.Add(i);
            else
                _small.Add(i);
        }

        _minXComparer.Keys = _minX;
        _small.Sort(_minXComparer);

        for (int a = 0; a < _small.Count; a++)
        {
            int ia = _small[a];
            float maxX = proxies[ia].Bounds.Max.X;
            for (int b = a + 1; b < _small.Count; b++)
            {
                int ib = _small[b];
                if (_minX[ib] > maxX)
                    break;
                TryAdd(proxies[ia], proxies[ib], pairs);
            }
        }

        for (int a = 0; a < _large.Count; a++)
        {
            int ia = _large[a];
            for (int b = a + 1; b < _large.Count; b++)
                TryAdd(proxies[ia], proxies[_large[b]], pairs);

            for (int b = 0; b < _small.Count; b++)
                TryAdd(proxies[ia], proxies[_small[b]], pairs);
        }

        if (pairs.Count > 1)
            pairs.Sort(PairComparer.Instance);
    }

    public static void CollectPairsBruteForce(ReadOnlySpan<BroadphaseProxy> proxies, List<CollisionPair> pairs)
    {
        pairs.Clear();
        for (int i = 0; i < proxies.Length; i++)
        {
            if (!proxies[i].HasBody)
                continue;

            for (int j = i + 1; j < proxies.Length; j++)
            {
                if (!proxies[j].HasBody)
                    continue;
                TryAdd(proxies[i], proxies[j], pairs);
            }
        }
    }

    private static bool SceneIsSparse(ReadOnlySpan<BroadphaseProxy> proxies)
    {
        int n = 0;
        float minX = float.MaxValue;
        float maxX = float.MinValue;
        float sumWidth = 0f;

        for (int i = 0; i < proxies.Length; i++)
        {
            if (!proxies[i].HasBody)
                continue;

            float width = proxies[i].Bounds.Max.X - proxies[i].Bounds.Min.X;
            if (width >= LargeExtent)
                continue;

            n++;
            if (proxies[i].Bounds.Min.X < minX)
                minX = proxies[i].Bounds.Min.X;
            if (proxies[i].Bounds.Max.X > maxX)
                maxX = proxies[i].Bounds.Max.X;
            sumWidth += width;
        }

        if (n <= BruteForceCutoff)
            return false;

        float span = maxX - minX;
        if (span < 1e-4f)
            return false;

        // Average X extent narrower than a quarter of the scene. Above that the
        // sweep window covers most objects and the sort is pure overhead.
        return sumWidth < span * n * 0.25f;
    }

    private static void TryAdd(in BroadphaseProxy a, in BroadphaseProxy b, List<CollisionPair> pairs)
    {
        if (a.BodyId == b.BodyId)
            return;
        if (!a.IsDynamic && !b.IsDynamic)
            return;
        if ((a.Layer & b.Mask) == 0 && (b.Layer & a.Mask) == 0)
            return;
        if (!a.Bounds.Intersects(b.Bounds))
            return;

        pairs.Add(new CollisionPair(a.Index, b.Index));
    }

    private sealed class MinXComparer : IComparer<int>
    {
        public float[] Keys = Array.Empty<float>();

        public int Compare(int a, int b)
        {
            float d = Keys[a] - Keys[b];
            if (d < 0f) return -1;
            if (d > 0f) return 1;
            return a - b;
        }
    }

    private sealed class PairComparer : IComparer<CollisionPair>
    {
        public static readonly PairComparer Instance = new();

        public int Compare(CollisionPair a, CollisionPair b)
        {
            int c = a.BodyAIndex - b.BodyAIndex;
            if (c != 0)
                return c;
            return a.BodyBIndex - b.BodyBIndex;
        }
    }
}
