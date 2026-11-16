using System.Numerics;

namespace RedHoleEngine.AI.Navigation;

/// <summary>Outcome of a path query.</summary>
public enum NavPathResult
{
    /// <summary>A path was written to the output list.</summary>
    Success = 0,

    /// <summary>The start is not on, or near enough to, a walkable cell.</summary>
    InvalidStart = 1,

    /// <summary>The goal is not on, or near enough to, a walkable cell.</summary>
    InvalidGoal = 2,

    /// <summary>Start and goal are both walkable but not connected.</summary>
    Unreachable = 3
}

/// <summary>
/// A* over a <see cref="NavGrid"/> (8-connected, octile heuristic) followed by
/// line-of-sight string pulling. Search buffers are kept between calls, so a query
/// allocates nothing once the buffers have grown to the grid size.
/// Not thread-safe: use one instance per thread.
/// </summary>
public sealed class NavPathfinder
{
    private const float Diagonal = 1.41421356f;

    private float[] _cost = Array.Empty<float>();
    private int[] _parent = Array.Empty<int>();
    private int[] _openStamp = Array.Empty<int>();
    private int[] _closedStamp = Array.Empty<int>();
    private int[] _heapNode = new int[64];
    private float[] _heapPriority = new float[64];
    private int _heapCount;
    private int _stamp;
    private readonly List<int> _cells = new();
    private readonly List<Vector3> _raw = new();

    /// <summary>How far (world units) a blocked or off-grid start/goal may be snapped to a walkable cell.</summary>
    public float SnapDistance { get; set; } = 2f;

    /// <summary>
    /// How many points ahead string pulling looks for a shortcut. Bounds the cost of
    /// smoothing a long winding path; open ground is unaffected apart from a few collinear points.
    /// </summary>
    public int SmoothingLookahead { get; set; } = 64;

    /// <summary>Cells expanded by the most recent search. Zero when the region check answered the query.</summary>
    public int LastExpandedCount { get; private set; }

    /// <summary>
    /// Finds a path between two world positions. On success <paramref name="path"/> starts at
    /// <paramref name="start"/> and ends at the goal (or the walkable cell nearest to it).
    /// On failure the list is left empty.
    /// </summary>
    public NavPathResult FindPath(NavGrid grid, Vector3 start, Vector3 goal, List<Vector3> path, bool smooth = true)
    {
        ArgumentNullException.ThrowIfNull(grid);
        ArgumentNullException.ThrowIfNull(path);
        path.Clear();
        LastExpandedCount = 0;

        bool startOnCell = grid.IsWalkable(start);
        if (!grid.TryFindNearestWalkable(start, SnapDistance, out int startX, out int startZ))
            return NavPathResult.InvalidStart;

        bool goalOnCell = grid.IsWalkable(goal);
        if (!grid.TryFindNearestWalkable(goal, SnapDistance, out int goalX, out int goalZ))
            return NavPathResult.InvalidGoal;

        var result = FindCellPath(grid, startX, startZ, goalX, goalZ, _cells);
        if (result != NavPathResult.Success)
            return result;

        // Raw polyline: the real start, the cell centers in between, the real goal.
        // A snapped end is replaced by (start) or stops at (goal) its cell center.
        _raw.Clear();
        _raw.Add(new Vector3(start.X, grid.GetHeight(startX, startZ), start.Z));
        if (!startOnCell)
            _raw.Add(grid.CellToWorld(startX, startZ));
        for (int i = 1; i < _cells.Count - 1; i++)
            _raw.Add(grid.CellToWorld(_cells[i] % grid.Width, _cells[i] / grid.Width));
        if (goalOnCell)
            _raw.Add(new Vector3(goal.X, grid.GetHeight(goalX, goalZ), goal.Z));
        else if (_cells.Count > 1 || startOnCell)
            _raw.Add(grid.CellToWorld(goalX, goalZ));

        if (smooth)
            Smooth(grid, _raw, path, SmoothingLookahead);
        else
            path.AddRange(_raw);

        return NavPathResult.Success;
    }

    /// <summary>
    /// A* between two cells. Writes cell indices (<see cref="NavGrid.IndexOf"/>) from start to goal.
    /// </summary>
    public NavPathResult FindCellPath(NavGrid grid, int startX, int startZ, int goalX, int goalZ, List<int> cells)
    {
        ArgumentNullException.ThrowIfNull(grid);
        ArgumentNullException.ThrowIfNull(cells);
        cells.Clear();
        LastExpandedCount = 0;

        if (!grid.IsWalkable(startX, startZ))
            return NavPathResult.InvalidStart;
        if (!grid.IsWalkable(goalX, goalZ))
            return NavPathResult.InvalidGoal;
        if (!grid.IsReachable(startX, startZ, goalX, goalZ))
            return NavPathResult.Unreachable;

        int width = grid.Width;
        int start = grid.IndexOf(startX, startZ);
        int goal = grid.IndexOf(goalX, goalZ);
        if (start == goal)
        {
            cells.Add(start);
            return NavPathResult.Success;
        }

        Prepare(grid.CellCount);
        _cost[start] = 0f;
        _parent[start] = -1;
        _openStamp[start] = _stamp;
        Push(start, Heuristic(startX, startZ, goalX, goalZ));

        while (_heapCount > 0)
        {
            int current = Pop();
            if (_closedStamp[current] == _stamp)
                continue; // Stale duplicate left behind by a cheaper re-insert.

            if (current == goal)
            {
                for (int node = goal; node != -1; node = _parent[node])
                    cells.Add(node);
                cells.Reverse();
                return NavPathResult.Success;
            }

            _closedStamp[current] = _stamp;
            LastExpandedCount++;
            int cx = current % width;
            int cz = current / width;
            float baseCost = _cost[current];

            for (int dz = -1; dz <= 1; dz++)
            {
                for (int dx = -1; dx <= 1; dx++)
                {
                    if ((dx == 0 && dz == 0) || !grid.CanTraverse(cx, cz, dx, dz))
                        continue;

                    int next = current + dz * width + dx;
                    if (_closedStamp[next] == _stamp)
                        continue;

                    float cost = baseCost + (dx != 0 && dz != 0 ? Diagonal : 1f);
                    if (_openStamp[next] == _stamp && cost >= _cost[next])
                        continue;

                    _openStamp[next] = _stamp;
                    _cost[next] = cost;
                    _parent[next] = current;
                    Push(next, cost + Heuristic(cx + dx, cz + dz, goalX, goalZ));
                }
            }
        }

        return NavPathResult.Unreachable;
    }

