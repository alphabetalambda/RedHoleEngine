using System.Numerics;
using System.Runtime.CompilerServices;

namespace RedHoleEngine.Physics.Constraints;

/// <summary>
/// Sequential impulse solver for link constraints.
/// Handles rigid, elastic, plastic, and rope constraints.
/// </summary>
public class ConstraintSolver
{
    // ===== Link Storage =====
    private readonly List<LinkConstraint> _links = new();
    private readonly List<LinkChain> _chains = new();
    private readonly List<LinkMesh> _meshes = new();
    
    private readonly List<LinkConstraint> _pendingRemoval = new();
    private readonly List<LinkBreakEvent> _pendingBreakEvents = new();
    private readonly List<LinkYieldEvent> _pendingYieldEvents = new();
    private readonly List<ChainBreakEvent> _pendingChainBreakEvents = new();
    private readonly List<MeshDamageEvent> _pendingMeshDamageEvents = new();
    
    private int _nextLinkId = 1;
    private int _nextChainId = 1;
    private int _nextMeshId = 1;
    
    // ===== Settings =====
    
    /// <summary>
    /// Baumgarte stabilization factor (0-1). Higher = faster position correction but less stable.
    /// </summary>
    public float BiasFactor = 0.2f;
    
    /// <summary>
    /// Allowed constraint error before correction kicks in (slop).
    /// </summary>
    public float Slop = 0.005f;
    
    /// <summary>
    /// Use warm starting (reuse previous frame's impulses).
    /// </summary>
    public bool WarmStarting = true;
    
    /// <summary>
    /// Warm starting factor (0-1).
    /// </summary>
    public float WarmStartFactor = 0.8f;
    
    /// <summary>
    /// Maximum impulse per constraint per iteration.
    /// </summary>
    public float MaxImpulse = 10000f;
    
    /// <summary>
    /// Tension threshold for firing stretch events (as ratio of break threshold).
    /// </summary>
    public float StretchEventThreshold = 0.5f;
    
    // ===== Events =====
    
    public event LinkBreakHandler? OnLinkBreak;
    public event LinkYieldHandler? OnLinkYield;
    public event LinkStretchHandler? OnLinkStretch;
    public event ChainBreakHandler? OnChainBreak;
    public event MeshDamageHandler? OnMeshDamage;
    
    // ===== Public Properties =====
    
    public int LinkCount => _links.Count;
    public int ChainCount => _chains.Count;
    public int MeshCount => _meshes.Count;
    public IReadOnlyList<LinkConstraint> Links => _links;
    public IReadOnlyList<LinkChain> Chains => _chains;
    public IReadOnlyList<LinkMesh> Meshes => _meshes;
    
    // ===== Add/Remove Links =====
    
    /// <summary>
    /// Add a standalone link constraint
    /// </summary>
    public LinkConstraint AddLink(LinkConstraint link)
    {
        link.Id = _nextLinkId++;
        _links.Add(link);
        return link;
    }
    
    /// <summary>
    /// Add a chain (rope or rigid chain)
    /// </summary>
    public LinkChain AddChain(LinkChain chain)
    {
        chain.Id = _nextChainId++;
        _chains.Add(chain);
        
        // Register all links in the chain
        foreach (var link in chain.Links)
        {
            link.Id = _nextLinkId++;
            link.ChainId = chain.Id;
            _links.Add(link);
        }
        
        return chain;
    }
    
    /// <summary>
    /// Add a mesh (soft body or destructible)
    /// </summary>
    public LinkMesh AddMesh(LinkMesh mesh)
    {
        mesh.Id = _nextMeshId++;
        _meshes.Add(mesh);
        
        // Register all links in the mesh
        void RegisterLinks(List<LinkConstraint> links)
        {
            foreach (var link in links)
            {
                link.Id = _nextLinkId++;
                link.MeshId = mesh.Id;
                _links.Add(link);
            }
        }
        
        RegisterLinks(mesh.StructuralLinks);
        RegisterLinks(mesh.ShearLinks);
        RegisterLinks(mesh.BendLinks);
        
        return mesh;
    }
    
