using System.Numerics;
using RedHoleEngine.Components;
using RedHoleEngine.Core.ECS;

namespace RedHoleEngine.Audio;

/// <summary>
/// Result of a single acoustic ray trace
/// </summary>
public struct AcousticRayHit
{
    /// <summary>Hit position in world space</summary>
    public Vector3 Position;
    
    /// <summary>Surface normal at hit point</summary>
    public Vector3 Normal;
    
    /// <summary>Distance traveled to reach this point</summary>
    public float Distance;
    
    /// <summary>Accumulated travel time in seconds</summary>
    public float TravelTime;
    
    /// <summary>Entity that was hit</summary>
    public Entity HitEntity;
    
    /// <summary>Material at hit point</summary>
    public AcousticMaterial? Material;
    
    /// <summary>Accumulated frequency response (absorption/transmission)</summary>
    public FrequencyResponse AccumulatedResponse;
    
    /// <summary>Number of bounces so far</summary>
    public int BounceCount;
    
    /// <summary>Whether this ray reached the listener</summary>
    public bool ReachedListener;
    
    /// <summary>Gravitational time dilation factor (1 = normal time)</summary>
    public float TimeDilationFactor;
    
    /// <summary>Gravitational redshift factor (1 = no shift)</summary>
    public float GravitationalRedshift;
}

/// <summary>
/// Complete propagation path from source to listener
/// </summary>
public class AcousticPath
{
    /// <summary>All hits along this path</summary>
    public List<AcousticRayHit> Hits { get; } = new();
    
    /// <summary>Total distance traveled</summary>
    public float TotalDistance { get; set; }
    
    /// <summary>Total travel time including relativistic effects</summary>
    public float TotalTime { get; set; }
    
    /// <summary>Final frequency response after all interactions</summary>
    public FrequencyResponse FinalResponse { get; set; }
    
    /// <summary>Whether this is a direct (unobstructed) path</summary>
    public bool IsDirect { get; set; }
    
    /// <summary>Path type for mixing</summary>
    public PathType Type { get; set; }
    
    /// <summary>Doppler shift factor (frequency multiplier)</summary>
    public float DopplerShift { get; set; } = 1f;
    
    /// <summary>Accumulated gravitational redshift</summary>
    public float GravitationalShift { get; set; } = 1f;
    
    /// <summary>Final pitch multiplier (Doppler * gravitational)</summary>
    public float TotalPitchShift => DopplerShift * GravitationalShift;
}

public enum PathType
{
    Direct,
    EarlyReflection,
    LateReflection,
    Transmission,
    Diffraction
}

/// <summary>
/// Quality settings for acoustic raytracing
/// </summary>
public class AcousticQualitySettings
{
    /// <summary>Rays per source</summary>
    public int RaysPerSource { get; set; } = 64;
    
    /// <summary>Maximum bounces per ray</summary>
    public int MaxBounces { get; set; } = 4;
    
    /// <summary>Maximum ray distance</summary>
    public float MaxDistance { get; set; } = 200f;
    
    /// <summary>Minimum energy threshold to continue tracing (0-1)</summary>
    public float EnergyThreshold { get; set; } = 0.001f;
    
    /// <summary>Whether to calculate diffraction around edges</summary>
    public bool EnableDiffraction { get; set; } = false;
    
    /// <summary>Whether to calculate transmission through surfaces</summary>
    public bool EnableTransmission { get; set; } = true;
    
    /// <summary>Whether to apply relativistic effects</summary>
    public bool EnableRelativisticEffects { get; set; } = true;
    
    /// <summary>Speed of sound in m/s</summary>
    public float SpeedOfSound { get; set; } = 343f;
    
    /// <summary>Update rate for raytracing (Hz)</summary>
    public float UpdateRate { get; set; } = 30f;

    public static AcousticQualitySettings Low => new()
    {
        RaysPerSource = 16,
        MaxBounces = 2,
        MaxDistance = 100f,
        EnergyThreshold = 0.01f,
        EnableDiffraction = false,
        EnableTransmission = false,
        EnableRelativisticEffects = true,
        UpdateRate = 15f
    };

