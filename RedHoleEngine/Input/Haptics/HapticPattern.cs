namespace RedHoleEngine.Input.Haptics;

/// <summary>
/// A sequence of haptic feedback over time.
/// </summary>
public class HapticPattern
{
    /// <summary>
    /// Name of this pattern.
    /// </summary>
    public string Name { get; set; } = "";
    
    /// <summary>
    /// The keyframes in this pattern.
    /// </summary>
    public List<HapticKeyframe> Keyframes { get; set; } = new();
    
    /// <summary>
    /// Whether this pattern should loop.
    /// </summary>
    public bool Loop { get; set; }
    
    /// <summary>
    /// Total duration of the pattern in seconds.
    /// </summary>
    public float Duration => Keyframes.Count > 0 ? Keyframes[^1].Time : 0f;
    
    /// <summary>
    /// Sample the pattern at a given time.
    /// </summary>
    public HapticFeedback Sample(float time)
    {
        if (Keyframes.Count == 0)
            return HapticFeedback.None;
            
        if (Loop && Duration > 0)
            time %= Duration;
            
        // Find surrounding keyframes
        var prev = Keyframes[0];
        var next = prev;
        
        for (int i = 1; i < Keyframes.Count; i++)
        {
            if (Keyframes[i].Time >= time)
            {
                next = Keyframes[i];
                break;
            }
            prev = Keyframes[i];
            next = prev;
        }
        
        // Interpolate
        if (Math.Abs(next.Time - prev.Time) < 0.0001f)
            return prev.Feedback;
            
        var t = (time - prev.Time) / (next.Time - prev.Time);
        return Lerp(prev.Feedback, next.Feedback, t);
    }
    
    private static HapticFeedback Lerp(HapticFeedback a, HapticFeedback b, float t)
    {
        return new HapticFeedback
        {
            LeftMotor = a.LeftMotor + (b.LeftMotor - a.LeftMotor) * t,
            RightMotor = a.RightMotor + (b.RightMotor - a.RightMotor) * t,
            LeftTrigger = a.LeftTrigger + (b.LeftTrigger - a.LeftTrigger) * t,
            RightTrigger = a.RightTrigger + (b.RightTrigger - a.RightTrigger) * t
        };
    }
    
    /// <summary>
    /// Add a keyframe to the pattern.
    /// </summary>
    public HapticPattern AddKeyframe(float time, HapticFeedback feedback)
    {
        Keyframes.Add(new HapticKeyframe { Time = time, Feedback = feedback });
        Keyframes.Sort((a, b) => a.Time.CompareTo(b.Time));
        return this;
    }
    
    /// <summary>
    /// Create a simple pulse pattern.
    /// </summary>
    public static HapticPattern Pulse(float intensity, float duration, int count, float interval)
    {
        var pattern = new HapticPattern { Name = "Pulse" };
        var time = 0f;
        
        for (int i = 0; i < count; i++)
        {
            pattern.AddKeyframe(time, HapticFeedback.Rumble(intensity, duration));
            time += duration;
            pattern.AddKeyframe(time, HapticFeedback.None);
            time += interval;
        }
        
        return pattern;
    }
    
    /// <summary>
    /// Create a ramp-up pattern.
    /// </summary>
    public static HapticPattern RampUp(float duration, float maxIntensity = 1f)
    {
        var pattern = new HapticPattern { Name = "RampUp" };
        pattern.AddKeyframe(0f, HapticFeedback.None);
        pattern.AddKeyframe(duration, HapticFeedback.UniformRumble(maxIntensity));
        return pattern;
    }
    
    /// <summary>
    /// Create a ramp-down pattern.
    /// </summary>
    public static HapticPattern RampDown(float duration, float startIntensity = 1f)
    {
        var pattern = new HapticPattern { Name = "RampDown" };
        pattern.AddKeyframe(0f, HapticFeedback.UniformRumble(startIntensity));
        pattern.AddKeyframe(duration, HapticFeedback.None);
        return pattern;
    }
    
    /// <summary>
    /// Create a heartbeat pattern.
    /// </summary>
    public static HapticPattern Heartbeat(float bpm = 60f)
    {
        var beatDuration = 60f / bpm;
        var pattern = new HapticPattern { Name = "Heartbeat", Loop = true };
        
        // First beat (stronger)
        pattern.AddKeyframe(0f, HapticFeedback.Rumble(0.6f, 0.3f));
        pattern.AddKeyframe(0.08f, HapticFeedback.None);
        
        // Second beat (weaker)
        pattern.AddKeyframe(0.15f, HapticFeedback.Rumble(0.3f, 0.15f));
        pattern.AddKeyframe(0.22f, HapticFeedback.None);
        
        // Rest
        pattern.AddKeyframe(beatDuration, HapticFeedback.None);
        
        return pattern;
    }
    
    /// <summary>
    /// Create an engine idle pattern.
    /// </summary>
    public static HapticPattern EngineIdle(float rpm = 800f)
    {
        var cycleDuration = 60f / rpm;
        var pattern = new HapticPattern { Name = "EngineIdle", Loop = true };
        
        pattern.AddKeyframe(0f, HapticFeedback.Rumble(0.15f, 0.05f));
        pattern.AddKeyframe(cycleDuration * 0.5f, HapticFeedback.Rumble(0.2f, 0.08f));
        pattern.AddKeyframe(cycleDuration, HapticFeedback.Rumble(0.15f, 0.05f));
        
        return pattern;
    }
}

/// <summary>
/// A single keyframe in a haptic pattern.
/// </summary>
public struct HapticKeyframe
{
    /// <summary>
    /// Time in seconds from pattern start.
    /// </summary>
    public float Time { get; set; }
    
    /// <summary>
    /// The haptic feedback at this time.
    /// </summary>
    public HapticFeedback Feedback { get; set; }
}

/// <summary>
/// Plays haptic patterns over time.
/// </summary>
public class HapticPlayer
{
    private HapticPattern? _currentPattern;
    private float _time;
    private bool _playing;
    
    /// <summary>
    /// Whether a pattern is currently playing.
    /// </summary>
    public bool IsPlaying => _playing;
    
    /// <summary>
    /// Current playback time.
    /// </summary>
    public float Time => _time;
    
    /// <summary>
    /// Play a haptic pattern.
    /// </summary>
    public void Play(HapticPattern pattern)
    {
        _currentPattern = pattern;
        _time = 0f;
        _playing = true;
    }
    
    /// <summary>
    /// Stop the current pattern.
    /// </summary>
    public void Stop()
    {
        _playing = false;
        _currentPattern = null;
    }
    
    /// <summary>
    /// Update and get the current haptic feedback.
    /// </summary>
    public HapticFeedback Update(float deltaTime)
    {
        if (!_playing || _currentPattern == null)
            return HapticFeedback.None;
            
        _time += deltaTime;
        
        // Check if pattern finished
        if (!_currentPattern.Loop && _time > _currentPattern.Duration)
        {
            Stop();
            return HapticFeedback.None;
        }
        
        return _currentPattern.Sample(_time);
    }
}
