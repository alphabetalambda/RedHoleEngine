using System.Numerics;
using ImGuiNET;
using RedHoleEngine.Editor.Project;

namespace RedHoleEngine.Editor.UI.Panels;

public class GameProjectPanel : EditorPanel
{
    private readonly ProjectManager _projectManager;
    private readonly BuildSystem _buildSystem;
    private readonly Action _onProjectLoaded;
    
    private string _pathBuffer = "";
    private string _newProjectName = "New Project";
    private string _statusMessage = "";
    private bool _showNewProjectPopup;
    private bool _showProjectSettings;
    private BuildConfiguration _buildConfiguration = BuildConfiguration.Debug;

    public GameProjectPanel(ProjectManager projectManager, BuildSystem buildSystem, Action onProjectLoaded)
    {
        _projectManager = projectManager;
        _buildSystem = buildSystem;
        _onProjectLoaded = onProjectLoaded;
    }
    
    /// <summary>
    /// Access to the build system for menu integration
    /// </summary>
    public BuildSystem BuildSystem => _buildSystem;

    public override string Title => "Project";

    protected override void OnDraw()
    {
        if (_projectManager.HasProject)
        {
            DrawProjectInfo();
        }
        else
        {
            DrawNoProject();
        }

        DrawNewProjectPopup();
        DrawProjectSettingsPopup();
    }

    private void DrawNoProject()
    {
        ImGui.TextColored(new Vector4(1f, 0.8f, 0.3f, 1f), "No Project Loaded");
        ImGui.Separator();
        ImGui.Spacing();

        ImGui.Text("Open Existing Project");
        ImGui.SetNextItemWidth(-100);
        ImGui.InputTextWithHint("##ProjectPath", "Path to project folder or .rhproj file", ref _pathBuffer, 512);
        ImGui.SameLine();
        if (ImGui.Button("Browse..."))
        {
            // TODO: Integrate with file dialog
        }

        if (ImGui.Button("Open Project", new Vector2(120, 0)))
        {
            OpenProject(_pathBuffer);
        }

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        if (ImGui.Button("Create New Project", new Vector2(-1, 0)))
        {
            _showNewProjectPopup = true;
            ImGui.OpenPopup("New Project");
        }

        if (!string.IsNullOrEmpty(_statusMessage))
        {
            ImGui.Spacing();
            ImGui.Separator();
            ImGui.TextWrapped(_statusMessage);
        }
    }

