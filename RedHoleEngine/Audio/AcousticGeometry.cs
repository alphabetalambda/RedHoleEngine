using System.Numerics;
using RedHoleEngine.Physics.Collision;
using RedHoleEngine.Resources;

namespace RedHoleEngine.Audio;

/// <summary>
/// A world-space shape that acoustic rays can be traced against.
/// </summary>
public interface IAcousticShape
{
    /// <summary>
    /// Nearest intersection with t in [minT, maxT]. Direction must be normalized.
    /// </summary>
    bool Raycast(Vector3 origin, Vector3 direction, float minT, float maxT, out float t, out Vector3 normal);
}

/// <summary>Legacy approximation: a sphere centered on the entity.</summary>
public sealed class SphereAcousticShape : IAcousticShape
{
    public const float DefaultRadius = 5f;
    private readonly Vector3 _center;
    private readonly float _radius;

    public SphereAcousticShape(Vector3 center, float radius = DefaultRadius)
    {
        _center = center;
        _radius = radius;
    }

    public bool Raycast(Vector3 origin, Vector3 direction, float minT, float maxT, out float t, out Vector3 normal)
    {
        t = 0; normal = Vector3.UnitY;
        var toSurface = _center - origin;
        float tca = Vector3.Dot(toSurface, direction);
        if (tca < 0) return false;

        float d2 = Vector3.Dot(toSurface, toSurface) - tca * tca;
        float r2 = _radius * _radius;
        if (d2 > r2) return false;

        t = tca - MathF.Sqrt(r2 - d2);
        if (t < minT || t > maxT) return false;
        normal = Vector3.Normalize(origin + direction * t - _center);
        return true;
    }
}

/// <summary>Physics collider (sphere or box) used as acoustic geometry.</summary>
public sealed class ColliderAcousticShape : IAcousticShape
{
    private readonly Collider _collider;
    private readonly Vector3 _position;
    private readonly Quaternion _rotation;

    public ColliderAcousticShape(Collider collider, Vector3 position, Quaternion rotation)
    {
        _collider = collider;
        _position = position;
        _rotation = rotation;
    }

    public bool Raycast(Vector3 origin, Vector3 direction, float minT, float maxT, out float t, out Vector3 normal)
    {
        t = 0; normal = Vector3.UnitY;
        CollisionDetection.RaycastHit hit;
        bool ok = _collider switch
        {
            SphereCollider s => CollisionDetection.RaycastSphere(origin, direction, maxT, s, _position, _rotation, out hit),
            BoxCollider b => CollisionDetection.RaycastBox(origin, direction, maxT, b, _position, _rotation, out hit),
            _ => Fail(out hit)
        };
        if (!ok || hit.Distance < minT) return false;
        t = hit.Distance;
        normal = hit.Normal;
        return true;
    }

    private static bool Fail(out CollisionDetection.RaycastHit hit) { hit = default; return false; }
}

/// <summary>
/// Triangle mesh in world space with a median-split BVH for ray queries.
/// Triangles are double sided; the returned normal faces the ray origin.
/// </summary>
public sealed class MeshAcousticShape : IAcousticShape
{
    private const int LeafSize = 4;

    private struct Node
    {
        public Vector3 Min, Max;
        public int Left, Right;   // child node indices (interior)
        public int Start, Count;  // triangle range (leaf, Count > 0)
    }

    private readonly Vector3[] _v0, _e1, _e2;
    private readonly List<Node> _nodes = new();
    private readonly int[] _order;

    public int TriangleCount => _order.Length;

    public MeshAcousticShape(Mesh mesh, Matrix4x4 world)
        : this(mesh.Vertices.Select(v => Vector3.Transform(v.Position, world)).ToArray(), mesh.Indices)
    {
    }

    public MeshAcousticShape(Vector3[] worldVertices, uint[] indices)
    {
        int triCount = indices.Length / 3;
        _v0 = new Vector3[triCount];
        _e1 = new Vector3[triCount];
        _e2 = new Vector3[triCount];
        var centroids = new Vector3[triCount];
        var mins = new Vector3[triCount];
        var maxs = new Vector3[triCount];
        for (int i = 0; i < triCount; i++)
        {
            var a = worldVertices[indices[i * 3]];
            var b = worldVertices[indices[i * 3 + 1]];
            var c = worldVertices[indices[i * 3 + 2]];
            _v0[i] = a; _e1[i] = b - a; _e2[i] = c - a;
            centroids[i] = (a + b + c) / 3f;
            mins[i] = Vector3.Min(a, Vector3.Min(b, c));
            maxs[i] = Vector3.Max(a, Vector3.Max(b, c));
        }

        _order = Enumerable.Range(0, triCount).ToArray();
        if (triCount > 0) Build(0, triCount, centroids, mins, maxs);
    }

