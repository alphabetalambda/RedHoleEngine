using System.Numerics;
using RedHoleEngine.Core;
using RedHoleEngine.Input.Actions;
using RedHoleEngine.Input.Devices;
using RedHoleEngine.Input.Haptics;
using RedHoleEngine.Input.Providers;
using RedHoleEngine.Platform;
using Silk.NET.Input;

namespace RedHoleEngine.Input;

/// <summary>
/// Main entry point for the input system.
/// Manages input providers, devices, and action evaluation.
/// </summary>
public class InputManager : IDisposable
{
    private readonly List<IInputProvider> _providers = new();
    private readonly Dictionary<string, InputDevice> _devicesByName = new();
    
    // Default providers
    private KeyboardMouseProvider? _keyboardMouseProvider;
    private SilkInputProvider? _silkInputProvider;
    private SteamInputProvider? _steamInputProvider;
    
    // Action system
    private InputActionAsset? _actionAsset;
    
    // Frame state
    private long _frameNumber;
    private readonly InputState _currentState = new();
    
    // Haptic management
    private readonly Dictionary<InputDevice, HapticPlayer> _hapticPlayers = new();
    
    /// <summary>
    /// Whether the input manager has been initialized.
    /// </summary>
    public bool IsInitialized { get; private set; }
    
    /// <summary>
    /// The current input state snapshot.
    /// </summary>
    public InputState CurrentState => _currentState;
    
    /// <summary>
    /// All registered input providers.
    /// </summary>
    public IReadOnlyList<IInputProvider> Providers => _providers;
    
    /// <summary>
    /// The keyboard device (if available).
    /// </summary>
    public KeyboardDevice? Keyboard => _keyboardMouseProvider?.GetKeyboard();
    
    /// <summary>
    /// The mouse device (if available).
    /// </summary>
    public MouseDevice? Mouse => _keyboardMouseProvider?.GetMouse();
    
    /// <summary>
    /// The primary gamepad (prefers Steam Input, falls back to Silk.NET).
    /// </summary>
    public GamepadDevice? PrimaryGamepad
    {
        get
        {
            // Prefer Steam Input gamepad if available
            var steamGamepad = _steamInputProvider?.GetGamepads().FirstOrDefault();
            if (steamGamepad != null) return steamGamepad;
            
            // Fall back to Silk.NET
            return _silkInputProvider?.GetGamepads().FirstOrDefault();
        }
    }
    
    /// <summary>
    /// All connected gamepads.
    /// </summary>
    public IEnumerable<GamepadDevice> Gamepads
    {
        get
        {
            if (_steamInputProvider?.IsAvailable == true)
            {
                foreach (var gp in _steamInputProvider.GetGamepads())
                    yield return gp;
            }
            
            if (_silkInputProvider?.IsAvailable == true)
            {
                // Only return Silk gamepads not already handled by Steam
                foreach (var gp in _silkInputProvider.GetGamepads())
                {
                    if (_steamInputProvider?.IsAvailable != true)
                        yield return gp;
                }
            }
        }
    }
    
    /// <summary>
    /// The primary gyro device (if available).
    /// </summary>
    public GyroDevice? PrimaryGyro => _steamInputProvider?.GetGyros().FirstOrDefault();
    
    /// <summary>
    /// Whether Steam Input is available (running under Steam).
    /// </summary>
    public bool IsSteamInputAvailable => _steamInputProvider?.IsAvailable ?? false;
    
    /// <summary>
    /// The loaded action asset.
    /// </summary>
    public InputActionAsset? ActionAsset => _actionAsset;
    
    /// <summary>
    /// Event fired when any device connects.
    /// </summary>
    public event Action<InputDevice>? DeviceConnected;
    
    /// <summary>
    /// Event fired when any device disconnects.
    /// </summary>
    public event Action<InputDevice>? DeviceDisconnected;
    
    // ═══════════════════════════════════════════════════════════════════════════
    // INITIALIZATION
    // ═══════════════════════════════════════════════════════════════════════════
    
