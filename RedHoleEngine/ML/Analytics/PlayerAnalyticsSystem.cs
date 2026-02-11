using RedHoleEngine.Core.ECS;
using RedHoleEngine.ML.Components;
using RedHoleEngine.ML.Services;

namespace RedHoleEngine.ML.Analytics;

/// <summary>
/// System that tracks player behavior and uses ML to predict engagement, skill level, and churn risk.
/// </summary>
public sealed class PlayerAnalyticsSystem : GameSystem
{
    private MLService? _mlService;
    private string _skillModelId = "skill_predictor";
    private string _churnModelId = "churn_predictor";
    private float _analysisInterval = 10.0f;
    private float _timeSinceAnalysis;

    /// <summary>
    /// Event fired when player skill level is updated
    /// </summary>
    public event Action<Entity, float>? OnSkillLevelUpdated;
    
    /// <summary>
    /// Event fired when churn risk is updated
    /// </summary>
    public event Action<Entity, float>? OnChurnRiskUpdated;
    
    /// <summary>
    /// Event fired when potential churn is detected (risk > threshold)
    /// </summary>
    public event Action<Entity, float>? OnChurnWarning;
    
    /// <summary>
    /// Threshold for churn warning (0-1)
    /// </summary>
    public float ChurnWarningThreshold { get; set; } = 0.7f;

    /// <summary>
    /// Set the ML service for predictions
    /// </summary>
    public void SetMLService(MLService service)
    {
        _mlService = service;
    }

    /// <summary>
    /// Set the model IDs for skill and churn prediction
    /// </summary>
    public void SetModelIds(string skillModelId, string churnModelId)
    {
        _skillModelId = skillModelId;
        _churnModelId = churnModelId;
    }

    public override void Update(float deltaTime)
    {
        if (World == null)
            return;

        // Update play time and input tracking
        foreach (var entity in World.Query<PlayerBehaviorComponent>())
        {
            ref var behavior = ref World.GetComponent<PlayerBehaviorComponent>(entity);
            behavior.PlayTime += deltaTime;
            behavior.TimeSinceInput += deltaTime;
        }

        // Periodically run ML analysis
        _timeSinceAnalysis += deltaTime;
        if (_timeSinceAnalysis >= _analysisInterval && _mlService != null)
        {
            _timeSinceAnalysis = 0f;
            RunAnalysis();
        }
    }

