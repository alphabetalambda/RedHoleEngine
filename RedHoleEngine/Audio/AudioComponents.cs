using System.Numerics;
using RedHoleEngine.Core.ECS;

namespace RedHoleEngine.Audio;

/// <summary>
/// Spatial falloff models for audio sources
/// </summary>
public enum AttenuationModel
{
    /// <summary>Physically accurate inverse square law</summary>
    InverseSquare,
    /// <summary>Linear falloff (simpler, less realistic)</summary>
    Linear,
    /// <summary>Logarithmic falloff</summary>
    Logarithmic,
    /// <summary>No distance attenuation</summary>
    None
}

/// <summary>
/// Component for audio sources (sound emitters)
/// </summary>
public struct AudioSourceComponent : IComponent
{
    /// <summary>
    /// Audio clip/resource ID to play
    /// </summary>
    public string ClipId;

    /// <summary>
    /// Base volume (0-1)
    /// </summary>
    public float Volume;

    /// <summary>
    /// Pitch multiplier (1 = normal, 2 = octave up, 0.5 = octave down)
    /// </summary>
    public float Pitch;

    /// <summary>
    /// Whether the source is currently playing
    /// </summary>
    public bool IsPlaying;

    /// <summary>
    /// Whether to loop the audio
    /// </summary>
    public bool Loop;

    /// <summary>
    /// Minimum distance before attenuation begins (meters)
    /// </summary>
    public float MinDistance;

    /// <summary>
    /// Maximum distance where audio is still audible (meters)
    /// </summary>
    public float MaxDistance;

    /// <summary>
    /// Distance attenuation model
    /// </summary>
    public AttenuationModel Attenuation;

    /// <summary>
    /// Whether this source participates in acoustic raytracing
    /// </summary>
    public bool UseRaytracing;

    /// <summary>
    /// Number of rays to cast for this source (quality vs performance)
    /// </summary>
    public int RayCount;

    /// <summary>
    /// Maximum ray bounces for reflections
    /// </summary>
    public int MaxBounces;

    /// <summary>
    /// Velocity for Doppler effect calculation
    /// </summary>
    public Vector3 Velocity;

    /// <summary>
    /// Directivity pattern (0 = omnidirectional, 1 = fully directional)
    /// </summary>
    public float Directivity;

    /// <summary>
    /// Direction the source is "pointing" (for directional sources)
    /// </summary>
    public Vector3 Direction;

    /// <summary>
    /// Priority for voice limiting (higher = more important)
    /// </summary>
    public int Priority;

    public static AudioSourceComponent Default => new()
    {
        ClipId = "",
        Volume = 1f,
        Pitch = 1f,
        IsPlaying = false,
        Loop = false,
        MinDistance = 1f,
        MaxDistance = 100f,
        Attenuation = AttenuationModel.InverseSquare,
        UseRaytracing = true,
        RayCount = 64,
        MaxBounces = 3,
        Velocity = Vector3.Zero,
        Directivity = 0f,
        Direction = Vector3.UnitZ,
        Priority = 0
    };

    public static AudioSourceComponent CreateAmbient(string clipId, float volume = 1f) => new()
    {
        ClipId = clipId,
        Volume = volume,
        Pitch = 1f,
        IsPlaying = true,
        Loop = true,
        MinDistance = 0f,
        MaxDistance = float.MaxValue,
        Attenuation = AttenuationModel.None,
        UseRaytracing = false,
        RayCount = 0,
        MaxBounces = 0,
        Velocity = Vector3.Zero,
        Directivity = 0f,
        Direction = Vector3.UnitZ,
        Priority = 10
    };

    public static AudioSourceComponent Create3D(string clipId, float minDist = 1f, float maxDist = 50f) => new()
    {
        ClipId = clipId,
        Volume = 1f,
        Pitch = 1f,
        IsPlaying = false,
        Loop = false,
        MinDistance = minDist,
        MaxDistance = maxDist,
        Attenuation = AttenuationModel.InverseSquare,
        UseRaytracing = true,
        RayCount = 64,
        MaxBounces = 3,
        Velocity = Vector3.Zero,
        Directivity = 0f,
        Direction = Vector3.UnitZ,
        Priority = 0
    };
}

/// <summary>
/// Component for the audio listener (usually attached to camera/player)
/// </summary>
public struct AudioListenerComponent : IComponent
{
    /// <summary>
    /// Master volume multiplier
    /// </summary>
    public float Volume;

    /// <summary>
    /// Velocity for Doppler calculations
    /// </summary>
    public Vector3 Velocity;

