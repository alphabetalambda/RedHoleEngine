using System.Text.Json.Serialization;

namespace RedHoleEngine.Editor.Project;

/// <summary>
/// Represents a RedHole Engine project file (.rhproj)
/// </summary>
public class RedHoleProject
{
    /// <summary>
    /// Project file format version for forward compatibility
    /// </summary>
    public int Version { get; set; } = 1;

    /// <summary>
    /// Project metadata
    /// </summary>
    public ProjectMetadata Metadata { get; set; } = new();

    /// <summary>
    /// Asset folder configuration
    /// </summary>
    public AssetPaths Assets { get; set; } = new();

    /// <summary>
    /// Build and runtime settings
    /// </summary>
    public BuildSettings Build { get; set; } = new();

    /// <summary>
    /// Rendering settings defaults
    /// </summary>
    public RenderingDefaults Rendering { get; set; } = new();

    /// <summary>
    /// Physics settings
    /// </summary>
    public PhysicsSettings Physics { get; set; } = new();

    /// <summary>
    /// Editor preferences for this project
    /// </summary>
    public ProjectEditorSettings Editor { get; set; } = new();
}

/// <summary>
/// Project metadata information
/// </summary>
public class ProjectMetadata
{
    /// <summary>
    /// Display name of the project
    /// </summary>
    public string Name { get; set; } = "Untitled Project";

    /// <summary>
    /// Project description
    /// </summary>
    public string Description { get; set; } = "";

    /// <summary>
    /// Project author/company
    /// </summary>
    public string Author { get; set; } = "";

    /// <summary>
    /// Project version string
    /// </summary>
    public string ProjectVersion { get; set; } = "1.0.0";

    /// <summary>
    /// Target engine version
    /// </summary>
    public string EngineVersion { get; set; } = "1.0.0";

    /// <summary>
    /// When the project was created
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// When the project was last modified
    /// </summary>
    public DateTime ModifiedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Unique project identifier
    /// </summary>
    public string ProjectId { get; set; } = Guid.NewGuid().ToString();
}

/// <summary>
/// Asset folder paths relative to project root
/// </summary>
public class AssetPaths
{
    /// <summary>
    /// Root folder for all assets (default: "Assets")
    /// </summary>
    public string Root { get; set; } = "Assets";

    /// <summary>
    /// Scene files folder (relative to Root)
    /// </summary>
    public string Scenes { get; set; } = "Scenes";

    /// <summary>
    /// Texture files folder
    /// </summary>
    public string Textures { get; set; } = "Textures";

    /// <summary>
    /// 3D model files folder
    /// </summary>
    public string Models { get; set; } = "Models";

    /// <summary>
    /// Material definition files folder
    /// </summary>
    public string Materials { get; set; } = "Materials";

    /// <summary>
    /// Shader files folder
    /// </summary>
    public string Shaders { get; set; } = "Shaders";

    /// <summary>
    /// Audio files folder
    /// </summary>
    public string Audio { get; set; } = "Audio";

    /// <summary>
    /// Script files folder
    /// </summary>
    public string Scripts { get; set; } = "Scripts";

    /// <summary>
    /// Prefab files folder
    /// </summary>
    public string Prefabs { get; set; } = "Prefabs";

    /// <summary>
    /// Configuration files folder
    /// </summary>
    public string Config { get; set; } = "Config";

    /// <summary>
    /// Fonts folder
    /// </summary>
    public string Fonts { get; set; } = "Fonts";
}

/// <summary>
/// Build and runtime configuration
/// </summary>
public class BuildSettings
{
    /// <summary>
    /// C# project file path relative to project root
    /// </summary>
    public string CsProjectPath { get; set; } = "";

    /// <summary>
    /// Target framework (e.g., "net8.0", "net9.0")
    /// </summary>
    public string TargetFramework { get; set; } = "net9.0";

    /// <summary>
    /// Output directory for builds
    /// </summary>
    public string OutputDirectory { get; set; } = "Build";

