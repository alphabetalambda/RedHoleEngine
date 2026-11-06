using System.Numerics;
using Microsoft.ML;
using Microsoft.ML.Data;
using RedHoleEngine.Components;
using RedHoleEngine.Core.ECS;
using RedHoleEngine.ML.Agents;
using RedHoleEngine.ML.Components;
using RedHoleEngine.ML.Services;

namespace RedHoleEngine.Tests.ML;

public class MLAgentSystemTests
{
    [Fact]
    public void Update_WithoutWorldOrService_DoesNothing()
    {
        var detached = new MLAgentSystem();
        detached.Update(1f);

        using var world = new World();
        var system = world.AddSystem(new MLAgentSystem());
        var entity = AddAgent(world, "missing", MLAgentType.Classifier, 0f);
        world.Update(1f);

        Assert.Equal(0f, world.GetComponent<MLAgentComponent>(entity).TimeSinceDecision);
        Assert.Equal(-1, world.GetComponent<MLAgentComponent>(entity).LastAction);
    }

    [Fact]
    public void Update_WithoutTransform_SkipsAgent()
    {
        using var world = new World();
        using var service = new MLService(1);
        var system = world.AddSystem(new MLAgentSystem());
        system.SetMLService(service);
        var entity = world.CreateEntity();
        world.AddComponent(entity, MLAgentComponent.Create("m", decisionInterval: 0f));

        world.Update(2f);

        Assert.Equal(0f, world.GetComponent<MLAgentComponent>(entity).TimeSinceDecision);
    }

    [Fact]
    public void Update_InactiveAgent_DoesNotAccumulateOrDecide()
    {
        using var world = new World();
        using var service = TrainedService(out _, out _, out _);
        var system = world.AddSystem(new MLAgentSystem());
        system.SetMLService(service);
        var decisions = 0;
        system.OnAgentDecision += (_, _, _) => decisions++;
        var entity = AddAgent(world, "cls", MLAgentType.Classifier, 0f);
        world.GetComponent<MLAgentComponent>(entity).IsActive = false;

        world.Update(5f);

        Assert.Equal(0, decisions);
        Assert.Equal(0f, world.GetComponent<MLAgentComponent>(entity).TimeSinceDecision);
        Assert.Equal(-1, world.GetComponent<MLAgentComponent>(entity).LastAction);
    }

    [Fact]
    public void Update_BeforeDecisionInterval_AccumulatesWithoutPredicting()
    {
        using var world = new World();
        using var service = TrainedService(out _, out _, out _);
        var system = world.AddSystem(new MLAgentSystem());
        system.SetMLService(service);
        var decisions = 0;
        system.OnAgentDecision += (_, _, _) => decisions++;
        var entity = AddAgent(world, "reg", MLAgentType.Regressor, 1f);
        system.FeatureExtractor = _ => new[] { 4f, 0f };

        world.Update(0.4f);
        world.Update(0.4f);

        ref var agent = ref world.GetComponent<MLAgentComponent>(entity);
        Assert.Equal(0, decisions);
        Assert.Equal(0.8f, agent.TimeSinceDecision, 0.0001f);
        Assert.Equal(-1, agent.LastAction);
    }

    [Fact]
    public void Update_WhenIntervalElapses_ResetsTimerAndDecides()
    {
        using var world = new World();
        using var service = TrainedService(out _, out _, out _);
        var system = world.AddSystem(new MLAgentSystem());
        system.SetMLService(service);
        var decisions = 0;
        system.OnAgentDecision += (_, _, _) => decisions++;
        var entity = AddAgent(world, "reg", MLAgentType.Regressor, 1f);
        system.FeatureExtractor = _ => new[] { 4f, 0f };

        world.Update(0.4f);
        world.Update(0.7f);

        ref var agent = ref world.GetComponent<MLAgentComponent>(entity);
        Assert.Equal(1, decisions);
        Assert.Equal(0f, agent.TimeSinceDecision);
        Assert.Equal(1f, agent.LastConfidence);
        Assert.Equal(4, agent.LastAction);
    }

    [Fact]
    public void Update_ZeroInterval_DecidesEveryUpdate()
    {
        using var world = new World();
        using var service = TrainedService(out _, out _, out _);
        var system = world.AddSystem(new MLAgentSystem());
        system.SetMLService(service);
        var decisions = 0;
        system.OnAgentDecision += (_, _, _) => decisions++;
        AddAgent(world, "reg", MLAgentType.Regressor, 0f);
        system.FeatureExtractor = _ => new[] { 1f, 0f };

        world.Update(0f);
        world.Update(0.01f);

        Assert.Equal(2, decisions);
    }

