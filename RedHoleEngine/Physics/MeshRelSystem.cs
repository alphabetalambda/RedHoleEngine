using System.Collections.Generic;
using System.Numerics;
using RedHoleEngine.Components;
using RedHoleEngine.Core.ECS;
using RedHoleEngine.Physics.Constraints;
using RedHoleEngine.Resources;

namespace RedHoleEngine.Physics;

/// <summary>
/// ECS system that builds deformable meshes using link constraints.
/// Creates per-vertex physics nodes and updates mesh vertices from simulation.
/// </summary>
public class MeshRelSystem : GameSystem
{
    private PhysicsSystem? _physicsSystem;
    private LinkSystem? _linkSystem;
    private ConstraintSolver? _solver;
    private ResourceManager? _resources;
    private readonly HashSet<int> _registeredEntities = new();

    /// <summary>
    /// Priority runs after physics and link solving
    /// </summary>
    public override int Priority => -98;

    /// <summary>
    /// Provide ResourceManager to allow mesh cloning
    /// </summary>
    public void Initialize(ResourceManager resources)
    {
        _resources = resources;
    }

    public override void Update(float deltaTime)
    {
        if (World == null)
            return;

        TryResolveDependencies();
        if (_solver == null)
            return;

        RegisterNewMeshes();
        UpdateMeshes(deltaTime);
        CleanupMissingMeshes();
    }

    private void TryResolveDependencies()
    {
        if (_solver != null)
            return;

        _physicsSystem = World?.GetSystem<PhysicsSystem>();
        _linkSystem = World?.GetSystem<LinkSystem>();
        if (_physicsSystem == null)
            return;

        _solver = _physicsSystem.PhysicsWorld.ConstraintSolver;
    }

    private void RegisterNewMeshes()
    {
        foreach (var entity in World!.Query<MeshComponent, MeshRelComponent, TransformComponent>())
        {
            if (_registeredEntities.Contains(entity.Id))
                continue;

            ref var meshComponent = ref World.GetComponent<MeshComponent>(entity);
            ref var relComponent = ref World.GetComponent<MeshRelComponent>(entity);
            ref var transform = ref World.GetComponent<TransformComponent>(entity);

            if (!relComponent.AutoRegister)
                continue;

            relComponent.NodeEntities ??= new List<int>();
            relComponent.PinnedVertexIndices ??= new List<int>();

            var mesh = meshComponent.MeshHandle.Get();
            if (mesh == null)
                continue;

            if (relComponent.CloneMeshOnStart && _resources != null)
            {
                var cloned = new Mesh(mesh.Vertices.ToArray(), mesh.Indices.ToArray(), $"{mesh.Name}_MeshRel_{entity.Id}");
                string id = $"{meshComponent.MeshHandle.Id}_meshrel_{entity.Id}";
                _resources.Add(id, cloned);
                meshComponent.MeshHandle = _resources.GetHandle<Mesh>(id);
                mesh = cloned;
            }

            relComponent.RuntimeMesh = mesh;
            relComponent.NodeEntities.Clear();

            var nodeEntities = CreateNodeEntities(mesh, transform, relComponent);
            relComponent.NodeEntities.AddRange(nodeEntities);

            var linkMesh = BuildLinkMesh(mesh, transform, relComponent, nodeEntities);
            if (_linkSystem != null)
            {
                linkMesh = _linkSystem.AddMesh(linkMesh);
            }
            else
            {
                linkMesh = _solver!.AddMesh(linkMesh);
            }

            relComponent.MeshId = linkMesh.Id;
            relComponent.Initialized = true;
            relComponent.NormalUpdateTimer = 0f;

            _registeredEntities.Add(entity.Id);
        }
    }

