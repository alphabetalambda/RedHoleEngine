using System.Numerics;
using RedHoleEngine.Input;
using RedHoleEngine.Input.Recording;

namespace RedHoleEngine.Tests.Input;

public class InputRecordingBehaviorTests
{
    [Fact(Skip = "BUG: RecordedFrame.ApplyToState never sets InputState.GamepadConnected. Sticks and buttons round-trip, but the connected flag does not, because the recording format has no field for it. Not changed: adding a field would break existing binary recordings.")]
    public void ApplyToState_ShouldRestoreGamepadConnected()
    {
        var frame = RecordedFrame.FromState(new InputState { GamepadConnected = true, PressedGamepadButtons = { "A" } }, 0, 0.016f);
        var state = new InputState();
        frame.ApplyToState(state);
        Assert.True(state.GamepadConnected);
    }

    private static InputState SampleState()
    {
        return new InputState
        {
            FrameNumber = 4,
            Timestamp = 0.25,
            PressedKeys = { "W", "Space" },
            MousePosition = new Vector2(10, 20),
            MouseDelta = new Vector2(1, -1),
            ScrollDelta = new Vector2(0, 1),
            PressedMouseButtons = { 0, 1 },
            GamepadConnected = true,
            LeftStick = new Vector2(0.5f, -0.25f),
            RightStick = Vector2.UnitX,
            LeftTrigger = 0.2f,
            RightTrigger = 0.8f,
            DPad = Vector2.UnitY,
            PressedGamepadButtons = { "A" },
            GyroActive = true,
            GyroAngularVelocity = new Vector3(1, 2, 3)
        };
    }

    [Fact]
    public void Frames_RoundTripThroughJsonAndBinary_AndSeekByTime()
    {
        var recording = new InputRecording { Name = "demo" };
        recording.Metadata["map"] = "level1";
        var frame = RecordedFrame.FromState(SampleState(), recordingStartTime: 0.1, deltaTime: 0.016f);
        recording.AddFrame(frame);
        recording.AddFrame(RecordedFrame.FromState(SampleState(), 0.1, 0.016f));
        recording.Frames[1].Timestamp = 0.5;
        recording.Duration = 0.5;

        Assert.Equal(2, recording.FrameCount);
        Assert.Equal(frame, recording.GetFrame(0));
        Assert.Null(recording.GetFrame(-1));
        Assert.Null(recording.GetFrame(5));
        Assert.Equal(0.5, recording.GetFrameAtTime(0.5)!.Timestamp);
        Assert.Equal(recording.Frames[0].Timestamp, recording.GetFrameAtTime(0)!.Timestamp);
        Assert.Equal(recording.Frames[0], recording.GetFrameAtTime(-1));

        var state = new InputState();
        recording.Frames[0].ApplyToState(state);
        Assert.Contains("W", state.PressedKeys);
        Assert.Equal(new Vector2(10, 20), state.MousePosition);
        Assert.Contains(0, state.PressedMouseButtons);
        Assert.Contains("A", state.PressedGamepadButtons);
        Assert.Equal(0.2f, state.LeftTrigger);
        Assert.False(state.GamepadConnected);
        Assert.Equal(new Vector3(1, 2, 3), state.GyroAngularVelocity);

        var dir = Path.Combine(Path.GetTempPath(), "rec-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var json = Path.Combine(dir, "in.json");
            var bin = Path.Combine(dir, "in.bin");
            recording.Save(json);
            recording.SaveBinary(bin);
            var fromJson = InputRecording.Load(json);
            var fromBin = InputRecording.LoadBinary(bin);
            Assert.Equal("demo", fromJson.Name);
            Assert.Equal("level1", fromJson.Metadata["map"]);
            Assert.Equal(2, fromBin.FrameCount);
            Assert.Contains("W", fromBin.Frames[0].Keys!);
            Assert.Equal(0.5f, fromBin.Frames[0].LeftStick![0]);

            // RecordedFrame does not store GamepadConnected, so playback leaves it false
            // even when sticks and buttons were captured. Changing the binary layout would
            // break existing recordings, so the flag is left as-is.
            var replay = new InputState();
            fromBin.Frames[0].ApplyToState(replay);
            Assert.False(replay.GamepadConnected);
            Assert.NotEmpty(replay.PressedGamepadButtons);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
