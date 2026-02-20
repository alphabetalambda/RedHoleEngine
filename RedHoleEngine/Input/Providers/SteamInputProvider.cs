using System.Numerics;
using System.Runtime.InteropServices;
using RedHoleEngine.Input.Devices;
using RedHoleEngine.Input.Haptics;
using Steamworks;

namespace RedHoleEngine.Input.Providers;

/// <summary>
/// Input provider using Steam Input API via Steamworks.NET.
/// Provides full Steam Deck support including gyro, trackpads, and back buttons.
/// </summary>
public class SteamInputProvider : InputProviderBase
{
    public override string Name => "SteamInput";
    public override int Priority => 100; // Highest priority when available
    
    private bool _steamInitialized;
    private readonly Dictionary<ulong, SteamGamepad> _gamepads = new();
    
    // Action set handles
    private InputActionSetHandle_t _gameplayActionSet;
    private InputActionSetHandle_t _menuActionSet;
    
    // Digital action handles
    private readonly Dictionary<string, InputDigitalActionHandle_t> _digitalActions = new();
    
    // Analog action handles  
    private readonly Dictionary<string, InputAnalogActionHandle_t> _analogActions = new();
    
    // Polling interval for controller updates
    private const float PollInterval = 1f / 120f; // 120Hz
    private float _pollTimer;
    
    /// <summary>
    /// Path to the Steam Input action manifest (VDF file).
    /// </summary>
    public string ActionManifestPath { get; set; } = "Assets/Input/steam_input_manifest.vdf";
    
    public override bool Initialize()
    {
        // Check if Steam is running
        if (!SteamAPI.IsSteamRunning())
        {
            Console.WriteLine("[SteamInputProvider] Steam is not running");
            IsAvailable = false;
            return false;
        }
        
        try
        {
            // Initialize Steamworks if not already done
            if (!SteamAPI.Init())
            {
                Console.WriteLine("[SteamInputProvider] Failed to initialize Steam API");
                IsAvailable = false;
                return false;
            }
            
            _steamInitialized = true;
            
            // Initialize Steam Input
            if (!SteamInput.Init(false))
            {
                Console.WriteLine("[SteamInputProvider] Failed to initialize Steam Input");
                IsAvailable = false;
                return false;
            }
            
            // Enable device callbacks
            SteamInput.EnableDeviceCallbacks();
            
            // Run a frame to populate initial controllers
            SteamInput.RunFrame();
            
            // Get action set handles
            _gameplayActionSet = SteamInput.GetActionSetHandle("gameplay");
            _menuActionSet = SteamInput.GetActionSetHandle("menu");
            
            // Get action handles for common actions
            RegisterDigitalAction("jump");
            RegisterDigitalAction("attack");
            RegisterDigitalAction("interact");
            RegisterDigitalAction("menu_confirm");
            RegisterDigitalAction("menu_cancel");
            RegisterDigitalAction("pause");
            
            RegisterAnalogAction("move");
            RegisterAnalogAction("look");
            RegisterAnalogAction("gyro");
            
            // Enumerate connected controllers
            var controllers = new InputHandle_t[Constants.STEAM_INPUT_MAX_COUNT];
            int count = SteamInput.GetConnectedControllers(controllers);
            
            for (int i = 0; i < count; i++)
            {
                AddController(controllers[i]);
            }
            
            IsAvailable = true;
            Console.WriteLine($"[SteamInputProvider] Initialized with {count} controller(s)");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[SteamInputProvider] Exception during initialization: {ex.Message}");
            IsAvailable = false;
            return false;
        }
    }
    
    private void RegisterDigitalAction(string name)
    {
        var handle = SteamInput.GetDigitalActionHandle(name);
        if (handle.m_InputDigitalActionHandle != 0)
        {
            _digitalActions[name] = handle;
        }
    }
    
    private void RegisterAnalogAction(string name)
    {
        var handle = SteamInput.GetAnalogActionHandle(name);
        if (handle.m_InputAnalogActionHandle != 0)
        {
            _analogActions[name] = handle;
        }
    }
    
