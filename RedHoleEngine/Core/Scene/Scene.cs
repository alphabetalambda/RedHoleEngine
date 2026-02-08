using RedHoleEngine.Core.ECS;

namespace RedHoleEngine.Core.Scene;

/// <summary>
/// Represents a game scene containing a hierarchy of nodes.
/// Manages the scene graph and provides utilities for scene manipulation.
/// </summary>
public class Scene
{
    /// <summary>
    /// Name of the scene
    /// </summary>
    public string Name { get; set; }
    
    /// <summary>
    /// The ECS world for this scene
    /// </summary>
    public World World { get; }
    
    /// <summary>
    /// Root nodes of the scene (nodes without parents)
    /// </summary>
    private readonly List<SceneNode> _rootNodes = new();
    public IReadOnlyList<SceneNode> RootNodes => _rootNodes;
    
    /// <summary>
    /// All nodes indexed by entity for fast lookup
    /// </summary>
    private readonly Dictionary<Entity, SceneNode> _nodesByEntity = new();

    public Scene(string name = "Untitled")
    {
        Name = name;
        World = new World();
    }

    public Scene(World world, string name = "Untitled")
    {
        Name = name;
        World = world;
    }

    #region Node Management

    /// <summary>
    /// Create a new node in this scene
    /// </summary>
    public SceneNode CreateNode(string name = "Node")
    {
        var node = new SceneNode(World, name);
        _rootNodes.Add(node);
        _nodesByEntity[node.Entity] = node;
        return node;
    }

    /// <summary>
    /// Create a node as a child of another
    /// </summary>
    public SceneNode CreateNode(SceneNode parent, string name = "Node")
    {
        var node = CreateNode(name);
        node.SetParent(parent);
        return node;
    }

    /// <summary>
    /// Get a node by its entity
    /// </summary>
    public SceneNode? GetNode(Entity entity)
    {
        return _nodesByEntity.GetValueOrDefault(entity);
    }

    /// <summary>
    /// Register an externally created node
    /// </summary>
    public void RegisterNode(SceneNode node)
    {
        if (node.Parent == null && !_rootNodes.Contains(node))
        {
            _rootNodes.Add(node);
        }
        _nodesByEntity[node.Entity] = node;
    }

    /// <summary>
    /// Remove a node from the scene
    /// </summary>
    public void DestroyNode(SceneNode node)
    {
        _nodesByEntity.Remove(node.Entity);
        _rootNodes.Remove(node);
        node.Destroy();
    }

    /// <summary>
    /// Called when a node's parent changes (internal use)
    /// </summary>
    internal void OnNodeParentChanged(SceneNode node, SceneNode? oldParent, SceneNode? newParent)
    {
        if (oldParent == null)
        {
            // Was a root, now has parent
            _rootNodes.Remove(node);
        }
        else if (newParent == null)
        {
            // Now a root
            _rootNodes.Add(node);
        }
    }

    #endregion

    #region Queries

    /// <summary>
    /// Find a node by name (searches all nodes)
    /// </summary>
    public SceneNode? FindNode(string name)
    {
        foreach (var root in _rootNodes)
        {
            if (root.Name == name)
                return root;
            
            var found = root.FindDescendant(name);
            if (found != null)
                return found;
        }
        return null;
    }

    /// <summary>
    /// Find all nodes with a specific tag
    /// </summary>
    public IEnumerable<SceneNode> FindByTag(string tag)
    {
        foreach (var root in _rootNodes)
        {
            foreach (var node in root.FindByTag(tag))
                yield return node;
        }
    }

    /// <summary>
    /// Find all nodes with a specific component
    /// </summary>
    public IEnumerable<SceneNode> FindWithComponent<T>() where T : IComponent
    {
        foreach (var root in _rootNodes)
        {
            foreach (var node in root.FindWithComponent<T>())
                yield return node;
        }
    }

    /// <summary>
    /// Get all nodes in the scene
    /// </summary>
    public IEnumerable<SceneNode> GetAllNodes()
    {
        foreach (var root in _rootNodes)
        {
            yield return root;
            foreach (var descendant in root.GetDescendants())
                yield return descendant;
        }
    }

    /// <summary>
    /// Get all active nodes in the scene
    /// </summary>
    public IEnumerable<SceneNode> GetActiveNodes()
    {
        foreach (var root in _rootNodes)
        {
            if (root.Active)
            {
                yield return root;
                foreach (var descendant in root.GetActiveDescendants())
                    yield return descendant;
            }
        }
    }

    #endregion

    #region Lifecycle

    /// <summary>
    /// Update the scene
    /// </summary>
    public void Update(float deltaTime)
    {
        World.Update(deltaTime);
    }

    /// <summary>
    /// Fixed update for physics
    /// </summary>
    public void FixedUpdate(float fixedDeltaTime)
    {
        World.FixedUpdate(fixedDeltaTime);
    }

    /// <summary>
    /// Clear all nodes and reset the scene
    /// </summary>
    public void Clear()
    {
        foreach (var node in _rootNodes.ToList())
        {
            node.Destroy();
        }
        _rootNodes.Clear();
        _nodesByEntity.Clear();
    }

    /// <summary>
    /// Dispose the scene and its resources
    /// </summary>
    public void Dispose()
    {
        Clear();
        World.Dispose();
    }

    #endregion

    public override string ToString() => $"Scene({Name}, Nodes={_nodesByEntity.Count})";
}
