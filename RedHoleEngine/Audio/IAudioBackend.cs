using System.Numerics;

namespace RedHoleEngine.Audio;

/// <summary>
/// Audio clip format information
/// </summary>
public struct AudioFormat
{
    public int SampleRate;
    public int Channels;
    public int BitsPerSample;
    public int TotalSamples;
    
    public float Duration => TotalSamples / (float)SampleRate;
}

/// <summary>
/// Handle to a loaded audio clip
/// </summary>
public readonly struct AudioClipHandle : IEquatable<AudioClipHandle>
{
    public readonly uint Id;
    public readonly bool IsValid => Id != 0;
    
    public AudioClipHandle(uint id) => Id = id;
    
    public bool Equals(AudioClipHandle other) => Id == other.Id;
    public override bool Equals(object? obj) => obj is AudioClipHandle other && Equals(other);
    public override int GetHashCode() => (int)Id;
    
    public static bool operator ==(AudioClipHandle left, AudioClipHandle right) => left.Equals(right);
    public static bool operator !=(AudioClipHandle left, AudioClipHandle right) => !left.Equals(right);
}

/// <summary>
/// Handle to an active audio voice/channel
/// </summary>
public readonly struct VoiceHandle : IEquatable<VoiceHandle>
{
    public readonly uint Id;
    public readonly bool IsValid => Id != 0;
    
    public VoiceHandle(uint id) => Id = id;
    
    public bool Equals(VoiceHandle other) => Id == other.Id;
    public override bool Equals(object? obj) => obj is VoiceHandle other && Equals(other);
    public override int GetHashCode() => (int)Id;
    
    public static bool operator ==(VoiceHandle left, VoiceHandle right) => left.Equals(right);
    public static bool operator !=(VoiceHandle left, VoiceHandle right) => !left.Equals(right);
}

/// <summary>
/// Voice state
/// </summary>
public enum VoiceState
{
    Stopped,
    Playing,
    Paused
}

/// <summary>
/// Interface for audio playback backends
/// </summary>
public interface IAudioBackend : IDisposable
{
    /// <summary>
    /// Initialize the audio backend
    /// </summary>
    bool Initialize();
    
    /// <summary>
    /// Whether the backend is initialized and ready
    /// </summary>
    bool IsInitialized { get; }
    
    /// <summary>
    /// Backend name for debugging
    /// </summary>
    string Name { get; }

    #region Clips

    /// <summary>
    /// Load an audio clip from file
    /// </summary>
    AudioClipHandle LoadClip(string path);
    
    /// <summary>
    /// Load an audio clip from raw PCM data
    /// </summary>
    AudioClipHandle LoadClipFromMemory(byte[] data, AudioFormat format);
    
    /// <summary>
    /// Unload an audio clip
    /// </summary>
    void UnloadClip(AudioClipHandle clip);
    
    /// <summary>
    /// Get clip format information
    /// </summary>
    AudioFormat GetClipFormat(AudioClipHandle clip);

    #endregion

    #region Voices

    /// <summary>
    /// Play a clip, returning a voice handle
    /// </summary>
    VoiceHandle Play(AudioClipHandle clip, bool loop = false);
    
    /// <summary>
    /// Stop a voice
    /// </summary>
    void Stop(VoiceHandle voice);
    
    /// <summary>
    /// Pause a voice
    /// </summary>
    void Pause(VoiceHandle voice);
    
    /// <summary>
    /// Resume a paused voice
    /// </summary>
    void Resume(VoiceHandle voice);
    
    /// <summary>
    /// Get voice state
    /// </summary>
    VoiceState GetState(VoiceHandle voice);
    
    /// <summary>
    /// Check if voice is still playing
    /// </summary>
    bool IsPlaying(VoiceHandle voice);

    #endregion

    #region Voice Parameters

    /// <summary>
    /// Set voice volume (0-1)
    /// </summary>
    void SetVolume(VoiceHandle voice, float volume);
    
    /// <summary>
    /// Set voice pitch multiplier
    /// </summary>
    void SetPitch(VoiceHandle voice, float pitch);
    
    /// <summary>
    /// Set voice 3D position
    /// </summary>
    void SetPosition(VoiceHandle voice, Vector3 position);
    
    /// <summary>
    /// Set voice velocity (for Doppler)
    /// </summary>
    void SetVelocity(VoiceHandle voice, Vector3 velocity);
    
    /// <summary>
    /// Set voice direction (for directional sources)
    /// </summary>
    void SetDirection(VoiceHandle voice, Vector3 direction);
    
    /// <summary>
    /// Set distance attenuation parameters
    /// </summary>
    void SetDistanceModel(VoiceHandle voice, float refDistance, float maxDistance, float rolloff);
    
    /// <summary>
    /// Set stereo pan (-1 to 1)
    /// </summary>
    void SetPan(VoiceHandle voice, float pan);
    
    /// <summary>
    /// Set low-pass filter cutoff frequency
    /// </summary>
    void SetLowPassFilter(VoiceHandle voice, float cutoffHz);
    
    /// <summary>
    /// Set high-pass filter cutoff frequency
    /// </summary>
    void SetHighPassFilter(VoiceHandle voice, float cutoffHz);

    #endregion

    #region Listener

    /// <summary>
    /// Set listener position
    /// </summary>
    void SetListenerPosition(Vector3 position);
    
    /// <summary>
    /// Set listener velocity
    /// </summary>
    void SetListenerVelocity(Vector3 velocity);
    
    /// <summary>
    /// Set listener orientation
    /// </summary>
    void SetListenerOrientation(Vector3 forward, Vector3 up);
    
    /// <summary>
    /// Set master volume
    /// </summary>
    void SetMasterVolume(float volume);

    #endregion

    #region Global Settings

    /// <summary>
    /// Set speed of sound (affects Doppler)
    /// </summary>
    void SetSpeedOfSound(float speed);
    
    /// <summary>
    /// Set Doppler factor (0 = disabled, 1 = normal)
    /// </summary>
    void SetDopplerFactor(float factor);

    #endregion

    /// <summary>
    /// Update the audio system (call each frame)
    /// </summary>
    void Update();
}
