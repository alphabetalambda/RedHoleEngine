using System.Numerics;
using RedHoleEngine.Audio;
using RedHoleEngine.Components;
using RedHoleEngine.Core.ECS;
using RedHoleEngine.Core.Scene;
using RedHoleEngine.Engine;
using RedHoleEngine.Physics;
using RedHoleEngine.Physics.Collision;
using RedHoleEngine.Rendering;
using RedHoleEngine.Rendering.Backends;
using RedHoleEngine.Rendering.Debug;
using RedHoleEngine.Resources;
using Silk.NET.Input;
using Silk.NET.Maths;
using Silk.NET.Windowing;

namespace RedHoleEngine.Core;

/// <summary>
/// Main application class that manages the engine lifecycle.
/// </summary>
public class Application : IDisposable
{
    private IWindow? _window;
    private IInputContext? _inputContext;
    
    // Core systems
    private readonly GameLoop _gameLoop;
    private readonly ResourceManager _resourceManager;
    
    // Audio
    private AudioEngine? _audioEngine;
    
    // Debug
    private DebugDrawManager? _debugDraw;
    
    // Physics
    private PhysicsSystem? _physicsSystem;
    private GravitySystem? _gravitySystem;
    
    // Rendering
    private IGraphicsBackend? _backend;
    private GraphicsBackendType _backendType;
    
    // Legacy compatibility (will be migrated to ECS)
    private Camera? _legacyCamera;
    private InputHandler? _inputHandler;
    
    // Configuration
    public int WindowWidth { get; set; } = 1280;
    public int WindowHeight { get; set; } = 720;
    public string WindowTitle { get; set; } = "RedHole Engine";
    public bool VSync { get; set; } = true;
    
    /// <summary>
    /// Audio quality preset
    /// </summary>
    public AcousticQualitySettings AudioQuality { get; set; } = AcousticQualitySettings.Medium;

    /// <summary>
    /// The game loop manager
    /// </summary>
    public GameLoop GameLoop => _gameLoop;
    
    /// <summary>
    /// The resource manager
    /// </summary>
    public ResourceManager Resources => _resourceManager;
    
    /// <summary>
    /// The audio engine
    /// </summary>
    public AudioEngine? Audio => _audioEngine;
    
    /// <summary>
    /// Debug draw manager for visualizations
    /// </summary>
    public DebugDrawManager? DebugDraw => _debugDraw;
    
    /// <summary>
    /// Physics system for the active scene
    /// </summary>
    public PhysicsSystem? Physics => _physicsSystem;
    
    /// <summary>
    /// Current active scene
    /// </summary>
    public Scene.Scene? ActiveScene => _gameLoop.ActiveScene;
    
    /// <summary>
    /// The ECS world of the active scene
    /// </summary>
    public World? World => ActiveScene?.World;

    /// <summary>
    /// Event fired when the application is initialized
    /// </summary>
    public event Action? OnInitialize;
    
    /// <summary>
    /// Event fired each frame for custom update logic
    /// </summary>
    public event Action<float>? OnUpdate;
    
    /// <summary>
    /// Event fired when the application is shutting down
    /// </summary>
    public event Action? OnShutdown;

    public Application()
    {
        _gameLoop = new GameLoop();
        _resourceManager = new ResourceManager();
        
        // Register default loaders
        _resourceManager.RegisterLoader(new ShaderLoader());
    }

    /// <summary>
    /// Initialize and run the application
    /// </summary>
    public void Run(GraphicsBackendType backendType = GraphicsBackendType.Vulkan)
    {
        _backendType = backendType;
        
        Enginedeco.EngineTitlePrint();
        Console.WriteLine($"Using graphics backend: {backendType}");
        Console.WriteLine("Creating window...");
        
        // Platform setup for windowing
        if (backendType == GraphicsBackendType.Vulkan)
        {
            // Use GLFW for Vulkan - better cross-platform compatibility
            Silk.NET.Windowing.Glfw.GlfwWindowing.RegisterPlatform();
            Silk.NET.Input.Glfw.GlfwInput.RegisterPlatform();
            Window.PrioritizeGlfw();
        }
        
        var options = WindowOptions.Default with
        {
            Size = new Vector2D<int>(WindowWidth, WindowHeight),
            Title = WindowTitle,
            API = backendType == GraphicsBackendType.Vulkan 
                ? GraphicsAPI.DefaultVulkan 
                : new GraphicsAPI(ContextAPI.OpenGL, ContextProfile.Core, ContextFlags.ForwardCompatible, new APIVersion(4, 1)),
            VSync = VSync
        };

        _window = Window.Create(options);
        Console.WriteLine("Window created successfully");

        _window.Load += OnLoad;
        _window.Update += OnWindowUpdate;
        _window.Render += OnRender;
        _window.Closing += OnClose;
        _window.Resize += OnResize;

        Console.WriteLine("Starting window...");
        _window.Run();
    }
    


