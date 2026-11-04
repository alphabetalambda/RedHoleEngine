using System.Numerics;
using RedHoleEngine.Physics;
using RedHoleEngine.Physics.Collision;

namespace RedHoleEngine.Tests.Physics;

public class BroadphaseTests
{
    [Fact]
    public void Sweep_MatchesBruteForce_SparseWithPlane()
    {
        var proxies = new List<BroadphaseProxy>();
        proxies.Add(Proxy(0, new Vector3(-10000f), new Vector3(10000f), body: 0, dynamic: false));

        var rng = new Random(5);
        for (int i = 1; i < 240; i++)
        {
            float x = Next(rng, -80f, 80f);
            float y = Next(rng, 0f, 40f);
            float z = Next(rng, -80f, 80f);
            float s = 0.4f;
            bool dynamic = i % 11 != 0;
            int body = i % 17 == 0 ? i - 1 : i;
            uint layer = i % 19 == 0 ? 2u : 1u;
            uint mask = i % 19 == 0 ? 2u : 1u;
            proxies.Add(Proxy(i, new Vector3(x, y, z), new Vector3(x + s, y + s, z + s), body, dynamic, layer, mask));
        }

        AssertSamePairs(proxies);
    }

    [Fact]
    public void Sweep_MatchesBruteForce_DenseOverlap()
    {
        var proxies = new List<BroadphaseProxy>();
        for (int i = 0; i < 80; i++)
        {
            float x = (i % 8) * 0.5f;
            float y = (i / 8) * 0.5f;
            proxies.Add(Proxy(i, new Vector3(x, y, 0f), new Vector3(x + 2f, y + 2f, 2f), body: i, dynamic: true));
        }

        AssertSamePairs(proxies);
    }

    [Fact]
    public void Sweep_SkipsSameBody_StaticPairs_AndDisjointLayers()
    {
        var proxies = new[]
        {
            Proxy(0, Vector3.Zero, new Vector3(1f), body: 1, dynamic: true),
            Proxy(1, new Vector3(0.5f), new Vector3(1.5f), body: 1, dynamic: true),
            Proxy(2, new Vector3(10f), new Vector3(11f), body: 2, dynamic: false),
            Proxy(3, new Vector3(10.2f), new Vector3(11.2f), body: 3, dynamic: false),
            Proxy(4, new Vector3(0.2f), new Vector3(0.8f), body: 4, dynamic: true, layer: 1, mask: 1),
            Proxy(5, new Vector3(0.2f), new Vector3(0.8f), body: 5, dynamic: true, layer: 4, mask: 4),
            Proxy(6, new Vector3(50f), new Vector3(51f), body: 6, dynamic: true),
            BroadphaseProxy.Inactive(7)
        };

        var pairs = Collect(proxies);
        Assert.Equal(new[] { (0, 4), (1, 4) }, pairs.Select(p => (p.BodyAIndex, p.BodyBIndex)).ToArray());
    }

    [Fact]
    public void BoxAabb_MatchesEightCornerReference()
    {
        var box = new BoxCollider(new Vector3(1.5f, 0.25f, 3f))
        {
            Offset = new Vector3(0.2f, -0.4f, 0.1f)
        };
        var position = new Vector3(10f, -3f, 4f);
        var rotation = Quaternion.CreateFromYawPitchRoll(0.4f, -0.7f, 1.2f);

        var actual = box.GetWorldAABB(position, rotation);
        var expected = EightCorners(box, position, rotation);

        AssertClose(expected.Min, actual.Min);
        AssertClose(expected.Max, actual.Max);

        var center = position + Vector3.Transform(box.Offset, rotation);
        var he = box.HalfExtents;
        for (int i = 0; i < 8; i++)
        {
            var corner = center + Vector3.Transform(new Vector3(
                (i & 1) == 0 ? -he.X : he.X,
                (i & 2) == 0 ? -he.Y : he.Y,
                (i & 4) == 0 ? -he.Z : he.Z), rotation);
            Assert.True(actual.Contains(corner) || NearlyInside(actual, corner));
        }
    }

    [Fact]
    public void BoxAabb_IdentityRotation_IsTight()
    {
        var box = new BoxCollider(new Vector3(2f, 3f, 4f))
        {
            Offset = new Vector3(1f, 0f, -1f)
        };
        var aabb = box.GetWorldAABB(new Vector3(5f, 6f, 7f), Quaternion.Identity);
        AssertClose(new Vector3(4f, 3f, 2f), aabb.Min);
        AssertClose(new Vector3(8f, 9f, 10f), aabb.Max);
    }

    private static void AssertSamePairs(List<BroadphaseProxy> proxies)
    {
        var span = proxies.ToArray().AsSpan();
        var brute = new List<CollisionPair>();
        var sweep = new List<CollisionPair>();
        var auto = new List<CollisionPair>();
        SweepAndPruneBroadphase.CollectPairsBruteForce(span, brute);
        new SweepAndPruneBroadphase().CollectPairsSweep(span, sweep);
        new SweepAndPruneBroadphase().CollectPairs(span, auto);

        Assert.Equal(Keys(brute), Keys(sweep));
        Assert.Equal(Keys(brute), Keys(auto));
    }

    private static List<CollisionPair> Collect(BroadphaseProxy[] proxies)
    {
        var pairs = new List<CollisionPair>();
        new SweepAndPruneBroadphase().CollectPairsSweep(proxies, pairs);
        return pairs;
    }

    private static List<(int, int)> Keys(List<CollisionPair> pairs)
        => pairs.Select(p => (p.BodyAIndex, p.BodyBIndex)).ToList();

    private static BroadphaseProxy Proxy(
        int index, Vector3 min, Vector3 max, int body, bool dynamic, uint layer = 1, uint mask = 1)
    {
        return new BroadphaseProxy(index, new AABB(min, max), body, hasBody: true, dynamic, layer, mask);
    }

    private static AABB EightCorners(BoxCollider box, Vector3 position, Quaternion rotation)
    {
        var center = position + Vector3.Transform(box.Offset, rotation);
        var he = box.HalfExtents;
        var min = new Vector3(float.MaxValue);
        var max = new Vector3(float.MinValue);
        for (int i = 0; i < 8; i++)
        {
            var corner = center + Vector3.Transform(new Vector3(
                (i & 1) == 0 ? -he.X : he.X,
                (i & 2) == 0 ? -he.Y : he.Y,
                (i & 4) == 0 ? -he.Z : he.Z), rotation);
            min = Vector3.Min(min, corner);
            max = Vector3.Max(max, corner);
        }
        return new AABB(min, max);
    }

    private static void AssertClose(Vector3 expected, Vector3 actual)
    {
        Assert.Equal(expected.X, actual.X, precision: 4);
        Assert.Equal(expected.Y, actual.Y, precision: 4);
        Assert.Equal(expected.Z, actual.Z, precision: 4);
    }

    private static bool NearlyInside(AABB box, Vector3 point)
    {
        const float slack = 1e-4f;
        return point.X >= box.Min.X - slack && point.X <= box.Max.X + slack
            && point.Y >= box.Min.Y - slack && point.Y <= box.Max.Y + slack
            && point.Z >= box.Min.Z - slack && point.Z <= box.Max.Z + slack;
    }

    private static float Next(Random rng, float min, float max)
        => min + (float)rng.NextDouble() * (max - min);
}
