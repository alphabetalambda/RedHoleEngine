using RedHoleEngine.Core.ECS;

namespace RedHoleEngine.Core.Scene;

/// <summary>
/// A node in the scene graph, representing a game object with a transform.
/// Bridges the hierarchical scene structure with the flat ECS architecture.
/// </summary>
public class SceneNode
{
    /// <summary>
    /// The entity this node is associated with
    /// </summary>
    public Entity Entity { get; }
    
    /// <summary>
    /// The transform for this node
    /// </summary>
    public Transform Transform { get; }
    
    /// <summary>
    /// User-friendly name for this node
    /// </summary>
    public string Name { get; set; }
    
    /// <summary>
    /// Whether this node is active (inactive nodes and children are not processed)
    /// </summary>
    public bool Active { get; set; } = true;
    
    /// <summary>
    /// Optional tag for categorization
    /// </summary>
    public string? Tag { get; set; }
    
    /// <summary>
    /// Reference to the world for component access
    /// </summary>
    private readonly World _world;

    /// <summary>
    /// Parent node (null if root)
    /// </summary>
    public SceneNode? Parent { get; private set; }
    
    /// <summary>
    /// Child nodes
    /// </summary>
    private readonly List<SceneNode> _children = new();
    public IReadOnlyList<SceneNode> Children => _children;

    public SceneNode(World world, string name = "Node")
    {
        _world = world;
        Entity = world.CreateEntity();
        Transform = new Transform();
        Name = name;
    }

    public SceneNode(World world, Entity entity, string name = "Node")
    {
        _world = world;
        Entity = entity;
        Transform = new Transform();
        Name = name;
    }

    #region Hierarchy

    /// <summary>
    /// Set the parent of this node
    /// </summary>
    public void SetParent(SceneNode? newParent, bool worldPositionStays = true)
    {
        if (Parent == newParent)
            return;

        // Prevent circular hierarchy
        if (newParent != null && IsAncestorOf(newParent))
            throw new InvalidOperationException("Cannot set a descendant as parent (circular hierarchy)");

        // Remove from old parent
        Parent?._children.Remove(this);

        // Set new parent
        Parent = newParent;
        Parent?._children.Add(this);

        // Update transform hierarchy
        Transform.SetParent(newParent?.Transform, worldPositionStays);
    }

    /// <summary>
    /// Add a child node
    /// </summary>
    public void AddChild(SceneNode child)
    {
        child.SetParent(this);
    }

    /// <summary>
    /// Remove a child node
    /// </summary>
    public void RemoveChild(SceneNode child)
    {
        if (child.Parent == this)
        {
            child.SetParent(null);
        }
    }

    /// <summary>
    /// Check if this node is an ancestor of another
    /// </summary>
    public bool IsAncestorOf(SceneNode node)
    {
        var current = node.Parent;
        while (current != null)
        {
            if (current == this)
                return true;
            current = current.Parent;
        }
        return false;
    }

    /// <summary>
    /// Get the root of the hierarchy
    /// </summary>
    public SceneNode GetRoot()
    {
        var current = this;
        while (current.Parent != null)
            current = current.Parent;
        return current;
    }

    #endregion

    #region Component Access

    /// <summary>
    /// Add a component to this node's entity
    /// </summary>
    public ref T AddComponent<T>(T component) where T : IComponent
    {
        return ref _world.AddComponent(Entity, component);
    }

    /// <summary>
    /// Get a component from this node's entity
    /// </summary>
    public ref T GetComponent<T>() where T : IComponent
    {
        return ref _world.GetComponent<T>(Entity);
    }

    /// <summary>
    /// Try to get a component
    /// </summary>
    public bool TryGetComponent<T>(out T component) where T : IComponent
    {
        return _world.TryGetComponent(Entity, out component);
    }

    /// <summary>
    /// Check if this node has a component
    /// </summary>
    public bool HasComponent<T>() where T : IComponent
    {
        return _world.HasComponent<T>(Entity);
    }

    /// <summary>
    /// Remove a component from this node
    /// </summary>
    public void RemoveComponent<T>() where T : IComponent
    {
        _world.RemoveComponent<T>(Entity);
    }

    #endregion

    #region Queries

    /// <summary>
    /// Find a child by name (direct children only)
    /// </summary>
    public SceneNode? FindChild(string name)
    {
        return _children.FirstOrDefault(c => c.Name == name);
    }

    /// <summary>
    /// Find a descendant by name (recursive)
    /// </summary>
    public SceneNode? FindDescendant(string name)
    {
        foreach (var child in _children)
        {
            if (child.Name == name)
                return child;
            
            var found = child.FindDescendant(name);
            if (found != null)
                return found;
        }
        return null;
    }

    /// <summary>
    /// Find all descendants with a specific tag
    /// </summary>
    public IEnumerable<SceneNode> FindByTag(string tag)
    {
        if (Tag == tag)
            yield return this;
        
        foreach (var child in _children)
        {
            foreach (var found in child.FindByTag(tag))
                yield return found;
        }
    }

    /// <summary>
    /// Find all descendants with a specific component
    /// </summary>
    public IEnumerable<SceneNode> FindWithComponent<T>() where T : IComponent
    {
        if (HasComponent<T>())
            yield return this;
        
        foreach (var child in _children)
        {
            foreach (var found in child.FindWithComponent<T>())
                yield return found;
        }
    }

    /// <summary>
    /// Traverse all descendants (depth-first)
    /// </summary>
    public IEnumerable<SceneNode> GetDescendants()
    {
        foreach (var child in _children)
        {
            yield return child;
            foreach (var descendant in child.GetDescendants())
                yield return descendant;
        }
    }

    /// <summary>
    /// Traverse all active descendants
    /// </summary>
    public IEnumerable<SceneNode> GetActiveDescendants()
    {
        if (!Active) yield break;
        
        foreach (var child in _children)
        {
            if (child.Active)
            {
                yield return child;
                foreach (var descendant in child.GetActiveDescendants())
                    yield return descendant;
            }
        }
    }

    #endregion

    /// <summary>
    /// Destroy this node and all children
    /// </summary>
    public void Destroy()
    {
        // Destroy children first
        foreach (var child in _children.ToList())
        {
            child.Destroy();
        }
        
        // Remove from parent
        Parent?._children.Remove(this);
        
        // Destroy entity
        _world.DestroyEntity(Entity);
    }

    public override string ToString() => $"SceneNode({Name}, Entity={Entity})";
}