    private List<int> CreateNodeEntities(Mesh mesh, TransformComponent transform, MeshRelComponent relComponent)
    {
        var nodes = new List<int>(mesh.Vertices.Length);
        var pinned = relComponent.PinnedVertexIndices;

        for (int i = 0; i < mesh.Vertices.Length; i++)
        {
            var vertex = mesh.Vertices[i];
            var worldPos = transform.Transform.TransformPoint(vertex.Position);

            var nodeEntity = World!.CreateEntity();
            World.AddComponent(nodeEntity, new TransformComponent(worldPos));

            bool isPinned = pinned != null && pinned.Contains(i);
            if (isPinned)
            {
                var rb = RigidBodyComponent.CreateStatic();
                rb.UseGravity = false;
                World.AddComponent(nodeEntity, rb);
            }
            else
            {
                var rb = RigidBodyComponent.CreateDynamic(relComponent.NodeMass);
                rb.UseGravity = relComponent.UseGravity;
                rb.LinearDamping = relComponent.NodeLinearDamping;
                rb.AngularDamping = relComponent.NodeAngularDamping;
                rb.FreezeRotationX = true;
                rb.FreezeRotationY = true;
                rb.FreezeRotationZ = true;
                World.AddComponent(nodeEntity, rb);
            }

            nodes.Add(nodeEntity.Id);
        }

        return nodes;
    }

    private static LinkMesh BuildLinkMesh(
        Mesh mesh,
        TransformComponent transform,
        MeshRelComponent relComponent,
        IReadOnlyList<int> nodeEntities)
    {
        var linkMesh = new LinkMesh();
        linkMesh.NodeEntities.AddRange(nodeEntities);

        var vertices = mesh.Vertices;
        var indices = mesh.Indices;

        var initialWorldPositions = new Vector3[vertices.Length];
        for (int i = 0; i < vertices.Length; i++)
        {
            initialWorldPositions[i] = transform.Transform.TransformPoint(vertices[i].Position);
        }

        var edges = new HashSet<(int A, int B)>();
        var edgeOpposites = new Dictionary<(int A, int B), int>();

        for (int i = 0; i + 2 < indices.Length; i += 3)
        {
            int i0 = (int)indices[i];
            int i1 = (int)indices[i + 1];
            int i2 = (int)indices[i + 2];

            AddEdge(edges, i0, i1);
            AddEdge(edges, i1, i2);
            AddEdge(edges, i2, i0);

            AddOpposite(edgeOpposites, i0, i1, i2, linkMesh, relComponent, nodeEntities, initialWorldPositions);
            AddOpposite(edgeOpposites, i1, i2, i0, linkMesh, relComponent, nodeEntities, initialWorldPositions);
            AddOpposite(edgeOpposites, i2, i0, i1, linkMesh, relComponent, nodeEntities, initialWorldPositions);
        }

        foreach (var edge in edges)
        {
            int a = edge.A;
            int b = edge.B;
            float restLength = Vector3.Distance(initialWorldPositions[a], initialWorldPositions[b]);
            var link = CreateLink(relComponent, nodeEntities[a], nodeEntities[b], restLength, relComponent.Stiffness, relComponent.Damping);
            link.IndexInCollection = linkMesh.StructuralLinks.Count;
            linkMesh.StructuralLinks.Add(link);
        }

        return linkMesh;
    }

    private static void AddEdge(HashSet<(int A, int B)> edges, int a, int b)
    {
        if (a < b)
            edges.Add((a, b));
        else if (b < a)
            edges.Add((b, a));
    }

    private static void AddOpposite(
        Dictionary<(int A, int B), int> edgeOpposites,
        int a,
        int b,
        int opposite,
        LinkMesh linkMesh,
        MeshRelComponent relComponent,
        IReadOnlyList<int> nodeEntities,
        IReadOnlyList<Vector3> initialWorldPositions)
    {
        if (!relComponent.IncludeBendLinks)
            return;

        var edge = a < b ? (a, b) : (b, a);
        if (edgeOpposites.TryGetValue(edge, out var otherOpposite))
        {
            if (otherOpposite == opposite)
                return;

            float restLength = Vector3.Distance(initialWorldPositions[opposite], initialWorldPositions[otherOpposite]);
            float bendStiffness = relComponent.Stiffness * relComponent.BendStiffnessMultiplier;
            var link = CreateLink(relComponent, nodeEntities[opposite], nodeEntities[otherOpposite], restLength, bendStiffness, relComponent.Damping);
            link.IndexInCollection = linkMesh.BendLinks.Count;
            linkMesh.BendLinks.Add(link);
        }
        else
        {
            edgeOpposites[edge] = opposite;
        }
    }

