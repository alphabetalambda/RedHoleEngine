using System.Numerics;
using RedHoleEngine.Physics.Collision;

namespace RedHoleEngine.Physics;

/// <summary>
/// Configuration for the physics simulation
/// </summary>
public class PhysicsSettings
{
    public Vector3 Gravity { get; set; } = new(0, -9.81f, 0);
    public int VelocityIterations { get; set; } = 8;
    public int PositionIterations { get; set; } = 3;
    public float BaumgarteScale { get; set; } = 0.2f;  // Position correction factor
    public float SleepLinearThreshold { get; set; } = 0.05f;
    public float SleepAngularThreshold { get; set; } = 0.05f;
    public float AllowedPenetration { get; set; } = 0.01f;  // Slop for collision resolution
    public float MaxLinearVelocity { get; set; } = 500f;
    public float MaxAngularVelocity { get; set; } = 50f;
}

/// <summary>
/// A pair of colliding bodies
/// </summary>
public struct CollisionPair : IEquatable<CollisionPair>
{
    public int BodyAIndex;
    public int BodyBIndex;

    public CollisionPair(int a, int b)
    {
        // Ensure consistent ordering for deduplication
        if (a < b)
        {
            BodyAIndex = a;
            BodyBIndex = b;
        }
        else
        {
            BodyAIndex = b;
            BodyBIndex = a;
        }
    }

    public bool Equals(CollisionPair other) => 
        BodyAIndex == other.BodyAIndex && BodyBIndex == other.BodyBIndex;

    public override bool Equals(object? obj) => 
        obj is CollisionPair other && Equals(other);

    public override int GetHashCode() => 
        HashCode.Combine(BodyAIndex, BodyBIndex);
}

/// <summary>
/// Event data for collision callbacks
/// </summary>
public class CollisionEvent
{
    public RigidBody BodyA { get; set; } = null!;
    public RigidBody BodyB { get; set; } = null!;
    public Collider ColliderA { get; set; } = null!;
    public Collider ColliderB { get; set; } = null!;
    public CollisionManifold Manifold { get; set; } = null!;
}

/// <summary>
/// The physics simulation world.
/// Manages rigid bodies, collision detection, and physics integration.
/// </summary>
public class PhysicsWorld
{
    private readonly List<RigidBody> _bodies = new();
    private readonly List<Collider> _colliders = new();
    private readonly Dictionary<int, List<Collider>> _bodyColliders = new(); // EntityId -> Colliders
    private readonly Dictionary<int, int> _entityToBody = new(); // EntityId -> Body index
    
    // Collision data
    private readonly HashSet<CollisionPair> _broadphasePairs = new();
    private readonly List<CollisionManifold> _manifolds = new();
    
    // Settings
    public PhysicsSettings Settings { get; } = new();
    
    // Events
    public event Action<CollisionEvent>? OnCollisionEnter;
    public event Action<CollisionEvent>? OnCollisionStay;
    public event Action<CollisionEvent>? OnCollisionExit;
    public event Action<CollisionEvent>? OnTriggerEnter;
    public event Action<CollisionEvent>? OnTriggerExit;
    
    // Previous frame collisions (for Enter/Exit detection)
    private readonly HashSet<CollisionPair> _previousCollisions = new();
    private readonly HashSet<CollisionPair> _currentCollisions = new();

    /// <summary>
    /// Number of rigid bodies in the world
    /// </summary>
    public int BodyCount => _bodies.Count;

    /// <summary>
    /// Number of colliders in the world
    /// </summary>
    public int ColliderCount => _colliders.Count;

    /// <summary>
    /// Add a rigid body to the world
    /// </summary>
    public void AddBody(RigidBody body)
    {
        if (_entityToBody.ContainsKey(body.EntityId))
        {
            Console.WriteLine($"Warning: Entity {body.EntityId} already has a rigid body");
            return;
        }
        
        _entityToBody[body.EntityId] = _bodies.Count;
        _bodies.Add(body);
        _bodyColliders[body.EntityId] = new List<Collider>();
    }

