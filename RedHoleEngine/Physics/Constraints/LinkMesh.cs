using System.Numerics;

namespace RedHoleEngine.Physics.Constraints;

/// <summary>
/// Helper for creating and managing a mesh of link constraints.
/// Useful for cloth, soft bodies, and destructible grids.
/// </summary>
public class LinkMesh
{
    /// <summary>
    /// Unique identifier for this mesh
    /// </summary>
    public int Id;

    /// <summary>
    /// Entity IDs for each node in the mesh
    /// </summary>
    public List<int> NodeEntities { get; } = new();

    /// <summary>
    /// Structural links (primary edges)
    /// </summary>
    public List<LinkConstraint> StructuralLinks { get; } = new();

    /// <summary>
    /// Shear links (diagonals)
    /// </summary>
    public List<LinkConstraint> ShearLinks { get; } = new();

    /// <summary>
    /// Bend links (secondary stiffness)
    /// </summary>
    public List<LinkConstraint> BendLinks { get; } = new();

    /// <summary>
    /// Create a simple cloth grid using elastic links
    /// </summary>
    public static LinkMesh CreateCloth(
        IReadOnlyList<int> nodeEntities,
        int width,
        int height,
        float spacing,
        float stiffness = 800f,
        float damping = 10f,
        bool includeShear = true,
        bool includeBend = true)
    {
        return CreateGrid(
            nodeEntities,
            width,
            height,
            spacing,
            LinkType.Elastic,
            stiffness,
            damping,
            yieldThreshold: 1.1f,
            breakThreshold: 1.5f,
            includeShear,
            includeBend);
    }

    /// <summary>
    /// Create a soft body grid with elastic links
    /// </summary>
    public static LinkMesh CreateSoftBody(
        IReadOnlyList<int> nodeEntities,
        int width,
        int height,
        float spacing,
        float stiffness = 1200f,
        float damping = 20f)
    {
        return CreateGrid(
            nodeEntities,
            width,
            height,
            spacing,
            LinkType.Elastic,
            stiffness,
            damping,
            yieldThreshold: 1.1f,
            breakThreshold: 1.8f,
            includeShear: true,
            includeBend: true);
    }

    /// <summary>
    /// Create a destructible grid using plastic links
    /// </summary>
    public static LinkMesh CreateDestructibleGrid(
        IReadOnlyList<int> nodeEntities,
        int width,
        int height,
        float spacing,
        float stiffness = 1500f,
        float damping = 10f,
        float yieldThreshold = 1.15f,
        float breakThreshold = 1.4f)
    {
        return CreateGrid(
            nodeEntities,
            width,
            height,
            spacing,
            LinkType.Plastic,
            stiffness,
            damping,
            yieldThreshold,
            breakThreshold,
            includeShear: true,
            includeBend: true);
    }

    /// <summary>
    /// Apply damage to the mesh by breaking nearby links
    /// </summary>
    public void ApplyImpact(Vector3 worldPoint, float radius, float force)
    {
        if (radius <= 0f || force <= 0f)
            return;

        float radiusSq = radius * radius;

        foreach (var link in GetAllLinks())
        {
            if (link.State == LinkState.Broken)
                continue;

            var delta = link.Midpoint - worldPoint;
            if (delta.LengthSquared() > radiusSq)
                continue;

            if (link.BreakForce > 0f)
            {
                if (force < link.BreakForce)
                    continue;
            }
            else if (link.BreakThreshold > 1f && link.CurrentLength > 0f)
            {
                if (link.StretchRatio < link.BreakThreshold)
                    continue;
            }
            else if (force < 1f)
            {
                continue;
            }

            link.State = LinkState.Broken;
        }
    }

    /// <summary>
    /// Get structural integrity (0-1) based on unbroken links
    /// </summary>
    public float GetIntegrity()
    {
        int total = StructuralLinks.Count + ShearLinks.Count + BendLinks.Count;
        if (total == 0) return 1f;

        int active = 0;
        foreach (var link in StructuralLinks)
            if (link.State != LinkState.Broken) active++;
        foreach (var link in ShearLinks)
            if (link.State != LinkState.Broken) active++;
        foreach (var link in BendLinks)
            if (link.State != LinkState.Broken) active++;

        return active / (float)total;
    }

