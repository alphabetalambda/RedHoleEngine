using RedHoleEngine.Input.Actions;

namespace RedHoleEngine.Input.Defaults;

/// <summary>
/// Default input actions for common gameplay scenarios.
/// </summary>
public static class DefaultActions
{
    /// <summary>
    /// Create a default input action asset with common gameplay actions.
    /// </summary>
    public static InputActionAsset CreateDefault()
    {
        var asset = new InputActionAsset { Name = "DefaultInputActions" };
        
        // Add control schemes
        asset.ControlSchemes.Add(ControlScheme.KeyboardMouse);
        asset.ControlSchemes.Add(ControlScheme.Gamepad);
        asset.ControlSchemes.Add(ControlScheme.GamepadWithGyro);
        
        // Gameplay actions
        asset.AddActionMap(CreateGameplayMap());
        
        // UI actions
        asset.AddActionMap(CreateUIMap());
        
        // Camera actions (for games with separate camera control)
        asset.AddActionMap(CreateCameraMap());
        
        return asset;
    }
    
    /// <summary>
    /// Create the gameplay action map.
    /// </summary>
    public static InputActionMap CreateGameplayMap()
    {
        var map = InputActionMap.Create("Gameplay");
        
        // Movement (WASD + Left Stick)
        var move = map.AddVector2("Move");
        move.Composites.Add(CompositeBinding.Create2DVector(
            InputBinding.Create(InputBindingPath.Keyboard.W, "up"),
            InputBinding.Create(InputBindingPath.Keyboard.S, "down"),
            InputBinding.Create(InputBindingPath.Keyboard.A, "left"),
            InputBinding.Create(InputBindingPath.Keyboard.D, "right")
        ));
        move.Bindings.Add(InputBinding.Create(InputBindingPath.Gamepad.LeftStick));
        
        // Look (Mouse + Right Stick + Gyro)
        var look = map.AddVector2("Look", 
            InputBindingPath.Mouse.Delta,
            InputBindingPath.Gamepad.RightStick);
        
        // Gyro look (separate for fine control)
        map.AddVector3("GyroLook", InputBindingPath.Gyro.AngularVelocity);
        
        // Jump (Space + A/Cross)
        map.AddButton("Jump", 
            InputBindingPath.Keyboard.Space,
            InputBindingPath.Gamepad.ButtonSouth);
        
        // Attack/Fire (Left Mouse + Right Trigger)
        map.AddButton("Attack",
            InputBindingPath.Mouse.LeftButton,
            InputBindingPath.Gamepad.RightTrigger);
        
        // Alt Attack/Aim (Right Mouse + Left Trigger)
        map.AddButton("AltAttack",
            InputBindingPath.Mouse.RightButton,
            InputBindingPath.Gamepad.LeftTrigger);
        
        // Interact (E + X/Square)
        map.AddButton("Interact",
            InputBindingPath.Keyboard.E,
            InputBindingPath.Gamepad.ButtonWest);
        
        // Reload (R + X/Square)
        map.AddButton("Reload",
            InputBindingPath.Keyboard.R,
            InputBindingPath.Gamepad.ButtonWest);
        
        // Sprint (Shift + L3)
        map.AddButton("Sprint",
            InputBindingPath.Keyboard.LeftShift,
            InputBindingPath.Gamepad.LeftStickPress);
        
        // Crouch (C/Ctrl + B/Circle)
        map.AddButton("Crouch",
            InputBindingPath.Keyboard.C,
            InputBindingPath.Keyboard.LeftCtrl,
            InputBindingPath.Gamepad.ButtonEast);
        
        // Pause (Escape + Start)
        map.AddButton("Pause",
            InputBindingPath.Keyboard.Escape,
            InputBindingPath.Gamepad.Start);
        
        // Quick Menu/Inventory (Tab + Select)
        map.AddButton("QuickMenu",
            InputBindingPath.Keyboard.Tab,
            InputBindingPath.Gamepad.Select);
        
        // Weapon slots (1-4 + D-Pad)
        map.AddButton("Weapon1", InputBindingPath.Keyboard.Num1, InputBindingPath.Gamepad.DPadUp);
        map.AddButton("Weapon2", InputBindingPath.Keyboard.Num2, InputBindingPath.Gamepad.DPadRight);
        map.AddButton("Weapon3", InputBindingPath.Keyboard.Num3, InputBindingPath.Gamepad.DPadDown);
        map.AddButton("Weapon4", InputBindingPath.Keyboard.Num4, InputBindingPath.Gamepad.DPadLeft);
        
        // Scroll (Mouse Wheel + D-Pad Up/Down for cycling)
        map.AddAxis("WeaponScroll", InputBindingPath.Mouse.ScrollY);
        
        // Ability buttons (Q, F + Bumpers)
        map.AddButton("Ability1",
            InputBindingPath.Keyboard.Q,
            InputBindingPath.Gamepad.LeftShoulder);
        
        map.AddButton("Ability2",
            InputBindingPath.Keyboard.F,
            InputBindingPath.Gamepad.RightShoulder);
        
        // Back paddle actions (Steam Deck / Elite controllers)
        map.AddButton("QuickAction1", InputBindingPath.Gamepad.LeftPaddle1);
        map.AddButton("QuickAction2", InputBindingPath.Gamepad.LeftPaddle2);
        map.AddButton("QuickAction3", InputBindingPath.Gamepad.RightPaddle1);
        map.AddButton("QuickAction4", InputBindingPath.Gamepad.RightPaddle2);
        
        return map;
    }
    
