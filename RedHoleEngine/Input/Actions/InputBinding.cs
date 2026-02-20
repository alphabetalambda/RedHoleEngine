using System.Text.Json.Serialization;

namespace RedHoleEngine.Input.Actions;

/// <summary>
/// Maps an input action to one or more physical input paths.
/// </summary>
public class InputBinding
{
    /// <summary>
    /// Unique identifier for this binding.
    /// </summary>
    [JsonPropertyName("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString();
    
    /// <summary>
    /// The input path this binding maps to (e.g., "&lt;Gamepad&gt;/buttonSouth").
    /// </summary>
    [JsonPropertyName("path")]
    public string Path { get; set; } = "";
    
    /// <summary>
    /// Optional name for display in UI (e.g., "Jump", "Fire").
    /// </summary>
    [JsonPropertyName("name")]
    public string? Name { get; set; }
    
    /// <summary>
    /// The interaction type for button actions.
    /// </summary>
    [JsonPropertyName("interaction")]
    public InputInteractionType Interaction { get; set; } = InputInteractionType.Press;
    
    /// <summary>
    /// Processors to apply to the input value (e.g., "invert", "deadzone(0.1)").
    /// </summary>
    [JsonPropertyName("processors")]
    public string? Processors { get; set; }
    
    /// <summary>
    /// Control scheme this binding belongs to (e.g., "Keyboard&amp;Mouse", "Gamepad").
    /// </summary>
    [JsonPropertyName("controlScheme")]
    public string? ControlScheme { get; set; }
    
    /// <summary>
    /// For composite bindings (e.g., WASD), the part this represents.
    /// </summary>
    [JsonPropertyName("compositePart")]
    public string? CompositePart { get; set; }
    
    /// <summary>
    /// Whether this binding is currently active.
    /// </summary>
    [JsonPropertyName("isActive")]
    public bool IsActive { get; set; } = true;
    
    /// <summary>
    /// Create a simple binding to a single input path.
    /// </summary>
    public static InputBinding Create(string path, string? name = null)
    {
        return new InputBinding
        {
            Path = path,
            Name = name
        };
    }
    
    /// <summary>
    /// Create a binding with a specific interaction type.
    /// </summary>
    public static InputBinding CreateWithInteraction(string path, InputInteractionType interaction)
    {
        return new InputBinding
        {
            Path = path,
            Interaction = interaction
        };
    }
    
    /// <summary>
    /// Create a composite part binding (for WASD-style inputs).
    /// </summary>
    public static InputBinding CreateCompositePart(string path, string compositePart, string controlScheme)
    {
        return new InputBinding
        {
            Path = path,
            CompositePart = compositePart,
            ControlScheme = controlScheme
        };
    }
    
    public override string ToString()
    {
        return $"Binding({Path}, {Interaction})";
    }
}

/// <summary>
/// A composite binding that combines multiple inputs into a single value.
/// Used for things like WASD movement where 4 keys create a Vector2.
/// </summary>
public class CompositeBinding
{
    /// <summary>
    /// The type of composite (e.g., "2DVector", "1DAxis", "ButtonWithModifier").
    /// </summary>
    [JsonPropertyName("type")]
    public string Type { get; set; } = "2DVector";
    
    /// <summary>
    /// The parts that make up this composite.
    /// For 2DVector: up, down, left, right
    /// For 1DAxis: negative, positive
    /// </summary>
    [JsonPropertyName("parts")]
    public Dictionary<string, InputBinding> Parts { get; set; } = new();
    
    /// <summary>
    /// Create a 2D vector composite from 4 directional inputs.
    /// </summary>
    public static CompositeBinding Create2DVector(
        InputBinding up, 
        InputBinding down, 
        InputBinding left, 
        InputBinding right)
    {
        return new CompositeBinding
        {
            Type = "2DVector",
            Parts = new Dictionary<string, InputBinding>
            {
                ["up"] = up,
                ["down"] = down,
                ["left"] = left,
                ["right"] = right
            }
        };
    }
    
    /// <summary>
    /// Create a 1D axis composite from negative/positive inputs.
    /// </summary>
    public static CompositeBinding Create1DAxis(InputBinding negative, InputBinding positive)
    {
        return new CompositeBinding
        {
            Type = "1DAxis",
            Parts = new Dictionary<string, InputBinding>
            {
                ["negative"] = negative,
                ["positive"] = positive
            }
        };
    }
}
