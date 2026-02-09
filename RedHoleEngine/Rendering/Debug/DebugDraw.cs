using System.Numerics;
using System.Runtime.InteropServices;

namespace RedHoleEngine.Rendering.Debug;

/// <summary>
/// RGBA color for debug drawing with predefined colors for audio visualization
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct DebugColor
{
    public float R, G, B, A;

    public DebugColor(float r, float g, float b, float a = 1f)
    {
        R = r;
        G = g;
        B = b;
        A = a;
    }

    public DebugColor WithAlpha(float alpha) => new(R, G, B, alpha);

    public static DebugColor Lerp(DebugColor a, DebugColor b, float t)
    {
        t = Math.Clamp(t, 0f, 1f);
        return new DebugColor(
            a.R + (b.R - a.R) * t,
            a.G + (b.G - a.G) * t,
            a.B + (b.B - a.B) * t,
            a.A + (b.A - a.A) * t
        );
    }

    // Basic colors
    public static readonly DebugColor White = new(1f, 1f, 1f);
    public static readonly DebugColor Black = new(0f, 0f, 0f);
    public static readonly DebugColor Red = new(1f, 0f, 0f);
    public static readonly DebugColor Green = new(0f, 1f, 0f);
    public static readonly DebugColor Blue = new(0f, 0f, 1f);
    public static readonly DebugColor Yellow = new(1f, 1f, 0f);
    public static readonly DebugColor Cyan = new(0f, 1f, 1f);
    public static readonly DebugColor Magenta = new(1f, 0f, 1f);
    public static readonly DebugColor Orange = new(1f, 0.5f, 0f);
    public static readonly DebugColor Purple = new(0.5f, 0f, 0.5f);
    public static readonly DebugColor Gray = new(0.5f, 0.5f, 0.5f);

    // Audio-specific colors
    public static readonly DebugColor AudioSource = new(0.2f, 0.8f, 0.2f);      // Bright green
    public static readonly DebugColor AudioListener = new(0.2f, 0.6f, 1.0f);    // Sky blue
    public static readonly DebugColor DirectPath = new(0.0f, 1.0f, 0.5f);       // Cyan-green
    public static readonly DebugColor ReflectionPath = new(1.0f, 0.8f, 0.2f);   // Gold
    public static readonly DebugColor TransmissionPath = new(0.8f, 0.2f, 0.8f); // Magenta
}

/// <summary>
/// A vertex for debug line/point rendering
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct DebugVertex
{
    public Vector3 Position;
    public DebugColor Color;

    public DebugVertex(Vector3 position, DebugColor color)
    {
        Position = position;
        Color = color;
    }

    public static readonly int SizeInBytes = Marshal.SizeOf<DebugVertex>();
}

/// <summary>
/// Types of debug primitives
/// </summary>
public enum DebugPrimitiveType
{
    Lines,
    Points
}

/// <summary>
/// A batch of debug primitives to render
/// </summary>
public class DebugBatch
{
    public DebugPrimitiveType Type { get; }
    public List<DebugVertex> Vertices { get; } = new();
    public float PointSize { get; set; } = 5f;
    public float LineWidth { get; set; } = 1f;

    public DebugBatch(DebugPrimitiveType type)
    {
        Type = type;
    }

    public void Clear() => Vertices.Clear();
}

/// <summary>
/// Text label for debug visualization
/// </summary>
public struct DebugText
{
    public Vector3 WorldPosition;
    public string Text;
    public DebugColor Color;

    public DebugText(Vector3 position, string text, DebugColor color)
    {
        WorldPosition = position;
        Text = text;
        Color = color;
    }
}

/// <summary>
/// Manages debug draw primitives for a single frame.
/// Collects draw calls and provides data to the renderer.
/// </summary>
public class DebugDrawManager
{
    private readonly DebugBatch _lineBatch = new(DebugPrimitiveType.Lines);
    private readonly DebugBatch _pointBatch = new(DebugPrimitiveType.Points);
    private readonly List<DebugText> _textLabels = new();

    /// <summary>
    /// Line vertices for rendering
    /// </summary>
    public IReadOnlyList<DebugVertex> LineVertices => _lineBatch.Vertices;

    /// <summary>
    /// Point vertices for rendering
    /// </summary>
    public IReadOnlyList<DebugVertex> PointVertices => _pointBatch.Vertices;

    /// <summary>
    /// Text labels (for future text rendering)
    /// </summary>
    public IReadOnlyList<DebugText> TextLabels => _textLabels;

