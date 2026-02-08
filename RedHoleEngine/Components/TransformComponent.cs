using System.Numerics;
using RedHoleEngine.Core.ECS;
using RedHoleEngine.Core.Scene;

namespace RedHoleEngine.Components;

/// <summary>
/// Component wrapper for Transform, allowing entities to have spatial data.
/// Use this when you need transform data through the ECS rather than scene graph.
/// </summary>
public struct TransformComponent : IComponent
{
    /// <summary>
    /// The underlying transform
    /// </summary>
    public Transform Transform;

    public TransformComponent()
    {
        Transform = new Transform();
    }

    public TransformComponent(Transform transform)
    {
        Transform = transform;
    }

    public TransformComponent(Vector3 position)
    {
        Transform = new Transform(position);
    }

    public TransformComponent(Vector3 position, Quaternion rotation)
    {
        Transform = new Transform(position, rotation);
    }

    public TransformComponent(Vector3 position, Quaternion rotation, Vector3 scale)
    {
        Transform = new Transform(position, rotation, scale);
    }

    // Convenience accessors
    public Vector3 Position
    {
        readonly get => Transform.Position;
        set => Transform.Position = value;
    }

    public Vector3 LocalPosition
    {
        readonly get => Transform.LocalPosition;
        set => Transform.LocalPosition = value;
    }

    public Quaternion Rotation
    {
        readonly get => Transform.Rotation;
        set => Transform.Rotation = value;
    }

    public Quaternion LocalRotation
    {
        readonly get => Transform.LocalRotation;
        set => Transform.LocalRotation = value;
    }

    public Vector3 LocalScale
    {
        readonly get => Transform.LocalScale;
        set => Transform.LocalScale = value;
    }

    public readonly Vector3 Forward => Transform.Forward;
    public readonly Vector3 Right => Transform.Right;
    public readonly Vector3 Up => Transform.Up;

    public readonly Matrix4x4 WorldMatrix => Transform.WorldMatrix;
    public readonly Matrix4x4 LocalMatrix => Transform.LocalMatrix;
}
