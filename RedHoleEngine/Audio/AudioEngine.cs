using System.Numerics;
using RedHoleEngine.Components;
using RedHoleEngine.Core.ECS;
using RedHoleEngine.Rendering.Debug;

namespace RedHoleEngine.Audio;

/// <summary>
/// High-level audio engine that manages the complete audio pipeline:
/// Acoustic Raytracing -> Mixing -> Backend Output
/// </summary>
public class AudioEngine : IDisposable
{
    private readonly World _world;
    private readonly IAudioBackend _backend;
    private readonly AcousticRaytracer _raytracer;
    private readonly AudioMixer _mixer;
    
    // Debug visualization
    private AudioDebugVisualizer? _debugVisualizer;
    private float _currentTime;
    
    // Voice management
    private readonly Dictionary<int, VoiceHandle> _entityVoices = new();
    private readonly Dictionary<int, AudioClipHandle> _loadedClips = new();
    private readonly Dictionary<string, AudioClipHandle> _clipsByPath = new();
    
    // Listener cache
    private Entity _activeListener;
    private Vector3 _listenerPosition;
    private Vector3 _listenerVelocity;
    private Vector3 _listenerForward;
    private Vector3 _listenerUp;
    
    // Timing
    private float _timeSinceRaytrace;
    private readonly float _raytraceInterval;

    /// <summary>
    /// Quality settings
    /// </summary>
    public AcousticQualitySettings QualitySettings { get; }
    
    /// <summary>
    /// The audio backend
    /// </summary>
    public IAudioBackend Backend => _backend;
    
    /// <summary>
    /// Whether the engine is initialized
    /// </summary>
    public bool IsInitialized => _backend.IsInitialized;
    
    /// <summary>
    /// Master volume (0-1)
    /// </summary>
    public float MasterVolume
    {
        get => _mixer.MasterVolume;
        set
        {
            _mixer.MasterVolume = value;
            _backend.SetMasterVolume(value);
        }
    }
    
    /// <summary>
    /// The debug visualizer (null if not enabled)
    /// </summary>
    public AudioDebugVisualizer? DebugVisualizer => _debugVisualizer;

    public AudioEngine(World world, AcousticQualitySettings? settings = null)
    {
        _world = world;
        QualitySettings = settings ?? AcousticQualitySettings.Medium;
        
        _backend = new OpenALBackend();
        _raytracer = new AcousticRaytracer(world, QualitySettings);
        _mixer = new AudioMixer();
        
        _raytraceInterval = 1f / QualitySettings.UpdateRate;
    }

    /// <summary>
    /// Initialize the audio engine
    /// </summary>
    public bool Initialize()
    {
        if (!_backend.Initialize())
        {
            Console.WriteLine("AudioEngine: Failed to initialize backend");
            return false;
        }

        // Configure backend
        _backend.SetSpeedOfSound(QualitySettings.SpeedOfSound);
        _backend.SetDopplerFactor(1f);
        
        Console.WriteLine($"AudioEngine: Initialized ({QualitySettings.RaysPerSource} rays/source)");
        return true;
    }

    /// <summary>
    /// Enable debug visualization
    /// </summary>
    public void EnableDebugVisualization(DebugDrawManager debugDraw, AudioDebugFlags flags = AudioDebugFlags.All)
    {
        _debugVisualizer = new AudioDebugVisualizer(_world, debugDraw)
        {
            Flags = flags,
            Enabled = true
        };
        Console.WriteLine("AudioEngine: Debug visualization enabled");
    }

    /// <summary>
    /// Disable debug visualization
    /// </summary>
    public void DisableDebugVisualization()
    {
        _debugVisualizer = null;
    }

    /// <summary>
    /// Load an audio clip
    /// </summary>
    public AudioClipHandle LoadClip(string path)
    {
        if (_clipsByPath.TryGetValue(path, out var existing))
            return existing;

        var handle = _backend.LoadClip(path);
        if (handle.IsValid)
        {
            _clipsByPath[path] = handle;
        }
        return handle;
    }

