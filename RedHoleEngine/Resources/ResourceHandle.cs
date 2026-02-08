namespace RedHoleEngine.Resources;

/// <summary>
/// A lightweight handle to a resource, allowing deferred loading and reference counting.
/// </summary>
public readonly struct ResourceHandle<T> : IEquatable<ResourceHandle<T>> where T : class
{
    private readonly ResourceManager _manager;
    private readonly string _id;

    internal ResourceHandle(ResourceManager manager, string id)
    {
        _manager = manager;
        _id = id;
    }

    /// <summary>
    /// Resource ID
    /// </summary>
    public string Id => _id;

    /// <summary>
    /// Whether this handle is valid
    /// </summary>
    public bool IsValid => _manager != null && !string.IsNullOrEmpty(_id);

    /// <summary>
    /// Whether the resource is loaded
    /// </summary>
    public bool IsLoaded => IsValid && _manager.IsLoaded<T>(_id);

    /// <summary>
    /// Get the resource (loads if necessary)
    /// </summary>
    public T? Get()
    {
        if (!IsValid) return default;
        return _manager.Get<T>(_id);
    }

    /// <summary>
    /// Try to get the resource without loading
    /// </summary>
    public bool TryGet(out T? resource)
    {
        if (!IsValid || !IsLoaded)
        {
            resource = default;
            return false;
        }
        resource = _manager.Get<T>(_id);
        return resource != null;
    }

    public bool Equals(ResourceHandle<T> other) => _id == other._id;
    public override bool Equals(object? obj) => obj is ResourceHandle<T> other && Equals(other);
    public override int GetHashCode() => _id?.GetHashCode() ?? 0;
    
    public static bool operator ==(ResourceHandle<T> left, ResourceHandle<T> right) => left.Equals(right);
    public static bool operator !=(ResourceHandle<T> left, ResourceHandle<T> right) => !left.Equals(right);
    
    public override string ToString() => $"ResourceHandle<{typeof(T).Name}>({_id})";
}
