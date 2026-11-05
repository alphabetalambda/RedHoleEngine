namespace RedHoleEngine.AI.BehaviorTrees;

/// <summary>
/// Result of ticking a behavior node. Composites use this to decide
/// whether to keep walking their children or wait until next frame.
/// </summary>
public enum NodeStatus
{
    Success = 0,
    Failure = 1,
    Running = 2
}