    [Fact]
    public void Update_MissingModel_ResetsTimerWithoutDecision()
    {
        using var world = new World();
        using var service = new MLService(1);
        var system = world.AddSystem(new MLAgentSystem());
        system.SetMLService(service);
        var decisions = 0;
        system.OnAgentDecision += (_, _, _) => decisions++;
        var entity = AddAgent(world, "missing", MLAgentType.Classifier, 0f);

        world.Update(0.2f);

        Assert.Equal(0, decisions);
        Assert.Equal(0f, world.GetComponent<MLAgentComponent>(entity).TimeSinceDecision);
        Assert.Equal(-1, world.GetComponent<MLAgentComponent>(entity).LastAction);
    }

    [Fact]
    public void Update_Classifier_UsesDefaultPositionFeatures()
    {
        using var world = new World();
        using var service = TrainedService(out var classifier, out _, out _);
        service.RegisterModel("cls", classifier);
        var system = world.AddSystem(new MLAgentSystem());
        system.SetMLService(service);
        Entity? seen = null;
        int seenAction = -1;
        float seenConfidence = -1f;
        system.OnAgentDecision += (entity, action, confidence) =>
        {
            seen = entity;
            seenAction = action;
            seenConfidence = confidence;
        };

        var negative = AddAgent(world, "cls", MLAgentType.Classifier, 0f, new Vector3(0f, 0.2f, 9f));
        var positive = AddAgent(world, "cls", MLAgentType.Classifier, 0f, new Vector3(1f, 0.2f, 9f));

        world.Update(0.1f);

        Assert.Equal(1, world.GetComponent<MLAgentComponent>(negative).LastAction);
        Assert.Equal(0.8f, world.GetComponent<MLAgentComponent>(negative).LastConfidence);
        Assert.Equal(2, world.GetComponent<MLAgentComponent>(positive).LastAction);
        Assert.Equal(0.9f, world.GetComponent<MLAgentComponent>(positive).LastConfidence);
        Assert.Equal(positive, seen);
        Assert.Equal(seenAction, world.GetComponent<MLAgentComponent>(positive).LastAction);
        Assert.Equal(seenConfidence, world.GetComponent<MLAgentComponent>(positive).LastConfidence);
    }

    [Fact]
    public void Update_EmptyFeatureExtractor_FallsBackToTransform()
    {
        using var world = new World();
        using var service = TrainedService(out var classifier, out _, out _);
        service.RegisterModel("cls", classifier);
        var system = world.AddSystem(new MLAgentSystem());
        system.SetMLService(service);
        system.FeatureExtractor = _ => Array.Empty<float>();
        var entity = AddAgent(world, "cls", MLAgentType.Classifier, 0f, new Vector3(0f, 1f, 4f));

        world.Update(0.05f);

        Assert.Equal(1, world.GetComponent<MLAgentComponent>(entity).LastAction);
    }

    [Fact]
    public void Update_FeatureExtractor_OverridesTransform()
    {
        using var world = new World();
        using var service = TrainedService(out var classifier, out _, out _);
        service.RegisterModel("cls", classifier);
        var system = world.AddSystem(new MLAgentSystem());
        system.SetMLService(service);
        system.FeatureExtractor = _ => new[] { 0f, 0f };
        var entity = AddAgent(world, "cls", MLAgentType.Classifier, 0f, new Vector3(1f, 1f, 1f));

        world.Update(0.05f);

        Assert.Equal(1, world.GetComponent<MLAgentComponent>(entity).LastAction);
    }

    [Fact]
    public void Update_Regressor_StoresRoundedScoreAndFullConfidence()
    {
        using var world = new World();
        using var service = TrainedService(out _, out var regressor, out _);
        service.RegisterModel("reg", regressor);
        var system = world.AddSystem(new MLAgentSystem());
        system.SetMLService(service);
        var low = AddAgent(world, "reg", MLAgentType.Regressor, 0f);
        var high = AddAgent(world, "reg", MLAgentType.Regressor, 0f);
        system.FeatureExtractor = entity => entity.Id == low.Id ? new[] { 1f, 0f } : new[] { 8f, 0f };

        world.Update(0.05f);

        ref var lowAgent = ref world.GetComponent<MLAgentComponent>(low);
        ref var highAgent = ref world.GetComponent<MLAgentComponent>(high);
        Assert.Equal(1f, lowAgent.LastConfidence);
        Assert.Equal(1f, highAgent.LastConfidence);
        Assert.Equal(1, lowAgent.LastAction);
        Assert.Equal(8, highAgent.LastAction);
    }

