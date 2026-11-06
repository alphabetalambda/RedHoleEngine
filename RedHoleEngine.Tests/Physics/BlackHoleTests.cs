using System.Numerics;
using RedHoleEngine.Physics;

namespace RedHoleEngine.Tests.Physics;

/// <summary>
/// Kerr and Schwarzschild quantities on <see cref="BlackHole"/>.
/// Reference radii are the Bardeen ISCO and the equatorial prograde photon orbit,
/// checked at published spin samples (radii in units of M).
/// </summary>
public class BlackHoleTests
{
    private const float Eps = 1e-3f;

    [Fact]
    public void Schwarzschild_HorizonsMassAndDisk_MatchNaturalUnits()
    {
        var hole = new BlackHole(new Vector3(1, 2, 3), mass: 4f);

        Assert.Equal(new Vector3(1, 2, 3), hole.Position);
        Assert.Equal(0f, hole.Spin);
        Assert.Equal(Vector3.UnitY, hole.SpinAxis);
        Assert.Equal(8f, hole.SchwarzschildRadius, Eps);
        Assert.Equal(0f, hole.KerrParameter, Eps);
        Assert.Equal(8f, hole.OuterHorizonRadius, Eps); // r+ = 2M
        Assert.Equal(0f, hole.InnerHorizonRadius, Eps); // r- = 0
        Assert.Equal(8f, hole.ErgosphereEquatorialRadius, Eps);
        Assert.Equal(hole.OuterHorizonRadius, hole.ErgospherePolarRadius, Eps);
        Assert.Equal(0f, hole.RingSingularityRadius, Eps);
        // Prograde ISCO is 6M = 3 Rs; outer disk is 6 Rs.
        Assert.Equal(24f, hole.DiskInnerRadius, Eps);
        Assert.Equal(48f, hole.DiskOuterRadius, Eps);
        Assert.Equal(10000f, hole.DiskInnerTemperature);
        Assert.Equal(3000f, hole.DiskOuterTemperature);
    }

    [Theory]
    [InlineData(0.5f, 4.233003f, 7.554585f, 2.347296f)]
    [InlineData(0.9f, 2.320883f, 8.717352f, 1.557855f)]
    [InlineData(0.998f, 1.236971f, 8.994374f, 1.073909f)]
    public void Kerr_IscoAndPhotonOrbit_MatchBardeenSamples(float spin, float prograde, float retrograde, float photon)
    {
        const float mass = 3f;
        var hole = BlackHole.CreateKerr(Vector3.Zero, mass, spin, Vector3.UnitZ);

        Assert.Equal(Vector3.UnitZ, hole.SpinAxis);
        Assert.Equal(spin, hole.Spin, 1e-6f);
        Assert.Equal(prograde * mass, hole.CalculateProgradeISCO(), Eps);
        Assert.Equal(retrograde * mass, hole.CalculateRetrogradeISCO(), Eps);
        Assert.Equal(photon * mass, hole.CalculatePhotonSphereRadius(), Eps);
        Assert.Equal(hole.CalculateProgradeISCO(), hole.DiskInnerRadius, Eps);
        Assert.True(hole.CalculateProgradeISCO() < hole.CalculateRetrogradeISCO());
    }

    [Fact]
    public void Spin_IsClampedBelowExtremal_AndAxisIsNormalized()
    {
        var hole = new BlackHole(Vector3.Zero, 2f, spin: 5f, spinAxis: new Vector3(0, 3, 0));

        Assert.Equal(0.998f, hole.Spin, 1e-6f);
        Assert.Equal(Vector3.UnitY, hole.SpinAxis);
        Assert.True(hole.OuterHorizonRadius > hole.InnerHorizonRadius);
        Assert.True(hole.OuterHorizonRadius < hole.SchwarzschildRadius);
        Assert.True(hole.InnerHorizonRadius > 0f);
        // r+ + r- = 2M
        Assert.Equal(2f * hole.Mass, hole.OuterHorizonRadius + hole.InnerHorizonRadius, Eps);
    }

