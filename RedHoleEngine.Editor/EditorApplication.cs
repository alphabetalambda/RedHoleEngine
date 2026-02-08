using System.Numerics;
using ImGuiNET;
using RedHoleEngine.Components;
using RedHoleEngine.Core.ECS;
using RedHoleEngine.Editor.Selection;
using RedHoleEngine.Editor.UI.Panels;
using RedHoleEngine.Engine;
using RedHoleEngine.Physics;
using RedHoleEngine.Rendering;
using Silk.NET.Input;
using Silk.NET.Maths;
using Silk.NET.OpenGL;
using Silk.NET.OpenGL.Extensions.ImGui;
using Silk.NET.Windowing;

namespace RedHoleEngine.Editor;

/// <summary>
/// Main editor application with docking ImGui interface
/// </summary>
public class EditorApplication : IDisposable
{
    private IWindow? _window;
    private GL? _gl;
    private IInputContext? _input;
    private ImGuiController? _imguiController;

    // Editor state
    private readonly SelectionManager _selection = new();
    private World? _world;
    private bool _isPlaying;
    private bool _isPaused;

    // Panels
    private readonly List<EditorPanel> _panels = new();
    private SceneHierarchyPanel? _hierarchyPanel;
    private InspectorPanel? _inspectorPanel;
    private ConsolePanel? _consolePanel;
    private RaytracerSettingsPanel? _raytracerPanel;
    private RenderSettingsPanel? _renderPanel;

    private readonly RaytracerSettings _raytracerSettings = new();
    private readonly RenderSettings _renderSettings = new();

    // Viewport state
    private Renderer? _viewportRenderer;
    private Camera _viewportCamera = new(new Vector3(0f, 10f, 40f), -90f, -14f);
    private Vector2 _viewportSize;
    private float _viewportTime;
    private readonly List<GizmoLine> _gizmoLines = new();

    // Configuration
    public int WindowWidth { get; set; } = 1600;
    public int WindowHeight { get; set; } = 900;
    public string WindowTitle { get; set; } = "RedHole Engine Editor";

    /// <summary>
    /// Selection manager for editor entity selection
    /// </summary>
    public SelectionManager Selection => _selection;

    /// <summary>
    /// Current ECS world
    /// </summary>
    public World? World => _world;

    /// <summary>
    /// Whether the editor is in play mode
    /// </summary>
    public bool IsPlaying => _isPlaying;

    /// <summary>
    /// Whether play mode is paused
    /// </summary>
    public bool IsPaused => _isPaused;

    public EditorApplication()
    {
        // Create panels
        _hierarchyPanel = new SceneHierarchyPanel();
        _inspectorPanel = new InspectorPanel();
        _consolePanel = new ConsolePanel();
        _raytracerPanel = new RaytracerSettingsPanel(_raytracerSettings);
        _renderPanel = new RenderSettingsPanel(_renderSettings);

        _panels.Add(_hierarchyPanel);
        _panels.Add(_inspectorPanel);
        _panels.Add(_consolePanel);
        _panels.Add(_raytracerPanel);
        _panels.Add(_renderPanel);
    }

    /// <summary>
    /// Run the editor
    /// </summary>
    public void Run()
    {
        // Register GLFW platform
        Silk.NET.Windowing.Glfw.GlfwWindowing.RegisterPlatform();
        Silk.NET.Input.Glfw.GlfwInput.RegisterPlatform();
        Window.PrioritizeGlfw();

        var options = WindowOptions.Default with
        {
            Size = new Vector2D<int>(WindowWidth, WindowHeight),
            Title = WindowTitle,
            API = new GraphicsAPI(ContextAPI.OpenGL, ContextProfile.Core, ContextFlags.ForwardCompatible, new APIVersion(4, 1)),
            VSync = true
        };

        _window = Window.Create(options);

        _window.Load += OnLoad;
        _window.Update += OnUpdate;
        _window.Render += OnRender;
        _window.Closing += OnClosing;
        _window.Resize += OnResize;

        Console.WriteLine("Starting RedHole Engine Editor...");
        _window.Run();
    }