    /// <summary>
    /// Initialize the input manager with a Silk.NET input context.
    /// </summary>
    public void Initialize(IInputContext inputContext)
    {
        if (IsInitialized)
        {
            Console.WriteLine("[InputManager] Already initialized");
            return;
        }
        
        Console.WriteLine("[InputManager] Initializing...");
        
        // Always initialize keyboard/mouse (cannot be disabled)
        _keyboardMouseProvider = new KeyboardMouseProvider();
        if (_keyboardMouseProvider.Initialize(inputContext))
        {
            RegisterProvider(_keyboardMouseProvider);
            Console.WriteLine("[InputManager] Keyboard/Mouse provider initialized");
        }
        
        // Try Steam Input first (highest priority for gamepads)
        _steamInputProvider = new SteamInputProvider();
        if (_steamInputProvider.Initialize())
        {
            RegisterProvider(_steamInputProvider);
            Console.WriteLine("[InputManager] Steam Input provider initialized");
        }
        else
        {
            Console.WriteLine("[InputManager] Steam Input not available, using Silk.NET for gamepads");
        }
        
        // Always initialize Silk.NET for gamepad fallback
        _silkInputProvider = new SilkInputProvider();
        if (_silkInputProvider.Initialize(inputContext))
        {
            // Disable Silk gamepad provider if Steam Input is available
            if (_steamInputProvider.IsAvailable)
            {
                _silkInputProvider.IsEnabled = false;
                Console.WriteLine("[InputManager] Silk.NET gamepad provider disabled (Steam Input active)");
            }
            RegisterProvider(_silkInputProvider);
        }
        
        // Apply Steam Deck optimizations if detected
        if (PlatformDetector.IsSteamDeck)
        {
            ApplySteamDeckDefaults();
        }
        
        IsInitialized = true;
        Console.WriteLine($"[InputManager] Initialized with {_providers.Count} provider(s)");
    }
    
    private void RegisterProvider(IInputProvider provider)
    {
        _providers.Add(provider);
        provider.DeviceConnected += OnDeviceConnected;
        provider.DeviceDisconnected += OnDeviceDisconnected;
        
        // Register existing devices
        foreach (var device in provider.Devices)
        {
            _devicesByName[$"{provider.Name}/{device.Name}"] = device;
        }
    }
    
    private void OnDeviceConnected(InputDevice device)
    {
        _devicesByName[$"{device.ProviderName}/{device.Name}"] = device;
        DeviceConnected?.Invoke(device);
        Console.WriteLine($"[InputManager] Device connected: {device}");
    }
    
    private void OnDeviceDisconnected(InputDevice device)
    {
        _devicesByName.Remove($"{device.ProviderName}/{device.Name}");
        DeviceDisconnected?.Invoke(device);
        Console.WriteLine($"[InputManager] Device disconnected: {device}");
    }
    
    private void ApplySteamDeckDefaults()
    {
        Console.WriteLine("[InputManager] Applying Steam Deck defaults");
        
        // Enable gyro by default on Steam Deck
        if (PrimaryGyro != null)
        {
            PrimaryGyro.IsEnabled = true;
            PrimaryGyro.Sensitivity = 1.0f;
            PrimaryGyro.Deadzone = 3.0f; // Lower deadzone for gyro aiming
        }
    }
    
    // ═══════════════════════════════════════════════════════════════════════════
    // ACTION SYSTEM
    // ═══════════════════════════════════════════════════════════════════════════
    
    /// <summary>
    /// Load an action asset from a JSON file.
    /// </summary>
    public void LoadActions(string filePath)
    {
        _actionAsset = InputActionAsset.Load(filePath);
        Console.WriteLine($"[InputManager] Loaded action asset: {_actionAsset.Name} ({_actionAsset.ActionMaps.Count} maps)");
    }
    
    /// <summary>
    /// Set the action asset directly.
    /// </summary>
    public void SetActions(InputActionAsset asset)
    {
        _actionAsset = asset;
    }
    
    /// <summary>
    /// Get an action by name.
    /// </summary>
    public InputAction? FindAction(string name)
    {
        return _actionAsset?.FindAction(name);
    }
    
    /// <summary>
    /// Enable an action map by name.
    /// </summary>
    public void EnableActionMap(string name)
    {
        _actionAsset?.FindActionMap(name)?.Enable();
    }
    
