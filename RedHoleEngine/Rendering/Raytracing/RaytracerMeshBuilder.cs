using System;
using System.Buffers;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace RedHoleEngine.Rendering.Raytracing;

/// <summary>
/// Builds a binary BVH over raytracer triangles.
/// Node layout is unchanged (32 bytes, left child at LeftFirst and right at LeftFirst + 1)
/// so existing compute-shader traversal stays valid.
/// The split is a binned surface-area heuristic. A median split is used only when SAH
/// cannot separate the node or when a split would make the tree deeper than the
/// shader's 64-entry stack can walk.
/// </summary>
public static class RaytracerMeshBuilder
{
    public static RaytracerMeshData Build(IReadOnlyList<RaytracerTriangle> triangles, int maxLeafSize = 4)
    {
        ArgumentNullException.ThrowIfNull(triangles);
        if (triangles.Count == 0)
            return new RaytracerMeshData();

        if (maxLeafSize < 1)
            maxLeafSize = 1;

        var builder = new Builder(triangles, maxLeafSize);
        return builder.Build();
    }

    private sealed class Builder
    {
        // Matches the fixed stack in raytracer_vulkan.comp. Leaves created by a
        // balanced split stay well under this; SAH spines are redirected to a
        // median split before the predicted depth would drop children.
        private const int ShaderStackBudget = 48;
        private const int HardDepthLimit = 56;
        private const int BinCount = 12;

        private readonly int _maxLeaf;
        private readonly IReadOnlyList<RaytracerTriangle> _source;
        private readonly BuildPrim[] _prims;
        private readonly int[] _indices;
        private readonly RaytracerBvhNode[] _nodes;
        private readonly IndexAxisComparer _comparer = new();
        private int _nodeCount;

        public Builder(IReadOnlyList<RaytracerTriangle> triangles, int maxLeafSize)
        {
            _maxLeaf = maxLeafSize;
            _source = triangles;
            int count = triangles.Count;
            _prims = ArrayPool<BuildPrim>.Shared.Rent(count);
            _indices = ArrayPool<int>.Shared.Rent(count);
            int nodeCap = count > int.MaxValue / 2 ? int.MaxValue : count * 2;
            _nodes = ArrayPool<RaytracerBvhNode>.Shared.Rent(Math.Max(nodeCap, 1));
            _comparer.Prims = _prims;
        }

        public RaytracerMeshData Build()
        {
            try
            {
                int count = _source.Count;
                for (int i = 0; i < count; i++)
                {
                    _indices[i] = i;
                    var tri = _source[i];
                    var min = Vector3.Min(Vector3.Min(tri.V0, tri.V1), tri.V2);
                    var max = Vector3.Max(Vector3.Max(tri.V0, tri.V1), tri.V2);
                    _prims[i] = new BuildPrim((tri.V0 + tri.V1 + tri.V2) * (1f / 3f), min, max);
                }

                _nodeCount = 1;
                BuildNode(0, 0, count, 0);

                var ordered = new RaytracerTriangle[count];
                for (int i = 0; i < count; i++)
                    ordered[i] = _source[_indices[i]];

                var nodes = new RaytracerBvhNode[_nodeCount];
                Array.Copy(_nodes, nodes, _nodeCount);

                return new RaytracerMeshData
                {
                    Nodes = nodes,
                    Triangles = ordered
                };
            }
            finally
            {
                ArrayPool<BuildPrim>.Shared.Return(_prims);
                ArrayPool<int>.Shared.Return(_indices);
                ArrayPool<RaytracerBvhNode>.Shared.Return(_nodes);
            }
        }

        private void BuildNode(int nodeIndex, int start, int count, int depth)
        {
            RangeBounds(start, count, out var bmin, out var bmax);

            if (count <= _maxLeaf || depth >= HardDepthLimit)
            {
                _nodes[nodeIndex] = Leaf(bmin, bmax, start, count);
                return;
            }

            if (!ExceedsBudget(count, depth) && TrySahSplit(nodeIndex, start, count, depth, bmin, bmax))
                return;

            MedianSplit(nodeIndex, start, count, depth, bmin, bmax);
        }