    private void OnLoad()
    {
        Console.WriteLine("Loading editor...");

        _gl = _window!.CreateOpenGL();
        _input = _window.CreateInput();

        // Create ImGui controller
        _imguiController = new ImGuiController(_gl, _window, _input);

        // Configure ImGui for docking
        var io = ImGui.GetIO();
        io.ConfigFlags |= ImGuiConfigFlags.DockingEnable;

        // Create a default world
        _world = new World();

        // Update panel contexts
        foreach (var panel in _panels)
        {
            panel.SetContext(_world, _selection);
        }

        // Setup keyboard shortcuts
        foreach (var keyboard in _input.Keyboards)
        {
            keyboard.KeyDown += OnKeyDown;
        }

        Console.WriteLine("Editor loaded successfully");
        Console.WriteLine("Controls: Ctrl+N = New Entity, Delete = Delete Selected, Ctrl+D = Duplicate");
    }

    private void OnUpdate(double deltaTime)
    {
        _imguiController?.Update((float)deltaTime);
        _viewportTime += (float)deltaTime;

        // Validate selection
        if (_world != null)
        {
            _selection.ValidateSelection(_world);
        }

        // Update world if playing and not paused
        if (_isPlaying && !_isPaused && _world != null)
        {
            _world.Update((float)deltaTime);
        }
    }

    private void OnRender(double deltaTime)
    {
        _gl?.ClearColor(0.1f, 0.1f, 0.12f, 1f);
        _gl?.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);

        // Begin dockspace
        BeginDockSpace();

        // Draw menu bar
        DrawMenuBar();

        // Draw toolbar
        DrawToolbar();

        // Draw panels
        foreach (var panel in _panels)
        {
            panel.Draw();
        }

        // Draw viewport (placeholder for now)
        DrawViewport();

        // End dockspace
        EndDockSpace();