    public static AcousticQualitySettings Medium => new()
    {
        RaysPerSource = 64,
        MaxBounces = 4,
        MaxDistance = 200f,
        EnergyThreshold = 0.001f,
        EnableDiffraction = false,
        EnableTransmission = true,
        EnableRelativisticEffects = true,
        UpdateRate = 30f
    };

    public static AcousticQualitySettings High => new()
    {
        RaysPerSource = 256,
        MaxBounces = 8,
        MaxDistance = 500f,
        EnergyThreshold = 0.0001f,
        EnableDiffraction = true,
        EnableTransmission = true,
        EnableRelativisticEffects = true,
        UpdateRate = 60f
    };

    public static AcousticQualitySettings Cinematic => new()
    {
        RaysPerSource = 1024,
        MaxBounces = 16,
        MaxDistance = 1000f,
        EnergyThreshold = 0.00001f,
        EnableDiffraction = true,
        EnableTransmission = true,
        EnableRelativisticEffects = true,
        UpdateRate = 60f
    };
}

/// <summary>
/// CPU-based acoustic raytracer for audio propagation simulation
/// </summary>
public class AcousticRaytracer
{
    private readonly World _world;
    private readonly AcousticQualitySettings _settings;
    private readonly Random _random = new();
    
    // Cached data for performance
    private readonly List<(Entity entity, Vector3 position, AcousticSurfaceComponent surface, TransformComponent transform)> _surfaces = new();
    private readonly List<(Entity entity, Vector3 position, GravitySourceComponent gravity)> _gravitySources = new();
    
    // Speed of light for relativistic calculations (in same units as world)
    private const float SpeedOfLight = 299792458f; // m/s, but we scale for game units
    private const float GameSpeedOfLight = 100f; // Scaled for gameplay (1 unit = 1 meter, but light is "slower")
    
    public AcousticQualitySettings Settings => _settings;

    public AcousticRaytracer(World world, AcousticQualitySettings? settings = null)
    {
        _world = world;
        _settings = settings ?? AcousticQualitySettings.Medium;
    }

    /// <summary>
    /// Update cached scene data (call when scene changes)
    /// </summary>
    public void UpdateSceneCache()
    {
        _surfaces.Clear();
        _gravitySources.Clear();

        // Cache acoustic surfaces
        foreach (var entity in _world.Query<AcousticSurfaceComponent, TransformComponent>())
        {
            ref var surface = ref _world.GetComponent<AcousticSurfaceComponent>(entity);
            ref var transform = ref _world.GetComponent<TransformComponent>(entity);
            _surfaces.Add((entity, transform.Position, surface, transform));
        }

        // Cache gravity sources for relativistic effects
        foreach (var entity in _world.Query<GravitySourceComponent, TransformComponent>())
        {
            ref var gravity = ref _world.GetComponent<GravitySourceComponent>(entity);
            ref var transform = ref _world.GetComponent<TransformComponent>(entity);
            _gravitySources.Add((entity, transform.Position, gravity));
        }
    }

    /// <summary>
    /// Trace acoustic paths from a source to the listener
    /// </summary>
    public List<AcousticPath> TracePaths(
        Vector3 sourcePosition,
        Vector3 sourceVelocity,
        Vector3 listenerPosition,
        Vector3 listenerVelocity,
        int rayCount = -1)
    {
        if (rayCount < 0) rayCount = _settings.RaysPerSource;
        
        var paths = new List<AcousticPath>();

        // 1. Direct path (always try this first)
        var directPath = TraceDirectPath(sourcePosition, listenerPosition, sourceVelocity, listenerVelocity);
        if (directPath != null)
            paths.Add(directPath);

        // 2. Reflection paths (stochastic ray casting)
        for (int i = 0; i < rayCount; i++)
        {
            var direction = GenerateRandomDirection();
            var reflectionPath = TraceReflectionPath(sourcePosition, direction, listenerPosition, 
                                                      sourceVelocity, listenerVelocity);
            if (reflectionPath != null)
                paths.Add(reflectionPath);
        }

        return paths;
    }

