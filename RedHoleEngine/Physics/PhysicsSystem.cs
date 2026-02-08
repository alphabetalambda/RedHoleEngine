using System.Numerics;
using RedHoleEngine.Components;
using RedHoleEngine.Core.ECS;
using RedHoleEngine.Physics.Collision;
using RedHoleEngine.Rendering.Debug;

namespace RedHoleEngine.Physics;

/// <summary>
/// Flags for physics debug visualization
/// </summary>
[Flags]
public enum PhysicsDebugFlags
{
    None = 0,
    Colliders = 1 << 0,
    Contacts = 1 << 1,
    AABB = 1 << 2,
    Velocity = 1 << 3,
    AngularVelocity = 1 << 4,
    CenterOfMass = 1 << 5,
    SleepState = 1 << 6,
    All = ~0
}

/// <summary>
/// ECS system that manages physics simulation.
/// Syncs transform components with rigid bodies and steps the physics world.
/// </summary>
public class PhysicsSystem : GameSystem
{
    private readonly PhysicsWorld _physicsWorld;
    private readonly HashSet<int> _registeredEntities = new();
    
    // Debug visualization
    private DebugDrawManager? _debugDraw;
    private PhysicsDebugFlags _debugFlags = PhysicsDebugFlags.None;

    /// <summary>
    /// The underlying physics world
    /// </summary>
    public PhysicsWorld PhysicsWorld => _physicsWorld;

    /// <summary>
    /// Physics settings (gravity, iterations, etc.)
    /// </summary>
    public PhysicsSettings Settings => _physicsWorld.Settings;

    /// <summary>
    /// Enable/disable debug visualization
    /// </summary>
    public PhysicsDebugFlags DebugFlags
    {
        get => _debugFlags;
        set => _debugFlags = value;
    }

    /// <summary>
    /// Priority for execution order (lower = earlier)
    /// </summary>
    public override int Priority => -100; // Run early in the frame

    public PhysicsSystem()
    {
        _physicsWorld = new PhysicsWorld();
    }

    /// <summary>
    /// Set the debug draw manager for visualization
    /// </summary>
    public void SetDebugDraw(DebugDrawManager? debugDraw)
    {
        _debugDraw = debugDraw;
    }

    protected override void OnInitialize()
    {
        // Subscribe to collision events
        _physicsWorld.OnCollisionEnter += OnCollisionEnter;
        _physicsWorld.OnCollisionExit += OnCollisionExit;
        _physicsWorld.OnTriggerEnter += OnTriggerEnter;
        _physicsWorld.OnTriggerExit += OnTriggerExit;
    }

    public override void Update(float deltaTime)
    {
        if (World == null) return;

        // 1. Register new physics entities
        RegisterNewEntities();

        // 2. Sync transforms from ECS to physics (for kinematic bodies and external changes)
        SyncTransformsToPhysics();

        // 3. Step physics simulation
        _physicsWorld.Step(deltaTime);

        // 4. Sync transforms from physics back to ECS
        SyncPhysicsToTransforms();

        // 5. Draw debug visualization
        if (_debugDraw != null && _debugFlags != PhysicsDebugFlags.None)
        {
            DrawDebugVisualization();
        }
    }

    private void RegisterNewEntities()
    {
        // Find entities with RigidBodyComponent that aren't registered yet
        foreach (var entity in World!.Query<RigidBodyComponent, TransformComponent>())
        {
            if (_registeredEntities.Contains(entity.Id))
                continue;

            RegisterEntity(entity);
        }

        // Also check for entities that might have been destroyed
        var toRemove = new List<int>();
        foreach (var entityId in _registeredEntities)
        {
            var entity = new Entity(entityId, 0); // Generation 0 for lookup
            if (!World.HasComponent<RigidBodyComponent>(entity))
            {
                toRemove.Add(entityId);
            }
        }

        foreach (var entityId in toRemove)
        {
            UnregisterEntity(new Entity(entityId, 0));
        }
    }

