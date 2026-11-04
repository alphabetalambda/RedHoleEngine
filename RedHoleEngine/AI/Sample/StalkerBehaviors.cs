using System.Numerics;
using RedHoleEngine.AI.BehaviorTrees;
using RedHoleEngine.AI.Director;
using RedHoleEngine.AI.Perception;

namespace RedHoleEngine.AI.Sample;

/// <summary>Registry ids for the sample stalker tree. JSON and the fluent builder both use these.</summary>
public static class StalkerIds
{
    public const string ShouldRetreat = "stalker.shouldRetreat";
    public const string ShouldAmbush = "stalker.shouldAmbush";
    public const string HasVisualContact = "stalker.hasVisualContact";
    public const string HasFreshNoise = "stalker.hasFreshNoise";
    public const string HasSearchTarget = "stalker.hasSearchTarget";

    public const string Retreat = "stalker.retreat";
    public const string Ambush = "stalker.ambush";
    public const string Hunt = "stalker.hunt";
    public const string Investigate = "stalker.investigate";
    public const string Search = "stalker.search";
    public const string Patrol = "stalker.patrol";
}

/// <summary>Gate ids defined on a new stalker. Ambush starts locked; the others are available.</summary>
public static class StalkerGates
{
    public const string Retreat = "retreat";
    public const string Ambush = "ambush";
    public const string Hunt = "hunt";
    public const string Investigate = "investigate";
    public const string Search = "search";

    public static BehaviorGateSet CreateDefaultSet()
    {
        var gates = new BehaviorGateSet();
        gates.Define(Retreat, true);
        gates.Define(Ambush, false);
        gates.Define(Hunt, true);
        gates.Define(Investigate, true);
        gates.Define(Search, true);
        return gates;
    }
}

/// <summary>Durations and the menace level at which the sample tree breaks off on its own.</summary>
public static class StalkerTuning
{
    public const float InvestigateSeconds = 0.75f;
    public const float SearchSeconds = SensoryComponent.DefaultSearchMemorySeconds;

    /// <summary>Matches <see cref="DirectorConfig.BackstageThreshold"/> so a scripted menace spike retreats even before the phase flip is observed.</summary>
    public const float RetreatMenace = 0.80f;
}

/// <summary>
/// Stateless conditions and actions for the sample stalker.
/// Progress that has to survive across ticks is stored on the agent blackboard.
/// </summary>
public static class StalkerBehaviors
{
    private static readonly BehaviorRegistry Shared = CreateRegistry();

    public static BehaviorRegistry SharedRegistry => Shared;

    public static BehaviorRegistry CreateRegistry()
    {
        return new BehaviorRegistry()
            .RegisterCondition(StalkerIds.ShouldRetreat, ShouldRetreat)
            .RegisterCondition(StalkerIds.ShouldAmbush, ShouldAmbush)
            .RegisterCondition(StalkerIds.HasVisualContact, HasVisualContact)
            .RegisterCondition(StalkerIds.HasFreshNoise, HasFreshNoise)
            .RegisterCondition(StalkerIds.HasSearchTarget, HasSearchTarget)
            .RegisterAction(StalkerIds.Retreat, Retreat)
            .RegisterAction(StalkerIds.Ambush, Ambush)
            .RegisterAction(StalkerIds.Hunt, Hunt)
            .RegisterAction(StalkerIds.Investigate, Investigate)
            .RegisterAction(StalkerIds.Search, Search)
            .RegisterAction(StalkerIds.Patrol, Patrol);
    }

    public static bool ShouldRetreat(BehaviorContext context)
    {
        if (context.Blackboard.TryGet(BlackboardKeys.DirectorPhase, out DirectorPhase phase)
            && phase == DirectorPhase.Backstage)
            return true;

        return context.Blackboard.TryGet(BlackboardKeys.Menace, out float menace)
            && menace >= StalkerTuning.RetreatMenace;
    }

    public static bool ShouldAmbush(BehaviorContext context)
    {
        return context.Blackboard.TryGet(BlackboardKeys.PreferAmbush, out bool prefer) && prefer
            && context.Blackboard.TryGet(BlackboardKeys.HasVisualContact, out bool seen) && seen;
    }

    public static bool HasVisualContact(BehaviorContext context)
    {
        return context.Blackboard.TryGet(BlackboardKeys.HasVisualContact, out bool seen) && seen;
    }

    public static bool HasFreshNoise(BehaviorContext context)
    {
        return context.Blackboard.TryGet(BlackboardKeys.HeardNoise, out bool heard) && heard;
    }

    public static bool HasSearchTarget(BehaviorContext context)
    {
        var board = context.Blackboard;
        if (board.TryGet(BlackboardKeys.SearchActive, out bool active) && active
            && board.Has(BlackboardKeys.LastKnownPosition))
            return true;

        return board.TryGet(BlackboardKeys.HintKind, out DirectorHintKind kind)
            && kind == DirectorHintKind.SearchArea
            && board.Has(BlackboardKeys.HintPosition);
    }