    /// <summary>
    /// Remove a rigid body from the world
    /// </summary>
    public void RemoveBody(RigidBody body)
    {
        if (!_entityToBody.TryGetValue(body.EntityId, out var index))
            return;
        
        // Remove associated colliders
        if (_bodyColliders.TryGetValue(body.EntityId, out var colliders))
        {
            foreach (var collider in colliders)
            {
                _colliders.Remove(collider);
            }
            _bodyColliders.Remove(body.EntityId);
        }
        
        // Remove body (swap-remove for efficiency)
        var lastIndex = _bodies.Count - 1;
        if (index != lastIndex)
        {
            var lastBody = _bodies[lastIndex];
            _bodies[index] = lastBody;
            _entityToBody[lastBody.EntityId] = index;
        }
        _bodies.RemoveAt(lastIndex);
        _entityToBody.Remove(body.EntityId);
    }

    /// <summary>
    /// Get a rigid body by entity ID
    /// </summary>
    public RigidBody? GetBody(int entityId)
    {
        return _entityToBody.TryGetValue(entityId, out var index) ? _bodies[index] : null;
    }

    /// <summary>
    /// Add a collider to a rigid body
    /// </summary>
    public void AddCollider(RigidBody body, Collider collider)
    {
        collider.Body = body;
        _colliders.Add(collider);
        
        if (_bodyColliders.TryGetValue(body.EntityId, out var colliders))
        {
            colliders.Add(collider);
        }
    }

    /// <summary>
    /// Step the physics simulation
    /// </summary>
    public void Step(float deltaTime)
    {
        if (deltaTime <= 0) return;

        // 1. Apply gravity and external forces
        ApplyGravity();

        // 2. Integrate velocities
        IntegrateVelocities(deltaTime);

        // 3. Broadphase collision detection
        BroadphaseDetection();

        // 4. Narrowphase collision detection
        NarrowphaseDetection();

        // 5. Resolve collisions (velocity-level)
        ResolveVelocities();

        // 6. Integrate positions
        IntegratePositions(deltaTime);

        // 7. Position correction (reduce penetration)
        ResolvePositions();

        // 8. Fire collision events
        FireCollisionEvents();

        // 9. Update sleep states
        UpdateSleepStates(deltaTime);

        // 10. Clear forces
        ClearForces();
    }

    private void ApplyGravity()
    {
        foreach (var body in _bodies)
        {
            if (body.Type != RigidBodyType.Dynamic || !body.UseGravity || !body.IsAwake)
                continue;
            
            body.ApplyForce(Settings.Gravity * body.Mass);
        }
    }

    private void IntegrateVelocities(float dt)
    {
        foreach (var body in _bodies)
        {
            if (body.Type != RigidBodyType.Dynamic || !body.IsAwake)
                continue;

            // Linear velocity
            body.LinearVelocity += body.Force * body.InverseMass * dt;
            
            // Angular velocity (simplified - using diagonal inertia tensor)
            body.AngularVelocity += body.Torque * body.InverseInertia * dt;
            
            // Apply damping
            body.LinearVelocity *= MathF.Pow(1f - body.LinearDamping, dt);
            body.AngularVelocity *= MathF.Pow(1f - body.AngularDamping, dt);
            
            // Clamp velocities
            ClampVelocity(body);
            
            // Apply constraints
            body.ApplyConstraints();
        }
    }

    private void IntegratePositions(float dt)
    {
        foreach (var body in _bodies)
        {
            if (body.Type == RigidBodyType.Static || !body.IsAwake)
                continue;

            // Integrate position
            body.Position += body.LinearVelocity * dt;
            
            // Integrate rotation (using small angle approximation)
            if (body.AngularVelocity.LengthSquared() > 0.0001f)
            {
                var angularVelQuat = new Quaternion(
                    body.AngularVelocity.X * dt * 0.5f,
                    body.AngularVelocity.Y * dt * 0.5f,
                    body.AngularVelocity.Z * dt * 0.5f,
                    0
                );
                body.Rotation = Quaternion.Normalize(body.Rotation + angularVelQuat * body.Rotation);
            }
        }
    }

