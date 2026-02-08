namespace RedHoleEngine.Core.ECS;

/// <summary>
/// Base class for all systems. Systems contain logic that operates on components.
/// </summary>
public abstract class GameSystem
{
    /// <summary>
    /// Reference to the world this system belongs to
    /// </summary>
    protected World World { get; private set; } = null!;
    
    /// <summary>
    /// Whether this system is enabled
    /// </summary>
    public bool Enabled { get; set; } = true;
    
    /// <summary>
    /// Priority for execution order (lower = earlier)
    /// </summary>
    public virtual int Priority => 0;

    /// <summary>
    /// Called when the system is added to the world
    /// </summary>
    internal void SetWorld(World world)
    {
        World = world;
        OnInitialize();
    }

    /// <summary>
    /// Called once when system is added to world
    /// </summary>
    protected virtual void OnInitialize() { }

    /// <summary>
    /// Called every frame
    /// </summary>
    public abstract void Update(float deltaTime);

    /// <summary>
    /// Called at fixed timestep (for physics)
    /// </summary>
    public virtual void FixedUpdate(float fixedDeltaTime) { }

    /// <summary>
    /// Called when system is removed
    /// </summary>
    public virtual void OnDestroy() { }
}

/// <summary>
/// System that processes entities with specific component combinations
/// </summary>
public abstract class ComponentSystem<T1> : GameSystem where T1 : IComponent
{
    public override void Update(float deltaTime)
    {
        var pool = World.GetPool<T1>();
        foreach (var entityId in pool.GetEntityIds())
        {
            ref var c1 = ref pool.Get(entityId);
            Process(new Entity(entityId, 0), ref c1, deltaTime);
        }
    }

    protected abstract void Process(Entity entity, ref T1 component, float deltaTime);
}

/// <summary>
/// System that processes entities with two component types
/// </summary>
public abstract class ComponentSystem<T1, T2> : GameSystem 
    where T1 : IComponent 
    where T2 : IComponent
{
    public override void Update(float deltaTime)
    {
        var pool1 = World.GetPool<T1>();
        var pool2 = World.GetPool<T2>();
        
        // Iterate over the smaller pool for efficiency
        foreach (var entityId in pool1.GetEntityIds())
        {
            if (pool2.TryGet(entityId, out var c2))
            {
                ref var c1 = ref pool1.Get(entityId);
                Process(new Entity(entityId, 0), ref c1, ref c2, deltaTime);
            }
        }
    }

    protected abstract void Process(Entity entity, ref T1 c1, ref T2 c2, float deltaTime);
}

/// <summary>
/// System that processes entities with three component types
/// </summary>
public abstract class ComponentSystem<T1, T2, T3> : GameSystem 
    where T1 : IComponent 
    where T2 : IComponent
    where T3 : IComponent
{
    public override void Update(float deltaTime)
    {
        var pool1 = World.GetPool<T1>();
        var pool2 = World.GetPool<T2>();
        var pool3 = World.GetPool<T3>();
        
        foreach (var entityId in pool1.GetEntityIds())
        {
            if (pool2.TryGet(entityId, out var c2) && pool3.TryGet(entityId, out var c3))
            {
                ref var c1 = ref pool1.Get(entityId);
                Process(new Entity(entityId, 0), ref c1, ref c2, ref c3, deltaTime);
            }
        }
    }

    protected abstract void Process(Entity entity, ref T1 c1, ref T2 c2, ref T3 c3, float deltaTime);
}
