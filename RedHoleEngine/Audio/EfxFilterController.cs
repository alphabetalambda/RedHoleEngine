namespace RedHoleEngine.Audio;

/// <summary>
/// Minimal abstraction over the ALC EFX filter entry points so the filter logic is testable
/// without audio hardware.
/// </summary>
internal interface IEfxApi
{
    /// <summary>True when the EFX extension and all required entry points are available.</summary>
    bool IsSupported { get; }
    uint CreateFilter();
    void DeleteFilter(uint filter);
    void SetFilterType(uint filter, EfxFilterType type);
    void SetFilterFloat(uint filter, int param, float value);
    /// <summary>Attach a filter to a source's direct path (0 detaches).</summary>
    void AttachDirectFilter(uint source, uint filter);
}

internal enum EfxFilterType
{
    Null = 0,
    LowPass = 1,
    HighPass = 2,
    BandPass = 3
}

/// <summary>
/// Maps low-pass / high-pass cutoff requests onto EFX direct-path filters per source.
/// EFX filters have no cutoff frequency; they expose HF/LF gain. The cutoff is mapped to a gain
/// on a perceptual scale: a low-pass cutoff of 20kHz (or more) is transparent and lower cutoffs
/// attenuate high frequencies; a high-pass cutoff of 0Hz is transparent and cutoffs approaching
/// <see cref="HighPassFullCutHz"/> fully attenuate low frequencies.
/// All operations are no-ops when EFX is unsupported.
/// </summary>
internal sealed class EfxFilterController
{
    public const float LowPassOpenHz = 20000f;
    public const float HighPassFullCutHz = 5000f;

    // AL_FILTER_* / parameter ids from efx.h
    internal const int GainParam = 1;          // AL_*_GAIN (all filter types)
    internal const int LowPassGainHf = 2;      // AL_LOWPASS_GAINHF
    internal const int HighPassGainLf = 2;     // AL_HIGHPASS_GAINLF
    internal const int BandPassGainLf = 2;     // AL_BANDPASS_GAINLF
    internal const int BandPassGainHf = 3;     // AL_BANDPASS_GAINHF

    private sealed class State
    {
        public uint Source;
        public uint Filter;
        public float LowPassHz = LowPassOpenHz;
        public float HighPassHz;
        public EfxFilterType Applied = EfxFilterType.Null;
    }

    private readonly IEfxApi _api;
    private readonly Dictionary<uint, State> _states = new();

    public EfxFilterController(IEfxApi api) => _api = api;

    public bool IsSupported => _api.IsSupported;
    public int ActiveFilterCount => _states.Count;

    public static float LowPassCutoffToGainHf(float cutoffHz) =>
        Math.Clamp(cutoffHz / LowPassOpenHz, 0.0f, 1f);

    public static float HighPassCutoffToGainLf(float cutoffHz) =>
        1f - Math.Clamp(cutoffHz / HighPassFullCutHz, 0f, 1f);

    public void SetLowPass(uint voiceId, uint source, float cutoffHz)
    {
        if (!_api.IsSupported) return;
        var s = GetState(voiceId, source, cutoffHz < LowPassOpenHz);
        if (s == null) return;
        s.LowPassHz = float.IsNaN(cutoffHz) ? LowPassOpenHz : cutoffHz;
        Apply(s);
    }

    public void SetHighPass(uint voiceId, uint source, float cutoffHz)
    {
        if (!_api.IsSupported) return;
        var s = GetState(voiceId, source, cutoffHz > 0f);
        if (s == null) return;
        s.HighPassHz = float.IsNaN(cutoffHz) ? 0f : Math.Max(0f, cutoffHz);
        Apply(s);
    }

    /// <summary>Detach and delete the filter for a voice (call when the voice stops).</summary>
    public void Release(uint voiceId)
    {
        if (!_states.Remove(voiceId, out var s)) return;
        if (s.Filter != 0)
        {
            _api.AttachDirectFilter(s.Source, 0);
            _api.DeleteFilter(s.Filter);
        }
    }

    public void ReleaseAll()
    {
        foreach (var id in _states.Keys.ToList()) Release(id);
    }

    private State? GetState(uint voiceId, uint source, bool needed)
    {
        if (_states.TryGetValue(voiceId, out var existing)) return existing;
        if (!needed) return null; // nothing to filter and nothing allocated yet
        var s = new State { Source = source };
        _states[voiceId] = s;
        return s;
    }

    private void Apply(State s)
    {
        bool low = s.LowPassHz < LowPassOpenHz;
        bool high = s.HighPassHz > 0f;

        var want = low && high ? EfxFilterType.BandPass
                 : low ? EfxFilterType.LowPass
                 : high ? EfxFilterType.HighPass
                 : EfxFilterType.Null;

        if (want == EfxFilterType.Null)
        {
            if (s.Applied != EfxFilterType.Null)
            {
                _api.AttachDirectFilter(s.Source, 0);
                s.Applied = EfxFilterType.Null;
            }
            return;
        }

        if (s.Filter == 0)
        {
            s.Filter = _api.CreateFilter();
            if (s.Filter == 0) return; // allocation failed; degrade gracefully
        }

        if (want != s.Applied)
            _api.SetFilterType(s.Filter, want);

        float hf = LowPassCutoffToGainHf(s.LowPassHz);
        float lf = HighPassCutoffToGainLf(s.HighPassHz);
        switch (want)
        {
            case EfxFilterType.LowPass:
                _api.SetFilterFloat(s.Filter, GainParam, 1f);
                _api.SetFilterFloat(s.Filter, LowPassGainHf, hf);
                break;
            case EfxFilterType.HighPass:
                _api.SetFilterFloat(s.Filter, GainParam, 1f);
                _api.SetFilterFloat(s.Filter, HighPassGainLf, lf);
                break;
            default:
                _api.SetFilterFloat(s.Filter, GainParam, 1f);
                _api.SetFilterFloat(s.Filter, BandPassGainLf, lf);
                _api.SetFilterFloat(s.Filter, BandPassGainHf, hf);
                break;
        }

        // Re-attach (required by EFX after any filter change to take effect)
        _api.AttachDirectFilter(s.Source, s.Filter);
        s.Applied = want;
    }
}
