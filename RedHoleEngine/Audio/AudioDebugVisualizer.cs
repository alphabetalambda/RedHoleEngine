using System.Numerics;
using RedHoleEngine.Components;
using RedHoleEngine.Core.ECS;
using RedHoleEngine.Rendering.Debug;

namespace RedHoleEngine.Audio;

/// <summary>
/// Visualization modes for audio debugging
/// </summary>
[Flags]
public enum AudioDebugFlags
{
    None = 0,
    
    /// <summary>Show audio source positions and ranges</summary>
    Sources = 1 << 0,
    
    /// <summary>Show listener position and orientation</summary>
    Listener = 1 << 1,
    
    /// <summary>Show direct sound paths</summary>
    DirectPaths = 1 << 2,
    
    /// <summary>Show reflection paths</summary>
    ReflectionPaths = 1 << 3,
    
    /// <summary>Show transmission/occlusion paths</summary>
    TransmissionPaths = 1 << 4,
    
    /// <summary>Show acoustic surfaces</summary>
    AcousticSurfaces = 1 << 5,
    
    /// <summary>Show gravity sources affecting audio</summary>
    GravitySources = 1 << 6,
    
    /// <summary>Show reverb zones</summary>
    ReverbZones = 1 << 7,
    
    /// <summary>Show frequency response at hit points</summary>
    FrequencyData = 1 << 8,
    
    /// <summary>Show sound wave propagation rings</summary>
    WavePropagation = 1 << 9,
    
    /// <summary>Show Doppler/gravitational shift indicators</summary>
    RelativisticEffects = 1 << 10,
    
    /// <summary>Show labels with audio data</summary>
    Labels = 1 << 11,
    
    /// <summary>All visualizations</summary>
    All = ~0
}

/// <summary>
/// Cached data for a traced audio path (for visualization)
/// </summary>
public class DebugAudioPath
{
    public int SourceEntityId { get; set; }
    public Vector3 SourcePosition { get; set; }
    public Vector3 ListenerPosition { get; set; }
    public PathType PathType { get; set; }
    public List<Vector3> Points { get; } = new();
    public List<Vector3> HitNormals { get; } = new();
    public FrequencyResponse FinalResponse { get; set; }
    public float TotalDistance { get; set; }
    public float TotalTime { get; set; }
    public float DopplerShift { get; set; }
    public float GravitationalShift { get; set; }
    public bool IsOccluded { get; set; }
    public float Timestamp { get; set; }
}

/// <summary>
/// Visualizes raytraced audio for debugging purposes
/// </summary>
public class AudioDebugVisualizer
{
    private readonly World _world;
    private readonly DebugDrawManager _debugDraw;
    
    // Cached paths for visualization
    private readonly List<DebugAudioPath> _activePaths = new();
    private readonly Dictionary<int, List<DebugAudioPath>> _pathsBySource = new();
    
    // Animation state
    private float _waveAnimationTime;
    private const float WaveSpeed = 10f;
    private const float WaveInterval = 0.5f;
    
    /// <summary>
    /// What to visualize
    /// </summary>
    public AudioDebugFlags Flags { get; set; } = AudioDebugFlags.Sources | 
                                                   AudioDebugFlags.Listener | 
                                                   AudioDebugFlags.DirectPaths;
    
    /// <summary>
    /// How long paths remain visible (seconds)
    /// </summary>
    public float PathLifetime { get; set; } = 0.5f;
    
    /// <summary>
    /// Whether visualization is enabled
    /// </summary>
    public bool Enabled { get; set; } = true;
    
    /// <summary>
    /// Scale factor for visualizations
    /// </summary>
    public float Scale { get; set; } = 1f;

    public AudioDebugVisualizer(World world, DebugDrawManager debugDraw)
    {
        _world = world;
        _debugDraw = debugDraw;
    }

