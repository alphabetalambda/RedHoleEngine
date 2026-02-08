using System.Numerics;
using Silk.NET.Input;

namespace RedHoleEngine.Engine;

public class InputHandler
{
    private readonly Camera _camera;
    private IKeyboard? _keyboard;
    private IMouse? _mouse;
    
    private Vector2 _lastMousePosition;
    private bool _firstMouse = true;
    private bool _mouseCaptured = true;

    public InputHandler(Camera camera)
    {
        _camera = camera;
    }

    /// <summary>
    /// Initialize input devices from Silk.NET input context
    /// </summary>
    public void Initialize(IInputContext inputContext)
    {
        _keyboard = inputContext.Keyboards.FirstOrDefault();
        _mouse = inputContext.Mice.FirstOrDefault();

        if (_mouse != null)
        {
            _mouse.Cursor.CursorMode = CursorMode.Raw;
            _mouse.MouseMove += OnMouseMove;
        }

        if (_keyboard != null)
        {
            _keyboard.KeyDown += OnKeyDown;
        }
    }

    /// <summary>
    /// Process keyboard input for camera movement
    /// </summary>
    public void Update(float deltaTime)
    {
        if (_keyboard == null) return;

        // WASD movement
        if (_keyboard.IsKeyPressed(Key.W))
            _camera.MoveForward(deltaTime, forward: true);
        
        if (_keyboard.IsKeyPressed(Key.S))
            _camera.MoveForward(deltaTime, forward: false);
        
        if (_keyboard.IsKeyPressed(Key.A))
            _camera.MoveRight(deltaTime, right: false);
        
        if (_keyboard.IsKeyPressed(Key.D))
            _camera.MoveRight(deltaTime, right: true);

        // Up/Down movement (Space/Shift)
        if (_keyboard.IsKeyPressed(Key.Space))
            _camera.MoveUp(deltaTime, up: true);
        
        if (_keyboard.IsKeyPressed(Key.ShiftLeft))
            _camera.MoveUp(deltaTime, up: false);
    }

    /// <summary>
    /// Handle mouse movement for camera rotation
    /// </summary>
    private void OnMouseMove(IMouse mouse, Vector2 position)
    {
        if (!_mouseCaptured) return;

        if (_firstMouse)
        {
            _lastMousePosition = position;
            _firstMouse = false;
            return;
        }

        float deltaX = position.X - _lastMousePosition.X;
        float deltaY = position.Y - _lastMousePosition.Y;
        _lastMousePosition = position;

        _camera.Rotate(deltaX, deltaY);
    }

    /// <summary>
    /// Handle key press events
    /// </summary>
    private void OnKeyDown(IKeyboard keyboard, Key key, int scanCode)
    {
        // Escape to toggle mouse capture
        if (key == Key.Escape)
        {
            _mouseCaptured = !_mouseCaptured;
            if (_mouse != null)
            {
                _mouse.Cursor.CursorMode = _mouseCaptured ? CursorMode.Raw : CursorMode.Normal;
            }
            _firstMouse = true;
        }
    }

    /// <summary>
    /// Check if a specific key is pressed (for external use)
    /// </summary>
    public bool IsKeyPressed(Key key)
    {
        return _keyboard?.IsKeyPressed(key) ?? false;
    }

    /// <summary>
    /// Cleanup
    /// </summary>
    public void Dispose()
    {
        if (_mouse != null)
        {
            _mouse.MouseMove -= OnMouseMove;
        }
        if (_keyboard != null)
        {
            _keyboard.KeyDown -= OnKeyDown;
        }
    }
}
