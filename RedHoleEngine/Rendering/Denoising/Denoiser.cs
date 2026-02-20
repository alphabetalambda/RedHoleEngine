using System.Numerics;

namespace RedHoleEngine.Rendering.Denoising;

/// <summary>
/// Manages raytracing denoising using A-Trous wavelet or SVGF algorithms.
/// Provides high-quality edge-preserving denoising for noisy path-traced images.
/// </summary>
public class Denoiser : IDisposable
{
    private readonly RaytracerSettings _settings;
    private bool _disposed;
    
    // Shader handles (to be set by backend)
    public uint ATrousShaderHandle { get; set; }
    public uint SVGFTemporalShaderHandle { get; set; }
    public uint SVGFVarianceShaderHandle { get; set; }
    public uint SVGFSpatialShaderHandle { get; set; }
    
    // Image handles for ping-pong buffers and history
    public uint PingImageHandle { get; set; }
    public uint PongImageHandle { get; set; }
    public uint MomentsImageHandle { get; set; }
    public uint HistoryLengthImageHandle { get; set; }
    public uint VarianceImageHandle { get; set; }
    
    // Previous frame data for SVGF temporal accumulation
    public uint PrevColorImageHandle { get; set; }
    public uint PrevMomentsImageHandle { get; set; }
    public uint PrevHistoryLengthImageHandle { get; set; }
    public uint PrevNormalImageHandle { get; set; }
    public uint PrevDepthImageHandle { get; set; }
    
    public int Width { get; private set; }
    public int Height { get; private set; }
    
    public bool IsInitialized { get; private set; }

    public Denoiser(RaytracerSettings settings)
    {
        _settings = settings;
    }

    /// <summary>
    /// Initialize denoiser resources for the given resolution.
    /// Called by the rendering backend.
    /// </summary>
    public void Initialize(int width, int height)
    {
        Width = width;
        Height = height;
        IsInitialized = true;
    }

    /// <summary>
    /// Resize denoiser resources when resolution changes.
    /// </summary>
    public void Resize(int width, int height)
    {
        if (Width == width && Height == height)
            return;
            
        Width = width;
        Height = height;
        // Backend will handle actual resource recreation
    }

    /// <summary>
    /// Get parameters for A-Trous denoiser pass.
    /// </summary>
    public ATrousParams GetATrousParams(int iteration)
    {
        return new ATrousParams
        {
            Resolution = new Vector2(Width, Height),
            StepSize = 1 << iteration, // 1, 2, 4, 8, 16, 32
            ColorSigma = _settings.ATrousColorSigma,
            NormalSigma = _settings.ATrousNormalSigma,
            DepthSigma = _settings.ATrousDepthSigma
        };
    }

    /// <summary>
    /// Get parameters for SVGF temporal pass.
    /// </summary>
    public SVGFTemporalParams GetSVGFTemporalParams()
    {
        return new SVGFTemporalParams
        {
            Resolution = new Vector2(Width, Height),
            TemporalAlpha = _settings.SVGFTemporalAlpha,
            NormalThreshold = 0.9f,  // cos(~25 degrees)
            DepthThreshold = 0.1f,   // 10% relative depth difference
            MaxHistoryLength = 32
        };
    }

    /// <summary>
    /// Get parameters for SVGF variance estimation pass.
    /// </summary>
    public SVGFVarianceParams GetSVGFVarianceParams()
    {
        return new SVGFVarianceParams
        {
            Resolution = new Vector2(Width, Height),
            MinHistoryForVariance = 4,
            SpatialVarianceBoost = 4.0f
        };
    }

    /// <summary>
    /// Get parameters for SVGF spatial filter pass.
    /// </summary>
    public SVGFSpatialParams GetSVGFSpatialParams(int iteration)
    {
        return new SVGFSpatialParams
        {
            Resolution = new Vector2(Width, Height),
            StepSize = 1 << iteration,
            PhiColor = _settings.SVGFPhiColor,
            PhiNormal = _settings.SVGFPhiNormal,
            PhiDepth = 1.0f,
            VarianceBoost = 1.0f
        };
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        IsInitialized = false;
        GC.SuppressFinalize(this);
    }
}

/// <summary>
/// Parameters for A-Trous wavelet denoiser shader.
/// </summary>
public struct ATrousParams
{
    public Vector2 Resolution;
    public int StepSize;
    public float ColorSigma;
    public float NormalSigma;
    public float DepthSigma;
    public float Pad0;
    public float Pad1;
}

/// <summary>
/// Parameters for SVGF temporal accumulation pass.
/// </summary>
public struct SVGFTemporalParams
{
    public Vector2 Resolution;
    public float TemporalAlpha;
    public float NormalThreshold;
    public float DepthThreshold;
    public int MaxHistoryLength;
    public float Pad0;
    public float Pad1;
}

/// <summary>
/// Parameters for SVGF variance estimation pass.
/// </summary>
public struct SVGFVarianceParams
{
    public Vector2 Resolution;
    public int MinHistoryForVariance;
    public float SpatialVarianceBoost;
}

/// <summary>
/// Parameters for SVGF spatial filter pass.
/// </summary>
public struct SVGFSpatialParams
{
    public Vector2 Resolution;
    public int StepSize;
    public float PhiColor;
    public float PhiNormal;
    public float PhiDepth;
    public float VarianceBoost;
    public float Pad0;
}