    /// <summary>
    /// Disable an action map by name.
    /// </summary>
    public void DisableActionMap(string name)
    {
        _actionAsset?.FindActionMap(name)?.Disable();
    }
    
    // ═══════════════════════════════════════════════════════════════════════════
    // UPDATE
    // ═══════════════════════════════════════════════════════════════════════════
    
    /// <summary>
    /// Update all input providers and evaluate actions.
    /// Call this at the start of each frame.
    /// </summary>
    public void Update(float deltaTime)
    {
        if (!IsInitialized) return;
        
        _frameNumber++;
        
        // Update all providers
        foreach (var provider in _providers)
        {
            if (provider.IsEnabled)
            {
                provider.Update();
            }
        }
        
        // Capture current state
        _currentState.Capture(
            Keyboard,
            Mouse,
            PrimaryGamepad,
            PrimaryGyro,
            _frameNumber,
            Time.TotalTime
        );
        
        // Evaluate actions
        if (_actionAsset != null)
        {
            EvaluateActions();
        }
        
        // Update haptic players
        foreach (var (device, player) in _hapticPlayers)
        {
            if (player.IsPlaying)
            {
                var feedback = player.Update(deltaTime);
                var provider = GetProviderForDevice(device);
                provider?.SendHaptic(device, feedback);
            }
        }
    }
    
    /// <summary>
    /// End-of-frame processing.
    /// </summary>
    public void EndFrame()
    {
        foreach (var provider in _providers)
        {
            if (provider.IsEnabled)
            {
                provider.EndFrame();
            }
        }
    }
    
    private void EvaluateActions()
    {
        if (_actionAsset == null) return;
        
        foreach (var action in _actionAsset.GetAllActions())
        {
            if (!action.Enabled) continue;
            
            // Save previous values
            action.PreviousValueFloat = action.ValueFloat;
            action.PreviousValueVector2 = action.ValueVector2;
            action.PreviousValueVector3 = action.ValueVector3;
            
            // Evaluate bindings
            var previousPhase = action.Phase;
            EvaluateAction(action);
            
            // Fire events based on phase changes
            if (action.Phase == InputActionPhase.Started && previousPhase != InputActionPhase.Started)
            {
                action.FireStarted();
            }
            else if (action.Phase == InputActionPhase.Performed)
            {
                action.FirePerformed();
            }
            else if (action.Phase == InputActionPhase.Canceled && previousPhase != InputActionPhase.Canceled)
            {
                action.FireCanceled();
            }
        }
    }
    
    private void EvaluateAction(InputAction action)
    {
        float maxValue = 0f;
        var maxVector2 = Vector2.Zero;
        var maxVector3 = Vector3.Zero;
        
        // Evaluate direct bindings
        foreach (var binding in action.Bindings)
        {
            if (!binding.IsActive) continue;
            
            var (device, control) = InputBindingPath.Parse(binding.Path);
            var value = ReadControlValue(device, control, action.Type);
            
            switch (action.Type)
            {
                case InputActionType.Button:
                case InputActionType.Axis:
                    maxValue = MathF.Max(maxValue, MathF.Abs(value.X));
                    break;
                case InputActionType.Vector2:
                    if (value.Length() > maxVector2.Length())
                        maxVector2 = value;
                    break;
            }
        }
        
        // Evaluate composite bindings
        foreach (var composite in action.Composites)
        {
            var compositeValue = EvaluateComposite(composite);
            if (compositeValue.Length() > maxVector2.Length())
                maxVector2 = compositeValue;
        }
        
        // Update action values
        action.ValueFloat = maxValue;
        action.ValueVector2 = maxVector2;
        action.ValueVector3 = maxVector3;
        
        // Update phase
        var wasActive = action.Phase == InputActionPhase.Started || action.Phase == InputActionPhase.Performed;
        var isActive = action.Type switch
        {
            InputActionType.Button => maxValue > 0.5f,
            InputActionType.Axis => MathF.Abs(maxValue) > 0.001f,
            InputActionType.Vector2 => maxVector2.Length() > 0.001f,
            InputActionType.Vector3 => maxVector3.Length() > 0.001f,
            _ => false
        };
        
        if (isActive && !wasActive)
            action.Phase = InputActionPhase.Started;
        else if (isActive && wasActive)
            action.Phase = InputActionPhase.Performed;
        else if (!isActive && wasActive)
            action.Phase = InputActionPhase.Canceled;
        else
            action.Phase = InputActionPhase.Waiting;
    }
    