    /// <summary>
    /// Remove a specific link by ID
    /// </summary>
    public void RemoveLink(int linkId)
    {
        _links.RemoveAll(l => l.Id == linkId);
    }
    
    /// <summary>
    /// Remove a chain and all its links
    /// </summary>
    public void RemoveChain(int chainId)
    {
        _chains.RemoveAll(c => c.Id == chainId);
        _links.RemoveAll(l => l.ChainId == chainId);
    }
    
    /// <summary>
    /// Remove a mesh and all its links
    /// </summary>
    public void RemoveMesh(int meshId)
    {
        _meshes.RemoveAll(m => m.Id == meshId);
        _links.RemoveAll(l => l.MeshId == meshId);
    }
    
    /// <summary>
    /// Remove all links connected to an entity
    /// </summary>
    public void RemoveAllForEntity(int entityId)
    {
        _links.RemoveAll(l => l.EntityA == entityId || l.EntityB == entityId);
        
        // Also remove chains/meshes that are now invalid
        _chains.RemoveAll(c => c.Links.Count == 0 || 
            c.Links.All(l => l.State == LinkState.Broken));
        _meshes.RemoveAll(m => m.GetIntegrity() <= 0);
    }
    
    /// <summary>
    /// Clear all constraints
    /// </summary>
    public void Clear()
    {
        _links.Clear();
        _chains.Clear();
        _meshes.Clear();
        _pendingRemoval.Clear();
        _pendingBreakEvents.Clear();
        _pendingYieldEvents.Clear();
    }
    
    // ===== Solver Steps =====
    
    /// <summary>
    /// Pre-solve: Update world anchors, calculate effective masses, warm start
    /// </summary>
    public void PreSolve(PhysicsWorld world, float dt)
    {
        foreach (var link in _links)
        {
            if (link.State == LinkState.Broken)
                continue;
            
            // Resolve entity IDs to rigid bodies
            link.BodyA = world.GetBodyByEntityId(link.EntityA);
            if (link.EntityB.HasValue)
            {
                link.BodyB = world.GetBodyByEntityId(link.EntityB.Value);
            }
            else
            {
                link.BodyB = null;
            }
            
            // Skip if both bodies are missing or static
            if (link.BodyA == null && link.BodyB == null)
                continue;
            
            bool aStatic = link.BodyA == null || link.BodyA.Type != RigidBodyType.Dynamic;
            bool bStatic = link.BodyB == null || link.BodyB?.Type != RigidBodyType.Dynamic;
            
            if (aStatic && bStatic)
                continue;
            
            // Update world positions
            link.UpdateWorldAnchors();
            
            // Calculate effective mass
            link.CalculateEffectiveMass();
            
            // Calculate bias for Baumgarte stabilization
            float error = GetConstraintError(link);
            float biasVel = BiasFactor / dt;
            link.Bias = biasVel * Math.Max(0, Math.Abs(error) - Slop) * Math.Sign(error);
            
            // Warm starting
            if (WarmStarting && link.AccumulatedImpulse != 0)
            {
                float warmImpulse = link.AccumulatedImpulse * WarmStartFactor;
                link.ApplyImpulse(warmImpulse);
            }
        }
    }
    
    /// <summary>
    /// Solve velocity constraints (main solver loop)
    /// </summary>
    public void SolveVelocities(float dt)
    {
        foreach (var link in _links)
        {
            if (link.State == LinkState.Broken)
                continue;
            
            // Skip if no effective mass (both static or missing)
            if (link.EffectiveMass <= 0)
                continue;
            
            switch (link.Type)
            {
                case LinkType.Rigid:
                    SolveRigidLink(link, dt);
                    break;
                case LinkType.Elastic:
                    SolveElasticLink(link, dt);
                    break;
                case LinkType.Plastic:
                    SolvePlasticLink(link, dt);
                    break;
                case LinkType.Rope:
                    SolveRopeLink(link, dt);
                    break;
            }
        }
    }
    
