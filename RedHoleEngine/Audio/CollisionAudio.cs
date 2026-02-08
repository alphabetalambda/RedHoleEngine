using System.Numerics;
using RedHoleEngine.Core.ECS;
using RedHoleEngine.Physics;
using RedHoleEngine.Physics.Collision;

namespace RedHoleEngine.Audio;

/// <summary>
/// Surface types for collision audio selection
/// </summary>
public enum SurfaceType
{
    Default,
    Metal,
    Wood,
    Stone,
    Glass,
    Plastic,
    Rubber,
    Flesh,
    Water,
    Ice,
    Sand,
    Gravel,
    Grass,
    Dirt,
    Concrete,
    Carpet,
    Cloth,
    Paper,
    Ceramic,
    Energy // For sci-fi force fields, lasers, etc.
}

/// <summary>
/// Configuration for how collision sounds are selected and played
/// </summary>
public struct CollisionSoundConfig
{
    /// <summary>
    /// Base path for impact sounds (e.g., "sounds/impacts/")
    /// </summary>
    public string BasePath;
    
    /// <summary>
    /// File extension for sound files (e.g., ".wav", ".ogg")
    /// </summary>
    public string Extension;
    
    /// <summary>
    /// Minimum impact velocity to trigger any sound (m/s)
    /// </summary>
    public float MinImpactVelocity;
    
    /// <summary>
    /// Velocity at which volume reaches maximum (m/s)
    /// </summary>
    public float MaxVolumeVelocity;
    
    /// <summary>
    /// Random pitch variation range (+/- this value)
    /// </summary>
    public float PitchVariation;
    
    /// <summary>
    /// Minimum time between collision sounds for the same entity pair (seconds)
    /// </summary>
    public float Cooldown;
    
    /// <summary>
    /// Maximum number of collision sounds per frame
    /// </summary>
    public int MaxSoundsPerFrame;
    
    /// <summary>
    /// Whether to use sliding/scraping sounds for ongoing contacts
    /// </summary>
    public bool EnableSlidingSounds;
    
    /// <summary>
    /// Minimum sliding velocity to trigger sliding sounds (m/s)
    /// </summary>
    public float MinSlidingVelocity;
    
    public static CollisionSoundConfig Default => new()
    {
        BasePath = "sounds/impacts/",
        Extension = ".wav",
        MinImpactVelocity = 0.5f,
        MaxVolumeVelocity = 10f,
        PitchVariation = 0.15f,
        Cooldown = 0.05f,
        MaxSoundsPerFrame = 8,
        EnableSlidingSounds = true,
        MinSlidingVelocity = 0.3f
    };
    
    public static CollisionSoundConfig Quiet => new()
    {
        BasePath = "sounds/impacts/",
        Extension = ".wav",
        MinImpactVelocity = 1f,
        MaxVolumeVelocity = 15f,
        PitchVariation = 0.1f,
        Cooldown = 0.1f,
        MaxSoundsPerFrame = 4,
        EnableSlidingSounds = false,
        MinSlidingVelocity = 1f
    };
}

/// <summary>
/// Component for entities that should make sounds on collision.
/// Attach this to any entity with a RigidBody to enable collision audio.
/// </summary>
public struct CollisionSoundComponent : IComponent
{
    /// <summary>
    /// Surface type of this entity (affects which sounds are played)
    /// </summary>
    public SurfaceType SurfaceType;
    
    /// <summary>
    /// Volume multiplier for this entity's collision sounds (0-1)
    /// </summary>
    public float VolumeMultiplier;
    
    /// <summary>
    /// Pitch multiplier for this entity (1 = normal)
    /// </summary>
    public float PitchMultiplier;
    
    /// <summary>
    /// Override the minimum impact velocity (0 = use global config)
    /// </summary>
    public float MinImpactVelocityOverride;
    
    /// <summary>
    /// Custom sound path override (null = use material-based selection)
    /// </summary>
    public string? CustomSoundPath;
    
