using System.Runtime.CompilerServices;

namespace RedHoleEngine.Core.ECS;

/// <summary>
/// The World is the central container for all entities, components, and systems.
/// It manages the ECS lifecycle and provides efficient access to game data.
/// </summary>
public class World : IDisposable
{
    private readonly Dictionary<Type, IComponentPool> _pools = new();
    private readonly List<GameSystem> _systems = new();
    private readonly List<GameSystem> _sortedSystems = new();
    private bool _systemsDirty = true;
    
    // Entity management
    private readonly Queue<int> _freeEntityIds = new();
    private readonly int[] _entityGenerations;
    private int _nextEntityId = 1; // 0 is reserved for null
    private int _entityCount = 0;
    
    private const int MaxEntities = 100_000;

    public int EntityCount => _entityCount;
    public IReadOnlyList<GameSystem> Systems => _systems;

    public World()
    {
        _entityGenerations = new int[MaxEntities];
    }

    #region Entity Management

    /// <summary>
    /// Create a new entity
    /// </summary>
    public Entity CreateEntity()
    {
        int id;
        int generation;

        if (_freeEntityIds.Count > 0)
        {
            id = _freeEntityIds.Dequeue();
            generation = _entityGenerations[id];
        }
        else
        {
            if (_nextEntityId >= MaxEntities)
                throw new InvalidOperationException($"Maximum entity count ({MaxEntities}) exceeded");
            
            id = _nextEntityId++;
            generation = 1;
            _entityGenerations[id] = generation;
        }

        _entityCount++;
        return new Entity(id, generation);
    }

    /// <summary>
    /// Destroy an entity and all its components
    /// </summary>
    public void DestroyEntity(Entity entity)
    {
        if (!IsAlive(entity))
            return;

        // Remove all components
        foreach (var pool in _pools.Values)
        {
            pool.Remove(entity.Id);
        }

        // Increment generation to invalidate references
        _entityGenerations[entity.Id]++;
        _freeEntityIds.Enqueue(entity.Id);
        _entityCount--;
    }

    /// <summary>
    /// Check if an entity is still valid
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool IsAlive(Entity entity)
    {
        return entity.Id > 0 && 
               entity.Id < _entityGenerations.Length && 
               _entityGenerations[entity.Id] == entity.Generation;
    }

    /// <summary>
    /// Try to get a valid entity from an ID.
    /// Returns true if the ID refers to a currently alive entity.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryGetEntity(int entityId, out Entity entity)
    {
        if (entityId > 0 && entityId < _entityGenerations.Length)
        {
            int generation = _entityGenerations[entityId];
            if (generation > 0)
            {
                entity = new Entity(entityId, generation);
                return true;
            }
        }
        entity = Entity.Null;
        return false;
    }

    /// <summary>
    /// Get the current generation for an entity ID.
    /// Used for reconstructing Entity references from IDs.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int GetEntityGeneration(int entityId)
    {
        if (entityId > 0 && entityId < _entityGenerations.Length)
            return _entityGenerations[entityId];
        return 0;
    }

    #endregion

    #region Component Management

    /// <summary>
    /// Get or create the component pool for a type
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ComponentPool<T> GetPool<T>() where T : IComponent
    {
        var type = typeof(T);
        if (!_pools.TryGetValue(type, out var pool))
        {
            pool = new ComponentPool<T>();
            _pools[type] = pool;
        }
        return (ComponentPool<T>)pool;
    }

    /// <summary>
    /// Add a component to an entity
    /// </summary>
    public ref T AddComponent<T>(Entity entity, T component) where T : IComponent
    {
        if (!IsAlive(entity))
            throw new InvalidOperationException($"Entity {entity} is not alive");
        
        return ref GetPool<T>().Set(entity.Id, component);
    }

    /// <summary>
    /// Get a component from an entity
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ref T GetComponent<T>(Entity entity) where T : IComponent
    {
        return ref GetPool<T>().Get(entity.Id);
    }

    /// <summary>
    /// Try to get a component from an entity
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryGetComponent<T>(Entity entity, out T component) where T : IComponent
    {
        return GetPool<T>().TryGet(entity.Id, out component);
    }

