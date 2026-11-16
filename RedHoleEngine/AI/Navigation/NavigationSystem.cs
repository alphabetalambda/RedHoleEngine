using System.Numerics;
using RedHoleEngine.Components;
using RedHoleEngine.Core.ECS;
using RedHoleEngine.Physics;

namespace RedHoleEngine.AI.Navigation;

/// <summary>
/// Plans paths for every <see cref="NavAgentComponent"/> and walks the entity along them.
/// Runs after <see cref="BehaviorTreeSystem"/> so an order given by the tree this frame
/// moves the body this frame.
/// </summary>
/// <remarks>
/// Steering is "arrive" (brake so the agent stops at the goal) plus a short-range push
/// away from overlapping agents. How the entity is moved depends on what else it has:
/// a dynamic <see cref="RigidBodyComponent"/> gets its horizontal velocity set and physics
/// moves it; anything else (no body, kinematic, static) has its transform moved directly,
/// which <see cref="PhysicsSystem"/> copies onto a kinematic body.
/// </remarks>
public class NavigationSystem : GameSystem
{
    private const float MinArrivalDistance = 0.01f;
    private const float MinApproachSpeed = 0.2f;

    private struct Neighbor
    {
        public int EntityId;
        public Vector3 Position;
        public float Radius;
    }

    private readonly List<Neighbor> _neighbors = new();

    public override int Priority => 250;

    /// <summary>The grid agents plan on. Agents with a destination fail as unreachable while this is null.</summary>
    public NavGrid? Grid { get; set; }

    public NavPathfinder Pathfinder { get; } = new();

    /// <summary>Agent-agent separation. It compares every pair of agents, so turn it off for large crowds.</summary>
    public bool EnableSeparation { get; set; } = true;

    /// <summary>The world's navigation system, or null. Unlike <c>World.GetSystem</c> this does not allocate.</summary>
    public static NavigationSystem? Find(World world)
    {
        ArgumentNullException.ThrowIfNull(world);
        var systems = world.Systems;
        for (int i = 0; i < systems.Count; i++)
        {
            if (systems[i] is NavigationSystem navigation)
                return navigation;
        }

        return null;
    }

    /// <summary>
    /// Sets an agent's destination and plans the path now. Returns false, with the agent
    /// marked <see cref="NavAgentStatus.Unreachable"/>, when there is no path.
    /// </summary>
    public bool SetDestination(Entity entity, Vector3 destination)
    {
        if (!World.HasComponent<NavAgentComponent>(entity) || !World.HasComponent<TransformComponent>(entity))
            return false;

        ref var agent = ref World.GetComponent<NavAgentComponent>(entity);
        agent.SetDestination(destination);
        return Plan(ref agent, World.GetComponent<TransformComponent>(entity).Position);
    }

