using System.Numerics;
using RedHoleEngine.Audio;

namespace RedHoleEngine.Tests.Audio;

public class AudioMixerBehaviorTests
{
    private static AcousticPath Path(PathType type, float response, bool direct, float time, float doppler = 1f, float gravity = 1f, Vector3? hit = null)
    {
        var path = new AcousticPath
        {
            Type = type,
            IsDirect = direct,
            TotalTime = time,
            TotalDistance = time * 343f,
            FinalResponse = FrequencyResponse.Uniform(response),
            DopplerShift = doppler,
            GravitationalShift = gravity
        };
        if (hit.HasValue)
            path.Hits.Add(new AcousticRayHit { Position = hit.Value });
        return path;
    }

    [Fact]
    public void ProcessPaths_Empty_IsSilent_AndCachesByEntity()
    {
        var mixer = new AudioMixer { MasterVolume = 0.5f, ListenerPosition = Vector3.Zero, ListenerForward = Vector3.UnitZ };
        var silent = mixer.ProcessPaths(7, "clip", Vector3.UnitX, 1f, 1.2f, new List<AcousticPath>());
        Assert.Equal(0f, silent.Volume);
        Assert.Equal(1.2f, silent.Pitch);
        Assert.Equal("clip", silent.ClipId);

        var again = mixer.ProcessPaths(7, "other", Vector3.Zero, 1f, 1f, new List<AcousticPath>());
        Assert.Same(silent, again);
        Assert.Equal("other", again.ClipId);

        mixer.ClearSource(7);
        mixer.Clear();
    }

    [Fact]
    public void ProcessPaths_DirectPath_ScalesVolumePitchPanAndFilters()
    {
        var mixer = new AudioMixer
        {
            MasterVolume = 0.5f,
            ListenerPosition = Vector3.Zero,
            ListenerForward = -Vector3.UnitZ,
            ListenerUp = Vector3.UnitY,
            EnableHRTF = true
        };

        var muffled = new FrequencyResponse
        {
            Band63Hz = 0.1f,
            Band125Hz = 0.1f,
            Band250Hz = 0.8f,
            Band500Hz = 0.8f,
            Band1kHz = 0.8f,
            Band2kHz = 0.8f,
            Band4kHz = 0.1f,
            Band8kHz = 0.1f
        };
        var direct = new AcousticPath
        {
            Type = PathType.Direct,
            IsDirect = true,
            TotalTime = 0.2f,
            FinalResponse = muffled,
            DopplerShift = 1.1f,
            GravitationalShift = 0.9f
        };

        var processed = mixer.ProcessPaths(1, "a", new Vector3(1, 0, -4), 0.8f, 2f, new List<AcousticPath> { direct });
        Assert.Equal(0.8f * muffled.Average * 0.5f, processed.Volume, 1e-4f);
        Assert.Equal(2f * 1.1f * 0.9f, processed.Pitch, 1e-4f);
        Assert.Equal(0.2f, processed.Delay);
        Assert.True(processed.UseHRTF);
        Assert.True(processed.Pan > 0f); // source is to the right of a listener looking down -Z? 
        Assert.True(processed.LowPassCutoff < 20000f);
        Assert.True(processed.HighPassCutoff > 20f);
        Assert.Equal(0.3f, processed.ReverbSend);
        Assert.Equal(muffled.Band1kHz, processed.EQGain.Band1kHz);
    }

    [Fact]
    public void ProcessPaths_Reflections_AreCappedAndDriveReverb()
    {
        var mixer = new AudioMixer { ListenerForward = Vector3.UnitZ, ListenerUp = Vector3.UnitY };
        var paths = new List<AcousticPath>
        {
            Path(PathType.Direct, 0.8f, true, 0.1f)
        };
        for (int i = 0; i < 10; i++)
            paths.Add(Path(PathType.EarlyReflection, 0.4f, false, 0.2f + i * 0.01f, hit: new Vector3(i, 0, 1)));
        paths.Add(Path(PathType.LateReflection, 0.2f, false, 0.5f));
        paths.Add(Path(PathType.LateReflection, 0.4f, false, 0.6f));

        var processed = mixer.ProcessPaths(3, "b", Vector3.UnitZ, 1f, 1f, paths);
        Assert.Equal(8, processed.EarlyReflections.Count);
        Assert.All(processed.EarlyReflections, r => Assert.Equal(0.2f, r.Volume, 1e-4f));
        Assert.Equal(0.6f, processed.ReverbSend, 1e-4f); // avg(0.2,0.4)*2
        Assert.True(processed.LowPassCutoff >= 20000f);
        Assert.Equal(20f, processed.HighPassCutoff);
    }

    [Fact]
    public void PrioritizeVoices_DropsSilence_AndHonorsPriorityThenVolume()
    {
        var mixer = new AudioMixer { MaxVoices = 2 };
        ProcessedAudioSource Make(int id, float volume) => new() { SourceEntityId = id, Volume = volume };
        var sources = new[]
        {
            Make(1, 0.9f),
            Make(2, 0.0001f),
            Make(3, 0.2f),
            Make(4, 0.4f)
        };
        var kept = mixer.PrioritizeVoices(sources, new Dictionary<int, int> { [3] = 10, [4] = 10 });
        Assert.Equal(new[] { 4, 3 }, kept.Select(s => s.SourceEntityId).ToArray());
    }

    [Fact]
    public void ListenerRight_IsCrossOfForwardAndUp()
    {
        var mixer = new AudioMixer { ListenerForward = Vector3.UnitZ, ListenerUp = Vector3.UnitY };
        Assert.Equal(-Vector3.UnitX, mixer.ListenerRight);
        Assert.Equal(ReverbZoneComponent.SmallRoom.ReverbTime, mixer.GlobalReverb.ReverbTime);
    }
}