        private bool TrySahSplit(int nodeIndex, int start, int count, int depth, Vector3 bmin, Vector3 bmax)
        {
            CentroidBounds(start, count, out var cmin, out var cmax);

            Span<Bin> bins = stackalloc Bin[BinCount];
            Span<int> suffixCount = stackalloc int[BinCount];
            Span<Vector3> suffixMin = stackalloc Vector3[BinCount];
            Span<Vector3> suffixMax = stackalloc Vector3[BinCount];
            float bestCost = float.MaxValue;
            int bestAxis = -1;
            int bestBin = -1;
            float bestCMin = 0f;
            float bestScale = 0f;

            for (int axis = 0; axis < 3; axis++)
            {
                float extent = Axis(cmax, axis) - Axis(cmin, axis);
                if (extent < 1e-8f)
                    continue;

                bins.Clear();
                float scale = BinCount / extent;
                float axisMin = Axis(cmin, axis);

                for (int i = start; i < start + count; i++)
                {
                    ref readonly var prim = ref _prims[_indices[i]];
                    int bin = BinOf(prim.Centroid, axis, axisMin, scale);
                    ref var slot = ref bins[bin];
                    if (slot.Count == 0)
                    {
                        slot.Min = prim.Min;
                        slot.Max = prim.Max;
                    }
                    else
                    {
                        slot.Min = Vector3.Min(slot.Min, prim.Min);
                        slot.Max = Vector3.Max(slot.Max, prim.Max);
                    }
                    slot.Count++;
                }

                int runCount = 0;
                var runMin = default(Vector3);
                var runMax = default(Vector3);
                bool runHas = false;
                for (int i = BinCount - 1; i >= 0; i--)
                {
                    if (bins[i].Count > 0)
                    {
                        if (!runHas)
                        {
                            runMin = bins[i].Min;
                            runMax = bins[i].Max;
                            runHas = true;
                        }
                        else
                        {
                            runMin = Vector3.Min(runMin, bins[i].Min);
                            runMax = Vector3.Max(runMax, bins[i].Max);
                        }
                        runCount += bins[i].Count;
                    }
                    suffixCount[i] = runCount;
                    suffixMin[i] = runMin;
                    suffixMax[i] = runMax;
                }

                int leftCount = 0;
                var leftMin = default(Vector3);
                var leftMax = default(Vector3);
                bool leftHas = false;
                for (int i = 0; i < BinCount - 1; i++)
                {
                    if (bins[i].Count > 0)
                    {
                        if (!leftHas)
                        {
                            leftMin = bins[i].Min;
                            leftMax = bins[i].Max;
                            leftHas = true;
                        }
                        else
                        {
                            leftMin = Vector3.Min(leftMin, bins[i].Min);
                            leftMax = Vector3.Max(leftMax, bins[i].Max);
                        }
                        leftCount += bins[i].Count;
                    }

                    int rightCount = suffixCount[i + 1];
                    if (leftCount == 0 || rightCount == 0 || !leftHas)
                        continue;

                    float cost = SurfaceArea(leftMin, leftMax) * leftCount
                                 + SurfaceArea(suffixMin[i + 1], suffixMax[i + 1]) * rightCount;
                    if (cost < bestCost)
                    {
                        bestCost = cost;
                        bestAxis = axis;
                        bestBin = i;
                        bestCMin = axisMin;
                        bestScale = scale;
                    }
                }
            }

            if (bestAxis < 0)
                return false;

            int splitLeft = Partition(start, count, bestAxis, bestCMin, bestScale, bestBin);
            if (splitLeft <= 0 || splitLeft >= count)
                return false;
            if (ExceedsBudget(splitLeft, depth + 1) || ExceedsBudget(count - splitLeft, depth + 1))
                return false;

            EmitInterior(nodeIndex, start, splitLeft, count - splitLeft, depth, bmin, bmax);
            return true;
        }

        private void MedianSplit(int nodeIndex, int start, int count, int depth, Vector3 bmin, Vector3 bmax)
        {
            CentroidBounds(start, count, out var cmin, out var cmax);
            _comparer.Axis = LargestAxis(cmin, cmax);
            Array.Sort(_indices, start, count, _comparer);

            int leftCount = count / 2;
            EmitInterior(nodeIndex, start, leftCount, count - leftCount, depth, bmin, bmax);
        }

