using System.Numerics;
using RedHoleEngine.Components;
using RedHoleEngine.Physics;

namespace RedHoleEngine.Tests.Physics;

/// <summary>
/// Kerr limits in natural units (G = c = 1, r_s = 2M): photon orbits, ISCO,
/// horizons, ergosphere and frame dragging, checked against the closed-form
/// Bardeen / Boyer-Lindquist values at a* = 0, intermediate spin and a* = 1.
/// </summary>
public class KerrMetricTests
{
    private const float Eps = 1e-3f;

    /// <summary>
    /// The constructor clamps spin to 0.998, so extremal holes set Spin directly.
    /// </summary>
    private static BlackHole Extremal(float mass)
    {
        var hole = new BlackHole(Vector3.Zero, mass) { Spin = 1f };
        return hole;
    }

    // ---- Photon orbit ----

    [Theory]
    [InlineData(1f)]
    [InlineData(4f)]
    public void PhotonSphere_NoSpin_IsOneAndAHalfSchwarzschildRadii(float mass)
    {
        var hole = new BlackHole(Vector3.Zero, mass);

        Assert.Equal(1.5f * hole.SchwarzschildRadius, hole.PhotonSphereRadius, Eps);
        Assert.Equal(3f * mass, hole.CalculatePhotonSphereRadius(), Eps);
        Assert.Equal(3f * mass, hole.CalculateRetrogradePhotonOrbitRadius(), Eps);
        Assert.Equal(3f * mass, GravitySourceComponent.CreateBlackHole(mass).PhotonSphereRadius, Eps);
    }

    [Fact]
    public void PhotonSphere_ExtremalPrograde_IsHalfSchwarzschildRadius()
    {
        const float mass = 3f;
        var hole = Extremal(mass);

        Assert.Equal(mass, hole.PhotonSphereRadius, Eps);
        Assert.Equal(0.5f * hole.SchwarzschildRadius, hole.CalculatePhotonSphereRadius(), Eps);
        Assert.Equal(mass, KerrMetric.PhotonOrbitRadius(mass, 1f, prograde: true), Eps);
    }

    [Fact]
    public void PhotonSphere_ExtremalRetrograde_IsFourM()
    {
        const float mass = 3f;
        var hole = Extremal(mass);

        Assert.Equal(4f * mass, hole.CalculateRetrogradePhotonOrbitRadius(), Eps);
        Assert.Equal(4f * mass, KerrMetric.PhotonOrbitRadius(mass, 1f, prograde: false), Eps);
    }

    [Theory]
    [InlineData(0.5f, 2.347296f, 3.532089f)]
    [InlineData(0.9f, 1.557855f, 3.910268f)]
    public void PhotonSphere_IntermediateSpin_MatchesBardeen(float spin, float progradeOverM, float retrogradeOverM)
    {
        const float mass = 2f;
        var hole = BlackHole.CreateKerr(Vector3.Zero, mass, spin);
        var source = GravitySourceComponent.CreateRotatingBlackHole(mass, spin, Vector3.UnitY);

        Assert.Equal(progradeOverM * mass, hole.PhotonSphereRadius, Eps);
        Assert.Equal(retrogradeOverM * mass, hole.CalculateRetrogradePhotonOrbitRadius(), Eps);
        // Component and BlackHole share one source of truth.
        Assert.Equal(hole.PhotonSphereRadius, source.PhotonSphereRadius, 1e-6f);
        Assert.Equal(hole.CalculateRetrogradePhotonOrbitRadius(), source.RetrogradePhotonOrbitRadius, 1e-6f);
    }

    [Fact]
    public void PhotonSphere_IsMonotonicInSpin_AndStaysOutsideHorizon()
    {
        const float mass = 1f;
        float previousPro = float.MaxValue;
        float previousRetro = 0f;
        for (int i = 0; i <= 10; i++)
        {
            float spin = i * 0.0998f;
            var hole = BlackHole.CreateKerr(Vector3.Zero, mass, spin);
            float pro = hole.PhotonSphereRadius;
            float retro = hole.CalculateRetrogradePhotonOrbitRadius();

            Assert.True(pro <= previousPro + 1e-5f);
            Assert.True(retro >= previousRetro - 1e-5f);
            Assert.True(pro > hole.OuterHorizonRadius);
            Assert.True(pro < hole.CalculateProgradeISCO());
            previousPro = pro;
            previousRetro = retro;
        }
    }

    [Fact]
    public void PhotonSphere_NegativeSpin_UsesMagnitude()
    {
        Assert.Equal(KerrMetric.PhotonOrbitRadius(1f, 0.7f), KerrMetric.PhotonOrbitRadius(1f, -0.7f), 1e-6f);
    }

    // ---- ISCO ----

    [Fact]
    public void Isco_NoSpin_IsSixM()
    {
        var hole = new BlackHole(Vector3.Zero, 2f);

        Assert.Equal(12f, hole.CalculateProgradeISCO(), Eps);
        Assert.Equal(12f, hole.CalculateRetrogradeISCO(), Eps);
        Assert.Equal(12f, KerrMetric.Isco(2f, 0f, prograde: true), Eps);
        Assert.Equal(12f, KerrMetric.Isco(2f, 0f, prograde: false), Eps);
    }

    [Fact]
    public void Isco_Extremal_IsMProgradeAndNineMRetrograde()
    {
        const float mass = 2f;
        var hole = Extremal(mass);

        Assert.Equal(mass, hole.CalculateProgradeISCO(), Eps);
        Assert.Equal(9f * mass, hole.CalculateRetrogradeISCO(), Eps);
    }

