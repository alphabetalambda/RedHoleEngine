using System.Numerics;
using ImGuiNET;
using RedHoleEngine.Core.ECS;
using RedHoleEngine.Editor.Selection;
using RedHoleEngine.Editor.UI.Panels;
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

        _panels.Add(_hierarchyPanel);
        _panels.Add(_inspectorPanel);
        _panels.Add(_consolePanel);
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
            
            // Placeholder - in the future, render the engine to a framebuffer
            // and display it here as an ImGui image
            ImGui.PushStyleColor(ImGuiCol.ChildBg, new Vector4(0.05f, 0.05f, 0.08f, 1f));
            ImGui.BeginChild("ViewportContent", size, ImGuiChildFlags.None, ImGuiWindowFlags.NoScrollbar);
            
            // Center text
            var text = "Viewport - Engine rendering will appear here";
            var textSize = ImGui.CalcTextSize(text);
            var cursorPos = (size - textSize) * 0.5f;
            ImGui.SetCursorPos(cursorPos);
            ImGui.TextDisabled(text);
            
            ImGui.EndChild();
            ImGui.PopStyleColor();
        }
        ImGui.End();
        
        ImGui.PopStyleVar();
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
        _selection.ClearSelection();
        _world?.Dispose();
        _world = new World();

        foreach (var panel in _panels)
        {
            panel.SetContext(_world, _selection);
        }

        Console.WriteLine("Created new scene");
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

    #endregion

    public void Dispose()
    {
        // ImGui is disposed in OnClosing while GL context is valid
        _input?.Dispose();
        _world?.Dispose();
        // Window and GL are managed by Silk.NET, no need to dispose here
    }
}
