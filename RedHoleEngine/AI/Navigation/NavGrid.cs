using System.Numerics;

namespace RedHoleEngine.AI.Navigation;

/// <summary>
/// 2.5D navigation grid: a uniform grid on the XZ plane where every cell is either
/// walkable or blocked and carries one ground height.
/// </summary>
/// <remarks>
/// One height per cell covers flat floors, ramps and steps (see <see cref="MaxStepHeight"/>)
/// but not floors stacked above each other. Use one grid per storey for that.
/// Build it by hand with <see cref="SetWalkable(int,int,bool)"/> or bake it from colliders
/// with <see cref="NavGridBaker"/>.
/// </remarks>
public sealed class NavGrid
{
    private readonly bool[] _walkable;
    private readonly float[] _height;
    private int[]? _regions;
    private int[]? _floodStack;
    private int _regionVersion = -1;
    private float _maxStepHeight = 0.5f;

    /// <param name="origin">World position of the minimum (X, Z) corner. Y is the default ground height.</param>
    /// <param name="width">Cell count along X.</param>
    /// <param name="depth">Cell count along Z.</param>
    /// <param name="cellSize">Edge length of a cell in world units.</param>
    /// <param name="walkable">Initial state of every cell.</param>
    public NavGrid(Vector3 origin, int width, int depth, float cellSize, bool walkable = true)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(depth);
        if (!(cellSize > 0f) || !float.IsFinite(cellSize))
            throw new ArgumentOutOfRangeException(nameof(cellSize), "Cell size must be positive.");