    private void ClampVelocity(RigidBody body)
    {
        // Clamp linear velocity
        var linVelSq = body.LinearVelocity.LengthSquared();
        if (linVelSq > Settings.MaxLinearVelocity * Settings.MaxLinearVelocity)
        {
            body.LinearVelocity = body.LinearVelocity / MathF.Sqrt(linVelSq) * Settings.MaxLinearVelocity;
        }
        
        // Clamp angular velocity
        var angVelSq = body.AngularVelocity.LengthSquared();
        if (angVelSq > Settings.MaxAngularVelocity * Settings.MaxAngularVelocity)
        {
            body.AngularVelocity = body.AngularVelocity / MathF.Sqrt(angVelSq) * Settings.MaxAngularVelocity;
        }
    }

    #region Collision Detection

    private void BroadphaseDetection()
    {
        _broadphasePairs.Clear();
        
        // Simple O(n^2) broadphase with AABB tests
        // TODO: Replace with spatial partitioning (BVH, grid, etc.) for better performance
        
        for (int i = 0; i < _colliders.Count; i++)
        {
            var colliderA = _colliders[i];
            if (colliderA.Body == null) continue;
            
            var aabbA = colliderA.GetWorldAABB(colliderA.Body.Position, colliderA.Body.Rotation);
            
            for (int j = i + 1; j < _colliders.Count; j++)
            {
                var colliderB = _colliders[j];
                if (colliderB.Body == null) continue;
                
                // Skip if same body
                if (colliderA.Body == colliderB.Body) continue;
                
                // Skip if both static/kinematic
                if (colliderA.Body.Type != RigidBodyType.Dynamic && 
                    colliderB.Body.Type != RigidBodyType.Dynamic)
                    continue;
                
                // Check collision layers
                if ((colliderA.Body.CollisionLayer & colliderB.Body.CollisionMask) == 0 &&
                    (colliderB.Body.CollisionLayer & colliderA.Body.CollisionMask) == 0)
                    continue;
                
                // AABB test
                var aabbB = colliderB.GetWorldAABB(colliderB.Body.Position, colliderB.Body.Rotation);
                if (aabbA.Intersects(aabbB))
                {
                    _broadphasePairs.Add(new CollisionPair(i, j));
                }
            }
        }
    }

    private void NarrowphaseDetection()
    {
        _manifolds.Clear();
        _currentCollisions.Clear();
        
        foreach (var pair in _broadphasePairs)
        {
            var colliderA = _colliders[pair.BodyAIndex];
            var colliderB = _colliders[pair.BodyBIndex];
            
            if (colliderA.Body == null || colliderB.Body == null) continue;
            
            if (CollisionDetection.TestCollision(
                colliderA, colliderA.Body.Position, colliderA.Body.Rotation,
                colliderB, colliderB.Body.Position, colliderB.Body.Rotation,
                out var manifold))
            {
                if (manifold.Contacts.Count > 0)
                {
                    manifold.BodyA = colliderA.Body;
                    manifold.BodyB = colliderB.Body;
                    
                    // Calculate combined material properties
                    var matA = colliderA.Material ?? PhysicsMaterial.Default;
                    var matB = colliderB.Material ?? PhysicsMaterial.Default;
                    manifold.Restitution = MathF.Max(
                        colliderA.Body.Restitution * matA.Restitution,
                        colliderB.Body.Restitution * matB.Restitution);
                    manifold.Friction = MathF.Sqrt(
                        colliderA.Body.Friction * matA.DynamicFriction *
                        colliderB.Body.Friction * matB.DynamicFriction);
                    
                    _manifolds.Add(manifold);
                    _currentCollisions.Add(pair);
                }
            }
        }
    }

    #endregion

    #region Collision Resolution

    private void ResolveVelocities()
    {
        for (int iteration = 0; iteration < Settings.VelocityIterations; iteration++)
        {
            foreach (var manifold in _manifolds)
            {
                // Skip triggers
                if (manifold.ColliderA.IsTrigger || manifold.ColliderB.IsTrigger)
                    continue;
                
                foreach (var contact in manifold.Contacts)
                {
                    ResolveContact(manifold, contact);
                }
            }
        }
    }

