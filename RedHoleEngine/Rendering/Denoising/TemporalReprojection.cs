using System.Numerics;

namespace RedHoleEngine.Rendering.Denoising;

/// <summary>
/// CPU mirror of the math in denoise_svgf_temporal.comp. Keep both in sync.
/// Pure functions so the reprojection/validation logic is unit testable without a GPU.
/// </summary>
public static class TemporalReprojection
{
    /// <summary>
    /// Previous-frame pixel for a current pixel. Motion vectors point from current to previous
    /// (motion = (currentUV - prevUV) * resolution), so prev = current - motion.
    /// </summary>
    public static (int X, int Y) ReprojectPixel(int x, int y, Vector2 motion)
    {
        float px = x + 0.5f - motion.X;
        float py = y + 0.5f - motion.Y;
        return ((int)MathF.Floor(px), (int)MathF.Floor(py));
    }

    public static bool InBounds(int x, int y, int width, int height)
        => x >= 0 && y >= 0 && x < width && y < height;

    /// <summary>Normal consistency: dot(n, nPrev) must be at least the threshold.</summary>
    public static bool NormalsConsistent(Vector3 current, Vector3 previous, float threshold)
    {
        if (previous.Length() < 0.5f || current.Length() < 0.5f) return false;
        return Vector3.Dot(Vector3.Normalize(current), Vector3.Normalize(previous)) >= threshold;
    }

    /// <summary>Relative depth consistency. Non-positive depth (sky / no hit) is always rejected.</summary>
    public static bool DepthsConsistent(float current, float previous, float relativeThreshold)
    {
        if (previous <= 0f || current <= 0f) return false;
        return MathF.Abs(current - previous) / MathF.Max(current, 0.0001f) <= relativeThreshold;
    }

    /// <summary>Full history validation (bounds + depth + normal).</summary>
    public static bool IsHistoryValid(
        int prevX, int prevY, int width, int height,
        Vector3 currentNormal, float currentDepth,
        Vector3 prevNormal, float prevDepth,
        float normalThreshold, float depthThreshold)
    {
        return InBounds(prevX, prevY, width, height)
            && DepthsConsistent(currentDepth, prevDepth, depthThreshold)
            && NormalsConsistent(currentNormal, prevNormal, normalThreshold);
    }

    /// <summary>
    /// Exponential blend factor for the current frame: never lower than baseAlpha (strength),
    /// and 1/historyLength while history is still short (fast start-up).
    /// </summary>
    public static float BlendAlpha(float historyLength, float baseAlpha)
        => MathF.Min(1f, MathF.Max(baseAlpha, 1f / MathF.Max(historyLength, 1f)));

    /// <summary>New history length: previous + 1, capped.</summary>
    public static float NextHistoryLength(float previousLength, int maxHistory)
        => MathF.Min(previousLength + 1f, maxHistory);

    /// <summary>Variance-guided clamp of history to mean +/- gamma * sigma of the current neighbourhood.</summary>
    public static float ClampToNeighbourhood(float history, float mean, float sigma, float gamma)
        => Math.Clamp(history, mean - gamma * sigma, mean + gamma * sigma);
}
