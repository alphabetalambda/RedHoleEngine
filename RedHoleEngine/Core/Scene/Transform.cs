using System.Numerics;
using System.Runtime.CompilerServices;

namespace RedHoleEngine.Core.Scene;

/// <summary>
/// Represents a 3D transformation with position, rotation, and scale.
/// Supports hierarchical transforms (parent-child relationships).
/// </summary>
public class Transform
{
    private Vector3 _localPosition;
    private Quaternion _localRotation;
    private Vector3 _localScale;
    
    private Matrix4x4 _localMatrix;
    private Matrix4x4 _worldMatrix;
    
    private bool _localDirty = true;
    private bool _worldDirty = true;
    
    private Transform? _parent;
    private readonly List<Transform> _children = new();

    #region Properties

    /// <summary>
    /// Position relative to parent (or world if no parent)
    /// </summary>
    public Vector3 LocalPosition
    {
        get => _localPosition;
        set
        {
            _localPosition = value;
            MarkDirty();
        }
    }

    /// <summary>
    /// Rotation relative to parent (or world if no parent)
    /// </summary>
    public Quaternion LocalRotation
    {
        get => _localRotation;
        set
        {
            _localRotation = Quaternion.Normalize(value);
            MarkDirty();
        }
    }

    /// <summary>
    /// Scale relative to parent
    /// </summary>
    public Vector3 LocalScale
    {
        get => _localScale;
        set
        {
            _localScale = value;
            MarkDirty();
        }
    }

    /// <summary>
    /// World-space position
    /// </summary>
    public Vector3 Position
    {
        get
        {
            UpdateWorldMatrix();
            return new Vector3(_worldMatrix.M41, _worldMatrix.M42, _worldMatrix.M43);
        }
        set
        {
            if (_parent != null)
            {
                // Convert world position to local
                Matrix4x4.Invert(_parent.WorldMatrix, out var parentInverse);
                var localPos = Vector3.Transform(value, parentInverse);
                LocalPosition = localPos;
            }
            else
            {
                LocalPosition = value;
            }
        }
    }

    /// <summary>
    /// World-space rotation
    /// </summary>
    public Quaternion Rotation
    {
        get
        {
            if (_parent != null)
            {
                return _parent.Rotation * _localRotation;
            }
            return _localRotation;
        }
        set
        {
            if (_parent != null)
            {
                LocalRotation = Quaternion.Inverse(_parent.Rotation) * value;
            }
            else
            {
                LocalRotation = value;
            }
        }
    }

    /// <summary>
    /// Local-to-world transformation matrix
    /// </summary>
    public Matrix4x4 WorldMatrix
    {
        get
        {
            UpdateWorldMatrix();
            return _worldMatrix;
        }
    }

    /// <summary>
    /// Local transformation matrix
    /// </summary>
    public Matrix4x4 LocalMatrix
    {
        get
        {
            UpdateLocalMatrix();
            return _localMatrix;
        }
    }

    /// <summary>
    /// Parent transform (null if root)
    /// </summary>
    public Transform? Parent
    {
        get => _parent;
        set => SetParent(value);
    }

    /// <summary>
    /// Child transforms
    /// </summary>
    public IReadOnlyList<Transform> Children => _children;

    #endregion

    #region Direction Vectors

    /// <summary>
    /// Forward direction in world space (negative Z by convention)
    /// </summary>
    public Vector3 Forward
    {
        get
        {
            UpdateWorldMatrix();
            return Vector3.Normalize(new Vector3(-_worldMatrix.M31, -_worldMatrix.M32, -_worldMatrix.M33));
        }
    }

    /// <summary>
    /// Right direction in world space
    /// </summary>
    public Vector3 Right
    {
        get
        {
            UpdateWorldMatrix();
            return Vector3.Normalize(new Vector3(_worldMatrix.M11, _worldMatrix.M12, _worldMatrix.M13));
        }
    }

    /// <summary>
    /// Up direction in world space
    /// </summary>
    public Vector3 Up
    {
        get
        {
            UpdateWorldMatrix();
            return Vector3.Normalize(new Vector3(_worldMatrix.M21, _worldMatrix.M22, _worldMatrix.M23));
        }
    }

    #endregion

    public Transform()
    {
        _localPosition = Vector3.Zero;
        _localRotation = Quaternion.Identity;
        _localScale = Vector3.One;
    }

    public Transform(Vector3 position) : this()
    {
        _localPosition = position;
    }

    public Transform(Vector3 position, Quaternion rotation) : this()
    {
        _localPosition = position;
        _localRotation = rotation;
    }

    public Transform(Vector3 position, Quaternion rotation, Vector3 scale) : this()
    {
        _localPosition = position;
        _localRotation = rotation;
        _localScale = scale;
    }

    #region Hierarchy

