using System.Numerics;

namespace RedHoleEngine.Audio;

/// <summary>
/// Frequency bands for acoustic simulation
/// Based on octave bands commonly used in acoustics
/// </summary>
public enum FrequencyBand
{
    /// <summary>63 Hz - Deep bass</summary>
    Band63Hz = 0,
    /// <summary>125 Hz - Bass</summary>
    Band125Hz = 1,
    /// <summary>250 Hz - Low-mid</summary>
    Band250Hz = 2,
    /// <summary>500 Hz - Mid</summary>
    Band500Hz = 3,
    /// <summary>1000 Hz - Mid</summary>
    Band1kHz = 4,
    /// <summary>2000 Hz - Upper-mid</summary>
    Band2kHz = 5,
    /// <summary>4000 Hz - Presence</summary>
    Band4kHz = 6,
    /// <summary>8000 Hz - Brilliance</summary>
    Band8kHz = 7
}

/// <summary>
/// Frequency-dependent acoustic coefficients
/// </summary>
public struct FrequencyResponse
{
    /// <summary>
    /// Coefficients for each frequency band (0-1)
    /// </summary>
    public float Band63Hz;
    public float Band125Hz;
    public float Band250Hz;
    public float Band500Hz;
    public float Band1kHz;
    public float Band2kHz;
    public float Band4kHz;
    public float Band8kHz;

    public float this[FrequencyBand band]
    {
        readonly get => band switch
        {
            FrequencyBand.Band63Hz => Band63Hz,
            FrequencyBand.Band125Hz => Band125Hz,
            FrequencyBand.Band250Hz => Band250Hz,
            FrequencyBand.Band500Hz => Band500Hz,
            FrequencyBand.Band1kHz => Band1kHz,
            FrequencyBand.Band2kHz => Band2kHz,
            FrequencyBand.Band4kHz => Band4kHz,
            FrequencyBand.Band8kHz => Band8kHz,
            _ => 0f
        };
        set
        {
            switch (band)
            {
                case FrequencyBand.Band63Hz: Band63Hz = value; break;
                case FrequencyBand.Band125Hz: Band125Hz = value; break;
                case FrequencyBand.Band250Hz: Band250Hz = value; break;
                case FrequencyBand.Band500Hz: Band500Hz = value; break;
                case FrequencyBand.Band1kHz: Band1kHz = value; break;
                case FrequencyBand.Band2kHz: Band2kHz = value; break;
                case FrequencyBand.Band4kHz: Band4kHz = value; break;
                case FrequencyBand.Band8kHz: Band8kHz = value; break;
            }
        }
    }

    /// <summary>
    /// Create uniform response across all frequencies
    /// </summary>
    public static FrequencyResponse Uniform(float value) => new()
    {
        Band63Hz = value,
        Band125Hz = value,
        Band250Hz = value,
        Band500Hz = value,
        Band1kHz = value,
        Band2kHz = value,
        Band4kHz = value,
        Band8kHz = value
    };

    /// <summary>
    /// Linear interpolation between two responses
    /// </summary>
    public static FrequencyResponse Lerp(FrequencyResponse a, FrequencyResponse b, float t)
    {
        return new FrequencyResponse
        {
            Band63Hz = a.Band63Hz + (b.Band63Hz - a.Band63Hz) * t,
            Band125Hz = a.Band125Hz + (b.Band125Hz - a.Band125Hz) * t,
            Band250Hz = a.Band250Hz + (b.Band250Hz - a.Band250Hz) * t,
            Band500Hz = a.Band500Hz + (b.Band500Hz - a.Band500Hz) * t,
            Band1kHz = a.Band1kHz + (b.Band1kHz - a.Band1kHz) * t,
            Band2kHz = a.Band2kHz + (b.Band2kHz - a.Band2kHz) * t,
            Band4kHz = a.Band4kHz + (b.Band4kHz - a.Band4kHz) * t,
            Band8kHz = a.Band8kHz + (b.Band8kHz - a.Band8kHz) * t
        };
    }

    /// <summary>
    /// Multiply two responses (for chaining absorption)
    /// </summary>
    public static FrequencyResponse operator *(FrequencyResponse a, FrequencyResponse b)
    {
        return new FrequencyResponse
        {
            Band63Hz = a.Band63Hz * b.Band63Hz,
            Band125Hz = a.Band125Hz * b.Band125Hz,
            Band250Hz = a.Band250Hz * b.Band250Hz,
            Band500Hz = a.Band500Hz * b.Band500Hz,
            Band1kHz = a.Band1kHz * b.Band1kHz,
            Band2kHz = a.Band2kHz * b.Band2kHz,
            Band4kHz = a.Band4kHz * b.Band4kHz,
            Band8kHz = a.Band8kHz * b.Band8kHz
        };
    }

