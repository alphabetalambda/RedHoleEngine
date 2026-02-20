using System.Numerics;
using RedHoleEngine.Input.Actions;
using RedHoleEngine.Input.Defaults;
using RedHoleEngine.Input.Devices;
using RedHoleEngine.Input.Haptics;
using RedHoleEngine.Input.Recording;
using RedHoleEngine.Input;
using Xunit;

namespace RedHoleEngine.Tests.Input;

public class InputActionTests
{
    [Fact]
    public void CreateButton_CreatesButtonAction()
    {
        var action = InputAction.CreateButton("Jump", "<Keyboard>/space", "<Gamepad>/buttonSouth");
        
        Assert.Equal("Jump", action.Name);
        Assert.Equal(InputActionType.Button, action.Type);
        Assert.Equal(2, action.Bindings.Count);
    }
    
    [Fact]
    public void CreateVector2_CreatesVector2Action()
    {
        var action = InputAction.CreateVector2("Move", "<Gamepad>/leftStick");
        
        Assert.Equal("Move", action.Name);
        Assert.Equal(InputActionType.Vector2, action.Type);
        Assert.Single(action.Bindings);
    }
    
    [Fact]
    public void CreateVector2Composite_CreatesWASDComposite()
    {
        var action = InputAction.CreateVector2Composite(
            "Move",
            "<Keyboard>/w", "<Keyboard>/s", "<Keyboard>/a", "<Keyboard>/d");
        
        Assert.Equal("Move", action.Name);
        Assert.Equal(InputActionType.Vector2, action.Type);
        Assert.Single(action.Composites);
        Assert.Equal(4, action.Composites[0].Parts.Count);
    }
    
    [Fact]
    public void ActionPhase_StartsInWaiting()
    {
        var action = InputAction.CreateButton("Test");
        Assert.Equal(InputActionPhase.Waiting, action.Phase);
    }
}

public class InputActionMapTests
{
    [Fact]
    public void Create_CreatesEmptyMap()
    {
        var map = InputActionMap.Create("Gameplay");
        
        Assert.Equal("Gameplay", map.Name);
        Assert.Empty(map.Actions);
        Assert.True(map.Enabled);
    }
    
    [Fact]
    public void AddButton_AddsButtonAction()
    {
        var map = InputActionMap.Create("Test");
        var action = map.AddButton("Jump", "<Keyboard>/space");
        
        Assert.Single(map.Actions);
        Assert.Equal("Jump", action.Name);
        Assert.Same(action, map["Jump"]);
    }
    
    [Fact]
    public void FindAction_FindsActionByName()
    {
        var map = InputActionMap.Create("Test");
        map.AddButton("Jump", "<Keyboard>/space");
        map.AddButton("Attack", "<Mouse>/leftButton");
        
        var found = map.FindAction("Jump");
        Assert.NotNull(found);
        Assert.Equal("Jump", found.Name);
    }
    
    [Fact]
    public void FindAction_IsCaseInsensitive()
    {
        var map = InputActionMap.Create("Test");
        map.AddButton("Jump", "<Keyboard>/space");
        
        var found = map.FindAction("JUMP");
        Assert.NotNull(found);
    }
    
    [Fact]
    public void Disable_DisablesAllActions()
    {
        var map = InputActionMap.Create("Test");
        map.AddButton("Jump", "<Keyboard>/space");
        map.AddButton("Attack", "<Mouse>/leftButton");
        
        map.Disable();
        
        Assert.False(map.Enabled);
        Assert.All(map.Actions, a => Assert.False(a.Enabled));
    }
}

public class InputActionAssetTests
{
    [Fact]
    public void Create_CreatesEmptyAsset()
    {
        var asset = InputActionAsset.Create("Test");
        
        Assert.Equal("Test", asset.Name);
        Assert.Empty(asset.ActionMaps);
    }
    
    [Fact]
    public void AddActionMap_AddsAndReturnsMap()
    {
        var asset = InputActionAsset.Create("Test");
        var map = asset.AddActionMap("Gameplay");
        
        Assert.Single(asset.ActionMaps);
        Assert.Same(map, asset["Gameplay"]);
    }
    
    [Fact]
    public void FindAction_FindsAcrossAllMaps()
    {
        var asset = InputActionAsset.Create("Test");
        var gameplay = asset.AddActionMap("Gameplay");
        var ui = asset.AddActionMap("UI");
        
        gameplay.AddButton("Jump", "<Keyboard>/space");
        ui.AddButton("Submit", "<Keyboard>/enter");
        
        Assert.NotNull(asset.FindAction("Jump"));
        Assert.NotNull(asset.FindAction("Submit"));
        Assert.NotNull(asset.FindAction("Gameplay/Jump"));
    }
    