    /// <summary>
    /// Solve position constraints (Baumgarte stabilization)
    /// </summary>
    public void SolvePositions(float dt)
    {
        foreach (var link in _links)
        {
            if (link.State == LinkState.Broken)
                continue;
            
            if (link.EffectiveMass <= 0)
                continue;
            
            // Update world anchors
            link.UpdateWorldAnchors();
            
            // For rigid and rope links, apply position correction
            if (link.Type == LinkType.Rigid || link.Type == LinkType.Rope)
            {
                float error = GetConstraintError(link);
                
                // For ropes, only correct if stretched
                if (link.Type == LinkType.Rope && error < 0)
                    continue;
                
                if (Math.Abs(error) > Slop)
                {
                    float correction = error * 0.2f;  // Position correction factor
                    
                    float invMassA = link.BodyA?.InverseMass ?? 0;
                    float invMassB = link.BodyB?.InverseMass ?? 0;
                    float totalInvMass = invMassA + invMassB;
                    
                    if (totalInvMass > 0)
                    {
                        Vector3 correctionVec = link.Axis * correction;
                        
                        if (link.BodyA != null && link.BodyA.Type == RigidBodyType.Dynamic)
                        {
                            link.BodyA.Position -= correctionVec * (invMassA / totalInvMass);
                        }
                        if (link.BodyB != null && link.BodyB.Type == RigidBodyType.Dynamic)
                        {
                            link.BodyB.Position += correctionVec * (invMassB / totalInvMass);
                        }
                    }
                }
            }
        }
    }
    
    /// <summary>
    /// Post-solve: Fire events, cleanup broken links
    /// </summary>
    public void PostSolve()
    {
        // Fire pending events
        foreach (var evt in _pendingBreakEvents)
        {
            OnLinkBreak?.Invoke(evt);
        }
        _pendingBreakEvents.Clear();
        
        foreach (var evt in _pendingYieldEvents)
        {
            OnLinkYield?.Invoke(evt);
        }
        _pendingYieldEvents.Clear();
        
        foreach (var evt in _pendingChainBreakEvents)
        {
            OnChainBreak?.Invoke(evt);
        }
        _pendingChainBreakEvents.Clear();
        
        foreach (var evt in _pendingMeshDamageEvents)
        {
            OnMeshDamage?.Invoke(evt);
        }
        _pendingMeshDamageEvents.Clear();
        
        // Remove broken links (optional - keep them for debugging)
        // _links.RemoveAll(l => l.State == LinkState.Broken);
    }
    
