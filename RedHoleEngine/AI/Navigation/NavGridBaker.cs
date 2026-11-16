using System.Numerics;
using RedHoleEngine.Physics;
using RedHoleEngine.Physics.Collision;

namespace RedHoleEngine.AI.Navigation;

/// <summary>What the baker treats as ground and as an obstacle.</summary>
public sealed class NavBakeSettings
{
    /// <summary>Obstacles closer than this to a cell center block the cell.</summary>
    public float AgentRadius { get; set; } = 0.4f;

    /// <summary>Headroom an agent needs above the ground.</summary>
    public float AgentHeight { get; set; } = 1.8f;

    /// <summary>Extra clearance on top of <see cref="AgentRadius"/>. Raise it when cells are much larger than the agent.</summary>
    public float Padding { get; set; }

    /// <summary>Ledges up to this height are walked over; anything taller separates cells. Copied to <see cref="NavGrid.MaxStepHeight"/>.</summary>
    public float MaxStepHeight { get; set; } = 0.4f;

    /// <summary>Ground steeper than this is not walkable.</summary>
    public float MaxSlopeDegrees { get; set; } = 45f;

    /// <summary>Ground rays start this far above the grid origin. Keep it below a ceiling, or the ceiling becomes the ground.</summary>
    public float ScanHeight { get; set; } = 20f;

    /// <summary>Ground rays reach this far below the grid origin.</summary>
    public float ScanDepth { get; set; } = 20f;

    /// <summary>When false, a cell with no collider under it is walkable at the grid origin height.</summary>
    public bool RequireGround { get; set; } = true;

    public bool IncludeStatic { get; set; } = true;
    public bool IncludeKinematic { get; set; }
    public bool IncludeDynamic { get; set; }

    /// <summary>Only bodies whose collision layer intersects this mask are considered.</summary>
    public uint LayerMask { get; set; } = uint.MaxValue;
}

/// <summary>
/// Fills a <see cref="NavGrid"/> from the colliders in a <see cref="PhysicsWorld"/>.
/// For every cell: a downward raycast finds the ground (the highest surface under the cell
/// center), then overlap spheres stacked over that point check that the agent fits.
/// </summary>
/// <remarks>
/// Colliders reach the physics world when <see cref="PhysicsSystem"/> registers their entity,
/// so bake after the first physics update, not straight after creating the entities.
/// Capsule colliders are seen by the overlap test but not by raycasts, so they block cells
/// and are never ground. Triggers are ignored.
/// The cells under an obstacle's footprint take the height of its top. They stay walkable but
/// are cut off from the floor by <see cref="NavBakeSettings.MaxStepHeight"/>, so ask
/// <see cref="NavGrid.IsReachable"/> (or a path) rather than <see cref="NavGrid.IsWalkable(int,int)"/>
/// whether an agent can get somewhere.
/// </remarks>
public static class NavGridBaker
{
    private const float Skin = 0.02f;

    /// <summary>Creates a grid covering the XZ extent of <paramref name="min"/>..<paramref name="max"/> and bakes it.</summary>
    public static NavGrid Bake(PhysicsWorld physics, Vector3 min, Vector3 max, float cellSize, NavBakeSettings? settings = null)
    {
        if (!(cellSize > 0f))
            throw new ArgumentOutOfRangeException(nameof(cellSize), "Cell size must be positive.");

        int width = Math.Max(1, (int)MathF.Ceiling((max.X - min.X) / cellSize - 1e-4f));
        int depth = Math.Max(1, (int)MathF.Ceiling((max.Z - min.Z) / cellSize - 1e-4f));
        var grid = new NavGrid(new Vector3(min.X, min.Y, min.Z), width, depth, cellSize, walkable: false);
        Bake(grid, physics, settings);
        return grid;
    }

