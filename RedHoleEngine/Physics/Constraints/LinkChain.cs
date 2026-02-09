using System.Numerics;

namespace RedHoleEngine.Physics.Constraints;

/// <summary>
/// Helper for creating and managing a chain of link constraints.
/// Useful for ropes, chains, and segmented connections.
/// </summary>
public class LinkChain
{
    /// <summary>
    /// Unique identifier for this chain
    /// </summary>
    public int Id;

    /// <summary>
    /// Links that make up this chain (in order)
    /// </summary>
    public List<LinkConstraint> Links { get; } = new();

    /// <summary>
    /// Entity IDs for each node in the chain
    /// </summary>
    public List<int> NodeEntities { get; } = new();

    /// <summary>
    /// Default link type for this chain
    /// </summary>
    public LinkType Type { get; set; } = LinkType.Rope;

    /// <summary>
    /// Create a rope chain with uniform segment length
    /// </summary>
    public static LinkChain CreateRope(
        IReadOnlyList<int> nodeEntities,
        float segmentLength,
        Vector3? localAnchor = null,
        float stiffness = 5000f,
        float damping = 50f,
        float slack = 0f)
    {
        return CreateChain(
            nodeEntities,
            segmentLength,
            LinkType.Rope,
            localAnchor ?? Vector3.Zero,
            stiffness,
            damping,
            slack);
    }

    /// <summary>
    /// Create a rigid chain with uniform segment length
    /// </summary>
    public static LinkChain CreateRigidChain(
        IReadOnlyList<int> nodeEntities,
        float segmentLength,
        Vector3? localAnchor = null)
    {
        return CreateChain(
            nodeEntities,
            segmentLength,
            LinkType.Rigid,
            localAnchor ?? Vector3.Zero,
            stiffness: float.MaxValue,
            damping: 0f,
            slack: 0f);
    }

    /// <summary>
    /// Create a chain using per-node local anchors
    /// </summary>
    public static LinkChain CreateFromPath(
        IReadOnlyList<int> nodeEntities,
        IReadOnlyList<Vector3> localAnchors,
        float segmentLength,
        LinkType type = LinkType.Rope,
        float stiffness = 1000f,
        float damping = 10f,
        float slack = 0f)
    {
        if (nodeEntities.Count != localAnchors.Count)
            throw new ArgumentException("Node entities and anchors must have the same length");

        var chain = new LinkChain { Type = type };
        chain.NodeEntities.AddRange(nodeEntities);

        for (int i = 0; i < nodeEntities.Count - 1; i++)
        {
            var link = CreateConstraint(
                type,
                nodeEntities[i],
                nodeEntities[i + 1],
                localAnchors[i],
                localAnchors[i + 1],
                segmentLength,
                stiffness,
                damping,
                slack);
            link.IndexInCollection = chain.Links.Count;
            chain.Links.Add(link);
        }

        return chain;
    }

    /// <summary>
    /// Break a specific link in the chain
    /// </summary>
    public void Break(int segmentIndex)
    {
        if (segmentIndex < 0 || segmentIndex >= Links.Count)
            return;

        Links[segmentIndex].State = LinkState.Broken;
    }

    /// <summary>
    /// Detach one end of the chain by breaking the first or last link
    /// </summary>
    public void DetachEnd(bool detachA)
    {
        if (Links.Count == 0)
            return;

        Break(detachA ? 0 : Links.Count - 1);
    }

    /// <summary>
    /// Notify the chain that a link was broken by the solver
    /// </summary>
    public void OnLinkBroken(int segmentIndex)
    {
        Break(segmentIndex);
    }

    /// <summary>
    /// Get world-space points along the chain for rendering
    /// </summary>
    public List<Vector3> GetPoints()
    {
        var points = new List<Vector3>(Links.Count + 1);

        if (Links.Count == 0)
            return points;

        points.Add(Links[0].WorldAnchorA);
        foreach (var link in Links)
        {
            points.Add(link.WorldAnchorB);
        }

        return points;
    }

    /// <summary>
    /// Get points with tangents for smooth rendering
    /// </summary>
    public List<ChainPoint> GetPointsWithTangents()
    {
        var points = GetPoints();
        var result = new List<ChainPoint>(points.Count);

        if (points.Count == 0)
            return result;

        for (int i = 0; i < points.Count; i++)
        {
            Vector3 tangent;
            if (i == 0)
            {
                tangent = points.Count > 1 ? Vector3.Normalize(points[1] - points[0]) : Vector3.UnitY;
            }
            else if (i == points.Count - 1)
            {
                tangent = Vector3.Normalize(points[i] - points[i - 1]);
            }
            else
            {
                var prev = Vector3.Normalize(points[i] - points[i - 1]);
                var next = Vector3.Normalize(points[i + 1] - points[i]);
                tangent = Vector3.Normalize(prev + next);
            }

            result.Add(new ChainPoint
            {
                Position = points[i],
                Tangent = tangent
            });
        }

        return result;
    }

    private static LinkChain CreateChain(
        IReadOnlyList<int> nodeEntities,
        float segmentLength,
        LinkType type,
        Vector3 localAnchor,
        float stiffness,
        float damping,
        float slack)
    {
        var chain = new LinkChain { Type = type };
        chain.NodeEntities.AddRange(nodeEntities);

        for (int i = 0; i < nodeEntities.Count - 1; i++)
        {
            var link = CreateConstraint(
                type,
                nodeEntities[i],
                nodeEntities[i + 1],
                localAnchor,
                localAnchor,
                segmentLength,
                stiffness,
                damping,
                slack);
            link.IndexInCollection = chain.Links.Count;
            chain.Links.Add(link);
        }

        return chain;
    }

    private static LinkConstraint CreateConstraint(
        LinkType type,
        int entityA,
        int entityB,
        Vector3 anchorA,
        Vector3 anchorB,
        float length,
        float stiffness,
        float damping,
        float slack)
    {
        return type switch
        {
            LinkType.Rigid => LinkConstraint.Rigid(entityA, entityB, anchorA, anchorB, length),
            LinkType.Elastic => LinkConstraint.Elastic(entityA, entityB, anchorA, anchorB, length, stiffness, damping),
            LinkType.Plastic => LinkConstraint.Plastic(entityA, entityB, anchorA, anchorB, length, stiffness),
            LinkType.Rope => LinkConstraint.Rope(entityA, entityB, anchorA, anchorB, length, stiffness, damping, slack),
            _ => LinkConstraint.Rope(entityA, entityB, anchorA, anchorB, length, stiffness, damping, slack)
        };
    }
}

/// <summary>
/// A chain point with tangent for smooth rendering
/// </summary>
public struct ChainPoint
{
    public Vector3 Position;
    public Vector3 Tangent;
}