    [Fact]
    public void Update_Clusterer_AssignsDifferentClusters()
    {
        using var world = new World();
        using var service = TrainedService(out _, out _, out var clusterer);
        service.RegisterModel("clu", clusterer);
        var system = world.AddSystem(new MLAgentSystem());
        system.SetMLService(service);
        var origin = AddAgent(world, "clu", MLAgentType.Clusterer, 0f);
        var far = AddAgent(world, "clu", MLAgentType.Clusterer, 0f);
        system.FeatureExtractor = entity => entity.Id == origin.Id ? new[] { 0f, 0f } : new[] { 10f, 10f };

        world.Update(0.05f);

        ref var originAgent = ref world.GetComponent<MLAgentComponent>(origin);
        ref var farAgent = ref world.GetComponent<MLAgentComponent>(far);
        Assert.Equal(1, originAgent.LastAction);
        Assert.Equal(2, farAgent.LastAction);
        Assert.Equal(0.75f, originAgent.LastConfidence);
        Assert.Equal(0.75f, farAgent.LastConfidence);
    }

    [Fact]
    public void Update_SchemaMismatch_IsSwallowed()
    {
        using var world = new World();
        using var service = new MLService(1);
        service.RegisterModel("bad", service.TrainBinaryClassifier(BinaryRows(), classifierType: BinaryClassifierType.SdcaLogisticRegression));
        var system = world.AddSystem(new MLAgentSystem());
        system.SetMLService(service);
        var decisions = 0;
        system.OnAgentDecision += (_, _, _) => decisions++;
        var entity = AddAgent(world, "bad", MLAgentType.Classifier, 0f);
        system.FeatureExtractor = _ => new[] { 2f, 1f };

        world.Update(0.1f);

        Assert.Equal(0, decisions);
        Assert.Equal(-1, world.GetComponent<MLAgentComponent>(entity).LastAction);
    }

    private static Entity AddAgent(World world, string modelId, MLAgentType type, float interval, Vector3? position = null)
    {
        var entity = world.CreateEntity();
        world.AddComponent(entity, MLAgentComponent.Create(modelId, type, interval));
        world.AddComponent(entity, new TransformComponent(position ?? Vector3.Zero));
        return entity;
    }

    private static MLService TrainedService(out ITransformer classifier, out ITransformer regressor, out ITransformer clusterer)
    {
        var service = new MLService(42);
        var data = service.CreateDataView(new[] { new MLAgentSystem.AgentInput { Features = new[] { 0f, 0f } } });
        var ml = service.Context;

        classifier = ml.Transforms.CustomMapping<MLAgentSystem.AgentInput, MLAgentSystem.ClassifierOutput>(MapClassifier, null).Fit(data);
        regressor = ml.Transforms.CustomMapping<MLAgentSystem.AgentInput, MLAgentSystem.RegressorOutput>(MapRegressor, null).Fit(data);
        clusterer = ml.Transforms.CustomMapping<MLAgentSystem.AgentInput, MLAgentSystem.ClustererOutput>(MapCluster, null).Fit(data);
        service.RegisterModel("cls", classifier);
        service.RegisterModel("reg", regressor);
        service.RegisterModel("clu", clusterer);
        return service;
    }

    private static void MapClassifier(MLAgentSystem.AgentInput src, MLAgentSystem.ClassifierOutput dst)
    {
        bool positive = src.Features.Length > 0 && src.Features[0] >= 0.5f;
        dst.PredictedLabel = positive ? 2u : 1u;
        dst.Score = positive ? new[] { 0.1f, 0.9f } : new[] { 0.8f, 0.2f };
    }

    private static void MapRegressor(MLAgentSystem.AgentInput src, MLAgentSystem.RegressorOutput dst)
    {
        dst.Score = src.Features.Length > 0 ? src.Features[0] : 0f;
    }

    private static void MapCluster(MLAgentSystem.AgentInput src, MLAgentSystem.ClustererOutput dst)
    {
        bool far = src.Features.Length > 0 && src.Features[0] > 5f;
        dst.PredictedClusterId = far ? 2u : 1u;
        dst.Distances = far ? new[] { 4f, 0.25f } : new[] { 0.25f, 4f };
    }

    private static List<BinaryRow> BinaryRows()
    {
        var rows = new List<BinaryRow>();
        for (int i = 0; i < 10; i++)
        {
            rows.Add(new BinaryRow { Features = new[] { -2f, -1f }, Label = false });
            rows.Add(new BinaryRow { Features = new[] { 2f, 1f }, Label = true });
        }
        return rows;
    }

    private sealed class BinaryRow
    {
        [VectorType(2)]
        public float[] Features { get; set; } = Array.Empty<float>();
        public bool Label { get; set; }
    }
}