    /// <summary>
    /// Scale response by a factor
    /// </summary>
    public static FrequencyResponse operator *(FrequencyResponse a, float scale)
    {
        return new FrequencyResponse
        {
            Band63Hz = a.Band63Hz * scale,
            Band125Hz = a.Band125Hz * scale,
            Band250Hz = a.Band250Hz * scale,
            Band500Hz = a.Band500Hz * scale,
            Band1kHz = a.Band1kHz * scale,
            Band2kHz = a.Band2kHz * scale,
            Band4kHz = a.Band4kHz * scale,
            Band8kHz = a.Band8kHz * scale
        };
    }

    /// <summary>
    /// Get average across all bands
    /// </summary>
    public readonly float Average => (Band63Hz + Band125Hz + Band250Hz + Band500Hz + 
                                       Band1kHz + Band2kHz + Band4kHz + Band8kHz) / 8f;
}

/// <summary>
/// Acoustic properties of a material for raytraced audio
/// </summary>
public class AcousticMaterial
{
    /// <summary>
    /// Material name/identifier
    /// </summary>
    public string Name { get; set; } = "Default";

    /// <summary>
    /// Absorption coefficient per frequency band (0 = fully reflective, 1 = fully absorbing)
    /// </summary>
    public FrequencyResponse Absorption { get; set; }

    /// <summary>
    /// Transmission coefficient per frequency band (0 = blocks all sound, 1 = fully transmits)
    /// How much sound passes through the material
    /// </summary>
    public FrequencyResponse Transmission { get; set; }

    /// <summary>
    /// Scattering coefficient (0 = specular reflection, 1 = fully diffuse)
    /// </summary>
    public float Scattering { get; set; }

    /// <summary>
    /// Material thickness in meters (affects transmission)
    /// </summary>
    public float Thickness { get; set; }

    /// <summary>
    /// Density in kg/m³ (affects transmission loss calculation)
    /// </summary>
    public float Density { get; set; }

    /// <summary>
    /// Speed of sound through this material in m/s
    /// </summary>
    public float SpeedOfSound { get; set; }

    /// <summary>
    /// Calculate transmission loss based on thickness
    /// Uses mass law approximation: TL = 20 * log10(frequency * mass) - 47
    /// </summary>
    public FrequencyResponse CalculateTransmissionLoss(float thickness)
    {
        float surfaceMass = Density * thickness; // kg/m²
        
        return new FrequencyResponse
        {
            Band63Hz = CalculateTLForFrequency(63f, surfaceMass),
            Band125Hz = CalculateTLForFrequency(125f, surfaceMass),
            Band250Hz = CalculateTLForFrequency(250f, surfaceMass),
            Band500Hz = CalculateTLForFrequency(500f, surfaceMass),
            Band1kHz = CalculateTLForFrequency(1000f, surfaceMass),
            Band2kHz = CalculateTLForFrequency(2000f, surfaceMass),
            Band4kHz = CalculateTLForFrequency(4000f, surfaceMass),
            Band8kHz = CalculateTLForFrequency(8000f, surfaceMass)
        };
    }

    private static float CalculateTLForFrequency(float frequency, float surfaceMass)
    {
        // Mass law: TL (dB) = 20 * log10(f * m) - 47
        // Convert to linear transmission coefficient
        float tlDb = 20f * MathF.Log10(frequency * surfaceMass) - 47f;
        tlDb = MathF.Max(0f, tlDb); // Can't have negative TL
        
        // Convert dB to linear (transmission = 10^(-TL/10))
        float transmission = MathF.Pow(10f, -tlDb / 10f);
        return MathF.Min(1f, transmission);
    }

    #region Preset Materials

    /// <summary>
    /// Concrete wall - high mass, low absorption
    /// </summary>
    public static AcousticMaterial Concrete => new()
    {
        Name = "Concrete",
        Absorption = new FrequencyResponse
        {
            Band63Hz = 0.01f, Band125Hz = 0.01f, Band250Hz = 0.02f, Band500Hz = 0.02f,
            Band1kHz = 0.02f, Band2kHz = 0.03f, Band4kHz = 0.03f, Band8kHz = 0.04f
        },
        Transmission = FrequencyResponse.Uniform(0.01f),
        Scattering = 0.1f,
        Thickness = 0.2f,
        Density = 2400f,
        SpeedOfSound = 3400f
    };

    /// <summary>
    /// Glass - transmits high frequencies, reflects low
    /// </summary>
    public static AcousticMaterial Glass => new()
    {
        Name = "Glass",
        Absorption = new FrequencyResponse
        {
            Band63Hz = 0.18f, Band125Hz = 0.06f, Band250Hz = 0.04f, Band500Hz = 0.03f,
            Band1kHz = 0.02f, Band2kHz = 0.02f, Band4kHz = 0.02f, Band8kHz = 0.02f
        },
        Transmission = new FrequencyResponse
        {
            Band63Hz = 0.05f, Band125Hz = 0.08f, Band250Hz = 0.12f, Band500Hz = 0.15f,
            Band1kHz = 0.20f, Band2kHz = 0.25f, Band4kHz = 0.30f, Band8kHz = 0.35f
        },
        Scattering = 0.05f,
        Thickness = 0.006f,
        Density = 2500f,
        SpeedOfSound = 5500f
    };

