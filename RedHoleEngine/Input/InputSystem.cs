using RedHoleEngine.Components;
using RedHoleEngine.Core.ECS;
using RedHoleEngine.Input.Actions;

namespace RedHoleEngine.Input;

/// <summary>
/// ECS system that updates input receiver components with current input state.
/// </summary>
public class InputSystem : GameSystem
{
    private InputManager? _inputManager;
    
    /// <summary>
    /// The input manager used by this system.
    /// </summary>
    public InputManager? InputManager
    {
        get => _inputManager;
        set => _inputManager = value;
    }
    
    /// <summary>
    /// Set the input manager.
    /// </summary>
    public void SetInputManager(InputManager manager)
    {
        _inputManager = manager;
    }
    
    public override void Update(float deltaTime)
    {
        if (_inputManager == null) return;
        
        // Update the input manager first
        _inputManager.Update(deltaTime);
        
        // Update all input receiver components
        foreach (var entity in World.Query<InputReceiverComponent>())
        {
            ref var receiver = ref World.GetComponent<InputReceiverComponent>(entity);
            UpdateInputReceiver(ref receiver);
        }
    }
    
    /// <summary>
    /// End-of-frame processing.
    /// </summary>
    public void EndFrame()
    {
        _inputManager?.EndFrame();
    }
    
    private void UpdateInputReceiver(ref InputReceiverComponent receiver)
    {
        if (_inputManager == null) return;
        
        var actionAsset = _inputManager.ActionAsset;
        if (actionAsset == null) return;
        
        // Find the action map for this receiver
        var actionMap = actionAsset.FindActionMap(receiver.ActionMapName);
        if (actionMap == null || !actionMap.Enabled) return;
        
        // Update cached action values
        foreach (var action in actionMap.Actions)
        {
            if (!action.Enabled) continue;
            
            var key = action.Name;
            
            switch (action.Type)
            {
                case InputActionType.Button:
                    receiver.ButtonStates[key] = action.IsPressed;
                    receiver.ButtonPressed[key] = action.WasPressedThisFrame;
                    receiver.ButtonReleased[key] = action.WasReleasedThisFrame;
                    break;
                    
                case InputActionType.Axis:
                    receiver.AxisValues[key] = action.ValueFloat;
                    break;
                    
                case InputActionType.Vector2:
                    receiver.Vector2Values[key] = action.ValueVector2;
                    break;
                    
                case InputActionType.Vector3:
                    receiver.Vector3Values[key] = action.ValueVector3;
                    break;
            }
        }
        
        // Update convenience properties
        if (receiver.Vector2Values.TryGetValue("Move", out var move))
            receiver.MoveInput = move;
        
        if (receiver.Vector2Values.TryGetValue("Look", out var look))
            receiver.LookInput = look;
        
        if (receiver.Vector3Values.TryGetValue("GyroLook", out var gyro))
            receiver.GyroInput = gyro;
    }
}