    private void RegisterEntity(Entity entity)
    {
        ref var rbComponent = ref World!.GetComponent<RigidBodyComponent>(entity);
        ref var transform = ref World.GetComponent<TransformComponent>(entity);

        // Create the rigid body
        var body = new RigidBody
        {
            EntityId = entity.Id,
            Type = rbComponent.Type,
            Position = transform.Position,
            Rotation = transform.Rotation,
            Mass = rbComponent.Mass > 0 ? rbComponent.Mass : 1f,
            Restitution = rbComponent.Restitution,
            Friction = rbComponent.Friction,
            LinearDamping = rbComponent.LinearDamping,
            AngularDamping = rbComponent.AngularDamping,
            UseGravity = rbComponent.UseGravity,
            FreezePositionX = rbComponent.FreezePositionX,
            FreezePositionY = rbComponent.FreezePositionY,
            FreezePositionZ = rbComponent.FreezePositionZ,
            FreezeRotationX = rbComponent.FreezeRotationX,
            FreezeRotationY = rbComponent.FreezeRotationY,
            FreezeRotationZ = rbComponent.FreezeRotationZ,
            CollisionLayer = rbComponent.CollisionLayer != 0 ? rbComponent.CollisionLayer : 1,
            CollisionMask = rbComponent.CollisionMask != 0 ? rbComponent.CollisionMask : uint.MaxValue
        };

        // Store reference in component
        rbComponent.Body = body;

        // Add to physics world
        _physicsWorld.AddBody(body);

        // Check for collider component
        if (World.HasComponent<ColliderComponent>(entity))
        {
            ref var colliderComponent = ref World.GetComponent<ColliderComponent>(entity);
            var collider = colliderComponent.CreateCollider();
            colliderComponent.Collider = collider;
            _physicsWorld.AddCollider(body, collider);

            // Calculate inertia based on collider shape
            switch (colliderComponent.ShapeType)
            {
                case ColliderType.Sphere:
                    body.CalculateSphereInertia(colliderComponent.SphereRadius);
                    break;
                case ColliderType.Box:
                    body.CalculateBoxInertia(colliderComponent.BoxHalfExtents);
                    break;
                case ColliderType.Capsule:
                    // Approximate as cylinder + spheres
                    body.CalculateSphereInertia(colliderComponent.CapsuleRadius * 1.5f);
                    break;
            }
        }

        _registeredEntities.Add(entity.Id);
    }

    private void UnregisterEntity(Entity entity)
    {
        var body = _physicsWorld.GetBody(entity.Id);
        if (body != null)
        {
            _physicsWorld.RemoveBody(body);
        }
        _registeredEntities.Remove(entity.Id);
    }

    private void SyncTransformsToPhysics()
    {
        foreach (var entity in World!.Query<RigidBodyComponent, TransformComponent>())
        {
            ref var rbComponent = ref World.GetComponent<RigidBodyComponent>(entity);
            ref var transform = ref World.GetComponent<TransformComponent>(entity);

            if (rbComponent.Body == null) continue;

            // For kinematic bodies, always sync from transform
            // For dynamic bodies, only sync if transform was externally modified
            if (rbComponent.Type == RigidBodyType.Kinematic || rbComponent.Type == RigidBodyType.Static)
            {
                rbComponent.Body.Position = transform.Position;
                rbComponent.Body.Rotation = transform.Rotation;
            }
        }
    }

    private void SyncPhysicsToTransforms()
    {
        foreach (var entity in World!.Query<RigidBodyComponent, TransformComponent>())
        {
            ref var rbComponent = ref World.GetComponent<RigidBodyComponent>(entity);
            ref var transform = ref World.GetComponent<TransformComponent>(entity);

            if (rbComponent.Body == null) continue;

            // For dynamic bodies, sync physics results back to transform
            if (rbComponent.Type == RigidBodyType.Dynamic)
            {
                transform.Position = rbComponent.Body.Position;
                transform.Rotation = rbComponent.Body.Rotation;
            }
        }
    }

    #region Debug Visualization

