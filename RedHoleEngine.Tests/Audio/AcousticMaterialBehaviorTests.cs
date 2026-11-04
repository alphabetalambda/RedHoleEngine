using RedHoleEngine.Audio;

namespace RedHoleEngine.Tests.Audio;

public class AcousticMaterialBehaviorTests
{
    [Fact]
    public void FrequencyResponse_IndexLerpScaleAndAverage()
    {
        var a = FrequencyResponse.Uniform(0.25f);
        Assert.Equal(0.25f, a.Average);
        Assert.Equal(0.25f, a[FrequencyBand.Band2kHz]);

        a[FrequencyBand.Band8kHz] = 0.5f;
        Assert.Equal(0.5f, a.Band8kHz);
        a[(FrequencyBand)99] = 5f;
        Assert.Equal(0f, a[(FrequencyBand)99]);

        var b = a * 2f;
        Assert.Equal(0.5f, b.Band63Hz);
        Assert.Equal(1f, b.Band8kHz);

        var product = a * FrequencyResponse.Uniform(0.5f);
        Assert.Equal(0.125f, product.Band63Hz, 1e-5f);

        var mid = FrequencyResponse.Lerp(FrequencyResponse.Uniform(0f), FrequencyResponse.Uniform(1f), 0.25f);
        Assert.Equal(0.25f, mid.Average, 1e-5f);
    }

    [Fact]
    public void TransmissionLoss_GrowsWithFrequencyAndMass()
    {
        var concrete = AcousticMaterial.Concrete;
        var loss = concrete.CalculateTransmissionLoss(concrete.Thickness);
        Assert.True(loss.Band8kHz < loss.Band63Hz);
        Assert.True(loss.Band63Hz <= 1f);
        Assert.True(loss.Band8kHz >= 0f);

        var thicker = concrete.CalculateTransmissionLoss(concrete.Thickness * 4f);
        Assert.True(thicker.Band1kHz < loss.Band1kHz);

        // Tiny mass can clamp transmission loss at 0 dB (full transmission).
        var air = AcousticMaterial.Air;
        var open = air.CalculateTransmissionLoss(0.0000001f);
        Assert.Equal(1f, open.Band63Hz, 1e-3f);
    }

    [Fact]
    public void Presets_HaveTheDocumentedNamesAndExtremes()
    {
        Assert.Equal("Concrete", AcousticMaterial.Concrete.Name);
        Assert.Equal("Glass", AcousticMaterial.Glass.Name);
        Assert.Equal("Wood", AcousticMaterial.Wood.Name);
        Assert.Equal("Carpet", AcousticMaterial.Carpet.Name);
        Assert.Equal("Metal", AcousticMaterial.Metal.Name);
        Assert.Equal("Acoustic Foam", AcousticMaterial.AcousticFoam.Name);
        Assert.Equal("Air", AcousticMaterial.Air.Name);
        Assert.Equal("Spaceship Hull", AcousticMaterial.SpaceshipHull.Name);
        Assert.Equal("Event Horizon", AcousticMaterial.EventHorizon.Name);

        Assert.True(AcousticMaterial.Carpet.Absorption.Band8kHz > AcousticMaterial.Carpet.Absorption.Band63Hz);
        Assert.Equal(1f, AcousticMaterial.EventHorizon.Absorption.Average);
        Assert.Equal(0f, AcousticMaterial.EventHorizon.Transmission.Average);
        Assert.Equal(float.PositiveInfinity, AcousticMaterial.EventHorizon.Density);
        Assert.True(AcousticMaterial.Glass.Transmission.Band8kHz > AcousticMaterial.Glass.Transmission.Band63Hz);
        Assert.True(AcousticMaterial.AcousticFoam.Scattering > AcousticMaterial.Metal.Scattering);
        Assert.True(AcousticMaterial.SpaceshipHull.Transmission.Average < AcousticMaterial.Wood.Transmission.Average);
    }
}