    [Fact]
    public void Isco_ComponentMatchesBlackHole()
    {
        var hole = BlackHole.CreateKerr(Vector3.Zero, 5f, 0.7f);
        var source = GravitySourceComponent.CreateRotatingBlackHole(5f, 0.7f, Vector3.UnitY);

        Assert.Equal(hole.CalculateProgradeISCO(), source.ProgradeISCO, 1e-6f);
        Assert.Equal(hole.CalculateRetrogradeISCO(), source.RetrogradeISCO, 1e-6f);
    }

    // ---- Horizons ----

    [Theory]
    [InlineData(1f, 0f)]
    [InlineData(5f, 0.6f)]
    [InlineData(2f, 0.9f)]
    [InlineData(3f, 0.998f)]
    public void Horizons_MatchClosedForm(float mass, float spin)
    {
        var hole = BlackHole.CreateKerr(Vector3.Zero, mass, spin);
        float a = spin * mass;
        float root = MathF.Sqrt(mass * mass - a * a);

        Assert.Equal(a, hole.KerrParameter, 1e-5f);
        Assert.Equal(mass + root, hole.OuterHorizonRadius, Eps);
        Assert.Equal(mass - root, hole.InnerHorizonRadius, Eps);
        // r+ + r- = 2M and r+ r- = a²
        Assert.Equal(2f * mass, hole.OuterHorizonRadius + hole.InnerHorizonRadius, Eps);
        Assert.Equal(a * a, hole.OuterHorizonRadius * hole.InnerHorizonRadius, Eps);
    }

    [Fact]
    public void Horizons_Extremal_CoincideAtM()
    {
        var hole = Extremal(4f);

        Assert.Equal(4f, hole.OuterHorizonRadius, Eps);
        Assert.Equal(4f, hole.InnerHorizonRadius, Eps);
    }

    // ---- Ergosphere ----

    [Theory]
    [InlineData(0f)]
    [InlineData(0.5f)]
    [InlineData(0.998f)]
    public void Ergosphere_EquatorIsTwoM_PoleTouchesHorizon(float spin)
    {
        const float mass = 3f;
        var hole = BlackHole.CreateKerr(Vector3.Zero, mass, spin);
        var source = GravitySourceComponent.CreateRotatingBlackHole(mass, spin, Vector3.UnitY);

        Assert.Equal(2f * mass, hole.ErgosphereEquatorialRadius, Eps);
        Assert.Equal(hole.OuterHorizonRadius, hole.ErgospherePolarRadius, Eps);
        Assert.Equal(2f * mass, source.ErgosphereRadius(MathF.PI / 2f), Eps);
        Assert.Equal(source.OuterHorizonRadius, source.ErgosphereRadius(0f), Eps);
        Assert.True(hole.ErgosphereEquatorialRadius >= hole.OuterHorizonRadius);
    }

    // ---- Frame dragging ----

    [Fact]
    public void FrameDragging_AtOuterHorizon_EqualsHorizonAngularVelocity()
    {
        // Ω_H = a / (r+² + a²) = a / (2 M r+)
        var hole = BlackHole.CreateKerr(Vector3.Zero, 2f, 0.8f);
        float a = hole.KerrParameter;
        float rPlus = hole.OuterHorizonRadius;
        float omegaH = a / (2f * hole.Mass * rPlus);

        Assert.Equal(omegaH, hole.FrameDraggingAngularVelocity(rPlus, MathF.PI / 2f), 1e-5f);
        Assert.Equal(omegaH, hole.FrameDraggingAngularVelocity(rPlus, 0.3f), 1e-5f);
    }

    [Fact]
    public void FrameDragging_Equatorial_MatchesReducedForm_AndFallsOffAsInverseCube()
    {
        // Equatorial: ω = 2Ma / (r³ + a²r + 2Ma²); far field ω → 2J/r³ with J = Ma.
        var hole = BlackHole.CreateKerr(Vector3.Zero, 1f, 0.9f);
        float m = hole.Mass;
        float a = hole.KerrParameter;

        foreach (float r in new[] { 3f, 10f })
        {
            float expected = 2f * m * a / (r * r * r + a * a * r + 2f * m * a * a);
            Assert.Equal(expected, hole.FrameDraggingAngularVelocity(r, MathF.PI / 2f), 1e-5f);
        }

        const float far = 1000f;
        float farOmega = hole.FrameDraggingAngularVelocity(far, MathF.PI / 2f);
        Assert.Equal(2f * m * a / (far * far * far), farOmega, 1e-10f);
    }

    [Fact]
    public void FrameDragging_NoSpin_IsZero()
    {
        var hole = new BlackHole(Vector3.Zero, 2f);
        Assert.Equal(0f, hole.FrameDraggingAngularVelocity(10f, MathF.PI / 2f));
    }

    [Fact]
    public void FrameDragging_ComponentMatchesBlackHole()
    {
        var hole = BlackHole.CreateKerr(Vector3.Zero, 2f, 0.6f);
        var source = GravitySourceComponent.CreateRotatingBlackHole(2f, 0.6f, Vector3.UnitY);

        Assert.Equal(hole.FrameDraggingAngularVelocity(7f, 1.1f), source.FrameDraggingAngularVelocity(7f, 1.1f), 1e-7f);
    }
}