    [Fact]
    public void ToJson_SerializesAndDeserializes()
    {
        var original = InputActionAsset.Create("Test");
        var map = original.AddActionMap("Gameplay");
        map.AddButton("Jump", "<Keyboard>/space");
        
        var json = original.ToJson();
        var restored = InputActionAsset.FromJson(json);
        
        Assert.Equal("Test", restored.Name);
        Assert.Single(restored.ActionMaps);
        Assert.NotNull(restored.FindAction("Jump"));
    }
}

public class InputBindingPathTests
{
    [Fact]
    public void Parse_ParsesGamepadButton()
    {
        var (device, control) = InputBindingPath.Parse("<Gamepad>/buttonSouth");
        
        Assert.Equal("Gamepad", device);
        Assert.Equal("buttonSouth", control);
    }
    
    [Fact]
    public void Parse_ParsesNestedControl()
    {
        var (device, control) = InputBindingPath.Parse("<Gamepad>/leftStick/x");
        
        Assert.Equal("Gamepad", device);
        Assert.Equal("leftStick/x", control);
    }
    
    [Fact]
    public void GetDevice_ReturnsDeviceType()
    {
        Assert.Equal("Keyboard", InputBindingPath.GetDevice("<Keyboard>/space"));
        Assert.Equal("Mouse", InputBindingPath.GetDevice("<Mouse>/leftButton"));
        Assert.Equal("Gamepad", InputBindingPath.GetDevice("<Gamepad>/buttonSouth"));
        Assert.Equal("Gyro", InputBindingPath.GetDevice("<Gyro>/angularVelocity"));
    }
    
    [Fact]
    public void IsDevice_MatchesCorrectDevice()
    {
        Assert.True(InputBindingPath.IsDevice("<Gamepad>/buttonSouth", "Gamepad"));
        Assert.False(InputBindingPath.IsDevice("<Keyboard>/space", "Gamepad"));
    }
}

public class DefaultActionsTests
{
    [Fact]
    public void CreateDefault_CreatesAllMaps()
    {
        var asset = DefaultActions.CreateDefault();
        
        Assert.NotNull(asset.FindActionMap("Gameplay"));
        Assert.NotNull(asset.FindActionMap("UI"));
        Assert.NotNull(asset.FindActionMap("Camera"));
    }
    
    [Fact]
    public void CreateDefault_HasCoreActions()
    {
        var asset = DefaultActions.CreateDefault();
        
        // Gameplay actions
        Assert.NotNull(asset.FindAction("Move"));
        Assert.NotNull(asset.FindAction("Look"));
        Assert.NotNull(asset.FindAction("Jump"));
        Assert.NotNull(asset.FindAction("Attack"));
        Assert.NotNull(asset.FindAction("Pause"));
        
        // UI actions
        Assert.NotNull(asset.FindAction("Navigate"));
        Assert.NotNull(asset.FindAction("Submit"));
        Assert.NotNull(asset.FindAction("Cancel"));
    }
    
    [Fact]
    public void CreateGameplayMap_HasMovementComposite()
    {
        var map = DefaultActions.CreateGameplayMap();
        var move = map.FindAction("Move");
        
        Assert.NotNull(move);
        Assert.Equal(InputActionType.Vector2, move.Type);
        Assert.NotEmpty(move.Composites);
    }
}

public class GamepadDeviceTests
{
    [Fact]
    public void ApplyStickDeadzone_RemovesSmallValues()
    {
        var gamepad = new GamepadDevice { StickDeadzone = 0.15f };
        
        var small = gamepad.ApplyStickDeadzone(new Vector2(0.1f, 0.1f));
        Assert.Equal(Vector2.Zero, small);
    }
    
    [Fact]
    public void ApplyStickDeadzone_RemapsLargeValues()
    {
        var gamepad = new GamepadDevice { StickDeadzone = 0.2f };
        
        var large = gamepad.ApplyStickDeadzone(new Vector2(1f, 0f));
        Assert.True(large.X > 0.9f);
    }
    
    [Fact]
    public void ApplyTriggerDeadzone_RemovesSmallValues()
    {
        var gamepad = new GamepadDevice { TriggerDeadzone = 0.1f };
        
        Assert.Equal(0f, gamepad.ApplyTriggerDeadzone(0.05f));
        Assert.True(gamepad.ApplyTriggerDeadzone(0.5f) > 0.4f);
    }
}

