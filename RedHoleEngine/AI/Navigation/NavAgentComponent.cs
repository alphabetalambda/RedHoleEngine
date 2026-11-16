using System.Numerics;
using RedHoleEngine.Core.ECS;

namespace RedHoleEngine.AI.Navigation;

/// <summary>Where an agent is in the life of a move order.</summary>
public enum NavAgentStatus
{
    /// <summary>No destination.</summary>
    Idle = 0,

    /// <summary>A destination was set and <see cref="NavigationSystem"/> has not planned it yet.</summary>
    Pending = 1,

    /// <summary>Following a path.</summary>
    Moving = 2,

    /// <summary>Within <see cref="NavAgentComponent.StoppingDistance"/> of the end of the path.</summary>
    Arrived = 3,

    /// <summary>No path to the destination exists (or there is no grid to plan on).</summary>
    Unreachable = 4
}

/// <summary>
/// Makes an entity walk to a destination over the <see cref="NavGrid"/> owned by
/// <see cref="NavigationSystem"/>. Needs a <c>TransformComponent</c>.
/// Use <see cref="Create"/>; a default-initialized struct is disabled and has zero speed.
/// </summary>
public struct NavAgentComponent : IComponent
{
    public bool Enabled;

    /// <summary>Top speed in world units per second.</summary>
    public float Speed;

    /// <summary>How fast velocity changes, in units per second squared. Zero or less means instant.</summary>
    public float Acceleration;

    /// <summary>The agent has arrived once it is this close (on the XZ plane) to the end of its path.</summary>
    public float StoppingDistance;

    /// <summary>Body radius used for separation from other agents.</summary>
    public float Radius;

    /// <summary>Strength of the push away from overlapping agents. Zero turns separation off for this agent.</summary>
    public float SeparationWeight;

    /// <summary>Height of the entity origin above the ground, applied when <see cref="SnapToGround"/> is set.</summary>
    public float BaseOffset;

    /// <summary>Keep Y on the grid's ground height. Ignored for dynamic rigid bodies, which have gravity for that.</summary>
    public bool SnapToGround;

    /// <summary>Turn the transform to face the direction of travel. Ignored for dynamic rigid bodies.</summary>
    public bool FaceMovement;

    public Vector3 Destination;
    public bool HasDestination;
    public NavAgentStatus Status;

    /// <summary>Current planar velocity, written by <see cref="NavigationSystem"/>.</summary>
    public Vector3 Velocity;

    /// <summary>Smoothed waypoints of the current path. Owned by this agent and reused between plans.</summary>
    public List<Vector3>? Path;

    /// <summary>Index into <see cref="Path"/> of the waypoint being walked to.</summary>
    public int PathIndex;

    /// <summary><see cref="NavGrid.Version"/> the path was planned against.</summary>
    public int PathGridVersion;

    public static NavAgentComponent Create(
        float speed = 3.5f,
        float acceleration = 20f,
        float stoppingDistance = 0.1f,
        float radius = 0.4f)
    {
        return new NavAgentComponent
        {
            Enabled = true,
            Speed = speed,
            Acceleration = acceleration,
            StoppingDistance = stoppingDistance,
            Radius = radius,
            SeparationWeight = 1f,
            SnapToGround = true,
            FaceMovement = true,
            Status = NavAgentStatus.Idle
        };
    }

    public readonly bool IsMoving => Status == NavAgentStatus.Moving || Status == NavAgentStatus.Pending;

    /// <summary>
    /// Orders the agent to a new destination. The path is planned on the next
    /// <see cref="NavigationSystem"/> update; call <see cref="NavigationSystem.SetDestination"/>
    /// to plan immediately and learn at once whether it is reachable.
    /// </summary>
    public void SetDestination(Vector3 destination)
    {
        Destination = destination;
        HasDestination = true;
        Status = NavAgentStatus.Pending;
    }

    /// <summary>Drops the destination. The agent brakes at <see cref="Acceleration"/>.</summary>
    public void Stop()
    {
        HasDestination = false;
        Status = NavAgentStatus.Idle;
        PathIndex = 0;
        Path?.Clear();
    }

    /// <summary>Planar distance left along the path from <paramref name="position"/>. Zero when not moving.</summary>
    public readonly float RemainingDistance(Vector3 position)
    {
        if (Status != NavAgentStatus.Moving || Path == null || PathIndex >= Path.Count)
            return 0f;

        float distance = NavMath.PlanarDistance(position, Path[PathIndex]);
        for (int i = PathIndex + 1; i < Path.Count; i++)
            distance += NavMath.PlanarDistance(Path[i - 1], Path[i]);
        return distance;
    }
}
