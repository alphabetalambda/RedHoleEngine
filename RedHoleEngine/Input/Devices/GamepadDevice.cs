using System.Numerics;
using Silk.NET.Input;

namespace RedHoleEngine.Input.Devices;

/// <summary>
/// Represents a gamepad/controller input device.
/// </summary>
public class GamepadDevice : InputDevice
{
    public override InputDeviceType DeviceType => InputDeviceType.Gamepad;
    
    /// <summary>
    /// The specific type of gamepad.
    /// </summary>
    public GamepadType GamepadType { get; internal set; } = GamepadType.Unknown;
    
    /// <summary>
    /// Capabilities supported by this gamepad.
    /// </summary>
    public GamepadCapabilities Capabilities { get; internal set; } = GamepadCapabilities.None;
    
    /// <summary>
    /// The underlying Silk.NET gamepad (if using Silk provider).
    /// </summary>
    internal IGamepad? SilkGamepad { get; set; }
    
    /// <summary>
    /// Deadzone for analog sticks (0-1).
    /// </summary>
    public float StickDeadzone { get; set; } = 0.15f;
    
    /// <summary>
    /// Deadzone for triggers (0-1).
    /// </summary>
    public float TriggerDeadzone { get; set; } = 0.05f;
    
    // Button states
    private readonly Dictionary<string, float> _buttonStates = new();
    private readonly Dictionary<string, float> _previousButtonStates = new();
    
    // Stick positions
    public Vector2 LeftStick { get; internal set; }
    public Vector2 RightStick { get; internal set; }
    public Vector2 PreviousLeftStick { get; private set; }
    public Vector2 PreviousRightStick { get; private set; }
    
    // Triggers
    public float LeftTrigger { get; internal set; }
    public float RightTrigger { get; internal set; }
    
    // D-Pad
    public Vector2 DPad { get; internal set; }
    
    // Trackpads (Steam Deck)
    public Vector2 LeftTrackpad { get; internal set; }
    public Vector2 RightTrackpad { get; internal set; }
    public bool LeftTrackpadTouched { get; internal set; }
    public bool RightTrackpadTouched { get; internal set; }
    
    public GamepadDevice()
    {
        Name = "Gamepad";
    }
    
    public override void BeginFrame()
    {
        base.BeginFrame();
        
        PreviousLeftStick = LeftStick;
        PreviousRightStick = RightStick;
        
        _previousButtonStates.Clear();
        foreach (var kvp in _buttonStates)
        {
            _previousButtonStates[kvp.Key] = kvp.Value;
        }
    }
    
    /// <summary>
    /// Set button state (0 or 1 for digital, 0-1 for analog).
    /// </summary>
    internal void SetButtonState(string button, float value)
    {
        _buttonStates[button] = value;
    }
    
    /// <summary>
    /// Apply deadzone to a stick value.
    /// </summary>
    public Vector2 ApplyStickDeadzone(Vector2 value)
    {
        var magnitude = value.Length();
        if (magnitude < StickDeadzone)
            return Vector2.Zero;
        
        // Remap from deadzone-1 to 0-1
        var normalized = value / magnitude;
        var remapped = (magnitude - StickDeadzone) / (1f - StickDeadzone);
        return normalized * remapped;
    }
    
    /// <summary>
    /// Apply deadzone to a trigger value.
    /// </summary>
    public float ApplyTriggerDeadzone(float value)
    {
        if (value < TriggerDeadzone)
            return 0f;
        return (value - TriggerDeadzone) / (1f - TriggerDeadzone);
    }
    
    /// <summary>
    /// Check if a button is pressed (value > 0.5).
    /// </summary>
    public bool IsButtonPressed(string button)
    {
        return _buttonStates.TryGetValue(button, out var value) && value > 0.5f;
    }
    
    /// <summary>
    /// Check if a button was just pressed this frame.
    /// </summary>
    public bool WasButtonPressedThisFrame(string button)
    {
        var current = _buttonStates.TryGetValue(button, out var curr) && curr > 0.5f;
        var previous = _previousButtonStates.TryGetValue(button, out var prev) && prev > 0.5f;
        return current && !previous;
    }
    
    /// <summary>
    /// Check if a button was just released this frame.
    /// </summary>
    public bool WasButtonReleasedThisFrame(string button)
    {
        var current = _buttonStates.TryGetValue(button, out var curr) && curr > 0.5f;
        var previous = _previousButtonStates.TryGetValue(button, out var prev) && prev > 0.5f;
        return !current && previous;
    }
    
