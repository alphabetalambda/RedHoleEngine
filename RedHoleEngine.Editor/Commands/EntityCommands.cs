using System.Numerics;
using RedHoleEngine.Components;
using RedHoleEngine.Core.ECS;
using RedHoleEngine.Editor.Selection;

namespace RedHoleEngine.Editor.Commands;

/// <summary>
/// Command to create a new entity with optional components
/// </summary>
public class CreateEntityCommand : ICommand
{
    private readonly World _world;
    private readonly SelectionManager? _selection;
    private readonly List<IComponent> _components;
    private readonly string _entityType;
    private Entity _createdEntity;

    public string Description => $"Create {_entityType}";

    public CreateEntityCommand(World world, SelectionManager? selection, string entityType = "Entity", params IComponent[] components)
    {
        _world = world;
        _selection = selection;
        _entityType = entityType;
        _components = components.ToList();
    }

    public void Execute()
    {
        _createdEntity = _world.CreateEntity();
        
        foreach (var component in _components)
        {
            AddComponentDynamic(_createdEntity, component);
        }
        
        // Select the newly created entity
        _selection?.Select(_createdEntity);
    }

    public void Undo()
    {
        if (_world.IsAlive(_createdEntity))
        {
            // Clear selection if this entity was selected
            if (_selection != null && _selection.IsSelected(_createdEntity))
            {
                _selection.RemoveFromSelection(_createdEntity);
            }
            
            _world.DestroyEntity(_createdEntity);
        }
    }

    private void AddComponentDynamic(Entity entity, IComponent component)
    {
        // Use reflection to call the generic AddComponent method
        var method = typeof(World).GetMethod(nameof(World.AddComponent))!;
        var genericMethod = method.MakeGenericMethod(component.GetType());
        genericMethod.Invoke(_world, new object[] { entity, component });
    }
}

/// <summary>
/// Command to delete one or more entities
/// </summary>
public class DeleteEntitiesCommand : ICommand
{
    private readonly World _world;
    private readonly SelectionManager? _selection;
    private readonly List<EntitySnapshot> _deletedEntities = new();

    public string Description => _deletedEntities.Count == 1 
        ? "Delete Entity" 
        : $"Delete {_deletedEntities.Count} Entities";

    public DeleteEntitiesCommand(World world, SelectionManager? selection, IEnumerable<Entity> entities)
    {
        _world = world;
        _selection = selection;
        
        // Capture snapshots before deletion
        foreach (var entity in entities.Where(e => world.IsAlive(e)))
        {
            _deletedEntities.Add(CaptureEntitySnapshot(entity));
        }
    }

    public void Execute()
    {
        foreach (var snapshot in _deletedEntities)
        {
            if (_world.TryGetEntity(snapshot.EntityId, out var entity))
            {
                _selection?.RemoveFromSelection(entity);
                _world.DestroyEntity(entity);
            }
        }
    }

    public void Undo()
    {
        foreach (var snapshot in _deletedEntities)
        {
            RestoreEntityFromSnapshot(snapshot);
        }
    }

    private EntitySnapshot CaptureEntitySnapshot(Entity entity)
    {
        var snapshot = new EntitySnapshot
        {
            EntityId = entity.Id,
            Components = new List<IComponent>()
        };

        // Capture all known component types
        CaptureComponent<TransformComponent>(entity, snapshot);
        CaptureComponent<CameraComponent>(entity, snapshot);
        CaptureComponent<GravitySourceComponent>(entity, snapshot);
        CaptureComponent<RigidBodyComponent>(entity, snapshot);
        CaptureComponent<ColliderComponent>(entity, snapshot);
        CaptureComponent<MeshComponent>(entity, snapshot);
        CaptureComponent<MaterialComponent>(entity, snapshot);
        CaptureComponent<RenderSettingsComponent>(entity, snapshot);
        CaptureComponent<RaytracerMeshComponent>(entity, snapshot);
        CaptureComponent<AccretionDiskComponent>(entity, snapshot);

        return snapshot;
    }

