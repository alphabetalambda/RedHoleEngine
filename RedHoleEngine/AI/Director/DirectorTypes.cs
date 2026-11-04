using System.Numerics;

namespace RedHoleEngine.AI.Director;

/// <summary>
/// Frontstage: the stalker is allowed to pressure the player.
/// Backstage: the director is spending menace, and the stalker should break off.
/// </summary>
public enum DirectorPhase
{
    Frontstage = 0,
    Backstage = 1
}

/// <summary>
/// A hint is an area to search or a point to withdraw toward.
/// It is never a license to attack: the stalker still has to see the player itself.
/// </summary>
public enum DirectorHintKind
{
    None = 0,
    SearchArea = 1,
    Withdraw = 2
}

/// <summary>
/// Inputs to one step of the menace gauge. Kept as plain data so the curve can be
/// tested without a world.
/// </summary>
public readonly record struct MenaceStimulus(bool HasStalker, float Distance, bool VisualContact, bool Backstage);

/// <summary>
/// Tunables for the menace curve and for how hints are placed.
/// Thresholds must hysteresis: <see cref="FrontstageReenterThreshold"/> is strictly
/// below <see cref="BackstageThreshold"/>.
/// </summary>
public sealed class DirectorConfig
{
    /// <summary>Distance at which the stalker counts as "on stage" and menace can rise.</summary>
    public float NearRadius { get; set; } = 12f;

    /// <summary>Menace per second at point-blank range while frontstage, before contact bonus.</summary>
    public float NearRisePerSecond { get; set; } = 0.20f;

    /// <summary>Extra menace per second while the stalker can actually see the subject.</summary>
    public float ContactRisePerSecond { get; set; } = 0.35f;

    /// <summary>Fraction of <see cref="NearRisePerSecond"/> still applied at the edge of <see cref="NearRadius"/>.</summary>
    public float MinNearFactor { get; set; } = 0.35f;

    /// <summary>Menace lost per second when nobody is near the subject.</summary>
    public float FarFallPerSecond { get; set; } = 0.08f;

    /// <summary>Menace lost per second once the director has sent the stalker backstage.</summary>
    public float BackstageFallPerSecond { get; set; } = 0.25f;

    /// <summary>Entering backstage at or above this menace.</summary>
    public float BackstageThreshold { get; set; } = 0.80f;

    /// <summary>Leaving backstage at or below this menace.</summary>
    public float FrontstageReenterThreshold { get; set; } = 0.35f;

    /// <summary>
    /// Seconds between hint rewrites. Menace itself updates every tick; the position
    /// the stalker is nudged toward does not, so the director cannot puppet it every frame.
    /// Zero refreshes every tick.
    /// </summary>
    public float HintInterval { get; set; } = 3f;

    /// <summary>Radius of the search area issued while frontstage. The center is offset from the subject.</summary>
    public float SearchHintRadius { get; set; } = 8f;

    /// <summary>How far past the stalker the withdraw point is placed, away from the subject.</summary>
    public float WithdrawDistance { get; set; } = 30f;
}
