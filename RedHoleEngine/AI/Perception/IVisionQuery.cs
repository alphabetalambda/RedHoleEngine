using System.Numerics;

namespace RedHoleEngine.AI.Perception;

/// <summary>
/// Optional occlusion test plugged into <see cref="PerceptionSystem"/>.
/// Return true when the segment from the eye to the target is blocked.
/// The default (no query) treats the view cone as enough, which keeps tests headless.
/// </summary>
public interface IVisionQuery
{
    bool IsOccluded(Vector3 from, Vector3 to);
}
