using RedHoleEngine.Physics;

namespace RedHoleEngine.Rendering;

/// <summary>
/// Pure helpers for progressive accumulation persistence (sample cap).
/// </summary>
public static class AccumulationPolicy
{
    /// <summary>
    /// Per-pixel samples the raytracer adds each frame (RaysPerPixel * SamplesPerFrame).
    /// </summary>
    public static int SamplesPerFrame(RaytracerSettings settings)
    {
        return Math.Max(1, settings.RaysPerPixel) * Math.Max(1, settings.SamplesPerFrame);
    }

    /// <summary>
    /// Total per-pixel samples after <paramref name="accumulatedFrames"/> accumulated frames.
    /// </summary>
    public static long AccumulatedSamples(long accumulatedFrames, int samplesPerFrame)
    {
        return Math.Max(0, accumulatedFrames) * Math.Max(1, samplesPerFrame);
    }

    /// <summary>
    /// True when accumulation has reached <paramref name="maxSamples"/> and should stop tracing
    /// new frames. <paramref name="maxSamples"/> &lt;= 0 means unlimited.
    /// </summary>
    public static bool IsSampleCapReached(long accumulatedFrames, int samplesPerFrame, int maxSamples)
    {
        if (maxSamples <= 0)
            return false;
        return AccumulatedSamples(accumulatedFrames, samplesPerFrame) >= maxSamples;
    }

    /// <summary>
    /// Convenience overload using the settings' cap, accumulate flag and samples per frame.
    /// Never capped when accumulation is disabled.
    /// </summary>
    public static bool IsSampleCapReached(RaytracerSettings settings, long accumulatedFrames)
    {
        return settings.Accumulate &&
               IsSampleCapReached(accumulatedFrames, SamplesPerFrame(settings), settings.MaxAccumulatedSamples);
    }
}

/// <summary>
/// Detects changes to scene data that invalidate accumulated samples.
/// GPU uploads (BVH/triangles, materials, environment map) call <see cref="MarkDirty"/>;
/// per-frame parameters such as the black hole are folded in through <see cref="Update"/>.
/// </summary>
public sealed class SceneChangeTracker
{
    private int _revision;
    private int _lastHash;
    private bool _hasLast;

    /// <summary>Number of explicit scene uploads seen so far.</summary>
    public int Revision => _revision;

    /// <summary>Record that scene data was uploaded to the GPU.</summary>
    public void MarkDirty()
    {
        unchecked { _revision++; }
    }

    /// <summary>
    /// Fold per-frame scene parameters into the scene state and report whether anything changed
    /// since the previous call. The first call establishes a baseline and returns false.
    /// </summary>
    public bool Update(int frameStateHash)
    {
        int hash = HashCode.Combine(_revision, frameStateHash);
        bool changed = _hasLast && hash != _lastHash;
        _lastHash = hash;
        _hasLast = true;
        return changed;
    }

    /// <summary>Forget the baseline (the next <see cref="Update"/> returns false).</summary>
    public void Reset()
    {
        _hasLast = false;
    }

    /// <summary>
    /// Hash the black hole parameters that affect the raytraced image. Null hashes to 0.
    /// </summary>
    public static int HashBlackHole(BlackHole? blackHole)
    {
        if (blackHole == null)
            return 0;

        return HashCode.Combine(
            blackHole.Position,
            blackHole.Mass,
            blackHole.Spin,
            blackHole.SpinAxis,
            blackHole.DiskInnerRadius,
            blackHole.DiskOuterRadius);
    }
}