    public static NodeStatus Patrol(BehaviorContext context)
    {
        context.Blackboard.Set(BlackboardKeys.Intent, AgentIntent.Patrol);
        context.Blackboard.Remove(BlackboardKeys.IntentPosition);
        return NodeStatus.Success;
    }

    public static NodeStatus Investigate(BehaviorContext context)
    {
        var board = context.Blackboard;
        if (!board.TryGet(BlackboardKeys.NoiseId, out int noiseId))
            return NodeStatus.Failure;

        if (!board.TryGet(BlackboardKeys.InvestigateNoiseId, out int active) || active != noiseId)
        {
            board.Set(BlackboardKeys.InvestigateNoiseId, noiseId);
            board.Set(BlackboardKeys.InvestigateElapsed, 0f);
        }

        float elapsed = board.GetOr(BlackboardKeys.InvestigateElapsed, 0f) + context.DeltaTime;
        board.Set(BlackboardKeys.Intent, AgentIntent.Investigate);
        bool hasPosition = board.TryGet(BlackboardKeys.NoisePosition, out Vector3 position);
        if (hasPosition)
            board.Set(BlackboardKeys.IntentPosition, position);

        if (elapsed < StalkerTuning.InvestigateSeconds)
        {
            board.Set(BlackboardKeys.InvestigateElapsed, elapsed);
            return NodeStatus.Running;
        }

        board.Set(BlackboardKeys.HeardNoise, false);
        board.Set(BlackboardKeys.ConsumedNoiseId, noiseId);
        if (hasPosition)
            board.Set(BlackboardKeys.LastKnownPosition, position);
        board.Set(BlackboardKeys.SearchActive, true);
        board.Set(BlackboardKeys.SearchTimeRemaining, StalkerTuning.SearchSeconds);
        board.Remove(BlackboardKeys.InvestigateElapsed);
        return NodeStatus.Success;
    }

    public static NodeStatus Search(BehaviorContext context)
    {
        var board = context.Blackboard;
        if (board.TryGet(BlackboardKeys.SearchActive, out bool active) && active
            && board.TryGet(BlackboardKeys.LastKnownPosition, out Vector3 known))
        {
            float remaining = board.GetOr(BlackboardKeys.SearchTimeRemaining, StalkerTuning.SearchSeconds) - context.DeltaTime;
            board.Set(BlackboardKeys.Intent, AgentIntent.Search);
            board.Set(BlackboardKeys.IntentPosition, known);
            if (remaining <= 0f)
            {
                board.Set(BlackboardKeys.SearchActive, false);
                board.Remove(BlackboardKeys.SearchTimeRemaining);
                return NodeStatus.Success;
            }

            board.Set(BlackboardKeys.SearchTimeRemaining, remaining);
            return NodeStatus.Running;
        }

        if (board.TryGet(BlackboardKeys.HintKind, out DirectorHintKind kind)
            && kind == DirectorHintKind.SearchArea
            && board.TryGet(BlackboardKeys.HintPosition, out Vector3 hint))
        {
            board.Set(BlackboardKeys.Intent, AgentIntent.Search);
            board.Set(BlackboardKeys.IntentPosition, hint);
            return NodeStatus.Running;
        }

        return NodeStatus.Failure;
    }

    public static NodeStatus Hunt(BehaviorContext context)
    {
        context.Blackboard.Set(BlackboardKeys.Intent, AgentIntent.Hunt);
        if (context.Blackboard.TryGet(BlackboardKeys.LastSeenPosition, out Vector3 seen))
            context.Blackboard.Set(BlackboardKeys.IntentPosition, seen);
        return NodeStatus.Running;
    }

    public static NodeStatus Ambush(BehaviorContext context)
    {
        context.Blackboard.Set(BlackboardKeys.Intent, AgentIntent.Ambush);
        if (context.Blackboard.TryGet(BlackboardKeys.LastSeenPosition, out Vector3 seen))
            context.Blackboard.Set(BlackboardKeys.IntentPosition, seen);
        return NodeStatus.Running;
    }

    public static NodeStatus Retreat(BehaviorContext context)
    {
        var board = context.Blackboard;
        board.Set(BlackboardKeys.Intent, AgentIntent.Retreat);
        if (board.TryGet(BlackboardKeys.HintKind, out DirectorHintKind kind)
            && kind == DirectorHintKind.Withdraw
            && board.TryGet(BlackboardKeys.HintPosition, out Vector3 hint))
        {
            board.Set(BlackboardKeys.IntentPosition, hint);
        }

        return NodeStatus.Running;
    }
}