    private static LinkConstraint CreateLink(
        MeshRelComponent relComponent,
        int entityA,
        int entityB,
        float restLength,
        float stiffness,
        float damping)
    {
        LinkConstraint link = relComponent.DeformationType switch
        {
            LinkType.Plastic => LinkConstraint.Plastic(entityA, entityB, Vector3.Zero, Vector3.Zero, restLength, stiffness, relComponent.YieldThreshold, relComponent.BreakThreshold),
            _ => LinkConstraint.Elastic(entityA, entityB, Vector3.Zero, Vector3.Zero, restLength, stiffness, damping)
        };

        if (relComponent.DeformationType == LinkType.Plastic)
        {
            link.Damping = damping;
            link.PlasticRate = relComponent.PlasticRate;
        }

        return link;
    }

    private void UpdateMeshes(float deltaTime)
    {
        foreach (var entity in World!.Query<MeshComponent, MeshRelComponent, TransformComponent>())
        {
            if (!_registeredEntities.Contains(entity.Id))
                continue;

            ref var relComponent = ref World.GetComponent<MeshRelComponent>(entity);
            ref var transform = ref World.GetComponent<TransformComponent>(entity);

            var mesh = relComponent.RuntimeMesh;
            if (mesh == null || relComponent.NodeEntities == null || relComponent.NodeEntities.Count == 0)
                continue;

            var vertices = mesh.Vertices;

            for (int i = 0; i < vertices.Length && i < relComponent.NodeEntities.Count; i++)
            {
                int nodeEntityId = relComponent.NodeEntities[i];
                var body = _physicsSystem!.PhysicsWorld.GetBodyByEntityId(nodeEntityId);
                if (body == null)
                    continue;

                Vector3 localPos = transform.Transform.InverseTransformPoint(body.Position);
                var v = vertices[i];
                v.Position = localPos;
                vertices[i] = v;
            }

            if (relComponent.RecalculateNormals)
            {
                bool doUpdate = relComponent.NormalUpdateInterval <= 0f;
                if (!doUpdate)
                {
                    relComponent.NormalUpdateTimer -= deltaTime;
                    if (relComponent.NormalUpdateTimer <= 0f)
                    {
                        doUpdate = true;
                        relComponent.NormalUpdateTimer = relComponent.NormalUpdateInterval;
                    }
                }

                if (doUpdate)
                {
                    mesh.RecalculateNormals();
                    if (relComponent.RecalculateTangents)
                    {
                        mesh.RecalculateTangents();
                    }
                    mesh.RecalculateBounds();
                }
            }
        }
    }

    private void CleanupMissingMeshes()
    {
        if (_registeredEntities.Count == 0)
            return;

        var toRemove = new List<int>();
        foreach (var entityId in _registeredEntities)
        {
            if (!World!.TryGetEntity(entityId, out var entity) || !World.HasComponent<MeshRelComponent>(entity))
            {
                toRemove.Add(entityId);
            }
        }

        foreach (var entityId in toRemove)
        {
            if (World!.TryGetEntity(entityId, out var entity) && World.HasComponent<MeshRelComponent>(entity))
            {
                ref var relComponent = ref World.GetComponent<MeshRelComponent>(entity);
                if (relComponent.MeshId >= 0)
                {
                    _solver!.RemoveMesh(relComponent.MeshId);
                }

                if (relComponent.NodeEntities != null)
                {
                    foreach (var nodeId in relComponent.NodeEntities)
                    {
                        if (World.TryGetEntity(nodeId, out var nodeEntity))
                        {
                            World.DestroyEntity(nodeEntity);
                        }
                    }

                    relComponent.NodeEntities.Clear();
                }
                relComponent.Initialized = false;
                relComponent.MeshId = -1;
                relComponent.RuntimeMesh = null;
            }

            _registeredEntities.Remove(entityId);
        }
    }
}
