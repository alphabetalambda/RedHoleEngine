using System.Numerics;

namespace RedHoleEngine.Audio;

/// <summary>
/// Represents processed audio data ready for output
/// </summary>
public class ProcessedAudioSource
{
    /// <summary>Source entity ID</summary>
    public int SourceEntityId { get; set; }
    
    /// <summary>Audio clip ID to play</summary>
    public string ClipId { get; set; } = "";
    
    /// <summary>Final volume after all processing (0-1)</summary>
    public float Volume { get; set; }
    
    /// <summary>Final pitch multiplier (Doppler + gravitational)</summary>
    public float Pitch { get; set; }
    
    /// <summary>Stereo pan (-1 = left, 0 = center, 1 = right)</summary>
    public float Pan { get; set; }
    
    /// <summary>Delay in seconds (from propagation time)</summary>
    public float Delay { get; set; }
    
    /// <summary>Low-pass filter cutoff frequency (Hz)</summary>
    public float LowPassCutoff { get; set; }
    
    /// <summary>High-pass filter cutoff frequency (Hz)</summary>
    public float HighPassCutoff { get; set; }
    
    /// <summary>Per-band EQ gain (dB)</summary>
    public FrequencyResponse EQGain { get; set; }
    
    /// <summary>Reverb send level (0-1)</summary>
    public float ReverbSend { get; set; }
    
    /// <summary>3D position for HRTF</summary>
    public Vector3 Position { get; set; }
    
    /// <summary>Whether to use HRTF spatialization</summary>
    public bool UseHRTF { get; set; }
    
    /// <summary>Early reflections data</summary>
    public List<ReflectionData> EarlyReflections { get; } = new();
}

/// <summary>
/// Data for a single reflection
/// </summary>
public struct ReflectionData
{
    public float Delay;
    public float Volume;
    public float Pan;
    public FrequencyResponse Response;
}

/// <summary>
/// Audio mixing and spatialization system
/// </summary>
public class AudioMixer
{
    private readonly List<ProcessedAudioSource> _processedSources = new();
    private readonly Dictionary<int, ProcessedAudioSource> _sourceCache = new();
    
    /// <summary>Master volume</summary>
    public float MasterVolume { get; set; } = 1f;
    
    /// <summary>Global reverb settings</summary>
    public ReverbZoneComponent GlobalReverb { get; set; } = ReverbZoneComponent.SmallRoom;
    
    /// <summary>Listener position (for spatialization)</summary>
    public Vector3 ListenerPosition { get; set; }
    
    /// <summary>Listener forward direction</summary>
    public Vector3 ListenerForward { get; set; } = Vector3.UnitZ;
    
    /// <summary>Listener up direction</summary>
    public Vector3 ListenerUp { get; set; } = Vector3.UnitY;
    
    /// <summary>Listener right direction (calculated)</summary>
    public Vector3 ListenerRight => Vector3.Cross(ListenerForward, ListenerUp);
    
    /// <summary>Maximum number of simultaneous voices</summary>
    public int MaxVoices { get; set; } = 32;
    
    /// <summary>Whether to enable HRTF spatialization</summary>
    public bool EnableHRTF { get; set; } = true;

