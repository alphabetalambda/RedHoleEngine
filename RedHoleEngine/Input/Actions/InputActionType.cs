namespace RedHoleEngine.Input.Actions;

/// <summary>
/// The type of value an input action produces.
/// </summary>
public enum InputActionType
{
    /// <summary>
    /// A button that is either pressed (1) or released (0).
    /// Examples: A button, trigger fully pressed, key pressed.
    /// </summary>
    Button,
    
    /// <summary>
    /// A single floating point value, typically in range [-1, 1] or [0, 1].
    /// Examples: Trigger analog value, scroll wheel delta.
    /// </summary>
    Axis,
    
    /// <summary>
    /// A 2D vector value (X, Y).
    /// Examples: Thumbstick position, mouse delta, touchpad position.
    /// </summary>
    Vector2,
    
    /// <summary>
    /// A 3D vector value (X, Y, Z).
    /// Examples: Gyroscope angular velocity (pitch, yaw, roll), accelerometer.
    /// </summary>
    Vector3
}

/// <summary>
/// Interaction types for button actions.
/// </summary>
public enum InputInteractionType
{
    /// <summary>
    /// Action triggers immediately when button state changes.
    /// </summary>
    Press,
    
    /// <summary>
    /// Action triggers when button is released.
    /// </summary>
    Release,
    
    /// <summary>
    /// Action triggers when button is held for a duration.
    /// </summary>
    Hold,
    
    /// <summary>
    /// Action triggers on multiple quick presses.
    /// </summary>
    MultiTap,
    
    /// <summary>
    /// Action value is 1 while held, 0 otherwise (for continuous input).
    /// </summary>
    Continuous
}

/// <summary>
/// The phase of an input action.
/// </summary>
public enum InputActionPhase
{
    /// <summary>
    /// The action is not active.
    /// </summary>
    Waiting,
    
    /// <summary>
    /// The action has started (button pressed, stick moved from center).
    /// </summary>
    Started,
    
    /// <summary>
    /// The action is being performed (button held, stick moved).
    /// </summary>
    Performed,
    
    /// <summary>
    /// The action was canceled (button released before hold completed).
    /// </summary>
    Canceled
}
