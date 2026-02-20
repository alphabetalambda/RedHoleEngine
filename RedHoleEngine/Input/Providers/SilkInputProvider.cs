using System.Numerics;
using RedHoleEngine.Input.Devices;
using RedHoleEngine.Input.Haptics;
using Silk.NET.Input;

namespace RedHoleEngine.Input.Providers;

/// <summary>
/// Input provider for gamepads using Silk.NET (SDL/GLFW backend).
/// This is the fallback provider when Steam Input is not available.
/// </summary>
public class SilkInputProvider : InputProviderBase
{
    public override string Name => "SilkNET";
    public override int Priority => 50; // Medium priority (below Steam Input)
    
    private IInputContext? _inputContext;
    private readonly Dictionary<int, GamepadDevice> _gamepads = new();
    
    /// <summary>
    /// Initialize with a Silk.NET input context.
    /// </summary>
    public bool Initialize(IInputContext inputContext)
    {
        _inputContext = inputContext;
        return Initialize();
    }
    
    public override bool Initialize()
    {
        if (_inputContext == null)
        {
            Console.WriteLine("[SilkInputProvider] No input context provided");
            return false;
        }
        
        // Subscribe to gamepad connection events
        _inputContext.ConnectionChanged += OnConnectionChanged;
        
        // Initialize existing gamepads
        foreach (var gamepad in _inputContext.Gamepads)
        {
            AddGamepad(gamepad);
        }
        
        // Also check joysticks (some controllers appear here instead)
        foreach (var joystick in _inputContext.Joysticks)
        {
            // Joysticks with 2+ axes and buttons can be treated as gamepads
            if (joystick.Axes.Count >= 2 && joystick.Buttons.Count >= 4)
            {
                Console.WriteLine($"[SilkInputProvider] Found joystick: {joystick.Name}");
            }
        }
        
        IsAvailable = true;
        Console.WriteLine($"[SilkInputProvider] Initialized with {_gamepads.Count} gamepad(s)");
        return true;
    }
    
    private void OnConnectionChanged(IInputDevice device, bool connected)
    {
        if (device is IGamepad gamepad)
        {
            if (connected)
            {
                AddGamepad(gamepad);
            }
            else
            {
                RemoveGamepad(gamepad.Index);
            }
        }
    }
    
    private void AddGamepad(IGamepad silkGamepad)
    {
        if (_gamepads.ContainsKey(silkGamepad.Index))
            return;
            
        var gamepad = new GamepadDevice
        {
            DeviceId = silkGamepad.Index,
            Name = silkGamepad.Name ?? $"Gamepad {silkGamepad.Index}",
            SilkGamepad = silkGamepad,
            GamepadType = DetectGamepadType(silkGamepad.Name),
            Capabilities = DetectCapabilities(silkGamepad)
        };
        
        _gamepads[silkGamepad.Index] = gamepad;
        _devices.Add(gamepad);
        OnDeviceConnected(gamepad);
        
        // Subscribe to events
        silkGamepad.ButtonDown += (gp, btn) => OnButtonDown(gp.Index, btn);
        silkGamepad.ButtonUp += (gp, btn) => OnButtonUp(gp.Index, btn);
        
        Console.WriteLine($"[SilkInputProvider] Gamepad connected: {gamepad.Name} (Type: {gamepad.GamepadType})");
    }
    
    private void RemoveGamepad(int index)
    {
        if (!_gamepads.TryGetValue(index, out var gamepad))
            return;
            
        _gamepads.Remove(index);
        _devices.Remove(gamepad);
        OnDeviceDisconnected(gamepad);
        
        Console.WriteLine($"[SilkInputProvider] Gamepad disconnected: {gamepad.Name}");
    }
    
    private static GamepadType DetectGamepadType(string? name)
    {
        if (string.IsNullOrEmpty(name))
            return GamepadType.Generic;
            
        var lower = name.ToLowerInvariant();
        
        if (lower.Contains("xbox") || lower.Contains("xinput"))
            return GamepadType.Xbox;
        if (lower.Contains("dualshock") || lower.Contains("dualsense") || lower.Contains("playstation") || lower.Contains("ps4") || lower.Contains("ps5"))
            return GamepadType.PlayStation;
        if (lower.Contains("steam deck"))
            return GamepadType.SteamDeck;
        if (lower.Contains("steam controller"))
            return GamepadType.SteamController;
        if (lower.Contains("switch") || lower.Contains("nintendo") || lower.Contains("pro controller"))
            return GamepadType.Switch;
            
        return GamepadType.Generic;
    }
    
