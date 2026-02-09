using RedHoleEngine.Components;
using RedHoleEngine.Core.ECS;
using RedHoleEngine.Physics.Constraints;
using RedHoleEngine.Rendering.Debug;

namespace RedHoleEngine.Physics;

/// <summary>
/// ECS system that manages link constraints and debug visualization.
/// </summary>
public class LinkSystem : GameSystem
{
    private PhysicsSystem? _physicsSystem;
    private ConstraintSolver? _solver;
    private DebugDrawManager? _debugDraw;
    private bool _eventsHooked;
    private readonly HashSet<int> _registeredEntities = new();

    /// <summary>
    /// Priority for execution order (after physics step)
    /// </summary>
    public override int Priority => -99;

    /// <summary>
    /// Enable/disable debug drawing of links
    /// </summary>
    public bool DebugDrawLinks { get; set; }

    /// <summary>
    /// Set the debug draw manager
    /// </summary>
    public void SetDebugDraw(DebugDrawManager? debugDraw)
    {
        _debugDraw = debugDraw;
    }

    /// <summary>
    /// Forwarded events from the constraint solver
    /// </summary>
    public event LinkBreakHandler? LinkBreak;
    public event LinkYieldHandler? LinkYield;
    public event LinkStretchHandler? LinkStretch;
    public event ChainBreakHandler? ChainBreak;
    public event MeshDamageHandler? MeshDamage;

    public override void Update(float deltaTime)
    {
        if (World == null)
            return;

        TryResolveSolver();
        if (_solver == null)
            return;

        RegisterNewLinks();
        RemoveMissingLinks();

        if (DebugDrawLinks && _debugDraw != null)
        {
            DrawDebugLinks();
        }
    }

    /// <summary>
    /// Add a standalone link to the solver
    /// </summary>
    public LinkConstraint AddLink(LinkConstraint link)
    {
        TryResolveSolver();
        if (_solver == null)
            throw new InvalidOperationException("Constraint solver is not available");

        return _solver.AddLink(link);
    }

    /// <summary>
    /// Add a chain to the solver
    /// </summary>
    public LinkChain AddChain(LinkChain chain)
    {
        TryResolveSolver();
        if (_solver == null)
            throw new InvalidOperationException("Constraint solver is not available");

        return _solver.AddChain(chain);
    }

    /// <summary>
    /// Add a mesh to the solver
    /// </summary>
    public LinkMesh AddMesh(LinkMesh mesh)
    {
        TryResolveSolver();
        if (_solver == null)
            throw new InvalidOperationException("Constraint solver is not available");

        return _solver.AddMesh(mesh);
    }

    private void TryResolveSolver()
    {
        if (_solver != null)
            return;

        _physicsSystem = World?.GetSystem<PhysicsSystem>();
        if (_physicsSystem == null)
            return;

        _solver = _physicsSystem.PhysicsWorld.ConstraintSolver;

        if (!_eventsHooked)
        {
            _solver.OnLinkBreak += evt => LinkBreak?.Invoke(evt);
            _solver.OnLinkYield += evt => LinkYield?.Invoke(evt);
            _solver.OnLinkStretch += evt => LinkStretch?.Invoke(evt);
            _solver.OnChainBreak += evt => ChainBreak?.Invoke(evt);
            _solver.OnMeshDamage += evt => MeshDamage?.Invoke(evt);
            _eventsHooked = true;
        }
    }

    private void RegisterNewLinks()
    {
        foreach (var entity in World!.Query<LinkComponent>())
        {
            if (_registeredEntities.Contains(entity.Id))
                continue;

            ref var linkComponent = ref World.GetComponent<LinkComponent>(entity);
            if (!linkComponent.AutoRegister || linkComponent.Constraint == null)
                continue;

            var link = _solver!.AddLink(linkComponent.Constraint);
            linkComponent.LinkId = link.Id;
            linkComponent.ChainId = link.ChainId;
            linkComponent.MeshId = link.MeshId;

            _registeredEntities.Add(entity.Id);
        }
    }

    private void RemoveMissingLinks()
    {
        if (_registeredEntities.Count == 0)
            return;

        var toRemove = new List<int>();
        foreach (var entityId in _registeredEntities)
        {
            if (!World!.TryGetEntity(entityId, out var entity) || !World.HasComponent<LinkComponent>(entity))
            {
                toRemove.Add(entityId);
                continue;
            }

            ref var linkComponent = ref World.GetComponent<LinkComponent>(entity);
            if (!linkComponent.AutoRegister || linkComponent.Constraint == null)
            {
                toRemove.Add(entityId);
            }
        }

        foreach (var entityId in toRemove)
        {
            if (World!.TryGetEntity(entityId, out var entity) && World.HasComponent<LinkComponent>(entity))
            {
                ref var linkComponent = ref World.GetComponent<LinkComponent>(entity);
                if (linkComponent.LinkId > 0)
                {
                    _solver!.RemoveLink(linkComponent.LinkId);
                }
            }
            _registeredEntities.Remove(entityId);
        }
    }

    private void DrawDebugLinks()
    {
        foreach (var link in _solver!.Links)
        {
            var color = GetLinkColor(link);
            if (link.State == LinkState.Slack)
            {
                color = DebugColor.Gray.WithAlpha(0.3f);
            }

            _debugDraw!.DrawLine(link.WorldAnchorA, link.WorldAnchorB, color);
            _debugDraw.DrawPoint(link.Midpoint, color.WithAlpha(0.7f), 6f);
        }
    }

    private static DebugColor GetLinkColor(LinkConstraint link)
    {
        if (link.State == LinkState.Broken)
            return DebugColor.Red.WithAlpha(0.6f);

        return link.Type switch
        {
            LinkType.Rigid => DebugColor.Gray,
            LinkType.Elastic => DebugColor.Green,
            LinkType.Plastic => DebugColor.Yellow,
            LinkType.Rope => DebugColor.Cyan,
            _ => DebugColor.White
        };
    }
}
