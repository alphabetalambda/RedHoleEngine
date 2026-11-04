using System.Numerics;

namespace RedHoleEngine.AI.Perception;

/// <summary>
/// A one-shot sound dropped into the world. <see cref="Intensity"/> is the hearing radius
/// at a sensitivity of 1. Agents remember the id they have already investigated so a
/// sound that is still inside its lifetime is not re-triggered.
/// </summary>
public readonly record struct NoiseEvent(int Id, Vector3 Position, float Intensity, float Time, string? Tag);
