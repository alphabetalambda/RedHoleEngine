using System.Numerics;
using System.Text.Json.Serialization;

namespace RedHoleEngine.Input.Actions;

/// <summary>
/// Represents a single input action that can be bound to multiple physical inputs.
/// Actions are the primary way games read input - you query actions, not raw inputs.
/// </summary>
public class InputAction
{
    /// <summary>
    /// Unique identifier for this action.
    /// </summary>
    [JsonPropertyName("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString();
    
    /// <summary>
    /// Human-readable name for this action.
    /// </summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";
    
    /// <summary>
    /// The type of value this action produces.
    /// </summary>
    [JsonPropertyName("type")]
    public InputActionType Type { get; set; } = InputActionType.Button;
    
    /// <summary>
    /// The bindings for this action.
    /// </summary>
    [JsonPropertyName("bindings")]
    public List<InputBinding> Bindings { get; set; } = new();
    
    /// <summary>
    /// Composite bindings (e.g., WASD for movement).
    /// </summary>
    [JsonPropertyName("composites")]
    public List<CompositeBinding> Composites { get; set; } = new();
    
    /// <summary>
    /// Whether this action is currently enabled.
    /// </summary>
    [JsonIgnore]
    public bool Enabled { get; set; } = true;
    
    // ═══════════════════════════════════════════════════════════════════════════
    // RUNTIME STATE
    // ═══════════════════════════════════════════════════════════════════════════
    
    /// <summary>
    /// Current phase of this action.
    /// </summary>
    [JsonIgnore]
    public InputActionPhase Phase { get; internal set; } = InputActionPhase.Waiting;
    
    /// <summary>
    /// Current value as a float (for Button/Axis types).
    /// </summary>
    [JsonIgnore]
    public float ValueFloat { get; internal set; }
    
    /// <summary>
    /// Current value as a Vector2 (for Vector2 types).
    /// </summary>
    [JsonIgnore]
    public Vector2 ValueVector2 { get; internal set; }
    
    /// <summary>
    /// Current value as a Vector3 (for Vector3 types, like gyro).
    /// </summary>
    [JsonIgnore]
    public Vector3 ValueVector3 { get; internal set; }
    
    /// <summary>
    /// Previous frame's float value.
    /// </summary>
    [JsonIgnore]
    public float PreviousValueFloat { get; internal set; }
    
    /// <summary>
    /// Previous frame's Vector2 value.
    /// </summary>
    [JsonIgnore]
    public Vector2 PreviousValueVector2 { get; internal set; }
    
    /// <summary>
    /// Previous frame's Vector3 value.
    /// </summary>
    [JsonIgnore]
    public Vector3 PreviousValueVector3 { get; internal set; }
    
    // ═══════════════════════════════════════════════════════════════════════════
    // EVENTS
    // ═══════════════════════════════════════════════════════════════════════════
    
    /// <summary>
    /// Fired when the action starts (button pressed, stick moved from center).
    /// </summary>
    public event Action<InputAction>? Started;
    
    /// <summary>
    /// Fired when the action is performed (button held, continuous value change).
    /// </summary>
    public event Action<InputAction>? Performed;
    
    /// <summary>
    /// Fired when the action is canceled (button released).
    /// </summary>
    public event Action<InputAction>? Canceled;
    
    internal void FireStarted() => Started?.Invoke(this);
    internal void FirePerformed() => Performed?.Invoke(this);
    internal void FireCanceled() => Canceled?.Invoke(this);
    
    // ═══════════════════════════════════════════════════════════════════════════
    // CONVENIENCE PROPERTIES
    // ═══════════════════════════════════════════════════════════════════════════
    
    /// <summary>
    /// Whether the action was just pressed this frame (button actions).
    /// </summary>
    [JsonIgnore]
    public bool WasPressedThisFrame => Phase == InputActionPhase.Started;
    
    /// <summary>
    /// Whether the action was just released this frame (button actions).
    /// </summary>
    [JsonIgnore]
    public bool WasReleasedThisFrame => Phase == InputActionPhase.Canceled;
    
    /// <summary>
    /// Whether the action is currently pressed/active.
    /// </summary>
    [JsonIgnore]
    public bool IsPressed => Phase == InputActionPhase.Started || Phase == InputActionPhase.Performed;
    
    /// <summary>
    /// Read the current value as a bool (true if value > 0.5).
    /// </summary>
    public bool ReadValueAsBool() => ValueFloat > 0.5f;
    
    /// <summary>
    /// Read the current value as a float.
    /// </summary>
    public float ReadValueAsFloat() => ValueFloat;
    
    /// <summary>
    /// Read the current value as a Vector2.
    /// </summary>
    public Vector2 ReadValueAsVector2() => ValueVector2;
    
    /// <summary>
    /// Read the current value as a Vector3.
    /// </summary>
    public Vector3 ReadValueAsVector3() => ValueVector3;
    
    // ═══════════════════════════════════════════════════════════════════════════
    // FACTORY METHODS
    // ═══════════════════════════════════════════════════════════════════════════
    
    /// <summary>
    /// Create a button action.
    /// </summary>
    public static InputAction CreateButton(string name, params string[] bindingPaths)
    {
        var action = new InputAction
        {
            Name = name,
            Type = InputActionType.Button
        };
        
        foreach (var path in bindingPaths)
        {
            action.Bindings.Add(InputBinding.Create(path));
        }
        
        return action;
    }
    
    /// <summary>
    /// Create an axis action.
    /// </summary>
    public static InputAction CreateAxis(string name, params string[] bindingPaths)
    {
        var action = new InputAction
        {
            Name = name,
            Type = InputActionType.Axis
        };
        
        foreach (var path in bindingPaths)
        {
            action.Bindings.Add(InputBinding.Create(path));
        }
        
        return action;
    }
    
    /// <summary>
    /// Create a Vector2 action.
    /// </summary>
    public static InputAction CreateVector2(string name, params string[] bindingPaths)
    {
        var action = new InputAction
        {
            Name = name,
            Type = InputActionType.Vector2
        };
        
        foreach (var path in bindingPaths)
        {
            action.Bindings.Add(InputBinding.Create(path));
        }
        
        return action;
    }
    
    /// <summary>
    /// Create a Vector2 action with WASD-style composite binding.
    /// </summary>
    public static InputAction CreateVector2Composite(
        string name, 
        string upPath, string downPath, string leftPath, string rightPath)
    {
        var action = new InputAction
        {
            Name = name,
            Type = InputActionType.Vector2
        };
        
        action.Composites.Add(CompositeBinding.Create2DVector(
            InputBinding.Create(upPath, "up"),
            InputBinding.Create(downPath, "down"),
            InputBinding.Create(leftPath, "left"),
            InputBinding.Create(rightPath, "right")
        ));
        
        return action;
    }
    
    /// <summary>
    /// Create a Vector3 action (for gyroscope, etc.).
    /// </summary>
    public static InputAction CreateVector3(string name, params string[] bindingPaths)
    {
        var action = new InputAction
        {
            Name = name,
            Type = InputActionType.Vector3
        };
        
        foreach (var path in bindingPaths)
        {
            action.Bindings.Add(InputBinding.Create(path));
        }
        
        return action;
    }
    
    public override string ToString()
    {
        return $"Action({Name}, {Type}, {Phase})";
    }
}