    private void CaptureComponent<T>(Entity entity, EntitySnapshot snapshot) where T : IComponent
    {
        if (_world.TryGetComponent<T>(entity, out var component))
        {
            snapshot.Components.Add(component);
        }
    }

    private void RestoreEntityFromSnapshot(EntitySnapshot snapshot)
    {
        var entity = _world.CreateEntity();
        
        foreach (var component in snapshot.Components)
        {
            AddComponentDynamic(entity, component);
        }
        
        // Update snapshot with new entity ID for potential re-deletion
        snapshot.EntityId = entity.Id;
    }

    private void AddComponentDynamic(Entity entity, IComponent component)
    {
        var method = typeof(World).GetMethod(nameof(World.AddComponent))!;
        var genericMethod = method.MakeGenericMethod(component.GetType());
        genericMethod.Invoke(_world, new object[] { entity, component });
    }

    private class EntitySnapshot
    {
        public int EntityId { get; set; }
        public List<IComponent> Components { get; set; } = new();
    }
}

/// <summary>
/// Command to modify a component value
/// </summary>
public class ModifyComponentCommand<T> : ICommand where T : struct, IComponent
{
    private readonly World _world;
    private readonly int _entityId;
    private readonly T _oldValue;
    private readonly T _newValue;
    private readonly string _propertyName;
    private readonly DateTime _timestamp;

    public string Description => $"Modify {typeof(T).Name}.{_propertyName}";

    public ModifyComponentCommand(World world, Entity entity, T oldValue, T newValue, string propertyName = "")
    {
        _world = world;
        _entityId = entity.Id;
        _oldValue = oldValue;
        _newValue = newValue;
        _propertyName = string.IsNullOrEmpty(propertyName) ? "Value" : propertyName;
        _timestamp = DateTime.UtcNow;
    }

    public void Execute()
    {
        if (_world.TryGetEntity(_entityId, out var entity) && _world.HasComponent<T>(entity))
        {
            ref var component = ref _world.GetComponent<T>(entity);
            component = _newValue;
        }
    }

    public void Undo()
    {
        if (_world.TryGetEntity(_entityId, out var entity) && _world.HasComponent<T>(entity))
        {
            ref var component = ref _world.GetComponent<T>(entity);
            component = _oldValue;
        }
    }

    public bool CanMergeWith(ICommand other)
    {
        // Merge continuous edits to the same component property within 500ms
        if (other is ModifyComponentCommand<T> otherCmd)
        {
            return otherCmd._entityId == _entityId &&
                   otherCmd._propertyName == _propertyName &&
                   (otherCmd._timestamp - _timestamp).TotalMilliseconds < 500;
        }
        return false;
    }

    public void MergeWith(ICommand other)
    {
        // Keep our old value, take the new value from the merged command
        // This is handled by just not pushing the new command
    }
}

/// <summary>
/// Command to add a component to an entity
/// </summary>
public class AddComponentCommand<T> : ICommand where T : struct, IComponent
{
    private readonly World _world;
    private readonly int _entityId;
    private readonly T _component;

    public string Description => $"Add {typeof(T).Name}";

    public AddComponentCommand(World world, Entity entity, T component)
    {
        _world = world;
        _entityId = entity.Id;
        _component = component;
    }

    public void Execute()
    {
        if (_world.TryGetEntity(_entityId, out var entity) && !_world.HasComponent<T>(entity))
        {
            _world.AddComponent(entity, _component);
        }
    }

    public void Undo()
    {
        if (_world.TryGetEntity(_entityId, out var entity) && _world.HasComponent<T>(entity))
        {
            _world.RemoveComponent<T>(entity);
        }
    }
}

/// <summary>
/// Command to remove a component from an entity
/// </summary>
public class RemoveComponentCommand<T> : ICommand where T : struct, IComponent
{
    private readonly World _world;
    private readonly int _entityId;
    private T _removedComponent;

    public string Description => $"Remove {typeof(T).Name}";

    public RemoveComponentCommand(World world, Entity entity)
    {
        _world = world;
        _entityId = entity.Id;
        
        // Capture current value for undo
        if (world.TryGetEntity(_entityId, out var e) && world.TryGetComponent<T>(e, out var component))
        {
            _removedComponent = component;
        }
    }

