using System.Text.Json;
using System.Text.Json.Serialization;

namespace RedHoleEngine.Editor.Project;

/// <summary>
/// Manages loading, saving, and creating RedHole Engine projects
/// </summary>
public class ProjectManager
{
    /// <summary>
    /// Project file extension
    /// </summary>
    public const string ProjectExtension = ".rhproj";

    /// <summary>
    /// Project file name
    /// </summary>
    public const string ProjectFileName = "project.rhproj";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() }
    };

    /// <summary>
    /// Currently loaded project
    /// </summary>
    public RedHoleProject? CurrentProject { get; private set; }

    /// <summary>
    /// Path to the project directory (folder containing .rhproj)
    /// </summary>
    public string? ProjectDirectory { get; private set; }

    /// <summary>
    /// Full path to the project file
    /// </summary>
    public string? ProjectFilePath { get; private set; }

    /// <summary>
    /// Whether a project is currently loaded
    /// </summary>
    public bool HasProject => CurrentProject != null;

    /// <summary>
    /// Event raised when project is loaded or unloaded
    /// </summary>
    public event Action<RedHoleProject?>? ProjectChanged;

    /// <summary>
    /// Event raised when project is modified
    /// </summary>
    public event Action? ProjectModified;

    /// <summary>
    /// Creates a new project in the specified directory
    /// </summary>
    public RedHoleProject CreateProject(string directory, string name)
    {
        if (!Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var project = new RedHoleProject
        {
            Metadata = new ProjectMetadata
            {
                Name = name,
                CreatedAt = DateTime.UtcNow,
                ModifiedAt = DateTime.UtcNow,
                ProjectId = Guid.NewGuid().ToString()
            }
        };

        // Create default folder structure
        CreateProjectFolders(directory, project.Assets);

        // Save project file
        var projectPath = Path.Combine(directory, ProjectFileName);
        SaveProjectFile(project, projectPath);

        // Set as current
        CurrentProject = project;
        ProjectDirectory = directory;
        ProjectFilePath = projectPath;

        ProjectChanged?.Invoke(project);

        return project;
    }

    /// <summary>
    /// Opens a project from a directory or project file path
    /// </summary>
    public bool OpenProject(string path, out string error)
    {
        error = "";

        try
        {
            string projectFilePath;
            string projectDir;

            if (File.Exists(path) && path.EndsWith(ProjectExtension, StringComparison.OrdinalIgnoreCase))
            {
                // Direct path to .rhproj file
                projectFilePath = path;
                projectDir = Path.GetDirectoryName(path) ?? path;
            }
            else if (Directory.Exists(path))
            {
                // Directory - look for project file
                projectFilePath = Path.Combine(path, ProjectFileName);
                projectDir = path;

                if (!File.Exists(projectFilePath))
                {
                    // Try to find any .rhproj file
                    var projFiles = Directory.GetFiles(path, "*" + ProjectExtension, SearchOption.TopDirectoryOnly);
                    if (projFiles.Length > 0)
                    {
                        projectFilePath = projFiles[0];
                    }
                    else
                    {
                        error = $"No project file found in {path}. Create a new project or select a .rhproj file.";
                        return false;
                    }
                }
            }
            else
            {
                error = $"Path does not exist: {path}";
                return false;
            }

            // Load the project file
            var json = File.ReadAllText(projectFilePath);
            var project = JsonSerializer.Deserialize<RedHoleProject>(json, JsonOptions);

            if (project == null)
            {
                error = "Failed to parse project file.";
                return false;
            }

            CurrentProject = project;
            ProjectDirectory = projectDir;
            ProjectFilePath = projectFilePath;

            // Ensure folders exist
            EnsureProjectFolders();

            ProjectChanged?.Invoke(project);
            return true;
        }
        catch (Exception ex)
        {
            error = $"Failed to open project: {ex.Message}";
            return false;
        }
    }

    /// <summary>
    /// Saves the current project
    /// </summary>
    public bool SaveProject(out string error)
    {
        error = "";

        if (CurrentProject == null || string.IsNullOrEmpty(ProjectFilePath))
        {
            error = "No project loaded.";
            return false;
        }

        try
        {
            CurrentProject.Metadata.ModifiedAt = DateTime.UtcNow;
            SaveProjectFile(CurrentProject, ProjectFilePath);
            return true;
        }
        catch (Exception ex)
        {
            error = $"Failed to save project: {ex.Message}";
            return false;
        }
    }

    /// <summary>
    /// Closes the current project
    /// </summary>
    public void CloseProject()
    {
        CurrentProject = null;
        ProjectDirectory = null;
        ProjectFilePath = null;
        ProjectChanged?.Invoke(null);
    }

    /// <summary>
    /// Marks the project as modified
    /// </summary>
    public void MarkModified()
    {
        if (CurrentProject != null)
        {
            CurrentProject.Metadata.ModifiedAt = DateTime.UtcNow;
            ProjectModified?.Invoke();
        }
    }

    /// <summary>
    /// Gets the full path to an asset folder
    /// </summary>
    public string GetAssetPath(string relativePath = "")
    {
        if (ProjectDirectory == null || CurrentProject == null)
            return "";

        var assetsRoot = Path.Combine(ProjectDirectory, CurrentProject.Assets.Root);
        return string.IsNullOrEmpty(relativePath) 
            ? assetsRoot 
            : Path.Combine(assetsRoot, relativePath);
    }

    /// <summary>
    /// Gets the full path to the scenes folder
    /// </summary>
    public string GetScenesPath() => GetAssetPath(CurrentProject?.Assets.Scenes ?? "Scenes");

    /// <summary>
    /// Gets the full path to the textures folder
    /// </summary>
    public string GetTexturesPath() => GetAssetPath(CurrentProject?.Assets.Textures ?? "Textures");

    /// <summary>
    /// Gets the full path to the models folder
    /// </summary>
    public string GetModelsPath() => GetAssetPath(CurrentProject?.Assets.Models ?? "Models");

    /// <summary>
    /// Gets the full path to the materials folder
    /// </summary>
    public string GetMaterialsPath() => GetAssetPath(CurrentProject?.Assets.Materials ?? "Materials");

    /// <summary>
    /// Gets the full path to the shaders folder
    /// </summary>
    public string GetShadersPath() => GetAssetPath(CurrentProject?.Assets.Shaders ?? "Shaders");

    /// <summary>
    /// Gets the full path to the audio folder
    /// </summary>
    public string GetAudioPath() => GetAssetPath(CurrentProject?.Assets.Audio ?? "Audio");

    /// <summary>
    /// Gets the full path to the scripts folder
    /// </summary>
    public string GetScriptsPath() => GetAssetPath(CurrentProject?.Assets.Scripts ?? "Scripts");

    /// <summary>
    /// Gets the full path to the prefabs folder
    /// </summary>
    public string GetPrefabsPath() => GetAssetPath(CurrentProject?.Assets.Prefabs ?? "Prefabs");

    /// <summary>
    /// Gets the C# project file path
    /// </summary>
    public string? GetCsProjectPath()
    {
        if (ProjectDirectory == null || CurrentProject == null)
            return null;

        if (!string.IsNullOrEmpty(CurrentProject.Build.CsProjectPath))
        {
            return Path.Combine(ProjectDirectory, CurrentProject.Build.CsProjectPath);
        }

        // Try to find a .csproj file
        var csprojFiles = Directory.GetFiles(ProjectDirectory, "*.csproj", SearchOption.TopDirectoryOnly);
        return csprojFiles.Length > 0 ? csprojFiles[0] : null;
    }

    /// <summary>
    /// Adds a scene to recent scenes list
    /// </summary>
    public void AddRecentScene(string scenePath)
    {
        if (CurrentProject == null) return;

        var recentScenes = CurrentProject.Editor.RecentScenes;
        
        // Remove if already exists
        recentScenes.Remove(scenePath);
        
        // Add to front
        recentScenes.Insert(0, scenePath);
        
        // Keep only last 10
        while (recentScenes.Count > 10)
        {
            recentScenes.RemoveAt(recentScenes.Count - 1);
        }

        CurrentProject.Editor.LastOpenedScene = scenePath;
        MarkModified();
    }

    /// <summary>
    /// Gets a path relative to the project directory
    /// </summary>
    public string GetRelativePath(string absolutePath)
    {
        if (string.IsNullOrEmpty(ProjectDirectory) || string.IsNullOrEmpty(absolutePath))
            return absolutePath;

        if (absolutePath.StartsWith(ProjectDirectory))
        {
            return absolutePath.Substring(ProjectDirectory.Length).TrimStart(Path.DirectorySeparatorChar);
        }

        return absolutePath;
    }

    /// <summary>
    /// Converts a relative path to absolute
    /// </summary>
    public string GetAbsolutePath(string relativePath)
    {
        if (string.IsNullOrEmpty(ProjectDirectory) || Path.IsPathRooted(relativePath))
            return relativePath;

        return Path.Combine(ProjectDirectory, relativePath);
    }

    private void SaveProjectFile(RedHoleProject project, string path)
    {
        var json = JsonSerializer.Serialize(project, JsonOptions);
        File.WriteAllText(path, json);
    }

    private void CreateProjectFolders(string directory, AssetPaths assets)
    {
        var assetsRoot = Path.Combine(directory, assets.Root);
        
        var folders = new[]
        {
            assetsRoot,
            Path.Combine(assetsRoot, assets.Scenes),
            Path.Combine(assetsRoot, assets.Textures),
            Path.Combine(assetsRoot, assets.Models),
            Path.Combine(assetsRoot, assets.Materials),
            Path.Combine(assetsRoot, assets.Shaders),
            Path.Combine(assetsRoot, assets.Audio),
            Path.Combine(assetsRoot, assets.Scripts),
            Path.Combine(assetsRoot, assets.Prefabs),
            Path.Combine(assetsRoot, assets.Config),
            Path.Combine(assetsRoot, assets.Fonts)
        };

        foreach (var folder in folders)
        {
            if (!Directory.Exists(folder))
            {
                Directory.CreateDirectory(folder);
            }
        }
    }

    private void EnsureProjectFolders()
    {
        if (ProjectDirectory != null && CurrentProject != null)
        {
            CreateProjectFolders(ProjectDirectory, CurrentProject.Assets);
        }
    }

    /// <summary>
    /// Checks if a directory contains a RedHole project
    /// </summary>
    public static bool IsProjectDirectory(string path)
    {
        if (!Directory.Exists(path))
            return false;

        var projectFile = Path.Combine(path, ProjectFileName);
        if (File.Exists(projectFile))
            return true;

        var projFiles = Directory.GetFiles(path, "*" + ProjectExtension, SearchOption.TopDirectoryOnly);
        return projFiles.Length > 0;
    }

    /// <summary>
    /// Gets project info without fully loading it
    /// </summary>
    public static ProjectMetadata? GetProjectInfo(string path)
    {
        try
        {
            string projectFilePath;

            if (File.Exists(path) && path.EndsWith(ProjectExtension))
            {
                projectFilePath = path;
            }
            else if (Directory.Exists(path))
            {
                projectFilePath = Path.Combine(path, ProjectFileName);
                if (!File.Exists(projectFilePath))
                {
                    var projFiles = Directory.GetFiles(path, "*" + ProjectExtension, SearchOption.TopDirectoryOnly);
                    if (projFiles.Length == 0) return null;
                    projectFilePath = projFiles[0];
                }
            }
            else
            {
                return null;
            }

            var json = File.ReadAllText(projectFilePath);
            var project = JsonSerializer.Deserialize<RedHoleProject>(json, JsonOptions);
            return project?.Metadata;
        }
        catch
        {
            return null;
        }
    }
}