    /// <summary>
    /// Check if entity has a component
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool HasComponent<T>(Entity entity) where T : IComponent
    {
        return GetPool<T>().Has(entity.Id);
    }

    /// <summary>
    /// Remove a component from an entity
    /// </summary>
    public void RemoveComponent<T>(Entity entity) where T : IComponent
    {
        GetPool<T>().Remove(entity.Id);
    }

    #endregion

    #region System Management

    /// <summary>
    /// Register a system with the world
    /// </summary>
    public T AddSystem<T>() where T : GameSystem, new()
    {
        var system = new T();
        _systems.Add(system);
        _systemsDirty = true;
        system.SetWorld(this);
        return system;
    }

    /// <summary>
    /// Register an existing system instance
    /// </summary>
    public T AddSystem<T>(T system) where T : GameSystem
    {
        _systems.Add(system);
        _systemsDirty = true;
        system.SetWorld(this);
        return system;
    }

    /// <summary>
    /// Get a system by type
    /// </summary>
    public T? GetSystem<T>() where T : GameSystem
    {
        return _systems.OfType<T>().FirstOrDefault();
    }

    /// <summary>
    /// Remove a system
    /// </summary>
    public void RemoveSystem<T>() where T : GameSystem
    {
        var system = GetSystem<T>();
        if (system != null)
        {
            system.OnDestroy();
            _systems.Remove(system);
            _systemsDirty = true;
        }
    }

    private void EnsureSystemsSorted()
    {
        if (!_systemsDirty) return;
        
        _sortedSystems.Clear();
        _sortedSystems.AddRange(_systems);
        _sortedSystems.Sort((a, b) => a.Priority.CompareTo(b.Priority));
        _systemsDirty = false;
    }

    #endregion

    #region Update Loop

    /// <summary>
    /// Update all systems
    /// </summary>
    public void Update(float deltaTime)
    {
        EnsureSystemsSorted();
        
        foreach (var system in _sortedSystems)
        {
            if (system.Enabled)
                system.Update(deltaTime);
        }
    }

    /// <summary>
    /// Fixed update all systems (physics timestep)
    /// </summary>
    public void FixedUpdate(float fixedDeltaTime)
    {
        EnsureSystemsSorted();
        
        foreach (var system in _sortedSystems)
        {
            if (system.Enabled)
                system.FixedUpdate(fixedDeltaTime);
        }
    }

    #endregion

    #region Queries

    /// <summary>
    /// Query entities with a specific component
    /// </summary>
    public IEnumerable<Entity> Query<T>() where T : IComponent
    {
        var pool = GetPool<T>();
        foreach (var entityId in pool.GetEntityIds())
        {
            yield return new Entity(entityId, _entityGenerations[entityId]);
        }
    }

    /// <summary>
    /// Query entities with two components
    /// </summary>
    public IEnumerable<Entity> Query<T1, T2>() 
        where T1 : IComponent 
        where T2 : IComponent
    {
        var pool1 = GetPool<T1>();
        var pool2 = GetPool<T2>();
        
        foreach (var entityId in pool1.GetEntityIds())
        {
            if (pool2.Has(entityId))
                yield return new Entity(entityId, _entityGenerations[entityId]);
        }
    }

    /// <summary>
    /// Query entities with three components
    /// </summary>
    public IEnumerable<Entity> Query<T1, T2, T3>() 
        where T1 : IComponent 
        where T2 : IComponent
        where T3 : IComponent
    {
        var pool1 = GetPool<T1>();
        var pool2 = GetPool<T2>();
        var pool3 = GetPool<T3>();
        
        foreach (var entityId in pool1.GetEntityIds())
        {
            if (pool2.Has(entityId) && pool3.Has(entityId))
                yield return new Entity(entityId, _entityGenerations[entityId]);
        }
    }

    #endregion

    public void Dispose()
    {
        foreach (var system in _systems)
            system.OnDestroy();
        
        foreach (var pool in _pools.Values)
            pool.Clear();
        
        _systems.Clear();
        _pools.Clear();
    }
}