    // ===== Per-Type Solvers =====
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void SolveRigidLink(LinkConstraint link, float dt)
    {
        // Get relative velocity along constraint axis
        Vector3 velA = link.GetVelocityAtAnchorA();
        Vector3 velB = link.GetVelocityAtAnchorB();
        float relVel = Vector3.Dot(velB - velA, link.Axis);
        
        // Compute impulse: lambda = -(Jv + bias) / effectiveMass
        float lambda = -(relVel + link.Bias) * link.EffectiveMass;
        
        // Accumulate and clamp
        float oldAccum = link.AccumulatedImpulse;
        link.AccumulatedImpulse = Math.Clamp(oldAccum + lambda, -MaxImpulse, MaxImpulse);
        lambda = link.AccumulatedImpulse - oldAccum;
        
        // Apply impulse
        link.ApplyImpulse(lambda);
        link.CurrentForce = Math.Abs(lambda) / dt;
    }
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void SolveElasticLink(LinkConstraint link, float dt)
    {
        // Spring-damper force
        float extension = link.CurrentLength - link.CurrentRestLength;
        float springForce = -link.Stiffness * extension;
        
        // Damping force
        Vector3 velA = link.GetVelocityAtAnchorA();
        Vector3 velB = link.GetVelocityAtAnchorB();
        float relVel = Vector3.Dot(velB - velA, link.Axis);
        float dampingForce = -link.Damping * relVel;
        
        // Total force (applied as impulse for stability)
        float totalForce = springForce + dampingForce;
        float impulse = totalForce * dt;
        
        link.ApplyImpulse(impulse);
        link.CurrentForce = Math.Abs(totalForce);
        
        // Fire stretch event if under significant tension
        if (link.BreakThreshold > 1f)
        {
            float stressLevel = link.StressLevel;
            if (stressLevel > StretchEventThreshold)
            {
                OnLinkStretch?.Invoke(new LinkStretchEvent
                {
                    LinkId = link.Id,
                    Tension = link.CurrentForce,
                    StretchRatio = link.StretchRatio,
                    StressLevel = stressLevel,
                    Position = link.Midpoint
                });
            }
        }
    }
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void SolvePlasticLink(LinkConstraint link, float dt)
    {
        // First solve as elastic
        SolveElasticLink(link, dt);
        
        float stretchRatio = link.StretchRatio;
        
        // Check for yielding
        if (stretchRatio > link.YieldThreshold && link.State != LinkState.Broken)
        {
            float oldRestLength = link.CurrentRestLength;
            
            // Permanent deformation - increase rest length
            float excessStretch = stretchRatio - link.YieldThreshold;
            float deformation = excessStretch * link.PlasticRate * dt * link.CurrentRestLength;
            link.CurrentRestLength += deformation;
            
            // Fire yield event
            if (link.State != LinkState.Yielding)
            {
                link.State = LinkState.Yielding;
            }
            
            _pendingYieldEvents.Add(new LinkYieldEvent
            {
                LinkId = link.Id,
                ChainId = link.ChainId,
                MeshId = link.MeshId,
                OldRestLength = oldRestLength,
                NewRestLength = link.CurrentRestLength,
                StretchRatio = stretchRatio,
                Position = link.Midpoint
            });
        }
        else if (link.State == LinkState.Yielding && stretchRatio <= link.YieldThreshold)
        {
            link.State = LinkState.Active;
        }
        
        // Check for breaking
        bool shouldBreak = stretchRatio > link.BreakThreshold;
        if (!shouldBreak && link.BreakForce > 0)
        {
            shouldBreak = link.CurrentForce > link.BreakForce;
        }
        
        if (shouldBreak)
        {
            BreakLink(link);
        }
    }
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void SolveRopeLink(LinkConstraint link, float dt)
    {
        // Calculate effective length including slack
        float effectiveRestLength = link.CurrentRestLength * (1f + link.Slack);
        
        // Only apply tension if stretched beyond slack
        if (link.CurrentLength <= effectiveRestLength)
        {
            link.State = LinkState.Slack;
            link.CurrentForce = 0;
            link.AccumulatedImpulse = 0;
            return;
        }
        
        link.State = LinkState.Active;
        
        // Solve like a rigid constraint but only resist stretching
        Vector3 velA = link.GetVelocityAtAnchorA();
        Vector3 velB = link.GetVelocityAtAnchorB();
        float relVel = Vector3.Dot(velB - velA, link.Axis);
        
        // Only correct if bodies are moving apart (positive relative velocity means stretching)
        float error = link.CurrentLength - effectiveRestLength;
        float biasFactor = BiasFactor / dt;
        float bias = biasFactor * Math.Max(0, error - Slop);
        
        float lambda = -(relVel + bias) * link.EffectiveMass;
        
        // Only allow tension (positive lambda pulls bodies together)
        float oldAccum = link.AccumulatedImpulse;
        link.AccumulatedImpulse = Math.Max(0, oldAccum + lambda);  // Clamp to positive only
        lambda = link.AccumulatedImpulse - oldAccum;
        
        // Also add spring force for more realistic behavior
        float springForce = -link.Stiffness * error * dt;
        float dampingForce = -link.Damping * relVel * dt;
        lambda += (springForce + dampingForce);
        
        // Clamp total
        lambda = Math.Max(0, Math.Min(lambda, MaxImpulse));
        
        link.ApplyImpulse(lambda);
        link.CurrentForce = Math.Abs(lambda) / dt;
    }
    