    /// <summary>
    /// Default scene to load on startup
    /// </summary>
    public string StartupScene { get; set; } = "";

    /// <summary>
    /// Company name for builds
    /// </summary>
    public string CompanyName { get; set; } = "";

    /// <summary>
    /// Product name for builds
    /// </summary>
    public string ProductName { get; set; } = "";

    /// <summary>
    /// Bundle identifier (for mobile/console)
    /// </summary>
    public string BundleIdentifier { get; set; } = "com.company.game";

    /// <summary>
    /// Target platforms
    /// </summary>
    public List<string> TargetPlatforms { get; set; } = new() { "Windows", "macOS", "Linux" };
}

/// <summary>
/// Default rendering settings for the project
/// </summary>
public class RenderingDefaults
{
    /// <summary>
    /// Default rays per pixel for raytracer
    /// </summary>
    public int RaysPerPixel { get; set; } = 4;

    /// <summary>
    /// Default max bounces for raytracer
    /// </summary>
    public int MaxBounces { get; set; } = 8;

    /// <summary>
    /// Default samples per frame
    /// </summary>
    public int SamplesPerFrame { get; set; } = 1;

    /// <summary>
    /// Enable accumulation by default
    /// </summary>
    public bool Accumulate { get; set; } = true;

    /// <summary>
    /// Enable denoising by default
    /// </summary>
    public bool Denoise { get; set; } = false;

    /// <summary>
    /// Preferred rendering backend
    /// </summary>
    public string PreferredBackend { get; set; } = "Vulkan";

    /// <summary>
    /// Default resolution width
    /// </summary>
    public int DefaultWidth { get; set; } = 1920;

    /// <summary>
    /// Default resolution height
    /// </summary>
    public int DefaultHeight { get; set; } = 1080;

    /// <summary>
    /// Enable VSync by default
    /// </summary>
    public bool VSync { get; set; } = true;
}

/// <summary>
/// Physics simulation settings
/// </summary>
public class PhysicsSettings
{
    /// <summary>
    /// Gravity vector
    /// </summary>
    public float GravityX { get; set; } = 0f;
    public float GravityY { get; set; } = -9.81f;
    public float GravityZ { get; set; } = 0f;

    /// <summary>
    /// Fixed timestep for physics simulation
    /// </summary>
    public float FixedTimestep { get; set; } = 0.02f;

    /// <summary>
    /// Maximum physics substeps per frame
    /// </summary>
    public int MaxSubsteps { get; set; } = 4;

    /// <summary>
    /// Enable continuous collision detection
    /// </summary>
    public bool ContinuousCollision { get; set; } = true;
}

/// <summary>
/// Editor-specific settings for this project
/// </summary>
public class ProjectEditorSettings
{
    /// <summary>
    /// Last opened scene path
    /// </summary>
    public string LastOpenedScene { get; set; } = "";

    /// <summary>
    /// Recently opened scenes (up to 10)
    /// </summary>
    public List<string> RecentScenes { get; set; } = new();

    /// <summary>
    /// Editor layout preset name
    /// </summary>
    public string LayoutPreset { get; set; } = "Default";

    /// <summary>
    /// Auto-save interval in seconds (0 = disabled)
    /// </summary>
    public int AutoSaveInterval { get; set; } = 300;

    /// <summary>
    /// Show grid in viewport
    /// </summary>
    public bool ShowGrid { get; set; } = true;

    /// <summary>
    /// Grid size in units
    /// </summary>
    public float GridSize { get; set; } = 1f;

    /// <summary>
    /// Snap to grid when moving
    /// </summary>
    public bool SnapToGrid { get; set; } = false;

    /// <summary>
    /// Rotation snap angle in degrees
    /// </summary>
    public float RotationSnapAngle { get; set; } = 15f;

    /// <summary>
    /// Scale snap increment
    /// </summary>
    public float ScaleSnapIncrement { get; set; } = 0.1f;
}
