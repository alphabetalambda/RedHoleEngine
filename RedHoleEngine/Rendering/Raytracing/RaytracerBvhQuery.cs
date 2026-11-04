using System.Numerics;
using System.Runtime.CompilerServices;

namespace RedHoleEngine.Rendering.Raytracing;

public readonly struct RaytracerRayHit
{
    public readonly float T;
    public readonly int TriangleIndex;

    public RaytracerRayHit(float t, int triangleIndex)
    {
        T = t;
        TriangleIndex = triangleIndex;
    }
}

/// <summary>
/// CPU traversal of a raytracer BVH. The walk matches the compute shader
/// (left child stored at LeftFirst, right at LeftFirst + 1, 64-entry stack,
/// closer hit kept) so node-visit counts predict shader cost.
/// Direction does not need to be normalized; T is in units of that vector.
/// </summary>
public static class RaytracerBvhQuery
{
    public static bool TryIntersect(
        RaytracerMeshData mesh,
        Vector3 origin,
        Vector3 direction,
        float maxDistance,
        out RaytracerRayHit hit)
    {
        return TryIntersect(mesh, origin, direction, maxDistance, out hit, out _, out _);
    }

    public static bool TryIntersect(
        RaytracerMeshData mesh,
        Vector3 origin,
        Vector3 direction,
        float maxDistance,
        out RaytracerRayHit hit,
        out int nodesVisited,
        out int trianglesTested)
    {
        nodesVisited = 0;
        trianglesTested = 0;
        hit = default;

        var nodes = mesh.Nodes;
        var tris = mesh.Triangles;
        if (nodes.Length == 0 || tris.Length == 0)
            return false;

        Span<int> stack = stackalloc int[64];
        int stackPtr = 0;
        stack[stackPtr++] = 0;

        float closest = maxDistance;
        int closestTri = -1;
        float invX = 1f / direction.X;
        float invY = 1f / direction.Y;
        float invZ = 1f / direction.Z;

        while (stackPtr > 0)
        {
            int nodeIndex = stack[--stackPtr];
            if ((uint)nodeIndex >= (uint)nodes.Length)
                continue;

            ref readonly var node = ref nodes[nodeIndex];
            nodesVisited++;

            if (!IntersectAabb(origin, invX, invY, invZ, node.BoundsMin, node.BoundsMax, out float tmin, out _)
                || tmin > closest)
            {
                continue;
            }

            if (node.TriCount > 0)
            {
                int first = node.LeftFirst;
                int end = Math.Min(first + node.TriCount, tris.Length);
                for (int i = Math.Max(first, 0); i < end; i++)
                {
                    trianglesTested++;
                    if (IntersectTriangle(origin, direction, tris[i], out float t, out _, out _)
                        && t < closest)
                    {
                        closest = t;
                        closestTri = i;
                    }
                }
            }
            else
            {
                int left = node.LeftFirst;
                int right = left + 1;
                if ((uint)right < (uint)nodes.Length && stackPtr < 63)
                    stack[stackPtr++] = right;
                if ((uint)left < (uint)nodes.Length && stackPtr < 63)
                    stack[stackPtr++] = left;
            }
        }

        if (closestTri < 0)
            return false;

        hit = new RaytracerRayHit(closest, closestTri);
        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IntersectAabb(
        Vector3 origin, float invX, float invY, float invZ,
        Vector3 bmin, Vector3 bmax, out float tmin, out float tmax)
    {
        float t0x = (bmin.X - origin.X) * invX;
        float t1x = (bmax.X - origin.X) * invX;
        float t0y = (bmin.Y - origin.Y) * invY;
        float t1y = (bmax.Y - origin.Y) * invY;
        float t0z = (bmin.Z - origin.Z) * invZ;
        float t1z = (bmax.Z - origin.Z) * invZ;

        float minX = t0x < t1x ? t0x : t1x;
        float maxX = t0x < t1x ? t1x : t0x;
        float minY = t0y < t1y ? t0y : t1y;
        float maxY = t0y < t1y ? t1y : t0y;
        float minZ = t0z < t1z ? t0z : t1z;
        float maxZ = t0z < t1z ? t1z : t0z;

        tmin = minX > minY ? minX : minY;
        if (minZ > tmin) tmin = minZ;
        if (tmin < 0f) tmin = 0f;

        tmax = maxX < maxY ? maxX : maxY;
        if (maxZ < tmax) tmax = maxZ;
        return tmax >= tmin;
    }

    /// <summary>
    /// Möller–Trumbore, matching the shader's epsilon (reject t &lt;= 0.0001).
    /// </summary>
    private static bool IntersectTriangle(
        Vector3 origin, Vector3 direction, in RaytracerTriangle tri,
        out float t, out float u, out float v)
    {
        var v0v1 = tri.V1 - tri.V0;
        var v0v2 = tri.V2 - tri.V0;
        var pvec = Vector3.Cross(direction, v0v2);
        float det = Vector3.Dot(v0v1, pvec);
        if (MathF.Abs(det) < 1e-6f)
        {
            t = 0f;
            u = 0f;
            v = 0f;
            return false;
        }

        float invDet = 1f / det;
        var tvec = origin - tri.V0;
        u = Vector3.Dot(tvec, pvec) * invDet;
        if (u < 0f || u > 1f)
        {
            t = 0f;
            v = 0f;
            return false;
        }

        var qvec = Vector3.Cross(tvec, v0v1);
        v = Vector3.Dot(direction, qvec) * invDet;
        if (v < 0f || u + v > 1f)
        {
            t = 0f;
            return false;
        }

        t = Vector3.Dot(v0v2, qvec) * invDet;
        return t > 0.0001f;
    }
}