    // ===== Helper Methods =====
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private float GetConstraintError(LinkConstraint link)
    {
        // Error is the difference between current and rest length
        return link.CurrentLength - link.CurrentRestLength;
    }
    
    private void BreakLink(LinkConstraint link)
    {
        link.State = LinkState.Broken;
        
        _pendingBreakEvents.Add(new LinkBreakEvent
        {
            LinkId = link.Id,
            ChainId = link.ChainId,
            MeshId = link.MeshId,
            EntityA = link.EntityA,
            EntityB = link.EntityB ?? -1,
            BreakPoint = link.Midpoint,
            BreakDirection = link.Axis,
            BreakForce = link.CurrentForce,
            StretchRatio = link.StretchRatio
        });
        
        // Handle chain breaks
        if (link.ChainId >= 0)
        {
            var chain = _chains.FirstOrDefault(c => c.Id == link.ChainId);
            if (chain != null)
            {
                chain.OnLinkBroken(link.IndexInCollection);
                
                _pendingChainBreakEvents.Add(new ChainBreakEvent
                {
                    OriginalChainId = chain.Id,
                    NewChainIdA = -1,  // TODO: Create new chains from segments
                    NewChainIdB = -1,
                    BreakIndex = link.IndexInCollection,
                    BreakPoint = link.Midpoint
                });
            }
        }
        
        // Handle mesh damage
        if (link.MeshId >= 0)
        {
            var mesh = _meshes.FirstOrDefault(m => m.Id == link.MeshId);
            if (mesh != null)
            {
                float prevIntegrity = mesh.GetIntegrity();
                mesh.OnLinkBroken(link);
                float newIntegrity = mesh.GetIntegrity();
                
                if (prevIntegrity - newIntegrity > 0.01f)  // Significant damage
                {
                    _pendingMeshDamageEvents.Add(new MeshDamageEvent
                    {
                        MeshId = mesh.Id,
                        LinksLost = 1,
                        Integrity = newIntegrity,
                        PreviousIntegrity = prevIntegrity,
                        DamageCenter = link.Midpoint,
                        DamageRadius = link.CurrentLength
                    });
                }
            }
        }
    }
    
    // ===== Query Methods =====
    
    /// <summary>
    /// Get all active (non-broken) links
    /// </summary>
    public IEnumerable<LinkConstraint> GetActiveLinks()
    {
        return _links.Where(l => l.State != LinkState.Broken);
    }
    
    /// <summary>
    /// Get all links for an entity
    /// </summary>
    public IEnumerable<LinkConstraint> GetLinksForEntity(int entityId)
    {
        return _links.Where(l => l.EntityA == entityId || l.EntityB == entityId);
    }
    
    /// <summary>
    /// Get a link by ID
    /// </summary>
    public LinkConstraint? GetLink(int linkId)
    {
        return _links.FirstOrDefault(l => l.Id == linkId);
    }
    
    /// <summary>
    /// Get a chain by ID
    /// </summary>
    public LinkChain? GetChain(int chainId)
    {
        return _chains.FirstOrDefault(c => c.Id == chainId);
    }
    
    /// <summary>
    /// Get a mesh by ID
    /// </summary>
    public LinkMesh? GetMesh(int meshId)
    {
        return _meshes.FirstOrDefault(m => m.Id == meshId);
    }
}
