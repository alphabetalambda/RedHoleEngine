namespace RedHoleEngine.Input.Haptics;

/// <summary>
/// Represents a haptic feedback command to send to a controller.
/// </summary>
public readonly struct HapticFeedback
{
    /// <summary>
    /// Left motor intensity (low frequency rumble). Range: 0-1.
    /// </summary>
    public float LeftMotor { get; init; }
    
    /// <summary>
    /// Right motor intensity (high frequency rumble). Range: 0-1.
    /// </summary>
    public float RightMotor { get; init; }
    
    /// <summary>
    /// Duration in seconds. 0 = play until stopped.
    /// </summary>
    public float Duration { get; init; }
    
    /// <summary>
    /// Left trigger motor (Xbox Series, DualSense).
    /// </summary>
    public float LeftTrigger { get; init; }
    
    /// <summary>
    /// Right trigger motor (Xbox Series, DualSense).
    /// </summary>
    public float RightTrigger { get; init; }
    
    /// <summary>
    /// Create a simple rumble effect.
    /// </summary>
    public static HapticFeedback Rumble(float left, float right, float duration = 0.1f)
    {
        return new HapticFeedback
        {
            LeftMotor = Math.Clamp(left, 0f, 1f),
            RightMotor = Math.Clamp(right, 0f, 1f),
            Duration = duration
        };
    }
    
    /// <summary>
    /// Create a uniform rumble effect with both motors at the same intensity.
    /// </summary>
    public static HapticFeedback UniformRumble(float intensity, float duration = 0.1f)
    {
        return Rumble(intensity, intensity, duration);
    }
    
    /// <summary>
    /// Create a light tap feedback.
    /// </summary>
    public static HapticFeedback LightTap => Rumble(0.3f, 0.1f, 0.05f);
    
    /// <summary>
    /// Create a medium impact feedback.
    /// </summary>
    public static HapticFeedback MediumImpact => Rumble(0.5f, 0.3f, 0.1f);
    
    /// <summary>
    /// Create a heavy impact feedback.
    /// </summary>
    public static HapticFeedback HeavyImpact => Rumble(0.8f, 0.5f, 0.15f);
    
    /// <summary>
    /// Create continuous engine/motor rumble.
    /// </summary>
    public static HapticFeedback EngineRumble(float intensity) => Rumble(intensity * 0.6f, intensity * 0.3f, 0f);
    
    /// <summary>
    /// Create weapon fire feedback.
    /// </summary>
    public static HapticFeedback WeaponFire => Rumble(0.7f, 0.4f, 0.08f);
    
    /// <summary>
    /// Create explosion feedback.
    /// </summary>
    public static HapticFeedback Explosion => Rumble(1f, 0.8f, 0.3f);
    
    /// <summary>
    /// Create damage taken feedback.
    /// </summary>
    public static HapticFeedback DamageTaken => Rumble(0.4f, 0.6f, 0.12f);
    
    /// <summary>
    /// No haptic feedback.
    /// </summary>
    public static HapticFeedback None => default;
    
    /// <summary>
    /// Create trigger feedback (DualSense-style).
    /// </summary>
    public static HapticFeedback TriggerFeedback(float leftTrigger, float rightTrigger, float duration = 0f)
    {
        return new HapticFeedback
        {
            LeftTrigger = Math.Clamp(leftTrigger, 0f, 1f),
            RightTrigger = Math.Clamp(rightTrigger, 0f, 1f),
            Duration = duration
        };
    }
    
    /// <summary>
    /// Combine two haptic feedbacks (takes maximum of each value).
    /// </summary>
    public static HapticFeedback Combine(HapticFeedback a, HapticFeedback b)
    {
        return new HapticFeedback
        {
            LeftMotor = Math.Max(a.LeftMotor, b.LeftMotor),
            RightMotor = Math.Max(a.RightMotor, b.RightMotor),
            LeftTrigger = Math.Max(a.LeftTrigger, b.LeftTrigger),
            RightTrigger = Math.Max(a.RightTrigger, b.RightTrigger),
            Duration = Math.Max(a.Duration, b.Duration)
        };
    }
    
    /// <summary>
    /// Scale the intensity of this feedback.
    /// </summary>
    public HapticFeedback WithIntensity(float scale)
    {
        return new HapticFeedback
        {
            LeftMotor = LeftMotor * scale,
            RightMotor = RightMotor * scale,
            LeftTrigger = LeftTrigger * scale,
            RightTrigger = RightTrigger * scale,
            Duration = Duration
        };
    }
    
    /// <summary>
    /// Set the duration of this feedback.
    /// </summary>
    public HapticFeedback WithDuration(float duration)
    {
        return new HapticFeedback
        {
            LeftMotor = LeftMotor,
            RightMotor = RightMotor,
            LeftTrigger = LeftTrigger,
            RightTrigger = RightTrigger,
            Duration = duration
        };
    }
    
    public override string ToString()
    {
        return $"Haptic(L={LeftMotor:F2}, R={RightMotor:F2}, Duration={Duration:F2}s)";
    }
}