    private void ResolveContact(CollisionManifold manifold, CollisionContact contact)
    {
        var bodyA = manifold.BodyA;
        var bodyB = manifold.BodyB;
        
        // Calculate relative velocity at contact point
        var rA = contact.PointOnA - bodyA.Position;
        var rB = contact.PointOnB - bodyB.Position;
        
        var velA = bodyA.GetVelocityAtPoint(contact.PointOnA);
        var velB = bodyB.GetVelocityAtPoint(contact.PointOnB);
        var relVel = velB - velA;
        
        var contactVel = Vector3.Dot(relVel, contact.Normal);
        
        // Don't resolve if separating
        if (contactVel > 0)
            return;
        
        // Calculate inverse mass sum with angular contribution
        var invMassA = bodyA.InverseMass;
        var invMassB = bodyB.InverseMass;
        
        var crossA = Vector3.Cross(rA, contact.Normal);
        var crossB = Vector3.Cross(rB, contact.Normal);
        var angularA = Vector3.Dot(crossA * bodyA.InverseInertia, crossA);
        var angularB = Vector3.Dot(crossB * bodyB.InverseInertia, crossB);
        
        var invMassSum = invMassA + invMassB + angularA + angularB;
        if (invMassSum <= 0) return;
        
        // Normal impulse (with restitution)
        var e = manifold.Restitution;
        var j = -(1f + e) * contactVel / invMassSum;
        
        var impulse = contact.Normal * j;
        
        // Apply normal impulse
        if (bodyA.Type == RigidBodyType.Dynamic)
        {
            bodyA.LinearVelocity -= impulse * invMassA;
            bodyA.AngularVelocity -= Vector3.Cross(rA, impulse) * bodyA.InverseInertia;
        }
        if (bodyB.Type == RigidBodyType.Dynamic)
        {
            bodyB.LinearVelocity += impulse * invMassB;
            bodyB.AngularVelocity += Vector3.Cross(rB, impulse) * bodyB.InverseInertia;
        }
        
        // Friction impulse
        var tangent = relVel - contact.Normal * contactVel;
        var tangentLengthSq = tangent.LengthSquared();
        
        if (tangentLengthSq > 0.0001f)
        {
            tangent /= MathF.Sqrt(tangentLengthSq);
            
            var tangentVel = Vector3.Dot(relVel, tangent);
            var jt = -tangentVel / invMassSum;
            
            // Coulomb friction clamp
            var frictionImpulse = MathF.Abs(jt) < j * manifold.Friction
                ? tangent * jt
                : tangent * (-j * manifold.Friction);
            
            // Apply friction impulse
            if (bodyA.Type == RigidBodyType.Dynamic)
            {
                bodyA.LinearVelocity -= frictionImpulse * invMassA;
                bodyA.AngularVelocity -= Vector3.Cross(rA, frictionImpulse) * bodyA.InverseInertia;
            }
            if (bodyB.Type == RigidBodyType.Dynamic)
            {
                bodyB.LinearVelocity += frictionImpulse * invMassB;
                bodyB.AngularVelocity += Vector3.Cross(rB, frictionImpulse) * bodyB.InverseInertia;
            }
        }
    }

    private void ResolvePositions()
    {
        for (int iteration = 0; iteration < Settings.PositionIterations; iteration++)
        {
            foreach (var manifold in _manifolds)
            {
                // Skip triggers
                if (manifold.ColliderA.IsTrigger || manifold.ColliderB.IsTrigger)
                    continue;
                
                foreach (var contact in manifold.Contacts)
                {
                    CorrectPosition(manifold, contact);
                }
            }
        }
    }

    private void CorrectPosition(CollisionManifold manifold, CollisionContact contact)
    {
        var bodyA = manifold.BodyA;
        var bodyB = manifold.BodyB;
        
        var invMassA = bodyA.InverseMass;
        var invMassB = bodyB.InverseMass;
        var invMassSum = invMassA + invMassB;
        
        if (invMassSum <= 0) return;
        
        // Calculate correction
        var penetration = contact.Depth - Settings.AllowedPenetration;
        if (penetration <= 0) return;
        
        var correction = contact.Normal * (penetration * Settings.BaumgarteScale / invMassSum);
        
        // Apply position correction
        if (bodyA.Type == RigidBodyType.Dynamic)
        {
            bodyA.Position -= correction * invMassA;
        }
        if (bodyB.Type == RigidBodyType.Dynamic)
        {
            bodyB.Position += correction * invMassB;
        }
    }

