using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;

namespace RedHoleEngine.Profiling;

/// <summary>
/// Central profiling system for performance analysis.
/// Tracks CPU timings, frame statistics, and can generate reports.
/// </summary>
public class Profiler
{
    private static readonly Lazy<Profiler> _instance = new(() => new Profiler());
    
    /// <summary>
    /// Singleton instance of the profiler
    /// </summary>
    public static Profiler Instance => _instance.Value;

    private readonly ConcurrentDictionary<string, ProfilerTimer> _timers = new();
    private readonly ConcurrentDictionary<string, ProfilerCounter> _counters = new();
    private readonly object _frameLock = new();
    
    // Frame timing
    private readonly Stopwatch _frameStopwatch = new();
    private readonly List<double> _frameHistory = new();
    private const int MaxFrameHistory = 300; // ~5 seconds at 60fps
    
    private double _lastFrameTimeMs;
    private int _frameCount;
    private double _totalFrameTimeMs;
    
    // GPU timing (set externally by graphics backend)
    private double _lastGpuTimeMs;
    private readonly List<double> _gpuFrameHistory = new();

    /// <summary>
    /// Whether profiling is enabled
    /// </summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>
    /// Current frame number
    /// </summary>
    public int FrameCount => _frameCount;

    /// <summary>
    /// Last frame time in milliseconds
    /// </summary>
    public double LastFrameTimeMs => _lastFrameTimeMs;

    /// <summary>
    /// Average frame time in milliseconds
    /// </summary>
    public double AverageFrameTimeMs => _frameHistory.Count > 0 ? _frameHistory.Average() : 0;

    /// <summary>
    /// Current FPS based on last frame
    /// </summary>
    public double CurrentFps => _lastFrameTimeMs > 0 ? 1000.0 / _lastFrameTimeMs : 0;

    /// <summary>
    /// Average FPS over frame history
    /// </summary>
    public double AverageFps => AverageFrameTimeMs > 0 ? 1000.0 / AverageFrameTimeMs : 0;

    /// <summary>
    /// 1% low FPS (99th percentile frame time)
    /// </summary>
    public double OnePercentLowFps
    {
        get
        {
            if (_frameHistory.Count < 10) return CurrentFps;
            var sorted = _frameHistory.OrderByDescending(x => x).ToList();
            var percentileIndex = (int)(sorted.Count * 0.01);
            var worstFrameTime = sorted[Math.Max(0, percentileIndex)];
            return worstFrameTime > 0 ? 1000.0 / worstFrameTime : 0;
        }
    }

    /// <summary>
    /// Last GPU frame time in milliseconds (if available)
    /// </summary>
    public double LastGpuTimeMs => _lastGpuTimeMs;

    /// <summary>
    /// Average GPU time in milliseconds
    /// </summary>
    public double AverageGpuTimeMs => _gpuFrameHistory.Count > 0 ? _gpuFrameHistory.Average() : 0;

    /// <summary>
    /// All registered timers
    /// </summary>
    public IReadOnlyDictionary<string, ProfilerTimer> Timers => _timers;

    /// <summary>
    /// All registered counters
    /// </summary>
    public IReadOnlyDictionary<string, ProfilerCounter> Counters => _counters;

    /// <summary>
    /// Frame time history for graphing
    /// </summary>
    public IReadOnlyList<double> FrameHistory => _frameHistory;

    /// <summary>
    /// GPU time history for graphing
    /// </summary>
    public IReadOnlyList<double> GpuFrameHistory => _gpuFrameHistory;

    private Profiler()
    {
        _frameStopwatch.Start();
    }

    /// <summary>
    /// Call at the beginning of each frame
    /// </summary>
    public void BeginFrame()
    {
        if (!IsEnabled) return;
        
        _frameStopwatch.Restart();
    }

    /// <summary>
    /// Call at the end of each frame
    /// </summary>
    public void EndFrame()
    {
        if (!IsEnabled) return;
        
        _frameStopwatch.Stop();
        _lastFrameTimeMs = _frameStopwatch.Elapsed.TotalMilliseconds;
        _frameCount++;
        _totalFrameTimeMs += _lastFrameTimeMs;
        
        lock (_frameLock)
        {
            _frameHistory.Add(_lastFrameTimeMs);
            if (_frameHistory.Count > MaxFrameHistory)
            {
                _frameHistory.RemoveAt(0);
            }
        }
    }

    /// <summary>
    /// Record GPU frame time (called by graphics backend)
    /// </summary>
    public void RecordGpuTime(double timeMs)
    {
        _lastGpuTimeMs = timeMs;
        
        lock (_frameLock)
        {
            _gpuFrameHistory.Add(timeMs);
            if (_gpuFrameHistory.Count > MaxFrameHistory)
            {
                _gpuFrameHistory.RemoveAt(0);
            }
        }
    }

    /// <summary>
    /// Get or create a timer with the given name
    /// </summary>
    public ProfilerTimer GetOrCreateTimer(string name, string category = "General")
    {
        var key = $"{category}/{name}";
        return _timers.GetOrAdd(key, _ => new ProfilerTimer(name, category));
    }

    /// <summary>
    /// Get or create a counter with the given name
    /// </summary>
    public ProfilerCounter GetOrCreateCounter(string name, string category = "General")
    {
        var key = $"{category}/{name}";
        return _counters.GetOrAdd(key, _ => new ProfilerCounter(name, category));
    }

