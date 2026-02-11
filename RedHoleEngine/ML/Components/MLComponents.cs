using RedHoleEngine.Core.ECS;

namespace RedHoleEngine.ML.Components;

/// <summary>
/// Component that marks an entity as having ML-based decision making.
/// The MLAgentSystem will process entities with this component.
/// </summary>
public struct MLAgentComponent : IComponent
{
    /// <summary>
    /// Unique identifier for the trained model to use
    /// </summary>
    public string ModelId;
    
    /// <summary>
    /// Type of agent behavior
    /// </summary>
    public MLAgentType AgentType;
    
    /// <summary>
    /// How often to make predictions (in seconds). 0 = every frame.
    /// </summary>
    public float DecisionInterval;
    
    /// <summary>
    /// Time since last decision
    /// </summary>
    public float TimeSinceDecision;
    
    /// <summary>
    /// Last predicted action index (for discrete actions)
    /// </summary>
    public int LastAction;
    
    /// <summary>
    /// Confidence of the last prediction (0-1)
    /// </summary>
    public float LastConfidence;
    
    /// <summary>
    /// Whether the agent is currently active
    /// </summary>
    public bool IsActive;

    public static MLAgentComponent Create(string modelId, MLAgentType agentType = MLAgentType.Classifier, float decisionInterval = 0.1f)
    {
        return new MLAgentComponent
        {
            ModelId = modelId,
            AgentType = agentType,
            DecisionInterval = decisionInterval,
            TimeSinceDecision = 0f,
            LastAction = -1,
            LastConfidence = 0f,
            IsActive = true
        };
    }
}

/// <summary>
/// Component for entities that contribute to player behavior analytics
/// </summary>
public struct PlayerBehaviorComponent : IComponent
{
    /// <summary>
    /// Session ID for this player
    /// </summary>
    public string SessionId;
    
    /// <summary>
    /// Total play time in seconds
    /// </summary>
    public float PlayTime;
    
    /// <summary>
    /// Number of deaths/failures
    /// </summary>
    public int Deaths;
    
    /// <summary>
    /// Number of levels/areas completed
    /// </summary>
    public int CompletedLevels;
    
    /// <summary>
    /// Current score/points
    /// </summary>
    public float Score;
    
    /// <summary>
    /// Average time between inputs (engagement metric)
    /// </summary>
    public float AverageInputInterval;
    
    /// <summary>
    /// Accumulated input count for averaging
    /// </summary>
    public int InputCount;
    
    /// <summary>
    /// Time since last input
    /// </summary>
    public float TimeSinceInput;
    
    /// <summary>
    /// Predicted player skill level (0-1, updated by analytics)
    /// </summary>
    public float SkillLevel;
    
    /// <summary>
    /// Predicted churn risk (0-1, updated by analytics)
    /// </summary>
    public float ChurnRisk;

    public static PlayerBehaviorComponent Create(string? sessionId = null)
    {
        return new PlayerBehaviorComponent
        {
            SessionId = sessionId ?? Guid.NewGuid().ToString("N"),
            PlayTime = 0f,
            Deaths = 0,
            CompletedLevels = 0,
            Score = 0f,
            AverageInputInterval = 0f,
            InputCount = 0,
            TimeSinceInput = 0f,
            SkillLevel = 0.5f,
            ChurnRisk = 0f
        };
    }
    
    /// <summary>
    /// Record a player input for engagement tracking
    /// </summary>
    public void RecordInput()
    {
        if (InputCount > 0)
        {
            AverageInputInterval = (AverageInputInterval * InputCount + TimeSinceInput) / (InputCount + 1);
        }
        InputCount++;
        TimeSinceInput = 0f;
    }
}

/// <summary>
/// Component for dynamic difficulty adjustment
/// </summary>
public struct DifficultyComponent : IComponent
{
    /// <summary>
    /// Current difficulty multiplier (1.0 = normal)
    /// </summary>
    public float DifficultyMultiplier;
    
    /// <summary>
    /// Target difficulty based on ML predictions
    /// </summary>
    public float TargetDifficulty;
    
    /// <summary>
    /// How fast difficulty adjusts to target
    /// </summary>
    public float AdjustmentSpeed;
    
    /// <summary>
    /// Minimum allowed difficulty
    /// </summary>
    public float MinDifficulty;
    
    /// <summary>
    /// Maximum allowed difficulty
    /// </summary>
    public float MaxDifficulty;
    
    /// <summary>
    /// Whether automatic adjustment is enabled
    /// </summary>
    public bool AutoAdjust;

    public static DifficultyComponent Create(float initialDifficulty = 1.0f, float min = 0.5f, float max = 2.0f)
    {
        return new DifficultyComponent
        {
            DifficultyMultiplier = initialDifficulty,
            TargetDifficulty = initialDifficulty,
            AdjustmentSpeed = 0.1f,
            MinDifficulty = min,
            MaxDifficulty = max,
            AutoAdjust = true
        };
    }
}

/// <summary>
/// Component for anomaly detection on entity behavior
/// </summary>
public struct AnomalyMonitorComponent : IComponent
{
    /// <summary>
    /// Model ID for anomaly detection
    /// </summary>
    public string ModelId;
    
    /// <summary>
    /// Current anomaly score (higher = more anomalous)
    /// </summary>
    public float AnomalyScore;
    
    /// <summary>
    /// Threshold above which to flag as anomaly
    /// </summary>
    public float Threshold;
    
    /// <summary>
    /// Whether currently flagged as anomalous
    /// </summary>
    public bool IsFlagged;
    
    /// <summary>
    /// Number of times flagged
    /// </summary>
    public int FlagCount;
    
    /// <summary>
    /// How often to check for anomalies (seconds)
    /// </summary>
    public float CheckInterval;
    
    /// <summary>
    /// Time since last check
    /// </summary>
    public float TimeSinceCheck;

    public static AnomalyMonitorComponent Create(string modelId, float threshold = 0.5f, float checkInterval = 1.0f)
    {
        return new AnomalyMonitorComponent
        {
            ModelId = modelId,
            AnomalyScore = 0f,
            Threshold = threshold,
            IsFlagged = false,
            FlagCount = 0,
            CheckInterval = checkInterval,
            TimeSinceCheck = 0f
        };
    }
}

#region Enums

public enum MLAgentType
{
    /// <summary>
    /// Agent uses a classifier to pick discrete actions
    /// </summary>
    Classifier,
    
    /// <summary>
    /// Agent uses regression to output continuous values
    /// </summary>
    Regressor,
    
    /// <summary>
    /// Agent uses clustering to identify state groups
    /// </summary>
    Clusterer
}

#endregion
