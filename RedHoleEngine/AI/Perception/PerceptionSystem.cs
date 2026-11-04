using System.Numerics;
using RedHoleEngine.AI.BehaviorTrees;
using RedHoleEngine.AI.Director;
using RedHoleEngine.Components;
using RedHoleEngine.Core.ECS;

namespace RedHoleEngine.AI.Perception;

/// <summary>
/// Updates <see cref="SensoryComponent"/> from the view cone and the noise board,
/// then copies the result onto the agent's <see cref="Blackboard"/> when it has one.
/// Runs before the director and the behavior tree.
/// </summary>
public class PerceptionSystem : GameSystem
{
    private readonly List<NoiseEvent> _noises = new();
    private int _nextNoiseId = 1;

    public override int Priority => 100;

    /// <summary>Seconds a noise stays on the board. Agents can still investigate it after it expires if they already heard it.</summary>
    public float NoiseLifetime { get; set; } = 1.5f;

    /// <summary>Seconds accumulated by this system. Noise timestamps are in this clock.</summary>
    public float Time { get; private set; }

    /// <summary>Optional occlusion. Null means the view cone is sufficient.</summary>
    public IVisionQuery? Vision { get; set; }

    public IReadOnlyList<NoiseEvent> ActiveNoises => _noises;

    public NoiseEvent EmitNoise(Vector3 position, float intensity, string? tag = null)
    {
        var noise = new NoiseEvent(_nextNoiseId++, position, intensity, Time, tag);
        _noises.Add(noise);
        return noise;
    }

    public void ClearNoises() => _noises.Clear();

    public override void Update(float deltaTime)
    {
        Time += deltaTime;
        PruneNoises();

        foreach (var entity in World.Query<SensoryComponent>())
        {
            ref var sensor = ref World.GetComponent<SensoryComponent>(entity);
            Vector3 origin = ResolveOrigin(entity, sensor);
            Vector3 forward = ResolveForward(entity, sensor);
            sensor.Position = origin;

            bool wasVisible = sensor.HasVisualContact;
            if (TryResolveTarget(entity, sensor, out var targetPosition))
            {
                var eye = origin + new Vector3(0f, sensor.EyeHeight, 0f);
                bool inCone = SensoryMath.InViewCone(eye, forward, targetPosition, sensor.ViewDistance, sensor.ViewAngleDegrees);
                bool blocked = inCone && Vision != null && Vision.IsOccluded(eye, targetPosition);
                if (inCone && !blocked)
                {
                    sensor.HasVisualContact = true;
                    sensor.LastSeenPosition = targetPosition;
                    sensor.TimeSinceSeen = 0f;
                }
                else
                {
                    sensor.HasVisualContact = false;
                    sensor.TimeSinceSeen += deltaTime;
                }
            }
            else
            {
                sensor.HasVisualContact = false;
                sensor.TimeSinceSeen = wasVisible ? deltaTime : sensor.TimeSinceSeen + deltaTime;
            }

            Blackboard? board = null;
            int consumed = sensor.ConsumedNoiseId;
            if (World.HasComponent<BehaviorAgentComponent>(entity))
            {
                ref var agent = ref World.GetComponent<BehaviorAgentComponent>(entity);
                var agentBoard = agent.Blackboard ?? new Blackboard();
                agent.Blackboard = agentBoard;
                board = agentBoard;
                if (agentBoard.TryGet(BlackboardKeys.ConsumedNoiseId, out int fromBoard))
                    consumed = Math.Max(consumed, fromBoard);
            }

            sensor.ConsumedNoiseId = consumed;
            if (TrySelectNoise(origin, sensor.HearingSensitivity, consumed, out var heard))
            {
                sensor.HeardNoise = true;
                sensor.HeardNoiseId = heard.Id;
                sensor.LastHeardPosition = heard.Position;
                sensor.LastHeardIntensity = heard.Intensity;
                sensor.TimeSinceHeard = 0f;
            }
            else
            {
                sensor.HeardNoise = false;
                sensor.TimeSinceHeard += deltaTime;
            }

            if (board != null)
                WriteBlackboard(board, sensor);
        }
    }

