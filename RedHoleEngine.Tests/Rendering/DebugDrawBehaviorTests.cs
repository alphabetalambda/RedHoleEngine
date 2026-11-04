using System.Numerics;
using RedHoleEngine.Rendering.Debug;

namespace RedHoleEngine.Tests.Rendering;

public class DebugDrawBehaviorTests
{
    [Fact]
    public void Colors_LerpAndClamp()
    {
        var mid = DebugColor.Lerp(DebugColor.Black, DebugColor.White, 0.25f);
        Assert.Equal(0.25f, mid.R);
        var clamped = DebugColor.Lerp(DebugColor.Red, DebugColor.Blue, 5f);
        Assert.Equal(0f, clamped.R);
        Assert.Equal(1f, clamped.B);
        Assert.Equal(0.4f, DebugColor.Green.WithAlpha(0.4f).A);
        Assert.True(DebugVertex.SizeInBytes > 0);
    }

    [Fact]
    public void Primitives_AppendVertices_AndClear()
    {
        var draw = new DebugDrawManager();
        Assert.False(draw.HasContent);

        draw.DrawLine(Vector3.Zero, Vector3.UnitX, DebugColor.Red, 2f);
        draw.DrawLineStrip(new[] { Vector3.Zero, Vector3.UnitY, Vector3.UnitZ }, DebugColor.Blue);
        draw.DrawArrow(Vector3.Zero, Vector3.UnitY, DebugColor.Green, 0.25f);
        draw.DrawPoint(Vector3.One, DebugColor.Yellow, 8f);
        draw.DrawWireBox(new Vector3(-1, -1, -1), Vector3.One, DebugColor.White);
        int beforeSphere = draw.GetTotalVertexCount();
        draw.DrawWireSphere(Vector3.Zero, 2f, DebugColor.Cyan, segments: 8);
        Assert.True(draw.GetTotalVertexCount() > beforeSphere);
        draw.DrawCircle(Vector3.Zero, 1f, Vector3.UnitY, DebugColor.Magenta, segments: 8);
        draw.DrawCone(Vector3.Zero, Vector3.UnitY, 2f, 0.4f, DebugColor.Orange, segments: 6);
        draw.DrawAxes(Vector3.Zero, 0.5f);
        draw.DrawGrid(Vector3.Zero, Vector3.UnitY, 4f, 4, DebugColor.Gray);
        draw.DrawText(Vector3.UnitX, "label", DebugColor.AudioSource);

        Assert.True(draw.HasContent);
        Assert.True(draw.LineVertices.Count > 10);
        Assert.Single(draw.PointVertices);
        Assert.Equal("label", Assert.Single(draw.TextLabels).Text);
        Assert.Equal(8f, draw.PointVertices[0].Position == Vector3.One ? 8f : 0f);

        draw.DrawLineStrip(Array.Empty<Vector3>(), DebugColor.Red);
        draw.DrawArrow(Vector3.Zero, Vector3.Zero, DebugColor.Red);

        var batch = new DebugBatch(DebugPrimitiveType.Lines);
        batch.Vertices.Add(new DebugVertex(Vector3.Zero, DebugColor.DirectPath));
        batch.Clear();
        Assert.Empty(batch.Vertices);

        draw.Clear();
        Assert.False(draw.HasContent);
        Assert.Equal(0, draw.GetTotalVertexCount());
    }
}
