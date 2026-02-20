using System.Numerics;
using RedHoleEngine.Audio;
using RedHoleEngine.Components;
using RedHoleEngine.Core.ECS;
using RedHoleEngine.Core.Scene;
using RedHoleEngine.Engine;
using RedHoleEngine.Input;
using RedHoleEngine.Input.Defaults;
using RedHoleEngine.Physics;
using RedHoleEngine.Physics.Collision;
using RedHoleEngine.Platform;
using RedHoleEngine.Profiling;
using RedHoleEngine.Rendering;
using RedHoleEngine.Rendering.Backends;
using RedHoleEngine.Rendering.Debug;
using RedHoleEngine.Rendering.Raytracing;
using RedHoleEngine.Rendering.Rasterization;
using RedHoleEngine.Rendering.UI;
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

    // Raytracing
    private RaytracerMeshSystem? _raytracerMeshSystem;
    private RasterMeshSystem? _rasterMeshSystem;
    private RenderSettingsSystem? _renderSettingsSystem;
    private UiSystem? _uiSystem;
    private UnpixSystem? _unpixSystem;
    private LaserSystem? _laserSystem;
    private LaserRenderSystem? _laserRenderSystem;
    
    // Rendering
    private IGraphicsBackend? _backend;
    private GraphicsBackendType _backendType;
    private readonly RaytracerSettings _raytracerSettings = new();
    private readonly RenderSettings _renderSettings = new();
    
    // Splash screen
    private SplashScreen? _splashScreen;
    
    // Legacy compatibility (will be migrated to ECS)
    private Camera? _legacyCamera;
    
    // New input system
    private InputManager? _inputManager;
    private InputSystem? _inputSystem;
    
    // Configuration
    public int WindowWidth { get; set; } = 1280;
    public int WindowHeight { get; set; } = 720;
    public string WindowTitle { get; set; } = "RedHole Engine";
    public bool VSync { get; set; } = true;
    
    /// <summary>
    /// Whether to start in fullscreen mode at native resolution.
    /// When true, WindowWidth and WindowHeight are ignored and native resolution is used.
    /// </summary>
    public bool Fullscreen { get; set; } = true;
    
    /// <summary>
    /// Whether to use borderless fullscreen (windowed fullscreen) instead of exclusive fullscreen.
    /// Borderless allows faster alt-tab but may have slightly higher input latency.
    /// </summary>
    public bool BorderlessFullscreen { get; set; } = true;
    
    /// <summary>
    /// Whether to show the engine splash screen on startup.
    /// Only shown when IsEditorMode is false (i.e., in compiled games).
    /// </summary>
    public bool ShowSplashScreen { get; set; } = true;
    
    /// <summary>
    /// Whether this is running in editor mode (no splash screen, development features enabled).
    /// Set to false for compiled/release games.
    /// </summary>
    public bool IsEditorMode { get; set; } = false;
    
    /// <summary>
    /// Path to the splash screen logo image
    /// </summary>
    public string SplashLogoPath { get; set; } = "Assets/Branding/redhole_logo.png";
    
    /// <summary>
    /// Duration to display the splash screen (seconds)
    /// </summary>
    public float SplashDuration { get; set; } = 2.5f;
    
    /// <summary>
    /// Audio quality preset
    /// </summary>
    public AcousticQualitySettings AudioQuality { get; set; } = AcousticQualitySettings.Medium;
    
    /// <summary>
    /// Performance profile to use. Set to Auto to detect hardware automatically.
    /// When Auto is selected, Steam Deck and other platforms are detected and appropriate settings applied.
    /// </summary>
    public PerformanceProfileType PerformanceProfile { get; set; } = PerformanceProfileType.Auto;
    
    /// <summary>
    /// Whether to automatically apply the performance profile on startup.
    /// Set to false if you want to manually configure settings.
    /// </summary>
    public bool AutoApplyPerformanceProfile { get; set; } = true;
    
    /// <summary>
    /// Whether to show profiler stats in console (every N frames)
    /// </summary>
    public int ProfilerLogInterval { get; set; } = 0; // 0 = disabled
    
    /// <summary>
    /// The profiler instance
    /// </summary>
    public Profiler Profiler => Profiler.Instance;

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
    /// The input manager for reading input state
    /// </summary>
    public InputManager? Input => _inputManager;
    
    /// <summary>
    /// Current active scene
    /// </summary>
    public Scene.Scene? ActiveScene => _gameLoop.ActiveScene;
    
    /// <summary>
    /// The ECS world of the active scene
    /// </summary>
    public World? World => ActiveScene?.World;

    /// <summary>
    /// Raytracer quality settings (rays per pixel, bounces)
    /// </summary>
    public RaytracerSettings RaytracerSettings => _backend?.RaytracerSettings ?? _raytracerSettings;

    /// <summary>
    /// Render mode settings
    /// </summary>
    public RenderSettings RenderSettings => _backend?.RenderSettings ?? _renderSettings;

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
        
        // Detect platform early for logging and pre-initialization decisions
        Console.WriteLine($"Platform: {PlatformDetector.GetPlatformDescription()}");
        if (PlatformDetector.IsSteamDeck)
        {
            Console.WriteLine("Steam Deck detected - will apply optimized settings");
        }
        
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
        
        // Get native resolution for fullscreen
        var monitor = Silk.NET.Windowing.Monitor.GetMainMonitor(null);
        var nativeRes = monitor?.VideoMode.Resolution ?? new Vector2D<int>(1920, 1080);
        
        int width = Fullscreen ? nativeRes.X : WindowWidth;
        int height = Fullscreen ? nativeRes.Y : WindowHeight;
        
        // Determine window state
        WindowState windowState = WindowState.Normal;
        WindowBorder windowBorder = WindowBorder.Resizable;
        
        if (Fullscreen)
        {
            if (BorderlessFullscreen)
            {
                // Borderless fullscreen (windowed fullscreen)
                windowState = WindowState.Fullscreen;
                windowBorder = WindowBorder.Hidden;
            }
            else
            {
                // Exclusive fullscreen
                windowState = WindowState.Fullscreen;
            }
        }
        
        var options = WindowOptions.Default with
        {
            Size = new Vector2D<int>(width, height),
            Title = WindowTitle,
            API = backendType == GraphicsBackendType.Vulkan 
                ? GraphicsAPI.DefaultVulkan 
                : new GraphicsAPI(ContextAPI.OpenGL, ContextProfile.Core, ContextFlags.ForwardCompatible, new APIVersion(4, 1)),
            VSync = VSync,
            WindowState = windowState,
            WindowBorder = windowBorder
        };
        
        // Update stored dimensions to actual resolution
        WindowWidth = width;
        WindowHeight = height;

        _window = Window.Create(options);
        Console.WriteLine($"Window created: {width}x{height} {(Fullscreen ? (BorderlessFullscreen ? "(borderless fullscreen)" : "(exclusive fullscreen)") : "(windowed)")}");

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

        // Initialize new input system
        _inputManager = new InputManager();
        _inputManager.Initialize(_inputContext);
        
        // Load default actions
        _inputManager.SetActions(DefaultActions.CreateDefault());
        _inputManager.EnableActionMap("Gameplay");
        
        // Set up cursor capture for FPS-style controls
        _inputManager.SetCursorCaptured(true);

        // Create graphics backend
        _backend = _backendType switch
        {
            GraphicsBackendType.Vulkan => new VulkanBackend(_window!, WindowWidth, WindowHeight),
            GraphicsBackendType.OpenGL => throw new NotSupportedException("OpenGL backend doesn't support compute shaders on macOS"),
            _ => throw new ArgumentOutOfRangeException()
        };
        
        _backend.Initialize();
        
        // Apply performance profile based on detected hardware
        if (AutoApplyPerformanceProfile)
        {
            if (PerformanceProfile == PerformanceProfileType.Auto)
            {
                _backend.RaytracerSettings.ApplyAutoProfile();
            }
            else
            {
                _backend.RaytracerSettings.ApplyPerformanceProfile(PerformanceProfile);
            }
        }

        // Initialize debug drawing
        _debugDraw = new DebugDrawManager();
        
        // Initialize physics
        _physicsSystem = new PhysicsSystem();
        _gravitySystem = new GravitySystem();
        scene.World.AddSystem(_gravitySystem);
        scene.World.AddSystem(_physicsSystem);
        _physicsSystem.SetDebugDraw(_debugDraw);
        Console.WriteLine("Physics system initialized");

        // Initialize raytracer mesh system (static BVH)
        _raytracerMeshSystem = new RaytracerMeshSystem();
        scene.World.AddSystem(_raytracerMeshSystem);
        if (_backend != null)
        {
            _raytracerMeshSystem.SetBackend(_backend);
        }
        if (_legacyCamera != null)
        {
            _raytracerMeshSystem.SetCamera(_legacyCamera);
        }

        // Initialize raster mesh system (primitive mode)
        _rasterMeshSystem = new RasterMeshSystem();
        scene.World.AddSystem(_rasterMeshSystem);
        if (_backend != null)
        {
            _rasterMeshSystem.SetBackend(_backend);
        }
        if (_legacyCamera != null)
        {
            _rasterMeshSystem.SetCamera(_legacyCamera);
        }

        // Render settings override system
        _renderSettingsSystem = new RenderSettingsSystem();
        scene.World.AddSystem(_renderSettingsSystem);
        if (_backend != null)
        {
            _renderSettingsSystem.SetBackend(_backend);
        }

        // UI system (2D overlay)
        _uiSystem = new UiSystem();
        scene.World.AddSystem(_uiSystem);
        _uiSystem.SetViewportSize(WindowWidth, WindowHeight);
        _uiSystem.ResourceBasePath = _resourceManager.BasePath;

        // Unpix system (mesh voxel dissolve)
        _unpixSystem = new UnpixSystem();
        _unpixSystem.Initialize(_resourceManager);
        scene.World.AddSystem(_unpixSystem);
        
        // Laser system
        _laserSystem = new LaserSystem();
        _laserSystem.Initialize(_physicsSystem!);
        scene.World.AddSystem(_laserSystem);
        
        // Laser render system
        _laserRenderSystem = new LaserRenderSystem();
        if (_backend != null)
        {
            _laserRenderSystem.SetBackend(_backend);
        }
        if (_legacyCamera != null)
        {
            _laserRenderSystem.SetCamera(_legacyCamera);
        }
        scene.World.AddSystem(_laserRenderSystem);
        
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

        // Initialize splash screen if not in editor mode
        if (ShowSplashScreen && !IsEditorMode)
        {
            _splashScreen = new SplashScreen(scene.World, WindowWidth, WindowHeight)
            {
                LogoPath = SplashLogoPath,
                DisplayDuration = SplashDuration,
                FadeInDuration = 0.4f,
                FadeOutDuration = 0.6f
            };
            _splashScreen.Show();
            Console.WriteLine("Splash screen initialized");
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
        Profiler.Instance.BeginFrame();
        
        // Update splash screen if active
        if (_splashScreen?.IsActive == true)
        {
            _splashScreen.Update((float)deltaTime);
            
            // Allow skipping splash with Space or Enter
            if (_inputContext != null)
            {
                foreach (var keyboard in _inputContext.Keyboards)
                {
                    if (keyboard.IsKeyPressed(Silk.NET.Input.Key.Space) || 
                        keyboard.IsKeyPressed(Silk.NET.Input.Key.Enter) ||
                        keyboard.IsKeyPressed(Silk.NET.Input.Key.Escape))
                    {
                        _splashScreen.Skip();
                        break;
                    }
                }
            }
            
            // Still update the ECS world for UI rendering during splash
            _gameLoop.Update((float)deltaTime);
            return; // Skip other updates during splash
        }
        
        // Clear debug primitives from previous frame
        _debugDraw?.Clear();
        
        using (Profiler.Instance.Scope("Input", "Update"))
        {
            _inputManager?.Update((float)deltaTime);
            
            // Legacy camera movement using new input system
            if (_inputManager != null && _legacyCamera != null)
            {
                // WASD movement via action system
                var moveInput = _inputManager.FindAction("Move")?.ValueVector2 ?? System.Numerics.Vector2.Zero;
                if (moveInput.Y > 0) _legacyCamera.MoveForward((float)deltaTime, forward: true);
                if (moveInput.Y < 0) _legacyCamera.MoveForward((float)deltaTime, forward: false);
                if (moveInput.X < 0) _legacyCamera.MoveRight((float)deltaTime, right: false);
                if (moveInput.X > 0) _legacyCamera.MoveRight((float)deltaTime, right: true);
                
                // Jump/Crouch for vertical movement
                if (_inputManager.FindAction("Jump")?.IsPressed == true)
                    _legacyCamera.MoveUp((float)deltaTime, up: true);
                if (_inputManager.FindAction("Sprint")?.IsPressed == true) // Using Sprint as down
                    _legacyCamera.MoveUp((float)deltaTime, up: false);
                
                // Mouse look
                var lookInput = _inputManager.GetMouseDelta();
                if (lookInput.LengthSquared() > 0.001f)
                {
                    _legacyCamera.Rotate(lookInput.X, lookInput.Y);
                }
                
                // Gyro look (additive)
                var gyroInput = _inputManager.GetGyro();
                if (gyroInput.LengthSquared() > 0.001f)
                {
                    // Gyro: Y = yaw, X = pitch
                    _legacyCamera.Rotate(gyroInput.Y * 0.1f, gyroInput.X * 0.1f);
                }
                
                // Escape to toggle mouse capture
                if (_inputManager.FindAction("Pause")?.WasPressedThisFrame == true)
                {
                    _inputManager.ToggleCursorCapture();
                }
            }
        }
        
        // End input frame
        _inputManager?.EndFrame();
        
        using (Profiler.Instance.Scope("GameLoop", "Update"))
        {
            _gameLoop.Update((float)deltaTime);
        }
        
        using (Profiler.Instance.Scope("Audio", "Update"))
        {
            _audioEngine?.Update((float)deltaTime);
        }
        
        OnUpdate?.Invoke((float)deltaTime);
    }

    private void OnRender(double deltaTime)
    {
        using (Profiler.Instance.Scope("Render", "Render"))
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
            
            if (_uiSystem != null)
            {
                _backend?.SetUiDrawData(_uiSystem.DrawData);
            }
            _backend?.Render(_legacyCamera!, blackHole, Time.TotalTime, _debugDraw);
        }
        
        Profiler.Instance.EndFrame();
        
        // Log profiler stats periodically if enabled
        if (ProfilerLogInterval > 0 && Profiler.Instance.FrameCount % ProfilerLogInterval == 0)
        {
            Console.WriteLine(Profiler.Instance.GetStatusLine());
        }
    }

    private void OnResize(Vector2D<int> newSize)
    {
        WindowWidth = newSize.X;
        WindowHeight = newSize.Y;
        _backend?.Resize(newSize.X, newSize.Y);
        _uiSystem?.SetViewportSize(newSize.X, newSize.Y);
    }

    private void OnClose()
    {
        OnShutdown?.Invoke();
        _audioEngine?.Dispose();
        _backend?.Dispose();
        _inputManager?.Dispose();
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

    #region ML Helpers

    private ML.Services.MLService? _mlService;
    private ML.Agents.MLAgentSystem? _mlAgentSystem;
    private ML.Analytics.DifficultyAdapterSystem? _difficultySystem;
    private ML.Analytics.PlayerAnalyticsSystem? _analyticsSystem;
    private ML.Analytics.AnomalyDetectionSystem? _anomalySystem;

    /// <summary>
    /// The ML service for machine learning operations
    /// </summary>
    public RedHoleEngine.ML.Services.MLService MachineLearning
    {
        get
        {
            _mlService ??= new RedHoleEngine.ML.Services.MLService();
            return _mlService;
        }
    }

    /// <summary>
    /// Initialize the ML subsystems (call after OnInitialize)
    /// </summary>
    public void InitializeML()
    {
        if (World == null)
            throw new InvalidOperationException("No active scene");

        _mlService ??= new RedHoleEngine.ML.Services.MLService();

        // Initialize ML Agent System
        _mlAgentSystem = new RedHoleEngine.ML.Agents.MLAgentSystem();
        _mlAgentSystem.SetMLService(_mlService);
        World.AddSystem(_mlAgentSystem);

        // Initialize Difficulty Adapter System
        _difficultySystem = new RedHoleEngine.ML.Analytics.DifficultyAdapterSystem();
        _difficultySystem.SetMLService(_mlService);
        World.AddSystem(_difficultySystem);

        // Initialize Player Analytics System
        _analyticsSystem = new RedHoleEngine.ML.Analytics.PlayerAnalyticsSystem();
        _analyticsSystem.SetMLService(_mlService);
        World.AddSystem(_analyticsSystem);

        // Initialize Anomaly Detection System
        _anomalySystem = new RedHoleEngine.ML.Analytics.AnomalyDetectionSystem();
        _anomalySystem.SetMLService(_mlService);
        World.AddSystem(_anomalySystem);

        Console.WriteLine("ML subsystems initialized");
    }

    /// <summary>
    /// Create an ML-controlled agent entity
    /// </summary>
    public Entity CreateMLAgent(Vector3 position, string modelId, RedHoleEngine.ML.Components.MLAgentType agentType = RedHoleEngine.ML.Components.MLAgentType.Classifier)
    {
        if (World == null)
            throw new InvalidOperationException("No active scene");

        var entity = World.CreateEntity();
        World.AddComponent(entity, new TransformComponent(position));
        World.AddComponent(entity, RedHoleEngine.ML.Components.MLAgentComponent.Create(modelId, agentType));

        return entity;
    }

    /// <summary>
    /// Create a player entity with behavior tracking for analytics
    /// </summary>
    public Entity CreateTrackedPlayer(Vector3 position, string? sessionId = null)
    {
        if (World == null)
            throw new InvalidOperationException("No active scene");

        var entity = World.CreateEntity();
        World.AddComponent(entity, new TransformComponent(position));
        World.AddComponent(entity, RedHoleEngine.ML.Components.PlayerBehaviorComponent.Create(sessionId));
        World.AddComponent(entity, RedHoleEngine.ML.Components.DifficultyComponent.Create());

        return entity;
    }

    /// <summary>
    /// Add anomaly monitoring to an entity
    /// </summary>
    public void AddAnomalyMonitor(Entity entity, string modelId, float threshold = 0.5f)
    {
        if (World == null)
            throw new InvalidOperationException("No active scene");

        World.AddComponent(entity, RedHoleEngine.ML.Components.AnomalyMonitorComponent.Create(modelId, threshold));
    }

    /// <summary>
    /// Get the ML agent system for advanced configuration
    /// </summary>
    public RedHoleEngine.ML.Agents.MLAgentSystem? MLAgentSystem => _mlAgentSystem;

    /// <summary>
    /// Get the difficulty adapter system
    /// </summary>
    public RedHoleEngine.ML.Analytics.DifficultyAdapterSystem? DifficultySystem => _difficultySystem;

    /// <summary>
    /// Get the player analytics system
    /// </summary>
    public RedHoleEngine.ML.Analytics.PlayerAnalyticsSystem? AnalyticsSystem => _analyticsSystem;

    /// <summary>
    /// Get the anomaly detection system
    /// </summary>
    public RedHoleEngine.ML.Analytics.AnomalyDetectionSystem? AnomalySystem => _anomalySystem;

    #endregion

    public void Dispose()
    {
        _mlService?.Dispose();
        _resourceManager.Dispose();
        _gameLoop.Shutdown();
    }
}
