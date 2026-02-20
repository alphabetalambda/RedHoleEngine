using System.Numerics;
using RedHoleEngine.Core.ECS;

namespace RedHoleEngine.Components;

/// <summary>
/// Component that receives input from the InputSystem.
/// Add this to entities that need to read input (players, vehicles, etc.).
/// </summary>
public struct InputReceiverComponent : IComponent
{
    /// <summary>
    /// Name of the action map to use (e.g., "Gameplay", "UI", "Driving").
    /// </summary>
    public string ActionMapName;
    
    /// <summary>
    /// Whether this receiver is currently active.
    /// </summary>
    public bool IsActive;
    
    /// <summary>
    /// Player index for local multiplayer (0 = first player).
    /// </summary>
    public int PlayerIndex;
    
    // ═══════════════════════════════════════════════════════════════════════════
    // CONVENIENCE VALUES (updated by InputSystem)
    // ═══════════════════════════════════════════════════════════════════════════
    
    /// <summary>
    /// Movement input (from "Move" action).
    /// </summary>
    public Vector2 MoveInput;
    
    /// <summary>
    /// Look input (from "Look" action).
    /// </summary>
    public Vector2 LookInput;
    
    /// <summary>
    /// Gyro input (from "GyroLook" action).
    /// </summary>
    public Vector3 GyroInput;
    
    // ═══════════════════════════════════════════════════════════════════════════
    // CACHED ACTION VALUES
    // ═══════════════════════════════════════════════════════════════════════════
    
    /// <summary>
    /// Current button states (true = pressed).
    /// </summary>
    public Dictionary<string, bool> ButtonStates;
    
    /// <summary>
    /// Buttons that were just pressed this frame.
    /// </summary>
    public Dictionary<string, bool> ButtonPressed;
    
    /// <summary>
    /// Buttons that were just released this frame.
    /// </summary>
    public Dictionary<string, bool> ButtonReleased;
    
    /// <summary>
    /// Axis values.
    /// </summary>
    public Dictionary<string, float> AxisValues;
    
    /// <summary>
    /// Vector2 values.
    /// </summary>
    public Dictionary<string, Vector2> Vector2Values;
    
    /// <summary>
    /// Vector3 values (gyro, etc.).
    /// </summary>
    public Dictionary<string, Vector3> Vector3Values;
    
    // ═══════════════════════════════════════════════════════════════════════════
    // CONVENIENCE METHODS
    // ═══════════════════════════════════════════════════════════════════════════
    
    /// <summary>
    /// Check if a button is currently pressed.
    /// </summary>
    public readonly bool IsButtonPressed(string action)
    {
        return ButtonStates.TryGetValue(action, out var pressed) && pressed;
    }
    
    /// <summary>
    /// Check if a button was just pressed this frame.
    /// </summary>
    public readonly bool WasButtonPressedThisFrame(string action)
    {
        return ButtonPressed.TryGetValue(action, out var pressed) && pressed;
    }
    
    /// <summary>
    /// Check if a button was just released this frame.
    /// </summary>
    public readonly bool WasButtonReleasedThisFrame(string action)
    {
        return ButtonReleased.TryGetValue(action, out var released) && released;
    }
    
    /// <summary>
    /// Get an axis value.
    /// </summary>
    public readonly float GetAxis(string action)
    {
        return AxisValues.TryGetValue(action, out var value) ? value : 0f;
    }
    
    /// <summary>
    /// Get a Vector2 value.
    /// </summary>
    public readonly Vector2 GetVector2(string action)
    {
        return Vector2Values.TryGetValue(action, out var value) ? value : Vector2.Zero;
    }
    
    /// <summary>
    /// Get a Vector3 value.
    /// </summary>
    public readonly Vector3 GetVector3(string action)
    {
        return Vector3Values.TryGetValue(action, out var value) ? value : Vector3.Zero;
    }
    
    // ═══════════════════════════════════════════════════════════════════════════
    // FACTORY METHODS
    // ═══════════════════════════════════════════════════════════════════════════
    
    /// <summary>
    /// Create an input receiver for the gameplay action map.
    /// </summary>
    public static InputReceiverComponent CreateGameplay(int playerIndex = 0)
    {
        return new InputReceiverComponent
        {
            ActionMapName = "Gameplay",
            IsActive = true,
            PlayerIndex = playerIndex,
            ButtonStates = new Dictionary<string, bool>(),
            ButtonPressed = new Dictionary<string, bool>(),
            ButtonReleased = new Dictionary<string, bool>(),
            AxisValues = new Dictionary<string, float>(),
            Vector2Values = new Dictionary<string, Vector2>(),
            Vector3Values = new Dictionary<string, Vector3>()
        };
    }
    
    /// <summary>
    /// Create an input receiver for the UI action map.
    /// </summary>
    public static InputReceiverComponent CreateUI(int playerIndex = 0)
    {
        return new InputReceiverComponent
        {
            ActionMapName = "UI",
            IsActive = true,
            PlayerIndex = playerIndex,
            ButtonStates = new Dictionary<string, bool>(),
            ButtonPressed = new Dictionary<string, bool>(),
            ButtonReleased = new Dictionary<string, bool>(),
            AxisValues = new Dictionary<string, float>(),
            Vector2Values = new Dictionary<string, Vector2>(),
            Vector3Values = new Dictionary<string, Vector3>()
        };
    }
    
    /// <summary>
    /// Create an input receiver for a custom action map.
    /// </summary>
    public static InputReceiverComponent Create(string actionMapName, int playerIndex = 0)
    {
        return new InputReceiverComponent
        {
            ActionMapName = actionMapName,
            IsActive = true,
            PlayerIndex = playerIndex,
            ButtonStates = new Dictionary<string, bool>(),
            ButtonPressed = new Dictionary<string, bool>(),
            ButtonReleased = new Dictionary<string, bool>(),
            AxisValues = new Dictionary<string, float>(),
            Vector2Values = new Dictionary<string, Vector2>(),
            Vector3Values = new Dictionary<string, Vector3>()
        };
    }
}

/// <summary>
/// Component for entities that provide haptic feedback.
/// </summary>
public struct HapticSourceComponent : IComponent
{
    /// <summary>
    /// Whether haptic feedback is enabled for this entity.
    /// </summary>
    public bool Enabled;
    
    /// <summary>
    /// Intensity multiplier for haptic effects (0-1).
    /// </summary>
    public float IntensityMultiplier;
    
    /// <summary>
    /// Player index this haptic source is associated with.
    /// </summary>
    public int PlayerIndex;
    
    public static HapticSourceComponent Create(int playerIndex = 0, float intensity = 1f)
    {
        return new HapticSourceComponent
        {
            Enabled = true,
            IntensityMultiplier = intensity,
            PlayerIndex = playerIndex
        };
    }
}