    private Vector2 EvaluateComposite(CompositeBinding composite)
    {
        if (composite.Type == "2DVector")
        {
            var up = composite.Parts.TryGetValue("up", out var upBinding) 
                ? ReadControlValue(InputBindingPath.Parse(upBinding.Path).device, InputBindingPath.Parse(upBinding.Path).control, InputActionType.Button).X 
                : 0f;
            var down = composite.Parts.TryGetValue("down", out var downBinding)
                ? ReadControlValue(InputBindingPath.Parse(downBinding.Path).device, InputBindingPath.Parse(downBinding.Path).control, InputActionType.Button).X
                : 0f;
            var left = composite.Parts.TryGetValue("left", out var leftBinding)
                ? ReadControlValue(InputBindingPath.Parse(leftBinding.Path).device, InputBindingPath.Parse(leftBinding.Path).control, InputActionType.Button).X
                : 0f;
            var right = composite.Parts.TryGetValue("right", out var rightBinding)
                ? ReadControlValue(InputBindingPath.Parse(rightBinding.Path).device, InputBindingPath.Parse(rightBinding.Path).control, InputActionType.Button).X
                : 0f;
            
            return new Vector2(right - left, up - down);
        }
        
        return Vector2.Zero;
    }
    
    private Vector2 ReadControlValue(string device, string control, InputActionType type)
    {
        device = device.ToLowerInvariant();
        
        switch (device)
        {
            case "keyboard":
                if (Keyboard != null)
                    return new Vector2(Keyboard.ReadButton(control), 0);
                break;
                
            case "mouse":
                if (Mouse != null)
                {
                    if (type == InputActionType.Vector2)
                        return Mouse.ReadVector2(control);
                    return new Vector2(Mouse.ReadAxis(control), 0);
                }
                break;
                
            case "gamepad":
                if (PrimaryGamepad != null)
                {
                    if (type == InputActionType.Vector2)
                        return PrimaryGamepad.ReadVector2(control);
                    return new Vector2(PrimaryGamepad.ReadAxis(control), 0);
                }
                break;
                
            case "gyro":
                if (PrimaryGyro != null)
                {
                    var v3 = PrimaryGyro.ReadVector3(control);
                    return new Vector2(v3.X, v3.Y);
                }
                break;
        }
        
        return Vector2.Zero;
    }
    
    private IInputProvider? GetProviderForDevice(InputDevice device)
    {
        return _providers.FirstOrDefault(p => p.Devices.Contains(device));
    }
    
    // ═══════════════════════════════════════════════════════════════════════════
    // CONVENIENCE METHODS
    // ═══════════════════════════════════════════════════════════════════════════
    
    /// <summary>
    /// Check if a key is currently pressed.
    /// </summary>
    public bool IsKeyPressed(string key) => Keyboard?.IsKeyPressed(key) ?? false;
    
    /// <summary>
    /// Check if a key was just pressed this frame.
    /// </summary>
    public bool WasKeyPressedThisFrame(string key) => Keyboard?.WasKeyPressedThisFrame(key) ?? false;
    
    /// <summary>
    /// Check if a mouse button is pressed.
    /// </summary>
    public bool IsMouseButtonPressed(Silk.NET.Input.MouseButton button) => Mouse?.IsButtonPressed(button) ?? false;
    
    /// <summary>
    /// Get mouse position.
    /// </summary>
    public Vector2 GetMousePosition() => Mouse?.Position ?? Vector2.Zero;
    
    /// <summary>
    /// Get mouse delta.
    /// </summary>
    public Vector2 GetMouseDelta() => Mouse?.Delta ?? Vector2.Zero;
    
    /// <summary>
    /// Set mouse cursor capture mode.
    /// </summary>
    public void SetCursorCaptured(bool captured) => _keyboardMouseProvider?.SetCursorCaptured(captured);
    