    private void OnLoad()
    {
        _inputContext = _window!.CreateInput();
        
        // Create default scene
        var scene = _gameLoop.CreateScene("Main Scene");
        
        // Initialize legacy camera (will be migrated to CameraComponent)
        _legacyCamera = new Camera(
            position: new Vector3(0, 10, 40),
            yaw: -90.0f,
            pitch: -14.0f
        );
        _legacyCamera.MovementSpeed = 10.0f;
        _legacyCamera.FieldOfView = 60.0f;

        // Initialize input
        _inputHandler = new InputHandler(_legacyCamera);
        _inputHandler.Initialize(_inputContext);

        // Create graphics backend
        _backend = _backendType switch
        {
            GraphicsBackendType.Vulkan => new VulkanBackend(_window!, WindowWidth, WindowHeight),
            GraphicsBackendType.OpenGL => throw new NotSupportedException("OpenGL backend doesn't support compute shaders on macOS"),
            _ => throw new ArgumentOutOfRangeException()
        };
        
        _backend.Initialize();

        // Initialize debug drawing
        _debugDraw = new DebugDrawManager();
        
        // Initialize physics
        _physicsSystem = new PhysicsSystem();
        _gravitySystem = new GravitySystem();
        scene.World.AddSystem(_gravitySystem);
        scene.World.AddSystem(_physicsSystem);
        _physicsSystem.SetDebugDraw(_debugDraw);
        Console.WriteLine("Physics system initialized");
        
        // Initialize audio engine
        _audioEngine = new AudioEngine(scene.World, AudioQuality);
        if (_audioEngine.Initialize())
        {
            // Add audio listener to camera position (temporary - will be ECS-based)
            var listenerEntity = scene.World.CreateEntity();
            scene.World.AddComponent(listenerEntity, new TransformComponent(new Vector3(0, 10, 40)));
            scene.World.AddAudioListener(listenerEntity);
            
            Console.WriteLine($"Audio engine initialized (Quality: {AudioQuality.RaysPerSource} rays/source)");
        }
        else
        {
            Console.WriteLine("Audio engine failed to initialize - audio disabled");
        }

        // Let user code initialize
        OnInitialize?.Invoke();

        Console.WriteLine("\n=== Controls ===");
        Console.WriteLine("WASD - Move camera");
        Console.WriteLine("Mouse - Look around");
        Console.WriteLine("Space - Move up");
        Console.WriteLine("Shift - Move down");
        Console.WriteLine("Escape - Toggle mouse capture");
        Console.WriteLine("================\n");
    }

    private void OnWindowUpdate(double deltaTime)
    {
        // Clear debug primitives from previous frame
        _debugDraw?.Clear();
        
        _inputHandler?.Update((float)deltaTime);
        _gameLoop.Update((float)deltaTime);
        _audioEngine?.Update((float)deltaTime);
        OnUpdate?.Invoke((float)deltaTime);
    }

    private void OnRender(double deltaTime)
    {
        _gameLoop.Render();
        
        // Get black hole from scene if it exists, otherwise use default
        BlackHole? blackHole = null;
        if (ActiveScene != null)
        {
            foreach (var entity in World!.Query<GravitySourceComponent, TransformComponent>())
            {
                ref var gravity = ref World.GetComponent<GravitySourceComponent>(entity);
                ref var transform = ref World.GetComponent<TransformComponent>(entity);
                
                if (gravity.GravityType == GravityType.Schwarzschild || 
                    gravity.GravityType == GravityType.Kerr)
                {
                    // Create legacy BlackHole for renderer compatibility
                    blackHole = new BlackHole(transform.Position, gravity.Mass);
                    break;
                }
            }
        }
        
        // Fallback to default if no black hole in scene
        blackHole ??= BlackHole.CreateDefault();
        
        _backend?.Render(_legacyCamera!, blackHole, Time.TotalTime, _debugDraw);
    }

    private void OnResize(Vector2D<int> newSize)
    {
        WindowWidth = newSize.X;
        WindowHeight = newSize.Y;
        _backend?.Resize(newSize.X, newSize.Y);
    }

    private void OnClose()
    {
        OnShutdown?.Invoke();
        _audioEngine?.Dispose();
        _backend?.Dispose();
        _inputHandler?.Dispose();
        _gameLoop.Shutdown();
        _resourceManager.Dispose();
    }

    #region Scene Helpers

    /// <summary>
    /// Create a black hole entity in the active scene
    /// </summary>
    public Entity CreateBlackHole(Vector3 position, float mass, bool withAccretionDisk = true)
    {
        if (World == null)
            throw new InvalidOperationException("No active scene");

        var entity = World.CreateEntity();
        
        World.AddComponent(entity, new TransformComponent(position));
        World.AddComponent(entity, GravitySourceComponent.CreateBlackHole(mass));
        
        if (withAccretionDisk)
        {
            World.AddComponent(entity, AccretionDiskComponent.CreateForBlackHole(mass));
        }

        return entity;
    }

    /// <summary>
    /// Create a camera entity
    /// </summary>
    public Entity CreateCamera(Vector3 position, float fov = 60f)
    {
        if (World == null)
            throw new InvalidOperationException("No active scene");

        var entity = World.CreateEntity();
        
        World.AddComponent(entity, new TransformComponent(position));
        World.AddComponent(entity, CameraComponent.CreatePerspective(fov, (float)WindowWidth / WindowHeight));

        return entity;
    }