    /// <summary>
    /// Add a traced path for visualization
    /// </summary>
    public void AddPath(int sourceEntityId, Vector3 sourcePos, Vector3 listenerPos, AcousticPath path, float currentTime)
    {
        var debugPath = new DebugAudioPath
        {
            SourceEntityId = sourceEntityId,
            SourcePosition = sourcePos,
            ListenerPosition = listenerPos,
            PathType = path.Type,
            FinalResponse = path.FinalResponse,
            TotalDistance = path.TotalDistance,
            TotalTime = path.TotalTime,
            DopplerShift = path.DopplerShift,
            GravitationalShift = path.GravitationalShift,
            IsOccluded = !path.IsDirect && path.Type == PathType.Transmission,
            Timestamp = currentTime
        };

        // Build path points
        debugPath.Points.Add(sourcePos);
        
        foreach (var hit in path.Hits)
        {
            debugPath.Points.Add(hit.Position);
            debugPath.HitNormals.Add(hit.Normal);
        }
        
        debugPath.Points.Add(listenerPos);

        _activePaths.Add(debugPath);
        
        if (!_pathsBySource.ContainsKey(sourceEntityId))
            _pathsBySource[sourceEntityId] = new List<DebugAudioPath>();
        _pathsBySource[sourceEntityId].Add(debugPath);
    }

    /// <summary>
    /// Clear paths for a specific source
    /// </summary>
    public void ClearPathsForSource(int sourceEntityId)
    {
        _activePaths.RemoveAll(p => p.SourceEntityId == sourceEntityId);
        _pathsBySource.Remove(sourceEntityId);
    }

    /// <summary>
    /// Update and render debug visualization
    /// </summary>
    public void Update(float deltaTime, float currentTime)
    {
        if (!Enabled) return;

        _waveAnimationTime += deltaTime;

        // Remove expired paths
        _activePaths.RemoveAll(p => currentTime - p.Timestamp > PathLifetime);
        foreach (var list in _pathsBySource.Values)
        {
            list.RemoveAll(p => currentTime - p.Timestamp > PathLifetime);
        }

        // Draw all visualizations based on flags
        if (Flags.HasFlag(AudioDebugFlags.Sources))
            DrawSources();
        
        if (Flags.HasFlag(AudioDebugFlags.Listener))
            DrawListener();
        
        if (Flags.HasFlag(AudioDebugFlags.DirectPaths))
            DrawPaths(PathType.Direct, currentTime);
        
        if (Flags.HasFlag(AudioDebugFlags.ReflectionPaths))
        {
            DrawPaths(PathType.EarlyReflection, currentTime);
            DrawPaths(PathType.LateReflection, currentTime);
        }
        
        if (Flags.HasFlag(AudioDebugFlags.TransmissionPaths))
            DrawPaths(PathType.Transmission, currentTime);
        
        if (Flags.HasFlag(AudioDebugFlags.AcousticSurfaces))
            DrawAcousticSurfaces();
        
        if (Flags.HasFlag(AudioDebugFlags.GravitySources))
            DrawGravitySources();
        
        if (Flags.HasFlag(AudioDebugFlags.ReverbZones))
            DrawReverbZones();
        
        if (Flags.HasFlag(AudioDebugFlags.WavePropagation))
            DrawWavePropagation(currentTime);
    }

