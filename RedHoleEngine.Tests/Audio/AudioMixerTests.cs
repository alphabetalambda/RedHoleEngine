using System.Numerics;
using RedHoleEngine.Audio;

namespace RedHoleEngine.Tests.Audio;

public class AudioMixerTests
{
    [Fact]
    public void Pan_FollowsListenerAxes_AndDefaultsToMirroredX()
    {
        var engineConvention = new AudioMixer
        {
            ListenerPosition = Vector3.Zero,
            ListenerForward = -Vector3.UnitZ,
            ListenerUp = Vector3.UnitY
        };
        Assert.Equal(Vector3.UnitX, engineConvention.ListenerRight);

        var right = engineConvention.ProcessPaths(1, "a", new Vector3(4, 0, 0), 1f, 1f, new List<AcousticPath> { Direct(1f) });
        var left = engineConvention.ProcessPaths(2, "a", new Vector3(-4, 0, 0), 1f, 1f, new List<AcousticPath> { Direct(1f) });
        var ahead = engineConvention.ProcessPaths(3, "a", new Vector3(0, 0, -6), 1f, 1f, new List<AcousticPath> { Direct(1f) });
        var behind = engineConvention.ProcessPaths(4, "a", new Vector3(0, 0, 6), 1f, 1f, new List<AcousticPath> { Direct(1f) });

        Assert.Equal(1f, right.Pan, 1e-5);
        Assert.Equal(-1f, left.Pan, 1e-5);
        Assert.Equal(0f, ahead.Pan, 1e-5);
        Assert.Equal(0f, behind.Pan, 1e-5);

        // The mixer's own default forward is +Z, so Cross(+Z, +Y) is -X and world +X pans left.
        var defaults = new AudioMixer { ListenerPosition = Vector3.Zero };
        Assert.Equal(Vector3.UnitZ, defaults.ListenerForward);
        Assert.Equal(-Vector3.UnitX, defaults.ListenerRight);
        var mirrored = defaults.ProcessPaths(5, "a", Vector3.UnitX, 1f, 1f, new List<AcousticPath> { Direct(1f) });
        Assert.Equal(-1f, mirrored.Pan, 1e-5);
    }

    [Fact]
    public void ProcessPaths_UsesDirectPathNumbers_AndExactFilterCutoffs()
    {
        var mixer = new AudioMixer
        {
            MasterVolume = 0.5f,
            EnableHRTF = false,
            ListenerPosition = Vector3.Zero,
            ListenerForward = -Vector3.UnitZ,
            ListenerUp = Vector3.UnitY
        };

        var response = new FrequencyResponse
        {
            Band63Hz = 0.2f,
            Band125Hz = 0.2f,
            Band250Hz = 1f,
            Band500Hz = 1f,
            Band1kHz = 1f,
            Band2kHz = 1f,
            Band4kHz = 0.1f,
            Band8kHz = 0.1f
        };
        var direct = new AcousticPath
        {
            Type = PathType.Direct,
            IsDirect = true,
            TotalTime = 0.25f,
            TotalDistance = 12f,
            FinalResponse = response,
            DopplerShift = 1.25f,
            GravitationalShift = 0.8f
        };

        var processed = mixer.ProcessPaths(4, "engine", new Vector3(1, 2, 3), 0.8f, 2f, new List<AcousticPath> { direct });

        Assert.Equal(0.8f * response.Average * 0.5f, processed.Volume, 1e-5);
        Assert.Equal(2f * 1.25f * 0.8f, processed.Pitch, 1e-5);
        Assert.Equal(0.25f, processed.Delay);
        Assert.False(processed.UseHRTF);
        Assert.Equal(new Vector3(1, 2, 3), processed.Position);
        Assert.Equal(response.Band4kHz, processed.EQGain.Band4kHz);
        Assert.Equal(2600f, processed.LowPassCutoff, 1e-3);   // 2000 + 0.1 * 6000
        Assert.Equal(160f, processed.HighPassCutoff, 1e-3);   // 200 * (1 - 0.2)
        Assert.Equal(0.3f, processed.ReverbSend);
    }

    [Fact]
    public void Filters_StayOpenAtThePointThreeBoundary_AndEngageJustBelowIt()
    {
        var mixer = new AudioMixer();
        var open = FrequencyResponse.Uniform(0.3f);
        var openPath = Direct(1f);
        openPath.FinalResponse = open;
        var opened = mixer.ProcessPaths(1, "a", Vector3.Zero, 1f, 1f, new List<AcousticPath> { openPath });
        Assert.Equal(20000f, opened.LowPassCutoff);
        Assert.Equal(20f, opened.HighPassCutoff);

        var closed = FrequencyResponse.Uniform(0.299f);
        var closedPath = Direct(1f);
        closedPath.FinalResponse = closed;
        var filtered = mixer.ProcessPaths(2, "b", Vector3.Zero, 1f, 1f, new List<AcousticPath> { closedPath });
        Assert.Equal(2000f + 0.299f * 6000f, filtered.LowPassCutoff, 1e-2);
        Assert.Equal(200f * (1f - 0.299f), filtered.HighPassCutoff, 1e-2);
    }

    [Fact]
    public void DirectPath_WinsOverAnEarlierOccludedPath()
    {
        var mixer = new AudioMixer { MasterVolume = 1f };
        var transmission = new AcousticPath
        {
            Type = PathType.Transmission,
            IsDirect = false,
            TotalTime = 0.9f,
            FinalResponse = FrequencyResponse.Uniform(0.1f),
            DopplerShift = 0.5f,
            GravitationalShift = 1f
        };
        var direct = Direct(0.8f);
        direct.TotalTime = 0.2f;
        direct.DopplerShift = 1.5f;

        var processed = mixer.ProcessPaths(1, "a", Vector3.Zero, 1f, 1f, new List<AcousticPath> { transmission, direct });
        Assert.Equal(0.8f, processed.Volume, 1e-5);
        Assert.Equal(1.5f, processed.Pitch, 1e-5);
        Assert.Equal(0.2f, processed.Delay);
    }