    #endregion

    #region Events

    private void FireCollisionEvents()
    {
        // Find new collisions (Enter)
        foreach (var pair in _currentCollisions)
        {
            if (!_previousCollisions.Contains(pair))
            {
                var manifold = _manifolds.FirstOrDefault(m => 
                    _entityToBody.TryGetValue(m.BodyA.EntityId, out var ia) &&
                    _entityToBody.TryGetValue(m.BodyB.EntityId, out var ib) &&
                    new CollisionPair(ia, ib).Equals(pair));
                
                if (manifold != null)
                {
                    var evt = new CollisionEvent
                    {
                        BodyA = manifold.BodyA,
                        BodyB = manifold.BodyB,
                        ColliderA = manifold.ColliderA,
                        ColliderB = manifold.ColliderB,
                        Manifold = manifold
                    };
                    
                    if (manifold.ColliderA.IsTrigger || manifold.ColliderB.IsTrigger)
                        OnTriggerEnter?.Invoke(evt);
                    else
                        OnCollisionEnter?.Invoke(evt);
                }
            }
        }
        
        // Ongoing collisions (Stay)
        foreach (var pair in _currentCollisions)
        {
            if (_previousCollisions.Contains(pair))
            {
                var manifold = _manifolds.FirstOrDefault(m => 
                    _entityToBody.TryGetValue(m.BodyA.EntityId, out var ia) &&
                    _entityToBody.TryGetValue(m.BodyB.EntityId, out var ib) &&
                    new CollisionPair(ia, ib).Equals(pair));
                
                if (manifold != null && !manifold.ColliderA.IsTrigger && !manifold.ColliderB.IsTrigger)
                {
                    OnCollisionStay?.Invoke(new CollisionEvent
                    {
                        BodyA = manifold.BodyA,
                        BodyB = manifold.BodyB,
                        ColliderA = manifold.ColliderA,
                        ColliderB = manifold.ColliderB,
                        Manifold = manifold
                    });
                }
            }
        }
        
        // Lost collisions (Exit)
        foreach (var pair in _previousCollisions)
        {
            if (!_currentCollisions.Contains(pair))
            {
                // Need to reconstruct event from stored data
                if (pair.BodyAIndex < _colliders.Count && pair.BodyBIndex < _colliders.Count)
                {
                    var colliderA = _colliders[pair.BodyAIndex];
                    var colliderB = _colliders[pair.BodyBIndex];
                    
                    if (colliderA.Body != null && colliderB.Body != null)
                    {
                        var evt = new CollisionEvent
                        {
                            BodyA = colliderA.Body,
                            BodyB = colliderB.Body,
                            ColliderA = colliderA,
                            ColliderB = colliderB,
                            Manifold = new CollisionManifold
                            {
                                BodyA = colliderA.Body,
                                BodyB = colliderB.Body,
                                ColliderA = colliderA,
                                ColliderB = colliderB
                            }
                        };
                        
                        if (colliderA.IsTrigger || colliderB.IsTrigger)
                            OnTriggerExit?.Invoke(evt);
                        else
                            OnCollisionExit?.Invoke(evt);
                    }
                }
            }
        }
        
        // Swap collision sets
        _previousCollisions.Clear();
        foreach (var pair in _currentCollisions)
        {
            _previousCollisions.Add(pair);
        }
    }

    #endregion

    private void UpdateSleepStates(float dt)
    {
        foreach (var body in _bodies)
        {
            body.UpdateSleep(dt);
        }
    }

    private void ClearForces()
    {
        foreach (var body in _bodies)
        {
            body.ClearForces();
        }
    }

    #region Queries