    /// <summary>
    /// Create a scoped timer that automatically starts/stops
    /// </summary>
    public ProfilerScope Scope(string name, string category = "General")
    {
        return new ProfilerScope(name, category);
    }

    /// <summary>
    /// Increment a counter
    /// </summary>
    public void Increment(string name, string category = "General", long amount = 1)
    {
        if (!IsEnabled) return;
        GetOrCreateCounter(name, category).Increment(amount);
    }

    /// <summary>
    /// Set a counter value
    /// </summary>
    public void SetCounter(string name, long value, string category = "General")
    {
        if (!IsEnabled) return;
        GetOrCreateCounter(name, category).Set(value);
    }

    /// <summary>
    /// Reset all timers and counters
    /// </summary>
    public void Reset()
    {
        foreach (var timer in _timers.Values)
        {
            timer.Reset();
        }
        
        foreach (var counter in _counters.Values)
        {
            counter.Reset();
        }
        
        lock (_frameLock)
        {
            _frameHistory.Clear();
            _gpuFrameHistory.Clear();
        }
        
        _frameCount = 0;
        _totalFrameTimeMs = 0;
        _lastFrameTimeMs = 0;
        _lastGpuTimeMs = 0;
    }

    /// <summary>
    /// Generate a text report of all profiling data
    /// </summary>
    public string GenerateReport()
    {
        var sb = new StringBuilder();
        
        sb.AppendLine("=== Profiler Report ===");
        sb.AppendLine();
        
        // Frame statistics
        sb.AppendLine("--- Frame Statistics ---");
        sb.AppendLine($"Total Frames:    {_frameCount}");
        sb.AppendLine($"Current FPS:     {CurrentFps:F1}");
        sb.AppendLine($"Average FPS:     {AverageFps:F1}");
        sb.AppendLine($"1% Low FPS:      {OnePercentLowFps:F1}");
        sb.AppendLine($"Frame Time:      {_lastFrameTimeMs:F2}ms");
        sb.AppendLine($"Avg Frame Time:  {AverageFrameTimeMs:F2}ms");
        
        if (_gpuFrameHistory.Count > 0)
        {
            sb.AppendLine($"GPU Time:        {_lastGpuTimeMs:F2}ms");
            sb.AppendLine($"Avg GPU Time:    {AverageGpuTimeMs:F2}ms");
        }
        
        sb.AppendLine();
        
        // Timers by category
        var timersByCategory = _timers.Values
            .GroupBy(t => t.Category)
            .OrderBy(g => g.Key);
        
        foreach (var category in timersByCategory)
        {
            sb.AppendLine($"--- {category.Key} ---");
            
            foreach (var timer in category.OrderByDescending(t => t.AverageElapsedMs))
            {
                sb.AppendLine($"  {timer.Name,-25} {timer.LastElapsedMs,8:F3}ms  (avg: {timer.AverageElapsedMs:F3}ms, min: {timer.MinElapsedMs:F3}ms, max: {timer.MaxElapsedMs:F3}ms)");
            }
            
            sb.AppendLine();
        }
        
        // Counters
        if (_counters.Count > 0)
        {
            sb.AppendLine("--- Counters ---");
            
            var countersByCategory = _counters.Values
                .GroupBy(c => c.Category)
                .OrderBy(g => g.Key);
            
            foreach (var category in countersByCategory)
            {
                foreach (var counter in category.OrderBy(c => c.Name))
                {
                    sb.AppendLine($"  [{category.Key}] {counter.Name,-20} {counter.Value,12:N0}");
                }
            }
        }
        
        sb.AppendLine();
        sb.AppendLine("=== End Report ===");
        
        return sb.ToString();
    }

    /// <summary>
    /// Generate a compact one-line status
    /// </summary>
    public string GetStatusLine()
    {
        var gpuInfo = _gpuFrameHistory.Count > 0 ? $" | GPU: {_lastGpuTimeMs:F2}ms" : "";
        return $"FPS: {CurrentFps:F0} ({_lastFrameTimeMs:F2}ms){gpuInfo} | Avg: {AverageFps:F0} | 1%: {OnePercentLowFps:F0}";
    }
}

/// <summary>
/// Counter for tracking discrete events or quantities
/// </summary>
public class ProfilerCounter
{
    private long _value;
    private long _lastValue;
    private long _total;
    private int _sampleCount;
    
    public string Name { get; }
    public string Category { get; }
    
    public long Value => _value;
    public long LastValue => _lastValue;
    public double Average => _sampleCount > 0 ? _total / (double)_sampleCount : 0;
    public int SampleCount => _sampleCount;

    public ProfilerCounter(string name, string category = "General")
    {
        Name = name;
        Category = category;
    }

    public void Increment(long amount = 1)
    {
        Interlocked.Add(ref _value, amount);
    }

    public void Decrement(long amount = 1)
    {
        Interlocked.Add(ref _value, -amount);
    }

    public void Set(long value)
    {
        Interlocked.Exchange(ref _value, value);
    }

    /// <summary>
    /// Record current value as a sample and reset
    /// </summary>
    public void Sample()
    {
        _lastValue = Interlocked.Exchange(ref _value, 0);
        _total += _lastValue;
        _sampleCount++;
    }

    public void Reset()
    {
        _value = 0;
        _lastValue = 0;
        _total = 0;
        _sampleCount = 0;
    }
}
