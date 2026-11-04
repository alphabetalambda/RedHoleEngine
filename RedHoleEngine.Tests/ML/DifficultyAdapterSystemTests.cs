using Microsoft.ML;
using Microsoft.ML.Data;
using RedHoleEngine.Core.ECS;
using RedHoleEngine.ML.Analytics;
using RedHoleEngine.ML.Components;
using RedHoleEngine.ML.Services;

namespace RedHoleEngine.Tests.ML;

public class DifficultyAdapterSystemTests
{
    [Fact]
    public void Update_WithoutWorld_DoesNothing()
    {
        var system = new DifficultyAdapterSystem();
        system.Update(1f);
        system.SetTargetDifficulty(new Entity(1, 1), 2f);
        system.SetDifficultyImmediate(new Entity(1, 1), 2f);
    }

    [Fact]
    public void Update_InterpolatesTowardTargetAndClamps()
    {
        using var world = new World();
        var system = world.AddSystem(new DifficultyAdapterSystem());
        var changes = new List<(float Old, float New)>();
        system.OnDifficultyChanged += (_, oldValue, newValue) => changes.Add((oldValue, newValue));
        var entity = AddDifficulty(world, multiplier: 1f, target: 2f, speed: 0.25f, min: 0f, max: 3f);

        world.Update(2f);

        Assert.Equal(1.5f, world.GetComponent<DifficultyComponent>(entity).DifficultyMultiplier, 0.0001f);
        Assert.Equal((1f, 1.5f), Assert.Single(changes));

        world.GetComponent<DifficultyComponent>(entity).DifficultyMultiplier = 1.8f;
        world.GetComponent<DifficultyComponent>(entity).TargetDifficulty = 3f;
        world.GetComponent<DifficultyComponent>(entity).AdjustmentSpeed = 5f;
        world.GetComponent<DifficultyComponent>(entity).MaxDifficulty = 2f;
        world.Update(1f);
        Assert.Equal(2f, world.GetComponent<DifficultyComponent>(entity).DifficultyMultiplier, 0.0001f);

        world.GetComponent<DifficultyComponent>(entity).DifficultyMultiplier = 0.7f;
        world.GetComponent<DifficultyComponent>(entity).TargetDifficulty = 0f;
        world.GetComponent<DifficultyComponent>(entity).MinDifficulty = 0.5f;
        world.Update(1f);
        Assert.Equal(0.5f, world.GetComponent<DifficultyComponent>(entity).DifficultyMultiplier, 0.0001f);
    }

    [Fact]
    public void Update_TinyOrDisabledAdjustment_DoesNotRaiseEvent()
    {
        using var world = new World();
        var system = world.AddSystem(new DifficultyAdapterSystem());
        var changes = 0;
        system.OnDifficultyChanged += (_, _, _) => changes++;
        var quiet = AddDifficulty(world, 1f, 1.0001f, 1f, 0f, 3f);
        var frozen = AddDifficulty(world, 1f, 2f, 1f, 0f, 3f);
        world.GetComponent<DifficultyComponent>(frozen).AutoAdjust = false;

        world.Update(1f);

        Assert.Equal(0, changes);
        Assert.Equal(1.0001f, world.GetComponent<DifficultyComponent>(quiet).DifficultyMultiplier, 0.0000001f);
        Assert.Equal(1f, world.GetComponent<DifficultyComponent>(frozen).DifficultyMultiplier);
    }

    [Fact]
    public void Update_WithoutServiceModelOrPlayer_StillInterpolatesButKeepsTarget()
    {
        using var world = new World();
        var system = world.AddSystem(new DifficultyAdapterSystem());
        system.SetUpdateInterval(0.1f);
        var entity = AddDifficulty(world, 1f, 2f, 1f, 0f, 3f);

        world.Update(0.5f);

        Assert.Equal(1.5f, world.GetComponent<DifficultyComponent>(entity).DifficultyMultiplier, 0.0001f);
        Assert.Equal(2f, world.GetComponent<DifficultyComponent>(entity).TargetDifficulty);

        using var service = new MLService(1);
        system.SetMLService(service);
        world.Update(0.5f);
        Assert.Equal(2f, world.GetComponent<DifficultyComponent>(entity).TargetDifficulty);
    }

