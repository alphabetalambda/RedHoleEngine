namespace RedHoleEngine.AI;

/// <summary>
/// What the sample stalker is currently trying to do. Movement code (not part of
/// this architecture) reads the intent and drives the body.
/// </summary>
public enum AgentIntent
{
    None = 0,
    Patrol = 1,
    Investigate = 2,
    Search = 3,
    Hunt = 4,
    Ambush = 5,
    Retreat = 6
}

/// <summary>
/// Well-known blackboard keys written by perception, the director, and the sample tree.
/// Game-specific behaviors can add their own keys; nothing here is reserved by the tree core.
/// </summary>
public static class BlackboardKeys
{
    public const string HasVisualContact = "sense.hasVisualContact";
    public const string LastSeenPosition = "sense.lastSeenPosition";
    public const string LastKnownPosition = "sense.lastKnownPosition";
    public const string HeardNoise = "sense.heardNoise";
    public const string NoiseId = "sense.noiseId";
    public const string NoisePosition = "sense.noisePosition";
    public const string NoiseIntensity = "sense.noiseIntensity";
    public const string ConsumedNoiseId = "sense.consumedNoiseId";
    public const string SearchActive = "sense.searchActive";
    public const string SearchTimeRemaining = "sense.searchTimeRemaining";

    public const string Menace = "director.menace";
    public const string DirectorPhase = "director.phase";
    public const string HintKind = "director.hintKind";
    public const string HintPosition = "director.hintPosition";
    public const string HintRadius = "director.hintRadius";

    public const string Intent = "agent.intent";
    public const string IntentPosition = "agent.intentPosition";
    public const string PreferAmbush = "agent.preferAmbush";
    public const string InvestigateElapsed = "stalker.investigateElapsed";
    public const string InvestigateNoiseId = "stalker.investigateNoiseId";
}