    /// <summary>
    /// Clear all debug primitives (called at start of each frame)
    /// </summary>
    public void Clear()
    {
        _lineBatch.Clear();
        _pointBatch.Clear();
        _textLabels.Clear();
    }

    #region Lines

    /// <summary>
    /// Draw a line between two points
    /// </summary>
    public void DrawLine(Vector3 start, Vector3 end, DebugColor color, float width = 1f)
    {
        _lineBatch.Vertices.Add(new DebugVertex(start, color));
        _lineBatch.Vertices.Add(new DebugVertex(end, color));
        _lineBatch.LineWidth = width;
    }

    /// <summary>
    /// Draw multiple connected line segments
    /// </summary>
    public void DrawLineStrip(IReadOnlyList<Vector3> points, DebugColor color, float width = 1f)
    {
        if (points.Count < 2) return;
        
        for (int i = 0; i < points.Count - 1; i++)
        {
            DrawLine(points[i], points[i + 1], color, width);
        }
    }

    /// <summary>
    /// Draw an arrow with a head
    /// </summary>
    public void DrawArrow(Vector3 start, Vector3 end, DebugColor color, float headSize = 0.2f)
    {
        var dir = end - start;
        if (dir.LengthSquared() < 0.0001f) return;

        dir = Vector3.Normalize(dir);
        
        // Main line
        DrawLine(start, end, color);

        // Create arrowhead using perpendicular vectors
        var perp = Vector3.Cross(dir, Vector3.UnitY);
        if (perp.LengthSquared() < 0.0001f)
            perp = Vector3.Cross(dir, Vector3.UnitX);
        perp = Vector3.Normalize(perp) * headSize;

        var headBase = end - dir * headSize * 2f;
        
        DrawLine(end, headBase + perp, color);
        DrawLine(end, headBase - perp, color);
        
        // Second pair at 90 degrees
        var perp2 = Vector3.Cross(dir, perp);
        perp2 = Vector3.Normalize(perp2) * headSize;
        DrawLine(end, headBase + perp2, color);
        DrawLine(end, headBase - perp2, color);
    }

    #endregion

    #region Points

    /// <summary>
    /// Draw a single point
    /// </summary>
    public void DrawPoint(Vector3 position, DebugColor color, float size = 5f)
    {
        _pointBatch.Vertices.Add(new DebugVertex(position, color));
        _pointBatch.PointSize = size;
    }

    #endregion

    #region Shapes

    /// <summary>
    /// Draw an axis-aligned wireframe box
    /// </summary>
    public void DrawWireBox(Vector3 min, Vector3 max, DebugColor color)
    {
        // Bottom face
        DrawLine(new Vector3(min.X, min.Y, min.Z), new Vector3(max.X, min.Y, min.Z), color);
        DrawLine(new Vector3(max.X, min.Y, min.Z), new Vector3(max.X, min.Y, max.Z), color);
        DrawLine(new Vector3(max.X, min.Y, max.Z), new Vector3(min.X, min.Y, max.Z), color);
        DrawLine(new Vector3(min.X, min.Y, max.Z), new Vector3(min.X, min.Y, min.Z), color);

        // Top face
        DrawLine(new Vector3(min.X, max.Y, min.Z), new Vector3(max.X, max.Y, min.Z), color);
        DrawLine(new Vector3(max.X, max.Y, min.Z), new Vector3(max.X, max.Y, max.Z), color);
        DrawLine(new Vector3(max.X, max.Y, max.Z), new Vector3(min.X, max.Y, max.Z), color);
        DrawLine(new Vector3(min.X, max.Y, max.Z), new Vector3(min.X, max.Y, min.Z), color);

        // Vertical edges
        DrawLine(new Vector3(min.X, min.Y, min.Z), new Vector3(min.X, max.Y, min.Z), color);
        DrawLine(new Vector3(max.X, min.Y, min.Z), new Vector3(max.X, max.Y, min.Z), color);
        DrawLine(new Vector3(max.X, min.Y, max.Z), new Vector3(max.X, max.Y, max.Z), color);
        DrawLine(new Vector3(min.X, min.Y, max.Z), new Vector3(min.X, max.Y, max.Z), color);
    }

    /// <summary>
    /// Draw a wireframe sphere
    /// </summary>
    public void DrawWireSphere(Vector3 center, float radius, DebugColor color, int segments = 16)
    {
        DrawCircle(center, radius, Vector3.UnitX, color, segments);
        DrawCircle(center, radius, Vector3.UnitY, color, segments);
        DrawCircle(center, radius, Vector3.UnitZ, color, segments);
    }