        Origin = origin;
        Width = width;
        Depth = depth;
        CellSize = cellSize;
        _walkable = new bool[width * depth];
        _height = new float[width * depth];
        Array.Fill(_walkable, walkable);
        Array.Fill(_height, origin.Y);
    }

    public Vector3 Origin { get; }
    public int Width { get; }
    public int Depth { get; }
    public float CellSize { get; }
    public int CellCount => _walkable.Length;

    /// <summary>Largest ground-height difference an agent can cross between neighbouring cells.</summary>
    public float MaxStepHeight
    {
        get => _maxStepHeight;
        set
        {
            _maxStepHeight = MathF.Max(0f, value);
            Version++;
        }
    }

    /// <summary>Bumped on every edit. Agents use it to notice that their path is stale.</summary>
    public int Version { get; private set; }

    public bool InBounds(int x, int z) => (uint)x < (uint)Width && (uint)z < (uint)Depth;

    public int IndexOf(int x, int z) => z * Width + x;

    /// <summary>False for blocked cells and for anything outside the grid.</summary>
    public bool IsWalkable(int x, int z) => InBounds(x, z) && _walkable[z * Width + x];

    public bool IsWalkable(Vector3 worldPosition)
        => WorldToCell(worldPosition, out int x, out int z) && _walkable[z * Width + x];

    public void SetWalkable(int x, int z, bool walkable)
    {
        if (!InBounds(x, z))
            throw new ArgumentOutOfRangeException(nameof(x), $"Cell ({x}, {z}) is outside the grid.");
        _walkable[z * Width + x] = walkable;
        Version++;
    }

    /// <summary>Sets every cell whose center lies inside the world-space XZ rectangle.</summary>
    public void SetWalkable(Vector3 min, Vector3 max, bool walkable)
    {
        for (int z = 0; z < Depth; z++)
        {
            for (int x = 0; x < Width; x++)
            {
                float cx = Origin.X + (x + 0.5f) * CellSize;
                float cz = Origin.Z + (z + 0.5f) * CellSize;
                if (cx >= min.X && cx <= max.X && cz >= min.Z && cz <= max.Z)
                    _walkable[z * Width + x] = walkable;
            }
        }

        Version++;
    }

    public void Fill(bool walkable)
    {
        Array.Fill(_walkable, walkable);
        Version++;
    }

    public float GetHeight(int x, int z) => InBounds(x, z) ? _height[z * Width + x] : Origin.Y;

    public void SetHeight(int x, int z, float height)
    {
        if (!InBounds(x, z))
            throw new ArgumentOutOfRangeException(nameof(x), $"Cell ({x}, {z}) is outside the grid.");
        _height[z * Width + x] = height;
        Version++;
    }

    /// <summary>Writes a cell without a bounds check or a version bump. Used by the baker.</summary>
    internal void SetCellUnchecked(int index, bool walkable, float height)
    {
        _walkable[index] = walkable;
        _height[index] = height;
    }

    internal void MarkChanged() => Version++;

    /// <summary>Cell containing a world position. Returns false (with clamped coordinates) when it is off the grid.</summary>
    public bool WorldToCell(Vector3 worldPosition, out int x, out int z)
    {
        int rawX = (int)MathF.Floor((worldPosition.X - Origin.X) / CellSize);
        int rawZ = (int)MathF.Floor((worldPosition.Z - Origin.Z) / CellSize);
        x = Math.Clamp(rawX, 0, Width - 1);
        z = Math.Clamp(rawZ, 0, Depth - 1);
        return rawX == x && rawZ == z;
    }

    /// <summary>World position of a cell center, at the cell's ground height.</summary>
    public Vector3 CellToWorld(int x, int z)
    {
        return new Vector3(
            Origin.X + (x + 0.5f) * CellSize,
            GetHeight(x, z),
            Origin.Z + (z + 0.5f) * CellSize);
    }

    /// <summary>Ground height under a world position (clamped onto the grid when outside it).</summary>
    public float SampleHeight(Vector3 worldPosition)
    {
        WorldToCell(worldPosition, out int x, out int z);
        return _height[z * Width + x];
    }

    /// <summary>
    /// Whether an agent can go from a cell to one of its eight neighbours.
    /// Diagonal moves need both orthogonal neighbours free, so a path never clips a corner.
    /// </summary>
    public bool CanTraverse(int x, int z, int dx, int dz)
    {
        if (!IsWalkable(x, z))
            return false;

        if (dx != 0 && dz != 0)
            return CanStep(x, z, x + dx, z) && CanStep(x, z, x, z + dz) && CanStep(x, z, x + dx, z + dz);

        return CanStep(x, z, x + dx, z + dz);
    }

    private bool CanStep(int fromX, int fromZ, int toX, int toZ)
    {
        if (!IsWalkable(toX, toZ))
            return false;
        return MathF.Abs(_height[toZ * Width + toX] - _height[fromZ * Width + fromX]) <= _maxStepHeight;
    }

    /// <summary>
    /// Nearest walkable cell to a world position, searching outward up to
    /// <paramref name="maxDistance"/> world units. Positions off the grid are clamped onto it first.
    /// </summary>
    public bool TryFindNearestWalkable(Vector3 worldPosition, float maxDistance, out int cellX, out int cellZ)
    {
        WorldToCell(worldPosition, out int cx, out int cz);
        cellX = cx;
        cellZ = cz;
        if (_walkable[cz * Width + cx])
            return true;

        int maxRing = Math.Min((int)MathF.Ceiling(MathF.Max(0f, maxDistance) / CellSize), Math.Max(Width, Depth));
        float best = float.MaxValue;
        bool found = false;
        int lastRing = maxRing;
        for (int ring = 1; ring <= lastRing; ring++)
        {
            for (int z = cz - ring; z <= cz + ring; z++)
            {
                bool edgeRow = z == cz - ring || z == cz + ring;
                int step = edgeRow ? 1 : 2 * ring;
                for (int x = cx - ring; x <= cx + ring; x += step)
                {
                    if (!IsWalkable(x, z))
                        continue;

                    float ddx = Origin.X + (x + 0.5f) * CellSize - worldPosition.X;
                    float ddz = Origin.Z + (z + 0.5f) * CellSize - worldPosition.Z;
                    float distance = ddx * ddx + ddz * ddz;
                    if (distance < best)
                    {
                        best = distance;
                        cellX = x;
                        cellZ = z;
                    }
                }
            }

            // A square ring is not a circle: one more ring can still hold a closer cell.
            if (best < float.MaxValue && !found)
            {
                found = true;
                lastRing = Math.Min(maxRing, ring + 1);
            }
        }

        return found;
    }

    /// <summary>
    /// Snaps a world position onto the walkable area. A position already on a walkable cell
    /// keeps its X and Z; otherwise the nearest walkable cell center is returned.
    /// </summary>
    public bool TrySamplePosition(Vector3 worldPosition, float maxDistance, out Vector3 result)
    {
        if (WorldToCell(worldPosition, out int x, out int z) && _walkable[z * Width + x])
        {
            result = new Vector3(worldPosition.X, _height[z * Width + x], worldPosition.Z);
            return true;
        }

        if (TryFindNearestWalkable(worldPosition, maxDistance, out x, out z))
        {
            result = CellToWorld(x, z);
            return true;
        }

        result = worldPosition;
        return false;
    }

    /// <summary>
    /// Whether a straight walk from <paramref name="from"/> to <paramref name="to"/> stays on
    /// walkable cells and never crosses a step higher than <see cref="MaxStepHeight"/>.
    /// Every cell the segment touches is visited; passing exactly through a cell corner
    /// requires both cells beside that corner to be free.
    /// </summary>
    public bool HasLineOfSight(Vector3 from, Vector3 to)
    {
        float ax = (from.X - Origin.X) / CellSize;
        float az = (from.Z - Origin.Z) / CellSize;
        float bx = (to.X - Origin.X) / CellSize;
        float bz = (to.Z - Origin.Z) / CellSize;

        int x = (int)MathF.Floor(ax);
        int z = (int)MathF.Floor(az);
        int endX = (int)MathF.Floor(bx);
        int endZ = (int)MathF.Floor(bz);
        if (!IsWalkable(x, z) || !IsWalkable(endX, endZ))
            return false;

        float dx = bx - ax;
        float dz = bz - az;
        int stepX = dx > 0f ? 1 : dx < 0f ? -1 : 0;
        int stepZ = dz > 0f ? 1 : dz < 0f ? -1 : 0;
        float deltaX = stepX != 0 ? 1f / MathF.Abs(dx) : float.PositiveInfinity;
        float deltaZ = stepZ != 0 ? 1f / MathF.Abs(dz) : float.PositiveInfinity;
        float nextX = stepX > 0 ? (x + 1 - ax) * deltaX : stepX < 0 ? (ax - x) * deltaX : float.PositiveInfinity;
        float nextZ = stepZ > 0 ? (z + 1 - az) * deltaZ : stepZ < 0 ? (az - z) * deltaZ : float.PositiveInfinity;

        const float cornerEpsilon = 1e-5f;
        int guard = Width + Depth + 4;
        while ((x != endX || z != endZ) && guard-- > 0)
        {
            if (MathF.Min(nextX, nextZ) > 1f + cornerEpsilon)
                return false; // Rounding walked past the end cell; be conservative.

            if (MathF.Abs(nextX - nextZ) <= cornerEpsilon)
            {
                if (!CanTraverse(x, z, stepX, stepZ))
                    return false;
                x += stepX;
                z += stepZ;
                nextX += deltaX;
                nextZ += deltaZ;
            }
            else if (nextX < nextZ)
            {
                if (!CanStep(x, z, x + stepX, z))
                    return false;
                x += stepX;
                nextX += deltaX;
            }
            else
            {
                if (!CanStep(x, z, x, z + stepZ))
                    return false;
                z += stepZ;
                nextZ += deltaZ;
            }
        }

        return x == endX && z == endZ;
    }

    /// <summary>
    /// Whether two cells belong to the same connected region. Regions are flood-filled once
    /// per grid <see cref="Version"/>, so this answers "is there any path" without a search.
    /// </summary>
    public bool IsReachable(int fromX, int fromZ, int toX, int toZ)
    {
        if (!IsWalkable(fromX, fromZ) || !IsWalkable(toX, toZ))
            return false;

        EnsureRegions();
        return _regions![fromZ * Width + fromX] == _regions[toZ * Width + toX];
    }

    /// <summary>Connected-region id of a cell, or 0 when it is blocked or off the grid.</summary>
    public int GetRegion(int x, int z)
    {
        if (!IsWalkable(x, z))
            return 0;
        EnsureRegions();
        return _regions![z * Width + x];
    }

    private void EnsureRegions()
    {
        if (_regions != null && _regionVersion == Version)
            return;

        _regions ??= new int[_walkable.Length];
        _floodStack ??= new int[_walkable.Length];
        Array.Clear(_regions);

        int region = 0;
        for (int seed = 0; seed < _walkable.Length; seed++)
        {
            if (!_walkable[seed] || _regions[seed] != 0)
                continue;

            region++;
            int count = 0;
            _floodStack[count++] = seed;
            _regions[seed] = region;
            while (count > 0)
            {
                int current = _floodStack[--count];
                int cx = current % Width;
                int cz = current / Width;
                for (int dz = -1; dz <= 1; dz++)
                {
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        if ((dx == 0 && dz == 0) || !CanTraverse(cx, cz, dx, dz))
                            continue;

                        int next = (cz + dz) * Width + cx + dx;
                        if (_regions[next] != 0)
                            continue;
                        _regions[next] = region;
                        _floodStack[count++] = next;
                    }
                }
            }
        }

        _regionVersion = Version;
    }
}
