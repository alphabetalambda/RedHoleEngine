using System.Text.Json.Serialization;

namespace RedHoleEngine.Input.Actions;

/// <summary>
/// A collection of related input actions, typically for a specific context 
/// (e.g., "Gameplay", "UI", "Vehicle").
/// </summary>
public class InputActionMap
{
    /// <summary>
    /// Unique identifier for this action map.
    /// </summary>
    [JsonPropertyName("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString();
    
    /// <summary>
    /// Human-readable name for this action map.
    /// </summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";
    
    /// <summary>
    /// The actions in this map.
    /// </summary>
    [JsonPropertyName("actions")]
    public List<InputAction> Actions { get; set; } = new();
    
    /// <summary>
    /// Whether this action map is currently enabled.
    /// When disabled, none of its actions will receive input.
    /// </summary>
    [JsonIgnore]
    public bool Enabled { get; private set; } = true;
    
    /// <summary>
    /// Lookup cache for actions by name.
    /// </summary>
    [JsonIgnore]
    private Dictionary<string, InputAction>? _actionCache;
    
    /// <summary>
    /// Enable this action map and all its actions.
    /// </summary>
    public void Enable()
    {
        Enabled = true;
        foreach (var action in Actions)
        {
            action.Enabled = true;
        }
    }
    
    /// <summary>
    /// Disable this action map and all its actions.
    /// </summary>
    public void Disable()
    {
        Enabled = false;
        foreach (var action in Actions)
        {
            action.Enabled = false;
        }
    }
    
    /// <summary>
    /// Get an action by name.
    /// </summary>
    public InputAction? FindAction(string name)
    {
        // Build cache on first access
        if (_actionCache == null)
        {
            _actionCache = new Dictionary<string, InputAction>(StringComparer.OrdinalIgnoreCase);
            foreach (var action in Actions)
            {
                _actionCache[action.Name] = action;
            }
        }
        
        return _actionCache.TryGetValue(name, out var foundAction) ? foundAction : null;
    }
    
    /// <summary>
    /// Get an action by name, throwing if not found.
    /// </summary>
    public InputAction GetAction(string name)
    {
        return FindAction(name) 
            ?? throw new KeyNotFoundException($"Action '{name}' not found in action map '{Name}'");
    }
    
    /// <summary>
    /// Indexer to get actions by name.
    /// </summary>
    public InputAction this[string name] => GetAction(name);
    
    /// <summary>
    /// Add an action to this map.
    /// </summary>
    public InputAction AddAction(InputAction action)
    {
        Actions.Add(action);
        _actionCache = null; // Invalidate cache
        return action;
    }
    
    /// <summary>
    /// Add a button action to this map.
    /// </summary>
    public InputAction AddButton(string name, params string[] bindingPaths)
    {
        var action = InputAction.CreateButton(name, bindingPaths);
        return AddAction(action);
    }
    
    /// <summary>
    /// Add an axis action to this map.
    /// </summary>
    public InputAction AddAxis(string name, params string[] bindingPaths)
    {
        var action = InputAction.CreateAxis(name, bindingPaths);
        return AddAction(action);
    }
    
    /// <summary>
    /// Add a Vector2 action to this map.
    /// </summary>
    public InputAction AddVector2(string name, params string[] bindingPaths)
    {
        var action = InputAction.CreateVector2(name, bindingPaths);
        return AddAction(action);
    }
    
    /// <summary>
    /// Add a Vector2 action with WASD-style composite binding.
    /// </summary>
    public InputAction AddVector2Composite(
        string name,
        string upPath, string downPath, string leftPath, string rightPath)
    {
        var action = InputAction.CreateVector2Composite(name, upPath, downPath, leftPath, rightPath);
        return AddAction(action);
    }
    
    /// <summary>
    /// Add a Vector3 action (gyro, accelerometer).
    /// </summary>
    public InputAction AddVector3(string name, params string[] bindingPaths)
    {
        var action = InputAction.CreateVector3(name, bindingPaths);
        return AddAction(action);
    }
    
    /// <summary>
    /// Invalidate the action cache (call after modifying actions).
    /// </summary>
    public void InvalidateCache()
    {
        _actionCache = null;
    }
    
    /// <summary>
    /// Create an empty action map.
    /// </summary>
    public static InputActionMap Create(string name)
    {
        return new InputActionMap { Name = name };
    }
    
    public override string ToString()
    {
        return $"ActionMap({Name}, {Actions.Count} actions, {(Enabled ? "enabled" : "disabled")})";
    }
}
