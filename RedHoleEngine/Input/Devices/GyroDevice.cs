using System.Numerics;

namespace RedHoleEngine.Input.Devices;

/// <summary>
/// Represents a gyroscope/motion sensor input device.
/// Can be standalone or part of a gamepad (Steam Deck, DualSense, etc.).
/// </summary>
public class GyroDevice : InputDevice
{
    public override InputDeviceType DeviceType => InputDeviceType.Gyroscope;
    
    /// <summary>
    /// Angular velocity in degrees per second.
    /// X = Pitch (tilting forward/backward)
    /// Y = Yaw (turning left/right)
    /// Z = Roll (tilting side to side)
    /// </summary>
    public Vector3 AngularVelocity { get; set; }
    
    /// <summary>
    /// Previous frame's angular velocity.
    /// </summary>
    public Vector3 PreviousAngularVelocity { get; private set; }
    
    /// <summary>
    /// Acceleration in g-forces.
    /// </summary>
    public Vector3 Acceleration { get; internal set; }
    
    /// <summary>
    /// Gravity vector (direction of gravity relative to device).
    /// </summary>
    public Vector3 Gravity { get; internal set; } = new(0, -1, 0);
    
    /// <summary>
    /// Whether gyro input is currently enabled.
    /// </summary>
    public bool IsEnabled { get; set; } = true;
    
    /// <summary>
    /// Sensitivity multiplier for angular velocity.
    /// </summary>
    public float Sensitivity { get; set; } = 1.0f;
    
    /// <summary>
    /// Deadzone for angular velocity (degrees/second).
    /// Values below this are treated as zero.
    /// </summary>
    public float Deadzone { get; set; } = 5.0f;
    
    /// <summary>
    /// The gamepad this gyro is part of (if any).
    /// </summary>
    public GamepadDevice? ParentGamepad { get; internal set; }
    
    public GyroDevice()
    {
        Name = "Gyroscope";
    }
    
    public override void BeginFrame()
    {
        base.BeginFrame();
        PreviousAngularVelocity = AngularVelocity;
    }
    
    /// <summary>
    /// Get angular velocity with deadzone and sensitivity applied.
    /// </summary>
    public Vector3 GetProcessedAngularVelocity()
    {
        if (!IsEnabled)
            return Vector3.Zero;
            
        var vel = AngularVelocity;
        
        // Apply deadzone per-axis
        vel.X = ApplyDeadzone(vel.X);
        vel.Y = ApplyDeadzone(vel.Y);
        vel.Z = ApplyDeadzone(vel.Z);
        
        return vel * Sensitivity;
    }
    
    private float ApplyDeadzone(float value)
    {
        if (MathF.Abs(value) < Deadzone)
            return 0f;
        
        // Remap from deadzone to max
        var sign = MathF.Sign(value);
        var abs = MathF.Abs(value);
        return sign * (abs - Deadzone);
    }
    
    /// <summary>
    /// Get the pitch rate (rotation around X axis, tilting forward/backward).
    /// </summary>
    public float Pitch => GetProcessedAngularVelocity().X;
    
    /// <summary>
    /// Get the yaw rate (rotation around Y axis, turning left/right).
    /// </summary>
    public float Yaw => GetProcessedAngularVelocity().Y;
    
    /// <summary>
    /// Get the roll rate (rotation around Z axis, tilting side to side).
    /// </summary>
    public float Roll => GetProcessedAngularVelocity().Z;
    
    public override float ReadAxis(string control)
    {
        var processed = GetProcessedAngularVelocity();
        return control switch
        {
            "pitch" => processed.X,
            "yaw" => processed.Y,
            "roll" => processed.Z,
            _ => 0f
        };
    }
    
    public override Vector3 ReadVector3(string control)
    {
        return control switch
        {
            "angularVelocity" => GetProcessedAngularVelocity(),
            "acceleration" => Acceleration,
            "gravity" => Gravity,
            _ => Vector3.Zero
        };
    }
    
    /// <summary>
    /// Calibrate the gyro by setting the current orientation as "level".
    /// </summary>
    public void Calibrate()
    {
        // Store current gravity as reference
        // Actual implementation depends on provider
    }
    
    /// <summary>
    /// Reset gyro state.
    /// </summary>
    public void Reset()
    {
        AngularVelocity = Vector3.Zero;
        PreviousAngularVelocity = Vector3.Zero;
        Acceleration = Vector3.Zero;
    }
}

/// <summary>
/// Gyro usage mode for aiming assistance.
/// </summary>
public enum GyroAimMode
{
    /// <summary>
    /// Gyro is always active for aiming.
    /// </summary>
    AlwaysOn,
    
    /// <summary>
    /// Gyro activates when aiming down sights (ADS).
    /// </summary>
    OnAds,
    
    /// <summary>
    /// Gyro activates when touching the right stick.
    /// </summary>
    OnStickTouch,
    
    /// <summary>
    /// Gyro activates while holding a trigger or button.
    /// </summary>
    OnButtonHold,
    
    /// <summary>
    /// Gyro is disabled.
    /// </summary>
    Off
}
