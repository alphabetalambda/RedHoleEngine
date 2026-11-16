namespace RedHoleEngine.Rendering.Denoising;

/// <summary>
/// Spatial filter run by the GPU denoiser.
/// </summary>
public enum DenoiseSpatialFilter
{
    None,
    ATrous,
    SVGF
}

/// <summary>
/// Logical images used by the GPU denoiser's spatial filter chain.
/// The backend maps each slot to a concrete Vulkan image.
/// </summary>
public enum DenoiseImageSlot
{
    /// <summary>Raytracer output (tonemapped color, render resolution).</summary>
    RaytracerOutput,
    /// <summary>Intermediate ping-pong image.</summary>
    Ping,
    /// <summary>Intermediate ping-pong image.</summary>
    Pong,
    /// <summary>Temporal color history, parity 0.</summary>
    HistoryA,
    /// <summary>Temporal color history, parity 1.</summary>
    HistoryB,
    /// <summary>Final denoised image that is blitted to the swapchain.</summary>
    Final
}

/// <summary>
/// One dispatch of the spatial filter: read <see cref="Source"/>, write <see cref="Destination"/>.
/// </summary>
public readonly record struct DenoisePassStep(DenoiseImageSlot Source, DenoiseImageSlot Destination, int Iteration);

/// <summary>
/// Pure description of which denoising work runs for the current settings.
/// Kept free of Vulkan types so it can be unit-tested.
/// </summary>
public readonly record struct DenoisePlan(
    bool InShaderBilateral,
    bool RunTemporal,
    bool RunVariance,
    bool UseHistory,
    DenoiseSpatialFilter SpatialFilter,
    int SpatialIterations)
{
    /// <summary>True when any separate GPU denoise pass is recorded.</summary>
    public bool RunsGpuPasses => SpatialFilter != DenoiseSpatialFilter.None;

    /// <summary>True when the temporal pass reprojects history and therefore needs motion vectors.</summary>
    public bool NeedsMotionVectors => RunTemporal && UseHistory;

    /// <summary>No denoising at all.</summary>
    public static DenoisePlan Off => new(false, false, false, false, DenoiseSpatialFilter.None, 0);

    /// <summary>
    /// Decide which denoising work to run.
    /// </summary>
    /// <param name="settings">Current raytracer settings.</param>
    /// <param name="gpuDenoiserAvailable">
    /// Whether the A-Trous/SVGF compute pipelines were created. When false, A-Trous and SVGF fall
    /// back to the raytracer's built-in bilateral filter.
    /// </param>
    public static DenoisePlan Create(RaytracerSettings settings, bool gpuDenoiserAvailable)
    {
        if (!settings.Denoise)
            return Off;

        switch (settings.DenoiseMethod)
        {
            case DenoiseMethod.None:
                return Off;

            case DenoiseMethod.Bilateral:
                return Off with { InShaderBilateral = true };

            case DenoiseMethod.ATrous:
                if (!gpuDenoiserAvailable)
                    return Off with { InShaderBilateral = true };
                return new DenoisePlan(
                    InShaderBilateral: false,
                    RunTemporal: settings.DenoiseUseHistory,
                    RunVariance: false,
                    UseHistory: settings.DenoiseUseHistory,
                    SpatialFilter: DenoiseSpatialFilter.ATrous,
                    SpatialIterations: Math.Clamp(settings.ATrousIterations, 1, 6));

            case DenoiseMethod.SVGF:
                if (!gpuDenoiserAvailable)
                    return Off with { InShaderBilateral = true };
                // SVGF always runs the temporal pass because it produces the luminance moments the
                // variance pass needs. Without history every pixel has history length 1 and the
                // variance pass falls back to a spatial estimate.
                return new DenoisePlan(
                    InShaderBilateral: false,
                    RunTemporal: true,
                    RunVariance: true,
                    UseHistory: settings.DenoiseUseHistory,
                    SpatialFilter: DenoiseSpatialFilter.SVGF,
                    SpatialIterations: Math.Clamp(settings.SVGFSpatialIterations, 1, 6));

            default:
                return Off;
        }
    }

    /// <summary>
    /// Build the ping-pong chain of spatial filter dispatches. The first step reads
    /// <paramref name="firstSource"/>, intermediate steps alternate Ping/Pong, and the last step
    /// always writes <see cref="DenoiseImageSlot.Final"/>. No step reads and writes the same image.
    /// </summary>
    public static DenoisePassStep[] BuildSpatialChain(DenoiseImageSlot firstSource, int iterations)
    {
        if (iterations <= 0)
            return Array.Empty<DenoisePassStep>();
        if (firstSource == DenoiseImageSlot.Final)
            throw new ArgumentException("The final image cannot be the chain source.", nameof(firstSource));

        var steps = new DenoisePassStep[iterations];
        var source = firstSource;
        for (int i = 0; i < iterations; i++)
        {
            var destination = i == iterations - 1
                ? DenoiseImageSlot.Final
                : source == DenoiseImageSlot.Ping ? DenoiseImageSlot.Pong : DenoiseImageSlot.Ping;
            steps[i] = new DenoisePassStep(source, destination, i);
            source = destination;
        }

        return steps;
    }
}
