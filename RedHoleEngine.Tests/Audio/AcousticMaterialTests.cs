using RedHoleEngine.Audio;

namespace RedHoleEngine.Tests.Audio;

public class AcousticMaterialTests
{
    [Fact]
    public void FrequencyResponse_IndexerRoundTripsEveryBand_AndCombinesLinearly()
    {
        var response = new FrequencyResponse();
        var bands = new[]
        {
            FrequencyBand.Band63Hz, FrequencyBand.Band125Hz, FrequencyBand.Band250Hz, FrequencyBand.Band500Hz,
            FrequencyBand.Band1kHz, FrequencyBand.Band2kHz, FrequencyBand.Band4kHz, FrequencyBand.Band8kHz
        };

        for (int i = 0; i < bands.Length; i++)
            response[bands[i]] = 0.1f * (i + 1);

        Assert.Equal(0.1f, response.Band63Hz);
        Assert.Equal(0.2f, response.Band125Hz);
        Assert.Equal(0.3f, response.Band250Hz);
        Assert.Equal(0.4f, response.Band500Hz);
        Assert.Equal(0.5f, response.Band1kHz);
        Assert.Equal(0.6f, response.Band2kHz);
        Assert.Equal(0.7f, response.Band4kHz);
        Assert.Equal(0.8f, response.Band8kHz);
        Assert.Equal(0.45f, response.Average, 1e-5);

        response[(FrequencyBand)42] = 9f;
        Assert.Equal(0f, response[(FrequencyBand)42]);

        var scaled = response * 2f;
        Assert.Equal(0.2f, scaled.Band63Hz, 1e-5);
        Assert.Equal(1.6f, scaled.Band8kHz, 1e-5);

        var product = response * FrequencyResponse.Uniform(0.5f);
        Assert.Equal(0.05f, product.Band63Hz, 1e-5);
        Assert.Equal(0.4f, product.Band8kHz, 1e-5);

        var start = FrequencyResponse.Lerp(FrequencyResponse.Uniform(0.2f), FrequencyResponse.Uniform(0.8f), 0f);
        var end = FrequencyResponse.Lerp(FrequencyResponse.Uniform(0.2f), FrequencyResponse.Uniform(0.8f), 1f);
        var mid = FrequencyResponse.Lerp(FrequencyResponse.Uniform(0.2f), FrequencyResponse.Uniform(0.8f), 0.5f);
        Assert.Equal(0.2f, start.Average, 1e-5);
        Assert.Equal(0.8f, end.Average, 1e-5);
        Assert.Equal(0.5f, mid.Average, 1e-5);
    }

    [Fact]
    public void TransmissionLoss_MatchesMassLaw_AndClampsNegativeDecibels()
    {
        var material = new AcousticMaterial { Density = 1f, Thickness = 1f };

        // TL(dB) = 20*log10(f * surfaceMass) - 47, surfaceMass = 1 kg/m².
        // 1 kHz: 20*log10(1000) - 47 = 13 dB → 10^(-1.3).
        float expected1k = MathF.Pow(10f, -13f / 10f);
        var loss = material.CalculateTransmissionLoss(1f);
        Assert.Equal(expected1k, loss.Band1kHz, 1e-5);

        // 63 Hz: 20*log10(63) - 47 is negative, so transmission is clamped to 1.
        float tl63 = 20f * MathF.Log10(63f) - 47f;
        Assert.True(tl63 < 0f);
        Assert.Equal(1f, loss.Band63Hz, 1e-5);

        // Higher bands lose more than lower bands, and the result never exceeds 1.
        Assert.True(loss.Band8kHz < loss.Band4kHz);
        Assert.True(loss.Band4kHz < loss.Band1kHz);
        Assert.True(loss.Band1kHz < loss.Band250Hz);
        Assert.InRange(loss.Band8kHz, 0f, 1f);

        var heavier = new AcousticMaterial { Density = 10f };
        var heavyLoss = heavier.CalculateTransmissionLoss(1f);
        Assert.True(heavyLoss.Band1kHz < loss.Band1kHz);

        // Zero surface mass cannot produce a positive transmission loss.
        var massless = new AcousticMaterial { Density = 0f };
        var open = massless.CalculateTransmissionLoss(1f);
        Assert.Equal(1f, open.Average, 1e-4);
    }