    /// <summary>
    /// Trace a direct path from source to listener
    /// </summary>
    private AcousticPath? TraceDirectPath(
        Vector3 source, 
        Vector3 listener, 
        Vector3 sourceVel, 
        Vector3 listenerVel)
    {
        var direction = listener - source;
        float distance = direction.Length();
        
        if (distance < 0.001f || distance > _settings.MaxDistance)
            return null;

        direction /= distance; // Normalize

        var path = new AcousticPath
        {
            TotalDistance = distance,
            TotalTime = distance / _settings.SpeedOfSound,
            FinalResponse = FrequencyResponse.Uniform(1f),
            IsDirect = true,
            Type = PathType.Direct
        };

        // Check for occlusion along the direct path
        var (occluded, occlusionResponse, transmittedDistance) = CheckOcclusion(source, listener);
        
        if (occluded && !_settings.EnableTransmission)
        {
            // Fully occluded and transmission disabled
            return null;
        }

        path.FinalResponse = occlusionResponse;
        path.IsDirect = !occluded;
        
        if (occluded)
            path.Type = PathType.Transmission;

        // Apply relativistic effects
        if (_settings.EnableRelativisticEffects)
        {
            ApplyRelativisticEffects(path, source, listener, sourceVel, listenerVel);
        }
        else
        {
            // Just calculate Doppler
            path.DopplerShift = CalculateDopplerShift(source, listener, sourceVel, listenerVel);
        }

        // Apply distance attenuation (inverse square law)
        float attenuation = 1f / (1f + distance * distance * 0.01f);
        path.FinalResponse = path.FinalResponse * attenuation;

        return path;
    }

    /// <summary>
    /// Trace a reflection path
    /// </summary>
    private AcousticPath? TraceReflectionPath(
        Vector3 origin,
        Vector3 direction,
        Vector3 listener,
        Vector3 sourceVel,
        Vector3 listenerVel)
    {
        var path = new AcousticPath
        {
            Type = PathType.EarlyReflection,
            FinalResponse = FrequencyResponse.Uniform(1f)
        };

        Vector3 currentPos = origin;
        Vector3 currentDir = direction;
        float totalDistance = 0f;
        float energy = 1f;

        for (int bounce = 0; bounce < _settings.MaxBounces && energy > _settings.EnergyThreshold; bounce++)
        {
            // Find nearest surface intersection
            var hit = RaycastSurfaces(currentPos, currentDir, _settings.MaxDistance - totalDistance);
            
            if (!hit.HasValue)
                break;

            var rayHit = hit.Value;
            totalDistance += rayHit.Distance;
            
            // Check if we can see the listener from this reflection point
            var toListener = listener - rayHit.Position;
            float listenerDist = toListener.Length();
            
            if (listenerDist < _settings.MaxDistance - totalDistance)
            {
                toListener /= listenerDist;
                
                // Check occlusion to listener
                var (occluded, _, _) = CheckOcclusion(rayHit.Position + rayHit.Normal * 0.01f, listener);
                
                if (!occluded)
                {
                    // This reflection path reaches the listener!
                    path.Hits.Add(rayHit);
                    path.TotalDistance = totalDistance + listenerDist;
                    path.TotalTime = path.TotalDistance / _settings.SpeedOfSound;
                    
                    // Apply material absorption from this hit
                    if (rayHit.Material != null)
                    {
                        var reflection = FrequencyResponse.Uniform(1f);
                        for (int band = 0; band < 8; band++)
                        {
                            var fb = (FrequencyBand)band;
                            reflection[fb] = 1f - rayHit.Material.Absorption[fb];
                        }
                        path.FinalResponse = path.FinalResponse * reflection;
                    }
                    
                    // Distance attenuation
                    float attenuation = 1f / (1f + path.TotalDistance * path.TotalDistance * 0.01f);
                    path.FinalResponse = path.FinalResponse * attenuation;
                    
                    // Reduce level for reflections vs direct
                    path.FinalResponse = path.FinalResponse * 0.7f;
                    
                    // Classify as early or late based on delay
                    if (path.TotalTime > 0.08f) // 80ms threshold
                        path.Type = PathType.LateReflection;

                    // Apply relativistic effects
                    if (_settings.EnableRelativisticEffects)
                    {
                        ApplyRelativisticEffects(path, origin, listener, sourceVel, listenerVel);
                    }
                    else
                    {
                        path.DopplerShift = CalculateDopplerShift(origin, listener, sourceVel, listenerVel);
                    }

                    return path;
                }
            }

            // Continue bouncing
            path.Hits.Add(rayHit);
            
            // Apply absorption
            if (rayHit.Material != null)
            {
                energy *= (1f - rayHit.Material.Absorption.Average);
                
                // Apply scattering to direction
                currentDir = ReflectWithScattering(currentDir, rayHit.Normal, rayHit.Material.Scattering);
            }
            else
            {
                currentDir = Vector3.Reflect(currentDir, rayHit.Normal);
            }

            currentPos = rayHit.Position + rayHit.Normal * 0.01f; // Offset to avoid self-intersection
        }

        return null; // Path didn't reach listener
    }

