using Silk.NET.Vulkan;

namespace RedHoleEngine.Rendering.Upscaling;

/// <summary>
/// Manages upscaler selection, initialization, and provides unified API for upscaling.
/// Automatically falls back to simpler upscalers if preferred one isn't available.
/// </summary>
public class UpscalerManager : IDisposable
{
    private readonly Dictionary<UpscaleMethod, IUpscaler> _upscalers = new();
    private IUpscaler? _activeUpscaler;
    private UpscalerSettings _settings = new();
    private bool _initialized;
    
    /// <summary>
    /// Currently active upscaler
    /// </summary>
    public IUpscaler? ActiveUpscaler => _activeUpscaler;
    
    /// <summary>
    /// Current upscaling settings
    /// </summary>
    public UpscalerSettings Settings => _settings;
    
    /// <summary>
    /// Whether upscaling is enabled and active
    /// </summary>
    public bool IsUpscalingEnabled => _activeUpscaler != null && _settings.Method != UpscaleMethod.None;
    
    /// <summary>
    /// Current render resolution (lower than display when upscaling)
    /// </summary>
    public int RenderWidth { get; private set; }
    
    /// <summary>
    /// Current render resolution
    /// </summary>
    public int RenderHeight { get; private set; }
    
    /// <summary>
    /// Display/output resolution
    /// </summary>
    public int DisplayWidth => _settings.DisplayWidth;
    
    /// <summary>
    /// Display/output resolution
    /// </summary>
    public int DisplayHeight => _settings.DisplayHeight;
    
    /// <summary>
    /// Get list of available (supported) upscaling methods
    /// </summary>
    public IEnumerable<UpscaleMethod> AvailableMethods
    {
        get
        {
            yield return UpscaleMethod.None; // Always available
            
            foreach (var (method, upscaler) in _upscalers)
            {
                if (upscaler.IsSupported)
                    yield return method;
            }
        }
    }
    
    /// <summary>
    /// Initialize the upscaler manager and detect available upscalers
    /// </summary>
    public void Initialize(UpscalerContext context, UpscalerSettings settings)
    {
        _settings = settings;
        
        // Create all upscaler instances
        _upscalers[UpscaleMethod.None] = new NoUpscaler();
        _upscalers[UpscaleMethod.FSR2] = new FSR2Upscaler();
        _upscalers[UpscaleMethod.DLSS] = new DLSSUpscaler();
        _upscalers[UpscaleMethod.XeSS] = new XeSSUpscaler();
        
        // Log available upscalers
        Console.WriteLine("[Upscaling] Detecting available upscalers...");
        foreach (var (method, upscaler) in _upscalers)
        {
            if (method == UpscaleMethod.None) continue;
            
            string status = upscaler.IsSupported ? "Available" : "Not Available";
            Console.WriteLine($"  {upscaler.Name}: {status}");
        }
        
        // Select and initialize the preferred upscaler
        SelectUpscaler(settings.Method, context, settings);
        
        _initialized = true;
    }
    
    /// <summary>
    /// Select an upscaler, with automatic fallback if not available
    /// </summary>
    public void SelectUpscaler(UpscaleMethod method, UpscalerContext context, UpscalerSettings settings)
    {
        _settings = settings;
        _settings.Method = method;
        
        // Dispose current upscaler
        _activeUpscaler?.Dispose();
        _activeUpscaler = null;
        
        // Try to use the requested method
        if (method != UpscaleMethod.None && _upscalers.TryGetValue(method, out var upscaler))
        {
            if (upscaler.IsSupported)
            {
                upscaler.Initialize(context, settings);
                _activeUpscaler = upscaler;
                
                var (w, h) = upscaler.GetRenderResolution(settings.DisplayWidth, settings.DisplayHeight, settings.Quality);
                RenderWidth = w;
                RenderHeight = h;
                
                Console.WriteLine($"[Upscaling] Using {upscaler.Name} at {settings.Quality} quality");
                Console.WriteLine($"[Upscaling] Render: {RenderWidth}x{RenderHeight} -> Display: {settings.DisplayWidth}x{settings.DisplayHeight}");
                return;
            }
            
            // Fallback chain: DLSS -> FSR2 -> XeSS -> None
            Console.WriteLine($"[Upscaling] {_upscalers[method].Name} not available, trying fallback...");
            
            var fallbackOrder = new[] { UpscaleMethod.DLSS, UpscaleMethod.FSR2, UpscaleMethod.XeSS };
            foreach (var fallback in fallbackOrder)
            {
                if (fallback != method && _upscalers.TryGetValue(fallback, out var fallbackUpscaler) && fallbackUpscaler.IsSupported)
                {
                    fallbackUpscaler.Initialize(context, settings);
                    _activeUpscaler = fallbackUpscaler;
                    _settings.Method = fallback;
                    
                    var (w, h) = fallbackUpscaler.GetRenderResolution(settings.DisplayWidth, settings.DisplayHeight, settings.Quality);
                    RenderWidth = w;
                    RenderHeight = h;
                    
                    Console.WriteLine($"[Upscaling] Fallback to {fallbackUpscaler.Name}");
                    return;
                }
            }
        }
        
        // No upscaling - native resolution
        _activeUpscaler = _upscalers[UpscaleMethod.None];
        _activeUpscaler.Initialize(context, settings);
        _settings.Method = UpscaleMethod.None;
        RenderWidth = settings.DisplayWidth;
        RenderHeight = settings.DisplayHeight;
        
        Console.WriteLine("[Upscaling] Disabled (native resolution)");
    }
    
