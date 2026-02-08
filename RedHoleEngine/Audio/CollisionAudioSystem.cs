using System.Numerics;
using RedHoleEngine.Core.ECS;
using RedHoleEngine.Physics;
using RedHoleEngine.Physics.Collision;

namespace RedHoleEngine.Audio;

/// <summary>
/// System that automatically plays sounds when physics collisions occur.
/// Listens to PhysicsWorld collision events and plays appropriate impact sounds
/// based on material types, impact velocity, and contact location.
/// </summary>
public class CollisionAudioSystem : GameSystem
{
    private PhysicsWorld? _physicsWorld;
    private AudioEngine? _audioEngine;
    private readonly ImpactSoundLibrary _soundLibrary;
    private readonly Random _random = new();
    
    // Cooldown tracking: (entityA, entityB) -> last impact time
    private readonly Dictionary<(int, int), float> _cooldowns = new();
    
    // Current frame state
    private int _soundsThisFrame;
    private float _currentTime;
    
    /// <summary>
    /// Configuration for collision audio behavior
    /// </summary>
    public CollisionSoundConfig Config { get; set; } = CollisionSoundConfig.Default;
    
    /// <summary>
    /// Sound library for material-based sound selection
    /// </summary>
    public ImpactSoundLibrary SoundLibrary => _soundLibrary;
    
    /// <summary>
    /// Priority runs after physics (50) and before audio update (100)
    /// </summary>
    public override int Priority => 75;
    
    /// <summary>
    /// Event fired when an impact sound is played (for debugging/effects)
    /// </summary>
    public event Action<ImpactData, VoiceHandle>? OnImpactSoundPlayed;

    public CollisionAudioSystem()
    {
        _soundLibrary = new ImpactSoundLibrary();
    }
    
    public CollisionAudioSystem(CollisionSoundConfig config) : this()
    {
        Config = config;
    }

    /// <summary>
    /// Initialize the system with required dependencies.
    /// Call this after adding the system to the world.
    /// </summary>
    public void Initialize(PhysicsWorld physicsWorld, AudioEngine audioEngine)
    {
        _physicsWorld = physicsWorld ?? throw new ArgumentNullException(nameof(physicsWorld));
        _audioEngine = audioEngine ?? throw new ArgumentNullException(nameof(audioEngine));
        
        // Subscribe to collision events
        _physicsWorld.OnCollisionEnter += HandleCollisionEnter;
        _physicsWorld.OnCollisionStay += HandleCollisionStay;
        
        // Update sound library paths from config
        _soundLibrary.BasePath = Config.BasePath;
        _soundLibrary.Extension = Config.Extension;
        
        Console.WriteLine("CollisionAudioSystem: Initialized");
    }

    protected override void OnInitialize()
    {
        // Base initialization - dependencies set via Initialize()
    }

    public override void Update(float deltaTime)
    {
        _currentTime += deltaTime;
        _soundsThisFrame = 0;
        
        // Clean up old cooldowns periodically
        if (_random.NextDouble() < 0.01) // 1% chance per frame
        {
            CleanupCooldowns();
        }
    }

    public override void FixedUpdate(float fixedDeltaTime)
    {
        // Physics events are handled via callbacks
    }

    public override void OnDestroy()
    {
        if (_physicsWorld != null)
        {
            _physicsWorld.OnCollisionEnter -= HandleCollisionEnter;
            _physicsWorld.OnCollisionStay -= HandleCollisionStay;
        }
    }

    private void HandleCollisionEnter(CollisionEvent evt)
    {
        if (_audioEngine == null || !_audioEngine.IsInitialized) return;
        if (_soundsThisFrame >= Config.MaxSoundsPerFrame) return;
        
        var impact = ExtractImpactData(evt);
        
        // Check if impact is strong enough
        if (impact.ImpactVelocity < Config.MinImpactVelocity) return;
        
        // Check cooldown
        if (!CheckAndUpdateCooldown(impact.EntityIdA, impact.EntityIdB)) return;
        
        // Play impact sound
        PlayImpactSound(impact);
    }

    private void HandleCollisionStay(CollisionEvent evt)
    {
        if (!Config.EnableSlidingSounds) return;
        if (_audioEngine == null || !_audioEngine.IsInitialized) return;
        
        var impact = ExtractImpactData(evt);
        
        // Check if sliding fast enough
        if (impact.SlidingVelocity < Config.MinSlidingVelocity) return;
        
        // Sliding sounds handled differently - use looping sources on entities
        // For now, just play occasional scrape sounds
        if (_random.NextDouble() < 0.02) // 2% chance per frame while sliding
        {
            PlaySlideSound(impact);
        }
    }

