using RedHoleEngine.Core;
using RedHoleEngine.Core.ECS;

namespace RedHoleEngine.Tests.Core;

public class GameLoopBehaviorTests
{
    public GameLoopBehaviorTests()
    {
        Time.Reset();
    }

    [Fact]
    public void CreateScene_SetsActive_AndUnloadClearsIt()
    {
        var loop = new GameLoop();
        var first = loop.CreateScene("A");
        var second = loop.CreateScene("B", setActive: false);

        Assert.Equal(first, loop.ActiveScene);
        Assert.Equal(2, loop.LoadedScenes.Count);

        loop.ActiveScene = second;
        Assert.Equal(second, loop.ActiveScene);
        loop.ActiveScene = second;

        loop.UnloadScene(second);
        Assert.Null(loop.ActiveScene);
        Assert.Single(loop.LoadedScenes);

        loop.Shutdown();
        Assert.Empty(loop.LoadedScenes);
    }

    [Fact]
    public void Update_ScalesTime_RunsFixedSteps_AndRenderEventsFire()
    {
        Time.Reset();
        Time.TimeScale = 2f;
        Time.FixedDeltaTime = 0.05f;

        var loop = new GameLoop { MaxFixedUpdatesPerFrame = 2 };
        var scene = loop.CreateScene("Play");
        var system = scene.World.AddSystem<CountSystem>();

        int pre = 0, post = 0, preRender = 0, postRender = 0;
        loop.OnPreUpdate += _ => pre++;
        loop.OnPostUpdate += _ => post++;
        loop.OnPreRender += () => preRender++;
        loop.OnPostRender += () => postRender++;

        loop.Update(0.2f); // unscaled clamped to 0.1, scaled to 0.2, fixed steps capped at 2
        Assert.Equal(1, pre);
        Assert.Equal(1, post);
        Assert.Equal(1, system.Updates);
        Assert.Equal(2, system.FixedUpdates);
        Assert.Equal(0.2f, Time.DeltaTime, 1e-4f);
        Assert.Equal(0.1f, Time.UnscaledDeltaTime, 1e-4f);
        Assert.Equal(1ul, Time.FrameCount);
        Assert.True(Time.FixedUpdateAlpha >= 0f);

        loop.Render();
        Assert.Equal(1, preRender);
        Assert.Equal(1, postRender);

        Time.Reset();
        Assert.Equal(0ul, Time.FrameCount);
        Assert.Equal(1f, Time.TimeScale);
    }

    [Fact]
    public void Update_WithoutScene_StillAdvancesTime()
    {
        Time.Reset();
        var loop = new GameLoop();
        loop.Update(0.016f);
        Assert.Equal(1ul, Time.FrameCount);
        Assert.False(Time.ShouldRunFixedUpdate() && Time.FixedUpdateAlpha < 0f);
    }

    private sealed class CountSystem : GameSystem
    {
        public int Updates;
        public int FixedUpdates;
        public override void Update(float deltaTime) => Updates++;
        public override void FixedUpdate(float fixedDeltaTime) => FixedUpdates++;
    }
}
