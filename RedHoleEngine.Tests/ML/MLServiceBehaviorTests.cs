using Microsoft.ML.Data;
using RedHoleEngine.Components;
using RedHoleEngine.Core.ECS;
using RedHoleEngine.ML.Agents;
using RedHoleEngine.ML.Analytics;
using RedHoleEngine.ML.Components;
using RedHoleEngine.ML.Services;

namespace RedHoleEngine.Tests.ML;

public class MLServiceBehaviorTests
{
    private sealed class BinaryRow
    {
        public bool Label { get; set; }
        [VectorType(2)]
        public float[] Features { get; set; } = Array.Empty<float>();
    }

    private sealed class BinaryOut
    {
        public bool PredictedLabel { get; set; }
        public float Score { get; set; }
    }

    private sealed class RegRow
    {
        public float Label { get; set; }
        [VectorType(1)]
        public float[] Features { get; set; } = Array.Empty<float>();
    }

    private sealed class RegOut
    {
        public float Score { get; set; }
    }

    [Fact]
    public void BinaryAndRegression_TrainPredictSaveAndRejectMissingModels()
    {
        using var ml = new MLService(seed: 1);
        Assert.Equal(1, ml.Seed);
        Assert.NotNull(ml.Context);

        var rows = new List<BinaryRow>
        {
            new() { Label = false, Features = new[] { 0f, 0f } },
            new() { Label = false, Features = new[] { 0.1f, 0.2f } },
            new() { Label = true, Features = new[] { 5f, 5f } },
            new() { Label = true, Features = new[] { 4.5f, 5.5f } }
        };
        var model = ml.TrainBinaryClassifier(rows, classifierType: BinaryClassifierType.SdcaLogisticRegression);
        ml.RegisterModel("sep", model);
        Assert.True(ml.HasModel("sep"));
        Assert.Same(model, ml.GetModel("sep"));

        var low = ml.Predict<BinaryRow, BinaryOut>("sep", new BinaryRow { Features = new[] { 0f, 0f } });
        var high = ml.Predict<BinaryRow, BinaryOut>("sep", new BinaryRow { Features = new[] { 5f, 5f } });
        Assert.False(low.PredictedLabel);
        Assert.True(high.PredictedLabel);
        // Cached engine path.
        var highAgain = ml.Predict<BinaryRow, BinaryOut>("sep", new BinaryRow { Features = new[] { 5f, 4f } });
        Assert.True(highAgain.PredictedLabel);

        var dir = Path.Combine(Path.GetTempPath(), "ml-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var path = Path.Combine(dir, "nested", "model.zip");
            ml.SaveModel("sep", path);
            ml.UnloadModel("sep");
            Assert.False(ml.HasModel("sep"));
            ml.LoadModel("sep", path);
            Assert.True(ml.HasModel("sep"));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }

        Assert.Throws<KeyNotFoundException>(() => ml.GetModel("missing"));
        Assert.Throws<FileNotFoundException>(() => ml.LoadModel("x", "no-such-model.zip"));
        Assert.Throws<KeyNotFoundException>(() => ml.SaveModel("missing", "out.zip"));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            ml.TrainBinaryClassifier(rows, classifierType: (BinaryClassifierType)99));

        var regRows = Enumerable.Range(0, 8).Select(i => new RegRow { Label = i, Features = new[] { (float)i } }).ToList();
        var reg = ml.TrainRegressor(regRows, regressorType: RegressorType.Sdca);
        ml.RegisterModel("reg", reg);
        var pred = ml.Predict<RegRow, RegOut>("reg", new RegRow { Features = new[] { 3f } });
        Assert.True(float.IsFinite(pred.Score));

        var view = ml.CreateDataView(regRows);
        Assert.NotNull(view);
        Assert.NotNull(ml.ConcatenateFeatures("Features", "A", "B"));
        Assert.NotNull(ml.NormalizeFeatures("Features"));
        Assert.NotNull(ml.MapLabelToKey("Label"));

        ml.Dispose();
        ml.Dispose();
    }

    [Fact]
    public void Systems_RecordPlayerState_AndClampDifficultyWithoutAModel()
    {
        using var world = new World();
        var analytics = world.AddSystem<PlayerAnalyticsSystem>();
        var difficulty = world.AddSystem<DifficultyAdapterSystem>();
        var anomalies = world.AddSystem<AnomalyDetectionSystem>();
        var agents = world.AddSystem<MLAgentSystem>();

        var player = world.CreateEntity();
        world.AddComponent(player, PlayerBehaviorComponent.Create("session"));
        world.GetComponent<PlayerBehaviorComponent>(player).RecordInput();
        analytics.RecordDeath(player);
        analytics.RecordLevelComplete(player, 12f);
        analytics.RecordInput(player);
        var summary = analytics.GetSummary(player);
        Assert.NotNull(summary);
        Assert.Equal(1, summary!.Deaths);
        Assert.Equal(1, summary.CompletedLevels);
        Assert.True(summary.PlayTimeMinutes >= 0f);

        var diffEntity = world.CreateEntity();
        world.AddComponent(diffEntity, DifficultyComponent.Create(1f, 0.5f, 2f));
        difficulty.SetTargetDifficulty(diffEntity, 5f);
        Assert.Equal(2f, world.GetComponent<DifficultyComponent>(diffEntity).TargetDifficulty);
        float old = 0, next = 0;
        difficulty.OnDifficultyChanged += (_, a, b) => { old = a; next = b; };
        difficulty.SetDifficultyImmediate(diffEntity, 0.1f);
        Assert.Equal(0.5f, world.GetComponent<DifficultyComponent>(diffEntity).DifficultyMultiplier);
        Assert.Equal(1f, old);
        Assert.Equal(0.5f, next);
        difficulty.SetUpdateInterval(0.01f);
        difficulty.Update(0.05f);
        Assert.InRange(world.GetComponent<DifficultyComponent>(diffEntity).DifficultyMultiplier, 0.5f, 2f);

        var monitor = world.CreateEntity();
        world.AddComponent(monitor, AnomalyMonitorComponent.Create("none", 0.2f, 0.01f));
        anomalies.Update(1f);
        anomalies.ResetFlags(monitor);
        Assert.Empty(anomalies.GetFlaggedEntities());

        var agent = world.CreateEntity();
        world.AddComponent(agent, new TransformComponent());
        world.AddComponent(agent, MLAgentComponent.Create("missing-model", decisionInterval: 0f));
        agents.FeatureExtractor = _ => new[] { 1f, 2f };
        agents.Update(0.1f);
        Assert.Equal(-1, world.GetComponent<MLAgentComponent>(agent).LastAction);

        Assert.Equal(0f, MLAgentComponent.Create("m").LastConfidence);
        var behavior = PlayerBehaviorComponent.Create();
        behavior.RecordInput();
        behavior.TimeSinceInput = 2f;
        behavior.RecordInput();
        Assert.Equal(2, behavior.InputCount);
        Assert.Equal(0f, behavior.TimeSinceInput);
    }
}