    /// <summary>
    /// Toggle cursor capture.
    /// </summary>
    public void ToggleCursorCapture() => _keyboardMouseProvider?.ToggleCursorCapture();
    
    /// <summary>
    /// Get left stick position.
    /// </summary>
    public Vector2 GetLeftStick() => PrimaryGamepad?.ReadVector2("leftStick") ?? Vector2.Zero;
    
    /// <summary>
    /// Get right stick position.
    /// </summary>
    public Vector2 GetRightStick() => PrimaryGamepad?.ReadVector2("rightStick") ?? Vector2.Zero;
    
    /// <summary>
    /// Get gyro angular velocity (pitch, yaw, roll).
    /// </summary>
    public Vector3 GetGyro() => PrimaryGyro?.GetProcessedAngularVelocity() ?? Vector3.Zero;
    
    /// <summary>
    /// Check if a gamepad button is pressed.
    /// </summary>
    public bool IsGamepadButtonPressed(string button) => PrimaryGamepad?.IsButtonPressed(button) ?? false;
    
    // ═══════════════════════════════════════════════════════════════════════════
    // HAPTICS
    // ═══════════════════════════════════════════════════════════════════════════
    
    /// <summary>
    /// Send haptic feedback to the primary gamepad.
    /// </summary>
    public void SendHaptic(HapticFeedback feedback)
    {
        var gamepad = PrimaryGamepad;
        if (gamepad == null) return;
        
        var provider = GetProviderForDevice(gamepad);
        provider?.SendHaptic(gamepad, feedback);
    }
    
    /// <summary>
    /// Play a haptic pattern on the primary gamepad.
    /// </summary>
    public void PlayHapticPattern(HapticPattern pattern)
    {
        var gamepad = PrimaryGamepad;
        if (gamepad == null) return;
        
        if (!_hapticPlayers.TryGetValue(gamepad, out var player))
        {
            player = new HapticPlayer();
            _hapticPlayers[gamepad] = player;
        }
        
        player.Play(pattern);
    }
    
    /// <summary>
    /// Stop all haptics on the primary gamepad.
    /// </summary>
    public void StopHaptics()
    {
        var gamepad = PrimaryGamepad;
        if (gamepad == null) return;
        
        if (_hapticPlayers.TryGetValue(gamepad, out var player))
        {
            player.Stop();
        }
        
        var provider = GetProviderForDevice(gamepad);
        provider?.StopHaptics(gamepad);
    }
    
    // ═══════════════════════════════════════════════════════════════════════════
    // STEAM INPUT SPECIFIC
    // ═══════════════════════════════════════════════════════════════════════════
    
    /// <summary>
    /// Activate a Steam Input action set.
    /// </summary>
    public void ActivateSteamActionSet(string actionSetName)
    {
        if (_steamInputProvider?.IsAvailable != true) return;
        
        foreach (var gamepad in _steamInputProvider.GetGamepads())
        {
            if (gamepad is SteamGamepad steamGamepad)
            {
                _steamInputProvider.ActivateActionSet(steamGamepad, actionSetName);
            }
        }
    }
    
    /// <summary>
    /// Show the Steam Input binding configuration panel.
    /// </summary>
    public void ShowSteamBindingPanel()
    {
        if (_steamInputProvider?.IsAvailable != true) return;
        
        var gamepad = _steamInputProvider.GetGamepads().FirstOrDefault() as SteamGamepad;
        if (gamepad != null)
        {
            _steamInputProvider.ShowBindingPanel(gamepad);
        }
    }
    
    // ═══════════════════════════════════════════════════════════════════════════
    // CLEANUP
    // ═══════════════════════════════════════════════════════════════════════════
    
    public void Dispose()
    {
        foreach (var provider in _providers)
        {
            provider.DeviceConnected -= OnDeviceConnected;
            provider.DeviceDisconnected -= OnDeviceDisconnected;
            provider.Dispose();
        }
        
        _providers.Clear();
        _devicesByName.Clear();
        _hapticPlayers.Clear();
        IsInitialized = false;
    }
}
