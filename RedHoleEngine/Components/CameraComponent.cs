using System.Numerics;
using RedHoleEngine.Core.ECS;

namespace RedHoleEngine.Components;

/// <summary>
/// Camera projection types
/// </summary>
public enum ProjectionType
{
    Perspective,
    Orthographic
}

/// <summary>
/// Component that makes an entity a camera.
/// Works with TransformComponent for position/rotation.
/// </summary>
public struct CameraComponent : IComponent
{
    /// <summary>
    /// Field of view in degrees (for perspective projection)
    /// </summary>
    public float FieldOfView;
    
    /// <summary>
    /// Near clipping plane distance
    /// </summary>
    public float NearPlane;
    
    /// <summary>
    /// Far clipping plane distance
    /// </summary>
    public float FarPlane;
    
    /// <summary>
    /// Aspect ratio (width / height)
    /// </summary>
    public float AspectRatio;
    
    /// <summary>
    /// Projection type
    /// </summary>
    public ProjectionType ProjectionType;
    
    /// <summary>
    /// Orthographic size (half-height of the view in world units)
    /// </summary>
    public float OrthographicSize;
    
    /// <summary>
    /// Render priority (lower = renders first, for multiple cameras)
    /// </summary>
    public int Priority;
    
    /// <summary>
    /// Whether this camera is active
    /// </summary>
    public bool IsActive;

    /// <summary>
    /// Create a perspective camera
    /// </summary>
    public static CameraComponent CreatePerspective(
        float fov = 60f, 
        float aspectRatio = 16f / 9f,
        float nearPlane = 0.1f, 
        float farPlane = 1000f)
    {
        return new CameraComponent
        {
            FieldOfView = fov,
            AspectRatio = aspectRatio,
            NearPlane = nearPlane,
            FarPlane = farPlane,
            ProjectionType = ProjectionType.Perspective,
            OrthographicSize = 5f,
            Priority = 0,
            IsActive = true
        };
    }

    /// <summary>
    /// Create an orthographic camera
    /// </summary>
    public static CameraComponent CreateOrthographic(
        float size = 5f,
        float aspectRatio = 16f / 9f,
        float nearPlane = 0.1f,
        float farPlane = 1000f)
    {
        return new CameraComponent
        {
            FieldOfView = 60f,
            AspectRatio = aspectRatio,
            NearPlane = nearPlane,
            FarPlane = farPlane,
            ProjectionType = ProjectionType.Orthographic,
            OrthographicSize = size,
            Priority = 0,
            IsActive = true
        };
    }

    /// <summary>
    /// Get the projection matrix
    /// </summary>
    public readonly Matrix4x4 GetProjectionMatrix()
    {
        return ProjectionType switch
        {
            ProjectionType.Perspective => Matrix4x4.CreatePerspectiveFieldOfView(
                FieldOfView * MathF.PI / 180f,
                AspectRatio,
                NearPlane,
                FarPlane),
            
            ProjectionType.Orthographic => Matrix4x4.CreateOrthographic(
                OrthographicSize * 2 * AspectRatio,
                OrthographicSize * 2,
                NearPlane,
                FarPlane),
            
            _ => Matrix4x4.Identity
        };
    }

    /// <summary>
    /// Get the view matrix from a transform
    /// </summary>
    public static Matrix4x4 GetViewMatrix(in TransformComponent transform)
    {
        var t = transform.Transform;
        return Matrix4x4.CreateLookAt(
            t.Position,
            t.Position + t.Forward,
            t.Up);
    }

    /// <summary>
    /// Get the view-projection matrix
    /// </summary>
    public readonly Matrix4x4 GetViewProjectionMatrix(in TransformComponent transform)
    {
        return GetViewMatrix(transform) * GetProjectionMatrix();
    }
}