    /// <summary>
    /// Create an audio source entity
    /// </summary>
    public Entity CreateAudioSource(Vector3 position, string clipId, bool autoPlay = false)
    {
        if (World == null)
            throw new InvalidOperationException("No active scene");

        var entity = World.CreateEntity();
        
        World.AddComponent(entity, new TransformComponent(position));
        World.AddAudioSource(entity, clipId, autoPlay);

        return entity;
    }

    /// <summary>
    /// Create an acoustic surface (wall, floor, etc.)
    /// </summary>
    public Entity CreateAcousticSurface(Vector3 position, AcousticMaterial material)
    {
        if (World == null)
            throw new InvalidOperationException("No active scene");

        var entity = World.CreateEntity();
        
        World.AddComponent(entity, new TransformComponent(position));
        World.AddAcousticSurface(entity, material);

        return entity;
    }

    /// <summary>
    /// Enable audio debug visualization
    /// </summary>
    public void EnableAudioDebug(AudioDebugFlags flags = AudioDebugFlags.All)
    {
        if (_audioEngine != null && _debugDraw != null)
        {
            _audioEngine.EnableDebugVisualization(_debugDraw, flags);
        }
    }

    /// <summary>
    /// Disable audio debug visualization
    /// </summary>
    public void DisableAudioDebug()
    {
        _audioEngine?.DisableDebugVisualization();
    }

    #endregion

    #region Physics Helpers

    /// <summary>
    /// Create a dynamic physics object with a sphere collider
    /// </summary>
    public Entity CreatePhysicsSphere(Vector3 position, float radius, float mass = 1f)
    {
        if (World == null)
            throw new InvalidOperationException("No active scene");

        var entity = World.CreateEntity();
        
        World.AddComponent(entity, new TransformComponent(position));
        World.AddComponent(entity, RigidBodyComponent.CreateDynamic(mass));
        World.AddComponent(entity, ColliderComponent.CreateSphere(radius));

        return entity;
    }

    /// <summary>
    /// Create a dynamic physics object with a box collider
    /// </summary>
    public Entity CreatePhysicsBox(Vector3 position, Vector3 halfExtents, float mass = 1f)
    {
        if (World == null)
            throw new InvalidOperationException("No active scene");

        var entity = World.CreateEntity();
        
        World.AddComponent(entity, new TransformComponent(position));
        World.AddComponent(entity, RigidBodyComponent.CreateDynamic(mass));
        World.AddComponent(entity, ColliderComponent.CreateBox(halfExtents));

        return entity;
    }

    /// <summary>
    /// Create a static ground plane
    /// </summary>
    public Entity CreateGroundPlane(float height = 0f)
    {
        if (World == null)
            throw new InvalidOperationException("No active scene");

        var entity = World.CreateEntity();
        
        World.AddComponent(entity, new TransformComponent(new Vector3(0, height, 0)));
        World.AddComponent(entity, RigidBodyComponent.CreateStatic());
        World.AddComponent(entity, ColliderComponent.CreateGroundPlane(height));

        return entity;
    }

    /// <summary>
    /// Create a static physics box (wall, platform, etc.)
    /// </summary>
    public Entity CreateStaticBox(Vector3 position, Vector3 halfExtents)
    {
        if (World == null)
            throw new InvalidOperationException("No active scene");

        var entity = World.CreateEntity();
        
        World.AddComponent(entity, new TransformComponent(position));
        World.AddComponent(entity, RigidBodyComponent.CreateStatic());
        World.AddComponent(entity, ColliderComponent.CreateBox(halfExtents));

        return entity;
    }

    /// <summary>
    /// Enable physics debug visualization
    /// </summary>
    public void EnablePhysicsDebug(PhysicsDebugFlags flags = PhysicsDebugFlags.Colliders | PhysicsDebugFlags.Contacts)
    {
        if (_physicsSystem != null)
        {
            _physicsSystem.DebugFlags = flags;
        }
    }

    /// <summary>
    /// Disable physics debug visualization
    /// </summary>
    public void DisablePhysicsDebug()
    {
        if (_physicsSystem != null)
        {
            _physicsSystem.DebugFlags = PhysicsDebugFlags.None;
        }
    }

    /// <summary>
    /// Cast a ray and return the first hit
    /// </summary>
    public bool Raycast(Vector3 origin, Vector3 direction, float maxDistance, out RaycastResult hit)
    {
        hit = default;
        return _physicsSystem?.Raycast(origin, direction, maxDistance, out hit) ?? false;
    }

    /// <summary>
    /// Set the gravity for the physics simulation
    /// </summary>
    public void SetGravity(Vector3 gravity)
    {
        if (_physicsSystem != null)
        {
            _physicsSystem.Settings.Gravity = gravity;
        }
    }

    /// <summary>
    /// Disable default gravity (useful when using GravitySourceComponents)
    /// </summary>
    public void DisableDefaultGravity()
    {
        SetGravity(Vector3.Zero);
    }

    #endregion

    public void Dispose()
    {
        _resourceManager.Dispose();
        _gameLoop.Shutdown();
    }
}
