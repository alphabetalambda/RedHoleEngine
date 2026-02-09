using System;
using System.IO;
using System.Numerics;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using ImGuiNET;
using RedHoleEngine.Components;
using RedHoleEngine.Core.ECS;
using RedHoleEngine.Editor.Selection;
using RedHoleEngine.Editor.UI.Panels;
using RedHoleEngine.Engine;
using RedHoleEngine.Game;
using RedHoleEngine.Physics;
using RedHoleEngine.Rendering;
using RedHoleEngine.Rendering.Backends;
using RedHoleEngine.Resources;
using RedHoleEngine.Serialization;
using RedHoleEngine.Terminal;
using RedHoleEngine.Editor.Commands;
using RedHoleEngine.Editor.Gizmos;
using RedHoleEngine.Editor.Project;
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
    private readonly UndoRedoManager _undoRedo = new();
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
    private GameProjectPanel? _gameProjectPanel;
    private TerminalPanel? _terminalPanel;
    private AssetBrowserPanel? _assetBrowserPanel;

    private readonly RaytracerSettings _raytracerSettings = new();
    private readonly RenderSettings _renderSettings = new();
    private EditorSettings _editorSettings = new();
    private readonly ResourceManager _gameResources = new();
    private readonly SceneSerializer _sceneSerializer = new();
    private readonly ProjectManager _projectManager = new();
    private IGameModule? _gameModule;
    private string _currentScenePath = string.Empty;
    private VirtualFileSystem _terminalFileSystem = new();
    private TerminalSession _terminalSession;
    private GameSaveManager _terminalSaveManager;
    
    // File dialogs
    private readonly UI.FileDialog _fileDialog = new();

    // Viewport state
    private Renderer? _viewportRenderer;
    private VulkanBackend? _viewportVulkanBackend;
    private IWindow? _vulkanViewportWindow;
    private ViewportBackendMode _viewportBackendMode = ViewportBackendMode.Vulkan;
    private bool _vulkanPreviewFailed;
    private bool _openglPreviewFailed;
    private bool _hasSavedCamera;
    private bool _loadShowcaseOnStart = true;
    private uint _viewportPreviewTexture;
    private Vector2 _viewportPreviewSize;
    private byte[] _viewportReadbackBuffer = Array.Empty<byte>();
    private Camera _viewportCamera = new(new Vector3(0f, 10f, 40f), -90f, -14f);
    private Vector2 _viewportSize;
    private float _viewportTime;
    private readonly List<GizmoLine> _gizmoLines = new();
    private readonly TransformGizmo _transformGizmo = new();

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
        _terminalSession = new TerminalSession(_terminalFileSystem);
        _terminalSaveManager = new GameSaveManager("Editor");

        // Create panels
        _hierarchyPanel = new SceneHierarchyPanel();
        _inspectorPanel = new InspectorPanel();
        _consolePanel = new ConsolePanel();
        _raytracerPanel = new RaytracerSettingsPanel(_raytracerSettings);
        _renderPanel = new RenderSettingsPanel(_renderSettings);
        _gameProjectPanel = new GameProjectPanel(_projectManager, LoadGameProjectFromSettings);
        _terminalPanel = new TerminalPanel(_terminalSession, () => _terminalSaveManager);
        _assetBrowserPanel = new AssetBrowserPanel(
            () => _projectManager.HasProject ? _projectManager.GetAssetPath() : "",
            path => LoadScene(path));

        _panels.Add(_hierarchyPanel);
        _panels.Add(_inspectorPanel);
        _panels.Add(_consolePanel);
        _panels.Add(_raytracerPanel);
        _panels.Add(_renderPanel);
        _panels.Add(_gameProjectPanel);
        _panels.Add(_terminalPanel);
        _panels.Add(_assetBrowserPanel);
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

        LoadEditorSettings();

        // Update panel contexts
        foreach (var panel in _panels)
        {
            panel.SetContext(_world, _selection);
        }

        if (_loadShowcaseOnStart)
        {
            LoadActiveScene();
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

        // Draw file dialog (modal, on top)
        DrawFileDialog();

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
                    OpenSceneDialog();
                }
                if (ImGui.MenuItem("Save Scene", "Ctrl+S"))
                {
                    SaveSceneDialog();
                }
                if (ImGui.MenuItem("Save Scene As...", "Ctrl+Shift+S"))
                {
                    SaveSceneAsDialog();
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
                var undoText = _undoRedo.CanUndo ? $"Undo {_undoRedo.UndoDescription}" : "Undo";
                if (ImGui.MenuItem(undoText, "Ctrl+Z", false, _undoRedo.CanUndo))
                {
                    _undoRedo.Undo();
                }
                var redoText = _undoRedo.CanRedo ? $"Redo {_undoRedo.RedoDescription}" : "Redo";
                if (ImGui.MenuItem(redoText, "Ctrl+Y", false, _undoRedo.CanRedo))
                {
                    _undoRedo.Redo();
                }
                ImGui.Separator();
                if (ImGui.MenuItem("Delete", "Delete", false, _selection.HasSelection))
                {
                    DeleteSelected();
                }
                if (ImGui.MenuItem("Duplicate", "Ctrl+D", false, _selection.HasSelection))
                {
                    DuplicateSelected();
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

            // Gizmo mode buttons
            var gizmoButtonSize = new Vector2(24, 24);
            
            if (_transformGizmo.Mode == GizmoMode.Translate)
                ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.3f, 0.5f, 0.7f, 1f));
            if (ImGui.Button("W", gizmoButtonSize))
                _transformGizmo.Mode = GizmoMode.Translate;
            if (_transformGizmo.Mode == GizmoMode.Translate)
                ImGui.PopStyleColor();
            if (ImGui.IsItemHovered()) ImGui.SetTooltip("Move (W)");

            ImGui.SameLine(0, 2);

            if (_transformGizmo.Mode == GizmoMode.Rotate)
                ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.3f, 0.5f, 0.7f, 1f));
            if (ImGui.Button("E", gizmoButtonSize))
                _transformGizmo.Mode = GizmoMode.Rotate;
            if (_transformGizmo.Mode == GizmoMode.Rotate)
                ImGui.PopStyleColor();
            if (ImGui.IsItemHovered()) ImGui.SetTooltip("Rotate (E)");

            ImGui.SameLine(0, 2);

            if (_transformGizmo.Mode == GizmoMode.Scale)
                ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.3f, 0.5f, 0.7f, 1f));
            if (ImGui.Button("R", gizmoButtonSize))
                _transformGizmo.Mode = GizmoMode.Scale;
            if (_transformGizmo.Mode == GizmoMode.Scale)
                ImGui.PopStyleColor();
            if (ImGui.IsItemHovered()) ImGui.SetTooltip("Scale (R)");

            ImGui.SameLine();
            ImGui.Separator();
            ImGui.SameLine();

            if (ImGui.Button("Showcase", new Vector2(100, 24)))
            {
                LoadActiveScene();
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
                bool hovered = ImGui.IsWindowHovered(ImGuiHoveredFlags.AllowWhenBlockedByActiveItem);
                UpdateViewportCamera(hovered);
                EnsureViewportCameraValid();

                var blackHole = GetPreviewBlackHole();
                var imageMin = ImGui.GetCursorScreenPos();

                if (_viewportBackendMode == ViewportBackendMode.Vulkan && !_vulkanPreviewFailed)
                {
                    EnsureVulkanViewport(size);
                    if (_viewportVulkanBackend != null)
                    {
                        ApplyRaytracerSettings(_viewportVulkanBackend.RaytracerSettings);
                        EnsureViewportPreviewTexture(size);
                        EnsureViewportReadbackBuffer(size);
                        _viewportVulkanBackend.RenderToReadback(_viewportCamera, blackHole, _viewportTime, _viewportReadbackBuffer);
                        UpdateViewportPreviewTexture(size, _viewportReadbackBuffer);
                        ImGui.Image((IntPtr)_viewportPreviewTexture, size, new Vector2(0, 1), new Vector2(1, 0));
                    }
                    else
                    {
                        ImGui.Image(IntPtr.Zero, size);
                    }
                }
                else
                {
                    EnsureViewportRenderer(size);
                    if (_viewportRenderer != null)
                    {
                        ApplyRaytracerSettings(_viewportRenderer.RaytracerSettings);
                        _viewportRenderer.RenderToTexture(_viewportCamera, blackHole, _viewportTime);
                        ImGui.Image((IntPtr)_viewportRenderer.OutputTextureId, size, new Vector2(0, 1), new Vector2(1, 0));
                    }
                    else
                    {
                        ImGui.Image(IntPtr.Zero, size);
                    }
                }
                var imageMax = imageMin + size;

                var drawList = ImGui.GetWindowDrawList();
                
                // Update and draw transform gizmo
                UpdateTransformGizmo(imageMin, size, hovered);
                if (_world != null)
                {
                    _transformGizmo.Draw(drawList, _world, _selection, _viewportCamera, imageMin, size);
                }

                if (_viewportBackendMode == ViewportBackendMode.OpenGL && _openglPreviewFailed)
                {
                    DrawViewportFallbackMessage(drawList, imageMin, imageMax, "OpenGL compute shaders unavailable");
                }
                if (_viewportBackendMode == ViewportBackendMode.Vulkan && _vulkanPreviewFailed)
                {
                    DrawViewportFallbackMessage(drawList, imageMin, imageMax, "Vulkan preview unavailable");
                }

                DrawViewportOverlayUi(imageMin, size);

                // Show gizmo mode and controls
                var modeStr = _transformGizmo.Mode switch
                {
                    GizmoMode.Translate => "Move (W)",
                    GizmoMode.Rotate => "Rotate (E)",
                    GizmoMode.Scale => "Scale (R)",
                    _ => "Move"
                };
                var overlayText = $"Viewport  |  {modeStr}  |  RMB orbit, MMB pan, Wheel zoom";
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

    private enum ViewportBackendMode
    {
        Vulkan,
        OpenGL
    }

    private void UpdateViewportCamera(bool hovered)
    {
        // Don't update camera if gizmo is being dragged
        if (_transformGizmo.IsDragging)
            return;

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

    private void UpdateTransformGizmo(Vector2 viewportMin, Vector2 viewportSize, bool hovered)
    {
        if (_world == null) return;

        var io = ImGui.GetIO();
        var mousePos = io.MousePos;
        
        // Only process gizmo input when viewport is hovered or gizmo is being dragged
        bool shouldProcess = hovered || _transformGizmo.IsDragging;
        
        // Don't interfere with right-click camera orbit
        if (ImGui.IsMouseDown(ImGuiMouseButton.Right))
            shouldProcess = false;

        if (shouldProcess)
        {
            _transformGizmo.Update(
                _world,
                _selection,
                _undoRedo,
                _viewportCamera,
                viewportMin,
                viewportSize,
                mousePos,
                ImGui.IsMouseDown(ImGuiMouseButton.Left),
                ImGui.IsMouseClicked(ImGuiMouseButton.Left),
                ImGui.IsMouseReleased(ImGuiMouseButton.Left));
        }
    }

    private void EnsureViewportRenderer(Vector2 size)
    {
        if (_openglPreviewFailed)
            return;

        if (!IsOpenGLComputeSupported())
        {
            _openglPreviewFailed = true;
            return;
        }

        int width = Math.Max(1, (int)size.X);
        int height = Math.Max(1, (int)size.Y);

        if (_viewportRenderer == null)
        {
            try
            {
                _viewportRenderer = new Renderer(_gl!, width, height);
                _viewportSize = size;
                _openglPreviewFailed = false;
                return;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"OpenGL viewport init failed: {ex.Message}");
                _viewportRenderer = null;
                _openglPreviewFailed = true;
                return;
            }
        }

        if (Math.Abs(_viewportSize.X - size.X) > 0.5f || Math.Abs(_viewportSize.Y - size.Y) > 0.5f)
        {
            _viewportRenderer.Resize(width, height);
            _viewportSize = size;
        }
    }

    private bool IsOpenGLComputeSupported()
    {
        if (_gl == null)
            return false;

        try
        {
            int major = _gl.GetInteger(GLEnum.MajorVersion);
            int minor = _gl.GetInteger(GLEnum.MinorVersion);
            if (major > 4 || (major == 4 && minor >= 3))
                return true;
        }
        catch
        {
            return false;
        }

        return false;
    }

    private void EnsureVulkanViewport(Vector2 size)
    {
        int width = Math.Max(1, (int)size.X);
        int height = Math.Max(1, (int)size.Y);

        if (_window == null)
        {
            _vulkanPreviewFailed = true;
            _viewportBackendMode = ViewportBackendMode.OpenGL;
            return;
        }

        if (_vulkanViewportWindow == null || Math.Abs(_viewportSize.X - size.X) > 0.5f || Math.Abs(_viewportSize.Y - size.Y) > 0.5f)
        {
            _viewportVulkanBackend?.Dispose();
            _viewportVulkanBackend = null;
            _vulkanViewportWindow?.Dispose();
            _vulkanViewportWindow = null;

            try
            {
                var options = WindowOptions.DefaultVulkan with
                {
                    Size = new Vector2D<int>(width, height),
                    Title = "RedHole Preview",
                    IsVisible = false,
                    WindowBorder = WindowBorder.Hidden
                };

                _vulkanViewportWindow = Window.Create(options);
                _vulkanViewportWindow.Initialize();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Vulkan preview window init failed: {ex.Message}");
                _vulkanViewportWindow?.Dispose();
                _vulkanViewportWindow = null;
                _vulkanPreviewFailed = true;
                _viewportBackendMode = ViewportBackendMode.OpenGL;
                return;
            }
        }

        if (_vulkanViewportWindow.VkSurface == null)
        {
            _vulkanPreviewFailed = true;
            _viewportBackendMode = ViewportBackendMode.OpenGL;
            return;
        }

        if (_viewportVulkanBackend != null && Math.Abs(_viewportSize.X - size.X) <= 0.5f && Math.Abs(_viewportSize.Y - size.Y) <= 0.5f)
            return;

        _viewportVulkanBackend?.Dispose();
        _viewportVulkanBackend = null;

        try
        {
            _viewportVulkanBackend = new VulkanBackend(_vulkanViewportWindow, width, height);
            _viewportVulkanBackend.Initialize();
            _viewportSize = size;
            _vulkanPreviewFailed = false;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Vulkan viewport init failed: {ex}");
            _viewportVulkanBackend?.Dispose();
            _viewportVulkanBackend = null;
            _viewportBackendMode = ViewportBackendMode.OpenGL;
            _vulkanPreviewFailed = true;
        }
    }

    private unsafe void EnsureViewportPreviewTexture(Vector2 size)
    {
        int width = Math.Max(1, (int)size.X);
        int height = Math.Max(1, (int)size.Y);

        if (_viewportPreviewTexture == 0)
        {
            _viewportPreviewTexture = _gl!.GenTexture();
            _gl.BindTexture(TextureTarget.Texture2D, _viewportPreviewTexture);
            _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)GLEnum.Linear);
            _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)GLEnum.Linear);
            _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)GLEnum.ClampToEdge);
            _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)GLEnum.ClampToEdge);
            _viewportPreviewSize = Vector2.Zero;
        }

        if (Math.Abs(_viewportPreviewSize.X - size.X) > 0.5f || Math.Abs(_viewportPreviewSize.Y - size.Y) > 0.5f)
        {
            _gl!.BindTexture(TextureTarget.Texture2D, _viewportPreviewTexture);
            _gl.TexImage2D(TextureTarget.Texture2D, 0, InternalFormat.Rgba8, (uint)width, (uint)height, 0, PixelFormat.Rgba, PixelType.UnsignedByte, (void*)0);
            _viewportPreviewSize = size;
        }
    }

    private void EnsureViewportReadbackBuffer(Vector2 size)
    {
        int width = Math.Max(1, (int)size.X);
        int height = Math.Max(1, (int)size.Y);
        int required = width * height * 4;
        if (_viewportReadbackBuffer.Length != required)
        {
            _viewportReadbackBuffer = new byte[required];
        }
    }

    private unsafe void UpdateViewportPreviewTexture(Vector2 size, byte[] rgba)
    {
        int width = Math.Max(1, (int)size.X);
        int height = Math.Max(1, (int)size.Y);
        _gl!.BindTexture(TextureTarget.Texture2D, _viewportPreviewTexture);
        fixed (byte* ptr = rgba)
        {
            _gl.TexSubImage2D(TextureTarget.Texture2D, 0, 0, 0, (uint)width, (uint)height, PixelFormat.Rgba, PixelType.UnsignedByte, ptr);
        }
    }

    private void DrawViewportOverlayUi(Vector2 imageMin, Vector2 size)
    {
        ImGui.SetCursorScreenPos(imageMin + new Vector2(8f, 8f));
        ImGui.SetNextWindowBgAlpha(0.35f);
        ImGui.BeginChild("ViewportOverlayUi", new Vector2(210f, 70f), ImGuiChildFlags.Border, ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse);

        ImGui.Text("Preview Backend");
        ImGui.PushItemWidth(140f);
        var current = _viewportBackendMode == ViewportBackendMode.Vulkan ? "Vulkan" : "OpenGL";
        if (ImGui.BeginCombo("##ViewportBackend", current))
        {
            bool vulkanSelected = _viewportBackendMode == ViewportBackendMode.Vulkan;
            if (ImGui.Selectable("Vulkan", vulkanSelected))
            {
                _viewportBackendMode = ViewportBackendMode.Vulkan;
                _vulkanPreviewFailed = false;
                _raytracerSettings.ResetAccumulation = true;
            }

            bool glSelected = _viewportBackendMode == ViewportBackendMode.OpenGL;
            if (ImGui.Selectable("OpenGL", glSelected))
            {
                _viewportBackendMode = ViewportBackendMode.OpenGL;
                _openglPreviewFailed = false;
                _raytracerSettings.ResetAccumulation = true;
            }

            ImGui.EndCombo();
        }
        ImGui.PopItemWidth();

        ImGui.EndChild();
    }

    private void DrawViewportFallbackMessage(ImDrawListPtr drawList, Vector2 imageMin, Vector2 imageMax, string message)
    {
        var center = (imageMin + imageMax) * 0.5f;
        var textSize = ImGui.CalcTextSize(message);
        var pos = center - textSize * 0.5f;
        drawList.AddText(pos, ImGui.GetColorU32(new Vector4(1f, 0.6f, 0.3f, 0.9f)), message);
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
        var shift = keyboard.IsKeyPressed(Key.ShiftLeft) || keyboard.IsKeyPressed(Key.ShiftRight);

        switch (key)
        {
            case Key.Delete:
                DeleteSelected();
                break;
            case Key.D when ctrl:
                DuplicateSelected();
                break;
            case Key.N when ctrl:
                CreateEmptyEntity();
                break;
            case Key.Z when ctrl:
                _undoRedo.Undo();
                break;
            case Key.Y when ctrl:
                _undoRedo.Redo();
                break;
            case Key.S when ctrl && shift:
                SaveSceneAsDialog();
                break;
            case Key.S when ctrl:
                SaveSceneDialog();
                break;
            case Key.O when ctrl:
                OpenSceneDialog();
                break;
            // Gizmo mode shortcuts
            case Key.W:
                _transformGizmo.Mode = GizmoMode.Translate;
                break;
            case Key.E:
                _transformGizmo.Mode = GizmoMode.Rotate;
                break;
            case Key.R:
                _transformGizmo.Mode = GizmoMode.Scale;
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

        SaveEditorSettings();
        
        // Dispose ImGui while OpenGL context is still valid
        _imguiController?.Dispose();
        _imguiController = null;

        _viewportRenderer?.Dispose();
        _viewportRenderer = null;

        _viewportVulkanBackend?.Dispose();
        _viewportVulkanBackend = null;
        _vulkanViewportWindow?.Dispose();
        _vulkanViewportWindow = null;

        if (_viewportPreviewTexture != 0)
        {
            _gl?.DeleteTexture(_viewportPreviewTexture);
            _viewportPreviewTexture = 0;
        }
    }

    #region Scene Save/Load

    private const string SceneFileFilter = "Scene Files (*.rhes)|*.rhes|All Files (*.*)|*.*";
    private const string SceneFileExtension = ".rhes";
    private UI.FileDialogMode _pendingFileDialogMode;

    private void OpenSceneDialog()
    {
        _pendingFileDialogMode = UI.FileDialogMode.Open;
        var startDir = GetSceneDirectory();
        _fileDialog.Open(UI.FileDialogMode.Open, "Scene", startDir, SceneFileFilter, SceneFileExtension);
    }

    private void SaveSceneDialog()
    {
        // If we have a current scene path, save directly without dialog
        if (!string.IsNullOrEmpty(_currentScenePath) && File.Exists(_currentScenePath))
        {
            SaveScene(_currentScenePath);
        }
        else
        {
            SaveSceneAsDialog();
        }
    }

    private void SaveSceneAsDialog()
    {
        _pendingFileDialogMode = UI.FileDialogMode.Save;
        var startDir = GetSceneDirectory();
        _fileDialog.Open(UI.FileDialogMode.Save, "Scene", startDir, SceneFileFilter, SceneFileExtension);
    }

    private void DrawFileDialog()
    {
        var result = _fileDialog.Draw();
        
        if (result == UI.FileDialogResult.Ok)
        {
            var path = _fileDialog.SelectedPath;
            
            if (_pendingFileDialogMode == UI.FileDialogMode.Open)
            {
                LoadScene(path);
            }
            else
            {
                SaveScene(path);
            }
        }
    }

    private void SaveScene(string path)
    {
        if (_world == null)
        {
            Console.WriteLine("Cannot save: No world loaded");
            return;
        }

        try
        {
            _sceneSerializer.SaveToFile(_world, path);
            _currentScenePath = path;
            Console.WriteLine($"Scene saved to: {path}");
            
            // Update window title
            var fileName = Path.GetFileName(path);
            _window!.Title = $"{WindowTitle} - {fileName}";
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to save scene: {ex.Message}");
        }
    }

    private void LoadScene(string path)
    {
        if (!File.Exists(path))
        {
            Console.WriteLine($"Scene file not found: {path}");
            return;
        }

        try
        {
            ResetWorld();
            if (_world == null) return;

            _sceneSerializer.LoadFromFile(_world, path);
            _currentScenePath = path;
            Console.WriteLine($"Scene loaded from: {path}");
            
            // Update window title
            var fileName = Path.GetFileName(path);
            _window!.Title = $"{WindowTitle} - {fileName}";
            
            // Reset accumulation for renderer
            _raytracerSettings.ResetAccumulation = true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to load scene: {ex.Message}");
            // Reload default scene on failure
            LoadDefaultShowcaseScene();
        }
    }

    private string GetSceneDirectory()
    {
        // Try to use project's Scenes folder if available
        if (_projectManager.HasProject)
        {
            var scenesDir = _projectManager.GetScenesPath();
            if (!string.IsNullOrEmpty(scenesDir))
            {
                try
                {
                    if (!Directory.Exists(scenesDir))
                        Directory.CreateDirectory(scenesDir);
                    return scenesDir;
                }
                catch
                {
                    // Fall through to default
                }
            }
        }
        
        // Fall back to user's documents
        var docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        var redHoleScenes = Path.Combine(docs, "RedHoleEngine", "Scenes");
        
        try
        {
            Directory.CreateDirectory(redHoleScenes);
        }
        catch
        {
            return docs;
        }
        
        return redHoleScenes;
    }

    #endregion

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

    private void LoadActiveScene()
    {
        if (_gameModule != null)
        {
            if (LoadGameProjectScene())
                return;
        }

        LoadDefaultShowcaseScene();
    }

    private void ResetWorld()
    {
        _selection.ClearSelection();
        _undoRedo.Clear();
        _world?.Dispose();
        _world = new World();
        if (!_hasSavedCamera)
        {
            SetDefaultViewportCamera();
        }
        _viewportTime = 0f;

        foreach (var panel in _panels)
        {
            panel.SetContext(_world, _selection);
        }
    }

    private void LoadEditorSettings()
    {
        try
        {
            var path = GetEditorSettingsPath();
            if (File.Exists(path))
            {
                var json = File.ReadAllText(path);
                var settings = JsonSerializer.Deserialize<EditorSettings>(json);
                if (settings != null)
                {
                    _editorSettings = settings;
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to load editor settings: {ex.Message}");
        }

        _loadShowcaseOnStart = _editorSettings.LoadShowcaseOnStart;

        // Load project from saved path
        if (!string.IsNullOrEmpty(_editorSettings.GameProjectPath))
        {
            _projectManager.OpenProject(_editorSettings.GameProjectPath, out _);
        }

        if (string.Equals(_editorSettings.ViewportBackend, "OpenGL", StringComparison.OrdinalIgnoreCase))
        {
            _viewportBackendMode = ViewportBackendMode.OpenGL;
        }
        else
        {
            _viewportBackendMode = ViewportBackendMode.Vulkan;
        }

        if (_editorSettings.HasCamera && _editorSettings.CameraPosition.Length() > 0.5f)
        {
            _viewportCamera = new Camera(_editorSettings.CameraPosition, _editorSettings.CameraYaw, _editorSettings.CameraPitch);
            _hasSavedCamera = true;
        }
        else
        {
            SetDefaultViewportCamera();
            _hasSavedCamera = false;
        }

        LoadGameProjectFromSettings();
    }

    private void SetDefaultViewportCamera()
    {
        _viewportCamera = new Camera(new Vector3(0f, 6f, 24f), -90f, -10f);
    }

    private void EnsureViewportCameraValid()
    {
        if (float.IsNaN(_viewportCamera.Position.X) || float.IsNaN(_viewportCamera.Position.Y) || float.IsNaN(_viewportCamera.Position.Z))
        {
            SetDefaultViewportCamera();
            _raytracerSettings.ResetAccumulation = true;
            return;
        }

        if (_viewportCamera.Position.LengthSquared() < 1f)
        {
            SetDefaultViewportCamera();
            _raytracerSettings.ResetAccumulation = true;
        }
    }

    private void SaveEditorSettings()
    {
        _editorSettings.ViewportBackend = _viewportBackendMode == ViewportBackendMode.OpenGL ? "OpenGL" : "Vulkan";
        _editorSettings.LoadShowcaseOnStart = _loadShowcaseOnStart;
        _editorSettings.GameProjectPath = _projectManager.ProjectFilePath ?? "";
        _editorSettings.HasCamera = true;
        _editorSettings.CameraPosition = _viewportCamera.Position;
        _editorSettings.CameraYaw = _viewportCamera.Yaw;
        _editorSettings.CameraPitch = _viewportCamera.Pitch;
        
        // Also save the project if one is loaded
        if (_projectManager.HasProject)
        {
            _projectManager.SaveProject(out _);
        }

        try
        {
            var path = GetEditorSettingsPath();
            var json = JsonSerializer.Serialize(_editorSettings, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(path, json);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to save editor settings: {ex.Message}");
        }
    }

    private string GetEditorSettingsPath()
    {
        var baseDir = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var dir = Path.Combine(baseDir, "RedHoleEngine");
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, "editor_settings.json");
    }

    private void LoadGameProjectFromSettings()
    {
        if (!_projectManager.HasProject)
        {
            _gameModule = null;
            return;
        }

        var csProjectPath = _projectManager.GetCsProjectPath();
        if (string.IsNullOrEmpty(csProjectPath))
        {
            _gameModule = null;
            return;
        }

        if (TryLoadGameProject(csProjectPath, out var module, out _))
        {
            _gameModule = module;
            if (_gameModule != null)
            {
                _terminalSaveManager = new GameSaveManager(_gameModule.Name);
            }
        }
        else
        {
            _gameModule = null;
            _terminalSaveManager = new GameSaveManager("Editor");
        }
    }

    private bool LoadGameProjectScene()
    {
        if (_gameModule == null || _world == null)
            return false;

        ResetWorld();
        if (_world == null)
            return false;

        var saveManager = new GameSaveManager(_gameModule.Name);
        var context = new GameContext(_world, _gameResources, _renderSettings, _raytracerSettings, saveManager, application: null, isEditor: true);
        _gameModule.BuildScene(context);
        Console.WriteLine($"Loaded game scene from {_gameModule.Name}");
        return true;
    }

    private bool TryLoadGameProject(string path, out IGameModule? module, out string status)
    {
        module = null;

        string projectDir = path;
        string? projectFile = null;

        if (File.Exists(path) && Path.GetExtension(path).Equals(".csproj", StringComparison.OrdinalIgnoreCase))
        {
            projectFile = path;
            projectDir = Path.GetDirectoryName(path) ?? path;
        }
        else if (Directory.Exists(path))
        {
            var csprojFiles = Directory.GetFiles(path, "*.csproj", SearchOption.TopDirectoryOnly);
            if (csprojFiles.Length > 0)
            {
                projectFile = csprojFiles[0];
                projectDir = Path.GetDirectoryName(projectFile) ?? path;
            }
        }

        if (projectFile == null)
        {
            status = "No .csproj found in the selected folder.";
            return false;
        }

        var assemblyName = Path.GetFileNameWithoutExtension(projectFile);
        var targetFramework = ExtractTargetFramework(projectFile) ?? "net10.0";
        var assemblyPath = Path.Combine(projectDir, "bin", "Debug", targetFramework, assemblyName + ".dll");

        if (!File.Exists(assemblyPath))
        {
            status = $"Build the project first. DLL not found at {assemblyPath}";
            return false;
        }

        try
        {
            _gameResources.BasePath = projectDir;
            var assembly = Assembly.LoadFrom(assemblyPath);
            foreach (var type in assembly.GetTypes())
            {
                if (typeof(IGameModule).IsAssignableFrom(type) && !type.IsAbstract && !type.IsInterface)
                {
                    module = (IGameModule?)Activator.CreateInstance(type);
                    if (module != null)
                    {
                        status = $"Loaded {module.Name} from {assemblyPath}";
                        return true;
                    }
                }
            }

            status = "No IGameModule implementation found in assembly.";
            return false;
        }
        catch (Exception ex)
        {
            status = $"Failed to load game assembly: {ex.Message}";
            return false;
        }
    }

    private static string? ExtractTargetFramework(string projectFile)
    {
        try
        {
            var contents = File.ReadAllText(projectFile);
            var single = Regex.Match(contents, "<TargetFramework>([^<]+)</TargetFramework>");
            if (single.Success)
                return single.Groups[1].Value.Trim();

            var multi = Regex.Match(contents, "<TargetFrameworks>([^<]+)</TargetFrameworks>");
            if (multi.Success)
                return multi.Groups[1].Value.Split(';', StringSplitOptions.RemoveEmptyEntries)[0].Trim();
        }
        catch
        {
            return null;
        }

        return null;
    }

    private void DeleteSelected()
    {
        if (_world == null || !_selection.HasSelection) return;

        var toDelete = _selection.SelectedEntities.ToList();
        var command = new DeleteEntitiesCommand(_world, _selection, toDelete);
        _undoRedo.ExecuteCommand(command);
    }

    private void DuplicateSelected()
    {
        if (_world == null || !_selection.HasSelection) return;

        var toDuplicate = _selection.SelectedEntities.ToList();
        var command = new DuplicateEntitiesCommand(_world, _selection, toDuplicate);
        _undoRedo.ExecuteCommand(command);
    }

    private void CreateEmptyEntity()
    {
        if (_world == null) return;

        var command = new CreateEntityCommand(_world, _selection, "Empty Entity",
            new TransformComponent());
        _undoRedo.ExecuteCommand(command);
    }

    private void CreateCamera()
    {
        if (_world == null) return;

        var command = new CreateEntityCommand(_world, _selection, "Camera",
            new TransformComponent(new Vector3(0, 5, 10)),
            CameraComponent.CreatePerspective(60f, 16f / 9f));
        _undoRedo.ExecuteCommand(command);
    }

    private void CreateBlackHole()
    {
        if (_world == null) return;

        var command = new CreateEntityCommand(_world, _selection, "Black Hole",
            new TransformComponent(),
            GravitySourceComponent.CreateBlackHole(1e6f));
        _undoRedo.ExecuteCommand(command);
    }

    private void CreatePhysicsSphere()
    {
        if (_world == null) return;

        var command = new CreateEntityCommand(_world, _selection, "Physics Sphere",
            new TransformComponent(new Vector3(0, 5, 0)),
            RigidBodyComponent.CreateDynamic(1f),
            ColliderComponent.CreateSphere(0.5f));
        _undoRedo.ExecuteCommand(command);
    }

    private void CreatePhysicsBox()
    {
        if (_world == null) return;

        var command = new CreateEntityCommand(_world, _selection, "Physics Box",
            new TransformComponent(new Vector3(0, 5, 0)),
            RigidBodyComponent.CreateDynamic(1f),
            ColliderComponent.CreateBox(new Vector3(0.5f)));
        _undoRedo.ExecuteCommand(command);
    }

    private void CreateGroundPlane()
    {
        if (_world == null) return;

        var command = new CreateEntityCommand(_world, _selection, "Ground Plane",
            new TransformComponent(),
            RigidBodyComponent.CreateStatic(),
            ColliderComponent.CreateGroundPlane());
        _undoRedo.ExecuteCommand(command);
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
        _viewportVulkanBackend?.Dispose();
        _vulkanViewportWindow?.Dispose();
        _world?.Dispose();
        // Window and GL are managed by Silk.NET, no need to dispose here
    }
}
