namespace RedHoleEngine.Physics;

/// <summary>
/// Shared closed-form Kerr radii in natural units (G = c = 1), so that
/// <see cref="BlackHole"/> and <see cref="RedHoleEngine.Components.GravitySourceComponent"/>
/// report the same values. All radii are Boyer-Lindquist radii in the same length units as the
/// mass M (r_s = 2M). The spin argument is the dimensionless a* = a/M = J/M²; its magnitude is
/// used and clamped to [0, 1].
/// </summary>
public static class KerrMetric
{
    /// <summary>
    /// Below this |a*| the Schwarzschild values are returned exactly.
    /// </summary>
    public const float SchwarzschildSpinThreshold = 0.001f;

    /// <summary>
    /// Equatorial circular photon orbit radius (Bardeen, Press &amp; Teukolsky 1972):
    /// r_ph = 2M{1 + cos[(2/3) arccos(∓a*)]}, upper sign prograde.
    /// a* = 0 gives 3M for both; a* = 1 gives M (prograde) and 4M (retrograde).
    /// </summary>
    /// <param name="mass">Mass M</param>
    /// <param name="spin">Dimensionless spin a*</param>
    /// <param name="prograde">True for the co-rotating orbit, false for the counter-rotating orbit</param>
    public static float PhotonOrbitRadius(float mass, float spin, bool prograde = true)
    {
        float a = ClampSpin(spin);
        if (a < SchwarzschildSpinThreshold)
            return 3f * mass;

        float arg = prograde ? -a : a;
        return 2f * mass * (1f + MathF.Cos(2f / 3f * MathF.Acos(arg)));
    }

    /// <summary>
    /// Innermost stable circular orbit radius (Bardeen, Press &amp; Teukolsky 1972):
    /// r_isco = M{3 + Z2 ∓ sqrt[(3 − Z1)(3 + Z1 + 2 Z2)]}, upper sign prograde.
    /// a* = 0 gives 6M for both; a* = 1 gives M (prograde) and 9M (retrograde).
    /// </summary>
    /// <param name="mass">Mass M</param>
    /// <param name="spin">Dimensionless spin a*</param>
    /// <param name="prograde">True for the co-rotating orbit, false for the counter-rotating orbit</param>
    public static float Isco(float mass, float spin, bool prograde = true)
    {
        float a = ClampSpin(spin);
        if (a < SchwarzschildSpinThreshold)
            return 6f * mass;

        float z1 = 1 + MathF.Pow(1 - a * a, 1f / 3f) * (MathF.Pow(1 + a, 1f / 3f) + MathF.Pow(1 - a, 1f / 3f));
        float z2 = MathF.Sqrt(3 * a * a + z1 * z1);
        float root = MathF.Sqrt((3 - z1) * (3 + z1 + 2 * z2));
        return mass * (3 + z2 + (prograde ? -root : root));
    }

    private static float ClampSpin(float spin) => MathF.Min(MathF.Abs(spin), 1f);
}
