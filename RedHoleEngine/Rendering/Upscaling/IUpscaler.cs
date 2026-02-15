using Silk.NET.Vulkan;

namespace RedHoleEngine.Rendering.Upscaling;

/// <summary>
/// Common interface for all upscaling technologies (DLSS, FSR, XeSS).
/// Implementations handle vendor-specific initialization and execution.
/// </summary>
public interface IUpscaler : IDisposable
{
    /// <summary>
    /// Name of the upscaler (e.g., "DLSS 3.5", "FSR 2.2", "XeSS 1.3")
    /// </summary>
    string Name { get; }
    
    /// <summary>
    /// The upscaling technology type
    /// </summary>
    UpscaleMethod Method { get; }
    
    /// <summary>
    /// Whether this upscaler is available on the current hardware
    /// </summary>
    bool IsSupported { get; }
    
    /// <summary>
    /// Whether this upscaler requires motion vectors
    /// </summary>
    bool RequiresMotionVectors { get; }
    
    /// <summary>
    /// Whether this upscaler requires depth buffer
    /// </summary>
    bool RequiresDepth { get; }
    
    /// <summary>
    /// Whether this upscaler supports ray reconstruction (DLSS RR)
    /// </summary>
    bool SupportsRayReconstruction { get; }
    
    /// <summary>
    /// Whether this upscaler supports frame generation
    /// </summary>
    bool SupportsFrameGeneration { get; }
    
    /// <summary>
    /// Initialize the upscaler with the given context and resolution settings
    /// </summary>
    /// <param name="context">Vulkan context containing device, queues, etc.</param>
    /// <param name="settings">Upscaler configuration</param>
    void Initialize(UpscalerContext context, UpscalerSettings settings);
    
    /// <summary>
    /// Resize the upscaler when resolution changes
    /// </summary>
    void Resize(int renderWidth, int renderHeight, int displayWidth, int displayHeight);
    
    /// <summary>
    /// Execute the upscaling pass
    /// </summary>
    /// <param name="cmd">Command buffer to record commands into</param>
    /// <param name="input">Input resources for upscaling</param>
    /// <param name="output">Output image to write upscaled result</param>
    void Execute(CommandBuffer cmd, UpscaleInput input, Image output);
    
    /// <summary>
    /// Get the recommended render resolution for a given quality preset
    /// </summary>
    /// <param name="displayWidth">Target display width</param>
    /// <param name="displayHeight">Target display height</param>
    /// <param name="quality">Quality preset</param>
    /// <returns>Recommended render resolution</returns>
    (int width, int height) GetRenderResolution(int displayWidth, int displayHeight, UpscaleQuality quality);
    
    /// <summary>
    /// Get the render scale factor for a given quality preset
    /// </summary>
    float GetRenderScale(UpscaleQuality quality);
}

/// <summary>
/// Upscaling technology types
/// </summary>
public enum UpscaleMethod
{
    /// <summary>
    /// No upscaling, 1:1 passthrough
    /// </summary>
    None,
    
    /// <summary>
    /// NVIDIA DLSS (Deep Learning Super Sampling)
    /// Requires RTX GPU
    /// </summary>
    DLSS,
    
    /// <summary>
    /// AMD FidelityFX Super Resolution 2.x
    /// Works on any GPU
    /// </summary>
    FSR2,
    
    /// <summary>
    /// Intel Xe Super Sampling
    /// Works on any GPU, optimized for Intel Arc
    /// </summary>
    XeSS
}

/// <summary>
/// Quality presets for upscaling
/// Higher quality = higher render resolution = more GPU load
/// </summary>
public enum UpscaleQuality
{
    /// <summary>
    /// Native resolution (1.0x scale, no upscaling)
    /// </summary>
    Native,
    
    /// <summary>
    /// Ultra Quality mode (~77% render scale)
    /// Best quality, minimal performance gain
    /// </summary>
    UltraQuality,
    
    /// <summary>
    /// Quality mode (~67% render scale)
    /// High quality with good performance
    /// </summary>
    Quality,
    
    /// <summary>
    /// Balanced mode (~58% render scale)
    /// Balance between quality and performance
    /// </summary>
    Balanced,
    
    /// <summary>
    /// Performance mode (~50% render scale)
    /// Good performance with acceptable quality
    /// </summary>
    Performance,
    
    /// <summary>
    /// Ultra Performance mode (~33% render scale)
    /// Maximum performance, lower quality
    /// </summary>
    UltraPerformance
}

