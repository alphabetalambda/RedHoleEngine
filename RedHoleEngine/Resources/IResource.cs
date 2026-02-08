namespace RedHoleEngine.Resources;

/// <summary>
/// Base interface for all loadable resources
/// </summary>
public interface IResource : IDisposable
{
    /// <summary>
    /// Unique identifier for this resource
    /// </summary>
    string Id { get; }
    
    /// <summary>
    /// Whether the resource is currently loaded
    /// </summary>
    bool IsLoaded { get; }
    
    /// <summary>
    /// Load the resource from its source
    /// </summary>
    void Load();
    
    /// <summary>
    /// Unload the resource and free memory
    /// </summary>
    void Unload();
}

/// <summary>
/// Resource with typed data
/// </summary>
public interface IResource<T> : IResource
{
    /// <summary>
    /// The loaded resource data
    /// </summary>
    T? Data { get; }
}
