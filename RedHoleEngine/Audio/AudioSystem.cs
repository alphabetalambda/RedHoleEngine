using System.Numerics;
using RedHoleEngine.Components;
using RedHoleEngine.Core.ECS;

namespace RedHoleEngine.Audio;

/// <summary>
/// Main audio system that integrates acoustic raytracing with the ECS
/// </summary>
public class AudioSystem : GameSystem
{
    private AcousticRaytracer? _raytracer;
    private AudioMixer _mixer = new();
    
    private float _timeSinceLastUpdate;
    private float _updateInterval;
    
    // Cached listener data
    private Entity _activeListener;
    private Vector3 _listenerPosition;
    private Vector3 _listenerVelocity;
    private Vector3 _listenerForward;
    private Vector3 _listenerUp;
    
    // Processed audio ready for output
    private readonly List<ProcessedAudioSource> _outputSources = new();
    
    /// <summary>
    /// Quality settings for acoustic simulation
    /// </summary>
    public AcousticQualitySettings QualitySettings { get; private set; } = AcousticQualitySettings.Medium;
    
    /// <summary>
    /// The audio mixer
    /// </summary>
    public AudioMixer Mixer => _mixer;
    
    /// <summary>
    /// Processed audio sources ready for output
    /// </summary>
    public IReadOnlyList<ProcessedAudioSource> OutputSources => _outputSources;
    
    /// <summary>
    /// Event fired when audio output is updated
    /// </summary>
    public event Action<IReadOnlyList<ProcessedAudioSource>>? OnAudioUpdate;

    public override int Priority => 100; // Run after physics/transforms

    protected override void OnInitialize()
    {
        _raytracer = new AcousticRaytracer(World, QualitySettings);
        _updateInterval = 1f / QualitySettings.UpdateRate;
    }

    /// <summary>
    /// Change quality settings
    /// </summary>
    public void SetQuality(AcousticQualitySettings settings)
    {
        QualitySettings = settings;
        _raytracer = new AcousticRaytracer(World, settings);
        _updateInterval = 1f / settings.UpdateRate;
    }

    public override void Update(float deltaTime)
    {
        _timeSinceLastUpdate += deltaTime;
        
        // Throttle raytracing updates
        if (_timeSinceLastUpdate < _updateInterval)
            return;
            
        _timeSinceLastUpdate = 0f;
        
        // Update scene cache periodically
        _raytracer?.UpdateSceneCache();
        
        // Find active listener
        UpdateListener();
        
        if (_activeListener.IsNull)
            return;
        
        // Update mixer with listener data
        _mixer.ListenerPosition = _listenerPosition;
        _mixer.ListenerForward = _listenerForward;
        _mixer.ListenerUp = _listenerUp;
        
        // Process all audio sources
        _outputSources.Clear();
        var priorities = new Dictionary<int, int>();
        
        foreach (var entity in World.Query<AudioSourceComponent, TransformComponent>())
        {
            ref var source = ref World.GetComponent<AudioSourceComponent>(entity);
            ref var transform = ref World.GetComponent<TransformComponent>(entity);
            
            if (!source.IsPlaying)
                continue;
            
            priorities[entity.Id] = source.Priority;
            
            // Get source position and velocity
            var sourcePos = transform.Position;
            var sourceVel = source.Velocity;
            
            List<AcousticPath> paths;
            
            if (source.UseRaytracing)
            {
                // Full raytraced audio
                paths = _raytracer!.TracePaths(
                    sourcePos, sourceVel,
                    _listenerPosition, _listenerVelocity,
                    source.RayCount);
            }
            else
            {
                // Simple direct path only
                paths = CreateSimplePath(sourcePos, sourceVel);
            }
            
            // Apply source-specific attenuation
            ApplySourceAttenuation(ref source, sourcePos, paths);
            
            // Process into mixable audio
            var processed = _mixer.ProcessPaths(
                entity.Id,
                source.ClipId,
                sourcePos,
                source.Volume,
                source.Pitch,
                paths);
            
            // Apply directivity
            if (source.Directivity > 0)
            {
                ApplyDirectivity(processed, source.Direction, source.Directivity, sourcePos);
            }
            
            _outputSources.Add(processed);
        }
        
        // Voice limiting
        var finalOutput = _mixer.PrioritizeVoices(_outputSources, priorities);
        
        // Notify listeners
        OnAudioUpdate?.Invoke(finalOutput);
    }

    private void UpdateListener()
    {
        _activeListener = Entity.Null;
        
        foreach (var entity in World.Query<AudioListenerComponent, TransformComponent>())
        {
            ref var listener = ref World.GetComponent<AudioListenerComponent>(entity);
            
            if (!listener.IsActive)
                continue;
            
            ref var transform = ref World.GetComponent<TransformComponent>(entity);
            
            _activeListener = entity;
            _listenerPosition = transform.Position;
            _listenerVelocity = listener.Velocity;
            _listenerForward = transform.Forward;
            _listenerUp = transform.Up;
            
            _mixer.MasterVolume = listener.Volume;
            
            break; // Only one active listener
        }
    }