    private void DrawProjectInfo()
    {
        var project = _projectManager.CurrentProject!;
        var metadata = project.Metadata;

        // Header with project name
        ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.4f, 0.8f, 0.4f, 1f));
        ImGui.Text(metadata.Name);
        ImGui.PopStyleColor();
        
        ImGui.SameLine(ImGui.GetWindowWidth() - 80);
        if (ImGui.SmallButton("Settings"))
        {
            _showProjectSettings = true;
            ImGui.OpenPopup("Project Settings");
        }

        ImGui.TextDisabled($"v{metadata.ProjectVersion}");
        
        if (!string.IsNullOrEmpty(metadata.Author))
        {
            ImGui.SameLine();
            ImGui.TextDisabled($"by {metadata.Author}");
        }

        ImGui.Separator();

        // Project info
        if (ImGui.CollapsingHeader("Info", ImGuiTreeNodeFlags.DefaultOpen))
        {
            ImGui.Indent();
            
            if (!string.IsNullOrEmpty(metadata.Description))
            {
                ImGui.TextWrapped(metadata.Description);
                ImGui.Spacing();
            }

            ImGui.TextDisabled($"Engine: {metadata.EngineVersion}");
            ImGui.TextDisabled($"Created: {metadata.CreatedAt:yyyy-MM-dd}");
            ImGui.TextDisabled($"Modified: {metadata.ModifiedAt:yyyy-MM-dd HH:mm}");
            
            ImGui.Unindent();
        }

        // Asset paths
        if (ImGui.CollapsingHeader("Asset Folders"))
        {
            ImGui.Indent();
            
            DrawFolderLink("Assets", _projectManager.GetAssetPath());
            DrawFolderLink("Scenes", _projectManager.GetScenesPath());
            DrawFolderLink("Textures", _projectManager.GetTexturesPath());
            DrawFolderLink("Models", _projectManager.GetModelsPath());
            DrawFolderLink("Scripts", _projectManager.GetScriptsPath());
            DrawFolderLink("Audio", _projectManager.GetAudioPath());
            
            ImGui.Unindent();
        }

        // Build settings
        if (ImGui.CollapsingHeader("Build", ImGuiTreeNodeFlags.DefaultOpen))
        {
            ImGui.Indent();
            
            var csPath = _projectManager.GetCsProjectPath();
            if (!string.IsNullOrEmpty(csPath))
            {
                ImGui.Text($"C# Project: {Path.GetFileName(csPath)}");
                
                // Configuration dropdown
                var configs = new[] { "Debug", "Release" };
                var currentConfig = (int)_buildConfiguration;
                ImGui.SetNextItemWidth(100);
                if (ImGui.Combo("##Config", ref currentConfig, configs, configs.Length))
                {
                    _buildConfiguration = (BuildConfiguration)currentConfig;
                }
                
                ImGui.SameLine();
                
                // Build buttons
                if (_buildSystem.IsBuildInProgress)
                {
                    ImGui.BeginDisabled();
                    ImGui.Button("Building...");
                    ImGui.EndDisabled();
                    
                    ImGui.SameLine();
                    if (ImGui.Button("Cancel"))
                    {
                        _buildSystem.CancelBuild();
                    }
                }
                else
                {
                    if (ImGui.Button("Build"))
                    {
                        _ = BuildProjectAsync();
                    }
                    
                    ImGui.SameLine();
                    if (ImGui.Button("Rebuild"))
                    {
                        _ = RebuildProjectAsync();
                    }
                    
                    ImGui.SameLine();
                    if (ImGui.Button("Clean"))
                    {
                        _ = CleanProjectAsync();
                    }
                }
                
                // Last build status
                var lastResult = _buildSystem.LastBuildResult;
                if (lastResult != null)
                {
                    ImGui.Spacing();
                    if (lastResult.Success)
                    {
                        ImGui.TextColored(new Vector4(0.4f, 0.9f, 0.4f, 1f), 
                            $"Build succeeded ({lastResult.Duration.TotalSeconds:F1}s)");
                    }
                    else
                    {
                        ImGui.TextColored(new Vector4(1f, 0.4f, 0.4f, 1f), 
                            $"Build failed ({lastResult.ErrorCount} errors)");
                    }
                    
                    if (lastResult.WarningCount > 0)
                    {
                        ImGui.SameLine();
                        ImGui.TextColored(new Vector4(1f, 0.9f, 0.4f, 1f), 
                            $"{lastResult.WarningCount} warning(s)");
                    }
                }
                
                ImGui.Spacing();
                ImGui.Separator();
                
                if (ImGui.Button("Reload Game Module"))
                {
                    _onProjectLoaded();
                }
                if (ImGui.IsItemHovered())
                {
                    ImGui.SetTooltip("Reload the game DLL after building");
                }
            }
            else
            {
                ImGui.TextDisabled("No C# project configured");
                ImGui.TextWrapped("Set the C# project path in Project Settings.");
            }

            if (!string.IsNullOrEmpty(project.Build.StartupScene))
            {
                ImGui.Spacing();
                ImGui.Text($"Startup Scene: {project.Build.StartupScene}");
            }
            
            ImGui.Unindent();
        }

        // Recent scenes
        if (project.Editor.RecentScenes.Count > 0 && ImGui.CollapsingHeader("Recent Scenes"))
        {
            ImGui.Indent();
            
            foreach (var scene in project.Editor.RecentScenes.Take(5))
            {
                var sceneName = Path.GetFileName(scene);
                if (ImGui.Selectable(sceneName))
                {
                    // TODO: Open scene
                }
                if (ImGui.IsItemHovered())
                {
                    ImGui.SetTooltip(scene);
                }
            }
            
            ImGui.Unindent();
        }

        ImGui.Spacing();
        ImGui.Separator();

        // Actions
        if (ImGui.Button("Save Project"))
        {
            SaveProject();
        }
        ImGui.SameLine();
        if (ImGui.Button("Close Project"))
        {
            _projectManager.CloseProject();
            _statusMessage = "";
        }

        if (!string.IsNullOrEmpty(_statusMessage))
        {
            ImGui.Spacing();
            ImGui.TextWrapped(_statusMessage);
        }
    }

    private void DrawFolderLink(string label, string path)
    {
        ImGui.Text($"{label}:");
        ImGui.SameLine(80);
        
        var folderName = Path.GetFileName(path);
        if (string.IsNullOrEmpty(folderName)) folderName = path;
        
        ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.5f, 0.7f, 1f, 1f));
        if (ImGui.Selectable($"{folderName}##{label}", false, ImGuiSelectableFlags.None, new Vector2(0, 0)))
        {
            OpenInExplorer(path);
        }
        ImGui.PopStyleColor();
        
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(path);
        }
    }

    private void DrawNewProjectPopup()
    {
        var center = ImGui.GetMainViewport().GetCenter();
        ImGui.SetNextWindowPos(center, ImGuiCond.Appearing, new Vector2(0.5f, 0.5f));
        ImGui.SetNextWindowSize(new Vector2(400, 200));

        if (ImGui.BeginPopupModal("New Project", ref _showNewProjectPopup, ImGuiWindowFlags.NoResize))
        {
            ImGui.Text("Project Name:");
            ImGui.SetNextItemWidth(-1);
            ImGui.InputText("##NewProjectName", ref _newProjectName, 128);

            ImGui.Spacing();
            
            ImGui.Text("Location:");
            ImGui.SetNextItemWidth(-100);
            ImGui.InputText("##NewProjectPath", ref _pathBuffer, 512);
            ImGui.SameLine();
            if (ImGui.Button("Browse..."))
            {
                // TODO: Folder picker
            }

            ImGui.Spacing();
            
            var fullPath = Path.Combine(_pathBuffer, _newProjectName);
            ImGui.TextDisabled($"Will create: {fullPath}");

            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();

            if (ImGui.Button("Create", new Vector2(100, 0)))
            {
                CreateNewProject(_pathBuffer, _newProjectName);
                _showNewProjectPopup = false;
                ImGui.CloseCurrentPopup();
            }
            ImGui.SameLine();
            if (ImGui.Button("Cancel", new Vector2(100, 0)))
            {
                _showNewProjectPopup = false;
                ImGui.CloseCurrentPopup();
            }

            ImGui.EndPopup();
        }
    }

    private void DrawProjectSettingsPopup()
    {
        if (!_projectManager.HasProject) return;

        var center = ImGui.GetMainViewport().GetCenter();
        ImGui.SetNextWindowPos(center, ImGuiCond.Appearing, new Vector2(0.5f, 0.5f));
        ImGui.SetNextWindowSize(new Vector2(500, 600));

        if (ImGui.BeginPopupModal("Project Settings", ref _showProjectSettings, ImGuiWindowFlags.None))
        {
            var project = _projectManager.CurrentProject!;

            if (ImGui.BeginTabBar("ProjectSettingsTabs"))
            {
                if (ImGui.BeginTabItem("Metadata"))
                {
                    DrawMetadataSettings(project);
                    ImGui.EndTabItem();
                }

                if (ImGui.BeginTabItem("Assets"))
                {
                    DrawAssetSettings(project);
                    ImGui.EndTabItem();
                }

                if (ImGui.BeginTabItem("Build"))
                {
                    DrawBuildSettings(project);
                    ImGui.EndTabItem();
                }

                if (ImGui.BeginTabItem("Rendering"))
                {
                    DrawRenderingSettings(project);
                    ImGui.EndTabItem();
                }

                if (ImGui.BeginTabItem("Physics"))
                {
                    DrawPhysicsSettings(project);
                    ImGui.EndTabItem();
                }

                if (ImGui.BeginTabItem("Editor"))
                {
                    DrawEditorSettings(project);
                    ImGui.EndTabItem();
                }

                ImGui.EndTabBar();
            }

            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();

            if (ImGui.Button("Save", new Vector2(100, 0)))
            {
                SaveProject();
                _showProjectSettings = false;
                ImGui.CloseCurrentPopup();
            }
            ImGui.SameLine();
            if (ImGui.Button("Cancel", new Vector2(100, 0)))
            {
                _showProjectSettings = false;
                ImGui.CloseCurrentPopup();
            }

            ImGui.EndPopup();
        }
    }

    private void DrawMetadataSettings(RedHoleProject project)
    {
        var meta = project.Metadata;
        var name = meta.Name;
        var desc = meta.Description ?? "";
        var author = meta.Author ?? "";
        var version = meta.ProjectVersion;

        ImGui.Text("Project Name:");
        if (ImGui.InputText("##MetaName", ref name, 128))
            meta.Name = name;

        ImGui.Text("Description:");
        if (ImGui.InputTextMultiline("##MetaDesc", ref desc, 1024, new Vector2(-1, 60)))
            meta.Description = desc;

        ImGui.Text("Author:");
        if (ImGui.InputText("##MetaAuthor", ref author, 128))
            meta.Author = author;

        ImGui.Text("Version:");
        if (ImGui.InputText("##MetaVersion", ref version, 32))
            meta.ProjectVersion = version;

        ImGui.Spacing();
        ImGui.TextDisabled($"Project ID: {meta.ProjectId}");
    }

    private void DrawAssetSettings(RedHoleProject project)
    {
        var assets = project.Assets;

        ImGui.TextWrapped("Asset folder paths relative to project root:");
        ImGui.Spacing();

        assets.Root = DrawPathInputReturn("Root Folder", assets.Root);
        ImGui.Separator();
        assets.Scenes = DrawPathInputReturn("Scenes", assets.Scenes);
        assets.Textures = DrawPathInputReturn("Textures", assets.Textures);
        assets.Models = DrawPathInputReturn("Models", assets.Models);
        assets.Materials = DrawPathInputReturn("Materials", assets.Materials);
        assets.Shaders = DrawPathInputReturn("Shaders", assets.Shaders);
        assets.Audio = DrawPathInputReturn("Audio", assets.Audio);
        assets.Scripts = DrawPathInputReturn("Scripts", assets.Scripts);
        assets.Prefabs = DrawPathInputReturn("Prefabs", assets.Prefabs);
        assets.Config = DrawPathInputReturn("Config", assets.Config);
        assets.Fonts = DrawPathInputReturn("Fonts", assets.Fonts);
    }

    private void DrawBuildSettings(RedHoleProject project)
    {
        var build = project.Build;

        var csPath = build.CsProjectPath;
        var framework = build.TargetFramework;
        var outputDir = build.OutputDirectory;
        var startupScene = build.StartupScene;
        var company = build.CompanyName;
        var product = build.ProductName;
        var bundle = build.BundleIdentifier;

        ImGui.Text("C# Project Path:");
        if (ImGui.InputText("##CsPath", ref csPath, 256))
            build.CsProjectPath = csPath;

        ImGui.Text("Target Framework:");
        if (ImGui.InputText("##Framework", ref framework, 32))
            build.TargetFramework = framework;

        ImGui.Text("Output Directory:");
        if (ImGui.InputText("##OutputDir", ref outputDir, 256))
            build.OutputDirectory = outputDir;

        ImGui.Separator();

        ImGui.Text("Startup Scene:");
        if (ImGui.InputText("##StartupScene", ref startupScene, 256))
            build.StartupScene = startupScene;

        ImGui.Separator();

        ImGui.Text("Company Name:");
        if (ImGui.InputText("##Company", ref company, 128))
            build.CompanyName = company;

        ImGui.Text("Product Name:");
        if (ImGui.InputText("##Product", ref product, 128))
            build.ProductName = product;

        ImGui.Text("Bundle Identifier:");
        if (ImGui.InputText("##Bundle", ref bundle, 128))
            build.BundleIdentifier = bundle;
    }

    private void DrawRenderingSettings(RedHoleProject project)
    {
        var render = project.Rendering;

        var rpp = render.RaysPerPixel;
        var bounces = render.MaxBounces;
        var spf = render.SamplesPerFrame;
        var accumulate = render.Accumulate;
        var denoise = render.Denoise;
        var width = render.DefaultWidth;
        var height = render.DefaultHeight;
        var vsync = render.VSync;

        ImGui.Text("Raytracer Defaults:");
        if (ImGui.SliderInt("Rays Per Pixel", ref rpp, 1, 32))
            render.RaysPerPixel = rpp;
        if (ImGui.SliderInt("Max Bounces", ref bounces, 1, 32))
            render.MaxBounces = bounces;
        if (ImGui.SliderInt("Samples Per Frame", ref spf, 1, 16))
            render.SamplesPerFrame = spf;
        
        if (ImGui.Checkbox("Accumulate", ref accumulate))
            render.Accumulate = accumulate;
        if (ImGui.Checkbox("Denoise", ref denoise))
            render.Denoise = denoise;

        ImGui.Separator();
        
        ImGui.Text("Display:");
        if (ImGui.InputInt("Default Width", ref width))
            render.DefaultWidth = width;
        if (ImGui.InputInt("Default Height", ref height))
            render.DefaultHeight = height;
        if (ImGui.Checkbox("VSync", ref vsync))
            render.VSync = vsync;

        ImGui.Separator();

        var backends = new[] { "Vulkan", "OpenGL" };
        var currentBackend = render.PreferredBackend == "OpenGL" ? 1 : 0;
        ImGui.Text("Preferred Backend:");
        if (ImGui.Combo("##Backend", ref currentBackend, backends, backends.Length))
        {
            render.PreferredBackend = backends[currentBackend];
        }
    }

    private void DrawPhysicsSettings(RedHoleProject project)
    {
        var physics = project.Physics;

        var gx = physics.GravityX;
        var gy = physics.GravityY;
        var gz = physics.GravityZ;
        var timestep = physics.FixedTimestep;
        var substeps = physics.MaxSubsteps;
        var ccd = physics.ContinuousCollision;

        ImGui.Text("Gravity:");
        if (ImGui.DragFloat("X", ref gx, 0.1f))
            physics.GravityX = gx;
        if (ImGui.DragFloat("Y", ref gy, 0.1f))
            physics.GravityY = gy;
        if (ImGui.DragFloat("Z", ref gz, 0.1f))
            physics.GravityZ = gz;

        ImGui.Separator();

        if (ImGui.DragFloat("Fixed Timestep", ref timestep, 0.001f, 0.001f, 0.1f))
            physics.FixedTimestep = timestep;
        if (ImGui.SliderInt("Max Substeps", ref substeps, 1, 16))
            physics.MaxSubsteps = substeps;
        if (ImGui.Checkbox("Continuous Collision", ref ccd))
            physics.ContinuousCollision = ccd;
    }

    private void DrawEditorSettings(RedHoleProject project)
    {
        var editor = project.Editor;

        var autoSave = editor.AutoSaveInterval;
        var showGrid = editor.ShowGrid;
        var gridSize = editor.GridSize;
        var snapGrid = editor.SnapToGrid;
        var rotSnap = editor.RotationSnapAngle;
        var scaleSnap = editor.ScaleSnapIncrement;

        if (ImGui.SliderInt("Auto-Save Interval (sec)", ref autoSave, 0, 600))
            editor.AutoSaveInterval = autoSave;
        if (editor.AutoSaveInterval == 0)
        {
            ImGui.SameLine();
            ImGui.TextDisabled("(disabled)");
        }

        ImGui.Separator();

        if (ImGui.Checkbox("Show Grid", ref showGrid))
            editor.ShowGrid = showGrid;
        if (ImGui.DragFloat("Grid Size", ref gridSize, 0.1f, 0.1f, 10f))
            editor.GridSize = gridSize;
        
        ImGui.Separator();

        if (ImGui.Checkbox("Snap to Grid", ref snapGrid))
            editor.SnapToGrid = snapGrid;
        if (ImGui.DragFloat("Rotation Snap", ref rotSnap, 1f, 1f, 90f, "%.0f deg"))
            editor.RotationSnapAngle = rotSnap;
        if (ImGui.DragFloat("Scale Snap", ref scaleSnap, 0.01f, 0.01f, 1f))
            editor.ScaleSnapIncrement = scaleSnap;
    }

    private string DrawPathInputReturn(string label, string path)
    {
        ImGui.Text($"{label}:");
        ImGui.SameLine(100);
        ImGui.SetNextItemWidth(-1);
        ImGui.InputText($"##{label}Path", ref path, 128);
        return path;
    }

    private void OpenProject(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            _statusMessage = "Please enter a project path.";
            return;
        }

        if (_projectManager.OpenProject(path, out var error))
        {
            _statusMessage = $"Opened project: {_projectManager.CurrentProject?.Metadata.Name}";
            _onProjectLoaded();
        }
        else
        {
            _statusMessage = error;
        }
    }

    private void CreateNewProject(string location, string name)
    {
        if (string.IsNullOrWhiteSpace(location) || string.IsNullOrWhiteSpace(name))
        {
            _statusMessage = "Please enter a location and project name.";
            return;
        }

        try
        {
            var projectDir = Path.Combine(location, name);
            _projectManager.CreateProject(projectDir, name);
            _statusMessage = $"Created project: {name}";
            _pathBuffer = "";
            _newProjectName = "New Project";
        }
        catch (Exception ex)
        {
            _statusMessage = $"Failed to create project: {ex.Message}";
        }
    }

    private void SaveProject()
    {
        if (_projectManager.SaveProject(out var error))
        {
            _statusMessage = "Project saved.";
        }
        else
        {
            _statusMessage = error;
        }
    }

    private async Task BuildProjectAsync()
    {
        _statusMessage = "Building...";
        var result = await _buildSystem.BuildAsync(_buildConfiguration);
        
        if (result.Success)
        {
            _statusMessage = $"Build succeeded in {result.Duration.TotalSeconds:F1}s";
            // Reload game module after successful build
            _onProjectLoaded();
        }
        else
        {
            _statusMessage = $"Build failed with {result.ErrorCount} error(s)";
        }
    }

    private async Task RebuildProjectAsync()
    {
        _statusMessage = "Rebuilding...";
        var result = await _buildSystem.RebuildAsync(_buildConfiguration);
        
        if (result.Success)
        {
            _statusMessage = $"Rebuild succeeded in {result.Duration.TotalSeconds:F1}s";
            _onProjectLoaded();
        }
        else
        {
            _statusMessage = $"Rebuild failed with {result.ErrorCount} error(s)";
        }
    }

    private async Task CleanProjectAsync()
    {
        _statusMessage = "Cleaning...";
        var result = await _buildSystem.CleanAsync();
        
        if (result.Success)
        {
            _statusMessage = "Clean succeeded";
        }
        else
        {
            _statusMessage = "Clean failed";
        }
    }

    private static void OpenInExplorer(string path)
    {
        try
        {
            if (!Directory.Exists(path))
            {
                Directory.CreateDirectory(path);
            }

            if (OperatingSystem.IsWindows())
            {
                System.Diagnostics.Process.Start("explorer.exe", path);
            }
            else if (OperatingSystem.IsMacOS())
            {
                System.Diagnostics.Process.Start("open", path);
            }
            else if (OperatingSystem.IsLinux())
            {
                System.Diagnostics.Process.Start("xdg-open", path);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to open explorer: {ex.Message}");
        }
    }
}