    /// <summary>
    /// Cast a ray and return the first hit
    /// </summary>
    public bool Raycast(Vector3 origin, Vector3 direction, float maxDistance, out CollisionDetection.RaycastHit hit)
    {
        hit = default;
        hit.Distance = maxDistance;
        bool hasHit = false;
        
        direction = Vector3.Normalize(direction);
        
        foreach (var collider in _colliders)
        {
            if (collider.Body == null) continue;
            
            CollisionDetection.RaycastHit tempHit = default;
            bool didHit = false;
            
            switch (collider.Type)
            {
                case ColliderType.Sphere:
                    didHit = CollisionDetection.RaycastSphere(
                        origin, direction, hit.Distance,
                        (SphereCollider)collider, collider.Body.Position, collider.Body.Rotation,
                        out tempHit);
                    break;
                case ColliderType.Box:
                    didHit = CollisionDetection.RaycastBox(
                        origin, direction, hit.Distance,
                        (BoxCollider)collider, collider.Body.Position, collider.Body.Rotation,
                        out tempHit);
                    break;
                case ColliderType.Plane:
                    didHit = CollisionDetection.RaycastPlane(
                        origin, direction, hit.Distance,
                        (PlaneCollider)collider, collider.Body.Position, collider.Body.Rotation,
                        out tempHit);
                    break;
            }
            
            if (didHit && tempHit.Distance < hit.Distance)
            {
                hit = tempHit;
                hit.Body = collider.Body;
                hasHit = true;
            }
        }
        
        return hasHit;
    }

    /// <summary>
    /// Cast a ray and return all hits
    /// </summary>
    public List<CollisionDetection.RaycastHit> RaycastAll(Vector3 origin, Vector3 direction, float maxDistance)
    {
        var hits = new List<CollisionDetection.RaycastHit>();
        direction = Vector3.Normalize(direction);
        
        foreach (var collider in _colliders)
        {
            if (collider.Body == null) continue;
            
            CollisionDetection.RaycastHit tempHit = default;
            bool didHit = false;
            
            switch (collider.Type)
            {
                case ColliderType.Sphere:
                    didHit = CollisionDetection.RaycastSphere(
                        origin, direction, maxDistance,
                        (SphereCollider)collider, collider.Body.Position, collider.Body.Rotation,
                        out tempHit);
                    break;
                case ColliderType.Box:
                    didHit = CollisionDetection.RaycastBox(
                        origin, direction, maxDistance,
                        (BoxCollider)collider, collider.Body.Position, collider.Body.Rotation,
                        out tempHit);
                    break;
                case ColliderType.Plane:
                    didHit = CollisionDetection.RaycastPlane(
                        origin, direction, maxDistance,
                        (PlaneCollider)collider, collider.Body.Position, collider.Body.Rotation,
                        out tempHit);
                    break;
            }
            
            if (didHit)
            {
                tempHit.Body = collider.Body;
                hits.Add(tempHit);
            }
        }
        
        hits.Sort((a, b) => a.Distance.CompareTo(b.Distance));
        return hits;
    }

    /// <summary>
    /// Find all colliders overlapping a sphere
    /// </summary>
    public List<Collider> OverlapSphere(Vector3 center, float radius)
    {
        var results = new List<Collider>();
        var testSphere = new SphereCollider(radius);
        
        foreach (var collider in _colliders)
        {
            if (collider.Body == null) continue;
            
            if (CollisionDetection.TestCollision(
                testSphere, center, Quaternion.Identity,
                collider, collider.Body.Position, collider.Body.Rotation,
                out _))
            {
                results.Add(collider);
            }
        }
        
        return results;
    }

    /// <summary>
    /// Find all colliders overlapping a box
    /// </summary>
    public List<Collider> OverlapBox(Vector3 center, Vector3 halfExtents, Quaternion rotation)
    {
        var results = new List<Collider>();
        var testBox = new BoxCollider(halfExtents);
        
        foreach (var collider in _colliders)
        {
            if (collider.Body == null) continue;
            
            if (CollisionDetection.TestCollision(
                testBox, center, rotation,
                collider, collider.Body.Position, collider.Body.Rotation,
                out _))
            {
                results.Add(collider);
            }
        }
        
        return results;
    }

    #endregion

    /// <summary>
    /// Clear all bodies and colliders
    /// </summary>
    public void Clear()
    {
        _bodies.Clear();
        _colliders.Clear();
        _bodyColliders.Clear();
        _entityToBody.Clear();
        _broadphasePairs.Clear();
        _manifolds.Clear();
        _previousCollisions.Clear();
        _currentCollisions.Clear();
    }
}