    private static GamepadCapabilities DetectCapabilities(IGamepad gamepad)
    {
        var caps = GamepadCapabilities.None;
        
        if (gamepad.VibrationMotors.Count > 0)
            caps |= GamepadCapabilities.Vibration;
            
        // Note: Silk.NET doesn't expose gyro/touchpad directly
        // Those features require Steam Input or platform-specific APIs
        
        return caps;
    }
    
    public override void Shutdown()
    {
        if (_inputContext != null)
        {
            _inputContext.ConnectionChanged -= OnConnectionChanged;
        }
        
        foreach (var gamepad in _gamepads.Values.ToList())
        {
            gamepad.StopVibration();
            OnDeviceDisconnected(gamepad);
        }
        
        _gamepads.Clear();
        _devices.Clear();
        IsAvailable = false;
    }
    
    public override void Update()
    {
        if (!IsEnabled) return;
        
        foreach (var (index, gamepad) in _gamepads)
        {
            gamepad.BeginFrame();
            
            var silkGp = gamepad.SilkGamepad;
            if (silkGp == null) continue;
            
            // Read thumbsticks
            var leftStick = Vector2.Zero;
            var rightStick = Vector2.Zero;
            
            foreach (var thumbstick in silkGp.Thumbsticks)
            {
                if (thumbstick.Index == 0)
                    leftStick = new Vector2(thumbstick.X, thumbstick.Y);
                else if (thumbstick.Index == 1)
                    rightStick = new Vector2(thumbstick.X, thumbstick.Y);
            }
            
            gamepad.LeftStick = leftStick;
            gamepad.RightStick = rightStick;
            
            // Read triggers
            foreach (var trigger in silkGp.Triggers)
            {
                if (trigger.Index == 0)
                    gamepad.LeftTrigger = trigger.Position;
                else if (trigger.Index == 1)
                    gamepad.RightTrigger = trigger.Position;
            }
            
            // Read D-Pad from buttons (already handled via events, but let's be sure)
            // D-Pad state is maintained via button events
        }
    }
    
    private void OnButtonDown(int gamepadIndex, Button button)
    {
        if (!_gamepads.TryGetValue(gamepadIndex, out var gamepad))
            return;
            
        var control = GamepadDevice.ButtonToControl(button.Name);
        gamepad.SetButtonState(control, 1f);
        
        // Update D-Pad vector
        UpdateDPad(gamepad);
    }
    
    private void OnButtonUp(int gamepadIndex, Button button)
    {
        if (!_gamepads.TryGetValue(gamepadIndex, out var gamepad))
            return;
            
        var control = GamepadDevice.ButtonToControl(button.Name);
        gamepad.SetButtonState(control, 0f);
        
        // Update D-Pad vector
        UpdateDPad(gamepad);
    }
    
    private void UpdateDPad(GamepadDevice gamepad)
    {
        var x = 0f;
        var y = 0f;
        
        if (gamepad.IsButtonPressed("dpad/left")) x -= 1f;
        if (gamepad.IsButtonPressed("dpad/right")) x += 1f;
        if (gamepad.IsButtonPressed("dpad/down")) y -= 1f;
        if (gamepad.IsButtonPressed("dpad/up")) y += 1f;
        
        gamepad.DPad = new Vector2(x, y);
    }
    
    public override IEnumerable<GamepadDevice> GetGamepads()
    {
        return _gamepads.Values;
    }
    
    public override void SendHaptic(InputDevice device, HapticFeedback feedback)
    {
        if (device is GamepadDevice gamepad)
        {
            gamepad.SetVibration(feedback.LeftMotor, feedback.RightMotor);
        }
    }
    
    public override void StopHaptics(InputDevice device)
    {
        if (device is GamepadDevice gamepad)
        {
            gamepad.StopVibration();
        }
    }
}