    /// <summary>
    /// Check occlusion between two points
    /// </summary>
    private (bool occluded, FrequencyResponse response, float transmittedDistance) CheckOcclusion(
        Vector3 from, Vector3 to)
    {
        var direction = to - from;
        float distance = direction.Length();
        if (distance < 0.001f)
            return (false, FrequencyResponse.Uniform(1f), 0f);
        
        direction /= distance;

        var response = FrequencyResponse.Uniform(1f);
        float transmittedDist = 0f;
        bool anyHit = false;

        // Check all surfaces along the path
        float currentDist = 0f;
        Vector3 currentPos = from;

        while (currentDist < distance - 0.01f)
        {
            var hit = RaycastSurfaces(currentPos, direction, distance - currentDist);
            
            if (!hit.HasValue)
                break;

            var rayHit = hit.Value;
            anyHit = true;

            if (rayHit.Material != null)
            {
                if (_settings.EnableTransmission && rayHit.Material.Transmission.Average > 0.001f)
                {
                    // Apply transmission loss
                    float thickness = rayHit.Material.Thickness;
                    if (thickness > 0)
                    {
                        var transmissionLoss = rayHit.Material.CalculateTransmissionLoss(thickness);
                        response = response * transmissionLoss;
                        transmittedDist += thickness;
                    }
                    else
                    {
                        response = response * rayHit.Material.Transmission;
                    }
                }
                else
                {
                    // Fully occluded
                    return (true, FrequencyResponse.Uniform(0f), 0f);
                }
            }

            currentPos = rayHit.Position + direction * 0.01f;
            currentDist += rayHit.Distance + 0.01f;
        }

        return (anyHit, response, transmittedDist);
    }

    /// <summary>
    /// Raycast against acoustic surfaces
    /// </summary>
    private AcousticRayHit? RaycastSurfaces(Vector3 origin, Vector3 direction, float maxDist)
    {
        AcousticRayHit? closestHit = null;
        float closestDist = maxDist;

        foreach (var (entity, position, surface, transform) in _surfaces)
        {
            // Simple sphere collision for now (TODO: mesh collision)
            // Assume each surface is a sphere with radius 1 at its position
            float radius = 5f; // Default surface radius
            
            var toSurface = position - origin;
            float tca = Vector3.Dot(toSurface, direction);
            
            if (tca < 0) continue; // Behind ray origin
            
            float d2 = Vector3.Dot(toSurface, toSurface) - tca * tca;
            float r2 = radius * radius;
            
            if (d2 > r2) continue; // Misses sphere
            
            float thc = MathF.Sqrt(r2 - d2);
            float t = tca - thc;
            
            if (t < 0.01f || t > closestDist) continue;

            var hitPos = origin + direction * t;
            var normal = Vector3.Normalize(hitPos - position);

            closestHit = new AcousticRayHit
            {
                Position = hitPos,
                Normal = normal,
                Distance = t,
                TravelTime = t / _settings.SpeedOfSound,
                HitEntity = entity,
                Material = surface.Material,
                BounceCount = 0,
                ReachedListener = false,
                TimeDilationFactor = 1f,
                GravitationalRedshift = 1f
            };
            closestDist = t;
        }

        return closestHit;
    }

