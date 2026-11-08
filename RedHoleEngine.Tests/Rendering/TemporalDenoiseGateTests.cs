using System.Numerics;
using RedHoleEngine.Components;
using RedHoleEngine.Physics;
using RedHoleEngine.Rendering;
using RedHoleEngine.Rendering.Denoising;

namespace RedHoleEngine.Tests.Rendering;

public class TemporalDenoiseGateTests
{
    private static readonly TemporalGateDecision Allowed = new(true, TemporalDenoiseGate.AllowedReason);

    [Fact]
    public void NoGravity_IsAllowed()
    {
        Assert.True(TemporalDenoiseGate.IsTemporalAllowed(null));
        Assert.True(TemporalDenoiseGate.IsTemporalAllowed(null, Array.Empty<GravitySourceComponent>(), new RaytracerSettings()));
        Assert.Equal(TemporalDenoiseGate.AllowedReason, TemporalDenoiseGate.Evaluate(null).Reason);
    }

    [Fact]
    public void ActiveBlackHole_IsDeniedWithReason()
    {
        var decision = TemporalDenoiseGate.Evaluate(new BlackHole(Vector3.Zero, 2f));
        Assert.False(decision.Allowed);
        Assert.Contains("lensing bends rays", decision.Reason);
    }

    [Fact]
    public void DefaultBlackHole_IsDenied()
    {
        Assert.False(TemporalDenoiseGate.IsTemporalAllowed(BlackHole.CreateDefault()));
    }

    [Theory]
    [InlineData(0.0f)]
    [InlineData(0.5f)]
    [InlineData(0.998f)]
    public void RotatingBlackHole_AnySpin_IsDenied(float spin)
    {
        var bh = new BlackHole(Vector3.One, 3f, spin, Vector3.UnitX);
        Assert.False(TemporalDenoiseGate.IsTemporalAllowed(bh));
    }

    [Theory]
    [InlineData(1e-30f)]
    [InlineData(-1f)]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    public void BlackHole_TinyNegativeOrNonFiniteMass_IsDeniedConservatively(float mass)
    {
        Assert.False(TemporalDenoiseGate.IsTemporalAllowed(new BlackHole(Vector3.Zero, 1f) { Mass = mass }));
    }

    [Fact]
    public void BlackHole_ZeroMass_IsAllowed_EvenWithSpinDiskAndOverlays()
    {
        // Mass 0 => renderer uploads all-zero lensing uniforms => straight rays.
        var bh = new BlackHole(Vector3.Zero, 1f, 0.9f, Vector3.UnitY)
        {
            Mass = 0f,
            DiskInnerRadius = 5f,
            DiskOuterRadius = 20f
        };
        var settings = new RaytracerSettings { ShowErgosphere = true, ShowPhotonSphere = true, LensingQuality = LensingQuality.Ultra };
        Assert.True(TemporalDenoiseGate.IsTemporalAllowed(bh, null, settings));
    }

    [Theory]
    [InlineData(LensingQuality.Low)]
    [InlineData(LensingQuality.Medium)]
    [InlineData(LensingQuality.High)]
    [InlineData(LensingQuality.Ultra)]
    [InlineData(LensingQuality.Custom)]
    public void EveryLensingQuality_WithActiveBlackHole_IsDenied(LensingQuality quality)
    {
        var settings = new RaytracerSettings();
        settings.ApplyLensingQuality(quality);
        Assert.False(TemporalDenoiseGate.IsTemporalAllowed(new BlackHole(Vector3.Zero, 2f), null, settings));
    }

    [Fact]
    public void BlackHoleVisualisationSettings_WithActiveBlackHole_AreDenied()
    {
        var bh = new BlackHole(Vector3.Zero, 2f, 0.7f);
        var settings = new RaytracerSettings { ShowErgosphere = true, ShowPhotonSphere = true };
        Assert.False(TemporalDenoiseGate.IsTemporalAllowed(bh, null, settings));
        Assert.False(TemporalDenoiseGate.IsTemporalAllowed(bh, null, new RaytracerSettings { ShowErgosphere = false, ShowPhotonSphere = false }));
    }

