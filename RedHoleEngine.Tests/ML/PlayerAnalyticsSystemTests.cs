using Microsoft.ML;
using Microsoft.ML.Data;
using RedHoleEngine.Core.ECS;
using RedHoleEngine.ML.Analytics;
using RedHoleEngine.ML.Components;
using RedHoleEngine.ML.Services;

namespace RedHoleEngine.Tests.ML;

public class PlayerAnalyticsSystemTests
{
    [Fact]
    public void RecordMethods_WithoutWorldOrComponent_DoNothing()
    {
        var detached = new PlayerAnalyticsSystem();
        var ghost = new Entity(3, 1);

        detached.RecordDeath(ghost);
        detached.RecordLevelComplete(ghost, 10f);
        detached.RecordInput(ghost);
        detached.Update(1f);

        Assert.Null(detached.GetSummary(ghost));
    }

    [Fact]
    public void RecordDeath_RecordLevelComplete_RecordInput_UpdateSummary()
    {
        using var world = new World();
        var system = world.AddSystem(new PlayerAnalyticsSystem());
        var entity = world.CreateEntity();
        world.AddComponent(entity, PlayerBehaviorComponent.Create("alpha"));

        system.RecordDeath(entity);
        system.RecordDeath(entity);
        system.RecordLevelComplete(entity, 25f);
        system.RecordLevelComplete(entity, 5f);
        system.RecordInput(entity);
        world.Update(4f);
        system.RecordInput(entity);

        var summary = system.GetSummary(entity);
        Assert.NotNull(summary);
        Assert.Equal("alpha", summary!.SessionId);
        Assert.Equal(2, summary.Deaths);
        Assert.Equal(2, summary.CompletedLevels);
        Assert.Equal(30f, summary.Score);
        Assert.Equal(0.5f, summary.SkillLevel);
        Assert.Equal(0f, summary.ChurnRisk);
        Assert.Equal(4f / 60f, summary.PlayTimeMinutes, 0.0001f);
        Assert.Equal(2f, world.GetComponent<PlayerBehaviorComponent>(entity).AverageInputInterval, 0.0001f);
        Assert.Equal(0.5f, summary.EngagementScore, 0.0001f);
        Assert.Null(system.GetSummary(new Entity(99, 1)));
    }

    [Fact]
    public void Update_WithoutServiceOrBeforeInterval_TracksTimeWithoutAnalysis()
    {
        using var world = new World();
        var system = world.AddSystem(new PlayerAnalyticsSystem());
        var entity = world.CreateEntity();
        world.AddComponent(entity, PlayerBehaviorComponent.Create("s"));
        var skillEvents = 0;
        system.OnSkillLevelUpdated += (_, _) => skillEvents++;

        world.Update(3f);
        system.SetMLService(new MLService(1));
        world.Update(3f);

        ref var behavior = ref world.GetComponent<PlayerBehaviorComponent>(entity);
        Assert.Equal(6f, behavior.PlayTime, 0.0001f);
        Assert.Equal(6f, behavior.TimeSinceInput, 0.0001f);
        Assert.Equal(0.5f, behavior.SkillLevel);
        Assert.Equal(0, skillEvents);
    }

    [Fact]
    public void Update_SkillModel_MovesSkillTowardCompletionAndClamps()
    {
        using var world = new World();
        using var service = new MLService(42);
        service.RegisterModel("skill_predictor", TrainSkill(service));
        var system = world.AddSystem(new PlayerAnalyticsSystem());
        system.SetMLService(service);
        var updates = new List<(Entity Entity, float Skill)>();
        system.OnSkillLevelUpdated += (entity, skill) => updates.Add((entity, skill));

        var strong = AddPlayer(world, completed: 9, deaths: 1, playTime: 3590f, score: 3600f, interval: 1f);
        var weak = AddPlayer(world, completed: 0, deaths: 30, playTime: 3590f, score: 0f, interval: 1f);

        world.Update(10f);

        var strongSkill = world.GetComponent<PlayerBehaviorComponent>(strong).SkillLevel;
        var weakSkill = world.GetComponent<PlayerBehaviorComponent>(weak).SkillLevel;
        Assert.Equal(0.9f, strongSkill, 0.0001f);
        Assert.Equal(0f, weakSkill, 0.0001f);
        Assert.Contains(updates, update => update.Entity.Equals(strong));
        Assert.Contains(updates, update => update.Entity.Equals(weak));
    }