public class GyroDeviceTests
{
    [Fact]
    public void GetProcessedAngularVelocity_AppliesDeadzone()
    {
        var gyro = new GyroDevice { Deadzone = 5f, Sensitivity = 1f, IsEnabled = true };
        gyro.AngularVelocity = new Vector3(2f, 2f, 2f); // Below deadzone
        
        var processed = gyro.GetProcessedAngularVelocity();
        Assert.Equal(Vector3.Zero, processed);
    }
    
    [Fact]
    public void GetProcessedAngularVelocity_AppliesSensitivity()
    {
        var gyro = new GyroDevice { Deadzone = 0f, Sensitivity = 2f, IsEnabled = true };
        gyro.AngularVelocity = new Vector3(10f, 10f, 10f);
        
        var processed = gyro.GetProcessedAngularVelocity();
        Assert.Equal(20f, processed.X);
    }
    
    [Fact]
    public void GetProcessedAngularVelocity_ReturnsZeroWhenDisabled()
    {
        var gyro = new GyroDevice { IsEnabled = false };
        gyro.AngularVelocity = new Vector3(100f, 100f, 100f);
        
        Assert.Equal(Vector3.Zero, gyro.GetProcessedAngularVelocity());
    }
}

public class HapticFeedbackTests
{
    [Fact]
    public void Rumble_ClampsValues()
    {
        var feedback = HapticFeedback.Rumble(1.5f, -0.5f);
        
        Assert.Equal(1f, feedback.LeftMotor);
        Assert.Equal(0f, feedback.RightMotor);
    }
    
    [Fact]
    public void WithIntensity_ScalesAllValues()
    {
        var feedback = HapticFeedback.Rumble(1f, 1f).WithIntensity(0.5f);
        
        Assert.Equal(0.5f, feedback.LeftMotor);
        Assert.Equal(0.5f, feedback.RightMotor);
    }
    
    [Fact]
    public void Combine_TakesMaximumValues()
    {
        var a = HapticFeedback.Rumble(0.3f, 0.8f);
        var b = HapticFeedback.Rumble(0.7f, 0.2f);
        var combined = HapticFeedback.Combine(a, b);
        
        Assert.Equal(0.7f, combined.LeftMotor, precision: 2);
        Assert.Equal(0.8f, combined.RightMotor, precision: 2);
    }
}

public class HapticPatternTests
{
    [Fact]
    public void Sample_InterpolatesBetweenKeyframes()
    {
        var pattern = new HapticPattern();
        pattern.AddKeyframe(0f, HapticFeedback.None);
        pattern.AddKeyframe(1f, HapticFeedback.UniformRumble(1f));
        
        var sample = pattern.Sample(0.5f);
        Assert.True(sample.LeftMotor > 0.4f && sample.LeftMotor < 0.6f);
    }
    
    [Fact]
    public void Pulse_CreatesMultipleKeyframes()
    {
        var pattern = HapticPattern.Pulse(0.5f, 0.1f, 3, 0.1f);
        
        Assert.True(pattern.Keyframes.Count >= 6); // At least 2 keyframes per pulse
    }
}

public class InputRecordingTests
{
    [Fact]
    public void AddFrame_IncreasesDuration()
    {
        var recording = new InputRecording();
        recording.AddFrame(new RecordedFrame { Timestamp = 0.5 });
        recording.AddFrame(new RecordedFrame { Timestamp = 1.0 });
        
        Assert.Equal(1.0, recording.Duration);
        Assert.Equal(2, recording.FrameCount);
    }
    
    [Fact]
    public void GetFrameAtTime_FindsCorrectFrame()
    {
        var recording = new InputRecording();
        recording.AddFrame(new RecordedFrame { Timestamp = 0.0, FrameNumber = 0 });
        recording.AddFrame(new RecordedFrame { Timestamp = 0.5, FrameNumber = 1 });
        recording.AddFrame(new RecordedFrame { Timestamp = 1.0, FrameNumber = 2 });
        
        var frame = recording.GetFrameAtTime(0.7);
        Assert.NotNull(frame);
        Assert.Equal(1, frame.FrameNumber);
    }
}

public class InputStateTests
{
    [Fact]
    public void Clone_CreatesIndependentCopy()
    {
        var state = new InputState
        {
            MousePosition = new Vector2(100, 200),
            LeftStick = new Vector2(0.5f, 0.5f)
        };
        state.PressedKeys.Add("space");
        
        var clone = state.Clone();
        
        // Modify original
        state.MousePosition = Vector2.Zero;
        state.PressedKeys.Clear();
        
        // Clone should be unchanged
        Assert.Equal(new Vector2(100, 200), clone.MousePosition);
        Assert.Contains("space", clone.PressedKeys);
    }
}
