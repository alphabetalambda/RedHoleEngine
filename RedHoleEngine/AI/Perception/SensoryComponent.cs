using System.Numerics;
using RedHoleEngine.Core.ECS;

namespace RedHoleEngine.AI.Perception;

/// <summary>
/// An agent's eyes and ears, plus the last thing they sensed.
/// Position comes from <see cref="RedHoleEngine.Components.TransformComponent"/> when one is present;
/// otherwise <see cref="Position"/> is used so tests do not need a transform.
/// A non-zero <see cref="Facing"/> overrides the transform's forward.
/// </summary>
public struct SensoryComponent : IComponent
{
    public const float DefaultViewDistance = 20f;
    public const float DefaultViewAngleDegrees = 70f;
    public const float DefaultHearingSensitivity = 1f;
    public const float DefaultSearchMemorySeconds = 2.5f;

    public float ViewDistance;
    public float ViewAngleDegrees;
    public float HearingSensitivity;
    public float EyeHeight;
    public float SearchMemorySeconds;

    /// <summary>World position used when the entity has no transform.</summary>
    public Vector3 Position;

    /// <summary>Look direction. Leave at zero to use the transform forward, or -Z if there is no transform.</summary>
    public Vector3 Facing;

    /// <summary>
    /// Entity this sensor is trying to see. <see cref="Entity.Null"/> means
    /// "the first director subject in the world".
    /// </summary>
    public Entity StimulusSource;

    public bool HasVisualContact;
    public Vector3 LastSeenPosition;
    public float TimeSinceSeen;

    public bool HeardNoise;
    public int HeardNoiseId;
    public Vector3 LastHeardPosition;
    public float LastHeardIntensity;
    public float TimeSinceHeard;
    public int ConsumedNoiseId;

    public static SensoryComponent Create(
        float viewDistance = DefaultViewDistance,
        float viewAngleDegrees = DefaultViewAngleDegrees,
        float hearingSensitivity = DefaultHearingSensitivity)
    {
        return new SensoryComponent
        {
            ViewDistance = viewDistance,
            ViewAngleDegrees = viewAngleDegrees,
            HearingSensitivity = hearingSensitivity,
            EyeHeight = 0f,
            SearchMemorySeconds = DefaultSearchMemorySeconds,
            Position = Vector3.Zero,
            Facing = Vector3.Zero,
            StimulusSource = Entity.Null,
            HasVisualContact = false,
            LastSeenPosition = Vector3.Zero,
            TimeSinceSeen = 0f,
            HeardNoise = false,
            HeardNoiseId = 0,
            LastHeardPosition = Vector3.Zero,
            LastHeardIntensity = 0f,
            TimeSinceHeard = 0f,
            ConsumedNoiseId = 0
        };
    }
}