    private List<AcousticPath> CreateSimplePath(Vector3 sourcePos, Vector3 sourceVel)
    {
        var paths = new List<AcousticPath>();
        
        var direction = _listenerPosition - sourcePos;
        float distance = direction.Length();
        
        if (distance < 0.001f)
        {
            distance = 0.001f;
        }

        // Simple inverse square attenuation
        float attenuation = 1f / (1f + distance * distance * 0.01f);
        
        paths.Add(new AcousticPath
        {
            TotalDistance = distance,
            TotalTime = distance / QualitySettings.SpeedOfSound,
            FinalResponse = FrequencyResponse.Uniform(attenuation),
            IsDirect = true,
            Type = PathType.Direct,
            DopplerShift = CalculateSimpleDoppler(sourcePos, sourceVel),
            GravitationalShift = 1f
        });

        return paths;
    }

    private float CalculateSimpleDoppler(Vector3 sourcePos, Vector3 sourceVel)
    {
        if (sourceVel == Vector3.Zero && _listenerVelocity == Vector3.Zero)
            return 1f;
        
        var direction = Vector3.Normalize(_listenerPosition - sourcePos);
        float sourceSpeed = Vector3.Dot(sourceVel, direction);
        float listenerSpeed = Vector3.Dot(_listenerVelocity, direction);
        
        float c = QualitySettings.SpeedOfSound;
        float denom = c - sourceSpeed;
        if (MathF.Abs(denom) < 0.01f) denom = 0.01f * MathF.Sign(denom);
        
        return (c - listenerSpeed) / denom;
    }

    private void ApplySourceAttenuation(ref AudioSourceComponent source, Vector3 sourcePos, List<AcousticPath> paths)
    {
        float distance = Vector3.Distance(sourcePos, _listenerPosition);
        
        float attenuation = source.Attenuation switch
        {
            AttenuationModel.InverseSquare => CalculateInverseSquare(distance, source.MinDistance, source.MaxDistance),
            AttenuationModel.Linear => CalculateLinear(distance, source.MinDistance, source.MaxDistance),
            AttenuationModel.Logarithmic => CalculateLogarithmic(distance, source.MinDistance, source.MaxDistance),
            AttenuationModel.None => 1f,
            _ => 1f
        };

        // Apply to all paths
        foreach (var path in paths)
        {
            path.FinalResponse = path.FinalResponse * attenuation;
        }
    }

    private float CalculateInverseSquare(float distance, float minDist, float maxDist)
    {
        if (distance <= minDist) return 1f;
        if (distance >= maxDist) return 0f;
        
        float normalizedDist = (distance - minDist) / (maxDist - minDist);
        return 1f / (1f + normalizedDist * normalizedDist * 10f);
    }

    private float CalculateLinear(float distance, float minDist, float maxDist)
    {
        if (distance <= minDist) return 1f;
        if (distance >= maxDist) return 0f;
        
        return 1f - (distance - minDist) / (maxDist - minDist);
    }

    private float CalculateLogarithmic(float distance, float minDist, float maxDist)
    {
        if (distance <= minDist) return 1f;
        if (distance >= maxDist) return 0f;
        
        float normalizedDist = (distance - minDist) / (maxDist - minDist);
        return 1f - MathF.Log10(1f + normalizedDist * 9f);
    }

    private void ApplyDirectivity(ProcessedAudioSource processed, Vector3 sourceDirection, float directivity, Vector3 sourcePos)
    {
        var toListener = Vector3.Normalize(_listenerPosition - sourcePos);
        float dot = Vector3.Dot(sourceDirection, toListener);
        
        // Cardioid-like pattern
        float directionFactor = (1f - directivity) + directivity * MathF.Max(0f, (dot + 1f) / 2f);
        
        processed.Volume *= directionFactor;
    }

    public override void OnDestroy()
    {
        _mixer.Clear();
    }
}

/// <summary>
/// Extension methods for easy audio setup
/// </summary>
public static class AudioExtensions
{
    /// <summary>
    /// Add an audio listener to an entity (typically the camera/player)
    /// </summary>
    public static void AddAudioListener(this World world, Entity entity, float volume = 1f)
    {
        world.AddComponent(entity, new AudioListenerComponent
        {
            Volume = volume,
            IsActive = true,
            Velocity = Vector3.Zero,
            RayCount = 128
        });
    }

    /// <summary>
    /// Add an audio source to an entity
    /// </summary>
    public static void AddAudioSource(this World world, Entity entity, string clipId, bool autoPlay = false)
    {
        var source = AudioSourceComponent.Create3D(clipId);
        source.IsPlaying = autoPlay;
        world.AddComponent(entity, source);
    }

    /// <summary>
    /// Add an ambient audio source (non-spatial)
    /// </summary>
    public static void AddAmbientAudio(this World world, Entity entity, string clipId, float volume = 1f)
    {
        world.AddComponent(entity, AudioSourceComponent.CreateAmbient(clipId, volume));
    }

    /// <summary>
    /// Add acoustic properties to a surface
    /// </summary>
    public static void AddAcousticSurface(this World world, Entity entity, AcousticMaterial material)
    {
        world.AddComponent(entity, AcousticSurfaceComponent.Create(material));
    }

    /// <summary>
    /// Add a reverb zone
    /// </summary>
    public static void AddReverbZone(this World world, Entity entity, ReverbZoneComponent reverb)
    {
        world.AddComponent(entity, reverb);
    }
}
