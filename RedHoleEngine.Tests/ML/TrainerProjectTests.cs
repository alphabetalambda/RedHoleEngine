extern alias trainer;

using trainer::RedHoleML.Trainer;

namespace RedHoleEngine.Tests.ML;

public class TrainerProjectTests : IDisposable
{
    private readonly string _tempDir;

    public TrainerProjectTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "rh_trainerproject_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch (IOException) { }
    }

    private string WriteCsv(string name = "data.csv")
    {
        var path = Path.Combine(_tempDir, name);
        File.WriteAllLines(path, new[]
        {
            "speed,angle,hit",
            "1.0,10,true",
            "2.5,20,false",
            "3.0,30,true",
        });
        return path;
    }

    [Fact]
    public void Json_RoundTripsSettings_WithRelativePaths()
    {
        var project = new TrainerProject
        {
            DatasetPath = Path.Combine(_tempDir, "data", "set.csv"),
            DatasetHasHeader = false,
            DatasetSeparator = ";",
            LabelColumn = "hit",
            FeatureColumns = { "speed", "angle" },
            ModelType = ModelType.Regression_FastTree,
            TrainTestSplit = 0.7f,
            ModelPath = Path.Combine(_tempDir, "model.zip"),
        };
        project.Hyperparameters.NumberOfTrees = 42;
        project.Hyperparameters.LearningRate = 0.25f;

        var json = project.ToJson(_tempDir);
        Assert.Contains("\"data/set.csv\"", json);
        Assert.Contains("Regression_FastTree", json);
        Assert.DoesNotContain(_tempDir, json);

        var loaded = TrainerProject.FromJson(json, _tempDir);
        Assert.Equal(project.DatasetPath, loaded.DatasetPath);
        Assert.Equal(project.ModelPath, loaded.ModelPath);
        Assert.False(loaded.DatasetHasHeader);
        Assert.Equal(';', loaded.GetSeparator());
        Assert.Equal("hit", loaded.LabelColumn);
        Assert.Equal(new[] { "speed", "angle" }, loaded.FeatureColumns);
        Assert.Equal(ModelType.Regression_FastTree, loaded.ModelType);
        Assert.Equal(42, loaded.Hyperparameters.NumberOfTrees);
        Assert.Equal(0.25f, loaded.Hyperparameters.LearningRate);
        Assert.Equal(0.7f, loaded.TrainTestSplit);
    }

    [Fact]
    public void Json_RejectsInvalidAndNewerFormats()
    {
        Assert.Throws<InvalidDataException>(() => TrainerProject.FromJson("not json"));
        Assert.Throws<InvalidDataException>(() => TrainerProject.FromJson("null"));
        Assert.Throws<InvalidDataException>(() => TrainerProject.FromJson("{\"formatVersion\": 999}"));
    }

    [Fact]
    public void MakeRelative_LeavesUnrelatedOrRelativePathsAlone()
    {
        Assert.Equal("", TrainerProject.MakeRelative("", _tempDir));
        Assert.Equal("already/relative.csv", TrainerProject.MakeRelative("already/relative.csv", _tempDir));
        Assert.Equal("x.csv", TrainerProject.MakeRelative(Path.Combine(_tempDir, "x.csv"), _tempDir));
        Assert.Equal(Path.Combine(_tempDir, "x.csv"), TrainerProject.MakeRelative(Path.Combine(_tempDir, "x.csv"), null));
    }

    [Fact]
    public void GetModelPathFor_ReplacesProjectExtension()
    {
        var modelPath = TrainerProject.GetModelPathFor(Path.Combine(_tempDir, "proj.rhml"));
        Assert.Equal(Path.Combine(_tempDir, "proj.model.zip"), modelPath);
    }

    [Fact]
    public void SaveAndLoad_RestoresDatasetAndColumnSelection()
    {
        var csv = WriteCsv();
        var projectPath = Path.Combine(_tempDir, "proj" + TrainerProject.FileExtension);

        using (var state = new TrainerState())
        {
            state.LoadDatasetFromCsv(csv);
            state.LabelColumn = "hit";
            state.FeatureColumns.AddRange(new[] { "speed", "angle" });
            state.ModelType = ModelType.BinaryClassification_FastTree;
            state.Hyperparameters.NumberOfLeaves = 7;
            state.TrainTestSplit = 0.6f;

            TrainerProject.Save(state, projectPath);
        }

        Assert.True(File.Exists(projectPath));
        Assert.False(File.Exists(TrainerProject.GetModelPathFor(projectPath))); // nothing trained

        using var restored = new TrainerState();
        var warnings = TrainerProject.Load(restored, projectPath);

        Assert.Empty(warnings);
        Assert.True(restored.HasDataset);
        Assert.Equal(Path.GetFullPath(csv), restored.DatasetPath);
        Assert.Equal(3, restored.DatasetRowCount);
        Assert.Equal("hit", restored.LabelColumn);
        Assert.Equal(new[] { "speed", "angle" }, restored.FeatureColumns);
        Assert.Equal(ModelType.BinaryClassification_FastTree, restored.ModelType);
        Assert.Equal(7, restored.Hyperparameters.NumberOfLeaves);
        Assert.Equal(0.6f, restored.TrainTestSplit);
    }

    [Fact]
    public void Load_WithMissingDataset_WarnsButKeepsModelSettings()
    {
        var projectPath = Path.Combine(_tempDir, "orphan.rhml");
        var project = new TrainerProject
        {
            DatasetPath = "gone.csv",
            LabelColumn = "y",
            ModelType = ModelType.Clustering_KMeans,
        };
        File.WriteAllText(projectPath, project.ToJson(_tempDir));

        using var state = new TrainerState();
        var warnings = TrainerProject.Load(state, projectPath);

        Assert.Single(warnings);
        Assert.Contains("Dataset not found", warnings[0]);
        Assert.False(state.HasDataset);
        Assert.Equal(ModelType.Clustering_KMeans, state.ModelType);
    }

    [Fact]
    public void Load_DropsColumnsThatNoLongerExist()
    {
        var csv = WriteCsv();
        var projectPath = Path.Combine(_tempDir, "stale.rhml");
        var project = new TrainerProject
        {
            DatasetPath = csv,
            LabelColumn = "removed_label",
            FeatureColumns = { "speed", "removed_feature" },
        };
        File.WriteAllText(projectPath, project.ToJson(_tempDir));

        using var state = new TrainerState();
        var warnings = TrainerProject.Load(state, projectPath);

        Assert.Equal("", state.LabelColumn);
        Assert.Equal(new[] { "speed" }, state.FeatureColumns);
        Assert.Equal(2, warnings.Count);
    }
}