    [Fact]
    public void Update_BeforeInterval_DoesNotRecalculateTarget()
    {
        using var world = new World();
        using var service = new MLService(42);
        service.RegisterModel("difficulty_predictor", TrainConstant(service, 1.7f));
        var system = world.AddSystem(new DifficultyAdapterSystem());
        system.SetMLService(service);
        system.SetUpdateInterval(5f);
        AddPlayer(world, skill: 0.5f);
        var entity = AddDifficulty(world, 1f, 1f, 0f, 0f, 2f);

        world.Update(4.9f);

        Assert.Equal(1f, world.GetComponent<DifficultyComponent>(entity).TargetDifficulty);
    }

    [Fact]
    public void Update_SkillSignal_MovesTargetAndClampsPrediction()
    {
        using var world = new World();
        using var service = new MLService(42);
        service.RegisterModel("difficulty_predictor", TrainIdentity(service));
        var system = world.AddSystem(new DifficultyAdapterSystem());
        system.SetMLService(service);
        system.SetUpdateInterval(1f);
        var player = AddPlayer(world, skill: 0.2f);
        var adjusting = AddDifficulty(world, 1f, 1f, 0f, 0f, 2f);
        var manual = AddDifficulty(world, 1.2f, 1.2f, 0f, 0f, 2f);
        world.GetComponent<DifficultyComponent>(manual).AutoAdjust = false;

        world.Update(1f);
        float lowTarget = world.GetComponent<DifficultyComponent>(adjusting).TargetDifficulty;

        world.GetComponent<PlayerBehaviorComponent>(player).SkillLevel = 0.9f;
        world.Update(1f);
        float highTarget = world.GetComponent<DifficultyComponent>(adjusting).TargetDifficulty;

        Assert.Equal(0.2f, lowTarget, 0.0001f);
        Assert.Equal(0.9f, highTarget, 0.0001f);
        Assert.Equal(1.2f, world.GetComponent<DifficultyComponent>(manual).TargetDifficulty);
    }

    [Fact]
    public void Update_PredictionAboveMax_ClampsTarget()
    {
        using var world = new World();
        using var service = new MLService(42);
        service.RegisterModel("custom", TrainConstant(service, 5f));
        var system = world.AddSystem(new DifficultyAdapterSystem());
        system.SetMLService(service);
        system.SetModelId("custom");
        system.SetUpdateInterval(1f);
        AddPlayer(world, 0.5f);
        var entity = AddDifficulty(world, 1f, 1f, 0f, 0.5f, 1.2f);

        world.Update(1f);

        Assert.Equal(1.2f, world.GetComponent<DifficultyComponent>(entity).TargetDifficulty, 0.0001f);
    }

    [Fact]
    public void Update_NoPlayerOrBadModel_LeavesTarget()
    {
        using var world = new World();
        using var service = new MLService(1);
        service.RegisterModel("difficulty_predictor", service.TrainRegressor(LineRows(), regressorType: RegressorType.Sdca));
        var system = world.AddSystem(new DifficultyAdapterSystem());
        system.SetMLService(service);
        system.SetUpdateInterval(0.1f);
        var entity = AddDifficulty(world, 1f, 1.4f, 0f, 0f, 2f);

        world.Update(0.2f);
        Assert.Equal(1.4f, world.GetComponent<DifficultyComponent>(entity).TargetDifficulty);

        AddPlayer(world, 0.5f);
        world.Update(0.2f);
        Assert.Equal(1.4f, world.GetComponent<DifficultyComponent>(entity).TargetDifficulty);
    }

