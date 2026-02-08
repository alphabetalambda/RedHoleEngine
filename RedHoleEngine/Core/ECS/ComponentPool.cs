using System.Collections;
using System.Runtime.CompilerServices;

namespace RedHoleEngine.Core.ECS;

/// <summary>
/// Interface for type-erased component pool access
/// </summary>
public interface IComponentPool
{
    Type ComponentType { get; }
    bool Has(int entityId);
    void Remove(int entityId);
    void Clear();
    int Count { get; }
    IEnumerable<int> GetEntityIds();
}

/// <summary>
/// Stores components of a specific type in a sparse set for cache-efficient iteration.
/// Uses a sparse-dense structure for O(1) add/remove/lookup while maintaining
/// dense storage for fast iteration.
/// </summary>
public class ComponentPool<T> : IComponentPool where T : IComponent
{
    // Sparse array: entityId -> index in dense arrays (or -1 if not present)
    private int[] _sparse;
    
    // Dense arrays: packed component data and corresponding entity IDs
    private T[] _dense;
    private int[] _denseEntityIds;
    
    private int _count;
    private const int InitialCapacity = 64;
    private const int SparsePageSize = 1024;

    public Type ComponentType => typeof(T);
    public int Count => _count;

    public ComponentPool()
    {
        _sparse = new int[SparsePageSize];
        Array.Fill(_sparse, -1);
        _dense = new T[InitialCapacity];
        _denseEntityIds = new int[InitialCapacity];
        _count = 0;
    }

    /// <summary>
    /// Add or update a component for an entity
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ref T Set(int entityId, T component)
    {
        EnsureSparseCapacity(entityId);
        
        int denseIndex = _sparse[entityId];
        if (denseIndex >= 0)
        {
            // Update existing
            _dense[denseIndex] = component;
            return ref _dense[denseIndex];
        }
        
        // Add new
        EnsureDenseCapacity(_count + 1);
        denseIndex = _count;
        _sparse[entityId] = denseIndex;
        _dense[denseIndex] = component;
        _denseEntityIds[denseIndex] = entityId;
        _count++;
        
        return ref _dense[denseIndex];
    }

    /// <summary>
    /// Get a reference to a component (throws if not present)
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ref T Get(int entityId)
    {
        if (entityId >= _sparse.Length || _sparse[entityId] < 0)
            throw new KeyNotFoundException($"Entity {entityId} does not have component {typeof(T).Name}");
        
        return ref _dense[_sparse[entityId]];
    }

    /// <summary>
    /// Try to get a component reference
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryGet(int entityId, out T component)
    {
        if (entityId < _sparse.Length && _sparse[entityId] >= 0)
        {
            component = _dense[_sparse[entityId]];
            return true;
        }
        component = default!;
        return false;
    }

    /// <summary>
    /// Check if entity has this component
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Has(int entityId)
    {
        return entityId < _sparse.Length && _sparse[entityId] >= 0;
    }

    /// <summary>
    /// Remove component from entity (swap-and-pop for O(1))
    /// </summary>
    public void Remove(int entityId)
    {
        if (entityId >= _sparse.Length || _sparse[entityId] < 0)
            return;

        int denseIndex = _sparse[entityId];
        int lastIndex = _count - 1;
        
        // Dispose if needed
        if (_dense[denseIndex] is IDisposable disposable)
            disposable.Dispose();

        if (denseIndex < lastIndex)
        {
            // Swap with last element
            _dense[denseIndex] = _dense[lastIndex];
            _denseEntityIds[denseIndex] = _denseEntityIds[lastIndex];
            _sparse[_denseEntityIds[denseIndex]] = denseIndex;
        }

        _sparse[entityId] = -1;
        _dense[lastIndex] = default!;
        _count--;
    }

    /// <summary>
    /// Clear all components
    /// </summary>
    public void Clear()
    {
        for (int i = 0; i < _count; i++)
        {
            if (_dense[i] is IDisposable disposable)
                disposable.Dispose();
            
            _sparse[_denseEntityIds[i]] = -1;
            _dense[i] = default!;
        }
        _count = 0;
    }

    /// <summary>
    /// Get all entity IDs that have this component
    /// </summary>
    public IEnumerable<int> GetEntityIds()
    {
        for (int i = 0; i < _count; i++)
            yield return _denseEntityIds[i];
    }

    /// <summary>
    /// Get a span over all components for efficient iteration
    /// </summary>
    public ReadOnlySpan<T> AsSpan() => _dense.AsSpan(0, _count);
    
    /// <summary>
    /// Get entity ID at dense index
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int GetEntityIdAt(int denseIndex) => _denseEntityIds[denseIndex];

    private void EnsureSparseCapacity(int entityId)
    {
        if (entityId >= _sparse.Length)
        {
            int newSize = Math.Max(_sparse.Length * 2, entityId + SparsePageSize);
            int oldSize = _sparse.Length;
            Array.Resize(ref _sparse, newSize);
            Array.Fill(_sparse, -1, oldSize, newSize - oldSize);
        }
    }

    private void EnsureDenseCapacity(int required)
    {
        if (required > _dense.Length)
        {
            int newSize = Math.Max(_dense.Length * 2, required);
            Array.Resize(ref _dense, newSize);
            Array.Resize(ref _denseEntityIds, newSize);
        }
    }
}