    /// <summary>
    /// Unload an audio clip
    /// </summary>
    public void UnloadClip(string path)
    {
        if (_clipsByPath.TryGetValue(path, out var handle))
        {
            _backend.UnloadClip(handle);
            _clipsByPath.Remove(path);
        }
    }

    /// <summary>
    /// Update the audio engine (call each frame)
    /// </summary>
    public void Update(float deltaTime)
    {
        if (!IsInitialized) return;

        _currentTime += deltaTime;
        _timeSinceRaytrace += deltaTime;
        
        // Update scene cache for raytracer
        _raytracer.UpdateSceneCache();
        
        // Update listener
        UpdateListener();
        
        if (_activeListener.IsNull) return;

        // Process audio sources
        bool shouldRaytrace = _timeSinceRaytrace >= _raytraceInterval;
        
        foreach (var entity in _world.Query<AudioSourceComponent, TransformComponent>())
        {
            ProcessAudioSource(entity, shouldRaytrace, deltaTime);
        }
        
        if (shouldRaytrace)
            _timeSinceRaytrace = 0f;

        // Update debug visualization
        _debugVisualizer?.Update(deltaTime, _currentTime);

        // Update backend
        _backend.Update();
    }

    private void UpdateListener()
    {
        _activeListener = Entity.Null;
        
        foreach (var entity in _world.Query<AudioListenerComponent, TransformComponent>())
        {
            ref var listener = ref _world.GetComponent<AudioListenerComponent>(entity);
            if (!listener.IsActive) continue;
            
            ref var transform = ref _world.GetComponent<TransformComponent>(entity);
            
            _activeListener = entity;
            _listenerPosition = transform.Position;
            _listenerVelocity = listener.Velocity;
            _listenerForward = transform.Forward;
            _listenerUp = transform.Up;
            
            // Update backend listener
            _backend.SetListenerPosition(_listenerPosition);
            _backend.SetListenerVelocity(_listenerVelocity);
            _backend.SetListenerOrientation(_listenerForward, _listenerUp);
            
            _mixer.ListenerPosition = _listenerPosition;
            _mixer.ListenerForward = _listenerForward;
            _mixer.ListenerUp = _listenerUp;
            _mixer.MasterVolume = listener.Volume;
            
            break;
        }
    }

    private void ProcessAudioSource(Entity entity, bool doRaytrace, float deltaTime)
    {
        ref var source = ref _world.GetComponent<AudioSourceComponent>(entity);
        ref var transform = ref _world.GetComponent<TransformComponent>(entity);
        
        var sourcePos = transform.Position;
        var sourceVel = source.Velocity;
        
        // Get or load clip
        if (string.IsNullOrEmpty(source.ClipId))
            return;

        if (!_loadedClips.TryGetValue(entity.Id, out var clipHandle))
        {
            clipHandle = LoadClip(source.ClipId);
            if (!clipHandle.IsValid) return;
            _loadedClips[entity.Id] = clipHandle;
        }

        // Handle play state
        bool hasVoice = _entityVoices.TryGetValue(entity.Id, out var voiceHandle);
        
        if (source.IsPlaying)
        {
            if (!hasVoice || !_backend.IsPlaying(voiceHandle))
            {
                // Start playing
                voiceHandle = _backend.Play(clipHandle, source.Loop);
                _entityVoices[entity.Id] = voiceHandle;
                hasVoice = true;
            }
        }
        else
        {
            if (hasVoice && _backend.IsPlaying(voiceHandle))
            {
                _backend.Stop(voiceHandle);
                _entityVoices.Remove(entity.Id);
            }
            return;
        }

        if (!hasVoice) return;

        // Do raytracing for spatial audio
        List<AcousticPath> paths;
        
        if (source.UseRaytracing && doRaytrace)
        {
            paths = _raytracer.TracePaths(
                sourcePos, sourceVel,
                _listenerPosition, _listenerVelocity,
                source.RayCount);
                
            // Add paths to debug visualizer
            if (_debugVisualizer != null)
            {
                foreach (var path in paths)
                {
                    _debugVisualizer.AddPath(entity.Id, sourcePos, _listenerPosition, path, _currentTime);
                }
            }
        }
        else
        {
            // Simple direct path
            paths = CreateDirectPath(sourcePos, sourceVel);
        }

        // Process through mixer
        var processed = _mixer.ProcessPaths(
            entity.Id,
            source.ClipId,
            sourcePos,
            source.Volume,
            source.Pitch,
            paths);

        // Apply to backend voice
        ApplyProcessedAudio(voiceHandle, processed, source);
    }

