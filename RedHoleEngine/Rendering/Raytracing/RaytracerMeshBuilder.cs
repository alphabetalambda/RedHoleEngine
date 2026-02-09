using System;
using System.Collections.Generic;
using System.Numerics;

namespace RedHoleEngine.Rendering.Raytracing;

public static class RaytracerMeshBuilder
{
    public static RaytracerMeshData Build(IReadOnlyList<RaytracerTriangle> triangles, int maxLeafSize = 4)
    {
        if (triangles.Count == 0)
        {
            return new RaytracerMeshData();
        }

        var triArray = triangles.ToArray();
        var nodes = new List<RaytracerBvhNode>(triArray.Length * 2);
        nodes.Add(default);

        BuildNode(nodes, triArray, 0, 0, triArray.Length, maxLeafSize);

        return new RaytracerMeshData
        {
            Nodes = nodes.ToArray(),
            Triangles = triArray
        };
    }

    private static void BuildNode(
        List<RaytracerBvhNode> nodes,
        RaytracerTriangle[] triangles,
        int nodeIndex,
        int start,
        int count,
        int maxLeafSize)
    {
        var bounds = ComputeBounds(triangles, start, count);

        if (count <= maxLeafSize)
        {
            nodes[nodeIndex] = new RaytracerBvhNode
            {
                BoundsMin = bounds.Min,
                BoundsMax = bounds.Max,
                LeftFirst = start,
                TriCount = count
            };
            return;
        }

        var centroidBounds = ComputeCentroidBounds(triangles, start, count);
        int axis = LargestAxis(centroidBounds.Min, centroidBounds.Max);

        Array.Sort(triangles, start, count, new TriangleCentroidComparer(axis));

        int leftCount = count / 2;
        int rightCount = count - leftCount;

        int leftIndex = nodes.Count;
        nodes.Add(default);
        int rightIndex = nodes.Count;
        nodes.Add(default);

        nodes[nodeIndex] = new RaytracerBvhNode
        {
            BoundsMin = bounds.Min,
            BoundsMax = bounds.Max,
            LeftFirst = leftIndex,
            TriCount = 0
        };

        BuildNode(nodes, triangles, leftIndex, start, leftCount, maxLeafSize);
        BuildNode(nodes, triangles, rightIndex, start + leftCount, rightCount, maxLeafSize);
    }

    private static Bounds ComputeBounds(RaytracerTriangle[] triangles, int start, int count)
    {
        var min = new Vector3(float.MaxValue);
        var max = new Vector3(float.MinValue);

        for (int i = start; i < start + count; i++)
        {
            ref var tri = ref triangles[i];
            min = Vector3.Min(min, tri.V0);
            min = Vector3.Min(min, tri.V1);
            min = Vector3.Min(min, tri.V2);

            max = Vector3.Max(max, tri.V0);
            max = Vector3.Max(max, tri.V1);
            max = Vector3.Max(max, tri.V2);
        }

        return new Bounds(min, max);
    }

    private static Bounds ComputeCentroidBounds(RaytracerTriangle[] triangles, int start, int count)
    {
        var min = new Vector3(float.MaxValue);
        var max = new Vector3(float.MinValue);

        for (int i = start; i < start + count; i++)
        {
            var centroid = (triangles[i].V0 + triangles[i].V1 + triangles[i].V2) / 3f;
            min = Vector3.Min(min, centroid);
            max = Vector3.Max(max, centroid);
        }

        return new Bounds(min, max);
    }

    private static int LargestAxis(Vector3 min, Vector3 max)
    {
        var size = max - min;
        if (size.X > size.Y && size.X > size.Z) return 0;
        if (size.Y > size.Z) return 1;
        return 2;
    }

    private readonly struct Bounds
    {
        public readonly Vector3 Min;
        public readonly Vector3 Max;

        public Bounds(Vector3 min, Vector3 max)
        {
            Min = min;
            Max = max;
        }
    }

    private sealed class TriangleCentroidComparer : IComparer<RaytracerTriangle>
    {
        private readonly int _axis;

        public TriangleCentroidComparer(int axis)
        {
            _axis = axis;
        }

        public int Compare(RaytracerTriangle x, RaytracerTriangle y)
        {
            float a = GetAxis((x.V0 + x.V1 + x.V2) / 3f);
            float b = GetAxis((y.V0 + y.V1 + y.V2) / 3f);
            return a.CompareTo(b);
        }

        private float GetAxis(Vector3 v)
        {
            return _axis switch
            {
                0 => v.X,
                1 => v.Y,
                _ => v.Z
            };
        }
    }
}