    /// <summary>Rebakes every cell of an existing grid. Returns the number of walkable cells.</summary>
    public static int Bake(NavGrid grid, PhysicsWorld physics, NavBakeSettings? settings = null)
    {
        ArgumentNullException.ThrowIfNull(grid);
        ArgumentNullException.ThrowIfNull(physics);
        settings ??= new NavBakeSettings();

        float radius = MathF.Max(0.01f, settings.AgentRadius + settings.Padding);
        float minNormalY = MathF.Cos(Math.Clamp(settings.MaxSlopeDegrees, 0f, 89.9f) * (MathF.PI / 180f)) - 1e-4f;
        float top = grid.Origin.Y + MathF.Max(0f, settings.ScanHeight);
        float rayLength = MathF.Max(0f, settings.ScanHeight) + MathF.Max(0f, settings.ScanDepth);

        int walkableCount = 0;
        for (int z = 0; z < grid.Depth; z++)
        {
            for (int x = 0; x < grid.Width; x++)
            {
                float worldX = grid.Origin.X + (x + 0.5f) * grid.CellSize;
                float worldZ = grid.Origin.Z + (z + 0.5f) * grid.CellSize;

                float ground = grid.Origin.Y;
                Collider? groundCollider = null;
                bool walkable = !settings.RequireGround;

                if (rayLength > 0f
                    && TryFindGround(physics, new Vector3(worldX, top, worldZ), rayLength, settings, out var hit))
                {
                    ground = hit.Point.Y;
                    groundCollider = hit.Collider;
                    // Slope only: a surface normal seen from above points up, whichever way the
                    // raycast reports it (the box raycast returns it facing along the ray).
                    walkable = MathF.Abs(hit.Normal.Y) >= minNormalY;
                }

                if (walkable)
                    walkable = HasClearance(physics, new Vector3(worldX, ground, worldZ), radius, settings, groundCollider);

                grid.SetCellUnchecked(grid.IndexOf(x, z), walkable, ground);
                if (walkable)
                    walkableCount++;
            }
        }

        // Also bumps the grid version, which invalidates agent paths and region labels.
        grid.MaxStepHeight = settings.MaxStepHeight;
        grid.MarkChanged();
        return walkableCount;
    }

    private static bool TryFindGround(
        PhysicsWorld physics,
        Vector3 origin,
        float maxDistance,
        NavBakeSettings settings,
        out CollisionDetection.RaycastHit ground)
    {
        // Hits come back nearest first, i.e. highest surface first for a downward ray.
        var hits = physics.RaycastAll(origin, -Vector3.UnitY, maxDistance);
        for (int i = 0; i < hits.Count; i++)
        {
            var hit = hits[i];
            if (hit.Collider is { IsTrigger: true } || !Accepts(settings, hit.Body))
                continue;

            ground = hit;
            return true;
        }

        ground = default;
        return false;
    }

    private static bool HasClearance(
        PhysicsWorld physics,
        Vector3 groundPoint,
        float radius,
        NavBakeSettings settings,
        Collider? groundCollider)
    {
        // The column starts above anything low enough to step over.
        float bottom = groundPoint.Y + MathF.Max(settings.MaxStepHeight, Skin) + radius;
        float topCenter = groundPoint.Y + settings.AgentHeight - radius;
        int count = topCenter > bottom ? (int)MathF.Ceiling((topCenter - bottom) / radius) + 1 : 1;
        float spacing = count > 1 ? (topCenter - bottom) / (count - 1) : 0f;

        for (int i = 0; i < count; i++)
        {
            var center = new Vector3(groundPoint.X, bottom + spacing * i, groundPoint.Z);
            var overlaps = physics.OverlapSphere(center, radius);
            for (int c = 0; c < overlaps.Count; c++)
            {
                var collider = overlaps[c];
                if (collider.IsTrigger || ReferenceEquals(collider, groundCollider))
                    continue;
                if (Accepts(settings, collider.Body))
                    return false;
            }
        }

        return true;
    }

    private static bool Accepts(NavBakeSettings settings, RigidBody? body)
    {
        if (body == null)
            return false;
        if ((body.CollisionLayer & settings.LayerMask) == 0)
            return false;

        return body.Type switch
        {
            RigidBodyType.Static => settings.IncludeStatic,
            RigidBodyType.Kinematic => settings.IncludeKinematic,
            _ => settings.IncludeDynamic
        };
    }
}
