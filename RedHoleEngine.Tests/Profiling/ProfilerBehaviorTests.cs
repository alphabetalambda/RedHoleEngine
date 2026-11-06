using RedHoleEngine.Profiling;

namespace RedHoleEngine.Tests.Profiling;

public class ProfilerBehaviorTests
{
    [Fact]
    public void FramesCountersAndReport_TrackEnabledWork()
    {
        var profiler = Profiler.Instance;
        profiler.Reset();
        profiler.IsEnabled = true;

        profiler.BeginFrame();
        profiler.EndFrame();
        Assert.Equal(1, profiler.FrameCount);
        Assert.True(profiler.LastFrameTimeMs >= 0);
        Assert.Single(profiler.FrameHistory);
        Assert.True(profiler.CurrentFps >= 0);
        Assert.Equal(profiler.CurrentFps, profiler.OnePercentLowFps);

        profiler.RecordGpuTime(2.5);
        profiler.RecordGpuTime(3.5);
        Assert.Equal(3.5, profiler.LastGpuTimeMs);
        Assert.Equal(3.0, profiler.AverageGpuTimeMs, 1e-6);

        profiler.Increment("draws", "Render", 4);
        profiler.SetCounter("entities", 9, "World");
        Assert.Equal(4, profiler.Counters["Render/draws"].Value);
        Assert.Equal(9, profiler.Counters["World/entities"].Value);
        profiler.Counters["Render/draws"].Decrement(1);
        profiler.Counters["Render/draws"].Sample();
        Assert.Equal(3, profiler.Counters["Render/draws"].LastValue);
        Assert.True(profiler.Counters["Render/draws"].Average > 0);

        var timer = profiler.GetOrCreateTimer("update", "Game");
        Assert.Same(timer, profiler.GetOrCreateTimer("update", "Game"));
        timer.Start();
        timer.Stop();
        Assert.Equal(1, timer.SampleCount);
        Assert.False(timer.IsRunning);
        Assert.True(timer.LastElapsedMs >= 0);
        Assert.Contains("Game/update", timer.ToString());

        using (profiler.Scope("scope", "Game"))
        {
        }
        Assert.True(profiler.Timers["Game/scope"].SampleCount >= 1);

        using (new ProfilerScope(timer: null, enabled: true))
        {
        }

        var disabled = new ProfilerScope(timer, enabled: false);
        disabled.Dispose();

        for (int i = 0; i < 12; i++)
            profiler.RecordGpuTime(i + 1);
        Assert.True(profiler.OnePercentLowFps >= 0);
        Assert.True(profiler.AverageFps >= 0);

        var report = profiler.GenerateReport();
        Assert.Contains("Profiler Report", report);
        Assert.Contains("update", report);
        var status = profiler.GetStatusLine();
        Assert.Contains("FPS", status);

        profiler.IsEnabled = false;
        var frames = profiler.FrameCount;
        profiler.BeginFrame();
        profiler.EndFrame();
        profiler.Increment("draws", "Render");
        Assert.Equal(frames, profiler.FrameCount);

        profiler.Reset();
        profiler.IsEnabled = true;
        Assert.Equal(0, profiler.FrameCount);
        Assert.Equal(0, profiler.LastGpuTimeMs);
        Assert.Equal(0, timer.SampleCount);
    }

    [Fact]
    public void GpuHistory_DropsSamplesBeyondTheCap()
    {
        var profiler = Profiler.Instance;
        profiler.Reset();
        profiler.IsEnabled = true;
        for (int i = 0; i < 305; i++)
        {
            profiler.BeginFrame();
            profiler.EndFrame();
            profiler.RecordGpuTime(i);
        }

        Assert.Equal(300, profiler.FrameHistory.Count);
        Assert.Equal(300, profiler.GpuFrameHistory.Count);
        Assert.Equal(304, profiler.LastGpuTimeMs);
        profiler.Reset();
    }
}
