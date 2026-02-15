using Silk.NET.Vulkan;

namespace RedHoleEngine.Rendering.Upscaling;

/// <summary>
/// Passthrough upscaler that performs no upscaling (1:1 copy).
/// Used when upscaling is disabled or as a fallback.
/// </summary>
public class NoUpscaler : IUpscaler
{
    private Vk? _vk;
    private Device _device;
    private bool _initialized;
    
    public string Name => "None (Native)";
    public UpscaleMethod Method => UpscaleMethod.None;
    public bool IsSupported => true;
    public bool RequiresMotionVectors => false;
    public bool RequiresDepth => false;
    public bool SupportsRayReconstruction => false;
    public bool SupportsFrameGeneration => false;
    
    public void Initialize(UpscalerContext context, UpscalerSettings settings)
    {
        _vk = context.Vk;
        _device = context.Device;
        _initialized = true;
    }
    
    public void Resize(int renderWidth, int renderHeight, int displayWidth, int displayHeight)
    {
        // No resources to resize for passthrough
    }
    
    public void Execute(CommandBuffer cmd, UpscaleInput input, Image output)
    {
        if (!_initialized || _vk == null) return;
        
        // Simply blit from input color to output
        // The VulkanBackend will handle this via existing blit code
        // This is a no-op since render resolution == display resolution
        
        var blitRegion = new ImageBlit
        {
            SrcSubresource = new ImageSubresourceLayers
            {
                AspectMask = ImageAspectFlags.ColorBit,
                MipLevel = 0,
                BaseArrayLayer = 0,
                LayerCount = 1
            },
            DstSubresource = new ImageSubresourceLayers
            {
                AspectMask = ImageAspectFlags.ColorBit,
                MipLevel = 0,
                BaseArrayLayer = 0,
                LayerCount = 1
            }
        };
        
        blitRegion.SrcOffsets[0] = new Offset3D(0, 0, 0);
        blitRegion.SrcOffsets[1] = new Offset3D(input.RenderWidth, input.RenderHeight, 1);
        blitRegion.DstOffsets[0] = new Offset3D(0, 0, 0);
        blitRegion.DstOffsets[1] = new Offset3D(input.DisplayWidth, input.DisplayHeight, 1);
        
        _vk.CmdBlitImage(
            cmd,
            input.ColorImage,
            ImageLayout.TransferSrcOptimal,
            output,
            ImageLayout.TransferDstOptimal,
            1,
            blitRegion,
            Filter.Linear
        );
    }
    
    public (int width, int height) GetRenderResolution(int displayWidth, int displayHeight, UpscaleQuality quality)
    {
        // No upscaling - always render at native resolution
        return (displayWidth, displayHeight);
    }
    
    public float GetRenderScale(UpscaleQuality quality)
    {
        return 1.0f;
    }
    
    public void Dispose()
    {
        _initialized = false;
    }
}
