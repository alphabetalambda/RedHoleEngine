using System.Numerics;
using System.Runtime.InteropServices;
using Silk.NET.OpenAL;

namespace RedHoleEngine.Audio;

/// <summary>
/// OpenAL-based audio backend using Silk.NET
/// </summary>
public unsafe class OpenALBackend : IAudioBackend
{
    private AL? _al;
    private ALContext? _alc;
    private Device* _device;
    private Context* _context;
    
    private bool _initialized;
    
    // Clip management
    private readonly Dictionary<uint, AudioFormat> _clipFormats = new();
    private uint _nextClipId = 1;
    
    // Voice management  
    private readonly Dictionary<uint, uint> _voiceToSource = new(); // VoiceHandle -> OpenAL source
    private readonly Dictionary<uint, uint> _voiceToBuffer = new(); // VoiceHandle -> buffer being played
    private readonly Queue<uint> _availableSources = new();
    private readonly List<uint> _allSources = new();
    private uint _nextVoiceId = 1;
    
    private const int MaxSources = 64;

    public bool IsInitialized => _initialized;
    public string Name => "OpenAL Soft";

    public bool Initialize()
    {
        try
        {
            _alc = ALContext.GetApi();
            _al = AL.GetApi();

            // Open default device
            _device = _alc.OpenDevice(null);
            if (_device == null)
            {
                Console.WriteLine("OpenAL: Failed to open audio device");
                return false;
            }

            // Create context
            _context = _alc.CreateContext(_device, null);
            if (_context == null)
            {
                Console.WriteLine("OpenAL: Failed to create context");
                _alc.CloseDevice(_device);
                return false;
            }

            _alc.MakeContextCurrent(_context);

            // Pre-allocate sources
            for (int i = 0; i < MaxSources; i++)
            {
                uint source = _al.GenSource();
                if (_al.GetError() == AudioError.NoError)
                {
                    _allSources.Add(source);
                    _availableSources.Enqueue(source);
                }
            }

            Console.WriteLine($"OpenAL: Initialized with {_allSources.Count} voices");
            Console.WriteLine($"OpenAL: Vendor: {_al.GetStateProperty(StateString.Vendor)}");
            Console.WriteLine($"OpenAL: Renderer: {_al.GetStateProperty(StateString.Renderer)}");

            // Set default listener
            _al.SetListenerProperty(ListenerVector3.Position, 0, 0, 0);
            _al.SetListenerProperty(ListenerVector3.Velocity, 0, 0, 0);
            
            // Forward = -Z, Up = +Y (OpenGL convention)
            float* orientation = stackalloc float[6] { 0, 0, -1, 0, 1, 0 };
            _al.SetListenerProperty(ListenerFloatArray.Orientation, orientation);

            _initialized = true;
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"OpenAL: Initialization failed: {ex.Message}");
            return false;
        }
    }

    #region Clips

    public AudioClipHandle LoadClip(string path)
    {
        if (!_initialized || _al == null) return default;

        try
        {
            // Load WAV file
            var (data, format) = LoadWavFile(path);
            if (data == null) return default;

            return LoadClipFromMemory(data, format);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"OpenAL: Failed to load clip '{path}': {ex.Message}");
            return default;
        }
    }

    public AudioClipHandle LoadClipFromMemory(byte[] data, AudioFormat format)
    {
        if (!_initialized || _al == null) return default;

        try
        {
            uint buffer = _al.GenBuffer();
            
            var alFormat = GetALFormat(format.Channels, format.BitsPerSample);
            
            fixed (byte* pData = data)
            {
                _al.BufferData(buffer, alFormat, pData, data.Length, format.SampleRate);
            }

            if (_al.GetError() != AudioError.NoError)
            {
                _al.DeleteBuffer(buffer);
                return default;
            }

            uint clipId = _nextClipId++;
            _clipFormats[buffer] = format;
            
            return new AudioClipHandle(buffer);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"OpenAL: Failed to load clip from memory: {ex.Message}");
            return default;
        }
    }

    public void UnloadClip(AudioClipHandle clip)
    {
        if (!_initialized || _al == null || !clip.IsValid) return;

        // Stop any voices using this buffer
        var voicesToStop = _voiceToBuffer
            .Where(kvp => kvp.Value == clip.Id)
            .Select(kvp => new VoiceHandle(kvp.Key))
            .ToList();
        
        foreach (var voice in voicesToStop)
        {
            Stop(voice);
        }

        _al.DeleteBuffer(clip.Id);
        _clipFormats.Remove(clip.Id);
    }

    public AudioFormat GetClipFormat(AudioClipHandle clip)
    {
        return _clipFormats.GetValueOrDefault(clip.Id);
    }

    private static BufferFormat GetALFormat(int channels, int bitsPerSample)
    {
        return (channels, bitsPerSample) switch
        {
            (1, 8) => BufferFormat.Mono8,
            (1, 16) => BufferFormat.Mono16,
            (2, 8) => BufferFormat.Stereo8,
            (2, 16) => BufferFormat.Stereo16,
            _ => BufferFormat.Mono16
        };
    }

    #endregion

    #region Voices

    public VoiceHandle Play(AudioClipHandle clip, bool loop = false)
    {
        if (!_initialized || _al == null || !clip.IsValid) return default;

        if (_availableSources.Count == 0)
        {
            // Try to reclaim finished sources
            ReclaimFinishedSources();
            
            if (_availableSources.Count == 0)
            {
                Console.WriteLine("OpenAL: No available voices");
                return default;
            }
        }

        uint source = _availableSources.Dequeue();
        uint voiceId = _nextVoiceId++;

        _al.SetSourceProperty(source, SourceInteger.Buffer, (int)clip.Id);
        _al.SetSourceProperty(source, SourceBoolean.Looping, loop);
        _al.SourcePlay(source);

        _voiceToSource[voiceId] = source;
        _voiceToBuffer[voiceId] = clip.Id;

        return new VoiceHandle(voiceId);
    }

    public void Stop(VoiceHandle voice)
    {
        if (!_initialized || _al == null || !voice.IsValid) return;
        if (!_voiceToSource.TryGetValue(voice.Id, out uint source)) return;

        _al.SourceStop(source);
        _al.SetSourceProperty(source, SourceInteger.Buffer, 0);
        
        _voiceToSource.Remove(voice.Id);
        _voiceToBuffer.Remove(voice.Id);
        _availableSources.Enqueue(source);
    }

    public void Pause(VoiceHandle voice)
    {
        if (!_initialized || _al == null || !voice.IsValid) return;
        if (!_voiceToSource.TryGetValue(voice.Id, out uint source)) return;

        _al.SourcePause(source);
    }

    public void Resume(VoiceHandle voice)
    {
        if (!_initialized || _al == null || !voice.IsValid) return;
        if (!_voiceToSource.TryGetValue(voice.Id, out uint source)) return;

        _al.SourcePlay(source);
    }

    public VoiceState GetState(VoiceHandle voice)
    {
        if (!_initialized || _al == null || !voice.IsValid) return VoiceState.Stopped;
        if (!_voiceToSource.TryGetValue(voice.Id, out uint source)) return VoiceState.Stopped;

        _al.GetSourceProperty(source, GetSourceInteger.SourceState, out int state);
        
        return (SourceState)state switch
        {
            SourceState.Playing => VoiceState.Playing,
            SourceState.Paused => VoiceState.Paused,
            _ => VoiceState.Stopped
        };
    }

    public bool IsPlaying(VoiceHandle voice)
    {
        return GetState(voice) == VoiceState.Playing;
    }

    private void ReclaimFinishedSources()
    {
        var finished = _voiceToSource
            .Where(kvp => 
            {
                _al!.GetSourceProperty(kvp.Value, GetSourceInteger.SourceState, out int state);
                return (SourceState)state == SourceState.Stopped;
            })
            .Select(kvp => new VoiceHandle(kvp.Key))
            .ToList();

        foreach (var voice in finished)
        {
            Stop(voice);
        }
    }

    #endregion

    #region Voice Parameters

    public void SetVolume(VoiceHandle voice, float volume)
    {
        if (!_initialized || _al == null || !voice.IsValid) return;
        if (!_voiceToSource.TryGetValue(voice.Id, out uint source)) return;

        _al.SetSourceProperty(source, SourceFloat.Gain, Math.Clamp(volume, 0f, 1f));
    }

    public void SetPitch(VoiceHandle voice, float pitch)
    {
        if (!_initialized || _al == null || !voice.IsValid) return;
        if (!_voiceToSource.TryGetValue(voice.Id, out uint source)) return;

        // OpenAL pitch must be > 0
        _al.SetSourceProperty(source, SourceFloat.Pitch, Math.Max(0.01f, pitch));
    }

    public void SetPosition(VoiceHandle voice, Vector3 position)
    {
        if (!_initialized || _al == null || !voice.IsValid) return;
        if (!_voiceToSource.TryGetValue(voice.Id, out uint source)) return;

        _al.SetSourceProperty(source, SourceVector3.Position, position.X, position.Y, position.Z);
    }

    public void SetVelocity(VoiceHandle voice, Vector3 velocity)
    {
        if (!_initialized || _al == null || !voice.IsValid) return;
        if (!_voiceToSource.TryGetValue(voice.Id, out uint source)) return;

        _al.SetSourceProperty(source, SourceVector3.Velocity, velocity.X, velocity.Y, velocity.Z);
    }

    public void SetDirection(VoiceHandle voice, Vector3 direction)
    {
        if (!_initialized || _al == null || !voice.IsValid) return;
        if (!_voiceToSource.TryGetValue(voice.Id, out uint source)) return;

        _al.SetSourceProperty(source, SourceVector3.Direction, direction.X, direction.Y, direction.Z);
    }

    public void SetDistanceModel(VoiceHandle voice, float refDistance, float maxDistance, float rolloff)
    {
        if (!_initialized || _al == null || !voice.IsValid) return;
        if (!_voiceToSource.TryGetValue(voice.Id, out uint source)) return;

        _al.SetSourceProperty(source, SourceFloat.ReferenceDistance, refDistance);
        _al.SetSourceProperty(source, SourceFloat.MaxDistance, maxDistance);
        _al.SetSourceProperty(source, SourceFloat.RolloffFactor, rolloff);
    }

    public void SetPan(VoiceHandle voice, float pan)
    {
        if (!_initialized || _al == null || !voice.IsValid) return;
        if (!_voiceToSource.TryGetValue(voice.Id, out uint source)) return;

        // OpenAL doesn't have direct pan - simulate with position
        // Place source on a circle around the listener
        float x = pan;
        float z = -MathF.Sqrt(1f - pan * pan);
        _al.SetSourceProperty(source, SourceVector3.Position, x, 0, z);
        _al.SetSourceProperty(source, SourceBoolean.SourceRelative, true);
    }

    public void SetLowPassFilter(VoiceHandle voice, float cutoffHz)
    {
        // OpenAL EFX extension required for filters
        // For now, this is a no-op - would need OpenAL.Extensions.EXT
        // TODO: Implement with EFX filters
    }

    public void SetHighPassFilter(VoiceHandle voice, float cutoffHz)
    {
        // OpenAL EFX extension required for filters
        // TODO: Implement with EFX filters
    }

    #endregion

    #region Listener

    public void SetListenerPosition(Vector3 position)
    {
        if (!_initialized || _al == null) return;
        _al.SetListenerProperty(ListenerVector3.Position, position.X, position.Y, position.Z);
    }

    public void SetListenerVelocity(Vector3 velocity)
    {
        if (!_initialized || _al == null) return;
        _al.SetListenerProperty(ListenerVector3.Velocity, velocity.X, velocity.Y, velocity.Z);
    }

    public void SetListenerOrientation(Vector3 forward, Vector3 up)
    {
        if (!_initialized || _al == null) return;
        
        float* orientation = stackalloc float[6] 
        { 
            forward.X, forward.Y, forward.Z,
            up.X, up.Y, up.Z 
        };
        _al.SetListenerProperty(ListenerFloatArray.Orientation, orientation);
    }

    public void SetMasterVolume(float volume)
    {
        if (!_initialized || _al == null) return;
        _al.SetListenerProperty(ListenerFloat.Gain, Math.Clamp(volume, 0f, 1f));
    }

    #endregion

    #region Global Settings

    public void SetSpeedOfSound(float speed)
    {
        if (!_initialized || _al == null) return;
        _al.SpeedOfSound(speed);
    }

    public void SetDopplerFactor(float factor)
    {
        if (!_initialized || _al == null) return;
        _al.DopplerFactor(factor);
    }

    #endregion

    public void Update()
    {
        if (!_initialized) return;
        
        // Reclaim finished voices periodically
        ReclaimFinishedSources();
    }

    #region WAV Loading

    private (byte[]? data, AudioFormat format) LoadWavFile(string path)
    {
        if (!File.Exists(path))
        {
            Console.WriteLine($"OpenAL: File not found: {path}");
            return (null, default);
        }

        using var stream = File.OpenRead(path);
        using var reader = new BinaryReader(stream);

        // RIFF header
        var riff = new string(reader.ReadChars(4));
        if (riff != "RIFF")
        {
            Console.WriteLine($"OpenAL: Invalid WAV file (no RIFF header): {path}");
            return (null, default);
        }

        reader.ReadInt32(); // File size
        
        var wave = new string(reader.ReadChars(4));
        if (wave != "WAVE")
        {
            Console.WriteLine($"OpenAL: Invalid WAV file (no WAVE header): {path}");
            return (null, default);
        }

        // Find fmt chunk
        int channels = 0, sampleRate = 0, bitsPerSample = 0;
        byte[]? audioData = null;

        while (stream.Position < stream.Length)
        {
            var chunkId = new string(reader.ReadChars(4));
            int chunkSize = reader.ReadInt32();

            if (chunkId == "fmt ")
            {
                var audioFormat = reader.ReadInt16(); // 1 = PCM
                channels = reader.ReadInt16();
                sampleRate = reader.ReadInt32();
                reader.ReadInt32(); // Byte rate
                reader.ReadInt16(); // Block align
                bitsPerSample = reader.ReadInt16();
                
                // Skip any extra format bytes
                if (chunkSize > 16)
                    reader.ReadBytes(chunkSize - 16);
            }
            else if (chunkId == "data")
            {
                audioData = reader.ReadBytes(chunkSize);
            }
            else
            {
                // Skip unknown chunk
                reader.ReadBytes(chunkSize);
            }
            
            // Align to word boundary
            if (chunkSize % 2 == 1 && stream.Position < stream.Length)
                reader.ReadByte();
        }

        if (audioData == null)
        {
            Console.WriteLine($"OpenAL: No audio data found in: {path}");
            return (null, default);
        }

        var format = new AudioFormat
        {
            Channels = channels,
            SampleRate = sampleRate,
            BitsPerSample = bitsPerSample,
            TotalSamples = audioData.Length / (channels * bitsPerSample / 8)
        };

        return (audioData, format);
    }

    #endregion

    public void Dispose()
    {
        if (!_initialized) return;

        // Stop all sources
        foreach (var source in _allSources)
        {
            _al?.SourceStop(source);
            _al?.DeleteSource(source);
        }

        // Delete all buffers
        foreach (var bufferId in _clipFormats.Keys)
        {
            _al?.DeleteBuffer(bufferId);
        }

        _alc?.DestroyContext(_context);
        _alc?.CloseDevice(_device);

        _al?.Dispose();
        _alc?.Dispose();

        _initialized = false;
        Console.WriteLine("OpenAL: Shutdown complete");
    }
}