    public override void Update(float deltaTime)
    {
        if (deltaTime <= 0f)
            return;

        var grid = Grid;
        int gridVersion = grid?.Version ?? -1;

        _neighbors.Clear();
        if (EnableSeparation)
        {
            foreach (var entity in World.Query<NavAgentComponent, TransformComponent>())
            {
                ref var other = ref World.GetComponent<NavAgentComponent>(entity);
                if (!other.Enabled)
                    continue;
                _neighbors.Add(new Neighbor
                {
                    EntityId = entity.Id,
                    Position = World.GetComponent<TransformComponent>(entity).Position,
                    Radius = other.Radius
                });
            }
        }

        foreach (var entity in World.Query<NavAgentComponent, TransformComponent>())
        {
            ref var agent = ref World.GetComponent<NavAgentComponent>(entity);
            if (!agent.Enabled)
                continue;

            ref var transform = ref World.GetComponent<TransformComponent>(entity);
            var position = transform.Position;

            if (agent.HasDestination)
            {
                bool stale = agent.PathGridVersion != gridVersion
                    && (agent.Status == NavAgentStatus.Moving || agent.Status == NavAgentStatus.Unreachable);
                if (agent.Status == NavAgentStatus.Pending || stale)
                    Plan(ref agent, position);
            }

            // Path following: where to head and how fast.
            var desired = Vector3.Zero;
            var pathDirection = Vector3.Zero;
            float distanceToWaypoint = 0f;
            bool onLastLeg = false;
            if (agent.Status == NavAgentStatus.Moving)
            {
                desired = Follow(ref agent, position, deltaTime, out pathDirection, out distanceToWaypoint, out onLastLeg);
                if (agent.Status == NavAgentStatus.Moving && EnableSeparation && agent.SeparationWeight > 0f)
                {
                    desired += Separation(entity.Id, position, agent.Radius) * (agent.SeparationWeight * agent.Speed);
                    float length = desired.Length();
                    if (length > agent.Speed && length > 0f)
                        desired *= agent.Speed / length;
                }
            }

            var previousVelocity = agent.Velocity;
            if (agent.Status == NavAgentStatus.Arrived)
                agent.Velocity = Vector3.Zero;
            else if (agent.Acceleration > 0f)
                agent.Velocity = NavMath.MoveTowards(agent.Velocity, desired, agent.Acceleration * deltaTime);
            else
                agent.Velocity = desired;

            if (TryDriveDynamicBody(entity, agent.Velocity, previousVelocity))
                continue;

            if (agent.Velocity.LengthSquared() <= 1e-10f)
            {
                agent.Velocity = Vector3.Zero;
                continue;
            }

            float speed = agent.Velocity.Length();
            var next = position + agent.Velocity * deltaTime;
            bool moving = agent.Status == NavAgentStatus.Moving;

            if (moving && onLastLeg && speed * deltaTime >= distanceToWaypoint)
            {
                // Do not step past the goal.
                next = agent.Path![agent.PathIndex];
                agent.Status = NavAgentStatus.Arrived;
                agent.Velocity = Vector3.Zero;
            }
            else if (grid != null && grid.IsWalkable(position) && !grid.IsWalkable(next))
            {
                if (moving)
                {
                    // Separation or momentum is pushing off the grid: go straight down the path instead.
                    float step = MathF.Min(speed * deltaTime, distanceToWaypoint);
                    next = position + pathDirection * step;
                    agent.Velocity = pathDirection * speed;
                }
                else
                {
                    next = position;
                    agent.Velocity = Vector3.Zero;
                }
            }

            if (agent.SnapToGround && grid != null)
                next.Y = grid.SampleHeight(next) + agent.BaseOffset;
            else
                next.Y = position.Y;

            transform.Position = next;

            if (agent.FaceMovement && speed > 0.05f)
            {
                var heading = agent.Velocity.LengthSquared() > 1e-8f ? agent.Velocity / agent.Velocity.Length() : pathDirection;
                if (heading.LengthSquared() > 1e-8f)
                {
                    // Engine forward is -Z.
                    float yaw = MathF.Atan2(-heading.X, -heading.Z);
                    transform.Rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, yaw);
                }
            }
        }
    }

    private bool Plan(ref NavAgentComponent agent, Vector3 position)
    {
        var path = agent.Path ??= new List<Vector3>();
        var grid = Grid;
        agent.PathIndex = 0;
        agent.PathGridVersion = grid?.Version ?? -1;

        if (grid == null || Pathfinder.FindPath(grid, position, agent.Destination, path) != NavPathResult.Success)
        {
            path.Clear();
            agent.Status = NavAgentStatus.Unreachable;
            return false;
        }

        agent.PathIndex = path.Count > 1 ? 1 : 0;
        bool alreadyThere = NavMath.PlanarDistance(position, path[^1])
            <= MathF.Max(agent.StoppingDistance, MinArrivalDistance);
        agent.Status = alreadyThere ? NavAgentStatus.Arrived : NavAgentStatus.Moving;
        return true;
    }

    /// <summary>Advances the waypoint cursor and returns the desired velocity along the path.</summary>
    private static Vector3 Follow(
        ref NavAgentComponent agent,
        Vector3 position,
        float deltaTime,
        out Vector3 direction,
        out float distance,
        out bool onLastLeg)
    {
        direction = Vector3.Zero;
        distance = 0f;
        onLastLeg = true;

        var path = agent.Path;
        if (path == null || path.Count == 0)
        {
            agent.Status = NavAgentStatus.Arrived;
            return Vector3.Zero;
        }

        // A waypoint counts as reached when the agent is on it or has crossed the line through it.
        float tolerance = MathF.Max(0.05f, agent.Speed * deltaTime);
        int index = Math.Clamp(agent.PathIndex, 0, path.Count - 1);
        while (index < path.Count - 1)
        {
            var waypoint = path[index];
            bool reached = NavMath.PlanarDistanceSquared(position, waypoint) <= tolerance * tolerance;
            if (!reached && index > 0)
            {
                var previous = path[index - 1];
                reached = (waypoint.X - previous.X) * (position.X - waypoint.X)
                    + (waypoint.Z - previous.Z) * (position.Z - waypoint.Z) >= 0f;
            }

            if (!reached)
                break;
            index++;
        }

        agent.PathIndex = index;
        onLastLeg = index == path.Count - 1;

        var offset = path[index] - position;
        offset.Y = 0f;
        distance = offset.Length();
        if (onLastLeg && distance <= MathF.Max(agent.StoppingDistance, MinArrivalDistance))
        {
            agent.Status = NavAgentStatus.Arrived;
            return Vector3.Zero;
        }

        if (distance > 1e-6f)
            direction = offset / distance;

        // Arrive: never faster than the speed from which the agent can still brake in time.
        float speed = agent.Speed;
        if (agent.Acceleration > 0f)
        {
            float room = MathF.Max(0f, agent.RemainingDistance(position) - agent.StoppingDistance);
            float braking = MathF.Sqrt(2f * agent.Acceleration * room);
            speed = MathF.Min(speed, MathF.Max(braking, MathF.Min(agent.Speed, MinApproachSpeed)));
        }

        return direction * speed;
    }

    private Vector3 Separation(int entityId, Vector3 position, float radius)
    {
        var push = Vector3.Zero;
        for (int i = 0; i < _neighbors.Count; i++)
        {
            var other = _neighbors[i];
            if (other.EntityId == entityId)
                continue;

            float range = radius + other.Radius;
            float dx = position.X - other.Position.X;
            float dz = position.Z - other.Position.Z;
            float distanceSquared = dx * dx + dz * dz;
            if (range <= 0f || distanceSquared >= range * range)
                continue;

            float distance = MathF.Sqrt(distanceSquared);
            Vector3 away;
            if (distance > 1e-4f)
            {
                away = new Vector3(dx / distance, 0f, dz / distance);
            }
            else
            {
                // Exactly stacked: pick a stable direction per entity so the pair splits.
                float angle = entityId * 2.3999631f;
                away = new Vector3(MathF.Cos(angle), 0f, MathF.Sin(angle));
            }

            push += away * (1f - distance / range);
        }

        return push;
    }

    /// <summary>
    /// Hands the velocity to physics when the entity is a dynamic rigid body.
    /// Returns false when the caller should move the transform itself.
    /// </summary>
    private bool TryDriveDynamicBody(Entity entity, Vector3 velocity, Vector3 previousVelocity)
    {
        if (!World.HasComponent<RigidBodyComponent>(entity))
            return false;

        ref var rigidBody = ref World.GetComponent<RigidBodyComponent>(entity);
        if (rigidBody.Type != RigidBodyType.Dynamic)
            return false;

        // Leave a resting body alone so physics can still push it around.
        var body = rigidBody.Body;
        bool active = velocity.LengthSquared() > 0f || previousVelocity.LengthSquared() > 0f;
        if (body != null && active)
        {
            body.LinearVelocity = new Vector3(velocity.X, body.LinearVelocity.Y, velocity.Z);
            if (velocity.LengthSquared() > 0f)
                body.Wake();
        }

        return true;
    }
}
