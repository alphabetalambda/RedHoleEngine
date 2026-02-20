using System.Text.Json;
using System.Text.Json.Serialization;

namespace RedHoleEngine.Input.Actions;

/// <summary>
/// A complete input action asset containing multiple action maps.
/// This is the top-level container that can be serialized to/from JSON.
/// </summary>
public class InputActionAsset
{
    /// <summary>
    /// Schema version for compatibility checking.
    /// </summary>
    [JsonPropertyName("version")]
    public int Version { get; set; } = 1;
    
    /// <summary>
    /// Human-readable name for this asset.
    /// </summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = "InputActions";
    
    /// <summary>
    /// The action maps in this asset.
    /// </summary>
    [JsonPropertyName("actionMaps")]
    public List<InputActionMap> ActionMaps { get; set; } = new();
    
    /// <summary>
    /// Control schemes supported by this asset.
    /// </summary>
    [JsonPropertyName("controlSchemes")]
    public List<ControlScheme> ControlSchemes { get; set; } = new();
    
    /// <summary>
    /// Lookup cache for action maps by name.
    /// </summary>
    [JsonIgnore]
    private Dictionary<string, InputActionMap>? _mapCache;
    
    /// <summary>
    /// Lookup cache for all actions by name (across all maps).
    /// </summary>
    [JsonIgnore]
    private Dictionary<string, InputAction>? _actionCache;
    
    /// <summary>
    /// Get an action map by name.
    /// </summary>
    public InputActionMap? FindActionMap(string name)
    {
        if (_mapCache == null)
        {
            _mapCache = new Dictionary<string, InputActionMap>(StringComparer.OrdinalIgnoreCase);
            foreach (var map in ActionMaps)
            {
                _mapCache[map.Name] = map;
            }
        }
        
        return _mapCache.TryGetValue(name, out var foundMap) ? foundMap : null;
    }
    
    /// <summary>
    /// Get an action map by name, throwing if not found.
    /// </summary>
    public InputActionMap GetActionMap(string name)
    {
        return FindActionMap(name) 
            ?? throw new KeyNotFoundException($"Action map '{name}' not found in asset '{Name}'");
    }
    
    /// <summary>
    /// Find an action by name across all action maps.
    /// </summary>
    public InputAction? FindAction(string name)
    {
        if (_actionCache == null)
        {
            _actionCache = new Dictionary<string, InputAction>(StringComparer.OrdinalIgnoreCase);
            foreach (var map in ActionMaps)
            {
                foreach (var action in map.Actions)
                {
                    // Allow both "MapName/ActionName" and just "ActionName"
                    _actionCache[$"{map.Name}/{action.Name}"] = action;
                    if (!_actionCache.ContainsKey(action.Name))
                    {
                        _actionCache[action.Name] = action;
                    }
                }
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
            ?? throw new KeyNotFoundException($"Action '{name}' not found in asset '{Name}'");
    }
    
    /// <summary>
    /// Indexer to get action maps by name.
    /// </summary>
    public InputActionMap this[string name] => GetActionMap(name);
    
    /// <summary>
    /// Add an action map to this asset.
    /// </summary>
    public InputActionMap AddActionMap(string name)
    {
        var map = InputActionMap.Create(name);
        ActionMaps.Add(map);
        InvalidateCache();
        return map;
    }
    
    /// <summary>
    /// Add an existing action map to this asset.
    /// </summary>
    public void AddActionMap(InputActionMap map)
    {
        ActionMaps.Add(map);
        InvalidateCache();
    }
    
    /// <summary>
    /// Enable all action maps.
    /// </summary>
    public void EnableAll()
    {
        foreach (var map in ActionMaps)
        {
            map.Enable();
        }
    }
    
    /// <summary>
    /// Disable all action maps.
    /// </summary>
    public void DisableAll()
    {
        foreach (var map in ActionMaps)
        {
            map.Disable();
        }
    }
    
    /// <summary>
    /// Invalidate caches (call after modifying maps/actions).
    /// </summary>
    public void InvalidateCache()
    {
        _mapCache = null;
        _actionCache = null;
    }
    
    /// <summary>
    /// Get all actions across all action maps.
    /// </summary>
    public IEnumerable<InputAction> GetAllActions()
    {
        foreach (var map in ActionMaps)
        {
            foreach (var action in map.Actions)
            {
                yield return action;
            }
        }
    }
    
    // ═══════════════════════════════════════════════════════════════════════════
    // SERIALIZATION
    // ═══════════════════════════════════════════════════════════════════════════
    
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };
    
    /// <summary>
    /// Serialize this asset to JSON.
    /// </summary>
    public string ToJson()
    {
        return JsonSerializer.Serialize(this, JsonOptions);
    }
    
    /// <summary>
    /// Deserialize an asset from JSON.
    /// </summary>
    public static InputActionAsset FromJson(string json)
    {
        return JsonSerializer.Deserialize<InputActionAsset>(json, JsonOptions) 
            ?? throw new InvalidOperationException("Failed to deserialize input action asset");
    }
    
    /// <summary>
    /// Load an asset from a file.
    /// </summary>
    public static InputActionAsset Load(string filePath)
    {
        var json = File.ReadAllText(filePath);
        return FromJson(json);
    }
    
    /// <summary>
    /// Save this asset to a file.
    /// </summary>
    public void Save(string filePath)
    {
        var json = ToJson();
        File.WriteAllText(filePath, json);
    }
    
    /// <summary>
    /// Create a new empty asset.
    /// </summary>
    public static InputActionAsset Create(string name)
    {
        return new InputActionAsset { Name = name };
    }
    
    public override string ToString()
    {
        return $"InputActionAsset({Name}, {ActionMaps.Count} maps)";
    }
}

/// <summary>
/// A control scheme defines a set of devices that work together.
/// </summary>
public class ControlScheme
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";
    
    [JsonPropertyName("bindingGroup")]
    public string BindingGroup { get; set; } = "";
    
    [JsonPropertyName("devices")]
    public List<DeviceRequirement> Devices { get; set; } = new();
    
    public static ControlScheme Create(string name, params string[] devicePaths)
    {
        var scheme = new ControlScheme
        {
            Name = name,
            BindingGroup = name
        };
        
        foreach (var path in devicePaths)
        {
            scheme.Devices.Add(new DeviceRequirement { DevicePath = path });
        }
        
        return scheme;
    }
    
    /// <summary>
    /// Standard keyboard + mouse control scheme.
    /// </summary>
    public static ControlScheme KeyboardMouse => Create("Keyboard&Mouse", "<Keyboard>", "<Mouse>");
    
    /// <summary>
    /// Standard gamepad control scheme.
    /// </summary>
    public static ControlScheme Gamepad => Create("Gamepad", "<Gamepad>");
    
    /// <summary>
    /// Gamepad with gyro support (Steam Deck, DualSense, etc.).
    /// </summary>
    public static ControlScheme GamepadWithGyro => Create("Gamepad+Gyro", "<Gamepad>", "<Gyro>");
}

/// <summary>
/// A requirement for a specific device in a control scheme.
/// </summary>
public class DeviceRequirement
{
    [JsonPropertyName("devicePath")]
    public string DevicePath { get; set; } = "";
    
    [JsonPropertyName("isOptional")]
    public bool IsOptional { get; set; }
}