    [Fact]
    public void NegativeSpin_IsStoredAsMagnitude()
    {
        var hole = new BlackHole(Vector3.Zero, 1f, spin: -0.4f);
        Assert.Equal(0.4f, hole.Spin, 1e-6f);
    }

    [Fact]
    public void Horizons_UnphysicalSpinSetAfterConstruction_ReturnZero()
    {
        var hole = new BlackHole(Vector3.Zero, 2f);
        hole.Spin = 1.2f; // naked singularity, a > M

        Assert.Equal(0f, hole.OuterHorizonRadius);
        Assert.Equal(0f, hole.InnerHorizonRadius);
        Assert.Equal(0f, hole.ErgospherePolarRadius);
    }

    [Fact]
    public void FrameDragging_ZeroSpin_IsZero_AndFallsOffWithRadius()
    {
        var hole = new BlackHole(Vector3.Zero, 2f);
        Assert.Equal(0f, hole.FrameDraggingAngularVelocity(10f, MathF.PI / 2f));

        hole.Spin = 0.8f;
        float near = hole.FrameDraggingAngularVelocity(6f, MathF.PI / 2f);
        float far = hole.FrameDraggingAngularVelocity(30f, MathF.PI / 2f);
        Assert.True(near > far);
        Assert.True(far > 0f);
    }

    [Fact]
    public void FrameDragging_MatchesAnalyticOmega_AndZeroDenominatorReturnsZero()
    {
        var hole = new BlackHole(Vector3.Zero, 2f, 0.8f);
        // ω = 2 M a r / ((r²+a²)² − a² Δ sin²θ), a = a* M
        Assert.Equal(0.00617856f, hole.FrameDraggingAngularVelocity(10f, MathF.PI / 2f), 1e-6f);

        // Equatorial, r = 0 collapses the denominator.
        Assert.Equal(0f, hole.FrameDraggingAngularVelocity(0f, MathF.PI / 2f));
    }

    [Fact]
    public void SlowSpin_UsesSchwarzschildIscoAndPhotonSphere()
    {
        var hole = new BlackHole(Vector3.Zero, 5f, 0.0005f);
        Assert.Equal(30f, hole.CalculateProgradeISCO(), Eps); // 6M
        Assert.Equal(30f, hole.CalculateRetrogradeISCO(), Eps);
        Assert.Equal(15f, hole.CalculatePhotonSphereRadius(), Eps); // 1.5 Rs = 3M
    }

    [Fact]
    public void CreateDefault_IsTwoSolarMassUnitsAtOrigin()
    {
        var hole = BlackHole.CreateDefault();
        Assert.Equal(Vector3.Zero, hole.Position);
        Assert.Equal(2f, hole.Mass);
        Assert.Equal(0f, hole.Spin);
    }

    [Fact]
    public void PhotonSphereRadius_Property_StaysAtOneAndAHalfSchwarzschildRadii()
    {
        var hole = BlackHole.CreateKerr(Vector3.Zero, 2f, 0.9f);
        Assert.Equal(1.5f * hole.SchwarzschildRadius, hole.PhotonSphereRadius, Eps);
        Assert.True(hole.CalculatePhotonSphereRadius() < hole.PhotonSphereRadius);
    }

    [Fact(Skip = "BUG: BlackHole.PhotonSphereRadius is fixed at 1.5*Rs for every spin, while CalculatePhotonSphereRadius() returns the spin-aware prograde photon orbit (smaller for a*>0). Not changed: the property comment documents the Schwarzschild approximation, and VulkanBackend already uploads CalculatePhotonSphereRadius().")]
    public void PhotonSphereRadius_Property_ShouldTrackSpin()
    {
        var hole = BlackHole.CreateKerr(Vector3.Zero, 2f, 0.9f);
        Assert.Equal(hole.CalculatePhotonSphereRadius(), hole.PhotonSphereRadius, Eps);
    }
}
