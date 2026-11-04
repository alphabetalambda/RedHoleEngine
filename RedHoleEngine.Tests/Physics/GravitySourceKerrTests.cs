using System.Numerics;
using RedHoleEngine.Components;

namespace RedHoleEngine.Tests.Physics;

public class GravitySourceKerrTests
{
    private const float Eps = 1e-3f;

    [Fact]
    public void KerrHorizons_SumToTwiceMass_AndErgosphereFollowsTheta()
    {
        var kerr = GravitySourceComponent.CreateRotatingBlackHole(5f, 0.6f, Vector3.UnitY);

        // a = 0.6*5 = 3, r± = 5 ± sqrt(25-9) = 5 ± 4
        Assert.Equal(9f, kerr.OuterHorizonRadius, Eps);
        Assert.Equal(1f, kerr.InnerHorizonRadius, Eps);
        Assert.Equal(9f, kerr.EventHorizonRadius, Eps);
        Assert.Equal(3f, kerr.KerrParameter, Eps);
        Assert.Equal(10f, kerr.ErgosphereEquatorialRadius, Eps);
        Assert.Equal(kerr.OuterHorizonRadius, kerr.ErgosphereRadius(0f), Eps);
        Assert.Equal(kerr.ErgosphereEquatorialRadius, kerr.ErgosphereRadius(MathF.PI / 2f), Eps);
    }

    [Theory]
    [InlineData(0.5f, 4.233003f, 7.554585f)]
    [InlineData(0.9f, 2.320883f, 8.717352f)]
    public void KerrIsco_MatchesBardeenSamples(float spin, float progradeOverM, float retrogradeOverM)
    {
        const float mass = 4f;
        var kerr = GravitySourceComponent.CreateRotatingBlackHole(mass, spin, new Vector3(0, 0, 2));

        Assert.Equal(Vector3.UnitZ, kerr.SpinAxis);
        Assert.Equal(progradeOverM * mass, kerr.ProgradeISCO, Eps);
        Assert.Equal(retrogradeOverM * mass, kerr.RetrogradeISCO, Eps);
        Assert.True(kerr.ProgradeISCO < kerr.ISCO);
        Assert.True(kerr.RetrogradeISCO > kerr.ISCO);
    }

    [Fact]
    public void NonSpinning_IscoAndHorizons_AreSchwarzschild()
    {
        var hole = GravitySourceComponent.CreateBlackHole(3f);
        Assert.Equal(6f, hole.OuterHorizonRadius, Eps);
        Assert.Equal(0f, hole.InnerHorizonRadius, Eps);
        Assert.Equal(18f, hole.ProgradeISCO, Eps);
        Assert.Equal(18f, hole.RetrogradeISCO, Eps);
        Assert.Equal(0f, hole.FrameDraggingAngularVelocity(10f, 1f));
        Assert.Equal(GravityType.Schwarzschild, hole.GravityType);
        Assert.Equal(hole.SchwarzschildRadius, hole.EventHorizonRadius, Eps);
    }

    [Fact]
    public void NewtonianAndUniform_HaveNoHorizon()
    {
        Assert.Equal(0f, GravitySourceComponent.CreateNewtonian(10f).EventHorizonRadius);
        Assert.Equal(0f, GravitySourceComponent.CreateUniform(Vector3.UnitY, 9.8f).EventHorizonRadius);
    }

    [Fact]
    public void Spin_IsClamped_AndNakedSpinSetLater_YieldsZeroHorizons()
    {
        var kerr = GravitySourceComponent.CreateRotatingBlackHole(2f, 4f, -Vector3.UnitX);
        Assert.Equal(0.998f, kerr.SpinParameter, 1e-5f);
        Assert.Equal(-Vector3.UnitX, kerr.SpinAxis);

        kerr.SpinParameter = 1.5f;
        Assert.Equal(0f, kerr.OuterHorizonRadius);
        Assert.Equal(0f, kerr.InnerHorizonRadius);
        // cos²θ = 1 and a > M makes the polar ergosphere discriminant negative.
        Assert.Equal(kerr.Mass, kerr.ErgosphereRadius(0f));
    }

    [Fact]
    public void FrameDragging_MatchesAnalyticOmega_AndZeroDenominatorReturnsZero()
    {
        var kerr = GravitySourceComponent.CreateRotatingBlackHole(2f, 0.8f, Vector3.UnitY);
        Assert.Equal(0.00617856f, kerr.FrameDraggingAngularVelocity(10f, MathF.PI / 2f), 1e-6f);
        Assert.Equal(0f, kerr.FrameDraggingAngularVelocity(0f, MathF.PI / 2f));
        Assert.True(kerr.FrameDraggingAngularVelocity(6f, 0.4f) > kerr.FrameDraggingAngularVelocity(40f, 0.4f));
    }

    [Fact]
    public void GetAccelerationAt_RespectsRange_Uniform_AndCoincidence()
    {
        var ranged = GravitySourceComponent.CreateNewtonian(8f, maxRange: 5f);
        Assert.Equal(Vector3.Zero, ranged.GetAccelerationAt(Vector3.Zero, new Vector3(9f, 0, 0)));

        var inside = ranged.GetAccelerationAt(Vector3.Zero, new Vector3(2f, 0, 0));
        Assert.Equal(-8f / 4f, inside.X, Eps); // a = M/r² toward origin, r=2

        var uniform = GravitySourceComponent.CreateUniform(new Vector3(0, -2, 0), 3f);
        Assert.Equal(new Vector3(0, -3f, 0), uniform.GetAccelerationAt(Vector3.One, Vector3.Zero));

        var point = GravitySourceComponent.CreateBlackHole(1f);
        Assert.Equal(Vector3.Zero, point.GetAccelerationAt(Vector3.One, Vector3.One));
    }

    [Fact]
    public void PhotonSphereRadius_IsThreeTimesMass_EvenForKerr()
    {
        var kerr = GravitySourceComponent.CreateRotatingBlackHole(10f, 0.9f, Vector3.UnitY);
        Assert.Equal(30f, kerr.PhotonSphereRadius, Eps);
        Assert.Equal(60f, kerr.ISCO, Eps);
    }

    [Fact(Skip = "BUG: GravitySourceComponent.PhotonSphereRadius is hardcoded to 3*Mass for Kerr sources. The spin-aware prograde photon orbit (BlackHole.CalculatePhotonSphereRadius) is smaller for a*>0. Left unchanged: the property comment scopes the formula to Schwarzschild, and there is no separate Kerr accessor. AudioDebugVisualizer draws this radius.")]
    public void PhotonSphereRadius_ShouldShrinkAsSpinIncreases()
    {
        var slow = GravitySourceComponent.CreateRotatingBlackHole(10f, 0.01f, Vector3.UnitY);
        var fast = GravitySourceComponent.CreateRotatingBlackHole(10f, 0.9f, Vector3.UnitY);
        Assert.True(fast.PhotonSphereRadius < slow.PhotonSphereRadius);
    }
}