    [Fact]
    public void Reflections_CapAtEight_AndLateEnergySetsReverbSend()
    {
        var mixer = new AudioMixer
        {
            ListenerPosition = Vector3.Zero,
            ListenerForward = -Vector3.UnitZ,
            ListenerUp = Vector3.UnitY
        };

        var paths = new List<AcousticPath> { Direct(1f) };
        for (int i = 0; i < 10; i++)
        {
            var early = new AcousticPath
            {
                Type = PathType.EarlyReflection,
                IsDirect = false,
                TotalTime = 0.05f,
                FinalResponse = FrequencyResponse.Uniform(0.4f)
            };
            early.Hits.Add(new AcousticRayHit { Position = new Vector3(3, 0, 0) });
            paths.Add(early);
        }
        paths.Add(Late(0.8f));

        var processed = mixer.ProcessPaths(1, "a", Vector3.Zero, 1f, 1f, paths);
        Assert.Equal(8, processed.EarlyReflections.Count);
        Assert.All(processed.EarlyReflections, r =>
        {
            Assert.Equal(0.2f, r.Volume, 1e-5); // 0.4 * 0.5
            Assert.Equal(0.05f, r.Delay);
            Assert.Equal(1f, r.Pan, 1e-4);
        });
        Assert.Equal(1f, processed.ReverbSend); // min(1, 0.8 * 2)

        var mild = mixer.ProcessPaths(2, "b", Vector3.Zero, 1f, 1f, new List<AcousticPath>
        {
            Direct(1f), Late(0.1f), Late(0.2f)
        });
        Assert.Equal(0.3f, mild.ReverbSend, 1e-5); // average 0.15 * 2

        var noLate = mixer.ProcessPaths(3, "c", Vector3.Zero, 1f, 1f, new List<AcousticPath> { Direct(0.5f) });
        Assert.Equal(0.3f, noLate.ReverbSend);
        Assert.Empty(noLate.EarlyReflections);
    }

    [Fact]
    public void EmptyPaths_SilenceTheSource_AndKeepTheCachedInstance()
    {
        var mixer = new AudioMixer { MasterVolume = 0.25f, EnableHRTF = true };
        var first = mixer.ProcessPaths(9, "first", Vector3.UnitY, 1f, 1.7f, new List<AcousticPath> { Direct(1f) });
        Assert.True(first.UseHRTF);
        Assert.Equal(0.25f, first.Volume, 1e-5);

        var silent = mixer.ProcessPaths(9, "second", Vector3.Zero, 1f, 0.4f, new List<AcousticPath>());
        Assert.Same(first, silent);
        Assert.Equal(0f, silent.Volume);
        Assert.Equal(0.4f, silent.Pitch);
        Assert.Equal("second", silent.ClipId);
        Assert.Empty(silent.EarlyReflections);
        // The empty-path branch does not clear HRTF; the previous value sticks.
        Assert.True(silent.UseHRTF);

        mixer.ClearSource(9);
        var fresh = mixer.ProcessPaths(9, "third", Vector3.Zero, 1f, 1f, new List<AcousticPath>());
        Assert.NotSame(silent, fresh);
        Assert.False(fresh.UseHRTF);

        mixer.Clear();
        var afterClear = mixer.ProcessPaths(9, "fourth", Vector3.Zero, 1f, 1f, new List<AcousticPath> { Direct(1f) });
        Assert.NotSame(fresh, afterClear);
    }

    [Fact]
    public void PrioritizeVoices_FiltersTheSilenceBoundary_ThenPriorityThenVolume()
    {
        var mixer = new AudioMixer { MaxVoices = 2 };
        var sources = new[]
        {
            new ProcessedAudioSource { SourceEntityId = 1, Volume = 0.9f },
            new ProcessedAudioSource { SourceEntityId = 2, Volume = 0.001f },
            new ProcessedAudioSource { SourceEntityId = 3, Volume = 0.0011f },
            new ProcessedAudioSource { SourceEntityId = 4, Volume = 0.2f },
            new ProcessedAudioSource { SourceEntityId = 5, Volume = 0.8f }
        };

        var kept = mixer.PrioritizeVoices(sources, new Dictionary<int, int> { [4] = 5, [5] = 5 });
        Assert.Equal(new[] { 5, 4 }, kept.Select(s => s.SourceEntityId).ToArray());

        mixer.MaxVoices = 0;
        Assert.Empty(mixer.PrioritizeVoices(sources, new Dictionary<int, int>()));

        mixer.MaxVoices = 1;
        var fallback = mixer.PrioritizeVoices(sources, new Dictionary<int, int>());
        Assert.Equal(1, Assert.Single(fallback).SourceEntityId);
    }

    private static AcousticPath Direct(float response) => new()
    {
        Type = PathType.Direct,
        IsDirect = true,
        TotalTime = 0.1f,
        FinalResponse = FrequencyResponse.Uniform(response),
        DopplerShift = 1f,
        GravitationalShift = 1f
    };

    private static AcousticPath Late(float response) => new()
    {
        Type = PathType.LateReflection,
        IsDirect = false,
        TotalTime = 0.2f,
        FinalResponse = FrequencyResponse.Uniform(response),
        DopplerShift = 1f,
        GravitationalShift = 1f
    };
}
