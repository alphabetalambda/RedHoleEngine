namespace RedHoleEngine.Rendering.Denoising;

/// <summary>Per-frame plan for the temporal pass.</summary>
public readonly record struct TemporalFramePlan(bool Run, bool HistoryValid, TemporalGateDecision Gate)
{
    /// <summary>Bit flags uploaded to the shader (bit0 = gate allows temporal, bit1 = history valid).</summary>
    public int ShaderFlags => !Run ? 0 : (1 | (HistoryValid ? 2 : 0));
}

/// <summary>
/// Pure state machine deciding whether the temporal pass runs this frame and whether history may be read.
/// History is invalidated whenever the pass does not run (including when the gravity gate denies),
/// so stale history can never be blended in after the gate flips back to allowed.
/// </summary>
public sealed class TemporalHistoryTracker
{
    private bool _historyValid;

    public bool HistoryValid => _historyValid;

    /// <param name="requested">User asked for temporal denoise (and denoise is on).</param>
    /// <param name="gate">Gravity gate decision for this frame.</param>
    /// <param name="forceReset">Resolution change, scene mutation, accumulation reset, ...</param>
    public TemporalFramePlan BeginFrame(bool requested, TemporalGateDecision gate, bool forceReset)
    {
        bool run = requested && gate.Allowed;
        if (!run || forceReset)
            _historyValid = false;

        // A reset frame still runs (and writes history) but reads none.
        return new TemporalFramePlan(run, run && _historyValid, gate);
    }

    /// <summary>Call after the pass was actually recorded: the history buffers now hold this frame.</summary>
    public void EndFrame(TemporalFramePlan plan)
    {
        if (plan.Run) _historyValid = true;
    }

    public void Invalidate() => _historyValid = false;
}