    public void Execute()
    {
        if (_world.TryGetEntity(_entityId, out var entity) && _world.HasComponent<T>(entity))
        {
            _removedComponent = _world.GetComponent<T>(entity);
            _world.RemoveComponent<T>(entity);
        }
    }

    public void Undo()
    {
        if (_world.TryGetEntity(_entityId, out var entity) && !_world.HasComponent<T>(entity))
        {
            _world.AddComponent(entity, _removedComponent);
        }
    }
}

/// <summary>
/// Command to duplicate entities
/// </summary>
public class DuplicateEntitiesCommand : ICommand
{
    private readonly World _world;
    private readonly SelectionManager? _selection;
    private readonly List<EntitySnapshot> _sourceSnapshots = new();
    private readonly List<Entity> _createdEntities = new();

    public string Description => _sourceSnapshots.Count == 1 
        ? "Duplicate Entity" 
        : $"Duplicate {_sourceSnapshots.Count} Entities";

    public DuplicateEntitiesCommand(World world, SelectionManager? selection, IEnumerable<Entity> entities)
    {
        _world = world;
        _selection = selection;
        
        foreach (var entity in entities.Where(e => world.IsAlive(e)))
        {
            _sourceSnapshots.Add(CaptureEntitySnapshot(entity));
        }
    }

    public void Execute()
    {
        _createdEntities.Clear();
        _selection?.ClearSelection();
        
        foreach (var snapshot in _sourceSnapshots)
        {
            var newEntity = _world.CreateEntity();
            
            foreach (var component in snapshot.Components)
            {
                var clonedComponent = CloneComponent(component);
                AddComponentDynamic(newEntity, clonedComponent);
            }
            
            // Offset position slightly so duplicates are visible
            if (_world.HasComponent<TransformComponent>(newEntity))
            {
                ref var transform = ref _world.GetComponent<TransformComponent>(newEntity);
                transform.LocalPosition += new Vector3(1f, 0f, 1f);
            }
            
            _createdEntities.Add(newEntity);
            _selection?.AddToSelection(newEntity);
        }
    }

    public void Undo()
    {
        foreach (var entity in _createdEntities)
        {
            if (_world.IsAlive(entity))
            {
                _selection?.RemoveFromSelection(entity);
                _world.DestroyEntity(entity);
            }
        }
        _createdEntities.Clear();
    }

    private EntitySnapshot CaptureEntitySnapshot(Entity entity)
    {
        var snapshot = new EntitySnapshot
        {
            EntityId = entity.Id,
            Components = new List<IComponent>()
        };

        CaptureComponent<TransformComponent>(entity, snapshot);
        CaptureComponent<CameraComponent>(entity, snapshot);
        CaptureComponent<GravitySourceComponent>(entity, snapshot);
        CaptureComponent<RigidBodyComponent>(entity, snapshot);
        CaptureComponent<ColliderComponent>(entity, snapshot);
        CaptureComponent<MeshComponent>(entity, snapshot);
        CaptureComponent<MaterialComponent>(entity, snapshot);
        CaptureComponent<RenderSettingsComponent>(entity, snapshot);
        CaptureComponent<RaytracerMeshComponent>(entity, snapshot);
        CaptureComponent<AccretionDiskComponent>(entity, snapshot);

        return snapshot;
    }

    private void CaptureComponent<T>(Entity entity, EntitySnapshot snapshot) where T : IComponent
    {
        if (_world.TryGetComponent<T>(entity, out var component))
        {
            snapshot.Components.Add(component);
        }
    }

    private IComponent CloneComponent(IComponent component)
    {
        // Struct components are value types, so assignment creates a copy
        return component;
    }

    private void AddComponentDynamic(Entity entity, IComponent component)
    {
        var method = typeof(World).GetMethod(nameof(World.AddComponent))!;
        var genericMethod = method.MakeGenericMethod(component.GetType());
        genericMethod.Invoke(_world, new object[] { entity, component });
    }

    private class EntitySnapshot
    {
        public int EntityId { get; set; }
        public List<IComponent> Components { get; set; } = new();
    }
}