    private void DrawSources()
    {
        foreach (var entity in _world.Query<AudioSourceComponent, TransformComponent>())
        {
            ref var source = ref _world.GetComponent<AudioSourceComponent>(entity);
            ref var transform = ref _world.GetComponent<TransformComponent>(entity);

            var pos = transform.Position;
            var color = source.IsPlaying ? DebugColor.AudioSource : DebugColor.AudioSource.WithAlpha(0.3f);

            // Draw source icon (small sphere)
            _debugDraw.DrawWireSphere(pos, 0.5f * Scale, color, 8);

            // Draw min/max distance spheres
            if (source.IsPlaying)
            {
                _debugDraw.DrawCircle(pos, source.MinDistance, Vector3.UnitY, 
                    DebugColor.Green.WithAlpha(0.3f), 32);
                _debugDraw.DrawCircle(pos, source.MaxDistance, Vector3.UnitY, 
                    DebugColor.Red.WithAlpha(0.2f), 32);
            }

            // Draw directivity cone
            if (source.Directivity > 0.1f && source.IsPlaying)
            {
                float coneAngle = 180f * (1f - source.Directivity);
                _debugDraw.DrawCone(pos, source.Direction, source.MaxDistance * 0.3f, coneAngle, 
                    DebugColor.Yellow.WithAlpha(0.3f));
            }

            // Draw velocity arrow (for Doppler)
            if (source.Velocity.LengthSquared() > 0.01f)
            {
                _debugDraw.DrawArrow(pos, pos + source.Velocity * 0.5f, DebugColor.Cyan, 0.2f);
            }

            // Label
            if (Flags.HasFlag(AudioDebugFlags.Labels))
            {
                string label = $"Source {entity.Id}";
                if (source.IsPlaying)
                    label += $"\nVol: {source.Volume:F2}";
                _debugDraw.DrawText(pos + Vector3.UnitY * 1.5f * Scale, label, color);
            }
        }
    }

    private void DrawListener()
    {
        foreach (var entity in _world.Query<AudioListenerComponent, TransformComponent>())
        {
            ref var listener = ref _world.GetComponent<AudioListenerComponent>(entity);
            if (!listener.IsActive) continue;

            ref var transform = ref _world.GetComponent<TransformComponent>(entity);
            var pos = transform.Position;

            // Draw listener icon (ear-like shape using circles)
            _debugDraw.DrawWireSphere(pos, 0.3f * Scale, DebugColor.AudioListener, 8);
            
            // Draw orientation
            _debugDraw.DrawArrow(pos, pos + transform.Forward * 2f * Scale, DebugColor.Blue, 0.3f);
            _debugDraw.DrawArrow(pos, pos + transform.Up * 1f * Scale, DebugColor.Green, 0.2f);
            _debugDraw.DrawArrow(pos, pos + transform.Right * 1f * Scale, DebugColor.Red, 0.2f);

            // Draw velocity
            if (listener.Velocity.LengthSquared() > 0.01f)
            {
                _debugDraw.DrawArrow(pos, pos + listener.Velocity * 0.5f, DebugColor.Magenta, 0.2f);
            }

            if (Flags.HasFlag(AudioDebugFlags.Labels))
            {
                _debugDraw.DrawText(pos + Vector3.UnitY * 1.5f * Scale, 
                    $"Listener\nVol: {listener.Volume:F2}", DebugColor.AudioListener);
            }
        }
    }