    [Fact]
    public void SetTargetDifficulty_ClampsWithoutEvent()
    {
        using var world = new World();
        var system = world.AddSystem(new DifficultyAdapterSystem());
        var events = 0;
        system.OnDifficultyChanged += (_, _, _) => events++;
        var entity = AddDifficulty(world, 1f, 1f, 0.1f, 0.5f, 1.5f);

        system.SetTargetDifficulty(entity, 9f);
        Assert.Equal(1.5f, world.GetComponent<DifficultyComponent>(entity).TargetDifficulty);
        system.SetTargetDifficulty(entity, -4f);
        system.SetTargetDifficulty(new Entity(40, 1), 1f);

        Assert.Equal(0, events);
        Assert.Equal(0.5f, world.GetComponent<DifficultyComponent>(entity).TargetDifficulty);
        Assert.Equal(1f, world.GetComponent<DifficultyComponent>(entity).DifficultyMultiplier);
    }

    [Fact]
    public void SetDifficultyImmediate_ClampsBothValuesAndAlwaysNotifies()
    {
        using var world = new World();
        var system = world.AddSystem(new DifficultyAdapterSystem());
        float oldSeen = -1f;
        float newSeen = -1f;
        system.OnDifficultyChanged += (_, oldValue, newValue) =>
        {
            oldSeen = oldValue;
            newSeen = newValue;
        };
        var entity = AddDifficulty(world, 0.8f, 0.8f, 0.1f, 0.5f, 1.5f);

        system.SetDifficultyImmediate(entity, 4f);

        Assert.Equal(0.8f, oldSeen);
        Assert.Equal(1.5f, newSeen);
        Assert.Equal(1.5f, world.GetComponent<DifficultyComponent>(entity).DifficultyMultiplier);
        Assert.Equal(1.5f, world.GetComponent<DifficultyComponent>(entity).TargetDifficulty);

        system.SetDifficultyImmediate(entity, 1.5f);
        Assert.Equal(1.5f, newSeen);
        system.SetDifficultyImmediate(new Entity(8, 1), 1f);
    }

    private static Entity AddDifficulty(World world, float multiplier, float target, float speed, float min, float max)
    {
        var entity = world.CreateEntity();
        var difficulty = DifficultyComponent.Create(multiplier, min, max);
        difficulty.TargetDifficulty = target;
        difficulty.AdjustmentSpeed = speed;
        world.AddComponent(entity, difficulty);
        return entity;
    }

    private static Entity AddPlayer(World world, float skill)
    {
        var entity = world.CreateEntity();
        var behavior = PlayerBehaviorComponent.Create("player");
        behavior.SkillLevel = skill;
        world.AddComponent(entity, behavior);
        return entity;
    }

    private static ITransformer TrainIdentity(MLService service)
    {
        return Fit(service, static (src, dst) =>
        {
            dst.PredictedDifficulty = src.Features.Length > 0 ? src.Features[0] : 0f;
        });
    }

    private static ITransformer TrainConstant(MLService service, float value)
    {
        return Fit(service, (_, dst) => dst.PredictedDifficulty = value);
    }

    private static ITransformer Fit(MLService service, Action<DifficultyAdapterSystem.DifficultyInput, DifficultyAdapterSystem.DifficultyOutput> map)
    {
        var data = service.CreateDataView(new[] { new DifficultyAdapterSystem.DifficultyInput { Features = new float[5] } });
        return service.Context.Transforms
            .CustomMapping(map, null)
            .Fit(data);
    }

    private static List<LineRow> LineRows()
    {
        var rows = new List<LineRow>();
        for (int i = 0; i <= 4; i++)
            rows.Add(new LineRow { Features = new[] { (float)i }, Label = i });
        return rows;
    }

    private sealed class LineRow
    {
        [VectorType(1)]
        public float[] Features { get; set; } = Array.Empty<float>();
        public float Label { get; set; }
    }
}
