using RedHoleEngine.AI.BehaviorTrees;
using RedHoleEngine.Core.ECS;

namespace RedHoleEngine.AI;

/// <summary>
/// Ticks every <see cref="BehaviorAgentComponent"/>.
/// Runs after <see cref="Perception.PerceptionSystem"/> and <see cref="Director.DirectorSystem"/>
/// so the tree sees this frame's senses and director hints.
/// </summary>
public class BehaviorTreeSystem : GameSystem
{
    public override int Priority => 200;

    public override void Update(float deltaTime)
    {
        foreach (var entity in World.Query<BehaviorAgentComponent>())
        {
            ref var agent = ref World.GetComponent<BehaviorAgentComponent>(entity);
            var tree = agent.Tree;
            if (!agent.Enabled || tree == null)
                continue;

            var blackboard = agent.Blackboard ?? new Blackboard();
            var gates = agent.Gates ?? new BehaviorGateSet();
            agent.Blackboard = blackboard;
            agent.Gates = gates;
            tree.RecordTrace = agent.RecordTrace;

            var context = new BehaviorContext(World, entity, blackboard, gates, deltaTime);
            agent.LastStatus = tree.Tick(context);
            agent.LastTrace = tree.LastTrace?.ToString();
        }
    }
}
