using System.Numerics;
using RedHoleEngine.Input.Devices;
using RedHoleEngine.Input.Haptics;
using Silk.NET.Input;

namespace RedHoleEngine.Input.Providers;

/// <summary>
/// Input provider for keyboard and mouse using Silk.NET.
/// This provider is always active and cannot be disabled.
/// </summary>
public class KeyboardMouseProvider : InputProviderBase
{
    public override string Name => "KeyboardMouse";
    public override int Priority => 0; // Lowest priority (always active as fallback)
    
    private IInputContext? _inputContext;
    private KeyboardDevice? _keyboard;
    private MouseDevice? _mouse;
    
    // First-mouse tracking for camera control
    private bool _firstMouse = true;
    private Vector2 _lastMousePosition;
    
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
            Console.WriteLine("[KeyboardMouseProvider] No input context provided");
            return false;
        }
        
        // Get keyboard
        var silkKeyboard = _inputContext.Keyboards.FirstOrDefault();
        if (silkKeyboard != null)
        {
            _keyboard = new KeyboardDevice
            {
                DeviceId = 0,
                SilkKeyboard = silkKeyboard
            };
            _keyboard.ProviderName = Name;
            _devices.Add(_keyboard);
            
            // Subscribe to events
            silkKeyboard.KeyDown += OnKeyDown;
            silkKeyboard.KeyUp += OnKeyUp;
            
            Console.WriteLine($"[KeyboardMouseProvider] Keyboard initialized: {silkKeyboard.Name}");
        }
        
        // Get mouse
        var silkMouse = _inputContext.Mice.FirstOrDefault();
        if (silkMouse != null)
        {
            _mouse = new MouseDevice
            {
                DeviceId = 0,
                SilkMouse = silkMouse
            };
            _mouse.ProviderName = Name;
            _devices.Add(_mouse);
            
            // Subscribe to events
            silkMouse.MouseMove += OnMouseMove;
            silkMouse.MouseDown += OnMouseDown;
            silkMouse.MouseUp += OnMouseUp;
            silkMouse.Scroll += OnScroll;
            
            Console.WriteLine($"[KeyboardMouseProvider] Mouse initialized: {silkMouse.Name}");
        }
        
        IsAvailable = _keyboard != null || _mouse != null;
        return IsAvailable;
    }
    
    public override void Shutdown()
    {
        if (_keyboard?.SilkKeyboard != null)
        {
            _keyboard.SilkKeyboard.KeyDown -= OnKeyDown;
            _keyboard.SilkKeyboard.KeyUp -= OnKeyUp;
        }
        
        if (_mouse?.SilkMouse != null)
        {
            _mouse.SilkMouse.MouseMove -= OnMouseMove;
            _mouse.SilkMouse.MouseDown -= OnMouseDown;
            _mouse.SilkMouse.MouseUp -= OnMouseUp;
            _mouse.SilkMouse.Scroll -= OnScroll;
        }
        
        _devices.Clear();
        _keyboard = null;
        _mouse = null;
        IsAvailable = false;
    }
    
    public override void Update()
    {
        if (!IsEnabled) return;
        
        _keyboard?.BeginFrame();
        _mouse?.BeginFrame();
        
        // Poll current state for continuous input
        if (_keyboard?.SilkKeyboard != null)
        {
            // Keys are updated via events
        }
        
        if (_mouse?.SilkMouse != null)
        {
            _mouse.Position = _mouse.SilkMouse.Position;
        }
    }
    
    public override void EndFrame()
    {
        _keyboard?.EndFrame();
        _mouse?.EndFrame();
        
        // Reset first mouse flag if mouse was just captured
        if (_mouse?.IsCursorCaptured == true && _firstMouse)
        {
            _lastMousePosition = _mouse.Position;
            _firstMouse = false;
        }
    }
    
    private void OnKeyDown(IKeyboard keyboard, Key key, int scanCode)
    {
        if (_keyboard == null || !IsEnabled) return;
        var control = KeyboardDevice.KeyToControl(key);
        _keyboard.SetKeyState(control, true);
    }
    
    private void OnKeyUp(IKeyboard keyboard, Key key, int scanCode)
    {
        if (_keyboard == null || !IsEnabled) return;
        var control = KeyboardDevice.KeyToControl(key);
        _keyboard.SetKeyState(control, false);
    }
    
    private void OnMouseMove(IMouse mouse, Vector2 position)
    {
        if (_mouse == null || !IsEnabled) return;
        
        _mouse.Position = position;
        
        if (_mouse.IsCursorCaptured)
        {
            if (_firstMouse)
            {
                _lastMousePosition = position;
                _firstMouse = false;
                return;
            }
            
            var delta = position - _lastMousePosition;
            _mouse.AddDelta(delta);
            _lastMousePosition = position;
        }
    }
    
    private void OnMouseDown(IMouse mouse, MouseButton button)
    {
        if (_mouse == null || !IsEnabled) return;
        _mouse.SetButtonState(button, true);
    }
    
    private void OnMouseUp(IMouse mouse, MouseButton button)
    {
        if (_mouse == null || !IsEnabled) return;
        _mouse.SetButtonState(button, false);
    }
    
    private void OnScroll(IMouse mouse, ScrollWheel wheel)
    {
        if (_mouse == null || !IsEnabled) return;
        _mouse.SetScrollDelta(new Vector2(wheel.X, wheel.Y));
    }
    
    public override KeyboardDevice? GetKeyboard() => _keyboard;
    public override MouseDevice? GetMouse() => _mouse;
    
    /// <summary>
    /// Set mouse cursor capture mode.
    /// </summary>
    public void SetCursorCaptured(bool captured)
    {
        if (_mouse != null)
        {
            _mouse.SetCursorCaptured(captured);
            if (captured)
            {
                _firstMouse = true;
            }
        }
    }
    
    /// <summary>
    /// Toggle cursor capture.
    /// </summary>
    public void ToggleCursorCapture()
    {
        if (_mouse != null)
        {
            SetCursorCaptured(!_mouse.IsCursorCaptured);
        }
    }
    
    public override void SendHaptic(InputDevice device, HapticFeedback feedback)
    {
        // Keyboard and mouse don't support haptics
    }
    
    public override void StopHaptics(InputDevice device)
    {
        // No-op
    }
}