    private int Build(int start, int count, Vector3[] centroids, Vector3[] mins, Vector3[] maxs)
    {
        var bmin = new Vector3(float.MaxValue);
        var bmax = new Vector3(float.MinValue);
        var cmin = new Vector3(float.MaxValue);
        var cmax = new Vector3(float.MinValue);
        for (int i = start; i < start + count; i++)
        {
            int tri = _order[i];
            bmin = Vector3.Min(bmin, mins[tri]);
            bmax = Vector3.Max(bmax, maxs[tri]);
            cmin = Vector3.Min(cmin, centroids[tri]);
            cmax = Vector3.Max(cmax, centroids[tri]);
        }

        int index = _nodes.Count;
        _nodes.Add(new Node { Min = bmin, Max = bmax, Start = start, Count = count });
        if (count <= LeafSize) return index;

        var ext = cmax - cmin;
        int axis = ext.X >= ext.Y && ext.X >= ext.Z ? 0 : (ext.Y >= ext.Z ? 1 : 2);
        if (Axis(ext, axis) <= 1e-8f) return index; // all centroids coincide; keep as leaf

        Array.Sort(_order, start, count,
            Comparer<int>.Create((a, b) => Axis(centroids[a], axis).CompareTo(Axis(centroids[b], axis))));

        int half = count / 2;
        int left = Build(start, half, centroids, mins, maxs);
        int right = Build(start + half, count - half, centroids, mins, maxs);
        var n = _nodes[index];
        n.Left = left; n.Right = right; n.Count = 0;
        _nodes[index] = n;
        return index;
    }

    private static float SafeInv(float d) => MathF.Abs(d) < 1e-20f ? 1e20f : 1f / d;

    private static float Axis(Vector3 v, int axis) => axis == 0 ? v.X : (axis == 1 ? v.Y : v.Z);

    public bool Raycast(Vector3 origin, Vector3 direction, float minT, float maxT, out float t, out Vector3 normal)
    {
        t = maxT; normal = Vector3.UnitY;
        if (_nodes.Count == 0) return false;

        var inv = new Vector3(SafeInv(direction.X), SafeInv(direction.Y), SafeInv(direction.Z));
        bool found = false;
        int tri = -1;
        var stack = new Stack<int>();
        stack.Push(0);
        while (stack.Count > 0)
        {
            var node = _nodes[stack.Pop()];
            if (!HitsBox(origin, inv, node.Min, node.Max, t)) continue;

            if (node.Count > 0)
            {
                for (int i = node.Start; i < node.Start + node.Count; i++)
                {
                    int k = _order[i];
                    if (RayTriangle(origin, direction, _v0[k], _e1[k], _e2[k], out float hitT)
                        && hitT >= minT && hitT <= t)
                    {
                        t = hitT; tri = k; found = true;
                    }
                }
            }
            else
            {
                stack.Push(node.Left);
                stack.Push(node.Right);
            }
        }

        if (!found) return false;
        var n = Vector3.Normalize(Vector3.Cross(_e1[tri], _e2[tri]));
        normal = Vector3.Dot(n, direction) > 0 ? -n : n;
        return true;
    }

    private static bool HitsBox(Vector3 o, Vector3 inv, Vector3 min, Vector3 max, float maxT)
    {
        float t1 = (min.X - o.X) * inv.X, t2 = (max.X - o.X) * inv.X;
        float tmin = MathF.Min(t1, t2), tmax = MathF.Max(t1, t2);
        t1 = (min.Y - o.Y) * inv.Y; t2 = (max.Y - o.Y) * inv.Y;
        tmin = MathF.Max(tmin, MathF.Min(t1, t2)); tmax = MathF.Min(tmax, MathF.Max(t1, t2));
        t1 = (min.Z - o.Z) * inv.Z; t2 = (max.Z - o.Z) * inv.Z;
        tmin = MathF.Max(tmin, MathF.Min(t1, t2)); tmax = MathF.Min(tmax, MathF.Max(t1, t2));
        // NaN (0 * inf) compares false and is treated as a miss only when no overlap is possible.
        return tmax >= MathF.Max(tmin, 0f) && tmin <= maxT;
    }

    /// <summary>Moller-Trumbore, double sided.</summary>
    public static bool RayTriangle(Vector3 origin, Vector3 dir, Vector3 v0, Vector3 e1, Vector3 e2, out float t)
    {
        t = 0;
        var p = Vector3.Cross(dir, e2);
        float det = Vector3.Dot(e1, p);
        if (MathF.Abs(det) < 1e-9f) return false;
        float invDet = 1f / det;
        var s = origin - v0;
        float u = Vector3.Dot(s, p) * invDet;
        if (u < 0f || u > 1f) return false;
        var q = Vector3.Cross(s, e1);
        float v = Vector3.Dot(dir, q) * invDet;
        if (v < 0f || u + v > 1f) return false;
        t = Vector3.Dot(e2, q) * invDet;
        return t > 0f;
    }
}
