using System.Text.Json;
using System.Text.Json.Serialization;

namespace RedHoleEngine.Input.Recording;

/// <summary>
/// A recorded sequence of input states that can be played back.
/// </summary>
public class InputRecording
{
    /// <summary>
    /// Name/identifier for this recording.
    /// </summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";
    
    /// <summary>
    /// When the recording was created.
    /// </summary>
    [JsonPropertyName("createdAt")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    
    /// <summary>
    /// Total duration in seconds.
    /// </summary>
    [JsonPropertyName("duration")]
    public double Duration { get; set; }
    
    /// <summary>
    /// The recorded frames.
    /// </summary>
    [JsonPropertyName("frames")]
    public List<RecordedFrame> Frames { get; set; } = new();
    
    /// <summary>
    /// Metadata about the recording (game version, scene, etc.).
    /// </summary>
    [JsonPropertyName("metadata")]
    public Dictionary<string, string> Metadata { get; set; } = new();
    
    /// <summary>
    /// Total number of frames.
    /// </summary>
    [JsonIgnore]
    public int FrameCount => Frames.Count;
    
    /// <summary>
    /// Add a frame to the recording.
    /// </summary>
    public void AddFrame(RecordedFrame frame)
    {
        Frames.Add(frame);
        Duration = frame.Timestamp;
    }
    
    /// <summary>
    /// Get frame at a specific time.
    /// </summary>
    public RecordedFrame? GetFrameAtTime(double time)
    {
        if (Frames.Count == 0) return null;
        
        // Binary search for the closest frame
        int left = 0;
        int right = Frames.Count - 1;
        
        while (left < right)
        {
            int mid = (left + right + 1) / 2;
            if (Frames[mid].Timestamp <= time)
                left = mid;
            else
                right = mid - 1;
        }
        
        return Frames[left];
    }
    
    /// <summary>
    /// Get frame by index.
    /// </summary>
    public RecordedFrame? GetFrame(int index)
    {
        if (index < 0 || index >= Frames.Count)
            return null;
        return Frames[index];
    }
    
    // ═══════════════════════════════════════════════════════════════════════════
    // SERIALIZATION
    // ═══════════════════════════════════════════════════════════════════════════
    
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false, // Recordings can be large, don't waste space
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };
    
    /// <summary>
    /// Save recording to a file.
    /// </summary>
    public void Save(string filePath)
    {
        var json = JsonSerializer.Serialize(this, JsonOptions);
        File.WriteAllText(filePath, json);
    }
    
    /// <summary>
    /// Load recording from a file.
    /// </summary>
    public static InputRecording Load(string filePath)
    {
        var json = File.ReadAllText(filePath);
        return JsonSerializer.Deserialize<InputRecording>(json, JsonOptions)
            ?? throw new InvalidOperationException($"Failed to load recording from {filePath}");
    }
    
    /// <summary>
    /// Save recording in binary format (smaller file size).
    /// </summary>
    public void SaveBinary(string filePath)
    {
        using var stream = File.Create(filePath);
        using var writer = new BinaryWriter(stream);
        
        // Header
        writer.Write("RHINP"); // Magic bytes
        writer.Write(1); // Version
        writer.Write(Name);
        writer.Write(CreatedAt.ToBinary());
        writer.Write(Duration);
        writer.Write(Frames.Count);
        
        // Frames
        foreach (var frame in Frames)
        {
            frame.WriteBinary(writer);
        }
    }
    
    /// <summary>
    /// Load recording from binary format.
    /// </summary>
    public static InputRecording LoadBinary(string filePath)
    {
        using var stream = File.OpenRead(filePath);
        using var reader = new BinaryReader(stream);
        
        // Header
        var magic = reader.ReadString();
        if (magic != "RHINP")
            throw new InvalidOperationException("Invalid recording file format");
        
        var version = reader.ReadInt32();
        if (version != 1)
            throw new InvalidOperationException($"Unsupported recording version: {version}");
        
        var recording = new InputRecording
        {
            Name = reader.ReadString(),
            CreatedAt = DateTime.FromBinary(reader.ReadInt64()),
            Duration = reader.ReadDouble()
        };
        
        var frameCount = reader.ReadInt32();
        for (int i = 0; i < frameCount; i++)
        {
            recording.Frames.Add(RecordedFrame.ReadBinary(reader));
        }
        
        return recording;
    }
}

/// <summary>
/// A single frame of recorded input.
/// </summary>
public class RecordedFrame
{
    /// <summary>
    /// Frame number.
    /// </summary>
    [JsonPropertyName("frame")]
    public long FrameNumber { get; set; }
    
