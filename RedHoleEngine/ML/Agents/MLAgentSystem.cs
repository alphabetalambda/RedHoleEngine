using System.Numerics;
using RedHoleEngine.Components;
using RedHoleEngine.Core.ECS;
using RedHoleEngine.ML.Components;
using RedHoleEngine.ML.Services;

namespace RedHoleEngine.ML.Agents;

/// <summary>
/// System that processes ML agents, running predictions to determine NPC behavior.
/// Requires an MLService to be set before use.
/// </summary>
public sealed class MLAgentSystem : GameSystem
{
    private MLService? _mlService;
    
    /// <summary>
    /// Event fired when an agent makes a decision
    /// </summary>
    public event Action<Entity, int, float>? OnAgentDecision;
    
    /// <summary>
    /// Callback to gather input features for an agent
    /// </summary>
    public Func<Entity, float[]>? FeatureExtractor { get; set; }

    /// <summary>
    /// Set the ML service to use for predictions
    /// </summary>
    public void SetMLService(MLService service)
    {
        _mlService = service;
    }

    public override void Update(float deltaTime)
    {
        if (World == null || _mlService == null)
            return;

        foreach (var entity in World.Query<MLAgentComponent, TransformComponent>())
        {
            ref var agent = ref World.GetComponent<MLAgentComponent>(entity);
            
            if (!agent.IsActive)
                continue;
                
            agent.TimeSinceDecision += deltaTime;
            
            if (agent.TimeSinceDecision >= agent.DecisionInterval)
            {
                agent.TimeSinceDecision = 0f;
                ProcessAgent(entity, ref agent);
            }
        }
    }

    private void ProcessAgent(Entity entity, ref MLAgentComponent agent)
    {
        if (!_mlService!.HasModel(agent.ModelId))
        {
            Console.WriteLine($"[MLAgentSystem] Model '{agent.ModelId}' not found for entity {entity.Id}");
            return;
        }

        // Get input features
        var features = FeatureExtractor?.Invoke(entity);
        if (features == null || features.Length == 0)
        {
            // Try to extract default features from transform
            features = ExtractDefaultFeatures(entity);
        }

        try
        {
            switch (agent.AgentType)
            {
                case MLAgentType.Classifier:
                    ProcessClassifierAgent(entity, ref agent, features);
                    break;
                case MLAgentType.Regressor:
                    ProcessRegressorAgent(entity, ref agent, features);
                    break;
                case MLAgentType.Clusterer:
                    ProcessClustererAgent(entity, ref agent, features);
                    break;
            }
            
            OnAgentDecision?.Invoke(entity, agent.LastAction, agent.LastConfidence);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[MLAgentSystem] Prediction failed for entity {entity.Id}: {ex.Message}");
        }
    }

    private void ProcessClassifierAgent(Entity entity, ref MLAgentComponent agent, float[] features)
    {
        var input = new AgentInput { Features = features };
        var prediction = _mlService!.Predict<AgentInput, ClassifierOutput>(agent.ModelId, input);
        
        agent.LastAction = (int)prediction.PredictedLabel;
        agent.LastConfidence = prediction.Score?.Max() ?? 0f;
    }

    private void ProcessRegressorAgent(Entity entity, ref MLAgentComponent agent, float[] features)
    {
        var input = new AgentInput { Features = features };
        var prediction = _mlService!.Predict<AgentInput, RegressorOutput>(agent.ModelId, input);
        
        // For regressors, we store the predicted value as the action
        agent.LastAction = (int)Math.Round(prediction.Score);
        agent.LastConfidence = 1.0f; // Regressors don't have confidence scores
    }

    private void ProcessClustererAgent(Entity entity, ref MLAgentComponent agent, float[] features)
    {
        var input = new AgentInput { Features = features };
        var prediction = _mlService!.Predict<AgentInput, ClustererOutput>(agent.ModelId, input);
        
        agent.LastAction = (int)prediction.PredictedClusterId;
        agent.LastConfidence = 1.0f - (prediction.Distances?.Min() ?? 0f); // Inverse distance as confidence
    }

    private float[] ExtractDefaultFeatures(Entity entity)
    {
        var features = new List<float>();
        
        if (World!.HasComponent<TransformComponent>(entity))
        {
            ref var transform = ref World.GetComponent<TransformComponent>(entity);
            features.Add(transform.Position.X);
            features.Add(transform.Position.Y);
            features.Add(transform.Position.Z);
        }
        
        if (World.HasComponent<RigidBodyComponent>(entity))
        {
            ref var rb = ref World.GetComponent<RigidBodyComponent>(entity);
            if (rb.Body != null)
            {
                features.Add(rb.Body.LinearVelocity.X);
                features.Add(rb.Body.LinearVelocity.Y);
                features.Add(rb.Body.LinearVelocity.Z);
            }
        }

        return features.ToArray();
    }

    #region ML Input/Output Types

    /// <summary>
    /// Input type for ML predictions
    /// </summary>
    public class AgentInput
    {
        [Microsoft.ML.Data.VectorType]
        public float[] Features { get; set; } = Array.Empty<float>();
    }

    /// <summary>
    /// Output for binary/multiclass classification
    /// </summary>
    public class ClassifierOutput
    {
        public uint PredictedLabel { get; set; }
        public float[]? Score { get; set; }
    }

    /// <summary>
    /// Output for regression
    /// </summary>
    public class RegressorOutput
    {
        public float Score { get; set; }
    }

    /// <summary>
    /// Output for clustering
    /// </summary>
    public class ClustererOutput
    {
        public uint PredictedClusterId { get; set; }
        public float[]? Distances { get; set; }
    }

    #endregion
}