    private void AddController(InputHandle_t handle)
    {
        if (handle.m_InputHandle == 0 || _gamepads.ContainsKey(handle.m_InputHandle))
            return;
            
        var inputType = SteamInput.GetInputTypeForHandle(handle);
        var gamepadType = InputTypeToGamepadType(inputType);
        
        var gamepad = new SteamGamepad
        {
            DeviceId = (int)handle.m_InputHandle,
            Name = GetControllerName(inputType),
            Handle = handle,
            GamepadType = gamepadType,
            Capabilities = GetCapabilities(inputType)
        };
        
        // Create associated gyro device if supported
        if (gamepad.Capabilities.HasFlag(GamepadCapabilities.Gyroscope))
        {
            gamepad.Gyro = new GyroDevice
            {
                DeviceId = gamepad.DeviceId,
                Name = $"{gamepad.Name} Gyro",
                ParentGamepad = gamepad
            };
            gamepad.Gyro.ProviderName = Name;
            _devices.Add(gamepad.Gyro);
        }
        
        _gamepads[handle.m_InputHandle] = gamepad;
        _devices.Add(gamepad);
        OnDeviceConnected(gamepad);
        
        // Activate gameplay action set
        SteamInput.ActivateActionSet(handle, _gameplayActionSet);
        
        Console.WriteLine($"[SteamInputProvider] Controller connected: {gamepad.Name} (Type: {inputType})");
    }
    
    private void RemoveController(InputHandle_t handle)
    {
        if (!_gamepads.TryGetValue(handle.m_InputHandle, out var gamepad))
            return;
            
        if (gamepad.Gyro != null)
        {
            _devices.Remove(gamepad.Gyro);
            OnDeviceDisconnected(gamepad.Gyro);
        }
        
        _gamepads.Remove(handle.m_InputHandle);
        _devices.Remove(gamepad);
        OnDeviceDisconnected(gamepad);
        
        Console.WriteLine($"[SteamInputProvider] Controller disconnected: {gamepad.Name}");
    }
    
    private static string GetControllerName(ESteamInputType type)
    {
        return type switch
        {
            ESteamInputType.k_ESteamInputType_SteamController => "Steam Controller",
            ESteamInputType.k_ESteamInputType_SteamDeckController => "Steam Deck",
            ESteamInputType.k_ESteamInputType_XBox360Controller => "Xbox 360",
            ESteamInputType.k_ESteamInputType_XBoxOneController => "Xbox One",
            ESteamInputType.k_ESteamInputType_PS4Controller => "DualShock 4",
            ESteamInputType.k_ESteamInputType_PS5Controller => "DualSense",
            ESteamInputType.k_ESteamInputType_SwitchProController => "Switch Pro",
            ESteamInputType.k_ESteamInputType_SwitchJoyConPair => "Joy-Con Pair",
            _ => "Unknown Controller"
        };
    }
    
    private static GamepadType InputTypeToGamepadType(ESteamInputType type)
    {
        return type switch
        {
            ESteamInputType.k_ESteamInputType_SteamController => GamepadType.SteamController,
            ESteamInputType.k_ESteamInputType_SteamDeckController => GamepadType.SteamDeck,
            ESteamInputType.k_ESteamInputType_XBox360Controller => GamepadType.Xbox,
            ESteamInputType.k_ESteamInputType_XBoxOneController => GamepadType.Xbox,
            ESteamInputType.k_ESteamInputType_PS4Controller => GamepadType.PlayStation,
            ESteamInputType.k_ESteamInputType_PS5Controller => GamepadType.PlayStation,
            ESteamInputType.k_ESteamInputType_SwitchProController => GamepadType.Switch,
            ESteamInputType.k_ESteamInputType_SwitchJoyConPair => GamepadType.Switch,
            _ => GamepadType.Generic
        };
    }
    
