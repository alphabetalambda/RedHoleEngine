using RedHoleEngine.ML.Components;

namespace RedHoleEngine.Tests.ML;

public class MLComponentTests
{
    [Fact]
    public void MLAgentComponent_Create_SetsDefaults()
    {
        var agent = MLAgentComponent.Create("pilot", MLAgentType.Regressor, 0.25f);

        Assert.Equal("pilot", agent.ModelId);
        Assert.Equal(MLAgentType.Regressor, agent.AgentType);
        Assert.Equal(0.25f, agent.DecisionInterval);
        Assert.Equal(0f, agent.TimeSinceDecision);
        Assert.Equal(-1, agent.LastAction);
        Assert.Equal(0f, agent.LastConfidence);
        Assert.True(agent.IsActive);
    }

    [Fact]
    public void PlayerBehaviorComponent_Create_UsesProvidedSessionAndDefaults()
    {
        var provided = PlayerBehaviorComponent.Create("session-1");
        var generated = PlayerBehaviorComponent.Create();

        Assert.Equal("session-1", provided.SessionId);
        Assert.Equal(0.5f, provided.SkillLevel);
        Assert.Equal(0f, provided.ChurnRisk);
        Assert.Equal(0, provided.Deaths);
        Assert.Equal(0, provided.CompletedLevels);
        Assert.Equal(32, generated.SessionId.Length);
        Assert.NotEqual(provided.SessionId, generated.SessionId);
    }

    [Fact]
    public void PlayerBehaviorComponent_RecordInput_AveragesIntervalsAfterTheFirstSample()
    {
        var behavior = PlayerBehaviorComponent.Create("s");
        behavior.TimeSinceInput = 4f;

        behavior.RecordInput();

        Assert.Equal(1, behavior.InputCount);
        Assert.Equal(0f, behavior.AverageInputInterval);
        Assert.Equal(0f, behavior.TimeSinceInput);

        behavior.TimeSinceInput = 6f;
        behavior.RecordInput();

        Assert.Equal(2, behavior.InputCount);
        Assert.Equal(3f, behavior.AverageInputInterval);
        Assert.Equal(0f, behavior.TimeSinceInput);

        behavior.TimeSinceInput = 3f;
        behavior.RecordInput();

        Assert.Equal(3, behavior.InputCount);
        Assert.Equal(3f, behavior.AverageInputInterval);
    }

    [Fact]
    public void DifficultyComponent_Create_ClampsRangeAndEnablesAutoAdjust()
    {
        var difficulty = DifficultyComponent.Create(1.25f, 0.4f, 3f);

        Assert.Equal(1.25f, difficulty.DifficultyMultiplier);
        Assert.Equal(1.25f, difficulty.TargetDifficulty);
        Assert.Equal(0.1f, difficulty.AdjustmentSpeed);
        Assert.Equal(0.4f, difficulty.MinDifficulty);
        Assert.Equal(3f, difficulty.MaxDifficulty);
        Assert.True(difficulty.AutoAdjust);
    }

    [Fact]
    public void AnomalyMonitorComponent_Create_StartsUnflagged()
    {
        var monitor = AnomalyMonitorComponent.Create("watch", 0.8f, 2.5f);

        Assert.Equal("watch", monitor.ModelId);
        Assert.Equal(0f, monitor.AnomalyScore);
        Assert.Equal(0.8f, monitor.Threshold);
        Assert.False(monitor.IsFlagged);
        Assert.Equal(0, monitor.FlagCount);
        Assert.Equal(2.5f, monitor.CheckInterval);
        Assert.Equal(0f, monitor.TimeSinceCheck);
    }
}