    /// <summary>
    /// Create the UI action map.
    /// </summary>
    public static InputActionMap CreateUIMap()
    {
        var map = InputActionMap.Create("UI");
        
        // Navigation
        var navigate = map.AddVector2("Navigate");
        navigate.Composites.Add(CompositeBinding.Create2DVector(
            InputBinding.Create(InputBindingPath.Keyboard.UpArrow, "up"),
            InputBinding.Create(InputBindingPath.Keyboard.DownArrow, "down"),
            InputBinding.Create(InputBindingPath.Keyboard.LeftArrow, "left"),
            InputBinding.Create(InputBindingPath.Keyboard.RightArrow, "right")
        ));
        navigate.Bindings.Add(InputBinding.Create(InputBindingPath.Gamepad.LeftStick));
        navigate.Bindings.Add(InputBinding.Create(InputBindingPath.Gamepad.DPad));
        
        // Submit/Confirm (Enter + A/Cross)
        map.AddButton("Submit",
            InputBindingPath.Keyboard.Enter,
            InputBindingPath.Keyboard.Space,
            InputBindingPath.Gamepad.ButtonSouth);
        
        // Cancel/Back (Escape + B/Circle)
        map.AddButton("Cancel",
            InputBindingPath.Keyboard.Escape,
            InputBindingPath.Gamepad.ButtonEast);
        
        // Point (Mouse position)
        map.AddVector2("Point", InputBindingPath.Mouse.Position);
        
        // Click (Mouse buttons)
        map.AddButton("Click", 
            InputBindingPath.Mouse.LeftButton,
            InputBindingPath.Gamepad.ButtonSouth);
        
        map.AddButton("RightClick",
            InputBindingPath.Mouse.RightButton,
            InputBindingPath.Gamepad.ButtonEast);
        
        map.AddButton("MiddleClick", InputBindingPath.Mouse.MiddleButton);
        
        // Scroll
        map.AddVector2("ScrollWheel", InputBindingPath.Mouse.Scroll);
        
        // Tab navigation
        map.AddButton("PreviousTab",
            InputBindingPath.Gamepad.LeftShoulder,
            InputBindingPath.Keyboard.Q);
        
        map.AddButton("NextTab",
            InputBindingPath.Gamepad.RightShoulder,
            InputBindingPath.Keyboard.E);
        
        return map;
    }
    
    /// <summary>
    /// Create camera control action map.
    /// </summary>
    public static InputActionMap CreateCameraMap()
    {
        var map = InputActionMap.Create("Camera");
        
        // Orbit camera (for editors, strategy games)
        var orbit = map.AddVector2("Orbit");
        orbit.Bindings.Add(InputBinding.Create(InputBindingPath.Mouse.Delta));
        orbit.Bindings.Add(InputBinding.Create(InputBindingPath.Gamepad.RightStick));
        
        // Pan camera
        var pan = map.AddVector2("Pan");
        pan.Composites.Add(CompositeBinding.Create2DVector(
            InputBinding.Create(InputBindingPath.Keyboard.W, "up"),
            InputBinding.Create(InputBindingPath.Keyboard.S, "down"),
            InputBinding.Create(InputBindingPath.Keyboard.A, "left"),
            InputBinding.Create(InputBindingPath.Keyboard.D, "right")
        ));
        
        // Zoom
        map.AddAxis("Zoom",
            InputBindingPath.Mouse.ScrollY,
            InputBindingPath.Gamepad.RightTrigger);
        
        // Rotate button (hold to enable rotation)
        map.AddButton("RotateModifier",
            InputBindingPath.Mouse.RightButton,
            InputBindingPath.Gamepad.LeftShoulder);
        
        // Reset camera
        map.AddButton("ResetCamera",
            InputBindingPath.Keyboard.Home,
            InputBindingPath.Gamepad.RightStickPress);
        
        return map;
    }
    