    private void RunAnalysis()
    {
        foreach (var entity in World!.Query<PlayerBehaviorComponent>())
        {
            ref var behavior = ref World.GetComponent<PlayerBehaviorComponent>(entity);
            
            // Predict skill level
            if (_mlService!.HasModel(_skillModelId))
            {
                try
                {
                    var skillFeatures = ExtractSkillFeatures(behavior);
                    var input = new PlayerInput { Features = skillFeatures };
                    var prediction = _mlService.Predict<PlayerInput, SkillOutput>(_skillModelId, input);
                    
                    float oldSkill = behavior.SkillLevel;
                    behavior.SkillLevel = MathF.Max(0f, MathF.Min(1f, prediction.SkillLevel));
                    
                    if (MathF.Abs(behavior.SkillLevel - oldSkill) > 0.01f)
                    {
                        OnSkillLevelUpdated?.Invoke(entity, behavior.SkillLevel);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[PlayerAnalytics] Skill prediction failed: {ex.Message}");
                }
            }

            // Predict churn risk
            if (_mlService.HasModel(_churnModelId))
            {
                try
                {
                    var churnFeatures = ExtractChurnFeatures(behavior);
                    var input = new PlayerInput { Features = churnFeatures };
                    var prediction = _mlService.Predict<PlayerInput, ChurnOutput>(_churnModelId, input);
                    
                    float oldChurn = behavior.ChurnRisk;
                    behavior.ChurnRisk = MathF.Max(0f, MathF.Min(1f, prediction.ChurnProbability));
                    
                    if (MathF.Abs(behavior.ChurnRisk - oldChurn) > 0.01f)
                    {
                        OnChurnRiskUpdated?.Invoke(entity, behavior.ChurnRisk);
                    }
                    
                    // Warn if high churn risk
                    if (behavior.ChurnRisk >= ChurnWarningThreshold && oldChurn < ChurnWarningThreshold)
                    {
                        OnChurnWarning?.Invoke(entity, behavior.ChurnRisk);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[PlayerAnalytics] Churn prediction failed: {ex.Message}");
                }
            }
        }
    }

    private float[] ExtractSkillFeatures(PlayerBehaviorComponent behavior)
    {
        float completionRate = behavior.CompletedLevels / MathF.Max(1f, behavior.CompletedLevels + behavior.Deaths);
        float deathRate = behavior.Deaths / MathF.Max(1f, behavior.PlayTime / 60f);
        
        return new[]
        {
            completionRate,
            deathRate,
            behavior.AverageInputInterval,
            behavior.Score / MathF.Max(1f, behavior.PlayTime),
            behavior.PlayTime / 3600f // Hours played
        };
    }

    private float[] ExtractChurnFeatures(PlayerBehaviorComponent behavior)
    {
        float sessionLength = behavior.PlayTime / 60f; // Minutes
        float engagement = 1f / MathF.Max(0.1f, behavior.AverageInputInterval);
        float frustration = behavior.Deaths / MathF.Max(1f, behavior.CompletedLevels + 1);
        
        return new[]
        {
            sessionLength,
            engagement,
            frustration,
            behavior.SkillLevel,
            behavior.TimeSinceInput
        };
    }

    /// <summary>
    /// Record a player death for analytics
    /// </summary>
    public void RecordDeath(Entity entity)
    {
        if (World == null || !World.HasComponent<PlayerBehaviorComponent>(entity))
            return;

        ref var behavior = ref World.GetComponent<PlayerBehaviorComponent>(entity);
        behavior.Deaths++;
    }

    /// <summary>
    /// Record level completion for analytics
    /// </summary>
    public void RecordLevelComplete(Entity entity, float score = 0f)
    {
        if (World == null || !World.HasComponent<PlayerBehaviorComponent>(entity))
            return;

        ref var behavior = ref World.GetComponent<PlayerBehaviorComponent>(entity);
        behavior.CompletedLevels++;
        behavior.Score += score;
    }

    /// <summary>
    /// Record player input for engagement tracking
    /// </summary>
    public void RecordInput(Entity entity)
    {
        if (World == null || !World.HasComponent<PlayerBehaviorComponent>(entity))
            return;

        ref var behavior = ref World.GetComponent<PlayerBehaviorComponent>(entity);
        behavior.RecordInput();
    }

    /// <summary>
    /// Get analytics summary for a player
    /// </summary>
    public PlayerAnalyticsSummary? GetSummary(Entity entity)
    {
        if (World == null || !World.HasComponent<PlayerBehaviorComponent>(entity))
            return null;

        ref var behavior = ref World.GetComponent<PlayerBehaviorComponent>(entity);
        return new PlayerAnalyticsSummary
        {
            SessionId = behavior.SessionId,
            PlayTimeMinutes = behavior.PlayTime / 60f,
            Deaths = behavior.Deaths,
            CompletedLevels = behavior.CompletedLevels,
            Score = behavior.Score,
            SkillLevel = behavior.SkillLevel,
            ChurnRisk = behavior.ChurnRisk,
            EngagementScore = 1f / MathF.Max(0.1f, behavior.AverageInputInterval)
        };
    }

    #region ML Types

    public class PlayerInput
    {
        [Microsoft.ML.Data.VectorType]
        public float[] Features { get; set; } = Array.Empty<float>();
    }

    public class SkillOutput
    {
        public float SkillLevel { get; set; }
    }

    public class ChurnOutput
    {
        public float ChurnProbability { get; set; }
    }

    #endregion
}

/// <summary>
/// Summary of player analytics data
/// </summary>
public class PlayerAnalyticsSummary
{
    public string SessionId { get; set; } = "";
    public float PlayTimeMinutes { get; set; }
    public int Deaths { get; set; }
    public int CompletedLevels { get; set; }
    public float Score { get; set; }
    public float SkillLevel { get; set; }
    public float ChurnRisk { get; set; }
    public float EngagementScore { get; set; }
}
