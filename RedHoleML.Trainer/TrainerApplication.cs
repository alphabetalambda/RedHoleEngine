using System.Numerics;
using ImGuiNET;
using RedHoleML.Trainer.UI.Panels;
using Silk.NET.Input;
using Silk.NET.Maths;
using Silk.NET.OpenGL;
using Silk.NET.OpenGL.Extensions.ImGui;
using Silk.NET.Windowing;

namespace RedHoleML.Trainer;

/// <summary>
/// Main ML Trainer application with ImGui interface
/// </summary>
public class TrainerApplication : IDisposable
{
    private IWindow? _window;
    private GL? _gl;
    private IInputContext? _input;
    private ImGuiController? _imguiController;

    // Panels
    private DatasetPanel? _datasetPanel;
    private ModelDesignerPanel? _modelDesignerPanel;
    private TrainingPanel? _trainingPanel;
    private TestingPanel? _testingPanel;
    private ExportPanel? _exportPanel;

    // Shared state
    private readonly TrainerState _state = new();

    public int WindowWidth { get; set; } = 1400;
    public int WindowHeight { get; set; } = 900;
    public string WindowTitle { get; set; } = "RedHole ML Trainer";

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

        Console.WriteLine("Starting RedHole ML Trainer...");
        _window.Run();
    }

    private void OnLoad()
    {
        Console.WriteLine("Loading trainer...");

        _gl = _window!.CreateOpenGL();
        _input = _window.CreateInput();

        // Create ImGui controller
        _imguiController = new ImGuiController(_gl, _window, _input);

        // Configure ImGui for docking
        var io = ImGui.GetIO();
        io.ConfigFlags |= ImGuiConfigFlags.DockingEnable;

        // Initialize panels
        _datasetPanel = new DatasetPanel(_state);
        _modelDesignerPanel = new ModelDesignerPanel(_state);
        _trainingPanel = new TrainingPanel(_state);
        _testingPanel = new TestingPanel(_state);
        _exportPanel = new ExportPanel(_state);

        Console.WriteLine("ML Trainer loaded successfully");
    }

    private void OnUpdate(double deltaTime)
    {
        _imguiController?.Update((float)deltaTime);
        
        // Update training progress if active
        _trainingPanel?.Update((float)deltaTime);
    }

    private void OnRender(double deltaTime)
    {
        _gl?.ClearColor(0.1f, 0.1f, 0.12f, 1f);
        _gl?.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);

        // Begin dockspace
        BeginDockSpace();

        // Draw menu bar
        DrawMenuBar();

        // Draw panels
        _datasetPanel?.Draw();
        _modelDesignerPanel?.Draw();
        _trainingPanel?.Draw();
        _testingPanel?.Draw();
        _exportPanel?.Draw();

        // Draw status bar
        DrawStatusBar();

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

        var dockspaceId = ImGui.GetID("TrainerDockspace");
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
                if (ImGui.MenuItem("New Project", "Ctrl+N"))
                {
                    _state.Reset();
                }
                if (ImGui.MenuItem("Open Project", "Ctrl+O"))
                {
                    // TODO: Open project dialog
                }
                if (ImGui.MenuItem("Save Project", "Ctrl+S"))
                {
                    // TODO: Save project
                }
                ImGui.Separator();
                if (ImGui.MenuItem("Import Dataset", "Ctrl+I"))
                {
                    _datasetPanel?.ShowImportDialog();
                }
                if (ImGui.MenuItem("Export Model", "Ctrl+E", false, _state.TrainedModel != null))
                {
                    _exportPanel?.ShowExportDialog();
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
                if (ImGui.MenuItem("Clear Dataset", null, false, _state.HasDataset))
                {
                    _state.ClearDataset();
                }
                if (ImGui.MenuItem("Reset Model", null, false, _state.TrainedModel != null))
                {
                    _state.ClearModel();
                }
                ImGui.EndMenu();
            }

            if (ImGui.BeginMenu("View"))
            {
                bool showDataset = _datasetPanel?.IsVisible ?? true;
                if (ImGui.MenuItem("Dataset", null, ref showDataset))
                    if (_datasetPanel != null) _datasetPanel.IsVisible = showDataset;

                bool showDesigner = _modelDesignerPanel?.IsVisible ?? true;
                if (ImGui.MenuItem("Model Designer", null, ref showDesigner))
                    if (_modelDesignerPanel != null) _modelDesignerPanel.IsVisible = showDesigner;

                bool showTraining = _trainingPanel?.IsVisible ?? true;
                if (ImGui.MenuItem("Training", null, ref showTraining))
                    if (_trainingPanel != null) _trainingPanel.IsVisible = showTraining;

                bool showTesting = _testingPanel?.IsVisible ?? true;
                if (ImGui.MenuItem("Testing", null, ref showTesting))
                    if (_testingPanel != null) _testingPanel.IsVisible = showTesting;

                bool showExport = _exportPanel?.IsVisible ?? true;
                if (ImGui.MenuItem("Export", null, ref showExport))
                    if (_exportPanel != null) _exportPanel.IsVisible = showExport;

                ImGui.EndMenu();
            }

            if (ImGui.BeginMenu("Help"))
            {
                if (ImGui.MenuItem("Documentation"))
                {
                    // TODO: Open docs
                }
                if (ImGui.MenuItem("About"))
                {
                    // TODO: About dialog
                }
                ImGui.EndMenu();
            }

            ImGui.EndMenuBar();
        }
    }

    private void DrawStatusBar()
    {
        var viewport = ImGui.GetMainViewport();
        float statusBarHeight = 25f;
        
        ImGui.SetNextWindowPos(new Vector2(viewport.WorkPos.X, viewport.WorkPos.Y + viewport.WorkSize.Y - statusBarHeight));
        ImGui.SetNextWindowSize(new Vector2(viewport.WorkSize.X, statusBarHeight));
        
        ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, 0);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 0);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(8, 4));
        
        if (ImGui.Begin("StatusBar", ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoResize | 
                                      ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoScrollbar |
                                      ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.NoDocking))
        {
            // Dataset status
            if (_state.HasDataset)
            {
                ImGui.TextColored(new Vector4(0.4f, 0.9f, 0.4f, 1f), $"Dataset: {_state.DatasetRowCount} rows, {_state.DatasetColumnCount} columns");
            }
            else
            {
                ImGui.TextColored(new Vector4(0.6f, 0.6f, 0.6f, 1f), "No dataset loaded");
            }

            ImGui.SameLine(300);

            // Model status
            if (_state.TrainedModel != null)
            {
                ImGui.TextColored(new Vector4(0.4f, 0.7f, 1f, 1f), $"Model: {_state.ModelType} (trained)");
            }
            else if (_state.ModelType != ModelType.None)
            {
                ImGui.TextColored(new Vector4(1f, 0.8f, 0.4f, 1f), $"Model: {_state.ModelType} (not trained)");
            }
            else
            {
                ImGui.TextColored(new Vector4(0.6f, 0.6f, 0.6f, 1f), "No model configured");
            }

            ImGui.SameLine(600);

            // Training status
            if (_state.IsTraining)
            {
                ImGui.TextColored(new Vector4(1f, 0.9f, 0.3f, 1f), $"Training... {_state.TrainingProgress:P0}");
            }
        }
        ImGui.End();
        
        ImGui.PopStyleVar(3);
    }

    private void OnResize(Vector2D<int> size)
    {
        WindowWidth = size.X;
        WindowHeight = size.Y;
        _gl?.Viewport(size);
    }

    private void OnClosing()
    {
        Console.WriteLine("Closing trainer...");
        _imguiController?.Dispose();
        _state.Dispose();
    }

    public void Dispose()
    {
        _state.Dispose();
    }
}