    [Fact]
    public void SchwarzschildAndKerrSources_AreDenied_RegardlessOfAffectsLight()
    {
        var schw = GravitySourceComponent.CreateBlackHole(2f);
        schw.AffectsLight = false;
        var kerr = GravitySourceComponent.CreateRotatingBlackHole(2f, 0.5f, Vector3.UnitY);
        kerr.AffectsLight = false;

        Assert.False(TemporalDenoiseGate.IsTemporalAllowed(null, new[] { schw }));
        Assert.False(TemporalDenoiseGate.IsTemporalAllowed(null, new[] { kerr }));
    }

    [Fact]
    public void ZeroMassBlackHoleSource_IsAllowed()
    {
        var schw = GravitySourceComponent.CreateBlackHole(2f);
        schw.Mass = 0f;
        Assert.True(TemporalDenoiseGate.IsTemporalAllowed(null, new[] { schw }));
    }

    [Fact]
    public void NewtonianAndUniform_DenyOnlyWhenAffectingLight()
    {
        var newt = GravitySourceComponent.CreateNewtonian(10f);
        newt.AffectsLight = true;
        Assert.False(TemporalDenoiseGate.IsTemporalAllowed(null, new[] { newt }));
        newt.AffectsLight = false;
        Assert.True(TemporalDenoiseGate.IsTemporalAllowed(null, new[] { newt }));

        var uni = GravitySourceComponent.CreateUniform(-Vector3.UnitY, 9.8f);
        uni.AffectsLight = true;
        Assert.False(TemporalDenoiseGate.IsTemporalAllowed(null, new[] { uni }));
        uni.AffectsLight = false;
        Assert.True(TemporalDenoiseGate.IsTemporalAllowed(null, new[] { uni }));
    }

    [Fact]
    public void AnyOneDenyingSource_AmongMany_Denies()
    {
        var harmless = GravitySourceComponent.CreateNewtonian(5f);
        harmless.AffectsLight = false;
        var bh = GravitySourceComponent.CreateBlackHole(1f);
        Assert.False(TemporalDenoiseGate.IsTemporalAllowed(null, new[] { harmless, harmless, bh }));
    }

    [Fact]
    public void UnknownGravityType_IsDenied()
    {
        var weird = GravitySourceComponent.CreateNewtonian(1f);
        weird.GravityType = (GravityType)999;
        Assert.False(TemporalDenoiseGate.IsTemporalAllowed(null, new[] { weird }));
    }

    // ---- History tracker: gate flips must reset history -------------------------------------------------

    [Fact]
    public void Tracker_FirstFrame_RunsWithoutHistory_ThenReadsHistory()
    {
        var t = new TemporalHistoryTracker();
        var p1 = t.BeginFrame(true, Allowed, false);
        Assert.True(p1.Run);
        Assert.False(p1.HistoryValid);
        t.EndFrame(p1);

        var p2 = t.BeginFrame(true, Allowed, false);
        Assert.True(p2.Run);
        Assert.True(p2.HistoryValid);
        Assert.Equal(3, p2.ShaderFlags);
        Assert.Equal(1, p1.ShaderFlags);
    }

    [Fact]
    public void Tracker_GatedFrame_NeverRuns_AndClearsFlags()
    {
        var t = new TemporalHistoryTracker();
        var denied = TemporalDenoiseGate.Evaluate(BlackHole.CreateDefault());
        var plan = t.BeginFrame(true, denied, false);
        Assert.False(plan.Run);
        Assert.False(plan.HistoryValid);
        Assert.Equal(0, plan.ShaderFlags);   // bit0 clear => shader passes through
        t.EndFrame(plan);                    // must not mark history valid
        Assert.False(t.HistoryValid);
    }