    /// <summary>
    /// Whether to enable collision sounds for this entity
    /// </summary>
    public bool Enabled;
    
    // Runtime state (managed by CollisionAudioSystem)
    internal float LastImpactTime;
    internal int LastCollidedEntityId;
    
    public static CollisionSoundComponent Default => new()
    {
        SurfaceType = SurfaceType.Default,
        VolumeMultiplier = 1f,
        PitchMultiplier = 1f,
        MinImpactVelocityOverride = 0f,
        CustomSoundPath = null,
        Enabled = true,
        LastImpactTime = float.NegativeInfinity,
        LastCollidedEntityId = -1
    };
    
    public static CollisionSoundComponent Create(SurfaceType surface, float volume = 1f) => new()
    {
        SurfaceType = surface,
        VolumeMultiplier = volume,
        PitchMultiplier = 1f,
        MinImpactVelocityOverride = 0f,
        CustomSoundPath = null,
        Enabled = true,
        LastImpactTime = float.NegativeInfinity,
        LastCollidedEntityId = -1
    };
    
    public static CollisionSoundComponent CreateMetal(float volume = 1f) => Create(SurfaceType.Metal, volume);
    public static CollisionSoundComponent CreateWood(float volume = 1f) => Create(SurfaceType.Wood, volume);
    public static CollisionSoundComponent CreateStone(float volume = 1f) => Create(SurfaceType.Stone, volume);
    public static CollisionSoundComponent CreateGlass(float volume = 1f) => Create(SurfaceType.Glass, volume);
}

/// <summary>
/// Data about a collision impact for audio processing
/// </summary>
public readonly struct ImpactData
{
    /// <summary>
    /// World position where the impact occurred
    /// </summary>
    public Vector3 Position { get; init; }
    
    /// <summary>
    /// Impact normal direction
    /// </summary>
    public Vector3 Normal { get; init; }
    
    /// <summary>
    /// Impact velocity (speed along normal)
    /// </summary>
    public float ImpactVelocity { get; init; }
    
    /// <summary>
    /// Tangential (sliding) velocity
    /// </summary>
    public float SlidingVelocity { get; init; }
    
    /// <summary>
    /// Total relative velocity magnitude
    /// </summary>
    public float TotalRelativeVelocity { get; init; }
    
    /// <summary>
    /// Surface type of the first object
    /// </summary>
    public SurfaceType SurfaceA { get; init; }
    
    /// <summary>
    /// Surface type of the second object
    /// </summary>
    public SurfaceType SurfaceB { get; init; }
    
    /// <summary>
    /// Combined mass of both objects (for impact intensity)
    /// </summary>
    public float CombinedMass { get; init; }
    
    /// <summary>
    /// Entity ID of the first body
    /// </summary>
    public int EntityIdA { get; init; }
    
    /// <summary>
    /// Entity ID of the second body
    /// </summary>
    public int EntityIdB { get; init; }
}

/// <summary>
/// Manages collision audio playback based on impact data
/// </summary>
public class ImpactSoundLibrary
{
    private readonly Dictionary<(SurfaceType, SurfaceType), string[]> _impactSounds = new();
    private readonly Dictionary<SurfaceType, string[]> _slideSounds = new();
    private readonly Random _random = new();
    
    public string BasePath { get; set; } = "sounds/impacts/";
    public string Extension { get; set; } = ".wav";
    
    public ImpactSoundLibrary()
    {
        // Register default sound mappings
        RegisterDefaultSounds();
    }
    
