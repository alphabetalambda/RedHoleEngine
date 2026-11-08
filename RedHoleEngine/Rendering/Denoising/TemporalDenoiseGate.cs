using RedHoleEngine.Components;
using RedHoleEngine.Physics;

namespace RedHoleEngine.Rendering.Denoising;

/// <summary>
/// Result of the temporal denoise gate: whether temporal reprojection may run, and a human readable reason.
/// </summary>
public readonly record struct TemporalGateDecision(bool Allowed, string Reason);

/// <summary>
/// HARD SAFETY RULE: temporal denoising (history reprojection) must never run when gravity can bend rays.
///
/// Temporal reprojection assumes straight camera rays: a surface point seen at pixel p this frame is
/// re-projected into the previous frame with a pinhole camera model. Gravitational lensing bends the rays,
/// so the pixel->world mapping (and therefore motion vectors, depth and normals) is no longer a function
/// of the camera alone; history would be smeared or ghosted.
///
/// This class is the single, pure decision function. It is deliberately conservative: when in doubt it denies.
/// See docs/TEMPORAL_DENOISE.md for the exact semantics and how to extend it.
/// </summary>
public static class TemporalDenoiseGate
{
    public const string AllowedReason = "No gravitational lensing active";

    public const string DeniedLensingReason = "Disabled: gravitational lensing bends rays";

    /// <summary>Convenience wrapper returning only the boolean.</summary>
    public static bool IsTemporalAllowed(
        BlackHole? blackHole,
        IEnumerable<GravitySourceComponent>? gravitySources = null,
        RaytracerSettings? settings = null)
        => Evaluate(blackHole, gravitySources, settings).Allowed;

    /// <summary>
    /// Decide whether temporal denoising is allowed.
    ///
    /// Semantics:
    ///  - A BlackHole whose Mass is anything other than exactly 0 (including negative, NaN, infinite) denies.
    ///    Mass == 0 (or no black hole) means the renderer feeds all-zero lensing uniforms, i.e. straight rays: allowed.
    ///    Spin, disk, ergosphere/photon-sphere overlays and lensing quality settings are derived from, or only act
    ///    through, the mass, so with mass 0 they cannot bend rays and do not deny on their own.
    ///  - A Schwarzschild/Kerr GravitySourceComponent with Mass != 0 denies regardless of AffectsLight, because the
    ///    application converts every such source into a lensing BlackHole.
    ///  - Newtonian/Uniform sources deny only if AffectsLight and they have a non-zero (or non-finite) strength.
    ///  - Unknown gravity types deny.
    /// The <paramref name="settings"/> parameter is reserved for future ray-bending settings that do not depend on mass.
    /// </summary>
    public static TemporalGateDecision Evaluate(
        BlackHole? blackHole,
        IEnumerable<GravitySourceComponent>? gravitySources = null,
        RaytracerSettings? settings = null)
    {
        if (blackHole != null && IsNonZero(blackHole.Mass))
            return Deny("black hole with non-zero mass");

        if (gravitySources != null)
        {
            foreach (var source in gravitySources)
            {
                switch (source.GravityType)
                {
                    case GravityType.Schwarzschild:
                    case GravityType.Kerr:
                        if (IsNonZero(source.Mass))
                            return Deny($"{source.GravityType} gravity source");
                        break;
                    case GravityType.Newtonian:
                        if (source.AffectsLight && IsNonZero(source.Mass))
                            return Deny("Newtonian gravity source affecting light");
                        break;
                    case GravityType.Uniform:
                        if (source.AffectsLight && IsNonZero(source.UniformStrength))
                            return Deny("uniform gravity field affecting light");
                        break;
                    default:
                        return Deny("unknown gravity source type");
                }
            }
        }

        return new TemporalGateDecision(true, AllowedReason);
    }

    private static bool IsNonZero(float value) => !(value == 0f);   // NaN -> true (deny)

    private static TemporalGateDecision Deny(string detail)
        => new(false, $"{DeniedLensingReason} ({detail})");
}