        // Render ImGui
        _imguiController?.Render();
    }

    private void BeginDockSpace()
    {
        var windowFlags = ImGuiWindowFlags.MenuBar | ImGuiWindowFlags.NoDocking;
        var viewport = ImGui.GetMainViewport();

        ImGui.SetNextWindowPos(viewport.WorkPos);
        ImGui.SetNextWindowSize(viewport.WorkSize);
        ImGui.SetNextWindowViewport(viewport.ID);

        ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, 0);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 0);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);

        windowFlags |= ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoCollapse;
        windowFlags |= ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoMove;
        windowFlags |= ImGuiWindowFlags.NoBringToFrontOnFocus | ImGuiWindowFlags.NoNavFocus;

        ImGui.Begin("DockSpaceWindow", windowFlags);
        ImGui.PopStyleVar(3);

        var dockspaceId = ImGui.GetID("EditorDockspace");
        ImGui.DockSpace(dockspaceId, Vector2.Zero, ImGuiDockNodeFlags.None);
    }

    private void EndDockSpace()
    {
        ImGui.End();
    }

    private void DrawMenuBar()
    {
        if (ImGui.BeginMenuBar())
        {
            if (ImGui.BeginMenu("File"))
            {
                if (ImGui.MenuItem("New Scene", "Ctrl+N"))
                {
                    NewScene();
                }
                if (ImGui.MenuItem("Load Default Showcase"))
                {
                    LoadDefaultShowcaseScene();
                }
                if (ImGui.MenuItem("Open Scene", "Ctrl+O"))
                {
                    // TODO: Open scene dialog
                }
                if (ImGui.MenuItem("Save Scene", "Ctrl+S"))
                {
                    // TODO: Save scene
                }
                ImGui.Separator();
                if (ImGui.MenuItem("Exit", "Alt+F4"))
                {
                    _window?.Close();
                }
                ImGui.EndMenu();
            }

            if (ImGui.BeginMenu("Edit"))
            {
                if (ImGui.MenuItem("Undo", "Ctrl+Z", false, false))
                {
                    // TODO: Undo
                }
                if (ImGui.MenuItem("Redo", "Ctrl+Y", false, false))
                {
                    // TODO: Redo
                }
                ImGui.Separator();
                if (ImGui.MenuItem("Delete", "Delete", false, _selection.HasSelection))
                {
                    DeleteSelected();
                }
                if (ImGui.MenuItem("Duplicate", "Ctrl+D", false, _selection.HasSelection))
                {
                    // TODO: Duplicate
                }
                ImGui.EndMenu();
            }

            if (ImGui.BeginMenu("View"))
            {
                foreach (var panel in _panels)
                {
                    bool visible = panel.IsVisible;
                    if (ImGui.MenuItem(panel.Title, null, ref visible))
                    {
                        panel.IsVisible = visible;
                    }
                }
                ImGui.EndMenu();
            }

            if (ImGui.BeginMenu("Entity"))
            {
                if (ImGui.MenuItem("Create Empty"))
                {
                    CreateEmptyEntity();
                }
                ImGui.Separator();
                if (ImGui.MenuItem("Camera"))
                {
                    CreateCamera();
                }
                if (ImGui.MenuItem("Black Hole"))
                {
                    CreateBlackHole();
                }
                if (ImGui.BeginMenu("Physics"))
                {
                    if (ImGui.MenuItem("Sphere")) CreatePhysicsSphere();
                    if (ImGui.MenuItem("Box")) CreatePhysicsBox();
                    if (ImGui.MenuItem("Ground Plane")) CreateGroundPlane();
                    ImGui.EndMenu();
                }
                ImGui.EndMenu();
            }

            if (ImGui.BeginMenu("Help"))
            {
                if (ImGui.MenuItem("About"))
                {
                    // TODO: About dialog
                }
                ImGui.EndMenu();
            }

            ImGui.EndMenuBar();
        }
    }

    private void DrawToolbar()
    {
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(8, 4));
        
        if (ImGui.Begin("Toolbar", ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoScrollbar))
        {
            // Play/Pause/Stop controls
            var buttonSize = new Vector2(80, 24);

            // Play button
            if (_isPlaying)
            {
                ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.2f, 0.5f, 0.2f, 1f));
            }
            if (ImGui.Button(_isPlaying ? "Stop" : "Play", buttonSize))
            {
                if (_isPlaying)
                    Stop();
                else
                    Play();
            }
            if (_isPlaying)
            {
                ImGui.PopStyleColor();
            }

            ImGui.SameLine();

            // Pause button
            ImGui.BeginDisabled(!_isPlaying);
            if (_isPaused)
            {
                ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.5f, 0.5f, 0.2f, 1f));
            }
            if (ImGui.Button(_isPaused ? "Resume" : "Pause", buttonSize))
            {
                _isPaused = !_isPaused;
            }
            if (_isPaused)
            {
                ImGui.PopStyleColor();
            }
            ImGui.EndDisabled();

            ImGui.SameLine();
            ImGui.Separator();
            ImGui.SameLine();

            if (ImGui.Button("Showcase", new Vector2(100, 24)))
            {
                LoadDefaultShowcaseScene();
            }

            // Status
            if (_isPlaying)
            {
                var color = _isPaused 
                    ? new Vector4(1f, 0.9f, 0.4f, 1f) 
                    : new Vector4(0.4f, 1f, 0.4f, 1f);
                ImGui.TextColored(color, _isPaused ? "PAUSED" : "PLAYING");
            }
            else
            {
                ImGui.TextDisabled("EDITOR");
            }
        }
        ImGui.End();
        
        ImGui.PopStyleVar();
    }

    private void DrawViewport()
    {
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);
        
        if (ImGui.Begin("Viewport"))
        {
            var size = ImGui.GetContentRegionAvail();
            if (size.X > 1 && size.Y > 1)
            {
                UpdateViewportCamera(ImGui.IsWindowHovered(ImGuiHoveredFlags.AllowWhenBlockedByActiveItem));
                EnsureViewportRenderer(size);

                if (_viewportRenderer != null)
                {
                    ApplyRaytracerSettings(_viewportRenderer.RaytracerSettings);
                    var blackHole = GetPreviewBlackHole();
                    _viewportRenderer.RenderToTexture(_viewportCamera, blackHole, _viewportTime);
                }

                var imageMin = ImGui.GetCursorScreenPos();
                ImGui.Image(_viewportRenderer != null
                    ? (IntPtr)_viewportRenderer.OutputTextureId
                    : IntPtr.Zero,
                    size,
                    new Vector2(0, 1),
                    new Vector2(1, 0));
                var imageMax = imageMin + size;

                var drawList = ImGui.GetWindowDrawList();
                DrawGizmoOverlay(drawList, imageMin, imageMax);

                var overlayText = "Viewport  |  RMB drag to orbit, wheel to zoom";
                var overlayPos = new Vector2(imageMin.X + 8f, imageMax.Y - 22f);
                drawList.AddText(overlayPos, ImGui.GetColorU32(new Vector4(0.7f, 0.75f, 0.85f, 0.8f)), overlayText);
            }
        }
        ImGui.End();
        
        ImGui.PopStyleVar();
    }

    private struct GizmoLine
    {
        public Vector3 Start;
        public Vector3 End;
        public uint Color;
        public float Thickness;
    }

    private void UpdateViewportCamera(bool hovered)
    {
        var io = ImGui.GetIO();

        if (hovered)
        {
            if (ImGui.IsMouseDown(ImGuiMouseButton.Right))
            {
                _viewportCamera.Rotate(io.MouseDelta.X, io.MouseDelta.Y);
            }

            if (MathF.Abs(io.MouseWheel) > 0.001f)
            {
                _viewportCamera.Position += _viewportCamera.Forward * (io.MouseWheel * 2f);
            }

            if (ImGui.IsMouseDown(ImGuiMouseButton.Middle))
            {
                _viewportCamera.Position -= _viewportCamera.Right * (io.MouseDelta.X * 0.02f);
                _viewportCamera.Position += _viewportCamera.Up * (io.MouseDelta.Y * 0.02f);
            }
        }
    }

    private void EnsureViewportRenderer(Vector2 size)
    {
        int width = Math.Max(1, (int)size.X);
        int height = Math.Max(1, (int)size.Y);

        if (_viewportRenderer == null)
        {
            _viewportRenderer = new Renderer(_gl!, width, height);
            _viewportSize = size;
            return;
        }

        if (Math.Abs(_viewportSize.X - size.X) > 0.5f || Math.Abs(_viewportSize.Y - size.Y) > 0.5f)
        {
            _viewportRenderer.Resize(width, height);
            _viewportSize = size;
        }
    }

    private void ApplyRaytracerSettings(RaytracerSettings target)
    {
        bool reset = _raytracerSettings.ResetAccumulation;
        target.RaysPerPixel = _raytracerSettings.RaysPerPixel;
        target.MaxBounces = _raytracerSettings.MaxBounces;
        target.SamplesPerFrame = _raytracerSettings.SamplesPerFrame;
        target.Accumulate = _raytracerSettings.Accumulate;
        target.Denoise = _raytracerSettings.Denoise;
        target.Preset = _raytracerSettings.Preset;
        target.MaxRaysPerPixelLimit = _raytracerSettings.MaxRaysPerPixelLimit;
        target.MaxBouncesLimit = _raytracerSettings.MaxBouncesLimit;
        target.MaxSamplesPerFrameLimit = _raytracerSettings.MaxSamplesPerFrameLimit;
        target.ResetAccumulation = reset;
        if (reset)
            _raytracerSettings.ResetAccumulation = false;
    }

    private BlackHole GetPreviewBlackHole()
    {
        if (_world != null)
        {
            foreach (var entity in _world.Query<GravitySourceComponent, TransformComponent>())
            {
                ref var gravity = ref _world.GetComponent<GravitySourceComponent>(entity);
                ref var transform = ref _world.GetComponent<TransformComponent>(entity);

                if (gravity.GravityType == GravityType.Schwarzschild || gravity.GravityType == GravityType.Kerr)
                {
                    return new BlackHole(transform.Position, gravity.Mass);
                }
            }
        }

        return BlackHole.CreateDefault();
    }

    private void DrawGizmoOverlay(ImDrawListPtr drawList, Vector2 imageMin, Vector2 imageMax)
    {
        _gizmoLines.Clear();

        if (_world != null && _selection.HasSelection)
        {
            var primary = _selection.PrimarySelection;
            if (_world.IsAlive(primary) && _world.HasComponent<TransformComponent>(primary))
            {
                ref var transform = ref _world.GetComponent<TransformComponent>(primary);
                var origin = transform.Position;
                float axisLength = 1.5f;

                AddGizmoAxis(origin, Vector3.UnitX, axisLength, new Vector4(0.95f, 0.3f, 0.3f, 1f));
                AddGizmoAxis(origin, Vector3.UnitY, axisLength, new Vector4(0.3f, 0.95f, 0.35f, 1f));
                AddGizmoAxis(origin, Vector3.UnitZ, axisLength, new Vector4(0.3f, 0.55f, 1f, 1f));
            }
        }

        if (_gizmoLines.Count == 0)
            return;

        var viewportSize = imageMax - imageMin;
        float aspect = viewportSize.X / Math.Max(1f, viewportSize.Y);
        var view = _viewportCamera.GetViewMatrix();
        var projection = Matrix4x4.CreatePerspectiveFieldOfView(
            _viewportCamera.FieldOfView * MathF.PI / 180f,
            aspect,
            0.1f,
            10000f);
        var viewProj = view * projection;

        foreach (var line in _gizmoLines)
        {
            if (TryProject(line.Start, viewProj, imageMin, viewportSize, out var screenStart) &&
                TryProject(line.End, viewProj, imageMin, viewportSize, out var screenEnd))
            {
                drawList.AddLine(screenStart, screenEnd, line.Color, line.Thickness);
            }
        }
    }

    private void AddGizmoAxis(Vector3 origin, Vector3 axis, float length, Vector4 color)
    {
        _gizmoLines.Add(new GizmoLine
        {
            Start = new Vector3(origin.X, origin.Y, origin.Z),
            End = origin + axis * length,
            Color = ImGui.GetColorU32(color),
            Thickness = 2.5f
        });
    }

    private bool TryProject(Vector3 world, Matrix4x4 viewProj, Vector2 viewportMin, Vector2 viewportSize, out Vector2 screen)
    {
        var clip = Vector4.Transform(new Vector4(world, 1f), viewProj);
        if (clip.W <= 0.0001f)
        {
            screen = default;
            return false;
        }

        var ndc = new Vector3(clip.X, clip.Y, clip.Z) / clip.W;
        var x = (ndc.X * 0.5f + 0.5f) * viewportSize.X + viewportMin.X;
        var y = (1f - (ndc.Y * 0.5f + 0.5f)) * viewportSize.Y + viewportMin.Y;
        screen = new Vector2(x, y);
        return ndc.Z >= -1f && ndc.Z <= 1f;
    }

    private void OnKeyDown(IKeyboard keyboard, Key key, int scancode)
    {
        var ctrl = keyboard.IsKeyPressed(Key.ControlLeft) || keyboard.IsKeyPressed(Key.ControlRight);

        switch (key)
        {
            case Key.Delete:
                DeleteSelected();
                break;
            case Key.D when ctrl:
                // TODO: Duplicate
                break;
            case Key.N when ctrl:
                CreateEmptyEntity();
                break;
        }
    }

    private void OnResize(Vector2D<int> size)
    {
        WindowWidth = size.X;
        WindowHeight = size.Y;
        _gl?.Viewport(size);
    }

    private void OnClosing()
    {
        Console.WriteLine("Closing editor...");
        
        // Dispose ImGui while OpenGL context is still valid
        _imguiController?.Dispose();
        _imguiController = null;

        _viewportRenderer?.Dispose();
        _viewportRenderer = null;
    }

    #region Play Mode

    private void Play()
    {
        _isPlaying = true;
        _isPaused = false;
        Console.WriteLine("Entered play mode");
    }

    private void Stop()
    {
        _isPlaying = false;
        _isPaused = false;
        Console.WriteLine("Exited play mode");
    }

    #endregion

    #region Entity Creation

    private void NewScene()
    {
        ResetWorld();
        Console.WriteLine("Created new scene");
    }

    private void LoadDefaultShowcaseScene()
    {
        ResetWorld();
        if (_world == null) return;

        var blackHole = _world.CreateEntity();
        _world.AddComponent(blackHole, new TransformComponent(Vector3.Zero));
        _world.AddComponent(blackHole, GravitySourceComponent.CreateBlackHole(2.2f));
        _world.AddComponent(blackHole, AccretionDiskComponent.CreateForBlackHole(2.2f));

        var ground = _world.CreateEntity();
        _world.AddComponent(ground, new TransformComponent(new Vector3(0f, -2f, 0f)));
        _world.AddComponent(ground, RigidBodyComponent.CreateStatic());
        _world.AddComponent(ground, ColliderComponent.CreateGroundPlane(0f));

        CreateStaticBox(new Vector3(-8f, 0.5f, -8f), new Vector3(2f, 5f, 2f));
        CreateStaticBox(new Vector3(8f, 0f, -8f), new Vector3(2f, 4f, 2f));
        CreateStaticBox(new Vector3(0f, -0.5f, 10f), new Vector3(3f, 3f, 3f));

        CreateDynamicSphere(new Vector3(-3f, 6f, -2f), 0.6f);
        CreateDynamicSphere(new Vector3(2f, 8f, -1f), 0.5f);
        CreateDynamicBox(new Vector3(4f, 7f, 2f), new Vector3(0.8f, 0.8f, 0.8f));
        CreateDynamicBox(new Vector3(-5f, 9f, 3f), new Vector3(1f, 0.6f, 1.2f));

        Console.WriteLine("Loaded default showcase scene");
    }

    private void ResetWorld()
    {
        _selection.ClearSelection();
        _world?.Dispose();
        _world = new World();
        _viewportCamera = new Camera(new Vector3(0f, 10f, 40f), -90f, -14f);
        _viewportTime = 0f;

        foreach (var panel in _panels)
        {
            panel.SetContext(_world, _selection);
        }
    }

    private void DeleteSelected()
    {
        if (_world == null) return;

        var toDelete = _selection.SelectedEntities.ToList();
        _selection.ClearSelection();

        foreach (var entity in toDelete)
        {
            if (_world.IsAlive(entity))
            {
                _world.DestroyEntity(entity);
            }
        }
    }

    private void CreateEmptyEntity()
    {
        if (_world == null) return;

        var entity = _world.CreateEntity();
        _world.AddComponent(entity, new RedHoleEngine.Components.TransformComponent());
        _selection.Select(entity);
    }

    private void CreateCamera()
    {
        if (_world == null) return;

        var entity = _world.CreateEntity();
        _world.AddComponent(entity, new RedHoleEngine.Components.TransformComponent(new Vector3(0, 5, 10)));
        _world.AddComponent(entity, RedHoleEngine.Components.CameraComponent.CreatePerspective(60f, 16f / 9f));
        _selection.Select(entity);
    }

    private void CreateBlackHole()
    {
        if (_world == null) return;

        var entity = _world.CreateEntity();
        _world.AddComponent(entity, new RedHoleEngine.Components.TransformComponent());
        _world.AddComponent(entity, RedHoleEngine.Components.GravitySourceComponent.CreateBlackHole(1e6f));
        _selection.Select(entity);
    }

    private void CreatePhysicsSphere()
    {
        if (_world == null) return;

        var entity = _world.CreateEntity();
        _world.AddComponent(entity, new RedHoleEngine.Components.TransformComponent(new Vector3(0, 5, 0)));
        _world.AddComponent(entity, RedHoleEngine.Components.RigidBodyComponent.CreateDynamic(1f));
        _world.AddComponent(entity, RedHoleEngine.Components.ColliderComponent.CreateSphere(0.5f));
        _selection.Select(entity);
    }

    private void CreatePhysicsBox()
    {
        if (_world == null) return;

        var entity = _world.CreateEntity();
        _world.AddComponent(entity, new RedHoleEngine.Components.TransformComponent(new Vector3(0, 5, 0)));
        _world.AddComponent(entity, RedHoleEngine.Components.RigidBodyComponent.CreateDynamic(1f));
        _world.AddComponent(entity, RedHoleEngine.Components.ColliderComponent.CreateBox(new Vector3(0.5f)));
        _selection.Select(entity);
    }

    private void CreateGroundPlane()
    {
        if (_world == null) return;

        var entity = _world.CreateEntity();
        _world.AddComponent(entity, new RedHoleEngine.Components.TransformComponent());
        _world.AddComponent(entity, RedHoleEngine.Components.RigidBodyComponent.CreateStatic());
        _world.AddComponent(entity, RedHoleEngine.Components.ColliderComponent.CreateGroundPlane());
        _selection.Select(entity);
    }

    private void CreateStaticBox(Vector3 position, Vector3 size)
    {
        if (_world == null) return;

        var entity = _world.CreateEntity();
        _world.AddComponent(entity, new TransformComponent(position));
        _world.AddComponent(entity, RigidBodyComponent.CreateStatic());
        _world.AddComponent(entity, ColliderComponent.CreateBox(size * 0.5f));
    }

    private void CreateDynamicSphere(Vector3 position, float radius)
    {
        if (_world == null) return;

        var entity = _world.CreateEntity();
        _world.AddComponent(entity, new TransformComponent(position));
        _world.AddComponent(entity, RigidBodyComponent.CreateDynamic(1.2f));
        _world.AddComponent(entity, ColliderComponent.CreateSphere(radius));
    }

    private void CreateDynamicBox(Vector3 position, Vector3 size)
    {
        if (_world == null) return;

        var entity = _world.CreateEntity();
        _world.AddComponent(entity, new TransformComponent(position));
        _world.AddComponent(entity, RigidBodyComponent.CreateDynamic(1.5f));
        _world.AddComponent(entity, ColliderComponent.CreateBox(size * 0.5f));
    }

    #endregion

    public void Dispose()
    {
        // ImGui is disposed in OnClosing while GL context is valid
        _input?.Dispose();
        _viewportRenderer?.Dispose();
        _world?.Dispose();
        // Window and GL are managed by Silk.NET, no need to dispose here
    }
}
