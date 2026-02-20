using System.Numerics;
using Silk.NET.Input;

namespace RedHoleEngine.Input.Devices;

/// <summary>
/// Represents a keyboard input device.
/// </summary>
public class KeyboardDevice : InputDevice
{
    public override InputDeviceType DeviceType => InputDeviceType.Keyboard;
    
    /// <summary>
    /// The underlying Silk.NET keyboard (if using Silk provider).
    /// </summary>
    internal IKeyboard? SilkKeyboard { get; set; }
    
    /// <summary>
    /// Current key states (true = pressed).
    /// </summary>
    private readonly Dictionary<string, bool> _keyStates = new();
    
    /// <summary>
    /// Previous frame key states.
    /// </summary>
    private readonly Dictionary<string, bool> _previousKeyStates = new();
    
    /// <summary>
    /// Keys that were pressed this frame.
    /// </summary>
    private readonly HashSet<string> _pressedThisFrame = new();
    
    /// <summary>
    /// Keys that were released this frame.
    /// </summary>
    private readonly HashSet<string> _releasedThisFrame = new();
    
    public KeyboardDevice()
    {
        Name = "Keyboard";
    }
    
    public override void BeginFrame()
    {
        base.BeginFrame();
        _pressedThisFrame.Clear();
        _releasedThisFrame.Clear();
        
        // Copy current to previous
        _previousKeyStates.Clear();
        foreach (var kvp in _keyStates)
        {
            _previousKeyStates[kvp.Key] = kvp.Value;
        }
    }
    
    /// <summary>
    /// Update key state (called by provider).
    /// </summary>
    internal void SetKeyState(string key, bool pressed)
    {
        var wasPressed = _keyStates.TryGetValue(key, out var prev) && prev;
        _keyStates[key] = pressed;
        
        if (pressed && !wasPressed)
            _pressedThisFrame.Add(key);
        else if (!pressed && wasPressed)
            _releasedThisFrame.Add(key);
    }
    
    /// <summary>
    /// Check if a key is currently pressed.
    /// </summary>
    public bool IsKeyPressed(string key)
    {
        return _keyStates.TryGetValue(key, out var pressed) && pressed;
    }
    
    /// <summary>
    /// Check if a key was just pressed this frame.
    /// </summary>
    public bool WasKeyPressedThisFrame(string key)
    {
        return _pressedThisFrame.Contains(key);
    }
    
    /// <summary>
    /// Check if a key was just released this frame.
    /// </summary>
    public bool WasKeyReleasedThisFrame(string key)
    {
        return _releasedThisFrame.Contains(key);
    }
    
    public override float ReadButton(string control)
    {
        return IsKeyPressed(control) ? 1f : 0f;
    }
    
    public override float ReadAxis(string control)
    {
        return ReadButton(control);
    }
    
    /// <summary>
    /// Convert Silk.NET Key to our control name.
    /// </summary>
    public static string KeyToControl(Key key)
    {
        return key switch
        {
            // Letters
            Key.A => "a", Key.B => "b", Key.C => "c", Key.D => "d",
            Key.E => "e", Key.F => "f", Key.G => "g", Key.H => "h",
            Key.I => "i", Key.J => "j", Key.K => "k", Key.L => "l",
            Key.M => "m", Key.N => "n", Key.O => "o", Key.P => "p",
            Key.Q => "q", Key.R => "r", Key.S => "s", Key.T => "t",
            Key.U => "u", Key.V => "v", Key.W => "w", Key.X => "x",
            Key.Y => "y", Key.Z => "z",
            
            // Numbers
            Key.Number0 => "0", Key.Number1 => "1", Key.Number2 => "2",
            Key.Number3 => "3", Key.Number4 => "4", Key.Number5 => "5",
            Key.Number6 => "6", Key.Number7 => "7", Key.Number8 => "8",
            Key.Number9 => "9",
            
            // Function keys
            Key.F1 => "f1", Key.F2 => "f2", Key.F3 => "f3", Key.F4 => "f4",
            Key.F5 => "f5", Key.F6 => "f6", Key.F7 => "f7", Key.F8 => "f8",
            Key.F9 => "f9", Key.F10 => "f10", Key.F11 => "f11", Key.F12 => "f12",
            
            // Modifiers
            Key.ShiftLeft => "leftShift", Key.ShiftRight => "rightShift",
            Key.ControlLeft => "leftCtrl", Key.ControlRight => "rightCtrl",
            Key.AltLeft => "leftAlt", Key.AltRight => "rightAlt",
            Key.SuperLeft => "leftMeta", Key.SuperRight => "rightMeta",
            
            // Special keys
            Key.Space => "space", Key.Enter => "enter", Key.Escape => "escape",
            Key.Tab => "tab", Key.Backspace => "backspace", Key.Delete => "delete",
            Key.Insert => "insert", Key.Home => "home", Key.End => "end",
            Key.PageUp => "pageUp", Key.PageDown => "pageDown",
            
            // Arrow keys
            Key.Up => "upArrow", Key.Down => "downArrow",
            Key.Left => "leftArrow", Key.Right => "rightArrow",
            
            // Punctuation
            Key.Comma => "comma", Key.Period => "period", Key.Slash => "slash",
            Key.Semicolon => "semicolon", Key.Apostrophe => "quote",
            Key.LeftBracket => "leftBracket", Key.RightBracket => "rightBracket",
            Key.BackSlash => "backslash", Key.Minus => "minus", Key.Equal => "equals",
            Key.GraveAccent => "backquote",
            
            // Numpad
            Key.Keypad0 => "numpad0", Key.Keypad1 => "numpad1", Key.Keypad2 => "numpad2",
            Key.Keypad3 => "numpad3", Key.Keypad4 => "numpad4", Key.Keypad5 => "numpad5",
            Key.Keypad6 => "numpad6", Key.Keypad7 => "numpad7", Key.Keypad8 => "numpad8",
            Key.Keypad9 => "numpad9", Key.KeypadEnter => "numpadEnter",
            Key.KeypadAdd => "numpadPlus", Key.KeypadSubtract => "numpadMinus",
            Key.KeypadMultiply => "numpadMultiply", Key.KeypadDivide => "numpadDivide",
            Key.KeypadDecimal => "numpadPeriod",
            
            _ => key.ToString().ToLowerInvariant()
        };
    }
}