    /// <summary>
    /// String pulling: from each kept point, jump to the furthest later point (within
    /// <paramref name="lookahead"/>) that is in line of sight. The next input point is always
    /// accepted, so the result is never worse than the input.
    /// </summary>
    public static void Smooth(NavGrid grid, IReadOnlyList<Vector3> points, List<Vector3> output, int lookahead = 64)
    {
        ArgumentNullException.ThrowIfNull(grid);
        ArgumentNullException.ThrowIfNull(points);
        ArgumentNullException.ThrowIfNull(output);
        output.Clear();
        if (points.Count == 0)
            return;

        lookahead = Math.Max(1, lookahead);
        int anchor = 0;
        output.Add(points[0]);
        while (anchor < points.Count - 1)
        {
            int next = anchor + 1;
            int furthest = (int)Math.Min(points.Count - 1L, anchor + (long)lookahead);
            for (int candidate = furthest; candidate > next; candidate--)
            {
                if (grid.HasLineOfSight(points[anchor], points[candidate]))
                {
                    next = candidate;
                    break;
                }
            }

            output.Add(points[next]);
            anchor = next;
        }
    }

    /// <summary>Planar (XZ) length of a polyline.</summary>
    public static float PathLength(IReadOnlyList<Vector3> path)
    {
        ArgumentNullException.ThrowIfNull(path);
        float length = 0f;
        for (int i = 1; i < path.Count; i++)
            length += NavMath.PlanarDistance(path[i - 1], path[i]);
        return length;
    }

    private static float Heuristic(int x, int z, int goalX, int goalZ)
    {
        int dx = Math.Abs(goalX - x);
        int dz = Math.Abs(goalZ - z);
        return dx > dz ? dx + (Diagonal - 1f) * dz : dz + (Diagonal - 1f) * dx;
    }

    private void Prepare(int cellCount)
    {
        if (_cost.Length < cellCount)
        {
            _cost = new float[cellCount];
            _parent = new int[cellCount];
            _openStamp = new int[cellCount];
            _closedStamp = new int[cellCount];
            _stamp = 0;
        }

        if (_stamp == int.MaxValue)
        {
            Array.Clear(_openStamp);
            Array.Clear(_closedStamp);
            _stamp = 0;
        }

        _stamp++;
        _heapCount = 0;
    }

    private void Push(int node, float priority)
    {
        if (_heapCount == _heapNode.Length)
        {
            Array.Resize(ref _heapNode, _heapCount * 2);
            Array.Resize(ref _heapPriority, _heapCount * 2);
        }

        int i = _heapCount++;
        while (i > 0)
        {
            int parent = (i - 1) >> 1;
            if (_heapPriority[parent] <= priority)
                break;
            _heapNode[i] = _heapNode[parent];
            _heapPriority[i] = _heapPriority[parent];
            i = parent;
        }

        _heapNode[i] = node;
        _heapPriority[i] = priority;
    }

    private int Pop()
    {
        int top = _heapNode[0];
        _heapCount--;
        if (_heapCount > 0)
        {
            int node = _heapNode[_heapCount];
            float priority = _heapPriority[_heapCount];
            int i = 0;
            while (true)
            {
                int child = 2 * i + 1;
                if (child >= _heapCount)
                    break;
                if (child + 1 < _heapCount && _heapPriority[child + 1] < _heapPriority[child])
                    child++;
                if (_heapPriority[child] >= priority)
                    break;
                _heapNode[i] = _heapNode[child];
                _heapPriority[i] = _heapPriority[child];
                i = child;
            }

            _heapNode[i] = node;
            _heapPriority[i] = priority;
        }

        return top;
    }
}

internal static class NavMath
{
    public static float PlanarDistanceSquared(Vector3 a, Vector3 b)
    {
        float dx = a.X - b.X;
        float dz = a.Z - b.Z;
        return dx * dx + dz * dz;
    }

    public static float PlanarDistance(Vector3 a, Vector3 b) => MathF.Sqrt(PlanarDistanceSquared(a, b));

    public static Vector3 MoveTowards(Vector3 current, Vector3 target, float maxDelta)
    {
        var delta = target - current;
        float length = delta.Length();
        if (length <= maxDelta || length < 1e-6f)
            return target;
        return current + delta * (maxDelta / length);
    }
}
