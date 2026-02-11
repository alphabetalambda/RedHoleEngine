using System.Numerics;
using RedHoleEngine.Components;
using RedHoleEngine.Core.ECS;
using RedHoleEngine.ML.Components;
using RedHoleEngine.ML.Services;

namespace RedHoleEngine.ML.Analytics;

/// <summary>
/// System that detects anomalous behavior using ML.NET anomaly detection.
/// Useful for cheat detection, bot detection, or unusual pattern identification.
/// </summary>
public sealed class AnomalyDetectionSystem : GameSystem
{
    private MLService? _mlService;
    
    /// <summary>
    /// Event fired when an anomaly is detected
    /// </summary>
    public event Action<Entity, float, string>? OnAnomalyDetected; // entity, score, reason
    
    /// <summary>
    /// Event fired when an entity is flagged for repeated anomalies
    /// </summary>
    public event Action<Entity, int>? OnEntityFlagged; // entity, flagCount
    
    /// <summary>
    /// Custom feature extractor for anomaly detection
    /// </summary>
    public Func<Entity, float[]>? FeatureExtractor { get; set; }

    /// <summary>
    /// Set the ML service for anomaly detection
    /// </summary>
    public void SetMLService(MLService service)
    {
        _mlService = service;
    }

    public override void Update(float deltaTime)
    {
        if (World == null || _mlService == null)
            return;

        foreach (var entity in World.Query<AnomalyMonitorComponent, TransformComponent>())
        {
            ref var monitor = ref World.GetComponent<AnomalyMonitorComponent>(entity);
            
            monitor.TimeSinceCheck += deltaTime;
            
            if (monitor.TimeSinceCheck >= monitor.CheckInterval)
            {
                monitor.TimeSinceCheck = 0f;
                CheckForAnomaly(entity, ref monitor);
            }
        }
    }

    private void CheckForAnomaly(Entity entity, ref AnomalyMonitorComponent monitor)
    {
        if (!_mlService!.HasModel(monitor.ModelId))
            return;

        try
        {
            var features = FeatureExtractor?.Invoke(entity) ?? ExtractDefaultFeatures(entity);
            var input = new AnomalyInput { Features = features };
            var prediction = _mlService.Predict<AnomalyInput, AnomalyOutput>(monitor.ModelId, input);
            
            monitor.AnomalyScore = prediction.Score;
            bool wasFlag = monitor.IsFlagged;
            monitor.IsFlagged = prediction.PredictedLabel || prediction.Score > monitor.Threshold;
            
            if (monitor.IsFlagged)
            {
                if (!wasFlag)
                {
                    monitor.FlagCount++;
                    OnEntityFlagged?.Invoke(entity, monitor.FlagCount);
                }
                
                string reason = DetermineAnomalyReason(features, prediction.Score);
                OnAnomalyDetected?.Invoke(entity, prediction.Score, reason);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[AnomalyDetection] Check failed for entity {entity.Id}: {ex.Message}");
        }
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
                var vel = rb.Body.LinearVelocity;
                features.Add(vel.Length()); // Speed
                features.Add(rb.Body.AngularVelocity.Length()); // Angular speed
            }
        }

        if (World.HasComponent<PlayerBehaviorComponent>(entity))
        {
            ref var behavior = ref World.GetComponent<PlayerBehaviorComponent>(entity);
            features.Add(behavior.AverageInputInterval);
            features.Add(behavior.Score / MathF.Max(1f, behavior.PlayTime));
            features.Add(behavior.Deaths / MathF.Max(1f, behavior.PlayTime / 60f));
        }

        return features.ToArray();
    }

    private string DetermineAnomalyReason(float[] features, float score)
    {
        // Simple heuristic-based reason determination
        // In practice, you'd want more sophisticated analysis
        if (features.Length >= 3)
        {
            float speed = features.Length > 3 ? features[3] : 0;
            if (speed > 100f) return "Unusually high speed";
        }
        
        if (score > 0.9f) return "Highly anomalous pattern";
        if (score > 0.7f) return "Suspicious activity pattern";
        return "Unusual behavior detected";
    }

    /// <summary>
    /// Reset the flag count for an entity
    /// </summary>
    public void ResetFlags(Entity entity)
    {
        if (World == null || !World.HasComponent<AnomalyMonitorComponent>(entity))
            return;

        ref var monitor = ref World.GetComponent<AnomalyMonitorComponent>(entity);
        monitor.FlagCount = 0;
        monitor.IsFlagged = false;
    }

    /// <summary>
    /// Get all currently flagged entities
    /// </summary>
    public IEnumerable<(Entity Entity, int FlagCount, float Score)> GetFlaggedEntities()
    {
        if (World == null)
            yield break;

        foreach (var entity in World.Query<AnomalyMonitorComponent>())
        {
            ref var monitor = ref World.GetComponent<AnomalyMonitorComponent>(entity);
            if (monitor.IsFlagged)
            {
                yield return (entity, monitor.FlagCount, monitor.AnomalyScore);
            }
        }
    }

    #region ML Types

    public class AnomalyInput
    {
        [Microsoft.ML.Data.VectorType]
        public float[] Features { get; set; } = Array.Empty<float>();
    }

    public class AnomalyOutput
    {
        public bool PredictedLabel { get; set; }
        public float Score { get; set; }
    }

    #endregion
}
