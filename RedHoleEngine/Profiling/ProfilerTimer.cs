using System.Diagnostics;

namespace RedHoleEngine.Profiling;

/// <summary>
/// High-precision timer for profiling code sections.
/// Uses Stopwatch for accurate measurements.
/// </summary>
public class ProfilerTimer
{
    private readonly Stopwatch _stopwatch = new();
    private long _accumulatedTicks;
    private int _sampleCount;
    
    /// <summary>
    /// Name of this timer for identification
    /// </summary>
    public string Name { get; }
    
    /// <summary>
    /// Category for grouping related timers
    /// </summary>
    public string Category { get; }
    
    /// <summary>
    /// Whether the timer is currently running
    /// </summary>
    public bool IsRunning => _stopwatch.IsRunning;
    
    /// <summary>
    /// Last measured elapsed time in milliseconds
    /// </summary>
    public double LastElapsedMs { get; private set; }
    
    /// <summary>
    /// Average elapsed time across all samples in milliseconds
    /// </summary>
    public double AverageElapsedMs => _sampleCount > 0 
        ? (_accumulatedTicks / (double)_sampleCount) / Stopwatch.Frequency * 1000.0 
        : 0;
    
    /// <summary>
    /// Total accumulated time in milliseconds
    /// </summary>
    public double TotalElapsedMs => _accumulatedTicks / (double)Stopwatch.Frequency * 1000.0;
    
    /// <summary>
    /// Number of times this timer has been sampled
    /// </summary>
    public int SampleCount => _sampleCount;
    
    /// <summary>
    /// Minimum elapsed time recorded in milliseconds
    /// </summary>
    public double MinElapsedMs { get; private set; } = double.MaxValue;
    
    /// <summary>
    /// Maximum elapsed time recorded in milliseconds
    /// </summary>
    public double MaxElapsedMs { get; private set; } = double.MinValue;

    public ProfilerTimer(string name, string category = "General")
    {
        Name = name;
        Category = category;
    }

    /// <summary>
    /// Start timing
    /// </summary>
    public void Start()
    {
        _stopwatch.Restart();
    }

    /// <summary>
    /// Stop timing and record the sample
    /// </summary>
    public void Stop()
    {
        _stopwatch.Stop();
        var ticks = _stopwatch.ElapsedTicks;
        _accumulatedTicks += ticks;
        _sampleCount++;
        
        LastElapsedMs = ticks / (double)Stopwatch.Frequency * 1000.0;
        
        if (LastElapsedMs < MinElapsedMs) MinElapsedMs = LastElapsedMs;
        if (LastElapsedMs > MaxElapsedMs) MaxElapsedMs = LastElapsedMs;
    }

    /// <summary>
    /// Reset all accumulated statistics
    /// </summary>
    public void Reset()
    {
        _stopwatch.Reset();
        _accumulatedTicks = 0;
        _sampleCount = 0;
        LastElapsedMs = 0;
        MinElapsedMs = double.MaxValue;
        MaxElapsedMs = double.MinValue;
    }

    public override string ToString()
    {
        return $"{Category}/{Name}: {LastElapsedMs:F3}ms (avg: {AverageElapsedMs:F3}ms, min: {MinElapsedMs:F3}ms, max: {MaxElapsedMs:F3}ms, samples: {SampleCount})";
    }
}

/// <summary>
/// RAII-style scope for automatic timer start/stop.
/// Use with 'using' statement for automatic cleanup.
/// </summary>
public readonly struct ProfilerScope : IDisposable
{
    private readonly ProfilerTimer? _timer;
    private readonly bool _enabled;

    public ProfilerScope(ProfilerTimer? timer, bool enabled = true)
    {
        _timer = timer;
        _enabled = enabled && timer != null;
        
        if (_enabled)
        {
            _timer!.Start();
        }
    }
    
    /// <summary>
    /// Create a scope that automatically registers with the global profiler
    /// </summary>
    public ProfilerScope(string name, string category = "General")
    {
        _timer = Profiler.Instance.GetOrCreateTimer(name, category);
        _enabled = Profiler.Instance.IsEnabled;
        
        if (_enabled)
        {
            _timer.Start();
        }
    }

    public void Dispose()
    {
        if (_enabled)
        {
            _timer!.Stop();
        }
    }
}
