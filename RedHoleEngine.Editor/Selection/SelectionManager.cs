using RedHoleEngine.Core.ECS;

namespace RedHoleEngine.Editor.Selection;

/// <summary>
/// Manages entity selection in the editor
/// </summary>
public class SelectionManager
{
    private readonly HashSet<Entity> _selectedEntities = new();
    private Entity _primarySelection = Entity.Null;
    
    /// <summary>
    /// Event fired when selection changes
    /// </summary>
    public event Action<IReadOnlyCollection<Entity>>? SelectionChanged;

    /// <summary>
    /// The primary selected entity (first in selection or most recently selected)
    /// </summary>
    public Entity PrimarySelection => _primarySelection;

    /// <summary>
    /// All currently selected entities
    /// </summary>
    public IReadOnlyCollection<Entity> SelectedEntities => _selectedEntities;

    /// <summary>
    /// Number of selected entities
    /// </summary>
    public int Count => _selectedEntities.Count;

    /// <summary>
    /// Whether any entity is selected
    /// </summary>
    public bool HasSelection => _selectedEntities.Count > 0;

    /// <summary>
    /// Select a single entity, clearing previous selection
    /// </summary>
    public void Select(Entity entity)
    {
        if (_selectedEntities.Count == 1 && _selectedEntities.Contains(entity))
            return; // Already selected

        _selectedEntities.Clear();
        
        if (!entity.IsNull)
        {
            _selectedEntities.Add(entity);
            _primarySelection = entity;
        }
        else
        {
            _primarySelection = Entity.Null;
        }

        SelectionChanged?.Invoke(_selectedEntities);
    }

    /// <summary>
    /// Add an entity to the selection (multi-select)
    /// </summary>
    public void AddToSelection(Entity entity)
    {
        if (entity.IsNull || _selectedEntities.Contains(entity))
            return;

        _selectedEntities.Add(entity);
        _primarySelection = entity;
        SelectionChanged?.Invoke(_selectedEntities);
    }

    /// <summary>
    /// Remove an entity from the selection
    /// </summary>
    public void RemoveFromSelection(Entity entity)
    {
        if (!_selectedEntities.Remove(entity))
            return;

        if (_primarySelection == entity)
        {
            _primarySelection = _selectedEntities.Count > 0 
                ? _selectedEntities.First() 
                : Entity.Null;
        }

        SelectionChanged?.Invoke(_selectedEntities);
    }

    /// <summary>
    /// Toggle an entity's selection state
    /// </summary>
    public void ToggleSelection(Entity entity)
    {
        if (_selectedEntities.Contains(entity))
            RemoveFromSelection(entity);
        else
            AddToSelection(entity);
    }

    /// <summary>
    /// Clear all selection
    /// </summary>
    public void ClearSelection()
    {
        if (_selectedEntities.Count == 0)
            return;

        _selectedEntities.Clear();
        _primarySelection = Entity.Null;
        SelectionChanged?.Invoke(_selectedEntities);
    }

    /// <summary>
    /// Check if an entity is selected
    /// </summary>
    public bool IsSelected(Entity entity)
    {
        return _selectedEntities.Contains(entity);
    }

    /// <summary>
    /// Select multiple entities, clearing previous selection
    /// </summary>
    public void SelectMultiple(IEnumerable<Entity> entities)
    {
        _selectedEntities.Clear();
        
        foreach (var entity in entities)
        {
            if (!entity.IsNull)
            {
                _selectedEntities.Add(entity);
            }
        }

        _primarySelection = _selectedEntities.Count > 0 
            ? _selectedEntities.First() 
            : Entity.Null;

        SelectionChanged?.Invoke(_selectedEntities);
    }

    /// <summary>
    /// Validate selection against a world (remove dead entities)
    /// </summary>
    public void ValidateSelection(World world)
    {
        var toRemove = _selectedEntities.Where(e => !world.IsAlive(e)).ToList();
        
        if (toRemove.Count == 0)
            return;

        foreach (var entity in toRemove)
        {
            _selectedEntities.Remove(entity);
        }

        if (!world.IsAlive(_primarySelection))
        {
            _primarySelection = _selectedEntities.Count > 0 
                ? _selectedEntities.First() 
                : Entity.Null;
        }

        SelectionChanged?.Invoke(_selectedEntities);
    }
}
