namespace RedHoleEngine.Input.Actions;

/// <summary>
/// Device-agnostic input paths for binding actions to physical inputs.
/// Format: &lt;Device&gt;/&lt;Control&gt; or &lt;Device&gt;/&lt;Control&gt;/&lt;Subcontrol&gt;
/// </summary>
public static class InputBindingPath
{
    // ═══════════════════════════════════════════════════════════════════════════
    // GAMEPAD
    // ═══════════════════════════════════════════════════════════════════════════
    
    public static class Gamepad
    {
        // Face buttons
        public const string ButtonSouth = "<Gamepad>/buttonSouth";       // A / Cross
        public const string ButtonEast = "<Gamepad>/buttonEast";         // B / Circle
        public const string ButtonWest = "<Gamepad>/buttonWest";         // X / Square
        public const string ButtonNorth = "<Gamepad>/buttonNorth";       // Y / Triangle
        
        // Bumpers and triggers
        public const string LeftShoulder = "<Gamepad>/leftShoulder";     // LB / L1
        public const string RightShoulder = "<Gamepad>/rightShoulder";   // RB / R1
        public const string LeftTrigger = "<Gamepad>/leftTrigger";       // LT / L2 (axis 0-1)
        public const string RightTrigger = "<Gamepad>/rightTrigger";     // RT / R2 (axis 0-1)
        
        // Thumbsticks
        public const string LeftStick = "<Gamepad>/leftStick";           // Vector2
        public const string LeftStickX = "<Gamepad>/leftStick/x";        // Axis
        public const string LeftStickY = "<Gamepad>/leftStick/y";        // Axis
        public const string LeftStickPress = "<Gamepad>/leftStickPress"; // L3
        
        public const string RightStick = "<Gamepad>/rightStick";         // Vector2
        public const string RightStickX = "<Gamepad>/rightStick/x";      // Axis
        public const string RightStickY = "<Gamepad>/rightStick/y";      // Axis
        public const string RightStickPress = "<Gamepad>/rightStickPress"; // R3
        
        // D-Pad
        public const string DPad = "<Gamepad>/dpad";                     // Vector2
        public const string DPadUp = "<Gamepad>/dpad/up";
        public const string DPadDown = "<Gamepad>/dpad/down";
        public const string DPadLeft = "<Gamepad>/dpad/left";
        public const string DPadRight = "<Gamepad>/dpad/right";
        
        // Menu buttons
        public const string Start = "<Gamepad>/start";
        public const string Select = "<Gamepad>/select";
        public const string Guide = "<Gamepad>/guide";                   // Xbox/PS button
        
        // Steam Deck specific
        public const string LeftTrackpad = "<Gamepad>/leftTrackpad";     // Vector2
        public const string RightTrackpad = "<Gamepad>/rightTrackpad";   // Vector2
        public const string LeftTrackpadPress = "<Gamepad>/leftTrackpadPress";
        public const string RightTrackpadPress = "<Gamepad>/rightTrackpadPress";
        
        // Back buttons (Steam Deck / Elite controllers)
        public const string LeftPaddle1 = "<Gamepad>/leftPaddle1";       // L4
        public const string LeftPaddle2 = "<Gamepad>/leftPaddle2";       // L5
        public const string RightPaddle1 = "<Gamepad>/rightPaddle1";     // R4
        public const string RightPaddle2 = "<Gamepad>/rightPaddle2";     // R5
    }
    
    // ═══════════════════════════════════════════════════════════════════════════
    // GYROSCOPE
    // ═══════════════════════════════════════════════════════════════════════════
    
    public static class Gyro
    {
        public const string AngularVelocity = "<Gyro>/angularVelocity"; // Vector3 (pitch, yaw, roll)
        public const string Pitch = "<Gyro>/pitch";                      // Axis (rotation around X)
        public const string Yaw = "<Gyro>/yaw";                          // Axis (rotation around Y)
        public const string Roll = "<Gyro>/roll";                        // Axis (rotation around Z)
        
        public const string Acceleration = "<Gyro>/acceleration";        // Vector3
        public const string Gravity = "<Gyro>/gravity";                  // Vector3
    }
    