    /// <summary>
    /// Time in seconds since recording started.
    /// </summary>
    [JsonPropertyName("t")]
    public double Timestamp { get; set; }
    
    /// <summary>
    /// Delta time for this frame.
    /// </summary>
    [JsonPropertyName("dt")]
    public float DeltaTime { get; set; }
    
    /// <summary>
    /// Pressed keys (compact format).
    /// </summary>
    [JsonPropertyName("keys")]
    public List<string>? Keys { get; set; }
    
    /// <summary>
    /// Mouse position.
    /// </summary>
    [JsonPropertyName("mpos")]
    public float[]? MousePosition { get; set; }
    
    /// <summary>
    /// Mouse delta.
    /// </summary>
    [JsonPropertyName("mdelta")]
    public float[]? MouseDelta { get; set; }
    
    /// <summary>
    /// Mouse buttons (bitmask).
    /// </summary>
    [JsonPropertyName("mbtns")]
    public int MouseButtons { get; set; }
    
    /// <summary>
    /// Left stick position.
    /// </summary>
    [JsonPropertyName("lstick")]
    public float[]? LeftStick { get; set; }
    
    /// <summary>
    /// Right stick position.
    /// </summary>
    [JsonPropertyName("rstick")]
    public float[]? RightStick { get; set; }
    
    /// <summary>
    /// Triggers [left, right].
    /// </summary>
    [JsonPropertyName("triggers")]
    public float[]? Triggers { get; set; }
    
    /// <summary>
    /// Gamepad buttons (compact format).
    /// </summary>
    [JsonPropertyName("gbtns")]
    public List<string>? GamepadButtons { get; set; }
    
    /// <summary>
    /// Gyro angular velocity [pitch, yaw, roll].
    /// </summary>
    [JsonPropertyName("gyro")]
    public float[]? Gyro { get; set; }
    
    /// <summary>
    /// Create frame from input state.
    /// </summary>
    public static RecordedFrame FromState(InputState state, double recordingStartTime, float deltaTime)
    {
        var frame = new RecordedFrame
        {
            FrameNumber = state.FrameNumber,
            Timestamp = state.Timestamp - recordingStartTime,
            DeltaTime = deltaTime
        };
        
        // Keys
        if (state.PressedKeys.Count > 0)
            frame.Keys = state.PressedKeys.ToList();
        
        // Mouse
        if (state.MousePosition != System.Numerics.Vector2.Zero)
            frame.MousePosition = new[] { state.MousePosition.X, state.MousePosition.Y };
        
        if (state.MouseDelta != System.Numerics.Vector2.Zero)
            frame.MouseDelta = new[] { state.MouseDelta.X, state.MouseDelta.Y };
        
        foreach (var btn in state.PressedMouseButtons)
            frame.MouseButtons |= (1 << btn);
        
        // Gamepad
        if (state.GamepadConnected)
        {
            if (state.LeftStick.Length() > 0.01f)
                frame.LeftStick = new[] { state.LeftStick.X, state.LeftStick.Y };
            
            if (state.RightStick.Length() > 0.01f)
                frame.RightStick = new[] { state.RightStick.X, state.RightStick.Y };
            
            if (state.LeftTrigger > 0.01f || state.RightTrigger > 0.01f)
                frame.Triggers = new[] { state.LeftTrigger, state.RightTrigger };
            
            if (state.PressedGamepadButtons.Count > 0)
                frame.GamepadButtons = state.PressedGamepadButtons.ToList();
        }
        
        // Gyro
        if (state.GyroActive && state.GyroAngularVelocity.Length() > 0.01f)
        {
            frame.Gyro = new[] 
            { 
                state.GyroAngularVelocity.X, 
                state.GyroAngularVelocity.Y, 
                state.GyroAngularVelocity.Z 
            };
        }
        
        return frame;
    }
    