/// <summary>
/// Vulkan context passed to upscalers
/// </summary>
public struct UpscalerContext
{
    public Vk Vk;
    public Instance Instance;
    public PhysicalDevice PhysicalDevice;
    public Device Device;
    public Queue GraphicsQueue;
    public uint GraphicsQueueFamily;
    public CommandPool CommandPool;
}

/// <summary>
/// Input resources for upscaling
/// </summary>
public struct UpscaleInput
{
    /// <summary>
    /// Color image to upscale (render resolution)
    /// </summary>
    public Image ColorImage;
    public ImageView ColorImageView;
    
    /// <summary>
    /// Depth buffer (render resolution, optional based on upscaler)
    /// </summary>
    public Image DepthImage;
    public ImageView DepthImageView;
    
    /// <summary>
    /// Motion vectors (render resolution, optional based on upscaler)
    /// R16G16_SFLOAT format, in pixels
    /// </summary>
    public Image MotionVectors;
    public ImageView MotionVectorsView;
    
    /// <summary>
    /// Exposure value for HDR (optional)
    /// </summary>
    public Image? ExposureImage;
    
    /// <summary>
    /// Reactive mask for transparency/particles (optional, FSR2)
    /// </summary>
    public Image? ReactiveMask;
    
    /// <summary>
    /// Transparency and composition mask (optional, FSR2)
    /// </summary>
    public Image? TransparencyMask;
    
    /// <summary>
    /// Render resolution
    /// </summary>
    public int RenderWidth;
    public int RenderHeight;
    
    /// <summary>
    /// Display/output resolution
    /// </summary>
    public int DisplayWidth;
    public int DisplayHeight;
    
    /// <summary>
    /// Jitter offset for this frame (in pixels at render resolution)
    /// </summary>
    public float JitterX;
    public float JitterY;
    
    /// <summary>
    /// Frame delta time in seconds
    /// </summary>
    public float DeltaTime;
    
    /// <summary>
    /// Camera near plane
    /// </summary>
    public float NearPlane;
    
    /// <summary>
    /// Camera far plane
    /// </summary>
    public float FarPlane;
    
    /// <summary>
    /// Vertical field of view in radians
    /// </summary>
    public float FovY;
    
    /// <summary>
    /// Whether to reset temporal accumulation (camera cut, teleport, etc.)
    /// </summary>
    public bool Reset;
    
    /// <summary>
    /// Current frame index
    /// </summary>
    public uint FrameIndex;
    
    /// <summary>
    /// Enable sharpening post-process (if supported)
    /// </summary>
    public bool EnableSharpening;
    
    /// <summary>
    /// Sharpness value (0.0 - 1.0)
    /// </summary>
    public float Sharpness;
}

/// <summary>
/// Configuration settings for upscalers
/// </summary>
public class UpscalerSettings
{
    /// <summary>
    /// Selected upscaling method
    /// </summary>
    public UpscaleMethod Method { get; set; } = UpscaleMethod.None;
    
    /// <summary>
    /// Quality preset
    /// </summary>
    public UpscaleQuality Quality { get; set; } = UpscaleQuality.Balanced;
    
    /// <summary>
    /// Display width
    /// </summary>
    public int DisplayWidth { get; set; }
    
    /// <summary>
    /// Display height
    /// </summary>
    public int DisplayHeight { get; set; }
    
    /// <summary>
    /// Enable sharpening
    /// </summary>
    public bool EnableSharpening { get; set; } = true;
    
    /// <summary>
    /// Sharpness amount (0.0 - 1.0)
    /// </summary>
    public float Sharpness { get; set; } = 0.5f;
    
    /// <summary>
    /// Enable DLSS Frame Generation (requires DLSS 3.0+)
    /// </summary>
    public bool EnableFrameGeneration { get; set; } = false;
    
    /// <summary>
    /// Enable DLSS Ray Reconstruction
    /// </summary>
    public bool EnableRayReconstruction { get; set; } = false;
    
    /// <summary>
    /// HDR mode
    /// </summary>
    public bool IsHDR { get; set; } = true;
    
    /// <summary>
    /// Motion vector scale (typically render resolution)
    /// </summary>
    public float MotionVectorScale { get; set; } = 1.0f;
    
    /// <summary>
    /// Calculate render resolution based on quality preset
    /// </summary>
    public (int width, int height) GetRenderResolution(float scale)
    {
        return (
            (int)(DisplayWidth * scale),
            (int)(DisplayHeight * scale)
        );
    }
}
