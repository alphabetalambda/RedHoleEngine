using System.Numerics;
using RedHoleEngine.Audio;

namespace RedHoleEngine.Tests.Audio;

/// <summary>
/// In-memory audio backend so engine tests never open an OpenAL device.
/// Records the calls the engine and collision audio make so tests can assert
/// volumes, pitches, positions, filters, and listener updates.
/// </summary>
public sealed class FakeAudioBackend : IAudioBackend
{
    public bool InitializeResult { get; set; } = true;
    public bool IsInitialized { get; private set; }
    public string Name => "Fake";
    public int UpdateCount { get; private set; }
    public float MasterVolume { get; private set; } = 1f;
    public float SpeedOfSound { get; private set; }
    public float DopplerFactor { get; private set; }
    public Vector3 ListenerPosition { get; private set; }
    public Vector3 ListenerVelocity { get; private set; }
    public Vector3 ListenerForward { get; private set; }
    public Vector3 ListenerUp { get; private set; }
    public List<string> LoadedPaths { get; } = new();
    public List<uint> PlayedClips { get; } = new();
    public List<VoiceHandle> PlayedVoices { get; } = new();
    public List<bool> PlayedLoops { get; } = new();
    public List<uint> StoppedVoices { get; } = new();
    public List<uint> UnloadedClipIds { get; } = new();
    public List<string> CallLog { get; } = new();
    public Dictionary<uint, float> Volumes { get; } = new();
    public Dictionary<uint, float> Pitches { get; } = new();
    public Dictionary<uint, Vector3> Positions { get; } = new();
    public Dictionary<uint, Vector3> Velocities { get; } = new();
    public List<(uint voice, float refDistance, float maxDistance, float rolloff)> DistanceModels { get; } = new();
    public Dictionary<uint, float> LowPass { get; } = new();
    public Dictionary<uint, float> HighPass { get; } = new();
    public bool Disposed { get; private set; }

    private uint _nextClip = 1;
    private uint _nextVoice = 1;
    private readonly Dictionary<uint, VoiceState> _voices = new();
    private readonly Dictionary<uint, AudioFormat> _formats = new();

    public bool Initialize()
    {
        CallLog.Add("Initialize");
        IsInitialized = InitializeResult;
        return InitializeResult;
    }

    public AudioClipHandle LoadClip(string path)
    {
        LoadedPaths.Add(path);
        if (path.Contains("missing", StringComparison.OrdinalIgnoreCase))
            return default;
        return new AudioClipHandle(_nextClip++);
    }

    public AudioClipHandle LoadClipFromMemory(byte[] data, AudioFormat format)
    {
        var handle = new AudioClipHandle(_nextClip++);
        _formats[handle.Id] = format;
        return handle;
    }

    public void UnloadClip(AudioClipHandle clip)
    {
        CallLog.Add("Unload");
        UnloadedClipIds.Add(clip.Id);
    }

    public AudioFormat GetClipFormat(AudioClipHandle clip) =>
        _formats.TryGetValue(clip.Id, out var format)
            ? format
            : new AudioFormat { SampleRate = 44100, Channels = 1, BitsPerSample = 16, TotalSamples = 44100 };

    public VoiceHandle Play(AudioClipHandle clip, bool loop = false)
    {
        CallLog.Add("Play");
        PlayedClips.Add(clip.Id);
        PlayedLoops.Add(loop);
        var id = _nextVoice++;
        _voices[id] = VoiceState.Playing;
        var handle = new VoiceHandle(id);
        PlayedVoices.Add(handle);
        return handle;
    }

    public void Stop(VoiceHandle voice)
    {
        CallLog.Add("Stop");
        StoppedVoices.Add(voice.Id);
        _voices[voice.Id] = VoiceState.Stopped;
    }

    public void Pause(VoiceHandle voice) => _voices[voice.Id] = VoiceState.Paused;
    public void Resume(VoiceHandle voice) => _voices[voice.Id] = VoiceState.Playing;
    public VoiceState GetState(VoiceHandle voice) => _voices.GetValueOrDefault(voice.Id);
    public bool IsPlaying(VoiceHandle voice) => GetState(voice) == VoiceState.Playing;

    public void SetVoiceState(VoiceHandle voice, VoiceState state) => _voices[voice.Id] = state;

    public void SetVolume(VoiceHandle voice, float volume) => Volumes[voice.Id] = volume;
    public void SetPitch(VoiceHandle voice, float pitch) => Pitches[voice.Id] = pitch;
    public void SetPosition(VoiceHandle voice, Vector3 position) => Positions[voice.Id] = position;
    public void SetVelocity(VoiceHandle voice, Vector3 velocity) => Velocities[voice.Id] = velocity;
    public void SetDirection(VoiceHandle voice, Vector3 direction) { }

    public void SetDistanceModel(VoiceHandle voice, float refDistance, float maxDistance, float rolloff) =>
        DistanceModels.Add((voice.Id, refDistance, maxDistance, rolloff));

    public void SetPan(VoiceHandle voice, float pan) { }
    public void SetLowPassFilter(VoiceHandle voice, float cutoffHz) => LowPass[voice.Id] = cutoffHz;
    public void SetHighPassFilter(VoiceHandle voice, float cutoffHz) => HighPass[voice.Id] = cutoffHz;
    public void SetListenerPosition(Vector3 position) => ListenerPosition = position;
    public void SetListenerVelocity(Vector3 velocity) => ListenerVelocity = velocity;

    public void SetListenerOrientation(Vector3 forward, Vector3 up)
    {
        ListenerForward = forward;
        ListenerUp = up;
    }

    public void SetMasterVolume(float volume) => MasterVolume = volume;
    public void SetSpeedOfSound(float speed) => SpeedOfSound = speed;
    public void SetDopplerFactor(float factor) => DopplerFactor = factor;
    public void Update() => UpdateCount++;

    public void Dispose()
    {
        CallLog.Add("Dispose");
        Disposed = true;
        IsInitialized = false;
    }
}