        private void EmitInterior(int nodeIndex, int start, int leftCount, int rightCount, int depth, Vector3 bmin, Vector3 bmax)
        {
            int leftIndex = _nodeCount++;
            int rightIndex = _nodeCount++;
            _nodes[nodeIndex] = new RaytracerBvhNode
            {
                BoundsMin = bmin,
                BoundsMax = bmax,
                LeftFirst = leftIndex,
                TriCount = 0
            };

            BuildNode(leftIndex, start, leftCount, depth + 1);
            BuildNode(rightIndex, start + leftCount, rightCount, depth + 1);
        }

        private int Partition(int start, int count, int axis, float cmin, float scale, int maxBin)
        {
            int end = start + count;
            int left = start;
            int right = end - 1;
            while (left <= right)
            {
                int bin = BinOf(_prims[_indices[left]].Centroid, axis, cmin, scale);
                if (bin <= maxBin)
                {
                    left++;
                }
                else
                {
                    (_indices[left], _indices[right]) = (_indices[right], _indices[left]);
                    right--;
                }
            }
            return left - start;
        }

        private void RangeBounds(int start, int count, out Vector3 min, out Vector3 max)
        {
            min = new Vector3(float.MaxValue);
            max = new Vector3(float.MinValue);
            for (int i = start; i < start + count; i++)
            {
                ref readonly var prim = ref _prims[_indices[i]];
                min = Vector3.Min(min, prim.Min);
                max = Vector3.Max(max, prim.Max);
            }
        }

        private void CentroidBounds(int start, int count, out Vector3 min, out Vector3 max)
        {
            min = new Vector3(float.MaxValue);
            max = new Vector3(float.MinValue);
            for (int i = start; i < start + count; i++)
            {
                var c = _prims[_indices[i]].Centroid;
                min = Vector3.Min(min, c);
                max = Vector3.Max(max, c);
            }
        }

        private bool ExceedsBudget(int count, int depth)
        {
            if (depth >= ShaderStackBudget)
                return true;
            int leaves = (count + _maxLeaf - 1) / _maxLeaf;
            if (leaves <= 1)
                return false;
            return CeilLog2(leaves) >= ShaderStackBudget - depth;
        }

        private static RaytracerBvhNode Leaf(Vector3 min, Vector3 max, int start, int count)
        {
            return new RaytracerBvhNode
            {
                BoundsMin = min,
                BoundsMax = max,
                LeftFirst = start,
                TriCount = count
            };
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int BinOf(Vector3 centroid, int axis, float axisMin, float scale)
        {
            int bin = (int)((Axis(centroid, axis) - axisMin) * scale);
            if (bin < 0)
                return 0;
            if (bin >= BinCount)
                return BinCount - 1;
            return bin;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static float Axis(Vector3 v, int axis)
        {
            return axis == 0 ? v.X : axis == 1 ? v.Y : v.Z;
        }

        private static int LargestAxis(Vector3 min, Vector3 max)
        {
            var size = max - min;
            if (size.X >= size.Y && size.X >= size.Z)
                return 0;
            if (size.Y >= size.Z)
                return 1;
            return 2;
        }

        private static int CeilLog2(int n)
        {
            int v = 0;
            int c = 1;
            while (c < n && v < 30)
            {
                c <<= 1;
                v++;
            }
            return v;
        }

        private static float SurfaceArea(Vector3 min, Vector3 max)
        {
            float dx = max.X - min.X;
            float dy = max.Y - min.Y;
            float dz = max.Z - min.Z;
            if (dx < 0f) dx = 0f;
            if (dy < 0f) dy = 0f;
            if (dz < 0f) dz = 0f;
            return dx * dy + dy * dz + dz * dx;
        }

        private struct Bin
        {
            public int Count;
            public Vector3 Min;
            public Vector3 Max;
        }

        private readonly struct BuildPrim
        {
            public readonly Vector3 Centroid;
            public readonly Vector3 Min;
            public readonly Vector3 Max;

            public BuildPrim(Vector3 centroid, Vector3 min, Vector3 max)
            {
                Centroid = centroid;
                Min = min;
                Max = max;
            }
        }

        private sealed class IndexAxisComparer : IComparer<int>
        {
            public BuildPrim[] Prims = null!;
            public int Axis;

            public int Compare(int x, int y)
            {
                float a = RaytracerMeshBuilder.Builder.Axis(Prims[x].Centroid, Axis);
                float b = RaytracerMeshBuilder.Builder.Axis(Prims[y].Centroid, Axis);
                if (a < b) return -1;
                if (a > b) return 1;
                return 0;
            }
        }
    }
}