    /// <summary>
    /// Apply relativistic effects to a path
    /// </summary>
    private void ApplyRelativisticEffects(
        AcousticPath path,
        Vector3 source,
        Vector3 listener,
        Vector3 sourceVel,
        Vector3 listenerVel)
    {
        // 1. Doppler effect from motion
        path.DopplerShift = CalculateDopplerShift(source, listener, sourceVel, listenerVel);

        // 2. Gravitational effects
        float totalTimeDilation = 1f;
        float totalRedshift = 1f;

        foreach (var (entity, gravityPos, gravity) in _gravitySources)
        {
            if (!gravity.AffectsLight) continue; // Only relativistic sources

            // Check effect on source
            float sourceDist = Vector3.Distance(source, gravityPos);
            var sourceEffects = CalculateGravitationalEffects(gravity, sourceDist);
            
            // Check effect on listener  
            float listenerDist = Vector3.Distance(listener, gravityPos);
            var listenerEffects = CalculateGravitationalEffects(gravity, listenerDist);

            // Path average (simplified - real calculation would integrate along path)
            float avgTimeDilation = (sourceEffects.timeDilation + listenerEffects.timeDilation) / 2f;
            float avgRedshift = (sourceEffects.redshift + listenerEffects.redshift) / 2f;

            totalTimeDilation *= avgTimeDilation;
            totalRedshift *= avgRedshift;
        }

        path.GravitationalShift = totalRedshift;
        path.TotalTime *= totalTimeDilation;
    }

    /// <summary>
    /// Calculate gravitational time dilation and redshift
    /// </summary>
    private (float timeDilation, float redshift) CalculateGravitationalEffects(
        GravitySourceComponent gravity, 
        float distance)
    {
        if (gravity.GravityType != GravityType.Schwarzschild && 
            gravity.GravityType != GravityType.Kerr)
        {
            return (1f, 1f);
        }

        float rs = gravity.SchwarzschildRadius;
        
        // Prevent division by zero at event horizon
        if (distance <= rs * 1.01f)
        {
            // At/inside event horizon - extreme effects
            return (0.001f, 0.001f); // Nearly frozen, extremely redshifted
        }

        // Schwarzschild metric time dilation: sqrt(1 - rs/r)
        float timeDilation = MathF.Sqrt(1f - rs / distance);
        
        // Gravitational redshift is the same factor for sound
        // (frequency observed = frequency emitted * sqrt(1 - rs/r))
        float redshift = timeDilation;

        return (timeDilation, redshift);
    }

    /// <summary>
    /// Calculate Doppler shift from relative motion
    /// </summary>
    private float CalculateDopplerShift(
        Vector3 source, 
        Vector3 listener, 
        Vector3 sourceVel, 
        Vector3 listenerVel)
    {
        var direction = Vector3.Normalize(listener - source);
        
        // Relative velocities along the line of propagation
        float sourceSpeed = Vector3.Dot(sourceVel, direction);
        float listenerSpeed = Vector3.Dot(listenerVel, direction);
        
        // Classic Doppler formula: f' = f * (c + vListener) / (c + vSource)
        // Source moving toward listener = higher pitch (positive sourceSpeed)
        // Listener moving toward source = higher pitch (positive listenerSpeed)
        float c = _settings.SpeedOfSound;
        
        float denominator = c - sourceSpeed; // Source moving toward reduces this
        float numerator = c - listenerSpeed; // Listener moving toward reduces this
        
        if (MathF.Abs(denominator) < 0.01f)
            denominator = 0.01f * MathF.Sign(denominator);

        return numerator / denominator;
    }

    /// <summary>
    /// Reflect a direction with scattering
    /// </summary>
    private Vector3 ReflectWithScattering(Vector3 incoming, Vector3 normal, float scattering)
    {
        // Perfect reflection
        var reflected = Vector3.Reflect(incoming, normal);
        
        if (scattering < 0.001f)
            return reflected;

        // Random direction in hemisphere
        var random = GenerateRandomDirection();
        if (Vector3.Dot(random, normal) < 0)
            random = -random;

        // Blend between specular and diffuse based on scattering
        return Vector3.Normalize(Vector3.Lerp(reflected, random, scattering));
    }

    /// <summary>
    /// Generate a random direction on a unit sphere
    /// </summary>
    private Vector3 GenerateRandomDirection()
    {
        // Use spherical coordinates for uniform distribution
        float theta = _random.NextSingle() * 2f * MathF.PI;
        float phi = MathF.Acos(2f * _random.NextSingle() - 1f);
        
        return new Vector3(
            MathF.Sin(phi) * MathF.Cos(theta),
            MathF.Sin(phi) * MathF.Sin(theta),
            MathF.Cos(phi)
        );
    }
}