    private void DrawDebugVisualization()
    {
        if (_debugDraw == null || World == null) return;

        foreach (var entity in World.Query<RigidBodyComponent, TransformComponent>())
        {
            ref var rbComponent = ref World.GetComponent<RigidBodyComponent>(entity);
            ref var transform = ref World.GetComponent<TransformComponent>(entity);

            if (rbComponent.Body == null) continue;
            var body = rbComponent.Body;

            // Color based on body type and state
            var color = body.Type switch
            {
                RigidBodyType.Dynamic => body.IsAwake ? DebugColor.Green : DebugColor.Gray,
                RigidBodyType.Kinematic => DebugColor.Cyan,
                RigidBodyType.Static => DebugColor.Blue,
                _ => DebugColor.White
            };

            // Draw colliders
            if (_debugFlags.HasFlag(PhysicsDebugFlags.Colliders) && 
                World.HasComponent<ColliderComponent>(entity))
            {
                ref var colliderComp = ref World.GetComponent<ColliderComponent>(entity);
                if (colliderComp.Collider != null)
                {
                    DrawCollider(colliderComp.Collider, body.Position, body.Rotation, color);
                }
            }

            // Draw AABB
            if (_debugFlags.HasFlag(PhysicsDebugFlags.AABB) &&
                World.HasComponent<ColliderComponent>(entity))
            {
                ref var colliderComp = ref World.GetComponent<ColliderComponent>(entity);
                if (colliderComp.Collider != null)
                {
                    var aabb = colliderComp.Collider.GetWorldAABB(body.Position, body.Rotation);
                    _debugDraw.DrawWireBox(aabb.Min, aabb.Max, DebugColor.Yellow.WithAlpha(0.3f));
                }
            }

            // Draw center of mass
            if (_debugFlags.HasFlag(PhysicsDebugFlags.CenterOfMass))
            {
                _debugDraw.DrawPoint(body.Position, DebugColor.Red, 10f);
            }

            // Draw velocity
            if (_debugFlags.HasFlag(PhysicsDebugFlags.Velocity) && body.LinearVelocity.LengthSquared() > 0.01f)
            {
                _debugDraw.DrawArrow(body.Position, body.Position + body.LinearVelocity * 0.2f, DebugColor.Yellow);
            }

            // Draw angular velocity
            if (_debugFlags.HasFlag(PhysicsDebugFlags.AngularVelocity) && body.AngularVelocity.LengthSquared() > 0.01f)
            {
                _debugDraw.DrawArrow(body.Position, body.Position + body.AngularVelocity * 0.5f, DebugColor.Magenta);
            }

            // Draw sleep state indicator
            if (_debugFlags.HasFlag(PhysicsDebugFlags.SleepState) && !body.IsAwake)
            {
                _debugDraw.DrawText(body.Position + Vector3.UnitY, "Zzz", DebugColor.Gray);
            }
        }
    }

    private void DrawCollider(Collider collider, Vector3 position, Quaternion rotation, DebugColor color)
    {
        var center = position + Vector3.Transform(collider.Offset, rotation);

        switch (collider)
        {
            case SphereCollider sphere:
                _debugDraw!.DrawWireSphere(center, sphere.Radius, color.WithAlpha(0.6f), 16);
                break;

            case BoxCollider box:
                DrawWireOBB(center, box.HalfExtents, rotation, color.WithAlpha(0.6f));
                break;

            case CapsuleCollider capsule:
                capsule.GetEndpoints(position, rotation, out var p0, out var p1);
                _debugDraw!.DrawWireSphere(p0, capsule.Radius, color.WithAlpha(0.6f), 12);
                _debugDraw.DrawWireSphere(p1, capsule.Radius, color.WithAlpha(0.6f), 12);
                _debugDraw.DrawLine(p0, p1, color);
                break;

            case PlaneCollider plane:
                var planeNormal = Vector3.Transform(plane.Normal, rotation);
                var planeCenter = position + planeNormal * plane.Distance;
                _debugDraw!.DrawGrid(planeCenter, planeNormal, 20f, 10, color.WithAlpha(0.3f));
                _debugDraw.DrawArrow(planeCenter, planeCenter + planeNormal * 2f, color);
                break;
        }
    }

