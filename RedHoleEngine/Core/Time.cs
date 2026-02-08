namespace RedHoleEngine.Core;

/// <summary>
/// Provides time information for the game loop.
/// </summary>
public static class Time
{
    /// <summary>
    /// Time since the last frame (in seconds)
    /// </summary>
    public static float DeltaTime { get; private set; }
    
    /// <summary>
    /// Fixed timestep for physics updates (in seconds)
    /// </summary>
    public static float FixedDeltaTime { get; set; } = 1f / 60f;
    
    /// <summary>
    /// Total time since the engine started (in seconds)
    /// </summary>
    public static float TotalTime { get; private set; }
    
    /// <summary>
    /// Unscaled time since last frame (ignores TimeScale)
    /// </summary>
    public static float UnscaledDeltaTime { get; private set; }
    
    /// <summary>
    /// Scale factor for time (1.0 = normal, 0.5 = half speed, 2.0 = double speed)
    /// </summary>
    public static float TimeScale { get; set; } = 1f;
    
    /// <summary>
    /// Current frame number since start
    /// </summary>
    public static ulong FrameCount { get; private set; }

    /// <summary>
    /// Accumulated time for fixed updates
    /// </summary>
    private static float _fixedTimeAccumulator;
    
    /// <summary>
    /// Maximum delta time to prevent spiral of death
    /// </summary>
    public const float MaxDeltaTime = 0.1f;

    /// <summary>
    /// Update time values (called by engine)
    /// </summary>
    internal static void Update(float rawDeltaTime)
    {
        // Clamp to prevent spiral of death
        UnscaledDeltaTime = MathF.Min(rawDeltaTime, MaxDeltaTime);
        DeltaTime = UnscaledDeltaTime * TimeScale;
        TotalTime += DeltaTime;
        FrameCount++;
        
        _fixedTimeAccumulator += DeltaTime;
    }

    /// <summary>
    /// Check if a fixed update should run (and consume accumulated time)
    /// </summary>
    internal static bool ShouldRunFixedUpdate()
    {
        if (_fixedTimeAccumulator >= FixedDeltaTime)
        {
            _fixedTimeAccumulator -= FixedDeltaTime;
            return true;
        }
        return false;
    }

    /// <summary>
    /// Get interpolation factor for rendering between fixed updates
    /// </summary>
    public static float FixedUpdateAlpha => _fixedTimeAccumulator / FixedDeltaTime;

    /// <summary>
    /// Reset time values (e.g., when loading a new scene)
    /// </summary>
    internal static void Reset()
    {
        DeltaTime = 0;
        TotalTime = 0;
        FrameCount = 0;
        _fixedTimeAccumulator = 0;
        TimeScale = 1f;
    }
}
