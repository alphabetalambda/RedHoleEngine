using System.Numerics;
using RedHoleEngine.Core.Scene;

namespace RedHoleEngine.Tests.Core.Scene;

public class TransformGapTests
{
    [Fact]
    public void RotateEuler_MatchesSetEulerAnglesFromIdentity()
    {
        var rotated = new Transform();
        var expected = new Transform();

        rotated.Rotate(0f, 90f, 0f);
        expected.SetEulerAngles(0f, 90f, 0f);

        Assert.Equal(1f, Vector3.Dot(expected.Forward, rotated.Forward), 3);
        Assert.Equal(1f, Vector3.Dot(expected.Up, rotated.Up), 3);
        Assert.Equal(1f, Vector3.Dot(expected.Right, rotated.Right), 3);
    }

    [Fact]
    public void LookAt_PointsForwardAtTarget()
    {
        var transform = new Transform(new Vector3(0f, 0f, 0f));

        transform.LookAt(new Vector3(0f, 0f, -10f), Vector3.UnitY);

        Assert.Equal(0f, transform.Forward.X, 3);
        Assert.Equal(0f, transform.Forward.Y, 3);
        Assert.Equal(-1f, transform.Forward.Z, 3);

        transform.LookAt(new Vector3(10f, 0f, 0f), Vector3.UnitY);

        Assert.Equal(1f, transform.Forward.X, 3);
        Assert.Equal(0f, transform.Forward.Y, 3);
        Assert.Equal(0f, transform.Forward.Z, 3);
    }

    [Fact]
    public void RemoveChild_IgnoresNodeThatIsNotAChild()
    {
        var parent = new Transform();
        var child = new Transform();
        var stranger = new Transform(new Vector3(4f, 0f, 0f));
        parent.AddChild(child);

        parent.RemoveChild(stranger);

        Assert.Same(parent, child.Parent);
        Assert.Null(stranger.Parent);
        Assert.Equal(new Vector3(4f, 0f, 0f), stranger.LocalPosition);
    }

    [Fact]
    public void SetParent_SameParent_LeavesChildListAlone()
    {
        var parent = new Transform();
        var child = new Transform();
        parent.AddChild(child);

        child.SetParent(parent);

        Assert.Same(parent, child.Parent);
        Assert.Equal(1, parent.Children.Count);
    }
}
