using RedHoleEngine.Input.Devices;
using RedHoleEngine.Input.Haptics;

namespace RedHoleEngine.Input.Providers;

/// <summary>
/// Interface for input providers that handle reading from physical devices.
/// Multiple providers can be active simultaneously (e.g., Steam Input + Keyboard/Mouse).
/// </summary>
public interface IInputProvider : IDisposable
{
    /// <summary>
    /// Unique name for this provider.
    /// </summary>
    string Name { get; }
    
    /// <summary>
    /// Priority for this provider (higher = preferred for gamepads).
    /// Steam Input should have highest priority when available.
    /// </summary>
    int Priority { get; }
    
    /// <summary>
    /// Whether this provider is currently available and initialized.
    /// </summary>
    bool IsAvailable { get; }
    
    /// <summary>
    /// Whether this provider is currently enabled.
    /// </summary>
    bool IsEnabled { get; set; }
    
    /// <summary>
    /// All devices currently managed by this provider.
    /// </summary>
    IReadOnlyList<InputDevice> Devices { get; }
    
    /// <summary>
    /// Initialize the provider.
    /// </summary>
    /// <returns>True if initialization succeeded.</returns>
    bool Initialize();
    
    /// <summary>
    /// Shutdown the provider and release resources.
    /// </summary>
    void Shutdown();
    
    /// <summary>
    /// Called at the start of each frame to poll/update device state.
    /// </summary>
    void Update();
    
    /// <summary>
    /// Called at the end of each frame to finalize state.
    /// </summary>
    void EndFrame();
    
    /// <summary>
    /// Get all gamepads managed by this provider.
    /// </summary>
    IEnumerable<GamepadDevice> GetGamepads();
    
    /// <summary>
    /// Get the keyboard (if this provider handles keyboards).
    /// </summary>
    KeyboardDevice? GetKeyboard();
    
    /// <summary>
    /// Get the mouse (if this provider handles mice).
    /// </summary>
    MouseDevice? GetMouse();
    
    /// <summary>
    /// Get gyro devices (may be standalone or attached to gamepads).
    /// </summary>
    IEnumerable<GyroDevice> GetGyros();
    
    /// <summary>
    /// Send haptic feedback to a device.
    /// </summary>
    void SendHaptic(InputDevice device, HapticFeedback feedback);
    
    /// <summary>
    /// Stop all haptics on a device.
    /// </summary>
    void StopHaptics(InputDevice device);
    
    /// <summary>
    /// Event fired when a device is connected.
    /// </summary>
    event Action<InputDevice>? DeviceConnected;
    
    /// <summary>
    /// Event fired when a device is disconnected.
    /// </summary>
    event Action<InputDevice>? DeviceDisconnected;
}

/// <summary>
/// Base class for input providers with common functionality.
/// </summary>
public abstract class InputProviderBase : IInputProvider
{
    public abstract string Name { get; }
    public abstract int Priority { get; }
    
    public bool IsAvailable { get; protected set; }
    public bool IsEnabled { get; set; } = true;
    
    protected readonly List<InputDevice> _devices = new();
    public IReadOnlyList<InputDevice> Devices => _devices;
    
    public event Action<InputDevice>? DeviceConnected;
    public event Action<InputDevice>? DeviceDisconnected;
    
    protected void OnDeviceConnected(InputDevice device)
    {
        device.WasJustConnected = true;
        device.IsConnected = true;
        device.ProviderName = Name;
        DeviceConnected?.Invoke(device);
    }
    
    protected void OnDeviceDisconnected(InputDevice device)
    {
        device.WasJustDisconnected = true;
        device.IsConnected = false;
        DeviceDisconnected?.Invoke(device);
    }
    
    public abstract bool Initialize();
    public abstract void Shutdown();
    public abstract void Update();
    
    public virtual void EndFrame()
    {
        foreach (var device in _devices)
        {
            device.EndFrame();
        }
    }
    
    public virtual IEnumerable<GamepadDevice> GetGamepads()
    {
        return _devices.OfType<GamepadDevice>();
    }
    
    public virtual KeyboardDevice? GetKeyboard()
    {
        return _devices.OfType<KeyboardDevice>().FirstOrDefault();
    }
    
    public virtual MouseDevice? GetMouse()
    {
        return _devices.OfType<MouseDevice>().FirstOrDefault();
    }
    
    public virtual IEnumerable<GyroDevice> GetGyros()
    {
        return _devices.OfType<GyroDevice>();
    }
    
    public virtual void SendHaptic(InputDevice device, HapticFeedback feedback)
    {
        // Default implementation for basic rumble
        if (device is GamepadDevice gamepad)
        {
            gamepad.SetVibration(feedback.LeftMotor, feedback.RightMotor);
        }
    }
    
    public virtual void StopHaptics(InputDevice device)
    {
        if (device is GamepadDevice gamepad)
        {
            gamepad.StopVibration();
        }
    }
    
    public virtual void Dispose()
    {
        Shutdown();
    }
}
