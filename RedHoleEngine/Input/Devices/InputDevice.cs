using System.Numerics;

namespace RedHoleEngine.Input.Devices;

/// <summary>
/// The type of input device.
/// </summary>
public enum InputDeviceType
{
    Unknown,
    Keyboard,
    Mouse,
    Gamepad,
    Gyroscope,
    Touchscreen
}

/// <summary>
/// Gamepad type for controller-specific features.
/// </summary>
public enum GamepadType
{
    Unknown,
    Xbox,           // Xbox controllers
    PlayStation,    // DualShock/DualSense
    SteamDeck,      // Steam Deck built-in controller
    SteamController,// Steam Controller
    Switch,         // Nintendo Switch Pro Controller
    Generic         // Generic XInput/DirectInput
}

/// <summary>
/// Base class for all input devices.
/// </summary>
public abstract class InputDevice
{
    /// <summary>
    /// Unique identifier for this device instance.
    /// </summary>
    public int DeviceId { get; internal set; }
    
    /// <summary>
    /// Human-readable name of the device.
    /// </summary>
    public string Name { get; internal set; } = "Unknown Device";
    
    /// <summary>
    /// The type of this device.
    /// </summary>
    public abstract InputDeviceType DeviceType { get; }
    
    /// <summary>
    /// Whether this device is currently connected.
    /// </summary>
    public bool IsConnected { get; internal set; } = true;
    
    /// <summary>
    /// Whether this device was just connected this frame.
    /// </summary>
    public bool WasJustConnected { get; internal set; }
    
    /// <summary>
    /// Whether this device was just disconnected this frame.
    /// </summary>
    public bool WasJustDisconnected { get; internal set; }
    
    /// <summary>
    /// The input provider managing this device.
    /// </summary>
    public string ProviderName { get; internal set; } = "";
    
    /// <summary>
    /// Called at the start of each frame to prepare for new input.
    /// </summary>
    public virtual void BeginFrame()
    {
        WasJustConnected = false;
        WasJustDisconnected = false;
    }
    
    /// <summary>
    /// Called at the end of each frame to finalize state.
    /// </summary>
    public virtual void EndFrame()
    {
    }
    
    /// <summary>
    /// Read a button state from this device.
    /// </summary>
    public virtual float ReadButton(string control) => 0f;
    
    /// <summary>
    /// Read an axis value from this device.
    /// </summary>
    public virtual float ReadAxis(string control) => 0f;
    
    /// <summary>
    /// Read a Vector2 value from this device.
    /// </summary>
    public virtual Vector2 ReadVector2(string control) => Vector2.Zero;
    
    /// <summary>
    /// Read a Vector3 value from this device.
    /// </summary>
    public virtual Vector3 ReadVector3(string control) => Vector3.Zero;
    
    public override string ToString()
    {
        return $"{DeviceType}({Name}, ID={DeviceId}, {(IsConnected ? "connected" : "disconnected")})";
    }
}

/// <summary>
/// Capabilities of a gamepad device.
/// </summary>
[Flags]
public enum GamepadCapabilities
{
    None = 0,
    Vibration = 1 << 0,         // Basic rumble
    TriggerRumble = 1 << 1,     // Per-trigger haptics (DualSense, Xbox Series)
    Gyroscope = 1 << 2,         // Motion sensor
    Accelerometer = 1 << 3,     // Motion sensor
    Touchpad = 1 << 4,          // Touchpad (DualShock 4, DualSense, Steam Deck)
    AdaptiveTriggers = 1 << 5,  // Resistance triggers (DualSense)
    BackButtons = 1 << 6,       // Extra back buttons (Elite, Steam Deck)
    Trackpads = 1 << 7,         // Trackpads (Steam Controller, Steam Deck)
    HDRumble = 1 << 8           // HD Rumble (Switch)
}