    private bool TrySelectNoise(Vector3 origin, float hearingSensitivity, int consumedId, out NoiseEvent heard)
    {
        int bestIndex = -1;
        float bestScore = float.NegativeInfinity;
        for (int i = 0; i < _noises.Count; i++)
        {
            var noise = _noises[i];
            if (noise.Id <= consumedId)
                continue;
            if (!SensoryMath.CanHear(origin, noise.Position, noise.Intensity, hearingSensitivity))
                continue;

            float score = noise.Intensity - Vector3.Distance(origin, noise.Position) * 0.001f;
            if (score > bestScore)
            {
                bestScore = score;
                bestIndex = i;
            }
        }

        if (bestIndex < 0)
        {
            heard = default;
            return false;
        }

        heard = _noises[bestIndex];
        return true;
    }

    private void WriteBlackboard(Blackboard board, in SensoryComponent sensor)
    {
        board.Set(BlackboardKeys.HasVisualContact, sensor.HasVisualContact);

        float memory = sensor.SearchMemorySeconds > 0f
            ? sensor.SearchMemorySeconds
            : SensoryComponent.DefaultSearchMemorySeconds;

        if (sensor.HasVisualContact)
        {
            board.Set(BlackboardKeys.LastSeenPosition, sensor.LastSeenPosition);
            board.Set(BlackboardKeys.LastKnownPosition, sensor.LastSeenPosition);
            board.Set(BlackboardKeys.SearchActive, true);
            board.Set(BlackboardKeys.SearchTimeRemaining, memory);
        }
        else if (sensor.HeardNoise)
        {
            // A sound is enough to search from, including when the investigate tactic is still locked.
            board.Set(BlackboardKeys.LastKnownPosition, sensor.LastHeardPosition);
            board.Set(BlackboardKeys.SearchActive, true);
            board.Set(BlackboardKeys.SearchTimeRemaining, memory);
        }

        if (sensor.HeardNoise)
        {
            board.Set(BlackboardKeys.HeardNoise, true);
            board.Set(BlackboardKeys.NoiseId, sensor.HeardNoiseId);
            board.Set(BlackboardKeys.NoisePosition, sensor.LastHeardPosition);
            board.Set(BlackboardKeys.NoiseIntensity, sensor.LastHeardIntensity);
        }
    }

    private bool TryResolveTarget(Entity sensorEntity, in SensoryComponent sensor, out Vector3 targetPosition)
    {
        if (!sensor.StimulusSource.IsNull && World.IsAlive(sensor.StimulusSource))
            return TryGetPosition(sensor.StimulusSource, out targetPosition);

        foreach (var entity in World.Query<DirectorSubjectComponent>())
        {
            if (entity == sensorEntity)
                continue;
            if (TryGetPosition(entity, out targetPosition))
                return true;
        }

        targetPosition = default;
        return false;
    }

    private Vector3 ResolveOrigin(Entity entity, in SensoryComponent sensor)
    {
        if (World.TryGetComponent<TransformComponent>(entity, out var transform))
            return transform.Position;
        return sensor.Position;
    }

    private Vector3 ResolveForward(Entity entity, in SensoryComponent sensor)
    {
        if (sensor.Facing.LengthSquared() > 1e-6f)
            return Vector3.Normalize(sensor.Facing);
        if (World.TryGetComponent<TransformComponent>(entity, out var transform))
            return transform.Forward;
        return -Vector3.UnitZ;
    }

    private bool TryGetPosition(Entity entity, out Vector3 position)
    {
        if (World.TryGetComponent<TransformComponent>(entity, out var transform))
        {
            position = transform.Position;
            return true;
        }

        if (World.TryGetComponent<SensoryComponent>(entity, out var sensor))
        {
            position = sensor.Position;
            return true;
        }

        position = default;
        return false;
    }

    private void PruneNoises()
    {
        for (int i = _noises.Count - 1; i >= 0; i--)
        {
            if (Time - _noises[i].Time > NoiseLifetime)
                _noises.RemoveAt(i);
        }
    }
}
