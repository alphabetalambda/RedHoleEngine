using System.Collections.Concurrent;

namespace RedHoleEngine.Resources;

/// <summary>
/// Manages loading, caching, and lifetime of game resources.
/// </summary>
public class ResourceManager : IDisposable
{
    private readonly ConcurrentDictionary<Type, ConcurrentDictionary<string, object>> _resourcesByType = new();
    private readonly ConcurrentDictionary<Type, IResourceLoader> _loaders = new();
    private readonly ConcurrentDictionary<string, int> _refCounts = new();
    
    /// <summary>
    /// Base path for loading resources
    /// </summary>
    public string BasePath { get; set; } = "";

    #region Loader Registration

    /// <summary>
    /// Register a resource loader for a specific type
    /// </summary>
    public void RegisterLoader<T>(IResourceLoader<T> loader) where T : class
    {
        _loaders[typeof(T)] = loader;
    }

    /// <summary>
    /// Get the loader for a type
    /// </summary>
    public IResourceLoader<T>? GetLoader<T>() where T : class
    {
        if (_loaders.TryGetValue(typeof(T), out var loader))
            return loader as IResourceLoader<T>;
        return null;
    }

    #endregion

    #region Resource Loading

    /// <summary>
    /// Load a resource synchronously
    /// </summary>
    public T? Load<T>(string path) where T : class
    {
        var cache = GetCache<T>();
        
        if (cache.TryGetValue(path, out var existing))
        {
            IncrementRef(path);
            return existing as T;
        }

        var loader = GetLoader<T>();
        if (loader == null)
            throw new InvalidOperationException($"No loader registered for type {typeof(T).Name}");

        var fullPath = string.IsNullOrEmpty(BasePath) ? path : Path.Combine(BasePath, path);
        var resource = loader.Load(fullPath);
        
        if (resource != null)
        {
            cache[path] = resource;
            IncrementRef(path);
        }

        return resource;
    }

    /// <summary>
    /// Load a resource asynchronously
    /// </summary>
    public async Task<T?> LoadAsync<T>(string path, CancellationToken cancellationToken = default) where T : class
    {
        var cache = GetCache<T>();
        
        if (cache.TryGetValue(path, out var existing))
        {
            IncrementRef(path);
            return existing as T;
        }

        var loader = GetLoader<T>();
        if (loader == null)
            throw new InvalidOperationException($"No loader registered for type {typeof(T).Name}");

        var fullPath = string.IsNullOrEmpty(BasePath) ? path : Path.Combine(BasePath, path);
        var resource = await loader.LoadAsync(fullPath, cancellationToken);
        
        if (resource != null)
        {
            cache[path] = resource;
            IncrementRef(path);
        }

        return resource;
    }

    /// <summary>
    /// Get a handle to a resource (doesn't load immediately)
    /// </summary>
    public ResourceHandle<T> GetHandle<T>(string path) where T : class
    {
        return new ResourceHandle<T>(this, path);
    }

    /// <summary>
    /// Get a loaded resource (returns null if not loaded)
    /// </summary>
    public T? Get<T>(string path) where T : class
    {
        var cache = GetCache<T>();
        
        if (cache.TryGetValue(path, out var resource))
            return resource as T;
        
        // Auto-load if we have a loader
        return Load<T>(path);
    }

    /// <summary>
    /// Check if a resource is loaded
    /// </summary>
    public bool IsLoaded<T>(string path) where T : class
    {
        return GetCache<T>().ContainsKey(path);
    }

    #endregion

    #region Resource Management

    /// <summary>
    /// Add an already-created resource to the cache
    /// </summary>
    public void Add<T>(string id, T resource) where T : class
    {
        var cache = GetCache<T>();
        cache[id] = resource;
        IncrementRef(id);
    }

    /// <summary>
    /// Release a reference to a resource
    /// </summary>
    public void Release<T>(string path) where T : class
    {
        if (DecrementRef(path) <= 0)
        {
            Unload<T>(path);
        }
    }

    /// <summary>
    /// Force unload a resource regardless of ref count
    /// </summary>
    public void Unload<T>(string path) where T : class
    {
        var cache = GetCache<T>();
        
        if (cache.TryRemove(path, out var resource))
        {
            (resource as IDisposable)?.Dispose();
            _refCounts.TryRemove(path, out _);
        }
    }

    /// <summary>
    /// Unload all resources of a type
    /// </summary>
    public void UnloadAll<T>() where T : class
    {
        var cache = GetCache<T>();
        
        foreach (var kvp in cache)
        {
            (kvp.Value as IDisposable)?.Dispose();
            _refCounts.TryRemove(kvp.Key, out _);
        }
        
        cache.Clear();
    }

    /// <summary>
    /// Unload all resources
    /// </summary>
    public void UnloadAll()
    {
        foreach (var typeCache in _resourcesByType.Values)
        {
            foreach (var resource in typeCache.Values)
            {
                (resource as IDisposable)?.Dispose();
            }
            typeCache.Clear();
        }
        _refCounts.Clear();
    }

    #endregion

    #region Utilities

    /// <summary>
    /// Get all loaded resource IDs of a type
    /// </summary>
    public IEnumerable<string> GetLoadedIds<T>() where T : class
    {
        return GetCache<T>().Keys;
    }

    /// <summary>
    /// Get the reference count for a resource
    /// </summary>
    public int GetRefCount(string path)
    {
        return _refCounts.GetValueOrDefault(path, 0);
    }

    private ConcurrentDictionary<string, object> GetCache<T>() where T : class
    {
        return _resourcesByType.GetOrAdd(typeof(T), _ => new ConcurrentDictionary<string, object>());
    }

    private void IncrementRef(string path)
    {
        _refCounts.AddOrUpdate(path, 1, (_, count) => count + 1);
    }

    private int DecrementRef(string path)
    {
        if (_refCounts.TryGetValue(path, out var count))
        {
            var newCount = count - 1;
            if (newCount <= 0)
                _refCounts.TryRemove(path, out _);
            else
                _refCounts[path] = newCount;
            return newCount;
        }
        return 0;
    }

    #endregion

    public void Dispose()
    {
        UnloadAll();
        _loaders.Clear();
    }
}

/// <summary>
/// Interface for resource loaders
/// </summary>
public interface IResourceLoader
{
}

/// <summary>
/// Typed resource loader interface
/// </summary>
public interface IResourceLoader<T> : IResourceLoader where T : class
{
    /// <summary>
    /// Load a resource synchronously
    /// </summary>
    T? Load(string path);
    
    /// <summary>
    /// Load a resource asynchronously
    /// </summary>
    Task<T?> LoadAsync(string path, CancellationToken cancellationToken = default);
}

/// <summary>
/// Base class for resource loaders with default async implementation
/// </summary>
public abstract class ResourceLoader<T> : IResourceLoader<T> where T : class
{
    public abstract T? Load(string path);

    public virtual Task<T?> LoadAsync(string path, CancellationToken cancellationToken = default)
    {
        return Task.Run(() => Load(path), cancellationToken);
    }
}