    /// <summary>
    /// Whether this listener is active (only one should be active)
    /// </summary>
    public bool IsActive;

    /// <summary>
    /// Number of rays to use for listener-side raytracing
    /// </summary>
    public int RayCount;

    public static AudioListenerComponent Default => new()
    {
        Volume = 1f,
        Velocity = Vector3.Zero,
        IsActive = true,
        RayCount = 128
    };
}

/// <summary>
/// Component for acoustic geometry (surfaces that interact with sound)
/// </summary>
public struct AcousticSurfaceComponent : IComponent
{
    /// <summary>
    /// The acoustic material of this surface
    /// </summary>
    public AcousticMaterial Material;

    /// <summary>
    /// Whether this surface affects direct sound paths
    /// </summary>
    public bool AffectsDirectSound;

    /// <summary>
    /// Whether this surface generates reflections
    /// </summary>
    public bool GeneratesReflections;

    /// <summary>
    /// Whether sound can transmit through this surface
    /// </summary>
    public bool AllowsTransmission;

    /// <summary>
    /// Override thickness (if 0, uses material's default)
    /// </summary>
    public float ThicknessOverride;

    public static AcousticSurfaceComponent Create(AcousticMaterial material) => new()
    {
        Material = material,
        AffectsDirectSound = true,
        GeneratesReflections = true,
        AllowsTransmission = material.Transmission.Average > 0.01f,
        ThicknessOverride = 0f
    };
}

/// <summary>
/// Component for reverb zones (areas with baked reverb characteristics)
/// </summary>
public struct ReverbZoneComponent : IComponent
{
    /// <summary>
    /// Reverb time (RT60) in seconds
    /// </summary>
    public float ReverbTime;

    /// <summary>
    /// Pre-delay in milliseconds
    /// </summary>
    public float PreDelay;

    /// <summary>
    /// Early reflection level (dB)
    /// </summary>
    public float EarlyReflectionLevel;

    /// <summary>
    /// Late reverb level (dB)
    /// </summary>
    public float LateReverbLevel;

    /// <summary>
    /// High frequency damping (0-1)
    /// </summary>
    public float HighFrequencyDamping;

    /// <summary>
    /// Low frequency gain (dB)
    /// </summary>
    public float LowFrequencyGain;

    /// <summary>
    /// Diffusion (0-1, affects echo density)
    /// </summary>
    public float Diffusion;

    /// <summary>
    /// Zone radius (for spherical zones)
    /// </summary>
    public float Radius;

    /// <summary>
    /// Blend distance (how far outside the zone reverb fades)
    /// </summary>
    public float BlendDistance;

    /// <summary>
    /// Priority when overlapping with other zones
    /// </summary>
    public int Priority;

    public static ReverbZoneComponent SmallRoom => new()
    {
        ReverbTime = 0.4f,
        PreDelay = 10f,
        EarlyReflectionLevel = -3f,
        LateReverbLevel = -6f,
        HighFrequencyDamping = 0.5f,
        LowFrequencyGain = 0f,
        Diffusion = 0.8f,
        Radius = 10f,
        BlendDistance = 2f,
        Priority = 0
    };

    public static ReverbZoneComponent LargeHall => new()
    {
        ReverbTime = 2.5f,
        PreDelay = 40f,
        EarlyReflectionLevel = -6f,
        LateReverbLevel = -3f,
        HighFrequencyDamping = 0.3f,
        LowFrequencyGain = 2f,
        Diffusion = 0.9f,
        Radius = 50f,
        BlendDistance = 10f,
        Priority = 0
    };

    public static ReverbZoneComponent SpaceStation => new()
    {
        ReverbTime = 1.2f,
        PreDelay = 15f,
        EarlyReflectionLevel = -4f,
        LateReverbLevel = -8f,
        HighFrequencyDamping = 0.6f,
        LowFrequencyGain = -3f,
        Diffusion = 0.7f,
        Radius = 30f,
        BlendDistance = 5f,
        Priority = 0
    };

    public static ReverbZoneComponent Vacuum => new()
    {
        ReverbTime = 0f,
        PreDelay = 0f,
        EarlyReflectionLevel = -100f,
        LateReverbLevel = -100f,
        HighFrequencyDamping = 1f,
        LowFrequencyGain = -100f,
        Diffusion = 0f,
        Radius = float.MaxValue,
        BlendDistance = 0f,
        Priority = -1000 // Lowest priority, overridden by any enclosed space
    };
}