    // ═══════════════════════════════════════════════════════════════════════════
    // KEYBOARD
    // ═══════════════════════════════════════════════════════════════════════════
    
    public static class Keyboard
    {
        // Letters
        public const string A = "<Keyboard>/a";
        public const string B = "<Keyboard>/b";
        public const string C = "<Keyboard>/c";
        public const string D = "<Keyboard>/d";
        public const string E = "<Keyboard>/e";
        public const string F = "<Keyboard>/f";
        public const string G = "<Keyboard>/g";
        public const string H = "<Keyboard>/h";
        public const string I = "<Keyboard>/i";
        public const string J = "<Keyboard>/j";
        public const string K = "<Keyboard>/k";
        public const string L = "<Keyboard>/l";
        public const string M = "<Keyboard>/m";
        public const string N = "<Keyboard>/n";
        public const string O = "<Keyboard>/o";
        public const string P = "<Keyboard>/p";
        public const string Q = "<Keyboard>/q";
        public const string R = "<Keyboard>/r";
        public const string S = "<Keyboard>/s";
        public const string T = "<Keyboard>/t";
        public const string U = "<Keyboard>/u";
        public const string V = "<Keyboard>/v";
        public const string W = "<Keyboard>/w";
        public const string X = "<Keyboard>/x";
        public const string Y = "<Keyboard>/y";
        public const string Z = "<Keyboard>/z";
        
        // Numbers
        public const string Num0 = "<Keyboard>/0";
        public const string Num1 = "<Keyboard>/1";
        public const string Num2 = "<Keyboard>/2";
        public const string Num3 = "<Keyboard>/3";
        public const string Num4 = "<Keyboard>/4";
        public const string Num5 = "<Keyboard>/5";
        public const string Num6 = "<Keyboard>/6";
        public const string Num7 = "<Keyboard>/7";
        public const string Num8 = "<Keyboard>/8";
        public const string Num9 = "<Keyboard>/9";
        
        // Function keys
        public const string F1 = "<Keyboard>/f1";
        public const string F2 = "<Keyboard>/f2";
        public const string F3 = "<Keyboard>/f3";
        public const string F4 = "<Keyboard>/f4";
        public const string F5 = "<Keyboard>/f5";
        public const string F6 = "<Keyboard>/f6";
        public const string F7 = "<Keyboard>/f7";
        public const string F8 = "<Keyboard>/f8";
        public const string F9 = "<Keyboard>/f9";
        public const string F10 = "<Keyboard>/f10";
        public const string F11 = "<Keyboard>/f11";
        public const string F12 = "<Keyboard>/f12";
        
        // Modifiers
        public const string LeftShift = "<Keyboard>/leftShift";
        public const string RightShift = "<Keyboard>/rightShift";
        public const string LeftCtrl = "<Keyboard>/leftCtrl";
        public const string RightCtrl = "<Keyboard>/rightCtrl";
        public const string LeftAlt = "<Keyboard>/leftAlt";
        public const string RightAlt = "<Keyboard>/rightAlt";
        public const string LeftMeta = "<Keyboard>/leftMeta";   // Win/Cmd
        public const string RightMeta = "<Keyboard>/rightMeta";
        
        // Special keys
        public const string Space = "<Keyboard>/space";
        public const string Enter = "<Keyboard>/enter";
        public const string Escape = "<Keyboard>/escape";
        public const string Tab = "<Keyboard>/tab";
        public const string Backspace = "<Keyboard>/backspace";
        public const string Delete = "<Keyboard>/delete";
        public const string Insert = "<Keyboard>/insert";
        public const string Home = "<Keyboard>/home";
        public const string End = "<Keyboard>/end";
        public const string PageUp = "<Keyboard>/pageUp";
        public const string PageDown = "<Keyboard>/pageDown";
        
        // Arrow keys
        public const string UpArrow = "<Keyboard>/upArrow";
        public const string DownArrow = "<Keyboard>/downArrow";
        public const string LeftArrow = "<Keyboard>/leftArrow";
        public const string RightArrow = "<Keyboard>/rightArrow";
        