    private void DrawWireOBB(Vector3 center, Vector3 halfExtents, Quaternion rotation, DebugColor color)
    {
        var he = halfExtents;
        var corners = new Vector3[8];
        
        corners[0] = new Vector3(-he.X, -he.Y, -he.Z);
        corners[1] = new Vector3(+he.X, -he.Y, -he.Z);
        corners[2] = new Vector3(+he.X, +he.Y, -he.Z);
        corners[3] = new Vector3(-he.X, +he.Y, -he.Z);
        corners[4] = new Vector3(-he.X, -he.Y, +he.Z);
        corners[5] = new Vector3(+he.X, -he.Y, +he.Z);
        corners[6] = new Vector3(+he.X, +he.Y, +he.Z);
        corners[7] = new Vector3(-he.X, +he.Y, +he.Z);

        for (int i = 0; i < 8; i++)
        {
            corners[i] = center + Vector3.Transform(corners[i], rotation);
        }

        // Bottom face
        _debugDraw!.DrawLine(corners[0], corners[1], color);
        _debugDraw.DrawLine(corners[1], corners[2], color);
        _debugDraw.DrawLine(corners[2], corners[3], color);
        _debugDraw.DrawLine(corners[3], corners[0], color);

        // Top face
        _debugDraw.DrawLine(corners[4], corners[5], color);
        _debugDraw.DrawLine(corners[5], corners[6], color);
        _debugDraw.DrawLine(corners[6], corners[7], color);
        _debugDraw.DrawLine(corners[7], corners[4], color);

        // Vertical edges
        _debugDraw.DrawLine(corners[0], corners[4], color);
        _debugDraw.DrawLine(corners[1], corners[5], color);
        _debugDraw.DrawLine(corners[2], corners[6], color);
        _debugDraw.DrawLine(corners[3], corners[7], color);
    }

    #endregion

    #region Collision Events

    // These can be overridden or subscribed to
    public event Action<Entity, Entity, CollisionManifold>? CollisionEnter;
    public event Action<Entity, Entity>? CollisionExit;
    public event Action<Entity, Entity>? TriggerEnter;
    public event Action<Entity, Entity>? TriggerExit;

    private void OnCollisionEnter(CollisionEvent evt)
    {
        CollisionEnter?.Invoke(
            new Entity(evt.BodyA.EntityId, 0),
            new Entity(evt.BodyB.EntityId, 0),
            evt.Manifold);
    }

    private void OnCollisionExit(CollisionEvent evt)
    {
        CollisionExit?.Invoke(
            new Entity(evt.BodyA.EntityId, 0),
            new Entity(evt.BodyB.EntityId, 0));
    }

    private void OnTriggerEnter(CollisionEvent evt)
    {
        TriggerEnter?.Invoke(
            new Entity(evt.BodyA.EntityId, 0),
            new Entity(evt.BodyB.EntityId, 0));
    }

    private void OnTriggerExit(CollisionEvent evt)
    {
        TriggerExit?.Invoke(
            new Entity(evt.BodyA.EntityId, 0),
            new Entity(evt.BodyB.EntityId, 0));
    }

    #endregion

    #region Queries

    /// <summary>
    /// Cast a ray and return the first hit
    /// </summary>
    public bool Raycast(Vector3 origin, Vector3 direction, float maxDistance, out RaycastResult result)
    {
        result = default;
        
        if (_physicsWorld.Raycast(origin, direction, maxDistance, out var hit))
        {
            result = new RaycastResult
            {
                Hit = true,
                Point = hit.Point,
                Normal = hit.Normal,
                Distance = hit.Distance,
                Entity = hit.Body != null ? new Entity(hit.Body.EntityId, 0) : default
            };
            return true;
        }
        
        return false;
    }

    /// <summary>
    /// Cast a ray and return all hits
    /// </summary>
    public List<RaycastResult> RaycastAll(Vector3 origin, Vector3 direction, float maxDistance)
    {
        var physicsHits = _physicsWorld.RaycastAll(origin, direction, maxDistance);
        var results = new List<RaycastResult>(physicsHits.Count);
        
        foreach (var hit in physicsHits)
        {
            results.Add(new RaycastResult
            {
                Hit = true,
                Point = hit.Point,
                Normal = hit.Normal,
                Distance = hit.Distance,
                Entity = hit.Body != null ? new Entity(hit.Body.EntityId, 0) : default
            });
        }
        
        return results;
    }

    /// <summary>
    /// Find all entities with colliders overlapping a sphere
    /// </summary>
    public List<Entity> OverlapSphere(Vector3 center, float radius)
    {
        var colliders = _physicsWorld.OverlapSphere(center, radius);
        var entities = new List<Entity>();
        
        foreach (var collider in colliders)
        {
            if (collider.Body != null)
            {
                entities.Add(new Entity(collider.Body.EntityId, 0));
            }
        }
        
        return entities;
    }

    #endregion

    public override void OnDestroy()
    {
        _physicsWorld.Clear();
        _registeredEntities.Clear();
        
        base.OnDestroy();
    }
}

/// <summary>
/// Result of a raycast query
/// </summary>
public struct RaycastResult
{
    public bool Hit;
    public Vector3 Point;
    public Vector3 Normal;
    public float Distance;
    public Entity Entity;
}
