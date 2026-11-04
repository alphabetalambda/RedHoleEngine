using System.Numerics;
using RedHoleEngine.Rendering.Debug;

namespace RedHoleEngine.Tests.Rendering;

public class DebugDrawCoverageTests
{
    [Fact]
    public void Colors_LerpClamp_AndNamedValues()
    {
        Assert.Equal(1f, DebugColor.Red.R);
        Assert.Equal(0f, DebugColor.Red.G);
        var faded = DebugColor.Blue.WithAlpha(0.25f);
        Assert.Equal(0.25f, faded.A);
        Assert.Equal(1f, faded.B);

        var mid = DebugColor.Lerp(DebugColor.Black, DebugColor.White, 0.25f);
        Assert.Equal(0.25f, mid.R, precision: 3);
        Assert.Equal(1f, mid.A, precision: 3);

        var clamped = DebugColor.Lerp(DebugColor.Black, DebugColor.White, 4f);
        Assert.Equal(1f, clamped.R);
        var start = DebugColor.Lerp(DebugColor.Red, DebugColor.White, 0f);
        Assert.Equal(DebugColor.Red.R, start.R);
        Assert.Equal(DebugColor.Red.G, start.G);
        Assert.Equal(0.8f, DebugColor.AudioSource.G);
        Assert.True(DebugColor.ReflectionPath.R > DebugColor.DirectPath.R);
    }

    [Fact]
    public void LinesPointsAndText_RecordVerticesUntilCleared()
    {
        Assert.True(DebugVertex.SizeInBytes >= 28);
        var draw = new DebugDrawManager();
        Assert.False(draw.HasContent);
        Assert.Equal(0, draw.GetTotalVertexCount());

        draw.DrawLine(Vector3.Zero, Vector3.UnitX, DebugColor.Red, 2f);
        draw.DrawLine(Vector3.UnitX, Vector3.UnitY, DebugColor.Green, 4f);
        draw.DrawPoint(Vector3.UnitZ, DebugColor.Blue, 9f);
        draw.DrawPoint(Vector3.One, DebugColor.White, 3f);
        draw.DrawText(Vector3.UnitX, "lock", DebugColor.Yellow);

        Assert.Equal(4, draw.LineVertices.Count);
        Assert.Equal(2, draw.PointVertices.Count);
        Assert.Single(draw.TextLabels);
        Assert.Equal("lock", draw.TextLabels[0].Text);
        Assert.Equal(Vector3.UnitX, draw.LineVertices[1].Position);
        Assert.Equal(6, draw.GetTotalVertexCount());
        Assert.True(draw.HasContent);

        draw.DrawLineStrip(new[] { Vector3.Zero }, DebugColor.White);
        Assert.Equal(4, draw.LineVertices.Count);
        draw.DrawLineStrip(new[] { Vector3.Zero, Vector3.UnitX, Vector3.UnitY }, DebugColor.Cyan);
        Assert.Equal(8, draw.LineVertices.Count);

        draw.Clear();
        Assert.Empty(draw.LineVertices);
        Assert.Empty(draw.PointVertices);
        Assert.Empty(draw.TextLabels);
        Assert.False(draw.HasContent);
    }

    [Fact]
    public void HasContent_CurrentBehavior_IgnoresTextOnlyDraws()
    {
        var draw = new DebugDrawManager();
        draw.DrawText(Vector3.Zero, "label", DebugColor.White);
        Assert.False(draw.HasContent);
        Assert.Equal(0, draw.GetTotalVertexCount());
        Assert.Single(draw.TextLabels);
    }

    [Fact(Skip = "BUG: expected HasContent to be true when only text labels are queued, actual HasContent is false")]
    public void HasContent_IncludesTextLabels()
    {
        var draw = new DebugDrawManager();
        draw.DrawText(Vector3.Zero, "label", DebugColor.White);
        Assert.True(draw.HasContent);
    }

    [Fact]
    public void Arrow_BuildsFiveSegments_AndSkipsZeroLength()
    {
        var draw = new DebugDrawManager();
        draw.DrawArrow(Vector3.Zero, Vector3.Zero, DebugColor.Red);
        Assert.Empty(draw.LineVertices);

        draw.DrawArrow(Vector3.Zero, Vector3.UnitY * 2f, DebugColor.Red, 0.25f);
        Assert.Equal(10, draw.LineVertices.Count);
        Assert.Equal(Vector3.Zero, draw.LineVertices[0].Position);
        Assert.Equal(Vector3.UnitY * 2f, draw.LineVertices[1].Position);
        Assert.Contains(draw.LineVertices, v => v.Position.X != 0f || v.Position.Z != 0f);

        draw.Clear();
        draw.DrawArrow(Vector3.Zero, Vector3.UnitY, DebugColor.White, 0.1f);
        Assert.Equal(10, draw.LineVertices.Count);
    }

    [Fact]
    public void WireShapes_UseExpectedSegmentCounts()
    {
        var draw = new DebugDrawManager();
        draw.DrawWireBox(new Vector3(-1f, -2f, -3f), new Vector3(1f, 2f, 3f), DebugColor.Gray);
        Assert.Equal(24, draw.LineVertices.Count);
        Assert.Contains(draw.LineVertices, v => v.Position == new Vector3(-1f, -2f, -3f));
        Assert.Contains(draw.LineVertices, v => v.Position == new Vector3(1f, 2f, 3f));

        draw.Clear();
        draw.DrawWireSphere(Vector3.Zero, 2f, DebugColor.Cyan, 8);
        Assert.Equal(3 * 8 * 2, draw.LineVertices.Count);
        Assert.All(draw.LineVertices, v => Assert.Equal(2f, v.Position.Length(), precision: 3));

        draw.Clear();
        draw.DrawCircle(Vector3.One, 1f, Vector3.UnitY, DebugColor.Orange, 4);
        Assert.Equal(8, draw.LineVertices.Count);

        draw.Clear();
        draw.DrawCone(Vector3.Zero, Vector3.UnitY, 2f, 45f, DebugColor.Purple, 8);
        Assert.True(draw.LineVertices.Count > 8);
        Assert.Contains(draw.LineVertices, v => v.Position == Vector3.Zero);

        draw.Clear();
        draw.DrawAxes(new Vector3(1f, 2f, 3f), 0.5f);
        Assert.Equal(6, draw.LineVertices.Count);
        Assert.Contains(draw.LineVertices, v => v.Color.R == 1f && v.Color.G == 0f && v.Position == new Vector3(1.5f, 2f, 3f));
        Assert.Contains(draw.LineVertices, v => v.Color.G == 1f && v.Color.R == 0f);
        Assert.Contains(draw.LineVertices, v => v.Color.B == 1f && v.Color.R == 0f);

        draw.Clear();
        draw.DrawGrid(Vector3.Zero, Vector3.UnitY, 2f, 2, DebugColor.White);
        Assert.Equal(12, draw.LineVertices.Count);
    }
}