    private void RegisterDefaultSounds()
    {
        // Impact sounds are named by convention: {surfaceA}_{surfaceB}_impact_{variant}
        // The library will try to find sounds matching this pattern
        
        // Common material combinations
        RegisterImpact(SurfaceType.Metal, SurfaceType.Metal, "metal_metal_impact");
        RegisterImpact(SurfaceType.Metal, SurfaceType.Stone, "metal_stone_impact");
        RegisterImpact(SurfaceType.Metal, SurfaceType.Wood, "metal_wood_impact");
        RegisterImpact(SurfaceType.Metal, SurfaceType.Concrete, "metal_concrete_impact");
        
        RegisterImpact(SurfaceType.Wood, SurfaceType.Wood, "wood_wood_impact");
        RegisterImpact(SurfaceType.Wood, SurfaceType.Stone, "wood_stone_impact");
        RegisterImpact(SurfaceType.Wood, SurfaceType.Concrete, "wood_concrete_impact");
        
        RegisterImpact(SurfaceType.Stone, SurfaceType.Stone, "stone_stone_impact");
        RegisterImpact(SurfaceType.Stone, SurfaceType.Concrete, "stone_concrete_impact");
        
        RegisterImpact(SurfaceType.Glass, SurfaceType.Default, "glass_impact");
        RegisterImpact(SurfaceType.Rubber, SurfaceType.Default, "rubber_impact");
        RegisterImpact(SurfaceType.Plastic, SurfaceType.Default, "plastic_impact");
        
        // Default fallbacks
        RegisterImpact(SurfaceType.Default, SurfaceType.Default, "default_impact");
        
        // Slide sounds
        RegisterSlide(SurfaceType.Metal, "metal_slide");
        RegisterSlide(SurfaceType.Wood, "wood_slide");
        RegisterSlide(SurfaceType.Stone, "stone_slide");
        RegisterSlide(SurfaceType.Default, "default_slide");
    }
    
    /// <summary>
    /// Register an impact sound for a material combination
    /// </summary>
    public void RegisterImpact(SurfaceType a, SurfaceType b, params string[] soundNames)
    {
        var key = NormalizeKey(a, b);
        _impactSounds[key] = soundNames;
    }
    
    /// <summary>
    /// Register sliding sounds for a material
    /// </summary>
    public void RegisterSlide(SurfaceType surface, params string[] soundNames)
    {
        _slideSounds[surface] = soundNames;
    }
    
    /// <summary>
    /// Get the appropriate impact sound path for a collision
    /// </summary>
    public string GetImpactSound(SurfaceType a, SurfaceType b)
    {
        var key = NormalizeKey(a, b);
        
        // Try exact match
        if (_impactSounds.TryGetValue(key, out var sounds) && sounds.Length > 0)
        {
            var sound = sounds[_random.Next(sounds.Length)];
            return $"{BasePath}{sound}{Extension}";
        }
        
        // Try with Default
        key = NormalizeKey(a, SurfaceType.Default);
        if (_impactSounds.TryGetValue(key, out sounds) && sounds.Length > 0)
        {
            var sound = sounds[_random.Next(sounds.Length)];
            return $"{BasePath}{sound}{Extension}";
        }
        
        key = NormalizeKey(b, SurfaceType.Default);
        if (_impactSounds.TryGetValue(key, out sounds) && sounds.Length > 0)
        {
            var sound = sounds[_random.Next(sounds.Length)];
            return $"{BasePath}{sound}{Extension}";
        }
        
        // Ultimate fallback
        return $"{BasePath}default_impact{Extension}";
    }
    
    /// <summary>
    /// Get the appropriate slide sound path for a surface
    /// </summary>
    public string GetSlideSound(SurfaceType surface)
    {
        if (_slideSounds.TryGetValue(surface, out var sounds) && sounds.Length > 0)
        {
            var sound = sounds[_random.Next(sounds.Length)];
            return $"{BasePath}{sound}{Extension}";
        }
        
        if (_slideSounds.TryGetValue(SurfaceType.Default, out sounds) && sounds.Length > 0)
        {
            var sound = sounds[_random.Next(sounds.Length)];
            return $"{BasePath}{sound}{Extension}";
        }
        
        return $"{BasePath}default_slide{Extension}";
    }
    
    /// <summary>
    /// Normalize key so (A,B) and (B,A) map to the same sounds
    /// </summary>
    private static (SurfaceType, SurfaceType) NormalizeKey(SurfaceType a, SurfaceType b)
    {
        return a <= b ? (a, b) : (b, a);
    }
}