    private ImpactData ExtractImpactData(CollisionEvent evt)
    {
        // Get contact point
        Vector3 contactPoint = Vector3.Zero;
        Vector3 contactNormal = Vector3.UnitY;
        
        if (evt.Manifold.Contacts.Count > 0)
        {
            var contact = evt.Manifold.Contacts[0];
            contactPoint = (contact.PointOnA + contact.PointOnB) * 0.5f;
            contactNormal = contact.Normal;
        }
        else
        {
            // Fallback to midpoint between bodies
            contactPoint = (evt.BodyA.Position + evt.BodyB.Position) * 0.5f;
        }
        
        // Calculate relative velocity at contact point
        var velA = evt.BodyA.GetVelocityAtPoint(contactPoint);
        var velB = evt.BodyB.GetVelocityAtPoint(contactPoint);
        var relativeVelocity = velB - velA;
        
        // Decompose into normal (impact) and tangential (sliding) components
        float normalSpeed = Vector3.Dot(relativeVelocity, contactNormal);
        var normalVelocity = contactNormal * normalSpeed;
        var tangentVelocity = relativeVelocity - normalVelocity;
        
        // Get surface types from components or use defaults
        var surfaceA = GetSurfaceType(evt.BodyA.EntityId);
        var surfaceB = GetSurfaceType(evt.BodyB.EntityId);
        
        // Calculate combined mass
        float massA = evt.BodyA.InverseMass > 0 ? 1f / evt.BodyA.InverseMass : 1000f;
        float massB = evt.BodyB.InverseMass > 0 ? 1f / evt.BodyB.InverseMass : 1000f;
        
        return new ImpactData
        {
            Position = contactPoint,
            Normal = contactNormal,
            ImpactVelocity = MathF.Abs(normalSpeed),
            SlidingVelocity = tangentVelocity.Length(),
            TotalRelativeVelocity = relativeVelocity.Length(),
            SurfaceA = surfaceA,
            SurfaceB = surfaceB,
            CombinedMass = massA + massB,
            EntityIdA = evt.BodyA.EntityId,
            EntityIdB = evt.BodyB.EntityId
        };
    }

    private SurfaceType GetSurfaceType(int entityId)
    {
        // Try to get CollisionSoundComponent from entity
        if (World.TryGetEntity(entityId, out var entity) && World.HasComponent<CollisionSoundComponent>(entity))
        {
            ref var comp = ref World.GetComponent<CollisionSoundComponent>(entity);
            if (comp.Enabled)
            {
                return comp.SurfaceType;
            }
        }
        
        // Fallback: try to infer from PhysicsMaterial on collider
        return SurfaceType.Default;
    }

    private void PlayImpactSound(ImpactData impact)
    {
        if (_audioEngine == null) return;
        
        // Get sound path
        string soundPath;
        
        // Check for custom sound override
        World.TryGetEntity(impact.EntityIdA, out var entityA);
        World.TryGetEntity(impact.EntityIdB, out var entityB);
        
        string? customPath = null;
        if (World.IsAlive(entityA) && World.HasComponent<CollisionSoundComponent>(entityA))
        {
            ref var comp = ref World.GetComponent<CollisionSoundComponent>(entityA);
            customPath = comp.CustomSoundPath;
        }
        if (customPath == null && World.IsAlive(entityB) && World.HasComponent<CollisionSoundComponent>(entityB))
        {
            ref var comp = ref World.GetComponent<CollisionSoundComponent>(entityB);
            customPath = comp.CustomSoundPath;
        }
        
        soundPath = customPath ?? _soundLibrary.GetImpactSound(impact.SurfaceA, impact.SurfaceB);
        
        // Calculate volume based on impact velocity
        float normalizedVelocity = MathF.Min(impact.ImpactVelocity / Config.MaxVolumeVelocity, 1f);
        float baseVolume = MathF.Sqrt(normalizedVelocity); // Perceptual scaling (sqrt for more natural feel)
        
        // Apply volume multipliers from components
        float volumeMultiplier = 1f;
        if (World.IsAlive(entityA) && World.HasComponent<CollisionSoundComponent>(entityA))
        {
            ref var comp = ref World.GetComponent<CollisionSoundComponent>(entityA);
            volumeMultiplier *= comp.VolumeMultiplier;
        }
        if (World.IsAlive(entityB) && World.HasComponent<CollisionSoundComponent>(entityB))
        {
            ref var comp = ref World.GetComponent<CollisionSoundComponent>(entityB);
            volumeMultiplier *= comp.VolumeMultiplier;
        }
        
        float volume = Math.Clamp(baseVolume * volumeMultiplier, 0f, 1f);
        
        // Calculate pitch with variation
        float basePitch = 1f;
        
        // Heavier impacts sound lower
        float massInfluence = MathF.Log10(impact.CombinedMass / 10f + 1f); // Gentle scaling
        basePitch -= massInfluence * 0.1f; // Reduce pitch slightly for heavy objects
        
        // Apply pitch multipliers from components
        float pitchMultiplier = 1f;
        if (World.IsAlive(entityA) && World.HasComponent<CollisionSoundComponent>(entityA))
        {
            ref var comp = ref World.GetComponent<CollisionSoundComponent>(entityA);
            pitchMultiplier *= comp.PitchMultiplier;
        }
        if (World.IsAlive(entityB) && World.HasComponent<CollisionSoundComponent>(entityB))
        {
            ref var comp = ref World.GetComponent<CollisionSoundComponent>(entityB);
            pitchMultiplier *= comp.PitchMultiplier;
        }
        
        // Add random variation
        float pitchVariation = (float)(_random.NextDouble() * 2 - 1) * Config.PitchVariation;
        
        float pitch = Math.Clamp(basePitch * pitchMultiplier + pitchVariation, 0.5f, 2f);
        
        // Play the sound
        var voice = _audioEngine.PlayOneShot(soundPath, impact.Position, volume, pitch);
        
        _soundsThisFrame++;
        
        // Fire event for external listeners
        OnImpactSoundPlayed?.Invoke(impact, voice);
    }

