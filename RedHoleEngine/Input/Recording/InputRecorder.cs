namespace RedHoleEngine.Input.Recording;

/// <summary>
/// Records input for debugging and replay purposes.
/// This is a development feature and should not be used in release builds.
/// </summary>
public class InputRecorder
{
    private InputRecording? _currentRecording;
    private double _startTime;
    private bool _isRecording;
    
    /// <summary>
    /// Whether recording is currently active.
    /// </summary>
    public bool IsRecording => _isRecording;
    
    /// <summary>
    /// The current recording (null if not recording).
    /// </summary>
    public InputRecording? CurrentRecording => _currentRecording;
    
    /// <summary>
    /// Maximum frames to record (0 = unlimited).
    /// </summary>
    public int MaxFrames { get; set; } = 0;
    
    /// <summary>
    /// Maximum duration in seconds (0 = unlimited).
    /// </summary>
    public double MaxDuration { get; set; } = 0;
    
    /// <summary>
    /// Event fired when recording starts.
    /// </summary>
    public event Action? RecordingStarted;
    
    /// <summary>
    /// Event fired when recording stops.
    /// </summary>
    public event Action<InputRecording>? RecordingStopped;
    
    /// <summary>
    /// Start recording input.
    /// </summary>
    public void StartRecording(string name = "")
    {
        if (_isRecording)
        {
            Console.WriteLine("[InputRecorder] Already recording");
            return;
        }
        
        _currentRecording = new InputRecording
        {
            Name = string.IsNullOrEmpty(name) ? $"Recording_{DateTime.Now:yyyyMMdd_HHmmss}" : name
        };
        
        _startTime = Core.Time.TotalTime;
        _isRecording = true;
        
        Console.WriteLine($"[InputRecorder] Started recording: {_currentRecording.Name}");
        RecordingStarted?.Invoke();
    }
    
    /// <summary>
    /// Stop recording and return the recording.
    /// </summary>
    public InputRecording? StopRecording()
    {
        if (!_isRecording)
        {
            Console.WriteLine("[InputRecorder] Not recording");
            return null;
        }
        
        _isRecording = false;
        var recording = _currentRecording;
        _currentRecording = null;
        
        Console.WriteLine($"[InputRecorder] Stopped recording: {recording?.FrameCount} frames, {recording?.Duration:F2}s");
        
        if (recording != null)
        {
            RecordingStopped?.Invoke(recording);
        }
        
        return recording;
    }
    
    /// <summary>
    /// Record a frame of input.
    /// </summary>
    public void RecordFrame(InputState state, float deltaTime)
    {
        if (!_isRecording || _currentRecording == null)
            return;
        
        // Check limits
        if (MaxFrames > 0 && _currentRecording.FrameCount >= MaxFrames)
        {
            StopRecording();
            return;
        }
        
        if (MaxDuration > 0 && _currentRecording.Duration >= MaxDuration)
        {
            StopRecording();
            return;
        }
        
        var frame = RecordedFrame.FromState(state, _startTime, deltaTime);
        _currentRecording.AddFrame(frame);
    }
    
    /// <summary>
    /// Save the current recording to a file and stop.
    /// </summary>
    public void SaveAndStop(string filePath, bool binary = true)
    {
        var recording = StopRecording();
        if (recording == null) return;
        
        if (binary)
            recording.SaveBinary(filePath);
        else
            recording.Save(filePath);
        
        Console.WriteLine($"[InputRecorder] Saved to: {filePath}");
    }
}
