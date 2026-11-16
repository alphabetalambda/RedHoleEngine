using System.Numerics;
using RedHoleEngine.Physics;
using RedHoleEngine.Rendering;

namespace RedHoleEngine.Tests.Rendering;

public class AccumulationControlTests
{
    [Fact]
    public void SamplesPerFrame_IsRaysTimesSamples()
    {
        var settings = new RaytracerSettings { RaysPerPixel = 3, SamplesPerFrame = 2 };
        Assert.Equal(6, AccumulationPolicy.SamplesPerFrame(settings));

        settings.RaysPerPixel = 0;
        settings.SamplesPerFrame = -1;
        Assert.Equal(1, AccumulationPolicy.SamplesPerFrame(settings));
    }

    [Theory]
    [InlineData(0, 1, 0, false)]          // 0 = unlimited
    [InlineData(100000, 4, 0, false)]     // 0 = unlimited
    [InlineData(5, 1, -3, false)]         // negative treated as unlimited
    [InlineData(3, 1, 4, false)]
    [InlineData(4, 1, 4, true)]
    [InlineData(5, 1, 4, true)]
    [InlineData(2, 2, 5, false)]          // 4 samples < 5
    [InlineData(3, 2, 5, true)]           // 6 samples >= 5
    [InlineData(0, 8, 1, false)]          // nothing accumulated yet
    public void IsSampleCapReached_ComparesAccumulatedSamples(long frames, int samplesPerFrame, int cap, bool expected)
    {
        Assert.Equal(expected, AccumulationPolicy.IsSampleCapReached(frames, samplesPerFrame, cap));
    }

    [Fact]
    public void IsSampleCapReached_DoesNotOverflowForLargeCounts()
    {
        Assert.True(AccumulationPolicy.IsSampleCapReached(int.MaxValue, 64, int.MaxValue));
    }

    [Fact]
    public void IsSampleCapReached_Settings_IgnoresCapWhenNotAccumulating()
    {
        var settings = new RaytracerSettings { Accumulate = false, MaxAccumulatedSamples = 1 };
        Assert.False(AccumulationPolicy.IsSampleCapReached(settings, 10));

        settings.Accumulate = true;
        Assert.True(AccumulationPolicy.IsSampleCapReached(settings, 10));

        settings.RaysPerPixel = 2;
        settings.MaxAccumulatedSamples = 21;
        Assert.False(AccumulationPolicy.IsSampleCapReached(settings, 10));
        Assert.True(AccumulationPolicy.IsSampleCapReached(settings, 11));
    }

    [Fact]
    public void Clamp_KeepsSampleCapNonNegative()
    {
        var settings = new RaytracerSettings { MaxAccumulatedSamples = -5 };
        settings.Clamp();
        Assert.Equal(0, settings.MaxAccumulatedSamples);

        settings.MaxAccumulatedSamples = 4096;
        settings.Clamp();
        Assert.Equal(4096, settings.MaxAccumulatedSamples);
    }

    [Fact]
    public void NewSettings_HaveExpectedDefaults()
    {
        var settings = new RaytracerSettings();
        Assert.Equal(0, settings.MaxAccumulatedSamples);
        Assert.True(settings.AutoResetAccumulationOnSceneChange);
        Assert.False(settings.DenoiseUseHistory);
    }

    [Fact]
    public void SceneChangeTracker_FirstUpdateEstablishesBaseline()
    {
        var tracker = new SceneChangeTracker();
        Assert.False(tracker.Update(123));
        Assert.False(tracker.Update(123));
    }

    [Fact]
    public void SceneChangeTracker_DetectsUploadsOnce()
    {
        var tracker = new SceneChangeTracker();
        tracker.Update(0);

        tracker.MarkDirty();
        Assert.Equal(1, tracker.Revision);
        Assert.True(tracker.Update(0));
        Assert.False(tracker.Update(0));

        tracker.MarkDirty();
        tracker.MarkDirty();
        Assert.True(tracker.Update(0));
        Assert.False(tracker.Update(0));
    }

    [Fact]
    public void SceneChangeTracker_DetectsFrameStateChanges()
    {
        var tracker = new SceneChangeTracker();
        tracker.Update(1);

        Assert.True(tracker.Update(2));
        Assert.False(tracker.Update(2));
        Assert.True(tracker.Update(1));
    }

    [Fact]
    public void SceneChangeTracker_ResetForgetsBaseline()
    {
        var tracker = new SceneChangeTracker();
        tracker.Update(1);
        tracker.Reset();
        Assert.False(tracker.Update(99));
    }

    [Fact]
    public void HashBlackHole_ChangesWithRenderedParameters()
    {
        Assert.Equal(0, SceneChangeTracker.HashBlackHole(null));

        var blackHole = new BlackHole(Vector3.Zero, 1.0f);
        int baseline = SceneChangeTracker.HashBlackHole(blackHole);
        Assert.Equal(baseline, SceneChangeTracker.HashBlackHole(blackHole));

        blackHole.Position = new Vector3(1, 0, 0);
        int moved = SceneChangeTracker.HashBlackHole(blackHole);
        Assert.NotEqual(baseline, moved);

        blackHole.Spin = 0.5f;
        Assert.NotEqual(moved, SceneChangeTracker.HashBlackHole(blackHole));

        int beforeDisk = SceneChangeTracker.HashBlackHole(blackHole);
        blackHole.DiskOuterRadius += 1f;
        Assert.NotEqual(beforeDisk, SceneChangeTracker.HashBlackHole(blackHole));
    }
}
