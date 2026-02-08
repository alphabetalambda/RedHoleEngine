using System.Numerics;
using RedHoleEngine.Core.Scene;

namespace RedHoleEngine.Tests.Core.Scene;

public class TransformTests
{
    private const float Epsilon = 0.001f;

    #region Construction Tests

    [Fact]
    public void Constructor_Default_HasIdentityValues()
    {
        var transform = new Transform();
        
        Assert.Equal(Vector3.Zero, transform.LocalPosition);
        Assert.Equal(Quaternion.Identity, transform.LocalRotation);
        Assert.Equal(Vector3.One, transform.LocalScale);
    }

    [Fact]
    public void Constructor_WithPosition_SetsPosition()
    {
        var position = new Vector3(10, 20, 30);
        var transform = new Transform(position);
        
        Assert.Equal(position, transform.LocalPosition);
    }

    [Fact]
    public void Constructor_WithPositionAndRotation_SetsBoth()
    {
        var position = new Vector3(10, 20, 30);
        var rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI / 2);
        var transform = new Transform(position, rotation);
        
        Assert.Equal(position, transform.LocalPosition);
        Assert.Equal(rotation.X, transform.LocalRotation.X, Epsilon);
        Assert.Equal(rotation.Y, transform.LocalRotation.Y, Epsilon);
        Assert.Equal(rotation.Z, transform.LocalRotation.Z, Epsilon);
        Assert.Equal(rotation.W, transform.LocalRotation.W, Epsilon);
    }

    [Fact]
    public void Constructor_WithAll_SetsAllValues()
    {
        var position = new Vector3(1, 2, 3);
        var rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitX, MathF.PI / 4);
        var scale = new Vector3(2, 3, 4);
        
        var transform = new Transform(position, rotation, scale);
        
        Assert.Equal(position, transform.LocalPosition);
        Assert.Equal(scale, transform.LocalScale);
    }

    #endregion

    #region Local Property Tests

    [Fact]
    public void LocalPosition_SetGet_Works()
    {
        var transform = new Transform();
        var newPos = new Vector3(5, 10, 15);
        
        transform.LocalPosition = newPos;
        
        Assert.Equal(newPos, transform.LocalPosition);
    }

    [Fact]
    public void LocalRotation_SetGet_Normalizes()
    {
        var transform = new Transform();
        var rotation = new Quaternion(1, 1, 1, 1); // Not normalized
        
        transform.LocalRotation = rotation;
        
        // Should be normalized (length = 1)
        var len = transform.LocalRotation.Length();
        Assert.Equal(1f, len, Epsilon);
    }

    [Fact]
    public void LocalScale_SetGet_Works()
    {
        var transform = new Transform();
        var newScale = new Vector3(2, 3, 4);
        
        transform.LocalScale = newScale;
        
        Assert.Equal(newScale, transform.LocalScale);
    }

    #endregion

    #region World Property Tests (No Parent)

    [Fact]
    public void Position_NoParent_EqualsLocalPosition()
    {
        var transform = new Transform();
        transform.LocalPosition = new Vector3(10, 20, 30);
        
        Assert.Equal(transform.LocalPosition, transform.Position);
    }

    [Fact]
    public void Rotation_NoParent_EqualsLocalRotation()
    {
        var transform = new Transform();
        transform.LocalRotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI / 2);
        
        Assert.Equal(transform.LocalRotation.X, transform.Rotation.X, Epsilon);
        Assert.Equal(transform.LocalRotation.Y, transform.Rotation.Y, Epsilon);
        Assert.Equal(transform.LocalRotation.Z, transform.Rotation.Z, Epsilon);
        Assert.Equal(transform.LocalRotation.W, transform.Rotation.W, Epsilon);
    }

    [Fact]
    public void Position_Set_UpdatesLocalPosition()
    {
        var transform = new Transform();
        
        transform.Position = new Vector3(5, 10, 15);
        
        Assert.Equal(new Vector3(5, 10, 15), transform.LocalPosition);
    }

    #endregion

    #region Hierarchy Tests

    [Fact]
    public void SetParent_EstablishesRelationship()
    {
        var parent = new Transform();
        var child = new Transform();
        
        child.SetParent(parent);
        
        Assert.Equal(parent, child.Parent);
        Assert.Contains(child, parent.Children);
    }

    [Fact]
    public void SetParent_Null_RemovesParent()
    {
        var parent = new Transform();
        var child = new Transform();
        
        child.SetParent(parent);
        child.SetParent(null);
        
        Assert.Null(child.Parent);
        Assert.DoesNotContain(child, parent.Children);
    }

    [Fact]
    public void AddChild_EstablishesRelationship()
    {
        var parent = new Transform();
        var child = new Transform();
        
        parent.AddChild(child);
        
        Assert.Equal(parent, child.Parent);
        Assert.Contains(child, parent.Children);
    }

    [Fact]
    public void RemoveChild_RemovesRelationship()
    {
        var parent = new Transform();
        var child = new Transform();
        
        parent.AddChild(child);
        parent.RemoveChild(child);
        
        Assert.Null(child.Parent);
        Assert.DoesNotContain(child, parent.Children);
    }

    [Fact]
    public void Position_WithParent_InheritsParentTransform()
    {
        var parent = new Transform();
        parent.LocalPosition = new Vector3(100, 0, 0);
        
        var child = new Transform();
        child.LocalPosition = new Vector3(10, 0, 0);
        
        // SetParent with worldPositionStays=false to directly inherit
        child.SetParent(parent, worldPositionStays: false);
        
        // Child world position = parent position + child local position
        Assert.Equal(110f, child.Position.X, Epsilon);
    }

    [Fact]
    public void Rotation_WithParent_InheritsParentRotation()
    {
        var parent = new Transform();
        parent.LocalRotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI / 2); // 90 degrees
        
        var child = new Transform();
        child.LocalRotation = Quaternion.Identity;
        child.SetParent(parent, worldPositionStays: false);
        
        // Child world rotation should equal parent rotation
        var expected = parent.LocalRotation;
        Assert.Equal(expected.X, child.Rotation.X, Epsilon);
        Assert.Equal(expected.Y, child.Rotation.Y, Epsilon);
        Assert.Equal(expected.Z, child.Rotation.Z, Epsilon);
        Assert.Equal(expected.W, child.Rotation.W, Epsilon);
    }

    [Fact]
    public void SetParent_WorldPositionStays_PreservesWorldPosition()
    {
        var parent = new Transform();
        parent.LocalPosition = new Vector3(100, 0, 0);
        
        var child = new Transform();
        child.LocalPosition = new Vector3(50, 0, 0);
        
        float originalWorldX = child.Position.X;
        
        child.SetParent(parent, worldPositionStays: true);
        
        // World position should remain 50
        Assert.Equal(originalWorldX, child.Position.X, Epsilon);
        // But local position should have changed
        Assert.Equal(-50f, child.LocalPosition.X, Epsilon);
    }

    #endregion

    #region Direction Vector Tests

    [Fact]
    public void Forward_NoRotation_NegativeZ()
    {
        var transform = new Transform();
        
        var forward = transform.Forward;
        
        Assert.Equal(0f, forward.X, Epsilon);
        Assert.Equal(0f, forward.Y, Epsilon);
        Assert.Equal(-1f, forward.Z, Epsilon);
    }

    [Fact]
    public void Right_NoRotation_PositiveX()
    {
        var transform = new Transform();
        
        var right = transform.Right;
        
        Assert.Equal(1f, right.X, Epsilon);
        Assert.Equal(0f, right.Y, Epsilon);
        Assert.Equal(0f, right.Z, Epsilon);
    }

    [Fact]
    public void Up_NoRotation_PositiveY()
    {
        var transform = new Transform();
        
        var up = transform.Up;
        
        Assert.Equal(0f, up.X, Epsilon);
        Assert.Equal(1f, up.Y, Epsilon);
        Assert.Equal(0f, up.Z, Epsilon);
    }

    [Fact]
    public void Forward_Rotated90Y_NegativeX()
    {
        var transform = new Transform();
        transform.LocalRotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI / 2);
        
        var forward = transform.Forward;
        
        // After 90 degree rotation around Y (counter-clockwise from above), 
        // forward (-Z) rotates to point toward -X
        Assert.Equal(-1f, forward.X, 0.01f);
        Assert.Equal(0f, forward.Y, 0.01f);
        Assert.Equal(0f, forward.Z, 0.01f);
    }

    #endregion

    #region Transformation Methods Tests

    [Fact]
    public void Translate_LocalSpace_MovesInLocalDirection()
    {
        var transform = new Transform();
        transform.LocalRotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI / 2); // 90 degrees
        
        transform.Translate(new Vector3(0, 0, -10)); // Move "forward" in local space
        
        // After 90 degree Y rotation (counter-clockwise), local -Z becomes world -X
        Assert.Equal(-10f, transform.LocalPosition.X, 0.1f);
        Assert.Equal(0f, transform.LocalPosition.Z, 0.1f);
    }

    [Fact]
    public void TranslateWorld_MovesInWorldDirection()
    {
        var transform = new Transform();
        transform.LocalRotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI / 2);
        
        transform.TranslateWorld(new Vector3(10, 0, 0));
        
        Assert.Equal(10f, transform.Position.X, Epsilon);
    }

    [Fact]
    public void Rotate_AppliesRotation()
    {
        var transform = new Transform();
        
        transform.Rotate(Vector3.UnitY, 90f); // 90 degrees around Y
        
        // Forward should now point roughly in -X direction (due to rotation direction)
        var forward = transform.Forward;
        Assert.True(MathF.Abs(forward.X) > 0.9f);
    }

    [Fact]
    public void TransformPoint_ConvertsLocalToWorld()
    {
        var transform = new Transform();
        transform.LocalPosition = new Vector3(10, 0, 0);
        
        var worldPoint = transform.TransformPoint(new Vector3(5, 0, 0));
        
        Assert.Equal(15f, worldPoint.X, Epsilon);
    }

    [Fact]
    public void TransformDirection_IgnoresPosition()
    {
        var transform = new Transform();
        transform.LocalPosition = new Vector3(100, 100, 100);
        
        var worldDir = transform.TransformDirection(Vector3.UnitX);
        
        // Should not be affected by position
        Assert.Equal(1f, worldDir.X, Epsilon);
        Assert.Equal(0f, worldDir.Y, Epsilon);
        Assert.Equal(0f, worldDir.Z, Epsilon);
    }

    [Fact]
    public void InverseTransformPoint_ConvertsWorldToLocal()
    {
        var transform = new Transform();
        transform.LocalPosition = new Vector3(10, 0, 0);
        
        var localPoint = transform.InverseTransformPoint(new Vector3(15, 0, 0));
        
        Assert.Equal(5f, localPoint.X, Epsilon);
    }

    #endregion

    #region Matrix Tests

    [Fact]
    public void LocalMatrix_Identity_IsIdentityMatrix()
    {
        var transform = new Transform();
        
        var matrix = transform.LocalMatrix;
        
        Assert.Equal(Matrix4x4.Identity, matrix);
    }

    [Fact]
    public void LocalMatrix_WithTranslation_HasTranslation()
    {
        var transform = new Transform();
        transform.LocalPosition = new Vector3(10, 20, 30);
        
        var matrix = transform.LocalMatrix;
        
        Assert.Equal(10f, matrix.M41, Epsilon);
        Assert.Equal(20f, matrix.M42, Epsilon);
        Assert.Equal(30f, matrix.M43, Epsilon);
    }

    [Fact]
    public void WorldMatrix_NoParent_EqualsLocalMatrix()
    {
        var transform = new Transform();
        transform.LocalPosition = new Vector3(10, 20, 30);
        transform.LocalScale = new Vector3(2, 2, 2);
        
        Assert.Equal(transform.LocalMatrix, transform.WorldMatrix);
    }

    [Fact]
    public void WorldMatrix_WithParent_CombinesMatrices()
    {
        var parent = new Transform();
        parent.LocalPosition = new Vector3(100, 0, 0);
        
        var child = new Transform();
        child.LocalPosition = new Vector3(10, 0, 0);
        child.SetParent(parent, worldPositionStays: false);
        
        var worldMatrix = child.WorldMatrix;
        
        // Translation should be combined
        Assert.Equal(110f, worldMatrix.M41, Epsilon);
    }

    #endregion

    #region Euler Angles Tests

    [Fact]
    public void SetEulerAngles_SetsRotation()
    {
        var transform = new Transform();
        
        transform.SetEulerAngles(0, 90, 0); // 90 degrees yaw
        
        var forward = transform.Forward;
        // After 90 degree yaw, forward direction changes significantly
        // The exact direction depends on rotation conventions
        Assert.True(MathF.Abs(forward.X) > 0.9f || MathF.Abs(forward.Z) < 0.1f);
    }

    [Fact]
    public void GetEulerAngles_ReturnsCorrectValues()
    {
        var transform = new Transform();
        transform.SetEulerAngles(30, 45, 60);
        
        var euler = transform.GetEulerAngles();
        
        // Should be close to original values (may have slight differences due to conversion)
        Assert.Equal(30f, euler.X, 1f); // pitch
        Assert.Equal(45f, euler.Y, 1f); // yaw
        Assert.Equal(60f, euler.Z, 1f); // roll
    }

    #endregion
}