    public override float ReadButton(string control)
    {
        return control switch
        {
            "buttonSouth" => _buttonStates.GetValueOrDefault("buttonSouth"),
            "buttonEast" => _buttonStates.GetValueOrDefault("buttonEast"),
            "buttonWest" => _buttonStates.GetValueOrDefault("buttonWest"),
            "buttonNorth" => _buttonStates.GetValueOrDefault("buttonNorth"),
            "leftShoulder" => _buttonStates.GetValueOrDefault("leftShoulder"),
            "rightShoulder" => _buttonStates.GetValueOrDefault("rightShoulder"),
            "leftStickPress" => _buttonStates.GetValueOrDefault("leftStickPress"),
            "rightStickPress" => _buttonStates.GetValueOrDefault("rightStickPress"),
            "start" => _buttonStates.GetValueOrDefault("start"),
            "select" => _buttonStates.GetValueOrDefault("select"),
            "guide" => _buttonStates.GetValueOrDefault("guide"),
            "dpad/up" => DPad.Y > 0 ? 1f : 0f,
            "dpad/down" => DPad.Y < 0 ? 1f : 0f,
            "dpad/left" => DPad.X < 0 ? 1f : 0f,
            "dpad/right" => DPad.X > 0 ? 1f : 0f,
            "leftPaddle1" => _buttonStates.GetValueOrDefault("leftPaddle1"),
            "leftPaddle2" => _buttonStates.GetValueOrDefault("leftPaddle2"),
            "rightPaddle1" => _buttonStates.GetValueOrDefault("rightPaddle1"),
            "rightPaddle2" => _buttonStates.GetValueOrDefault("rightPaddle2"),
            "leftTrackpadPress" => _buttonStates.GetValueOrDefault("leftTrackpadPress"),
            "rightTrackpadPress" => _buttonStates.GetValueOrDefault("rightTrackpadPress"),
            _ => 0f
        };
    }
    
    public override float ReadAxis(string control)
    {
        return control switch
        {
            "leftTrigger" => ApplyTriggerDeadzone(LeftTrigger),
            "rightTrigger" => ApplyTriggerDeadzone(RightTrigger),
            "leftStick/x" => ApplyStickDeadzone(LeftStick).X,
            "leftStick/y" => ApplyStickDeadzone(LeftStick).Y,
            "rightStick/x" => ApplyStickDeadzone(RightStick).X,
            "rightStick/y" => ApplyStickDeadzone(RightStick).Y,
            _ => ReadButton(control)
        };
    }
    
    public override Vector2 ReadVector2(string control)
    {
        return control switch
        {
            "leftStick" => ApplyStickDeadzone(LeftStick),
            "rightStick" => ApplyStickDeadzone(RightStick),
            "dpad" => DPad,
            "leftTrackpad" => LeftTrackpad,
            "rightTrackpad" => RightTrackpad,
            _ => Vector2.Zero
        };
    }
    
    /// <summary>
    /// Set vibration (if supported).
    /// </summary>
    public virtual void SetVibration(float leftMotor, float rightMotor)
    {
        if (!Capabilities.HasFlag(GamepadCapabilities.Vibration))
            return;
            
        // Clamp values
        leftMotor = Math.Clamp(leftMotor, 0f, 1f);
        rightMotor = Math.Clamp(rightMotor, 0f, 1f);
        
        // Implementation depends on provider - Silk.NET uses VibrationMotors
        if (SilkGamepad?.VibrationMotors.Count > 0)
        {
            // Motor 0 = left/low-frequency, Motor 1 = right/high-frequency
            if (SilkGamepad.VibrationMotors.Count > 0)
                SilkGamepad.VibrationMotors[0].Speed = leftMotor;
            if (SilkGamepad.VibrationMotors.Count > 1)
                SilkGamepad.VibrationMotors[1].Speed = rightMotor;
        }
    }
    
    /// <summary>
    /// Stop all vibration.
    /// </summary>
    public virtual void StopVibration()
    {
        SetVibration(0f, 0f);
    }
    
    /// <summary>
    /// Map Silk.NET Button to our button name.
    /// </summary>
    public static string ButtonToControl(ButtonName button)
    {
        return button switch
        {
            ButtonName.A => "buttonSouth",
            ButtonName.B => "buttonEast",
            ButtonName.X => "buttonWest",
            ButtonName.Y => "buttonNorth",
            ButtonName.LeftBumper => "leftShoulder",
            ButtonName.RightBumper => "rightShoulder",
            ButtonName.Back => "select",
            ButtonName.Start => "start",
            ButtonName.Home => "guide",
            ButtonName.LeftStick => "leftStickPress",
            ButtonName.RightStick => "rightStickPress",
            ButtonName.DPadUp => "dpad/up",
            ButtonName.DPadDown => "dpad/down",
            ButtonName.DPadLeft => "dpad/left",
            ButtonName.DPadRight => "dpad/right",
            _ => button.ToString().ToLowerInvariant()
        };
    }
}