    private static GamepadCapabilities GetCapabilities(ESteamInputType type)
    {
        var caps = GamepadCapabilities.Vibration;
        
        switch (type)
        {
            case ESteamInputType.k_ESteamInputType_SteamDeckController:
                caps |= GamepadCapabilities.Gyroscope | GamepadCapabilities.Accelerometer |
                       GamepadCapabilities.Trackpads | GamepadCapabilities.BackButtons |
                       GamepadCapabilities.Touchpad;
                break;
                
            case ESteamInputType.k_ESteamInputType_SteamController:
                caps |= GamepadCapabilities.Gyroscope | GamepadCapabilities.Trackpads;
                break;
                
            case ESteamInputType.k_ESteamInputType_PS4Controller:
                caps |= GamepadCapabilities.Gyroscope | GamepadCapabilities.Accelerometer |
                       GamepadCapabilities.Touchpad;
                break;
                
            case ESteamInputType.k_ESteamInputType_PS5Controller:
                caps |= GamepadCapabilities.Gyroscope | GamepadCapabilities.Accelerometer |
                       GamepadCapabilities.Touchpad | GamepadCapabilities.AdaptiveTriggers |
                       GamepadCapabilities.TriggerRumble;
                break;
                
            case ESteamInputType.k_ESteamInputType_SwitchProController:
            case ESteamInputType.k_ESteamInputType_SwitchJoyConPair:
                caps |= GamepadCapabilities.Gyroscope | GamepadCapabilities.Accelerometer |
                       GamepadCapabilities.HDRumble;
                break;
        }
        
        return caps;
    }
    
    public override void Shutdown()
    {
        foreach (var gamepad in _gamepads.Values.ToList())
        {
            SteamInput.StopAnalogActionMomentum(gamepad.Handle, _analogActions.GetValueOrDefault("look"));
            RemoveController(gamepad.Handle);
        }
        
        if (_steamInitialized)
        {
            SteamInput.Shutdown();
            // Note: Don't call SteamAPI.Shutdown() here as other systems may still need it
        }
        
        _gamepads.Clear();
        _devices.Clear();
        _digitalActions.Clear();
        _analogActions.Clear();
        IsAvailable = false;
    }
    
    public override void Update()
    {
        if (!IsEnabled || !IsAvailable) return;
        
        // Run Steam callbacks
        SteamAPI.RunCallbacks();
        SteamInput.RunFrame();
        
        // Check for controller changes
        var controllers = new InputHandle_t[Constants.STEAM_INPUT_MAX_COUNT];
        int count = SteamInput.GetConnectedControllers(controllers);
        
        // Check for new controllers
        for (int i = 0; i < count; i++)
        {
            if (!_gamepads.ContainsKey(controllers[i].m_InputHandle))
            {
                AddController(controllers[i]);
            }
        }
        
        // Check for disconnected controllers
        var connectedHandles = new HashSet<ulong>();
        for (int i = 0; i < count; i++)
        {
            connectedHandles.Add(controllers[i].m_InputHandle);
        }
        
        foreach (var handle in _gamepads.Keys.ToList())
        {
            if (!connectedHandles.Contains(handle))
            {
                RemoveController(new InputHandle_t(handle));
            }
        }
        
        // Update each controller
        foreach (var (handle, gamepad) in _gamepads)
        {
            gamepad.BeginFrame();
            UpdateController(gamepad);
        }
    }
    
    private void UpdateController(SteamGamepad gamepad)
    {
        var handle = gamepad.Handle;
        
        // Read analog actions
        if (_analogActions.TryGetValue("move", out var moveHandle))
        {
            var move = SteamInput.GetAnalogActionData(handle, moveHandle);
            if (move.bActive != 0)
            {
                gamepad.LeftStick = new Vector2(move.x, move.y);
            }
        }
        
        if (_analogActions.TryGetValue("look", out var lookHandle))
        {
            var look = SteamInput.GetAnalogActionData(handle, lookHandle);
            if (look.bActive != 0)
            {
                gamepad.RightStick = new Vector2(look.x, look.y);
            }
        }
        
        // Read gyro
        if (gamepad.Gyro != null && _analogActions.TryGetValue("gyro", out var gyroHandle))
        {
            var gyro = SteamInput.GetAnalogActionData(handle, gyroHandle);
            if (gyro.bActive != 0)
            {
                // Steam Input gyro: x = pitch, y = yaw, (no roll in basic API)
                gamepad.Gyro.AngularVelocity = new Vector3(gyro.x, gyro.y, 0);
            }
        }
        
        // Read motion data directly if available
        if (gamepad.Gyro != null)
        {
            var motion = SteamInput.GetMotionData(handle);
            gamepad.Gyro.AngularVelocity = new Vector3(
                motion.rotVelX,
                motion.rotVelY,
                motion.rotVelZ
            );
            gamepad.Gyro.Acceleration = new Vector3(
                motion.posAccelX,
                motion.posAccelY,
                motion.posAccelZ
            );
        }
        
        // Read digital actions
        foreach (var (name, actionHandle) in _digitalActions)
        {
            var data = SteamInput.GetDigitalActionData(handle, actionHandle);
            var buttonName = MapSteamActionToButton(name);
            gamepad.SetButtonState(buttonName, data.bState != 0 ? 1f : 0f);
        }
    }
    