    /// <summary>
    /// Draw a circle in 3D space
    /// </summary>
    public void DrawCircle(Vector3 center, float radius, Vector3 normal, DebugColor color, int segments = 32)
    {
        normal = Vector3.Normalize(normal);
        
        // Create perpendicular vectors
        var tangent = Vector3.Cross(normal, Vector3.UnitY);
        if (tangent.LengthSquared() < 0.0001f)
            tangent = Vector3.Cross(normal, Vector3.UnitX);
        tangent = Vector3.Normalize(tangent);
        var bitangent = Vector3.Cross(normal, tangent);

        float angleStep = MathF.PI * 2f / segments;
        
        for (int i = 0; i < segments; i++)
        {
            float angle1 = i * angleStep;
            float angle2 = (i + 1) * angleStep;

            var p1 = center + (tangent * MathF.Cos(angle1) + bitangent * MathF.Sin(angle1)) * radius;
            var p2 = center + (tangent * MathF.Cos(angle2) + bitangent * MathF.Sin(angle2)) * radius;

            DrawLine(p1, p2, color);
        }
    }

    /// <summary>
    /// Draw a cone shape (for directivity visualization)
    /// </summary>
    public void DrawCone(Vector3 apex, Vector3 direction, float length, float angle, DebugColor color, int segments = 16)
    {
        direction = Vector3.Normalize(direction);
        float radius = length * MathF.Tan(angle * MathF.PI / 180f);
        var baseCenter = apex + direction * length;

        // Draw base circle
        DrawCircle(baseCenter, radius, direction, color, segments);

        // Create perpendicular vectors
        var tangent = Vector3.Cross(direction, Vector3.UnitY);
        if (tangent.LengthSquared() < 0.0001f)
            tangent = Vector3.Cross(direction, Vector3.UnitX);
        tangent = Vector3.Normalize(tangent);
        var bitangent = Vector3.Cross(direction, tangent);

        // Draw lines from apex to base
        float angleStep = MathF.PI * 2f / 4; // Just 4 lines for simplicity
        for (int i = 0; i < 4; i++)
        {
            float a = i * angleStep;
            var basePoint = baseCenter + (tangent * MathF.Cos(a) + bitangent * MathF.Sin(a)) * radius;
            DrawLine(apex, basePoint, color);
        }
    }

    /// <summary>
    /// Draw coordinate axes at a position
    /// </summary>
    public void DrawAxes(Vector3 position, float size = 1f)
    {
        DrawLine(position, position + Vector3.UnitX * size, DebugColor.Red);
        DrawLine(position, position + Vector3.UnitY * size, DebugColor.Green);
        DrawLine(position, position + Vector3.UnitZ * size, DebugColor.Blue);
    }

    /// <summary>
    /// Draw a grid on a plane
    /// </summary>
    public void DrawGrid(Vector3 center, Vector3 normal, float size, int divisions, DebugColor color)
    {
        normal = Vector3.Normalize(normal);
        
        var tangent = Vector3.Cross(normal, Vector3.UnitY);
        if (tangent.LengthSquared() < 0.0001f)
            tangent = Vector3.Cross(normal, Vector3.UnitX);
        tangent = Vector3.Normalize(tangent);
        var bitangent = Vector3.Cross(normal, tangent);

        float halfSize = size / 2f;
        float step = size / divisions;

        for (int i = 0; i <= divisions; i++)
        {
            float offset = -halfSize + i * step;
            
            // Lines along tangent direction
            var start1 = center + tangent * offset - bitangent * halfSize;
            var end1 = center + tangent * offset + bitangent * halfSize;
            DrawLine(start1, end1, color);

            // Lines along bitangent direction
            var start2 = center + bitangent * offset - tangent * halfSize;
            var end2 = center + bitangent * offset + tangent * halfSize;
            DrawLine(start2, end2, color);
        }
    }

    #endregion

    #region Text

    /// <summary>
    /// Draw text at a world position (requires text rendering support)
    /// </summary>
    public void DrawText(Vector3 position, string text, DebugColor color)
    {
        _textLabels.Add(new DebugText(position, text, color));
    }

    #endregion

    #region Utility

    /// <summary>
    /// Get total vertex count for buffer allocation
    /// </summary>
    public int GetTotalVertexCount() => _lineBatch.Vertices.Count + _pointBatch.Vertices.Count;

    /// <summary>
    /// Check if there's anything to render
    /// </summary>
    public bool HasContent => _lineBatch.Vertices.Count > 0 || _pointBatch.Vertices.Count > 0;

    #endregion
}
