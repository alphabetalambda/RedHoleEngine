using System.Numerics;

namespace RedHoleEngine.Engine;

public class Camera
{
    public Vector3 Position { get; set; }
    public Vector3 Forward { get; private set; }
    public Vector3 Up { get; private set; }
    public Vector3 Right { get; private set; }

    private float _yaw;   // Rotation around Y axis (left/right)
    private float _pitch; // Rotation around X axis (up/down)

    public float MovementSpeed { get; set; } = 5.0f;
    public float MouseSensitivity { get; set; } = 0.1f;
    public float FieldOfView { get; set; } = 60.0f;

    public Camera(Vector3 position, float yaw = -90.0f, float pitch = 0.0f)
    {
        Position = position;
        _yaw = yaw;
        _pitch = pitch;
        UpdateVectors();
    }

    /// <summary>
    /// Rotate camera based on mouse movement
    /// </summary>
    public void Rotate(float deltaX, float deltaY)
    {
        _yaw += deltaX * MouseSensitivity;
        _pitch -= deltaY * MouseSensitivity;

        // Clamp pitch to avoid flipping
        _pitch = Math.Clamp(_pitch, -89.0f, 89.0f);

        UpdateVectors();
    }

    /// <summary>
    /// Move camera in world space
    /// </summary>
    public void Move(Vector3 direction, float deltaTime)
    {
        Position += direction * MovementSpeed * deltaTime;
    }

    /// <summary>
    /// Move forward/backward
    /// </summary>
    public void MoveForward(float deltaTime, bool forward)
    {
        float direction = forward ? 1.0f : -1.0f;
        Position += Forward * MovementSpeed * deltaTime * direction;
    }

    /// <summary>
    /// Strafe left/right
    /// </summary>
    public void MoveRight(float deltaTime, bool right)
    {
        float direction = right ? 1.0f : -1.0f;
        Position += Right * MovementSpeed * deltaTime * direction;
    }

    /// <summary>
    /// Move up/down
    /// </summary>
    public void MoveUp(float deltaTime, bool up)
    {
        float direction = up ? 1.0f : -1.0f;
        Position += Up * MovementSpeed * deltaTime * direction;
    }

    /// <summary>
    /// Get the view matrix for rendering
    /// </summary>
    public Matrix4x4 GetViewMatrix()
    {
        return Matrix4x4.CreateLookAt(Position, Position + Forward, Up);
    }

    /// <summary>
    /// Get the inverse view matrix (camera-to-world transform)
    /// </summary>
    public Matrix4x4 GetInverseViewMatrix()
    {
        Matrix4x4.Invert(GetViewMatrix(), out var inverse);
        return inverse;
    }

    /// <summary>
    /// Update direction vectors based on yaw and pitch
    /// </summary>
    private void UpdateVectors()
    {
        // Calculate forward vector from yaw and pitch
        float yawRad = MathF.PI / 180.0f * _yaw;
        float pitchRad = MathF.PI / 180.0f * _pitch;

        Forward = Vector3.Normalize(new Vector3(
            MathF.Cos(yawRad) * MathF.Cos(pitchRad),
            MathF.Sin(pitchRad),
            MathF.Sin(yawRad) * MathF.Cos(pitchRad)
        ));

        // Recalculate right and up vectors
        Right = Vector3.Normalize(Vector3.Cross(Forward, Vector3.UnitY));
        Up = Vector3.Normalize(Vector3.Cross(Right, Forward));
    }
}