    [Fact]
    public void Tracker_GateFlip_InvalidatesHistory_SoStaleHistoryIsNeverBlended()
    {
        var t = new TemporalHistoryTracker();
        var denied = TemporalDenoiseGate.Evaluate(BlackHole.CreateDefault());

        // build history while allowed
        for (int i = 0; i < 3; i++) t.EndFrame(t.BeginFrame(true, Allowed, false));
        Assert.True(t.HistoryValid);

        // gate flips off for one frame
        var off = t.BeginFrame(true, denied, false);
        Assert.False(off.Run);
        Assert.False(t.HistoryValid);

        // gate flips back on: first frame must not read history
        var on = t.BeginFrame(true, Allowed, false);
        Assert.True(on.Run);
        Assert.False(on.HistoryValid);
        t.EndFrame(on);
        Assert.True(t.BeginFrame(true, Allowed, false).HistoryValid);
    }

    [Fact]
    public void Tracker_UserDisable_AndForceReset_InvalidateHistory()
    {
        var t = new TemporalHistoryTracker();
        t.EndFrame(t.BeginFrame(true, Allowed, false));
        Assert.True(t.HistoryValid);

        var reset = t.BeginFrame(true, Allowed, true);
        Assert.True(reset.Run);
        Assert.False(reset.HistoryValid);

        t.EndFrame(reset);
        t.BeginFrame(false, Allowed, false);
        Assert.False(t.HistoryValid);

        t.EndFrame(t.BeginFrame(true, Allowed, false));
        t.Invalidate();
        Assert.False(t.HistoryValid);
    }

    // ---- Reprojection / validation math (CPU mirror of the shader) --------------------------------------

    [Fact]
    public void Reproject_SubtractsMotion_AndHandlesPixelCentres()
    {
        Assert.Equal((10, 20), TemporalReprojection.ReprojectPixel(10, 20, Vector2.Zero));
        Assert.Equal((8, 21), TemporalReprojection.ReprojectPixel(10, 20, new Vector2(2, -1)));
        // Sub-pixel motion floors the shifted centre
        Assert.Equal((9, 20), TemporalReprojection.ReprojectPixel(10, 20, new Vector2(0.75f, 0f)));
        Assert.Equal((10, 20), TemporalReprojection.ReprojectPixel(10, 20, new Vector2(0.25f, 0f)));
    }

    [Fact]
    public void Bounds_Check()
    {
        Assert.True(TemporalReprojection.InBounds(0, 0, 16, 16));
        Assert.True(TemporalReprojection.InBounds(15, 15, 16, 16));
        Assert.False(TemporalReprojection.InBounds(-1, 0, 16, 16));
        Assert.False(TemporalReprojection.InBounds(16, 0, 16, 16));
        Assert.False(TemporalReprojection.InBounds(0, 16, 16, 16));
    }

    [Fact]
    public void NormalRejection()
    {
        Assert.True(TemporalReprojection.NormalsConsistent(Vector3.UnitY, Vector3.UnitY * 2f, 0.9f));
        Assert.False(TemporalReprojection.NormalsConsistent(Vector3.UnitY, Vector3.UnitX, 0.9f));
        Assert.False(TemporalReprojection.NormalsConsistent(Vector3.UnitY, Vector3.Zero, 0.9f));
        var tilted = Vector3.Normalize(new Vector3(0.2f, 1f, 0f));
        Assert.True(TemporalReprojection.NormalsConsistent(Vector3.UnitY, tilted, 0.9f));
        Assert.False(TemporalReprojection.NormalsConsistent(Vector3.UnitY, tilted, 0.99f));
    }

    [Fact]
    public void DepthRejection()
    {
        Assert.True(TemporalReprojection.DepthsConsistent(10f, 10.5f, 0.1f));
        Assert.False(TemporalReprojection.DepthsConsistent(10f, 12f, 0.1f));
        Assert.False(TemporalReprojection.DepthsConsistent(10f, 0f, 0.1f));   // sky in history
        Assert.False(TemporalReprojection.DepthsConsistent(0f, 10f, 0.1f));   // sky now
    }

