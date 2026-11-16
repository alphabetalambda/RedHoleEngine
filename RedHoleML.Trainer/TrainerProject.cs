using System.Text.Json;
using System.Text.Json.Serialization;

namespace RedHoleML.Trainer;

/// <summary>
/// On-disk ML Trainer project (.rhml, JSON): dataset reference, column selection, model
/// configuration and, optionally, the trained model saved alongside as a .zip.
/// Paths are stored relative to the project file when possible so projects can be moved.
/// </summary>
public sealed class TrainerProject
{
    /// <summary>File extension for trainer projects</summary>
    public const string FileExtension = ".rhml";

    /// <summary>Filter string for file dialogs</summary>
    public const string FileFilter = "ML Trainer Project (*.rhml)|*.rhml|All Files (*.*)|*.*";

    /// <summary>Current format version</summary>
    public const int CurrentFormatVersion = 1;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() },
    };

    public int FormatVersion { get; set; } = CurrentFormatVersion;

    // Dataset
    public string DatasetPath { get; set; } = "";
    public bool DatasetHasHeader { get; set; } = true;
    public string DatasetSeparator { get; set; } = ",";
    public string LabelColumn { get; set; } = "";
    public List<string> FeatureColumns { get; set; } = new();

    // Model
    public ModelType ModelType { get; set; } = ModelType.None;
    public ModelHyperparameters Hyperparameters { get; set; } = new();
    public float TrainTestSplit { get; set; } = 0.8f;

    /// <summary>Trained model file (ML.NET .zip), empty if the model was not saved</summary>
    public string ModelPath { get; set; } = "";

    /// <summary>
    /// Capture the persistable parts of the trainer state
    /// </summary>
    public static TrainerProject FromState(TrainerState state)
    {
        return new TrainerProject
        {
            DatasetPath = state.DatasetPath,
            DatasetHasHeader = state.DatasetHasHeader,
            DatasetSeparator = state.DatasetSeparator.ToString(),
            LabelColumn = state.LabelColumn,
            FeatureColumns = state.FeatureColumns.ToList(),
            ModelType = state.ModelType,
            Hyperparameters = CopyHyperparameters(state.Hyperparameters),
            TrainTestSplit = state.TrainTestSplit,
        };
    }

    /// <summary>
    /// Serialize to JSON with paths made relative to <paramref name="projectDirectory"/>
    /// </summary>
    public string ToJson(string? projectDirectory = null)
    {
        var copy = (TrainerProject)MemberwiseClone();
        copy.DatasetPath = MakeRelative(DatasetPath, projectDirectory);
        copy.ModelPath = MakeRelative(ModelPath, projectDirectory);
        return JsonSerializer.Serialize(copy, JsonOptions);
    }

    /// <summary>
    /// Parse JSON, resolving relative paths against <paramref name="projectDirectory"/>
    /// </summary>
    /// <exception cref="InvalidDataException">The JSON is not a valid project</exception>
    public static TrainerProject FromJson(string json, string? projectDirectory = null)
    {
        TrainerProject? project;
        try
        {
            project = JsonSerializer.Deserialize<TrainerProject>(json, JsonOptions);
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"Invalid trainer project: {ex.Message}", ex);
        }

        if (project == null)
            throw new InvalidDataException("Invalid trainer project: empty document");
        if (project.FormatVersion > CurrentFormatVersion)
            throw new InvalidDataException($"Project format {project.FormatVersion} is newer than supported ({CurrentFormatVersion})");

        project.FeatureColumns ??= new List<string>();
        project.Hyperparameters ??= new ModelHyperparameters();
        project.DatasetPath = ResolvePath(project.DatasetPath, projectDirectory);
        project.ModelPath = ResolvePath(project.ModelPath, projectDirectory);
        return project;
    }

    /// <summary>
    /// The separator as a character (defaults to comma)
    /// </summary>
    public char GetSeparator() => string.IsNullOrEmpty(DatasetSeparator) ? ',' : DatasetSeparator[0];

    /// <summary>
    /// Path of the trained model file that accompanies a project file
    /// </summary>
    public static string GetModelPathFor(string projectFilePath)
    {
        return Path.ChangeExtension(projectFilePath, null) + ".model.zip";
    }

    /// <summary>
    /// Save the project (and trained model, if any) to a file
    /// </summary>
    public static void Save(TrainerState state, string projectFilePath)
    {
        var fullPath = Path.GetFullPath(projectFilePath);
        var directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        var project = FromState(state);
        if (state.TrainedModel != null)
        {
            var modelPath = GetModelPathFor(fullPath);
            state.MLContext.Model.Save(state.TrainedModel, state.ModelSchema ?? state.DatasetSchema, modelPath);
            project.ModelPath = modelPath;
        }

        File.WriteAllText(fullPath, project.ToJson(directory));
    }

    /// <summary>
    /// Load a project into the trainer state. Missing datasets or models are reported as warnings
    /// rather than failing the whole load.
    /// </summary>
    /// <returns>Warnings for parts that could not be restored</returns>
    public static List<string> Load(TrainerState state, string projectFilePath)
    {
        var fullPath = Path.GetFullPath(projectFilePath);
        var project = FromJson(File.ReadAllText(fullPath), Path.GetDirectoryName(fullPath));
        var warnings = new List<string>();

        state.Reset();

        if (!string.IsNullOrEmpty(project.DatasetPath))
        {
            if (File.Exists(project.DatasetPath))
            {
                state.LoadDatasetFromCsv(project.DatasetPath, project.DatasetHasHeader, project.GetSeparator());

                // Only restore columns that still exist in the dataset
                if (state.ColumnNames.Contains(project.LabelColumn))
                    state.LabelColumn = project.LabelColumn;
                else if (!string.IsNullOrEmpty(project.LabelColumn))
                    warnings.Add($"Label column '{project.LabelColumn}' no longer exists in the dataset");
                state.FeatureColumns.AddRange(project.FeatureColumns.Where(state.ColumnNames.Contains));

                var missing = project.FeatureColumns.Count(c => !state.ColumnNames.Contains(c));
                if (missing > 0)
                    warnings.Add($"{missing} feature column(s) no longer exist in the dataset");
            }
            else
            {
                warnings.Add($"Dataset not found: {project.DatasetPath}");
            }
        }

        state.ModelType = project.ModelType;
        CopyHyperparameters(project.Hyperparameters, state.Hyperparameters);
        state.TrainTestSplit = Math.Clamp(project.TrainTestSplit, 0.05f, 0.95f);

        if (!string.IsNullOrEmpty(project.ModelPath))
        {
            if (File.Exists(project.ModelPath))
            {
                try
                {
                    state.TrainedModel = state.MLContext.Model.Load(project.ModelPath, out var schema);
                    state.ModelSchema = schema;
                }
                catch (Exception ex)
                {
                    warnings.Add($"Could not load trained model: {ex.Message}");
                }
            }
            else
            {
                warnings.Add($"Trained model not found: {project.ModelPath}");
            }
        }

        return warnings;
    }

    private static ModelHyperparameters CopyHyperparameters(ModelHyperparameters source, ModelHyperparameters? target = null)
    {
        target ??= new ModelHyperparameters();
        target.NumberOfLeaves = source.NumberOfLeaves;
        target.NumberOfTrees = source.NumberOfTrees;
        target.MinDataPointsInLeaves = source.MinDataPointsInLeaves;
        target.LearningRate = source.LearningRate;
        target.NumberOfClusters = source.NumberOfClusters;
        target.Rank = source.Rank;
        target.MaxIterations = source.MaxIterations;
        return target;
    }

    /// <summary>
    /// Make a path relative to a base directory when it lies inside it (or a nearby folder)
    /// </summary>
    public static string MakeRelative(string path, string? baseDirectory)
    {
        if (string.IsNullOrEmpty(path) || string.IsNullOrEmpty(baseDirectory) || !Path.IsPathRooted(path))
            return path;

        var relative = Path.GetRelativePath(baseDirectory, path);
        // Different drive/root: GetRelativePath returns the original absolute path
        return Path.IsPathRooted(relative) ? path : relative.Replace('\\', '/');
    }

    /// <summary>
    /// Resolve a stored path against a base directory
    /// </summary>
    public static string ResolvePath(string path, string? baseDirectory)
    {
        if (string.IsNullOrEmpty(path) || string.IsNullOrEmpty(baseDirectory) || Path.IsPathRooted(path))
            return path;

        return Path.GetFullPath(Path.Combine(baseDirectory, path.Replace('/', Path.DirectorySeparatorChar)));
    }
}
