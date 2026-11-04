using System.Numerics;

namespace RedHoleEngine.AI.Director;

/// <summary>
/// The menace curve and the geometry of director hints.
/// Frontstage and near the subject, menace rises (faster if the stalker has real contact).
/// Backstage, menace falls even if the stalker is still close, which is the release valve.
/// Hints are areas and withdraw points, not the subject's exact coordinates.
/// </summary>
public static class MenaceModel
{
    public static float Step(float menace, float deltaTime, in MenaceStimulus stimulus, DirectorConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        if (deltaTime < 0f)
            throw new ArgumentOutOfRangeException(nameof(deltaTime), "Delta time cannot be negative.");
        return Math.Clamp(menace + Rate(stimulus, config) * deltaTime, 0f, 1f);
    }

    public static float Rate(in MenaceStimulus stimulus, DirectorConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        if (stimulus.Backstage)
            return -config.BackstageFallPerSecond;

        if (!stimulus.HasStalker || stimulus.Distance > config.NearRadius)
            return -config.FarFallPerSecond;

        float radius = MathF.Max(config.NearRadius, 0.0001f);
        float proximity = 1f - Math.Clamp(stimulus.Distance / radius, 0f, 1f);
        float rate = config.NearRisePerSecond * MathF.Max(proximity, config.MinNearFactor);
        if (stimulus.VisualContact)
            rate += config.ContactRisePerSecond;
        return rate;
    }

    public static DirectorPhase NextPhase(DirectorPhase current, float menace, DirectorConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        if (config.FrontstageReenterThreshold >= config.BackstageThreshold)
        {
            throw new InvalidOperationException(
                "DirectorConfig.FrontstageReenterThreshold must be lower than BackstageThreshold.");
        }

        if (current == DirectorPhase.Frontstage && menace >= config.BackstageThreshold)
            return DirectorPhase.Backstage;
        if (current == DirectorPhase.Backstage && menace <= config.FrontstageReenterThreshold)
            return DirectorPhase.Frontstage;
        return current;
    }

    /// <summary>
    /// A search point near the subject, pushed off to the side so it is not the subject's position.
    /// </summary>
    public static Vector3 SearchAreaCenter(Vector3 subjectPosition, Vector3 stalkerPosition, float radius)
    {
        return subjectPosition + Lateral(subjectPosition, stalkerPosition) * (radius * 0.25f);
    }

    public static Vector3 WithdrawPoint(Vector3 stalkerPosition, Vector3 subjectPosition, float distance)
    {
        var away = stalkerPosition - subjectPosition;
        if (away.LengthSquared() < 1e-6f)
            away = Vector3.UnitX;
        else
            away = Vector3.Normalize(away);
        return stalkerPosition + away * distance;
    }

    private static Vector3 Lateral(Vector3 subjectPosition, Vector3 stalkerPosition)
    {
        var toStalker = stalkerPosition - subjectPosition;
        if (toStalker.LengthSquared() < 1e-6f)
            return Vector3.UnitX;

        var forward = Vector3.Normalize(toStalker);
        var side = Vector3.Cross(forward, Vector3.UnitY);
        if (side.LengthSquared() < 1e-6f)
            side = Vector3.Cross(forward, Vector3.UnitZ);
        return Vector3.Normalize(side);
    }
}