    private void DrawPaths(PathType type, float currentTime)
    {
        var color = type switch
        {
            PathType.Direct => DebugColor.DirectPath,
            PathType.EarlyReflection => DebugColor.ReflectionPath,
            PathType.LateReflection => DebugColor.ReflectionPath.WithAlpha(0.4f),
            PathType.Transmission => DebugColor.TransmissionPath,
            PathType.Diffraction => DebugColor.Purple,
            _ => DebugColor.White
        };

        foreach (var path in _activePaths.Where(p => p.PathType == type))
        {
            float age = currentTime - path.Timestamp;
            float alpha = 1f - (age / PathLifetime);
            var fadeColor = color.WithAlpha(color.A * alpha);

            // Draw path segments
            for (int i = 0; i < path.Points.Count - 1; i++)
            {
                _debugDraw.DrawLine(path.Points[i], path.Points[i + 1], fadeColor, 2f);
            }

            // Draw hit points and normals
            for (int i = 0; i < path.HitNormals.Count && i < path.Points.Count - 1; i++)
            {
                var hitPos = path.Points[i + 1];
                var normal = path.HitNormals[i];

                _debugDraw.DrawPoint(hitPos, DebugColor.White, 0.15f * Scale);
                _debugDraw.DrawLine(hitPos, hitPos + normal * 0.5f * Scale, DebugColor.Cyan.WithAlpha(0.5f));
            }

            // Draw frequency response visualization at midpoint
            if (Flags.HasFlag(AudioDebugFlags.FrequencyData) && path.Points.Count >= 2)
            {
                int midIndex = path.Points.Count / 2;
                var midPoint = path.Points[midIndex];
                DrawFrequencyResponse(midPoint, path.FinalResponse, alpha);
            }

            // Draw relativistic effects indicator
            if (Flags.HasFlag(AudioDebugFlags.RelativisticEffects))
            {
                if (MathF.Abs(path.DopplerShift - 1f) > 0.05f || MathF.Abs(path.GravitationalShift - 1f) > 0.05f)
                {
                    var midPoint = path.Points[path.Points.Count / 2];
                    float totalShift = path.DopplerShift * path.GravitationalShift;
                    
                    // Blue = blueshifted (higher pitch), Red = redshifted (lower pitch)
                    var shiftColor = totalShift > 1f 
                        ? DebugColor.Lerp(DebugColor.White, DebugColor.Blue, (totalShift - 1f) * 2f)
                        : DebugColor.Lerp(DebugColor.White, DebugColor.Red, (1f - totalShift) * 2f);
                    
                    _debugDraw.DrawWireSphere(midPoint, 0.3f * Scale, shiftColor.WithAlpha(alpha * 0.7f), 8);
                    
                    if (Flags.HasFlag(AudioDebugFlags.Labels))
                    {
                        _debugDraw.DrawText(midPoint + Vector3.UnitY * 0.5f, 
                            $"Shift: {totalShift:F2}x", shiftColor.WithAlpha(alpha));
                    }
                }
            }
        }
    }

    private void DrawFrequencyResponse(Vector3 position, FrequencyResponse response, float alpha)
    {
        // Draw a small bar chart showing frequency bands
        float barWidth = 0.1f * Scale;
        float maxHeight = 1f * Scale;
        float spacing = 0.02f * Scale;
        float startX = -(4 * barWidth + 3.5f * spacing);

        var bands = new[]
        {
            response.Band63Hz, response.Band125Hz, response.Band250Hz, response.Band500Hz,
            response.Band1kHz, response.Band2kHz, response.Band4kHz, response.Band8kHz
        };

        for (int i = 0; i < 8; i++)
        {
            float x = startX + i * (barWidth + spacing);
            float height = bands[i] * maxHeight;
            
            // Color: green for high response, red for low
            var barColor = DebugColor.Lerp(DebugColor.Red, DebugColor.Green, bands[i]).WithAlpha(alpha * 0.7f);
            
            var bottom = position + new Vector3(x, 0, 0);
            var top = position + new Vector3(x, height, 0);
            
            _debugDraw.DrawLine(bottom, top, barColor, 3f);
        }
    }

    private void DrawAcousticSurfaces()
    {
        foreach (var entity in _world.Query<AcousticSurfaceComponent, TransformComponent>())
        {
            ref var surface = ref _world.GetComponent<AcousticSurfaceComponent>(entity);
            ref var transform = ref _world.GetComponent<TransformComponent>(entity);

            var pos = transform.Position;
            
            // Color based on absorption (high absorption = dark, low = bright)
            float avgAbsorption = surface.Material.Absorption.Average;
            var color = DebugColor.Lerp(DebugColor.White, DebugColor.Blue, avgAbsorption).WithAlpha(0.4f);

            // Draw as a small oriented quad/box
            _debugDraw.DrawWireBox(pos - Vector3.One * 2.5f, pos + Vector3.One * 2.5f, color);

            if (Flags.HasFlag(AudioDebugFlags.Labels))
            {
                _debugDraw.DrawText(pos + Vector3.UnitY * 3f, 
                    $"{surface.Material.Name}\nAbs: {avgAbsorption:F2}", color);
            }
        }
    }

