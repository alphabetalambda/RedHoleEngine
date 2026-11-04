using System.Numerics;
using RedHoleEngine.Core.ECS;

namespace RedHoleEngine.AI.Director;

/// <summary>
/// The actor the director is pacing the encounter around. The director is allowed
/// to know this entity's position. Stalkers are not, except through their own senses
/// and the hints written into their blackboard.
/// </summary>
public struct DirectorSubjectComponent : IComponent
{
}

/// <summary>
/// An agent the director may steer with menace and hints.
/// Hint fields remember the last order so it stays put between refreshes.
/// </summary>
public struct DirectorStalkerComponent : IComponent
{
    public bool HasHint;
    public DirectorHintKind HintKind;
    public Vector3 HintPosition;
    public float HintRadius;
}