    private void PlaySlideSound(ImpactData impact)
    {
        if (_audioEngine == null) return;
        
        // Get dominant surface for sliding sound
        var dominantSurface = impact.SurfaceA <= impact.SurfaceB ? impact.SurfaceA : impact.SurfaceB;
        string soundPath = _soundLibrary.GetSlideSound(dominantSurface);
        
        // Volume based on sliding speed
        float normalizedSpeed = MathF.Min(impact.SlidingVelocity / 5f, 1f);
        float volume = normalizedSpeed * 0.5f; // Sliding sounds are quieter
        
        // Pitch based on speed (faster = higher pitch)
        float pitch = 0.8f + normalizedSpeed * 0.4f;
        
        _audioEngine.PlayOneShot(soundPath, impact.Position, volume, pitch);
    }

    private bool CheckAndUpdateCooldown(int entityA, int entityB)
    {
        var key = entityA < entityB ? (entityA, entityB) : (entityB, entityA);
        
        if (_cooldowns.TryGetValue(key, out float lastTime))
        {
            if (_currentTime - lastTime < Config.Cooldown)
            {
                return false;
            }
        }
        
        _cooldowns[key] = _currentTime;
        return true;
    }

    private void CleanupCooldowns()
    {
        var keysToRemove = new List<(int, int)>();
        
        foreach (var kvp in _cooldowns)
        {
            if (_currentTime - kvp.Value > Config.Cooldown * 10)
            {
                keysToRemove.Add(kvp.Key);
            }
        }
        
        foreach (var key in keysToRemove)
        {
            _cooldowns.Remove(key);
        }
    }
    
    /// <summary>
    /// Manually trigger an impact sound at a position (for custom effects)
    /// </summary>
    public VoiceHandle PlayImpactAt(Vector3 position, SurfaceType surfaceA, SurfaceType surfaceB, 
        float impactVelocity, float volume = 1f)
    {
        if (_audioEngine == null) return default;
        
        string soundPath = _soundLibrary.GetImpactSound(surfaceA, surfaceB);
        
        float normalizedVelocity = MathF.Min(impactVelocity / Config.MaxVolumeVelocity, 1f);
        float computedVolume = MathF.Sqrt(normalizedVelocity) * volume;
        
        float pitchVariation = (float)(_random.NextDouble() * 2 - 1) * Config.PitchVariation;
        float pitch = 1f + pitchVariation;
        
        return _audioEngine.PlayOneShot(soundPath, position, computedVolume, pitch);
    }
}

/// <summary>
/// Extension methods for easy collision audio setup
/// </summary>
public static class CollisionAudioExtensions
{
    /// <summary>
    /// Add collision sound capability to an entity
    /// </summary>
    public static void AddCollisionSound(this World world, Entity entity, SurfaceType surface, float volume = 1f)
    {
        world.AddComponent(entity, CollisionSoundComponent.Create(surface, volume));
    }
    
    /// <summary>
    /// Add collision sound with custom settings
    /// </summary>
    public static void AddCollisionSound(this World world, Entity entity, CollisionSoundComponent component)
    {
        world.AddComponent(entity, component);
    }
    
    /// <summary>
    /// Check if entity has collision sound enabled
    /// </summary>
    public static bool HasCollisionSound(this World world, Entity entity)
    {
        return world.HasComponent<CollisionSoundComponent>(entity);
    }
    
    /// <summary>
    /// Set surface type for an entity's collision sounds
    /// </summary>
    public static void SetCollisionSurface(this World world, Entity entity, SurfaceType surface)
    {
        if (world.HasComponent<CollisionSoundComponent>(entity))
        {
            ref var comp = ref world.GetComponent<CollisionSoundComponent>(entity);
            comp.SurfaceType = surface;
        }
    }
}