    /// <summary>
    /// Apply this frame to an input state.
    /// </summary>
    public void ApplyToState(InputState state)
    {
        state.FrameNumber = FrameNumber;
        state.Timestamp = Timestamp;
        
        // Keys
        state.PressedKeys.Clear();
        if (Keys != null)
        {
            foreach (var key in Keys)
                state.PressedKeys.Add(key);
        }
        
        // Mouse
        if (MousePosition != null)
            state.MousePosition = new System.Numerics.Vector2(MousePosition[0], MousePosition[1]);
        
        if (MouseDelta != null)
            state.MouseDelta = new System.Numerics.Vector2(MouseDelta[0], MouseDelta[1]);
        
        state.PressedMouseButtons.Clear();
        for (int i = 0; i < 5; i++)
        {
            if ((MouseButtons & (1 << i)) != 0)
                state.PressedMouseButtons.Add(i);
        }
        
        // Gamepad
        if (LeftStick != null)
            state.LeftStick = new System.Numerics.Vector2(LeftStick[0], LeftStick[1]);
        
        if (RightStick != null)
            state.RightStick = new System.Numerics.Vector2(RightStick[0], RightStick[1]);
        
        if (Triggers != null)
        {
            state.LeftTrigger = Triggers[0];
            state.RightTrigger = Triggers[1];
        }
        
        state.PressedGamepadButtons.Clear();
        if (GamepadButtons != null)
        {
            foreach (var btn in GamepadButtons)
                state.PressedGamepadButtons.Add(btn);
        }
        
        // Gyro
        if (Gyro != null)
        {
            state.GyroAngularVelocity = new System.Numerics.Vector3(Gyro[0], Gyro[1], Gyro[2]);
            state.GyroActive = true;
        }
    }
    
    internal void WriteBinary(BinaryWriter writer)
    {
        writer.Write(FrameNumber);
        writer.Write(Timestamp);
        writer.Write(DeltaTime);
        
        // Keys
        writer.Write(Keys?.Count ?? 0);
        if (Keys != null)
        {
            foreach (var key in Keys)
                writer.Write(key);
        }
        
        // Mouse
        writer.Write(MousePosition != null);
        if (MousePosition != null)
        {
            writer.Write(MousePosition[0]);
            writer.Write(MousePosition[1]);
        }
        
        writer.Write(MouseDelta != null);
        if (MouseDelta != null)
        {
            writer.Write(MouseDelta[0]);
            writer.Write(MouseDelta[1]);
        }
        
        writer.Write(MouseButtons);
        
        // Gamepad
        writer.Write(LeftStick != null);
        if (LeftStick != null)
        {
            writer.Write(LeftStick[0]);
            writer.Write(LeftStick[1]);
        }
        
        writer.Write(RightStick != null);
        if (RightStick != null)
        {
            writer.Write(RightStick[0]);
            writer.Write(RightStick[1]);
        }
        
        writer.Write(Triggers != null);
        if (Triggers != null)
        {
            writer.Write(Triggers[0]);
            writer.Write(Triggers[1]);
        }
        
        writer.Write(GamepadButtons?.Count ?? 0);
        if (GamepadButtons != null)
        {
            foreach (var btn in GamepadButtons)
                writer.Write(btn);
        }
        
        // Gyro
        writer.Write(Gyro != null);
        if (Gyro != null)
        {
            writer.Write(Gyro[0]);
            writer.Write(Gyro[1]);
            writer.Write(Gyro[2]);
        }
    }
    
    internal static RecordedFrame ReadBinary(BinaryReader reader)
    {
        var frame = new RecordedFrame
        {
            FrameNumber = reader.ReadInt64(),
            Timestamp = reader.ReadDouble(),
            DeltaTime = reader.ReadSingle()
        };
        
        // Keys
        var keyCount = reader.ReadInt32();
        if (keyCount > 0)
        {
            frame.Keys = new List<string>(keyCount);
            for (int i = 0; i < keyCount; i++)
                frame.Keys.Add(reader.ReadString());
        }
        
        // Mouse
        if (reader.ReadBoolean())
            frame.MousePosition = new[] { reader.ReadSingle(), reader.ReadSingle() };
        
        if (reader.ReadBoolean())
            frame.MouseDelta = new[] { reader.ReadSingle(), reader.ReadSingle() };
        
        frame.MouseButtons = reader.ReadInt32();
        
        // Gamepad
        if (reader.ReadBoolean())
            frame.LeftStick = new[] { reader.ReadSingle(), reader.ReadSingle() };
        
        if (reader.ReadBoolean())
            frame.RightStick = new[] { reader.ReadSingle(), reader.ReadSingle() };
        
        if (reader.ReadBoolean())
            frame.Triggers = new[] { reader.ReadSingle(), reader.ReadSingle() };
        
        var btnCount = reader.ReadInt32();
        if (btnCount > 0)
        {
            frame.GamepadButtons = new List<string>(btnCount);
            for (int i = 0; i < btnCount; i++)
                frame.GamepadButtons.Add(reader.ReadString());
        }
        
        // Gyro
        if (reader.ReadBoolean())
            frame.Gyro = new[] { reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle() };
        
        return frame;
    }
}
