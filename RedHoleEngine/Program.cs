using System.Numerics;
using RedHoleEngine.Engine;
using RedHoleEngine.Physics;
using RedHoleEngine.Rendering;
using RedHoleEngine.Rendering.Backends;
using Silk.NET.Input;
using Silk.NET.Input.Sdl;
using Silk.NET.Maths;
using Silk.NET.Windowing;
using Silk.NET.Windowing.Sdl;

namespace RedHoleEngine;

class Program
{
    private static IWindow? _window;
    private static IInputContext? _inputContext;

    private static Camera? _camera;
    private static InputHandler? _inputHandler;
    private static IGraphicsBackend? _backend;
    private static BlackHole? _blackHole;

    private static float _time;

    private const int WindowWidth = 1280;
    private const int WindowHeight = 720;
    
    // Set which backend to use
    private static readonly GraphicsBackendType BackendType = GraphicsBackendType.Vulkan;

    static void Main(string[] args)
    {
        Enginedeco.EngineTitlePrint();
        
        Console.WriteLine($"Using graphics backend: {BackendType}");
        Console.WriteLine("Creating window...");
        
        // Use SDL for Vulkan support on macOS
        if (BackendType == GraphicsBackendType.Vulkan)
        {
            SdlWindowing.RegisterPlatform();
            SdlInput.RegisterPlatform();
            Window.PrioritizeSdl();
        }
        
        var options = WindowOptions.Default with
        {
            Size = new Vector2D<int>(WindowWidth, WindowHeight),
            Title = "RedHole Engine - Schwarzschild Black Hole Raytracer",
            // Use Vulkan API for Metal support on macOS via MoltenVK
            API = BackendType == GraphicsBackendType.Vulkan 
                ? GraphicsAPI.DefaultVulkan 
                : new GraphicsAPI(ContextAPI.OpenGL, ContextProfile.Core, ContextFlags.ForwardCompatible, new APIVersion(4, 1)),
            VSync = true
        };

        _window = Window.Create(options);
        Console.WriteLine("Window created successfully");

        _window.Load += OnLoad;
        _window.Update += OnUpdate;
        _window.Render += OnRender;
        _window.Closing += OnClose;
        _window.Resize += OnResize;

        Console.WriteLine("Starting window...");
        _window.Run();
    }

    private static void OnLoad()
    {
        _inputContext = _window!.CreateInput();

        // Initialize camera - start positioned to see the black hole
        // Camera at (0, 10, 40) looking toward origin at (0,0,0)
        _camera = new Camera(
            position: new Vector3(0, 10, 40), // Further back for full view
            yaw: -90.0f,                       // Looking toward -Z
            pitch: -14.0f                      // Looking down toward origin: atan(10/40) ≈ 14°
        );
        _camera.MovementSpeed = 10.0f;
        _camera.FieldOfView = 60.0f;

        // Initialize input
        _inputHandler = new InputHandler(_camera);
        _inputHandler.Initialize(_inputContext);

        // Initialize black hole at origin
        _blackHole = BlackHole.CreateDefault();

        // Create graphics backend
        _backend = BackendType switch
        {
            GraphicsBackendType.Vulkan => new VulkanBackend(_window!, WindowWidth, WindowHeight),
            GraphicsBackendType.OpenGL => throw new NotSupportedException("OpenGL backend doesn't support compute shaders on macOS"),
            _ => throw new ArgumentOutOfRangeException()
        };
        
        _backend.Initialize();

        Console.WriteLine("\n=== Controls ===");
        Console.WriteLine("WASD - Move camera");
        Console.WriteLine("Mouse - Look around");
        Console.WriteLine("Space - Move up");
        Console.WriteLine("Shift - Move down");
        Console.WriteLine("Escape - Toggle mouse capture");
        Console.WriteLine("================\n");
    }

    private static void OnUpdate(double deltaTime)
    {
        _time += (float)deltaTime;
        _inputHandler?.Update((float)deltaTime);
    }

    private static void OnRender(double deltaTime)
    {
        _backend?.Render(_camera!, _blackHole!, _time);
    }

    private static void OnResize(Vector2D<int> newSize)
    {
        _backend?.Resize(newSize.X, newSize.Y);
    }

    private static void OnClose()
    {
        _backend?.Dispose();
        _inputHandler?.Dispose();
        Console.WriteLine("goodbye");
    }
}