    [Fact]
    public void IsHistoryValid_CombinesAllChecks()
    {
        bool Check(int x, int y, Vector3 pn, float pd) =>
            TemporalReprojection.IsHistoryValid(x, y, 16, 16, Vector3.UnitY, 10f, pn, pd, 0.9f, 0.1f);

        Assert.True(Check(5, 5, Vector3.UnitY, 10f));
        Assert.False(Check(-1, 5, Vector3.UnitY, 10f));
        Assert.False(Check(5, 5, Vector3.UnitX, 10f));
        Assert.False(Check(5, 5, Vector3.UnitY, 20f));
    }

    [Fact]
    public void BlendAlpha_ShortHistoryIsFast_LongHistoryIsFlooredByStrength()
    {
        Assert.Equal(1f, TemporalReprojection.BlendAlpha(1f, 0.1f));
        Assert.Equal(0.5f, TemporalReprojection.BlendAlpha(2f, 0.1f));
        Assert.Equal(0.1f, TemporalReprojection.BlendAlpha(32f, 0.1f), 5);
        Assert.Equal(0.3f, TemporalReprojection.BlendAlpha(32f, 0.3f), 5);
        Assert.Equal(1f, TemporalReprojection.BlendAlpha(0f, 0.1f));
    }

    [Fact]
    public void HistoryLength_IsCapped()
    {
        Assert.Equal(2f, TemporalReprojection.NextHistoryLength(1f, 32));
        Assert.Equal(32f, TemporalReprojection.NextHistoryLength(32f, 32));
    }

    [Fact]
    public void VarianceClamp_PullsGhostedHistoryIntoNeighbourhood()
    {
        Assert.Equal(0.6f, TemporalReprojection.ClampToNeighbourhood(0.9f, 0.5f, 0.1f, 1f), 5);
        Assert.Equal(0.4f, TemporalReprojection.ClampToNeighbourhood(0.0f, 0.5f, 0.1f, 1f), 5);
        Assert.Equal(0.52f, TemporalReprojection.ClampToNeighbourhood(0.52f, 0.5f, 0.1f, 1f), 5);
    }

    // ---- Settings / params ---------------------------------------------------------------------------------

    [Fact]
    public void Settings_DefaultOff_AndClamped()
    {
        var s = new RaytracerSettings();
        Assert.False(s.TemporalDenoise);

        s.TemporalMaxHistory = 1000;
        s.TemporalNormalThreshold = -5f;
        s.TemporalDepthThreshold = 10f;
        s.TemporalVarianceClamp = 0f;
        s.Clamp();
        Assert.Equal(64, s.TemporalMaxHistory);
        Assert.Equal(0.5f, s.TemporalNormalThreshold);
        Assert.Equal(0.5f, s.TemporalDepthThreshold);
        Assert.Equal(0.5f, s.TemporalVarianceClamp);
    }

    [Fact]
    public void TemporalParams_CarrySettingsAndFlags_AndStructIs32Bytes()
    {
        var s = new RaytracerSettings { TemporalMaxHistory = 16, TemporalVarianceClamp = 2f, SVGFTemporalAlpha = 0.2f };
        var d = new Denoiser(s);
        d.Initialize(64, 32);
        var p = d.GetSVGFTemporalParams(3);
        Assert.Equal(3, p.Flags);
        Assert.Equal(16, p.MaxHistoryLength);
        Assert.Equal(2f, p.VarianceClampGamma);
        Assert.Equal(0.2f, p.TemporalAlpha);
        Assert.Equal(0, d.GetSVGFTemporalParams().Flags);
        Assert.Equal(32, System.Runtime.CompilerServices.Unsafe.SizeOf<SVGFTemporalParams>());
    }
}