    /// <summary>
    /// Create a first-person shooter action map.
    /// </summary>
    public static InputActionMap CreateFPSMap()
    {
        var map = CreateGameplayMap();
        map.Name = "FPS";
        
        // ADS (Aim Down Sights) - Hold
        var ads = map.FindAction("AltAttack");
        if (ads != null)
        {
            ads.Name = "ADS";
            foreach (var binding in ads.Bindings)
            {
                binding.Interaction = InputInteractionType.Continuous;
            }
        }
        
        // Melee
        map.AddButton("Melee",
            InputBindingPath.Keyboard.V,
            InputBindingPath.Gamepad.RightStickPress);
        
        // Grenade
        map.AddButton("Grenade",
            InputBindingPath.Keyboard.G,
            InputBindingPath.Gamepad.LeftShoulder);
        
        // Lean (Q/E or Gyro roll)
        map.AddButton("LeanLeft", InputBindingPath.Keyboard.Q);
        map.AddButton("LeanRight", InputBindingPath.Keyboard.E);
        map.AddAxis("LeanGyro", InputBindingPath.Gyro.Roll);
        
        return map;
    }
    
    /// <summary>
    /// Create a third-person action game map.
    /// </summary>
    public static InputActionMap CreateThirdPersonMap()
    {
        var map = CreateGameplayMap();
        map.Name = "ThirdPerson";
        
        // Lock-on targeting
        map.AddButton("LockOn",
            InputBindingPath.Mouse.MiddleButton,
            InputBindingPath.Gamepad.RightStickPress);
        
        // Dodge/Roll
        map.AddButton("Dodge",
            InputBindingPath.Keyboard.Space, // Double-tap space
            InputBindingPath.Gamepad.ButtonEast);
        
        // Light Attack (already have Attack)
        // Heavy Attack
        map.AddButton("HeavyAttack",
            InputBindingPath.Keyboard.F,
            InputBindingPath.Gamepad.RightTrigger);
        
        // Block/Parry
        map.AddButton("Block",
            InputBindingPath.Mouse.RightButton,
            InputBindingPath.Gamepad.LeftTrigger);
        
        return map;
    }
    
    /// <summary>
    /// Create a driving/racing game map.
    /// </summary>
    public static InputActionMap CreateDrivingMap()
    {
        var map = InputActionMap.Create("Driving");
        
        // Steering
        map.AddAxis("Steer",
            InputBindingPath.Gamepad.LeftStickX);
        var steerKeys = map.AddAxis("SteerKeyboard");
        steerKeys.Composites.Add(CompositeBinding.Create1DAxis(
            InputBinding.Create(InputBindingPath.Keyboard.A, "negative"),
            InputBinding.Create(InputBindingPath.Keyboard.D, "positive")
        ));
        
        // Gyro steering (Steam Deck)
        map.AddAxis("SteerGyro", InputBindingPath.Gyro.Roll);
        
        // Throttle/Brake
        map.AddAxis("Throttle",
            InputBindingPath.Gamepad.RightTrigger,
            InputBindingPath.Keyboard.W);
        
        map.AddAxis("Brake",
            InputBindingPath.Gamepad.LeftTrigger,
            InputBindingPath.Keyboard.S);
        
        // Handbrake
        map.AddButton("Handbrake",
            InputBindingPath.Keyboard.Space,
            InputBindingPath.Gamepad.ButtonSouth);
        
        // Nitro/Boost
        map.AddButton("Boost",
            InputBindingPath.Keyboard.LeftShift,
            InputBindingPath.Gamepad.ButtonWest);
        
        // Gear shift
        map.AddButton("ShiftUp",
            InputBindingPath.Keyboard.E,
            InputBindingPath.Gamepad.RightShoulder);
        
        map.AddButton("ShiftDown",
            InputBindingPath.Keyboard.Q,
            InputBindingPath.Gamepad.LeftShoulder);
        
        // Look around
        map.AddVector2("Look",
            InputBindingPath.Mouse.Delta,
            InputBindingPath.Gamepad.RightStick);
        
        // Horn
        map.AddButton("Horn",
            InputBindingPath.Keyboard.H,
            InputBindingPath.Gamepad.LeftStickPress);
        
        // Reset/Respawn
        map.AddButton("Reset",
            InputBindingPath.Keyboard.R,
            InputBindingPath.Gamepad.Select);
        
        return map;
    }
}