    [Fact]
    public void Presets_MatchPublishedAbsorptionTransmissionAndBulkProperties()
    {
        var concrete = AcousticMaterial.Concrete;
        Assert.Equal("Concrete", concrete.Name);
        Assert.Equal(0.2f, concrete.Thickness);
        Assert.Equal(2400f, concrete.Density);
        Assert.Equal(3400f, concrete.SpeedOfSound);
        Assert.Equal(0.1f, concrete.Scattering);
        Assert.True(concrete.Absorption.Band8kHz > concrete.Absorption.Band63Hz);
        Assert.Equal(0.01f, concrete.Transmission.Average, precision: 5);

        var glass = AcousticMaterial.Glass;
        Assert.Equal(0.006f, glass.Thickness);
        Assert.Equal(2500f, glass.Density);
        Assert.Equal(5500f, glass.SpeedOfSound);
        Assert.True(glass.Absorption.Band63Hz > glass.Absorption.Band8kHz);
        Assert.True(glass.Transmission.Band8kHz > glass.Transmission.Band63Hz);

        var wood = AcousticMaterial.Wood;
        Assert.Equal(0.02f, wood.Thickness);
        Assert.Equal(600f, wood.Density);
        Assert.Equal(4000f, wood.SpeedOfSound);
        Assert.True(wood.Transmission.Band63Hz > wood.Transmission.Band8kHz);

        var carpet = AcousticMaterial.Carpet;
        Assert.Equal(0f, carpet.Transmission.Average);
        Assert.Equal(0.7f, carpet.Scattering);
        Assert.Equal(200f, carpet.Density);
        Assert.True(carpet.Absorption.Band8kHz > 0.7f);
        Assert.True(carpet.Absorption.Band63Hz < 0.05f);

        var metal = AcousticMaterial.Metal;
        Assert.Equal(7800f, metal.Density);
        Assert.Equal(5100f, metal.SpeedOfSound);
        Assert.Equal(0.003f, metal.Thickness);
        Assert.True(metal.Absorption.Average < 0.05f);
        Assert.True(metal.Scattering < carpet.Scattering);

        var foam = AcousticMaterial.AcousticFoam;
        Assert.Equal(0.9f, foam.Scattering);
        Assert.Equal(30f, foam.Density);
        Assert.Equal(0f, foam.Transmission.Average);
        Assert.True(foam.Absorption.Band8kHz > foam.Absorption.Band63Hz);
        Assert.True(foam.Absorption.Average > carpet.Absorption.Average);

        var air = AcousticMaterial.Air;
        Assert.Equal(1f, air.Absorption.Average);
        Assert.Equal(1f, air.Transmission.Average);
        Assert.Equal(0f, air.Thickness);
        Assert.Equal(343f, air.SpeedOfSound);
        Assert.Equal(1.2f, air.Density);

        var hull = AcousticMaterial.SpaceshipHull;
        Assert.Equal(0.05f, hull.Thickness);
        Assert.Equal(4500f, hull.Density);
        Assert.Equal(5090f, hull.SpeedOfSound);
        Assert.True(hull.Transmission.Average < 0.001f);
        Assert.True(hull.Transmission.Average > 0f);

        var horizon = AcousticMaterial.EventHorizon;
        Assert.Equal(1f, horizon.Absorption.Average);
        Assert.Equal(0f, horizon.Transmission.Average);
        Assert.Equal(0f, horizon.SpeedOfSound);
        Assert.Equal(0f, horizon.Scattering);
        Assert.Equal(float.PositiveInfinity, horizon.Density);
    }
}