        // Punctuation
        public const string Comma = "<Keyboard>/comma";
        public const string Period = "<Keyboard>/period";
        public const string Slash = "<Keyboard>/slash";
        public const string Semicolon = "<Keyboard>/semicolon";
        public const string Quote = "<Keyboard>/quote";
        public const string LeftBracket = "<Keyboard>/leftBracket";
        public const string RightBracket = "<Keyboard>/rightBracket";
        public const string Backslash = "<Keyboard>/backslash";
        public const string Minus = "<Keyboard>/minus";
        public const string Equals = "<Keyboard>/equals";
        public const string Backquote = "<Keyboard>/backquote";
        
        // Numpad
        public const string Numpad0 = "<Keyboard>/numpad0";
        public const string Numpad1 = "<Keyboard>/numpad1";
        public const string Numpad2 = "<Keyboard>/numpad2";
        public const string Numpad3 = "<Keyboard>/numpad3";
        public const string Numpad4 = "<Keyboard>/numpad4";
        public const string Numpad5 = "<Keyboard>/numpad5";
        public const string Numpad6 = "<Keyboard>/numpad6";
        public const string Numpad7 = "<Keyboard>/numpad7";
        public const string Numpad8 = "<Keyboard>/numpad8";
        public const string Numpad9 = "<Keyboard>/numpad9";
        public const string NumpadEnter = "<Keyboard>/numpadEnter";
        public const string NumpadPlus = "<Keyboard>/numpadPlus";
        public const string NumpadMinus = "<Keyboard>/numpadMinus";
        public const string NumpadMultiply = "<Keyboard>/numpadMultiply";
        public const string NumpadDivide = "<Keyboard>/numpadDivide";
        public const string NumpadPeriod = "<Keyboard>/numpadPeriod";
    }
    
    // ═══════════════════════════════════════════════════════════════════════════
    // MOUSE
    // ═══════════════════════════════════════════════════════════════════════════
    
    public static class Mouse
    {
        public const string Position = "<Mouse>/position";       // Vector2 (screen position)
        public const string Delta = "<Mouse>/delta";             // Vector2 (movement delta)
        public const string Scroll = "<Mouse>/scroll";           // Vector2 (scroll delta)
        public const string ScrollY = "<Mouse>/scroll/y";        // Axis (vertical scroll)
        public const string ScrollX = "<Mouse>/scroll/x";        // Axis (horizontal scroll)
        
        public const string LeftButton = "<Mouse>/leftButton";
        public const string RightButton = "<Mouse>/rightButton";
        public const string MiddleButton = "<Mouse>/middleButton";
        public const string ForwardButton = "<Mouse>/forwardButton";   // Mouse4
        public const string BackButton = "<Mouse>/backButton";         // Mouse5
    }
    
    /// <summary>
    /// Parse a binding path into device and control parts.
    /// </summary>
    public static (string device, string control) Parse(string path)
    {
        if (string.IsNullOrEmpty(path))
            return ("", "");
            
        // Format: <Device>/control or <Device>/control/subcontrol
        var startBracket = path.IndexOf('<');
        var endBracket = path.IndexOf('>');
        
        if (startBracket < 0 || endBracket < 0 || endBracket <= startBracket)
            return ("", path);
            
        var device = path.Substring(startBracket + 1, endBracket - startBracket - 1);
        var control = path.Length > endBracket + 2 ? path.Substring(endBracket + 2) : "";
        
        return (device, control);
    }
    
    /// <summary>
    /// Get the device type from a binding path.
    /// </summary>
    public static string GetDevice(string path)
    {
        return Parse(path).device;
    }
    
    /// <summary>
    /// Get the control name from a binding path.
    /// </summary>
    public static string GetControl(string path)
    {
        return Parse(path).control;
    }
    
    /// <summary>
    /// Check if the binding path is for a specific device type.
    /// </summary>
    public static bool IsDevice(string path, string deviceType)
    {
        return GetDevice(path).Equals(deviceType, StringComparison.OrdinalIgnoreCase);
    }
}