    /// <summary>
    /// Notify the mesh that a link was broken
    /// </summary>
    public void OnLinkBroken(LinkConstraint link)
    {
        link.State = LinkState.Broken;
    }

    /// <summary>
    /// Enumerate all links in the mesh
    /// </summary>
    public IEnumerable<LinkConstraint> GetAllLinks()
    {
        foreach (var link in StructuralLinks) yield return link;
        foreach (var link in ShearLinks) yield return link;
        foreach (var link in BendLinks) yield return link;
    }

    private static LinkMesh CreateGrid(
        IReadOnlyList<int> nodeEntities,
        int width,
        int height,
        float spacing,
        LinkType linkType,
        float stiffness,
        float damping,
        float yieldThreshold,
        float breakThreshold,
        bool includeShear,
        bool includeBend)
    {
        int expectedCount = width * height;
        if (nodeEntities.Count < expectedCount)
            throw new ArgumentException("Not enough node entities for grid size");

        var mesh = new LinkMesh();
        mesh.NodeEntities.AddRange(nodeEntities);

        float diagonal = spacing * MathF.Sqrt(2f);

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int index = y * width + x;

                // Structural links
                if (x + 1 < width)
                {
                    AddLink(
                        mesh.StructuralLinks,
                        linkType,
                        nodeEntities[index],
                        nodeEntities[index + 1],
                        spacing,
                        stiffness,
                        damping,
                        yieldThreshold,
                        breakThreshold);
                }

                if (y + 1 < height)
                {
                    AddLink(
                        mesh.StructuralLinks,
                        linkType,
                        nodeEntities[index],
                        nodeEntities[index + width],
                        spacing,
                        stiffness,
                        damping,
                        yieldThreshold,
                        breakThreshold);
                }

                if (includeShear)
                {
                    if (x + 1 < width && y + 1 < height)
                    {
                        AddLink(
                            mesh.ShearLinks,
                            linkType,
                            nodeEntities[index],
                            nodeEntities[index + width + 1],
                            diagonal,
                            stiffness,
                            damping,
                            yieldThreshold,
                            breakThreshold);
                    }

                    if (x - 1 >= 0 && y + 1 < height)
                    {
                        AddLink(
                            mesh.ShearLinks,
                            linkType,
                            nodeEntities[index],
                            nodeEntities[index + width - 1],
                            diagonal,
                            stiffness,
                            damping,
                            yieldThreshold,
                            breakThreshold);
                    }
                }

                if (includeBend)
                {
                    if (x + 2 < width)
                    {
                        AddLink(
                            mesh.BendLinks,
                            linkType,
                            nodeEntities[index],
                            nodeEntities[index + 2],
                            spacing * 2f,
                            stiffness,
                            damping,
                            yieldThreshold,
                            breakThreshold);
                    }

                    if (y + 2 < height)
                    {
                        AddLink(
                            mesh.BendLinks,
                            linkType,
                            nodeEntities[index],
                            nodeEntities[index + 2 * width],
                            spacing * 2f,
                            stiffness,
                            damping,
                            yieldThreshold,
                            breakThreshold);
                    }
                }
            }
        }

        return mesh;
    }

    private static void AddLink(
        List<LinkConstraint> list,
        LinkType type,
        int entityA,
        int entityB,
        float length,
        float stiffness,
        float damping,
        float yieldThreshold,
        float breakThreshold)
    {
        var link = type switch
        {
            LinkType.Rigid => LinkConstraint.Rigid(entityA, entityB, Vector3.Zero, Vector3.Zero, length),
            LinkType.Elastic => LinkConstraint.Elastic(entityA, entityB, Vector3.Zero, Vector3.Zero, length, stiffness, damping),
            LinkType.Plastic => LinkConstraint.Plastic(entityA, entityB, Vector3.Zero, Vector3.Zero, length, stiffness, yieldThreshold, breakThreshold),
            LinkType.Rope => LinkConstraint.Rope(entityA, entityB, Vector3.Zero, Vector3.Zero, length, stiffness, damping, 0f),
            _ => LinkConstraint.Elastic(entityA, entityB, Vector3.Zero, Vector3.Zero, length, stiffness, damping)
        };

        link.IndexInCollection = list.Count;
        list.Add(link);
    }
}