    private List<AcousticPath> CreateDirectPath(Vector3 sourcePos, Vector3 sourceVel)
    {
        var direction = _listenerPosition - sourcePos;
        float distance = direction.Length();
        if (distance < 0.001f) distance = 0.001f;

        float attenuation = 1f / (1f + distance * distance * 0.01f);
        
        // Simple Doppler
        float doppler = 1f;
        if (sourceVel != Vector3.Zero || _listenerVelocity != Vector3.Zero)
        {
            var dir = Vector3.Normalize(direction);
            float sourceSpeed = Vector3.Dot(sourceVel, dir);
            float listenerSpeed = Vector3.Dot(_listenerVelocity, dir);
            float c = QualitySettings.SpeedOfSound;
            float denom = c - sourceSpeed;
            if (MathF.Abs(denom) < 0.01f) denom = 0.01f * MathF.Sign(denom);
            doppler = (c - listenerSpeed) / denom;
        }

        return new List<AcousticPath>
        {
            new()
            {
                TotalDistance = distance,
                TotalTime = distance / QualitySettings.SpeedOfSound,
                FinalResponse = FrequencyResponse.Uniform(attenuation),
                IsDirect = true,
                Type = PathType.Direct,
                DopplerShift = doppler,
                GravitationalShift = 1f
            }
        };
    }

    private void ApplyProcessedAudio(VoiceHandle voice, ProcessedAudioSource processed, AudioSourceComponent source)
    {
        // Volume (clamped)
        float volume = Math.Clamp(processed.Volume, 0f, 1f);
        _backend.SetVolume(voice, volume);
        
        // Pitch (includes Doppler and gravitational effects)
        float pitch = Math.Clamp(processed.Pitch, 0.1f, 4f);
        _backend.SetPitch(voice, pitch);
        
        // Position for 3D audio
        _backend.SetPosition(voice, processed.Position);
        _backend.SetVelocity(voice, source.Velocity);
        
        // Distance model
        _backend.SetDistanceModel(voice, source.MinDistance, source.MaxDistance, 1f);
        
        // Filters based on occlusion
        if (processed.LowPassCutoff < 15000f)
        {
            _backend.SetLowPassFilter(voice, processed.LowPassCutoff);
        }
        
        if (processed.HighPassCutoff > 30f)
        {
            _backend.SetHighPassFilter(voice, processed.HighPassCutoff);
        }
    }

    /// <summary>
    /// Play a one-shot sound at a position (no entity required)
    /// </summary>
    public VoiceHandle PlayOneShot(string clipPath, Vector3 position, float volume = 1f, float pitch = 1f)
    {
        var clip = LoadClip(clipPath);
        if (!clip.IsValid) return default;

        var voice = _backend.Play(clip, false);
        _backend.SetPosition(voice, position);
        _backend.SetVolume(voice, volume * MasterVolume);
        _backend.SetPitch(voice, pitch);
        
        return voice;
    }

    /// <summary>
    /// Stop all audio
    /// </summary>
    public void StopAll()
    {
        foreach (var voice in _entityVoices.Values)
        {
            _backend.Stop(voice);
        }
        _entityVoices.Clear();
    }

    /// <summary>
    /// Clean up resources for destroyed entities
    /// </summary>
    public void CleanupEntity(Entity entity)
    {
        if (_entityVoices.TryGetValue(entity.Id, out var voice))
        {
            _backend.Stop(voice);
            _entityVoices.Remove(entity.Id);
        }
        
        _loadedClips.Remove(entity.Id);
        _mixer.ClearSource(entity.Id);
    }

    public void Dispose()
    {
        StopAll();
        
        foreach (var clip in _clipsByPath.Values)
        {
            _backend.UnloadClip(clip);
        }
        _clipsByPath.Clear();
        
        _backend.Dispose();
    }
}
