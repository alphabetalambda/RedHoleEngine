using System.Numerics;
using RedHoleEngine.Components;
using RedHoleEngine.Core.ECS;
using RedHoleEngine.Profiling;

namespace RedHoleEngine.Tests.Profiling;

public class ProfilerOverlayBehaviorTests
{
    [Fact]
    public void Create_BuildsUiTextThatIncludesFrameStatsTimersAndCounters()
    {
        var overlay = ProfilerOverlayComponent.Create(
            position: new Vector2(4, 8),
            showTimers: true,
            showCounters: true,
            showGraph: true);
        Assert.Equal(new Vector2(4, 8), overlay.Position);
        Assert.True(overlay.ShowTimers);
        Assert.Equal(200f, overlay.GraphWidth);

        var profiler = Profiler.Instance;
        profiler.Reset();
        profiler.IsEnabled = true;
        profiler.BeginFrame();
        profiler.EndFrame();
        var timer = profiler.GetOrCreateTimer("sim", "Game");
        timer.Start();
        timer.Stop();
        profiler.Increment("draws", "Render", 3);

        using var world = new World();
        var system = world.AddSystem<ProfilerOverlaySystem>();
        Assert.Equal(1000, system.Priority);
        var entity = world.CreateEntity();
        world.AddComponent(entity, overlay);

        system.Update(0.016f);
        ref var stored = ref world.GetComponent<ProfilerOverlayComponent>(entity);
        Assert.True(stored.Initialized);
        var text = world.GetComponent<UiTextComponent>(stored.TextEntity).Text;
        Assert.Contains("FPS:", text);
        Assert.Contains("sim:", text);
        Assert.Contains("draws: 3", text);
        Assert.True(world.GetComponent<UiRectComponent>(stored.BackgroundEntity).Size.Y > 0);

        system.OnDestroy();
        profiler.Reset();
    }

    [Fact]
    public void BannerPrinters_WriteAndRestoreConsoleColor()
    {
        var before = Console.ForegroundColor;
        Enginedeco.EngineTitlePrint();
        Enginedeco.companylogoprint();
        Assert.Equal(before, Console.ForegroundColor);
    }
}
