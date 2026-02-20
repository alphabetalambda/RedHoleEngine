using System.Numerics;
using RedHoleEngine.Input.Actions;
using RedHoleEngine.Input.Devices;

namespace RedHoleEngine.Input;

/// <summary>
/// Snapshot of all input state for a single frame.
/// Can be used for input recording/playback.
/// </summary>
public class InputState
{
    /// <summary>
    /// Frame number when this state was captured.
    /// </summary>
    public long FrameNumber { get; set; }
    
    /// <summary>
    /// Timestamp in seconds since application start.
    /// </summary>
    public double Timestamp { get; set; }
    
    // ═══════════════════════════════════════════════════════════════════════════
    // KEYBOARD STATE
    // ═══════════════════════════════════════════════════════════════════════════
    
    /// <summary>
    /// Keys that are currently pressed.
    /// </summary>
    public HashSet<string> PressedKeys { get; set; } = new();
    
    /// <summary>
    /// Keys that were pressed this frame.
    /// </summary>
    public HashSet<string> JustPressedKeys { get; set; } = new();
    
    /// <summary>
    /// Keys that were released this frame.
    /// </summary>
    public HashSet<string> JustReleasedKeys { get; set; } = new();
    
    // ═══════════════════════════════════════════════════════════════════════════
    // MOUSE STATE
    // ═══════════════════════════════════════════════════════════════════════════
    
    /// <summary>
    /// Mouse position in screen coordinates.
    /// </summary>
    public Vector2 MousePosition { get; set; }
    
    /// <summary>
    /// Mouse movement delta since last frame.
    /// </summary>
    public Vector2 MouseDelta { get; set; }
    
    /// <summary>
    /// Scroll wheel delta.
    /// </summary>
    public Vector2 ScrollDelta { get; set; }
    
    /// <summary>
    /// Mouse buttons currently pressed.
    /// </summary>
    public HashSet<int> PressedMouseButtons { get; set; } = new();
    
    // ═══════════════════════════════════════════════════════════════════════════
    // GAMEPAD STATE (for primary gamepad)
    // ═══════════════════════════════════════════════════════════════════════════
    
    /// <summary>
    /// Whether any gamepad is connected.
    /// </summary>
    public bool GamepadConnected { get; set; }
    
    /// <summary>
    /// Left thumbstick position.
    /// </summary>
    public Vector2 LeftStick { get; set; }
    
    /// <summary>
    /// Right thumbstick position.
    /// </summary>
    public Vector2 RightStick { get; set; }
    
    /// <summary>
    /// Left trigger value (0-1).
    /// </summary>
    public float LeftTrigger { get; set; }
    
    /// <summary>
    /// Right trigger value (0-1).
    /// </summary>
    public float RightTrigger { get; set; }
    
    /// <summary>
    /// D-Pad direction.
    /// </summary>
    public Vector2 DPad { get; set; }
    
    /// <summary>
    /// Gamepad buttons currently pressed.
    /// </summary>
    public HashSet<string> PressedGamepadButtons { get; set; } = new();
    
    // ═══════════════════════════════════════════════════════════════════════════
    // GYRO STATE
    // ═══════════════════════════════════════════════════════════════════════════
    
    /// <summary>
    /// Whether gyro is available and enabled.
    /// </summary>
    public bool GyroActive { get; set; }
    
    /// <summary>
    /// Gyroscope angular velocity (degrees/second).
    /// X = Pitch, Y = Yaw, Z = Roll.
    /// </summary>
    public Vector3 GyroAngularVelocity { get; set; }
    
    /// <summary>
    /// Accelerometer data.
    /// </summary>
    public Vector3 Acceleration { get; set; }
    
    // ═══════════════════════════════════════════════════════════════════════════
    // METHODS
    // ═══════════════════════════════════════════════════════════════════════════
    
    /// <summary>
    /// Capture current state from input devices.
    /// </summary>
    public void Capture(
        KeyboardDevice? keyboard,
        MouseDevice? mouse,
        GamepadDevice? gamepad,
        GyroDevice? gyro,
        long frameNumber,
        double timestamp)
    {
        FrameNumber = frameNumber;
        Timestamp = timestamp;
        
        // Capture keyboard
        PressedKeys.Clear();
        JustPressedKeys.Clear();
        JustReleasedKeys.Clear();
        
        if (keyboard != null)
        {
            // Note: For full capture we'd need to iterate all possible keys
            // This is simplified - full implementation would track all key states
        }
        
        // Capture mouse
        if (mouse != null)
        {
            MousePosition = mouse.Position;
            MouseDelta = mouse.Delta;
            ScrollDelta = mouse.ScrollDelta;
            
            PressedMouseButtons.Clear();
            if (mouse.IsButtonPressed(Silk.NET.Input.MouseButton.Left))
                PressedMouseButtons.Add(0);
            if (mouse.IsButtonPressed(Silk.NET.Input.MouseButton.Right))
                PressedMouseButtons.Add(1);
            if (mouse.IsButtonPressed(Silk.NET.Input.MouseButton.Middle))
                PressedMouseButtons.Add(2);
        }
        
        // Capture gamepad
        GamepadConnected = gamepad?.IsConnected ?? false;
        if (gamepad != null && gamepad.IsConnected)
        {
            LeftStick = gamepad.LeftStick;
            RightStick = gamepad.RightStick;
            LeftTrigger = gamepad.LeftTrigger;
            RightTrigger = gamepad.RightTrigger;
            DPad = gamepad.DPad;
            
            PressedGamepadButtons.Clear();
            // Capture button states
            foreach (var buttonName in new[] 
            { 
                "buttonSouth", "buttonEast", "buttonWest", "buttonNorth",
                "leftShoulder", "rightShoulder", "start", "select",
                "leftStickPress", "rightStickPress"
            })
            {
                if (gamepad.IsButtonPressed(buttonName))
                    PressedGamepadButtons.Add(buttonName);
            }
        }
        
        // Capture gyro
        GyroActive = gyro?.IsEnabled ?? false;
        if (gyro != null && gyro.IsEnabled)
        {
            GyroAngularVelocity = gyro.GetProcessedAngularVelocity();
            Acceleration = gyro.Acceleration;
        }
    }
    
    /// <summary>
    /// Create a deep copy of this state.
    /// </summary>
    public InputState Clone()
    {
        return new InputState
        {
            FrameNumber = FrameNumber,
            Timestamp = Timestamp,
            MousePosition = MousePosition,
            MouseDelta = MouseDelta,
            ScrollDelta = ScrollDelta,
            PressedMouseButtons = new HashSet<int>(PressedMouseButtons),
            PressedKeys = new HashSet<string>(PressedKeys),
            JustPressedKeys = new HashSet<string>(JustPressedKeys),
            JustReleasedKeys = new HashSet<string>(JustReleasedKeys),
            GamepadConnected = GamepadConnected,
            LeftStick = LeftStick,
            RightStick = RightStick,
            LeftTrigger = LeftTrigger,
            RightTrigger = RightTrigger,
            DPad = DPad,
            PressedGamepadButtons = new HashSet<string>(PressedGamepadButtons),
            GyroActive = GyroActive,
            GyroAngularVelocity = GyroAngularVelocity,
            Acceleration = Acceleration
        };
    }
}
