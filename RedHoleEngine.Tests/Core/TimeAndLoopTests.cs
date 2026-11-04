using RedHoleEngine.Core;
using RedHoleEngine.Core.ECS;
using RedHoleEngine.Core.Scene;

namespace RedHoleEngine.Tests.Core;

public class TimeAndLoopTests
{
    private sealed class LoopProbe : GameSystem
    {
        public int Updates;
        public int FixedUpdates;
        public float LastUpdateDt;
        public float LastFixedDt;

        public override void Update(float deltaTime)
        {
            Updates++;
            LastUpdateDt = deltaTime;
        }

        public override void FixedUpdate(float fixedDeltaTime)
        {
            FixedUpdates++;
            LastFixedDt = fixedDeltaTime;
        }
    }

    public TimeAndLoopTests()
    {
        ResetTime();
    }

    [Fact]
    public void Update_AdvancesScaledClampedTimeAndFrameCount()
    {
        var loop = new GameLoop();
        var pre = new List<float>();
        var post = new List<float>();
        loop.OnPreUpdate += pre.Add;
        loop.OnPostUpdate += post.Add;

        loop.Update(0.05f);

        Assert.Equal(0.05f, Time.UnscaledDeltaTime, 0.0001f);
        Assert.Equal(0.05f, Time.DeltaTime, 0.0001f);
        Assert.Equal(0.05f, Time.TotalTime, 0.0001f);
        Assert.Equal(1ul, Time.FrameCount);
        Assert.Equal(new[] { 0.05f }, pre);
        Assert.Equal(new[] { 0.05f }, post);

        Time.TimeScale = 2f;
        loop.Update(0.05f);

        Assert.Equal(0.05f, Time.UnscaledDeltaTime, 0.0001f);
        Assert.Equal(0.1f, Time.DeltaTime, 0.0001f);
        Assert.Equal(0.15f, Time.TotalTime, 0.0001f);
        Assert.Equal(2ul, Time.FrameCount);
        Assert.Equal(0.05f, pre[1]);
    }

    [Fact]
    public void Update_ClampsRawDeltaAndPassesRawValueToCallbacks()
    {
        var loop = new GameLoop();
        var scene = loop.CreateScene("Level");
        var probe = scene.World.AddSystem(new LoopProbe());
        float? seen = null;
        loop.OnPreUpdate += value => seen = value;

        loop.Update(5f);

        Assert.Equal(Time.MaxDeltaTime, Time.UnscaledDeltaTime, 0.0001f);
        Assert.Equal(Time.MaxDeltaTime, Time.DeltaTime, 0.0001f);
        Assert.Equal(5f, seen);
        Assert.Equal(Time.MaxDeltaTime, probe.LastUpdateDt, 0.0001f);
        Assert.Equal(1, probe.Updates);
    }

    [Fact]
    public void Update_RunsFixedStepsUntilCapAndExposesAlpha()
    {
        Time.Reset();
        var loop = new GameLoop();
        var scene = loop.CreateScene("Level");
        var probe = scene.World.AddSystem(new LoopProbe());
        Time.FixedDeltaTime = 0.02f;

        loop.Update(0.05f);

        Assert.Equal(2, probe.FixedUpdates);
        Assert.Equal(0.02f, probe.LastFixedDt, 0.0001f);
        Assert.Equal(0.5f, Time.FixedUpdateAlpha, 0.0001f);

        probe.FixedUpdates = 0;
        loop.MaxFixedUpdatesPerFrame = 3;
        Time.FixedDeltaTime = 0.01f;
        Time.Reset();
        Time.FixedDeltaTime = 0.01f;
        loop.Update(0.1f);

        Assert.Equal(3, probe.FixedUpdates);
        Assert.InRange(Time.FixedUpdateAlpha, 5f, 8f);
    }

    [Fact]
    public void Update_ZeroFixedBudgetOrMissingScene_StillConsumesAccumulator()
    {
        var loop = new GameLoop();
        Time.FixedDeltaTime = 0.05f;
        loop.MaxFixedUpdatesPerFrame = 0;

        loop.Update(0.05f);

        Assert.Equal(0f, Time.FixedUpdateAlpha, 0.0001f);
        Assert.Null(loop.ActiveScene);
    }

    [Fact]
    public void Update_TimeScaleZero_DoesNotAdvanceTotalTime()
    {
        var loop = new GameLoop();
        Time.TimeScale = 0f;

        loop.Update(0.25f);

        Assert.Equal(0f, Time.DeltaTime);
        Assert.Equal(0.1f, Time.UnscaledDeltaTime, 0.0001f);
        Assert.Equal(0f, Time.TotalTime);
        Assert.Equal(1ul, Time.FrameCount);
    }

    [Fact]
    public void CreateScene_ActiveSceneAndUnload()
    {
        var loop = new GameLoop();
        var first = loop.CreateScene("First");
        var second = loop.CreateScene("Second", setActive: false);
        var external = new RedHoleEngine.Core.Scene.Scene("External");

        Assert.Same(first, loop.ActiveScene);
        Assert.Equal(new[] { first, second }, loop.LoadedScenes.ToArray());

        loop.ActiveScene = first;
        Assert.Equal(2, loop.LoadedScenes.Count);

        loop.ActiveScene = external;
        Assert.Contains(external, loop.LoadedScenes);
        Assert.Same(external, loop.ActiveScene);

        loop.ActiveScene = null;
        Assert.Null(loop.ActiveScene);
        Assert.Contains(first, loop.LoadedScenes);

        loop.ActiveScene = first;
        loop.UnloadScene(second);
        Assert.Same(first, loop.ActiveScene);
        Assert.DoesNotContain(second, loop.LoadedScenes);
        Assert.Empty(second.World.Systems);

        loop.UnloadScene(first);
        Assert.Null(loop.ActiveScene);
        Assert.DoesNotContain(first, loop.LoadedScenes);
    }

    [Fact]
    public void Render_RaisesPreAndPostWithoutAScene()
    {
        var loop = new GameLoop();
        var order = new List<string>();
        loop.OnPreRender += () => order.Add("pre");
        loop.OnPostRender += () => order.Add("post");

        loop.Render();

        Assert.Equal(new[] { "pre", "post" }, order);
    }

    [Fact]
    public void Shutdown_DisposesLoadedScenes()
    {
        var loop = new GameLoop();
        var scene = loop.CreateScene("Level");
        scene.World.AddSystem(new LoopProbe());

        loop.Shutdown();
        loop.Shutdown();

        Assert.Null(loop.ActiveScene);
        Assert.Empty(loop.LoadedScenes);
        Assert.Empty(scene.World.Systems);
    }

    private static void ResetTime()
    {
        Time.Reset();
        Time.FixedDeltaTime = 1f / 60f;
        Time.TimeScale = 1f;
    }
}