    /// <summary>
    /// Change quality preset
    /// </summary>
    public void SetQuality(UpscaleQuality quality)
    {
        if (_activeUpscaler == null) return;
        
        _settings.Quality = quality;
        var (w, h) = _activeUpscaler.GetRenderResolution(_settings.DisplayWidth, _settings.DisplayHeight, quality);
        RenderWidth = w;
        RenderHeight = h;
        
        _activeUpscaler.Resize(RenderWidth, RenderHeight, _settings.DisplayWidth, _settings.DisplayHeight);
        
        Console.WriteLine($"[Upscaling] Quality changed to {quality}: {RenderWidth}x{RenderHeight}");
    }
    
    /// <summary>
    /// Handle window resize
    /// </summary>
    public void Resize(int displayWidth, int displayHeight)
    {
        _settings.DisplayWidth = displayWidth;
        _settings.DisplayHeight = displayHeight;
        
        if (_activeUpscaler != null)
        {
            var (w, h) = _activeUpscaler.GetRenderResolution(displayWidth, displayHeight, _settings.Quality);
            RenderWidth = w;
            RenderHeight = h;
            
            _activeUpscaler.Resize(RenderWidth, RenderHeight, displayWidth, displayHeight);
        }
        else
        {
            RenderWidth = displayWidth;
            RenderHeight = displayHeight;
        }
    }
    
    /// <summary>
    /// Execute upscaling pass
    /// </summary>
    public void Execute(CommandBuffer cmd, UpscaleInput input, Image output)
    {
        _activeUpscaler?.Execute(cmd, input, output);
    }
    
    /// <summary>
    /// Get the jitter offset for the current frame
    /// Uses Halton sequence for temporal stability
    /// </summary>
    public (float x, float y) GetJitterOffset(uint frameIndex)
    {
        if (_activeUpscaler == null || !_activeUpscaler.RequiresMotionVectors)
            return (0, 0);
        
        // Halton sequence for jitter (same as used in TAA)
        const int JitterPhaseCount = 8;
        int phase = (int)(frameIndex % JitterPhaseCount);
        
        float x = HaltonSequence(phase + 1, 2) - 0.5f;
        float y = HaltonSequence(phase + 1, 3) - 0.5f;
        
        return (x, y);
    }
    
    private static float HaltonSequence(int index, int baseValue)
    {
        float result = 0;
        float f = 1.0f / baseValue;
        int i = index;
        
        while (i > 0)
        {
            result += f * (i % baseValue);
            i /= baseValue;
            f /= baseValue;
        }
        
        return result;
    }
    
    /// <summary>
    /// Check if the active upscaler requires motion vectors
    /// </summary>
    public bool RequiresMotionVectors => _activeUpscaler?.RequiresMotionVectors ?? false;
    
    /// <summary>
    /// Check if the active upscaler requires depth
    /// </summary>
    public bool RequiresDepth => _activeUpscaler?.RequiresDepth ?? false;
    
    /// <summary>
    /// Check if ray reconstruction is available and enabled
    /// </summary>
    public bool IsRayReconstructionEnabled => 
        _activeUpscaler is DLSSUpscaler dlss && 
        dlss.SupportsRayReconstruction && 
        _settings.EnableRayReconstruction;
    
    /// <summary>
    /// Check if frame generation is available and enabled
    /// </summary>
    public bool IsFrameGenerationEnabled => 
        _activeUpscaler is DLSSUpscaler dlss && 
        dlss.SupportsFrameGeneration && 
        _settings.EnableFrameGeneration;
    
    public void Dispose()
    {
        foreach (var upscaler in _upscalers.Values)
        {
            upscaler.Dispose();
        }
        _upscalers.Clear();
        _activeUpscaler = null;
        _initialized = false;
    }
}