    private void DrawGravitySources()
    {
        foreach (var entity in _world.Query<GravitySourceComponent, TransformComponent>())
        {
            ref var gravity = ref _world.GetComponent<GravitySourceComponent>(entity);
            ref var transform = ref _world.GetComponent<TransformComponent>(entity);

            if (!gravity.AffectsLight) continue; // Only show relativistic sources

            var pos = transform.Position;
            float rs = gravity.SchwarzschildRadius;

            // Event horizon (solid sphere would be black)
            _debugDraw.DrawWireSphere(pos, rs, DebugColor.Black, 16);

            // Photon sphere
            _debugDraw.DrawWireSphere(pos, gravity.PhotonSphereRadius, DebugColor.Orange.WithAlpha(0.4f), 16);

            // ISCO
            _debugDraw.DrawCircle(pos, gravity.ISCO, Vector3.UnitY, DebugColor.Yellow.WithAlpha(0.3f), 32);

            // Time dilation gradient rings
            for (int i = 1; i <= 5; i++)
            {
                float r = rs * (1.5f + i * 0.5f);
                float dilation = MathF.Sqrt(1f - rs / r);
                var ringColor = DebugColor.Lerp(DebugColor.Red, DebugColor.Purple, dilation).WithAlpha(0.2f);
                _debugDraw.DrawCircle(pos, r, Vector3.UnitY, ringColor, 24);
            }

            if (Flags.HasFlag(AudioDebugFlags.Labels))
            {
                _debugDraw.DrawText(pos + Vector3.UnitY * (rs + 2f), 
                    $"Black Hole\nM={gravity.Mass:F1}\nRs={rs:F1}", DebugColor.Purple);
            }
        }
    }

    private void DrawReverbZones()
    {
        foreach (var entity in _world.Query<ReverbZoneComponent, TransformComponent>())
        {
            ref var reverb = ref _world.GetComponent<ReverbZoneComponent>(entity);
            ref var transform = ref _world.GetComponent<TransformComponent>(entity);

            var pos = transform.Position;
            
            // Main zone
            _debugDraw.DrawWireSphere(pos, reverb.Radius, DebugColor.Cyan.WithAlpha(0.3f), 16);
            
            // Blend distance
            _debugDraw.DrawWireSphere(pos, reverb.Radius + reverb.BlendDistance, 
                DebugColor.Cyan.WithAlpha(0.1f), 16);

            if (Flags.HasFlag(AudioDebugFlags.Labels))
            {
                _debugDraw.DrawText(pos + Vector3.UnitY * (reverb.Radius + 1f),
                    $"Reverb Zone\nRT60: {reverb.ReverbTime:F2}s", DebugColor.Cyan);
            }
        }
    }

    private void DrawWavePropagation(float currentTime)
    {
        foreach (var entity in _world.Query<AudioSourceComponent, TransformComponent>())
        {
            ref var source = ref _world.GetComponent<AudioSourceComponent>(entity);
            if (!source.IsPlaying) continue;

            ref var transform = ref _world.GetComponent<TransformComponent>(entity);
            var pos = transform.Position;

            // Draw expanding rings
            float wavePhase = (_waveAnimationTime % WaveInterval) / WaveInterval;
            
            for (int i = 0; i < 3; i++)
            {
                float phase = (wavePhase + i * 0.33f) % 1f;
                float radius = phase * source.MaxDistance * 0.5f;
                float alpha = (1f - phase) * 0.3f;
                
                _debugDraw.DrawCircle(pos, radius, Vector3.UnitY, 
                    DebugColor.AudioSource.WithAlpha(alpha), 24);
            }
        }
    }

    /// <summary>
    /// Clear all cached visualization data
    /// </summary>
    public void Clear()
    {
        _activePaths.Clear();
        _pathsBySource.Clear();
    }
}
