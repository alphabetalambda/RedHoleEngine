using System.Numerics;
using Silk.NET.Input;

namespace RedHoleEngine.Input.Devices;

/// <summary>
/// Represents a mouse input device.
/// </summary>
public class MouseDevice : InputDevice
{
    public override InputDeviceType DeviceType => InputDeviceType.Mouse;
    
    /// <summary>
    /// The underlying Silk.NET mouse (if using Silk provider).
    /// </summary>
    internal IMouse? SilkMouse { get; set; }
    
    /// <summary>
    /// Current mouse position in screen coordinates.
    /// </summary>
    public Vector2 Position { get; internal set; }
    
    /// <summary>
    /// Mouse movement delta since last frame.
    /// </summary>
    public Vector2 Delta { get; internal set; }
    
    /// <summary>
    /// Scroll wheel delta (Y = vertical, X = horizontal).
    /// </summary>
    public Vector2 ScrollDelta { get; internal set; }
    
    /// <summary>
    /// Previous frame position.
    /// </summary>
    public Vector2 PreviousPosition { get; private set; }
    
    /// <summary>
    /// Whether the cursor is currently captured (hidden and locked).
    /// </summary>
    public bool IsCursorCaptured { get; internal set; }
    
    /// <summary>
    /// Current button states.
    /// </summary>
    private readonly Dictionary<MouseButton, bool> _buttonStates = new();
    private readonly Dictionary<MouseButton, bool> _previousButtonStates = new();
    
    public MouseDevice()
    {
        Name = "Mouse";
    }
    
    public override void BeginFrame()
    {
        base.BeginFrame();
        
        // Save previous position
        PreviousPosition = Position;
        
        // Reset delta (will be accumulated during frame)
        Delta = Vector2.Zero;
        ScrollDelta = Vector2.Zero;
        
        // Copy current to previous
        _previousButtonStates.Clear();
        foreach (var kvp in _buttonStates)
        {
            _previousButtonStates[kvp.Key] = kvp.Value;
        }
    }
    
    /// <summary>
    /// Update button state (called by provider).
    /// </summary>
    internal void SetButtonState(MouseButton button, bool pressed)
    {
        _buttonStates[button] = pressed;
    }
    
    /// <summary>
    /// Add to mouse delta (called by provider).
    /// </summary>
    internal void AddDelta(Vector2 delta)
    {
        Delta += delta;
    }
    
    /// <summary>
    /// Set scroll delta (called by provider).
    /// </summary>
    internal void SetScrollDelta(Vector2 delta)
    {
        ScrollDelta = delta;
    }
    
    /// <summary>
    /// Check if a mouse button is currently pressed.
    /// </summary>
    public bool IsButtonPressed(MouseButton button)
    {
        return _buttonStates.TryGetValue(button, out var pressed) && pressed;
    }
    
    /// <summary>
    /// Check if a mouse button was just pressed this frame.
    /// </summary>
    public bool WasButtonPressedThisFrame(MouseButton button)
    {
        var current = IsButtonPressed(button);
        var previous = _previousButtonStates.TryGetValue(button, out var prev) && prev;
        return current && !previous;
    }
    
    /// <summary>
    /// Check if a mouse button was just released this frame.
    /// </summary>
    public bool WasButtonReleasedThisFrame(MouseButton button)
    {
        var current = IsButtonPressed(button);
        var previous = _previousButtonStates.TryGetValue(button, out var prev) && prev;
        return !current && previous;
    }
    
    /// <summary>
    /// Set cursor capture mode.
    /// </summary>
    public void SetCursorCaptured(bool captured)
    {
        IsCursorCaptured = captured;
        if (SilkMouse != null)
        {
            SilkMouse.Cursor.CursorMode = captured ? CursorMode.Raw : CursorMode.Normal;
        }
    }
    
    public override float ReadButton(string control)
    {
        return control switch
        {
            "leftButton" => IsButtonPressed(MouseButton.Left) ? 1f : 0f,
            "rightButton" => IsButtonPressed(MouseButton.Right) ? 1f : 0f,
            "middleButton" => IsButtonPressed(MouseButton.Middle) ? 1f : 0f,
            "forwardButton" => IsButtonPressed(MouseButton.Button4) ? 1f : 0f,
            "backButton" => IsButtonPressed(MouseButton.Button5) ? 1f : 0f,
            _ => 0f
        };
    }
    
    public override float ReadAxis(string control)
    {
        return control switch
        {
            "scroll/y" => ScrollDelta.Y,
            "scroll/x" => ScrollDelta.X,
            _ => ReadButton(control)
        };
    }
    
    public override Vector2 ReadVector2(string control)
    {
        return control switch
        {
            "position" => Position,
            "delta" => Delta,
            "scroll" => ScrollDelta,
            _ => Vector2.Zero
        };
    }
}