    public void SetParent(Transform? newParent, bool worldPositionStays = true)
    {
        if (_parent == newParent)
            return;

        // Store world transform if needed
        Vector3 worldPos = Position;
        Quaternion worldRot = Rotation;

        // Remove from old parent
        _parent?._children.Remove(this);

        // Set new parent
        _parent = newParent;
        _parent?._children.Add(this);

        // Restore world transform if requested
        if (worldPositionStays && _parent != null)
        {
            Position = worldPos;
            Rotation = worldRot;
        }

        MarkDirty();
    }

    public void AddChild(Transform child)
    {
        child.SetParent(this);
    }

    public void RemoveChild(Transform child)
    {
        if (child._parent == this)
        {
            child.SetParent(null);
        }
    }

    #endregion

    #region Transformations

    /// <summary>
    /// Rotate around an axis (in local space)
    /// </summary>
    public void Rotate(Vector3 axis, float angleDegrees)
    {
        LocalRotation = Quaternion.CreateFromAxisAngle(Vector3.Normalize(axis), MathF.PI / 180f * angleDegrees) * _localRotation;
    }

    /// <summary>
    /// Rotate using Euler angles (in degrees)
    /// </summary>
    public void Rotate(float pitchDegrees, float yawDegrees, float rollDegrees)
    {
        var euler = Quaternion.CreateFromYawPitchRoll(
            MathF.PI / 180f * yawDegrees,
            MathF.PI / 180f * pitchDegrees,
            MathF.PI / 180f * rollDegrees
        );
        LocalRotation = euler * _localRotation;
    }

    /// <summary>
    /// Translate in local space
    /// </summary>
    public void Translate(Vector3 translation)
    {
        LocalPosition += Vector3.Transform(translation, _localRotation);
    }

    /// <summary>
    /// Translate in world space
    /// </summary>
    public void TranslateWorld(Vector3 translation)
    {
        Position += translation;
    }

    /// <summary>
    /// Look at a target position
    /// </summary>
    public void LookAt(Vector3 target, Vector3 up)
    {
        var lookMatrix = Matrix4x4.CreateLookAt(Position, target, up);
        Matrix4x4.Invert(lookMatrix, out var invLook);
        LocalRotation = Quaternion.CreateFromRotationMatrix(invLook);
    }

    /// <summary>
    /// Transform a point from local to world space
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Vector3 TransformPoint(Vector3 localPoint)
    {
        return Vector3.Transform(localPoint, WorldMatrix);
    }

    /// <summary>
    /// Transform a direction from local to world space (ignores translation)
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Vector3 TransformDirection(Vector3 localDirection)
    {
        return Vector3.TransformNormal(localDirection, WorldMatrix);
    }

    /// <summary>
    /// Transform a point from world to local space
    /// </summary>
    public Vector3 InverseTransformPoint(Vector3 worldPoint)
    {
        Matrix4x4.Invert(WorldMatrix, out var inverse);
        return Vector3.Transform(worldPoint, inverse);
    }

    #endregion

    #region Dirty Flag Management

    private void MarkDirty()
    {
        _localDirty = true;
        MarkWorldDirty();
    }

    private void MarkWorldDirty()
    {
        if (_worldDirty) return;
        
        _worldDirty = true;
        foreach (var child in _children)
        {
            child.MarkWorldDirty();
        }
    }

    private void UpdateLocalMatrix()
    {
        if (!_localDirty) return;

        _localMatrix = Matrix4x4.CreateScale(_localScale) *
                       Matrix4x4.CreateFromQuaternion(_localRotation) *
                       Matrix4x4.CreateTranslation(_localPosition);
        _localDirty = false;
    }

    private void UpdateWorldMatrix()
    {
        if (!_worldDirty) return;

        UpdateLocalMatrix();

        if (_parent != null)
        {
            _worldMatrix = _localMatrix * _parent.WorldMatrix;
        }
        else
        {
            _worldMatrix = _localMatrix;
        }

        _worldDirty = false;
    }

    #endregion

    /// <summary>
    /// Set Euler angles (in degrees)
    /// </summary>
    public void SetEulerAngles(float pitch, float yaw, float roll)
    {
        LocalRotation = Quaternion.CreateFromYawPitchRoll(
            MathF.PI / 180f * yaw,
            MathF.PI / 180f * pitch,
            MathF.PI / 180f * roll
        );
    }

    /// <summary>
    /// Get Euler angles (in degrees) - approximate, may have gimbal lock issues
    /// </summary>
    public Vector3 GetEulerAngles()
    {
        // Extract from rotation matrix
        var m = Matrix4x4.CreateFromQuaternion(_localRotation);
        
        float pitch, yaw, roll;
        
        if (MathF.Abs(m.M32) < 0.999f)
        {
            pitch = MathF.Asin(-m.M32) * 180f / MathF.PI;
            yaw = MathF.Atan2(m.M31, m.M33) * 180f / MathF.PI;
            roll = MathF.Atan2(m.M12, m.M22) * 180f / MathF.PI;
        }
        else
        {
            // Gimbal lock
            pitch = m.M32 < 0 ? 90f : -90f;
            yaw = MathF.Atan2(-m.M13, m.M11) * 180f / MathF.PI;
            roll = 0;
        }
        
        return new Vector3(pitch, yaw, roll);
    }
}
