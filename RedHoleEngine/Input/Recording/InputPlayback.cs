namespace RedHoleEngine.Input.Recording;

/// <summary>
/// Plays back recorded input for debugging and testing.
/// </summary>
public class InputPlayback
{
    private InputRecording? _recording;
    private int _currentFrameIndex;
    private double _playbackTime;
    private bool _isPlaying;
    private bool _isPaused;
    private float _playbackSpeed = 1.0f;
    
    /// <summary>
    /// Whether playback is active.
    /// </summary>
    public bool IsPlaying => _isPlaying;
    
    /// <summary>
    /// Whether playback is paused.
    /// </summary>
    public bool IsPaused => _isPaused;
    
    /// <summary>
    /// Current playback time in seconds.
    /// </summary>
    public double PlaybackTime => _playbackTime;
    
    /// <summary>
    /// Current frame index.
    /// </summary>
    public int CurrentFrameIndex => _currentFrameIndex;
    
    /// <summary>
    /// Total number of frames.
    /// </summary>
    public int TotalFrames => _recording?.FrameCount ?? 0;
    
    /// <summary>
    /// Total duration of the recording.
    /// </summary>
    public double Duration => _recording?.Duration ?? 0;
    
    /// <summary>
    /// Playback speed multiplier (1.0 = normal speed).
    /// </summary>
    public float PlaybackSpeed
    {
        get => _playbackSpeed;
        set => _playbackSpeed = Math.Max(0.1f, Math.Min(10f, value));
    }
    
    /// <summary>
    /// Whether to loop the playback.
    /// </summary>
    public bool Loop { get; set; }
    
    /// <summary>
    /// The current recording being played back.
    /// </summary>
    public InputRecording? Recording => _recording;
    
    /// <summary>
    /// Event fired when playback starts.
    /// </summary>
    public event Action? PlaybackStarted;
    
    /// <summary>
    /// Event fired when playback stops.
    /// </summary>
    public event Action? PlaybackStopped;
    
    /// <summary>
    /// Event fired when playback completes (not stopped manually).
    /// </summary>
    public event Action? PlaybackCompleted;
    
    /// <summary>
    /// Load a recording for playback.
    /// </summary>
    public void Load(InputRecording recording)
    {
        _recording = recording;
        Reset();
        Console.WriteLine($"[InputPlayback] Loaded: {recording.Name} ({recording.FrameCount} frames)");
    }
    
    /// <summary>
    /// Load a recording from file.
    /// </summary>
    public void Load(string filePath, bool binary = true)
    {
        var recording = binary 
            ? InputRecording.LoadBinary(filePath)
            : InputRecording.Load(filePath);
        Load(recording);
    }
    
    /// <summary>
    /// Start or resume playback.
    /// </summary>
    public void Play()
    {
        if (_recording == null)
        {
            Console.WriteLine("[InputPlayback] No recording loaded");
            return;
        }
        
        if (_isPlaying && !_isPaused)
        {
            Console.WriteLine("[InputPlayback] Already playing");
            return;
        }
        
        _isPlaying = true;
        _isPaused = false;
        
        Console.WriteLine($"[InputPlayback] Playing: {_recording.Name}");
        PlaybackStarted?.Invoke();
    }
    
    /// <summary>
    /// Pause playback.
    /// </summary>
    public void Pause()
    {
        if (!_isPlaying || _isPaused) return;
        _isPaused = true;
        Console.WriteLine("[InputPlayback] Paused");
    }
    
    /// <summary>
    /// Stop playback.
    /// </summary>
    public void Stop()
    {
        if (!_isPlaying) return;
        
        _isPlaying = false;
        _isPaused = false;
        Reset();
        
        Console.WriteLine("[InputPlayback] Stopped");
        PlaybackStopped?.Invoke();
    }
    
    /// <summary>
    /// Reset playback to the beginning.
    /// </summary>
    public void Reset()
    {
        _currentFrameIndex = 0;
        _playbackTime = 0;
    }
    
    /// <summary>
    /// Seek to a specific time.
    /// </summary>
    public void Seek(double time)
    {
        _playbackTime = Math.Clamp(time, 0, Duration);
        
        // Find the frame at this time
        if (_recording == null) return;
        
        for (int i = 0; i < _recording.FrameCount; i++)
        {
            var frame = _recording.GetFrame(i);
            if (frame != null && frame.Timestamp >= _playbackTime)
            {
                _currentFrameIndex = i;
                break;
            }
        }
    }
    
    /// <summary>
    /// Seek to a specific frame.
    /// </summary>
    public void SeekFrame(int frameIndex)
    {
        if (_recording == null) return;
        
        _currentFrameIndex = Math.Clamp(frameIndex, 0, _recording.FrameCount - 1);
        var frame = _recording.GetFrame(_currentFrameIndex);
        if (frame != null)
        {
            _playbackTime = frame.Timestamp;
        }
    }
    
    /// <summary>
    /// Step forward one frame.
    /// </summary>
    public void StepForward()
    {
        if (_recording == null || _currentFrameIndex >= _recording.FrameCount - 1)
            return;
        
        _currentFrameIndex++;
        var frame = _recording.GetFrame(_currentFrameIndex);
        if (frame != null)
        {
            _playbackTime = frame.Timestamp;
        }
    }
    
    /// <summary>
    /// Step backward one frame.
    /// </summary>
    public void StepBackward()
    {
        if (_recording == null || _currentFrameIndex <= 0)
            return;
        
        _currentFrameIndex--;
        var frame = _recording.GetFrame(_currentFrameIndex);
        if (frame != null)
        {
            _playbackTime = frame.Timestamp;
        }
    }
    
    /// <summary>
    /// Update playback and apply to input state.
    /// Returns true if there is a frame to apply.
    /// </summary>
    public bool Update(float deltaTime, InputState state)
    {
        if (!_isPlaying || _isPaused || _recording == null)
            return false;
        
        // Advance time
        _playbackTime += deltaTime * _playbackSpeed;
        
        // Check if we've reached the end
        if (_playbackTime >= Duration)
        {
            if (Loop)
            {
                Reset();
            }
            else
            {
                _isPlaying = false;
                Console.WriteLine("[InputPlayback] Completed");
                PlaybackCompleted?.Invoke();
                return false;
            }
        }
        
        // Find and apply the current frame
        var frame = _recording.GetFrameAtTime(_playbackTime);
        if (frame != null)
        {
            frame.ApplyToState(state);
            
            // Update frame index
            for (int i = _currentFrameIndex; i < _recording.FrameCount; i++)
            {
                var f = _recording.GetFrame(i);
                if (f != null && f.Timestamp >= _playbackTime)
                {
                    _currentFrameIndex = i;
                    break;
                }
            }
            
            return true;
        }
        
        return false;
    }
    
    /// <summary>
    /// Get the current frame without advancing time.
    /// </summary>
    public RecordedFrame? GetCurrentFrame()
    {
        return _recording?.GetFrame(_currentFrameIndex);
    }
}
