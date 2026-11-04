using RedHoleEngine.AI.BehaviorTrees;

namespace RedHoleEngine.AI.Sample;

/// <summary>
/// Sample stalker tree, in priority order:
/// retreat when the director spends menace, ambush once that tactic is unlocked,
/// hunt only on the agent's own visual contact, investigate a fresh noise,
/// search the last known position or the director's search area, otherwise patrol.
/// </summary>
/// <remarks>
/// The director hint never satisfies the hunt guard. Hunting requires
/// <see cref="BlackboardKeys.HasVisualContact"/> from perception.
/// </remarks>
public static class StalkerTree
{
    public const string Name = "stalker";

    public static BehaviorTree Create()
    {
        var registry = StalkerBehaviors.SharedRegistry;
        return new BehaviorTreeBuilder(Name, registry)
            .Selector("root")
                .Sequence("retreat").Gate(StalkerGates.Retreat)
                    .Guard("should-retreat", StalkerIds.ShouldRetreat)
                        .Action("retreat", StalkerIds.Retreat)
                    .End()
                .End()
                .Sequence("ambush").Gate(StalkerGates.Ambush)
                    .Guard("should-ambush", StalkerIds.ShouldAmbush)
                        .Action("ambush", StalkerIds.Ambush)
                    .End()
                .End()
                .Sequence("hunt").Gate(StalkerGates.Hunt)
                    .Guard("has-visual", StalkerIds.HasVisualContact)
                        .Action("hunt", StalkerIds.Hunt)
                    .End()
                .End()
                .Sequence("investigate").Gate(StalkerGates.Investigate)
                    .Guard("heard-noise", StalkerIds.HasFreshNoise)
                        .Action("investigate", StalkerIds.Investigate)
                    .End()
                .End()
                .Sequence("search").Gate(StalkerGates.Search)
                    .Guard("has-search-target", StalkerIds.HasSearchTarget)
                        .Action("search", StalkerIds.Search)
                    .End()
                .End()
                .Action("patrol", StalkerIds.Patrol)
            .End()
            .Build();
    }

    public static BehaviorAgentComponent CreateAgent(bool recordTrace = false)
    {
        return BehaviorAgentComponent.Create(Create(), StalkerGates.CreateDefaultSet(), recordTrace);
    }

    public static BehaviorTreeDocument CreateDocument() => BehaviorTreeDocument.FromTree(Create());

    public static string CreateJson() => CreateDocument().ToJson();
}