    /// <summary>
    /// Wood panel - moderate absorption, some transmission
    /// </summary>
    public static AcousticMaterial Wood => new()
    {
        Name = "Wood",
        Absorption = new FrequencyResponse
        {
            Band63Hz = 0.15f, Band125Hz = 0.11f, Band250Hz = 0.10f, Band500Hz = 0.07f,
            Band1kHz = 0.06f, Band2kHz = 0.07f, Band4kHz = 0.07f, Band8kHz = 0.07f
        },
        Transmission = new FrequencyResponse
        {
            Band63Hz = 0.10f, Band125Hz = 0.08f, Band250Hz = 0.05f, Band500Hz = 0.03f,
            Band1kHz = 0.02f, Band2kHz = 0.01f, Band4kHz = 0.01f, Band8kHz = 0.01f
        },
        Scattering = 0.15f,
        Thickness = 0.02f,
        Density = 600f,
        SpeedOfSound = 4000f
    };

    /// <summary>
    /// Carpet - high absorption, especially at high frequencies
    /// </summary>
    public static AcousticMaterial Carpet => new()
    {
        Name = "Carpet",
        Absorption = new FrequencyResponse
        {
            Band63Hz = 0.02f, Band125Hz = 0.06f, Band250Hz = 0.14f, Band500Hz = 0.37f,
            Band1kHz = 0.60f, Band2kHz = 0.65f, Band4kHz = 0.70f, Band8kHz = 0.72f
        },
        Transmission = FrequencyResponse.Uniform(0.0f), // Floor material
        Scattering = 0.7f,
        Thickness = 0.01f,
        Density = 200f,
        SpeedOfSound = 100f
    };

    /// <summary>
    /// Metal - highly reflective, low absorption
    /// </summary>
    public static AcousticMaterial Metal => new()
    {
        Name = "Metal",
        Absorption = new FrequencyResponse
        {
            Band63Hz = 0.01f, Band125Hz = 0.01f, Band250Hz = 0.01f, Band500Hz = 0.02f,
            Band1kHz = 0.02f, Band2kHz = 0.02f, Band4kHz = 0.03f, Band8kHz = 0.04f
        },
        Transmission = FrequencyResponse.Uniform(0.001f),
        Scattering = 0.05f,
        Thickness = 0.003f,
        Density = 7800f,
        SpeedOfSound = 5100f
    };

    /// <summary>
    /// Acoustic foam - very high absorption
    /// </summary>
    public static AcousticMaterial AcousticFoam => new()
    {
        Name = "Acoustic Foam",
        Absorption = new FrequencyResponse
        {
            Band63Hz = 0.08f, Band125Hz = 0.20f, Band250Hz = 0.55f, Band500Hz = 0.85f,
            Band1kHz = 0.95f, Band2kHz = 0.98f, Band4kHz = 0.99f, Band8kHz = 0.99f
        },
        Transmission = FrequencyResponse.Uniform(0.0f),
        Scattering = 0.9f,
        Thickness = 0.05f,
        Density = 30f,
        SpeedOfSound = 50f
    };

    /// <summary>
    /// Open air / no surface (for openings, windows)
    /// </summary>
    public static AcousticMaterial Air => new()
    {
        Name = "Air",
        Absorption = FrequencyResponse.Uniform(1.0f), // Fully absorbs (sound passes through)
        Transmission = FrequencyResponse.Uniform(1.0f),
        Scattering = 0f,
        Thickness = 0f,
        Density = 1.2f,
        SpeedOfSound = 343f
    };

    /// <summary>
    /// Spaceship hull - thick metal with some damping
    /// </summary>
    public static AcousticMaterial SpaceshipHull => new()
    {
        Name = "Spaceship Hull",
        Absorption = new FrequencyResponse
        {
            Band63Hz = 0.05f, Band125Hz = 0.04f, Band250Hz = 0.03f, Band500Hz = 0.03f,
            Band1kHz = 0.03f, Band2kHz = 0.04f, Band4kHz = 0.05f, Band8kHz = 0.06f
        },
        Transmission = FrequencyResponse.Uniform(0.0001f), // Almost nothing gets through
        Scattering = 0.1f,
        Thickness = 0.05f,
        Density = 4500f, // Titanium alloy
        SpeedOfSound = 5090f
    };

    /// <summary>
    /// Event horizon boundary - absorbs everything (for black hole audio effects)
    /// </summary>
    public static AcousticMaterial EventHorizon => new()
    {
        Name = "Event Horizon",
        Absorption = FrequencyResponse.Uniform(1.0f),
        Transmission = FrequencyResponse.Uniform(0.0f),
        Scattering = 0f,
        Thickness = 0f,
        Density = float.PositiveInfinity,
        SpeedOfSound = 0f // Nothing escapes
    };

    #endregion
}