    [Fact]
    public void Update_ChurnModel_WarnsOnlyWhenCrossingThreshold()
    {
        using var world = new World();
        using var service = new MLService(42);
        service.RegisterModel("churn_predictor", TrainChurn(service));
        var system = world.AddSystem(new PlayerAnalyticsSystem());
        system.SetMLService(service);
        system.ChurnWarningThreshold = 0.7f;
        var warnings = new List<Entity>();
        var riskUpdates = 0;
        system.OnChurnWarning += (entity, _) => warnings.Add(entity);
        system.OnChurnRiskUpdated += (_, _) => riskUpdates++;

        var calm = AddPlayer(world, completed: 5, deaths: 0, playTime: 50f, score: 0f, interval: 1f);
        var frustrated = AddPlayer(world, completed: 0, deaths: 10, playTime: 50f, score: 0f, interval: 1f);
        world.GetComponent<PlayerBehaviorComponent>(calm).SkillLevel = 0.5f;
        world.GetComponent<PlayerBehaviorComponent>(frustrated).SkillLevel = 0.5f;

        world.Update(10f);
        var calmRisk = world.GetComponent<PlayerBehaviorComponent>(calm).ChurnRisk;
        var frustratedRisk = world.GetComponent<PlayerBehaviorComponent>(frustrated).ChurnRisk;
        world.Update(10f);

        Assert.Equal(0.1f, calmRisk, 0.0001f);
        Assert.Equal(0.95f, frustratedRisk, 0.0001f);
        Assert.Equal(new[] { frustrated }, warnings);
        Assert.Equal(2, riskUpdates);
        Assert.Equal(0.5f, world.GetComponent<PlayerBehaviorComponent>(calm).SkillLevel);
    }

    [Fact]
    public void Update_SetModelIds_UsesNamedModelsAndIgnoresMissingChurnModel()
    {
        using var world = new World();
        using var service = new MLService(42);
        service.RegisterModel("custom-skill", TrainSkill(service));
        var system = world.AddSystem(new PlayerAnalyticsSystem());
        system.SetMLService(service);
        system.SetModelIds("custom-skill", "missing-churn");
        var warnings = 0;
        system.OnChurnWarning += (_, _) => warnings++;
        var entity = AddPlayer(world, completed: 9, deaths: 1, playTime: 3590f, score: 3600f, interval: 1f);

        world.Update(10f);

        Assert.Equal(0.9f, world.GetComponent<PlayerBehaviorComponent>(entity).SkillLevel, 0.0001f);
        Assert.Equal(0f, world.GetComponent<PlayerBehaviorComponent>(entity).ChurnRisk);
        Assert.Equal(0, warnings);
    }

    [Fact]
    public void Update_BadModel_DoesNotThrowOrChangeSkill()
    {
        using var world = new World();
        using var service = new MLService(1);
        service.RegisterModel("skill_predictor", service.TrainBinaryClassifier(BinaryRows(), classifierType: BinaryClassifierType.SdcaLogisticRegression));
        var system = world.AddSystem(new PlayerAnalyticsSystem());
        system.SetMLService(service);
        var entity = AddPlayer(world, completed: 1, deaths: 0, playTime: 0f, score: 0f, interval: 1f);

        world.Update(10f);

        Assert.Equal(0.5f, world.GetComponent<PlayerBehaviorComponent>(entity).SkillLevel);
    }

    private static Entity AddPlayer(World world, int completed, int deaths, float playTime, float score, float interval)
    {
        var entity = world.CreateEntity();
        var behavior = PlayerBehaviorComponent.Create("player");
        behavior.CompletedLevels = completed;
        behavior.Deaths = deaths;
        behavior.PlayTime = playTime;
        behavior.Score = score;
        behavior.AverageInputInterval = interval;
        world.AddComponent(entity, behavior);
        return entity;
    }

    private static ITransformer TrainSkill(MLService service)
    {
        var data = service.CreateDataView(new[] { new PlayerAnalyticsSystem.PlayerInput { Features = new float[5] } });
        return service.Context.Transforms
            .CustomMapping<PlayerAnalyticsSystem.PlayerInput, PlayerAnalyticsSystem.SkillOutput>(MapSkill, null)
            .Fit(data);
    }

    private static ITransformer TrainChurn(MLService service)
    {
        var data = service.CreateDataView(new[] { new PlayerAnalyticsSystem.PlayerInput { Features = new float[5] } });
        return service.Context.Transforms
            .CustomMapping<PlayerAnalyticsSystem.PlayerInput, PlayerAnalyticsSystem.ChurnOutput>(MapChurn, null)
            .Fit(data);
    }

    private static void MapSkill(PlayerAnalyticsSystem.PlayerInput src, PlayerAnalyticsSystem.SkillOutput dst)
    {
        dst.SkillLevel = src.Features.Length > 0 ? src.Features[0] : 0f;
    }

    private static void MapChurn(PlayerAnalyticsSystem.PlayerInput src, PlayerAnalyticsSystem.ChurnOutput dst)
    {
        float frustration = src.Features.Length > 2 ? src.Features[2] : 0f;
        dst.ChurnProbability = frustration > 1f ? 0.95f : 0.1f;
    }

    private static List<BinaryRow> BinaryRows()
    {
        var rows = new List<BinaryRow>();
        for (int i = 0; i < 8; i++)
        {
            rows.Add(new BinaryRow { Features = new[] { -1f, -1f }, Label = false });
            rows.Add(new BinaryRow { Features = new[] { 1f, 1f }, Label = true });
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