    /// <summary>
    /// Process acoustic paths into mixable audio data
    /// </summary>
    public ProcessedAudioSource ProcessPaths(
        int sourceEntityId,
        string clipId,
        Vector3 sourcePosition,
        float baseVolume,
        float basePitch,
        List<AcousticPath> paths)
    {
        if (!_sourceCache.TryGetValue(sourceEntityId, out var processed))
        {
            processed = new ProcessedAudioSource();
            _sourceCache[sourceEntityId] = processed;
        }

        processed.SourceEntityId = sourceEntityId;
        processed.ClipId = clipId;
        processed.Position = sourcePosition;
        processed.EarlyReflections.Clear();

        if (paths.Count == 0)
        {
            // No paths - fully occluded or out of range
            processed.Volume = 0f;
            processed.Pitch = basePitch;
            return processed;
        }

        // Find the direct/primary path
        var directPath = paths.FirstOrDefault(p => p.IsDirect) ?? paths[0];
        
        // Calculate base parameters from direct path
        processed.Volume = baseVolume * directPath.FinalResponse.Average * MasterVolume;
        processed.Pitch = basePitch * directPath.TotalPitchShift;
        processed.Delay = directPath.TotalTime;
        processed.UseHRTF = EnableHRTF;

        // Calculate panning from source position relative to listener
        processed.Pan = CalculatePan(sourcePosition);

        // Apply frequency-dependent filtering
        ApplyFrequencyFiltering(processed, directPath.FinalResponse);

        // Process early reflections
        foreach (var path in paths.Where(p => p.Type == PathType.EarlyReflection))
        {
            if (processed.EarlyReflections.Count >= 8) break; // Limit reflections
            
            // Calculate reflection position for panning
            var reflectionPos = path.Hits.Count > 0 ? path.Hits[0].Position : sourcePosition;
            
            processed.EarlyReflections.Add(new ReflectionData
            {
                Delay = path.TotalTime,
                Volume = path.FinalResponse.Average * 0.5f, // Reflections are quieter
                Pan = CalculatePan(reflectionPos),
                Response = path.FinalResponse
            });
        }

        // Calculate reverb send based on late reflections
        var lateReflections = paths.Where(p => p.Type == PathType.LateReflection).ToList();
        processed.ReverbSend = lateReflections.Count > 0 
            ? MathF.Min(1f, lateReflections.Average(p => p.FinalResponse.Average) * 2f)
            : 0.3f; // Default reverb send

        return processed;
    }

    /// <summary>
    /// Calculate stereo pan from world position
    /// </summary>
    private float CalculatePan(Vector3 worldPosition)
    {
        var toSource = worldPosition - ListenerPosition;
        
        // Project onto listener's horizontal plane
        float rightAmount = Vector3.Dot(toSource, ListenerRight);
        float forwardAmount = Vector3.Dot(toSource, ListenerForward);
        
        // Calculate angle
        float angle = MathF.Atan2(rightAmount, forwardAmount);
        
        // Convert to pan (-1 to 1)
        return MathF.Sin(angle);
    }

    /// <summary>
    /// Apply frequency-dependent filtering based on acoustic response
    /// </summary>
    private void ApplyFrequencyFiltering(ProcessedAudioSource source, FrequencyResponse response)
    {
        source.EQGain = response;

        // Determine cutoff frequencies based on response
        // If high frequencies are very attenuated, apply low-pass
        float highFreqAvg = (response.Band4kHz + response.Band8kHz) / 2f;
        float lowFreqAvg = (response.Band63Hz + response.Band125Hz) / 2f;
        
        if (highFreqAvg < 0.3f)
        {
            // Significant high frequency loss - apply low-pass
            source.LowPassCutoff = 2000f + highFreqAvg * 6000f; // 2-8kHz range
        }
        else
        {
            source.LowPassCutoff = 20000f; // No filtering
        }

        if (lowFreqAvg < 0.3f)
        {
            // Significant low frequency loss - apply high-pass
            source.HighPassCutoff = 200f * (1f - lowFreqAvg); // 0-200Hz range
        }
        else
        {
            source.HighPassCutoff = 20f; // No filtering
        }
    }

    /// <summary>
    /// Voice prioritization - returns the sources that should play
    /// </summary>
    public List<ProcessedAudioSource> PrioritizeVoices(
        IEnumerable<ProcessedAudioSource> sources,
        Dictionary<int, int> priorities)
    {
        return sources
            .Where(s => s.Volume > 0.001f) // Filter inaudible
            .OrderByDescending(s => priorities.GetValueOrDefault(s.SourceEntityId, 0)) // Priority
            .ThenByDescending(s => s.Volume) // Then by volume
            .Take(MaxVoices)
            .ToList();
    }

    /// <summary>
    /// Clear cached source data (call when sources are destroyed)
    /// </summary>
    public void ClearSource(int entityId)
    {
        _sourceCache.Remove(entityId);
    }

    /// <summary>
    /// Clear all cached data
    /// </summary>
    public void Clear()
    {
        _sourceCache.Clear();
        _processedSources.Clear();
    }
}