    private static string MapSteamActionToButton(string steamAction)
    {
        return steamAction switch
        {
            "jump" => "buttonSouth",
            "attack" => "rightTrigger",
            "interact" => "buttonWest",
            "menu_confirm" => "buttonSouth",
            "menu_cancel" => "buttonEast",
            "pause" => "start",
            _ => steamAction
        };
    }
    
    public override IEnumerable<GamepadDevice> GetGamepads()
    {
        return _gamepads.Values;
    }
    
    public override IEnumerable<GyroDevice> GetGyros()
    {
        return _gamepads.Values
            .Where(g => g.Gyro != null)
            .Select(g => g.Gyro!);
    }
    
    public override void SendHaptic(InputDevice device, HapticFeedback feedback)
    {
        if (device is SteamGamepad steamGamepad)
        {
            // Steam Input uses microseconds for duration
            var durationUs = (ushort)(feedback.Duration * 1_000_000);
            
            // Left motor = low freq, right motor = high freq
            SteamInput.Legacy_TriggerHapticPulse(
                steamGamepad.Handle,
                ESteamControllerPad.k_ESteamControllerPad_Left,
                durationUs
            );
            
            // For full rumble, use TriggerVibration
            SteamInput.TriggerVibration(
                steamGamepad.Handle,
                (ushort)(feedback.LeftMotor * 65535),
                (ushort)(feedback.RightMotor * 65535)
            );
        }
    }
    
    public override void StopHaptics(InputDevice device)
    {
        if (device is SteamGamepad steamGamepad)
        {
            SteamInput.TriggerVibration(steamGamepad.Handle, 0, 0);
        }
    }
    
    /// <summary>
    /// Activate a specific action set for a controller.
    /// </summary>
    public void ActivateActionSet(SteamGamepad gamepad, string actionSetName)
    {
        var handle = SteamInput.GetActionSetHandle(actionSetName);
        if (handle.m_InputActionSetHandle != 0)
        {
            SteamInput.ActivateActionSet(gamepad.Handle, handle);
        }
    }
    
    /// <summary>
    /// Activate the gameplay action set.
    /// </summary>
    public void ActivateGameplayActions()
    {
        foreach (var gamepad in _gamepads.Values)
        {
            SteamInput.ActivateActionSet(gamepad.Handle, _gameplayActionSet);
        }
    }
    
    /// <summary>
    /// Activate the menu action set.
    /// </summary>
    public void ActivateMenuActions()
    {
        foreach (var gamepad in _gamepads.Values)
        {
            SteamInput.ActivateActionSet(gamepad.Handle, _menuActionSet);
        }
    }
    
    /// <summary>
    /// Show the Steam Input binding configuration UI.
    /// </summary>
    public void ShowBindingPanel(SteamGamepad gamepad)
    {
        SteamInput.ShowBindingPanel(gamepad.Handle);
    }
}

/// <summary>
/// Extended gamepad device for Steam Input with additional Steam-specific features.
/// </summary>
public class SteamGamepad : GamepadDevice
{
    /// <summary>
    /// Steam Input handle for this controller.
    /// </summary>
    public InputHandle_t Handle { get; internal set; }
    
    /// <summary>
    /// Associated gyro device (if controller has gyro).
    /// </summary>
    public GyroDevice? Gyro { get; internal set; }
    
    /// <summary>
    /// Whether gyro aiming is currently active.
    /// </summary>
    public bool IsGyroActive { get; set; } = true;
}
