using RedHoleEngine.Core.ECS;
using RedHoleEngine.ML.Components;
using RedHoleEngine.ML.Services;

namespace RedHoleEngine.ML.Analytics;

/// <summary>
/// System that adapts game difficulty based on player performance using ML predictions.
/// </summary>
public sealed class DifficultyAdapterSystem : GameSystem
{
    private MLService? _mlService;
    private string _difficultyModelId = "difficulty_predictor";
    private float _updateInterval = 5.0f; // How often to recalculate difficulty
    private float _timeSinceUpdate;

    /// <summary>
    /// Event fired when difficulty changes
    /// </summary>
    public event Action<Entity, float, float>? OnDifficultyChanged; // entity, oldDifficulty, newDifficulty

    /// <summary>
    /// Set the ML service for difficulty predictions
    /// </summary>
    public void SetMLService(MLService service)
    {
        _mlService = service;
    }

    /// <summary>
    /// Set the model ID to use for difficulty prediction
    /// </summary>
    public void SetModelId(string modelId)
    {
        _difficultyModelId = modelId;
    }

    /// <summary>
    /// Set how often to update difficulty (in seconds)
    /// </summary>
    public void SetUpdateInterval(float interval)
    {
        _updateInterval = interval;
    }

    public override void Update(float deltaTime)
    {
        if (World == null)
            return;

        _timeSinceUpdate += deltaTime;

        // Smoothly interpolate difficulty toward target
        foreach (var entity in World.Query<DifficultyComponent>())
        {
            ref var difficulty = ref World.GetComponent<DifficultyComponent>(entity);
            
            if (!difficulty.AutoAdjust)
                continue;

            // Smooth interpolation
            float oldDifficulty = difficulty.DifficultyMultiplier;
            difficulty.DifficultyMultiplier = MathF.Max(
                difficulty.MinDifficulty,
                MathF.Min(
                    difficulty.MaxDifficulty,
                    difficulty.DifficultyMultiplier + 
                    (difficulty.TargetDifficulty - difficulty.DifficultyMultiplier) * 
                    difficulty.AdjustmentSpeed * deltaTime
                )
            );

            // Fire event if changed significantly
            if (MathF.Abs(difficulty.DifficultyMultiplier - oldDifficulty) > 0.001f)
            {
                OnDifficultyChanged?.Invoke(entity, oldDifficulty, difficulty.DifficultyMultiplier);
            }
        }

        // Periodically recalculate target difficulty based on player behavior
        if (_timeSinceUpdate >= _updateInterval)
        {
            _timeSinceUpdate = 0f;
            RecalculateTargetDifficulties();
        }
    }

    private void RecalculateTargetDifficulties()
    {
        if (_mlService == null || !_mlService.HasModel(_difficultyModelId))
            return;

        // Find player behavior data
        PlayerBehaviorComponent? playerBehavior = null;
        foreach (var entity in World!.Query<PlayerBehaviorComponent>())
        {
            playerBehavior = World.GetComponent<PlayerBehaviorComponent>(entity);
            break;
        }

        if (playerBehavior == null)
            return;

        // Update all difficulty components based on player behavior
        foreach (var entity in World.Query<DifficultyComponent>())
        {
            ref var difficulty = ref World.GetComponent<DifficultyComponent>(entity);
            
            if (!difficulty.AutoAdjust)
                continue;

            try
            {
                var features = ExtractPlayerFeatures(playerBehavior.Value);
                var input = new DifficultyInput { Features = features };
                var prediction = _mlService.Predict<DifficultyInput, DifficultyOutput>(_difficultyModelId, input);
                
                difficulty.TargetDifficulty = MathF.Max(
                    difficulty.MinDifficulty,
                    MathF.Min(difficulty.MaxDifficulty, prediction.PredictedDifficulty)
                );
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[DifficultyAdapter] Prediction failed: {ex.Message}");
            }
        }
    }

    private float[] ExtractPlayerFeatures(PlayerBehaviorComponent behavior)
    {
        return new[]
        {
            behavior.SkillLevel,
            behavior.Deaths / MathF.Max(1f, behavior.PlayTime / 60f), // Deaths per minute
            behavior.CompletedLevels / MathF.Max(1f, behavior.PlayTime / 60f), // Completions per minute
            behavior.AverageInputInterval,
            behavior.ChurnRisk
        };
    }

    /// <summary>
    /// Manually set target difficulty (bypasses ML)
    /// </summary>
    public void SetTargetDifficulty(Entity entity, float targetDifficulty)
    {
        if (World == null || !World.HasComponent<DifficultyComponent>(entity))
            return;

        ref var difficulty = ref World.GetComponent<DifficultyComponent>(entity);
        difficulty.TargetDifficulty = MathF.Max(
            difficulty.MinDifficulty,
            MathF.Min(difficulty.MaxDifficulty, targetDifficulty)
        );
    }

    /// <summary>
    /// Immediately set difficulty (no interpolation)
    /// </summary>
    public void SetDifficultyImmediate(Entity entity, float newDifficulty)
    {
        if (World == null || !World.HasComponent<DifficultyComponent>(entity))
            return;

        ref var difficulty = ref World.GetComponent<DifficultyComponent>(entity);
        float clamped = MathF.Max(
            difficulty.MinDifficulty,
            MathF.Min(difficulty.MaxDifficulty, newDifficulty)
        );
        
        float old = difficulty.DifficultyMultiplier;
        difficulty.DifficultyMultiplier = clamped;
        difficulty.TargetDifficulty = clamped;
        
        OnDifficultyChanged?.Invoke(entity, old, clamped);
    }

    #region ML Types

    public class DifficultyInput
    {
        [Microsoft.ML.Data.VectorType]
        public float[] Features { get; set; } = Array.Empty<float>();
    }

    public class DifficultyOutput
    {
        public float PredictedDifficulty { get; set; }
    }

    #endregion
}
