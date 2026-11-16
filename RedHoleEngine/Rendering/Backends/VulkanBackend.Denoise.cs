using System.Runtime.InteropServices;
using System.Text;
using RedHoleEngine.Rendering.Denoising;
using Silk.NET.Core.Native;
using Silk.NET.Shaderc;
using Silk.NET.Vulkan;
using VkMemoryBarrier = Silk.NET.Vulkan.MemoryBarrier;

namespace RedHoleEngine.Rendering.Backends;

/// <summary>
/// GPU denoiser (A-Trous / SVGF) integration for the Vulkan backend.
///
/// Passes, recorded into the compute command buffer after the raytracer (and motion vector) pass:
///   1. SVGF temporal (optional): reprojects last frame's history using motion vectors and
///      writes color/moments/history length for the current parity.
///   2. SVGF variance (SVGF only): per-pixel luminance variance from the moments.
///   3. Spatial chain: N edge-aware A-Trous (or variance-guided SVGF) iterations, ping-ponging
///      between two intermediates; the last iteration writes the "final" image.
///   4. The final image is transitioned to TRANSFER_SRC and is what gets blitted / read back.
/// All denoiser images live in GENERAL layout except the final image, which alternates
/// GENERAL (written by compute) and TRANSFER_SRC (blit source).
/// </summary>
public unsafe partial class VulkanBackend
{
    private enum DenoiseResource
    {
        Ping,
        Pong,
        Final,
        HistoryColor0,
        HistoryColor1,
        Moments0,
        Moments1,
        HistoryLength0,
        HistoryLength1,
        PrevNormal,
        PrevDepth,
        Variance,
        Count
    }

    private struct DenoiseImage
    {
        public Image Image;
        public DeviceMemory Memory;
        public ImageView View;
    }

    // Spatial descriptor sets are indexed [source * SpatialDestinationCount + destination]
    private const int SpatialSourceCount = 5;      // RaytracerOutput, Ping, Pong, HistoryA, HistoryB
    private const int SpatialDestinationCount = 3; // Ping, Pong, Final

    private Denoiser? _denoiser;
    private bool _denoiserPipelinesReady;
    private DescriptorPool _denoiseDescriptorPool;
    private DescriptorSetLayout _denoiseSpatialSetLayout;
    private DescriptorSetLayout _denoiseTemporalSetLayout;
    private DescriptorSetLayout _denoiseVarianceSetLayout;
    private PipelineLayout _denoiseSpatialPipelineLayout;
    private PipelineLayout _denoiseTemporalPipelineLayout;
    private PipelineLayout _denoiseVariancePipelineLayout;
    private Pipeline _denoiseATrousPipeline;
    private Pipeline _denoiseSvgfSpatialPipeline;
    private Pipeline _denoiseTemporalPipeline;
    private Pipeline _denoiseVariancePipeline;
    private readonly DescriptorSet[] _denoiseSpatialSets = new DescriptorSet[SpatialSourceCount * SpatialDestinationCount];
    private readonly DescriptorSet[] _denoiseTemporalSets = new DescriptorSet[2];
    private readonly DescriptorSet[] _denoiseVarianceSets = new DescriptorSet[2];

    private readonly DenoiseImage[] _denoiseImages = new DenoiseImage[(int)DenoiseResource.Count];
    private bool _denoiseImagesCreated;
    private bool _denoiseImagesWanted;      // Recreate on resize once the denoiser has been used
    private bool _denoiseLayoutsInitialized;
    private ImageLayout _denoiseFinalLayout = ImageLayout.Undefined;
    private int _denoiseHistoryParity;
    private bool _denoiseHistoryValid;
    private bool _blitFromDenoised;         // Final denoised image is the blit/readback source this frame
    private DenoisePlan _frameDenoisePlan = DenoisePlan.Off;

    /// <summary>Whether the A-Trous/SVGF compute pipelines are available on this device.</summary>
    public bool GpuDenoiserAvailable => _denoiserPipelinesReady;

    // ------------------------------------------------------------------
    // Pipeline setup / teardown
    // ------------------------------------------------------------------

    private void InitializeDenoiser()
    {
        _denoiser = new Denoiser(RaytracerSettings);
        _denoiser.Initialize(_renderWidth, _renderHeight);

        try
        {
            CreateDenoiserPipelines();
            _denoiserPipelinesReady = true;
            Console.WriteLine("[Denoiser] A-Trous/SVGF compute pipelines created");
        }
        catch (Exception ex)
        {
            // Fall back to the raytracer's built-in bilateral filter
            Console.WriteLine($"[Denoiser] Failed to create denoiser pipelines, falling back to bilateral filter: {ex.Message}");
            DestroyDenoiserPipelines();
            _denoiserPipelinesReady = false;
        }
    }

    private void CreateDenoiserPipelines()
    {
        // Spatial layout is shared by A-Trous and SVGF spatial:
        // 0 input, 1 output, 2 normal, 3 depth, 4 variance
        _denoiseSpatialSetLayout = CreateStorageImageSetLayout(5);
        _denoiseTemporalSetLayout = CreateStorageImageSetLayout(12);
        _denoiseVarianceSetLayout = CreateStorageImageSetLayout(3);

        _denoiseSpatialPipelineLayout = CreatePushConstantPipelineLayout(_denoiseSpatialSetLayout,
            (uint)Math.Max(sizeof(ATrousParams), sizeof(SVGFSpatialParams)));
        _denoiseTemporalPipelineLayout = CreatePushConstantPipelineLayout(_denoiseTemporalSetLayout, (uint)sizeof(SVGFTemporalParams));
        _denoiseVariancePipelineLayout = CreatePushConstantPipelineLayout(_denoiseVarianceSetLayout, (uint)sizeof(SVGFVarianceParams));

        _denoiseATrousPipeline = CreateDenoiseComputePipeline("denoise_atrous", _denoiseSpatialPipelineLayout);
        _denoiseSvgfSpatialPipeline = CreateDenoiseComputePipeline("denoise_svgf_spatial", _denoiseSpatialPipelineLayout);
        _denoiseTemporalPipeline = CreateDenoiseComputePipeline("denoise_svgf_temporal", _denoiseTemporalPipelineLayout);
        _denoiseVariancePipeline = CreateDenoiseComputePipeline("denoise_svgf_variance", _denoiseVariancePipelineLayout);

        // Dedicated pool so the raytracer pool sizes stay untouched
        uint spatialSetCount = SpatialSourceCount * SpatialDestinationCount;
        uint setCount = spatialSetCount + 2 + 2;
        uint imageDescriptorCount = spatialSetCount * 5 + 2 * 12 + 2 * 3;
        var poolSize = new DescriptorPoolSize { Type = DescriptorType.StorageImage, DescriptorCount = imageDescriptorCount };
        var poolInfo = new DescriptorPoolCreateInfo
        {
            SType = StructureType.DescriptorPoolCreateInfo,
            PoolSizeCount = 1,
            PPoolSizes = &poolSize,
            MaxSets = setCount
        };
        fixed (DescriptorPool* pool = &_denoiseDescriptorPool)
        {
            if (_vk!.CreateDescriptorPool(_device, &poolInfo, null, pool) != Result.Success)
                throw new Exception("Failed to create denoiser descriptor pool");
        }

        for (int i = 0; i < _denoiseSpatialSets.Length; i++)
            _denoiseSpatialSets[i] = AllocateDenoiseSet(_denoiseSpatialSetLayout);
        for (int p = 0; p < 2; p++)
        {
            _denoiseTemporalSets[p] = AllocateDenoiseSet(_denoiseTemporalSetLayout);
            _denoiseVarianceSets[p] = AllocateDenoiseSet(_denoiseVarianceSetLayout);
        }
    }

    private void DestroyDenoiserPipelines()
    {
        if (_vk == null || _device.Handle == 0)
            return;

        // Destroying the pool frees its sets
        if (_denoiseDescriptorPool.Handle != 0) _vk.DestroyDescriptorPool(_device, _denoiseDescriptorPool, null);
        _denoiseDescriptorPool = default;
        Array.Clear(_denoiseSpatialSets);
        Array.Clear(_denoiseTemporalSets);
        Array.Clear(_denoiseVarianceSets);

        if (_denoiseATrousPipeline.Handle != 0) _vk.DestroyPipeline(_device, _denoiseATrousPipeline, null);
        if (_denoiseSvgfSpatialPipeline.Handle != 0) _vk.DestroyPipeline(_device, _denoiseSvgfSpatialPipeline, null);
        if (_denoiseTemporalPipeline.Handle != 0) _vk.DestroyPipeline(_device, _denoiseTemporalPipeline, null);
        if (_denoiseVariancePipeline.Handle != 0) _vk.DestroyPipeline(_device, _denoiseVariancePipeline, null);
        _denoiseATrousPipeline = _denoiseSvgfSpatialPipeline = _denoiseTemporalPipeline = _denoiseVariancePipeline = default;

        if (_denoiseSpatialPipelineLayout.Handle != 0) _vk.DestroyPipelineLayout(_device, _denoiseSpatialPipelineLayout, null);
        if (_denoiseTemporalPipelineLayout.Handle != 0) _vk.DestroyPipelineLayout(_device, _denoiseTemporalPipelineLayout, null);
        if (_denoiseVariancePipelineLayout.Handle != 0) _vk.DestroyPipelineLayout(_device, _denoiseVariancePipelineLayout, null);
        _denoiseSpatialPipelineLayout = _denoiseTemporalPipelineLayout = _denoiseVariancePipelineLayout = default;

        if (_denoiseSpatialSetLayout.Handle != 0) _vk.DestroyDescriptorSetLayout(_device, _denoiseSpatialSetLayout, null);
        if (_denoiseTemporalSetLayout.Handle != 0) _vk.DestroyDescriptorSetLayout(_device, _denoiseTemporalSetLayout, null);
        if (_denoiseVarianceSetLayout.Handle != 0) _vk.DestroyDescriptorSetLayout(_device, _denoiseVarianceSetLayout, null);
        _denoiseSpatialSetLayout = _denoiseTemporalSetLayout = _denoiseVarianceSetLayout = default;

        _denoiserPipelinesReady = false;
    }

    private DescriptorSetLayout CreateStorageImageSetLayout(int bindingCount)
    {
        var bindings = stackalloc DescriptorSetLayoutBinding[bindingCount];
        for (int i = 0; i < bindingCount; i++)
        {
            bindings[i] = new DescriptorSetLayoutBinding
            {
                Binding = (uint)i,
                DescriptorType = DescriptorType.StorageImage,
                DescriptorCount = 1,
                StageFlags = ShaderStageFlags.ComputeBit
            };
        }

        var layoutInfo = new DescriptorSetLayoutCreateInfo
        {
            SType = StructureType.DescriptorSetLayoutCreateInfo,
            BindingCount = (uint)bindingCount,
            PBindings = bindings
        };

        DescriptorSetLayout layout;
        if (_vk!.CreateDescriptorSetLayout(_device, &layoutInfo, null, &layout) != Result.Success)
            throw new Exception("Failed to create denoiser descriptor set layout");
        return layout;
    }

    private PipelineLayout CreatePushConstantPipelineLayout(DescriptorSetLayout setLayout, uint pushConstantSize)
    {
        var range = new PushConstantRange
        {
            StageFlags = ShaderStageFlags.ComputeBit,
            Offset = 0,
            Size = pushConstantSize
        };

        var layoutInfo = new PipelineLayoutCreateInfo
        {
            SType = StructureType.PipelineLayoutCreateInfo,
            SetLayoutCount = 1,
            PSetLayouts = &setLayout,
            PushConstantRangeCount = 1,
            PPushConstantRanges = &range
        };

        PipelineLayout layout;
        if (_vk!.CreatePipelineLayout(_device, &layoutInfo, null, &layout) != Result.Success)
            throw new Exception("Failed to create denoiser pipeline layout");
        return layout;
    }

    private Pipeline CreateDenoiseComputePipeline(string shaderName, PipelineLayout layout)
    {
        byte[] code = LoadComputeShaderSpirv(shaderName);
        var module = CreateShaderModule(code);
        var entryPoint = SilkMarshal.StringToPtr("main", NativeStringEncoding.UTF8);

        try
        {
            var pipelineInfo = new ComputePipelineCreateInfo
            {
                SType = StructureType.ComputePipelineCreateInfo,
                Stage = new PipelineShaderStageCreateInfo
                {
                    SType = StructureType.PipelineShaderStageCreateInfo,
                    Stage = ShaderStageFlags.ComputeBit,
                    Module = module,
                    PName = (byte*)entryPoint
                },
                Layout = layout
            };

            Pipeline pipeline;
            if (_vk!.CreateComputePipelines(_device, default, 1, &pipelineInfo, null, &pipeline) != Result.Success)
                throw new Exception($"Failed to create compute pipeline for {shaderName}");
            return pipeline;
        }
        finally
        {
            _vk!.DestroyShaderModule(_device, module, null);
            SilkMarshal.Free(entryPoint);
        }
    }

    /// <summary>
    /// Compile Rendering/Shaders/{name}.comp with shaderc (like the raytracer) and fall back to the
    /// precompiled Rendering/Shaders/{name}.spv.
    /// </summary>
    private static byte[] LoadComputeShaderSpirv(string name)
    {
        string shaderDir = Path.Combine(AppContext.BaseDirectory, "Rendering", "Shaders");
        string sourcePath = Path.Combine(shaderDir, name + ".comp");
        string spvPath = Path.Combine(shaderDir, name + ".spv");

        if (File.Exists(sourcePath))
        {
            try
            {
                var shaderc = Shaderc.GetApi();
                var compiler = shaderc.CompilerInitialize();
                var options = shaderc.CompileOptionsInitialize();
                var sourceBytes = Encoding.UTF8.GetBytes(File.ReadAllText(sourcePath));
                var fileNamePtr = SilkMarshal.StringToPtr(name + ".comp", NativeStringEncoding.UTF8);
                var entryPointPtr = SilkMarshal.StringToPtr("main", NativeStringEncoding.UTF8);

                CompilationResult* result;
                fixed (byte* sourcePtr = sourceBytes)
                {
                    result = shaderc.CompileIntoSpv(compiler, sourcePtr, (nuint)sourceBytes.Length,
                        ShaderKind.ComputeShader, (byte*)fileNamePtr, (byte*)entryPointPtr, options);
                }

                byte[]? spirv = null;
                if (shaderc.ResultGetCompilationStatus(result) == CompilationStatus.Success)
                {
                    var length = shaderc.ResultGetLength(result);
                    spirv = new byte[(int)length];
                    Marshal.Copy((IntPtr)shaderc.ResultGetBytes(result), spirv, 0, (int)length);
                }
                else
                {
                    Console.WriteLine($"[Denoiser] Shaderc failed to compile {sourcePath}: {shaderc.ResultGetErrorMessageS(result)}");
                }

                shaderc.ResultRelease(result);
                shaderc.CompileOptionsRelease(options);
                shaderc.CompilerRelease(compiler);
                SilkMarshal.Free(fileNamePtr);
                SilkMarshal.Free(entryPointPtr);

                if (spirv != null)
                    return spirv;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Denoiser] Shaderc unavailable for {name}, falling back to SPIR-V: {ex.Message}");
            }
        }

        if (!File.Exists(spvPath))
            throw new FileNotFoundException($"Denoiser shader not found: {spvPath}");

        return File.ReadAllBytes(spvPath);
    }

    private DescriptorSet AllocateDenoiseSet(DescriptorSetLayout layout)
    {
        var allocInfo = new DescriptorSetAllocateInfo
        {
            SType = StructureType.DescriptorSetAllocateInfo,
            DescriptorPool = _denoiseDescriptorPool,
            DescriptorSetCount = 1,
            PSetLayouts = &layout
        };

        DescriptorSet set;
        if (_vk!.AllocateDescriptorSets(_device, &allocInfo, &set) != Result.Success)
            throw new Exception("Failed to allocate denoiser descriptor set");
        return set;
    }

    // ------------------------------------------------------------------
    // Render-resolution images
    // ------------------------------------------------------------------

    private static Format GetDenoiseImageFormat(DenoiseResource resource) => resource switch
    {
        DenoiseResource.HistoryLength0 or DenoiseResource.HistoryLength1 => Format.R32Sfloat,
        DenoiseResource.PrevDepth or DenoiseResource.Variance => Format.R32Sfloat,
        DenoiseResource.PrevNormal => Format.R16G16B16A16Sfloat, // Same format as the G-buffer normal (copy source)
        _ => Format.R32G32B32A32Sfloat
    };

    /// <summary>
    /// Create the denoiser's render-resolution images and bind them, if not created yet.
    /// Must be called outside command buffer recording.
    /// </summary>
    private void EnsureDenoiseImages()
    {
        if (_denoiseImagesCreated || !_denoiserPipelinesReady)
            return;

        // Descriptor sets must not be updated while a pending command buffer uses them
        _vk!.DeviceWaitIdle(_device);
        CreateDenoiseImages();
        UpdateDenoiseDescriptors();
    }

    private void CreateDenoiseImages()
    {
        if (!_denoiserPipelinesReady)
            return;

        for (int i = 0; i < (int)DenoiseResource.Count; i++)
        {
            var resource = (DenoiseResource)i;
            // Previous-frame G-buffer copies are written with vkCmdCopyImage
            var extraUsage = resource is DenoiseResource.PrevNormal or DenoiseResource.PrevDepth
                ? ImageUsageFlags.TransferDstBit
                : 0;
            CreateStorageImage(_renderWidth, _renderHeight, GetDenoiseImageFormat(resource),
                out _denoiseImages[i].Image, out _denoiseImages[i].Memory, out _denoiseImages[i].View, extraUsage);
        }

        _denoiseImagesCreated = true;
        _denoiseImagesWanted = true;
        _denoiseLayoutsInitialized = false;
        _denoiseFinalLayout = ImageLayout.Undefined;
        _denoiseHistoryValid = false;
        _blitFromDenoised = false;
        _denoiser?.Resize(_renderWidth, _renderHeight);
    }

    private void DestroyDenoiseImages()
    {
        _blitFromDenoised = false;
        _denoiseHistoryValid = false;
        _denoiseLayoutsInitialized = false;
        _denoiseFinalLayout = ImageLayout.Undefined;

        if (!_denoiseImagesCreated || _vk == null || _device.Handle == 0)
            return;

        for (int i = 0; i < _denoiseImages.Length; i++)
        {
            ref var img = ref _denoiseImages[i];
            if (img.View.Handle != 0) _vk.DestroyImageView(_device, img.View, null);
            if (img.Image.Handle != 0) _vk.DestroyImage(_device, img.Image, null);
            if (img.Memory.Handle != 0) _vk.FreeMemory(_device, img.Memory, null);
            img = default;
        }

        _denoiseImagesCreated = false;
    }

    /// <summary>Recreate denoiser images after render-resolution images were recreated.</summary>
    private void RecreateDenoiseImagesIfWanted()
    {
        _denoiser?.Resize(_renderWidth, _renderHeight);
        if (_denoiseImagesWanted && !_denoiseImagesCreated)
            CreateDenoiseImages();
    }

    private ImageView DenoiseView(DenoiseResource resource) => _denoiseImages[(int)resource].View;
    private Image DenoiseImageHandle(DenoiseResource resource) => _denoiseImages[(int)resource].Image;

    private ImageView SpatialSlotView(DenoiseImageSlot slot) => slot switch
    {
        DenoiseImageSlot.RaytracerOutput => _outputImageView,
        DenoiseImageSlot.Ping => DenoiseView(DenoiseResource.Ping),
        DenoiseImageSlot.Pong => DenoiseView(DenoiseResource.Pong),
        DenoiseImageSlot.HistoryA => DenoiseView(DenoiseResource.HistoryColor0),
        DenoiseImageSlot.HistoryB => DenoiseView(DenoiseResource.HistoryColor1),
        DenoiseImageSlot.Final => DenoiseView(DenoiseResource.Final),
        _ => throw new ArgumentOutOfRangeException(nameof(slot))
    };

    private static int SpatialSourceIndex(DenoiseImageSlot slot) => slot switch
    {
        DenoiseImageSlot.RaytracerOutput => 0,
        DenoiseImageSlot.Ping => 1,
        DenoiseImageSlot.Pong => 2,
        DenoiseImageSlot.HistoryA => 3,
        DenoiseImageSlot.HistoryB => 4,
        _ => throw new ArgumentOutOfRangeException(nameof(slot), "Not a spatial source")
    };

    private static int SpatialDestinationIndex(DenoiseImageSlot slot) => slot switch
    {
        DenoiseImageSlot.Ping => 0,
        DenoiseImageSlot.Pong => 1,
        DenoiseImageSlot.Final => 2,
        _ => throw new ArgumentOutOfRangeException(nameof(slot), "Not a spatial destination")
    };

    private static readonly DenoiseImageSlot[] SpatialSources =
    {
        DenoiseImageSlot.RaytracerOutput, DenoiseImageSlot.Ping, DenoiseImageSlot.Pong,
        DenoiseImageSlot.HistoryA, DenoiseImageSlot.HistoryB
    };

    private static readonly DenoiseImageSlot[] SpatialDestinations =
    {
        DenoiseImageSlot.Ping, DenoiseImageSlot.Pong, DenoiseImageSlot.Final
    };

    /// <summary>
    /// (Re)write every denoiser descriptor set. Called after the denoiser images or any of the
    /// raytracer images they reference (output, G-buffer normal, depth, motion vectors) are recreated.
    /// </summary>
    private void UpdateDenoiseDescriptors()
    {
        if (!_denoiseImagesCreated || !_denoiserPipelinesReady)
            return;

        var views = stackalloc ImageView[12];

        // Spatial sets: 0 input, 1 output, 2 normal, 3 depth, 4 variance
        foreach (var source in SpatialSources)
        {
            foreach (var destination in SpatialDestinations)
            {
                if (source == destination)
                    continue;
                views[0] = SpatialSlotView(source);
                views[1] = SpatialSlotView(destination);
                views[2] = _gbufferNormalView;
                views[3] = _raytracingDepthView;
                views[4] = DenoiseView(DenoiseResource.Variance);
                var set = _denoiseSpatialSets[SpatialSourceIndex(source) * SpatialDestinationCount + SpatialDestinationIndex(destination)];
                WriteStorageImageSet(set, views, 5);
            }
        }

        for (int p = 0; p < 2; p++)
        {
            int prev = 1 - p;
            var color = p == 0 ? DenoiseResource.HistoryColor0 : DenoiseResource.HistoryColor1;
            var prevColor = prev == 0 ? DenoiseResource.HistoryColor0 : DenoiseResource.HistoryColor1;
            var moments = p == 0 ? DenoiseResource.Moments0 : DenoiseResource.Moments1;
            var prevMoments = prev == 0 ? DenoiseResource.Moments0 : DenoiseResource.Moments1;
            var length = p == 0 ? DenoiseResource.HistoryLength0 : DenoiseResource.HistoryLength1;
            var prevLength = prev == 0 ? DenoiseResource.HistoryLength0 : DenoiseResource.HistoryLength1;

            // Temporal set (see denoise_svgf_temporal.comp)
            views[0] = _outputImageView;
            views[1] = _gbufferNormalView;
            views[2] = _raytracingDepthView;
            views[3] = _motionVectorView;
            views[4] = DenoiseView(prevColor);
            views[5] = DenoiseView(prevMoments);
            views[6] = DenoiseView(prevLength);
            views[7] = DenoiseView(DenoiseResource.PrevNormal);
            views[8] = DenoiseView(DenoiseResource.PrevDepth);
            views[9] = DenoiseView(color);
            views[10] = DenoiseView(moments);
            views[11] = DenoiseView(length);
            WriteStorageImageSet(_denoiseTemporalSets[p], views, 12);

            // Variance set (see denoise_svgf_variance.comp)
            views[0] = DenoiseView(moments);
            views[1] = DenoiseView(length);
            views[2] = DenoiseView(DenoiseResource.Variance);
            WriteStorageImageSet(_denoiseVarianceSets[p], views, 3);
        }
    }

    private void WriteStorageImageSet(DescriptorSet set, ImageView* views, int count)
    {
        var infos = stackalloc DescriptorImageInfo[count];
        var writes = stackalloc WriteDescriptorSet[count];
        for (int i = 0; i < count; i++)
        {
            infos[i] = new DescriptorImageInfo { ImageView = views[i], ImageLayout = ImageLayout.General };
            writes[i] = new WriteDescriptorSet
            {
                SType = StructureType.WriteDescriptorSet,
                DstSet = set,
                DstBinding = (uint)i,
                DstArrayElement = 0,
                DescriptorType = DescriptorType.StorageImage,
                DescriptorCount = 1,
                PImageInfo = &infos[i]
            };
        }

        _vk!.UpdateDescriptorSets(_device, (uint)count, writes, 0, null);
    }

    // ------------------------------------------------------------------
    // Per-frame
    // ------------------------------------------------------------------

    /// <summary>
    /// Decide this frame's denoise work and make sure its resources exist.
    /// Call after <see cref="RaytracerSettings.Clamp"/> and before writing uniforms / recording.
    /// </summary>
    private void PrepareDenoiseFrame()
    {
        _frameDenoisePlan = DenoisePlan.Create(RaytracerSettings, _denoiserPipelinesReady);
        if (_frameDenoisePlan.RunsGpuPasses)
        {
            EnsureDenoiseImages();
            if (!_denoiseImagesCreated)
                _frameDenoisePlan = DenoisePlan.Off with { InShaderBilateral = true };
        }
    }

    /// <summary>Image the swapchain blit and readback copy read from this frame.</summary>
    private Image GetPresentSourceImage() =>
        _blitFromDenoised ? DenoiseImageHandle(DenoiseResource.Final) : _outputImage;

    private void CmdComputeMemoryBarrier(CommandBuffer cmd,
        PipelineStageFlags srcStage, AccessFlags srcAccess,
        PipelineStageFlags dstStage, AccessFlags dstAccess)
    {
        var barrier = new VkMemoryBarrier
        {
            SType = StructureType.MemoryBarrier,
            SrcAccessMask = srcAccess,
            DstAccessMask = dstAccess
        };
        _vk!.CmdPipelineBarrier(cmd, srcStage, dstStage, 0, 1, &barrier, 0, null, 0, null);
    }

    private void CmdComputeToComputeBarrier(CommandBuffer cmd)
    {
        CmdComputeMemoryBarrier(cmd,
            PipelineStageFlags.ComputeShaderBit, AccessFlags.ShaderWriteBit,
            PipelineStageFlags.ComputeShaderBit, AccessFlags.ShaderReadBit | AccessFlags.ShaderWriteBit);
    }

    /// <summary>
    /// Record the denoiser passes. Expects the raytracer output, G-buffer and depth (and motion vectors
    /// when history is used) to have been written earlier in <paramref name="cmd"/>, with all of them in
    /// GENERAL layout. Leaves the final image in TRANSFER_SRC_OPTIMAL.
    /// </summary>
    private void RecordDenoisePasses(CommandBuffer cmd)
    {
        var plan = _frameDenoisePlan;
        if (!plan.RunsGpuPasses || !_denoiseImagesCreated || !_denoiserPipelinesReady || _denoiser == null)
        {
            _blitFromDenoised = false;
            _denoiseHistoryValid = false;
            return;
        }

        // One-time layout initialization after (re)creation. Contents are undefined until written;
        // history images are never read before being written because HistoryValid starts false.
        if (!_denoiseLayoutsInitialized)
        {
            for (int i = 0; i < _denoiseImages.Length; i++)
            {
                if (i == (int)DenoiseResource.Final)
                    continue;
                TransitionImageLayout(cmd, _denoiseImages[i].Image, ImageLayout.Undefined, ImageLayout.General);
            }
            _denoiseLayoutsInitialized = true;
        }

        var finalImage = DenoiseImageHandle(DenoiseResource.Final);
        if (_denoiseFinalLayout != ImageLayout.General)
        {
            // Undefined -> General on first use, TransferSrc -> General afterwards
            TransitionImageLayout(cmd, finalImage, _denoiseFinalLayout, ImageLayout.General);
            _denoiseFinalLayout = ImageLayout.General;
        }

        // The temporal descriptor set references the motion vector image even when it is not read
        if (plan.RunTemporal && _motionVectorImageLayout != ImageLayout.General)
        {
            TransitionImageLayout(cmd, _motionVectorImage, _motionVectorImageLayout, ImageLayout.General);
            _motionVectorImageLayout = ImageLayout.General;
        }

        // Raytracer (output, G-buffer, depth) and motion vector writes -> denoiser reads
        CmdComputeToComputeBarrier(cmd);

        uint groupsX = (uint)((_renderWidth + 15) / 16);
        uint groupsY = (uint)((_renderHeight + 15) / 16);
        int parity = _denoiseHistoryParity;
        var firstSource = DenoiseImageSlot.RaytracerOutput;

        if (plan.RunTemporal)
        {
            var temporalParams = _denoiser.GetSVGFTemporalParams(plan.UseHistory && _denoiseHistoryValid);
            _vk!.CmdBindPipeline(cmd, PipelineBindPoint.Compute, _denoiseTemporalPipeline);
            var temporalSet = _denoiseTemporalSets[parity];
            _vk.CmdBindDescriptorSets(cmd, PipelineBindPoint.Compute, _denoiseTemporalPipelineLayout, 0, 1, &temporalSet, 0, null);
            _vk.CmdPushConstants(cmd, _denoiseTemporalPipelineLayout, ShaderStageFlags.ComputeBit, 0, (uint)sizeof(SVGFTemporalParams), &temporalParams);
            _vk.CmdDispatch(cmd, groupsX, groupsY, 1);
            CmdComputeToComputeBarrier(cmd);

            if (plan.RunVariance)
            {
                var varianceParams = _denoiser.GetSVGFVarianceParams();
                _vk.CmdBindPipeline(cmd, PipelineBindPoint.Compute, _denoiseVariancePipeline);
                var varianceSet = _denoiseVarianceSets[parity];
                _vk.CmdBindDescriptorSets(cmd, PipelineBindPoint.Compute, _denoiseVariancePipelineLayout, 0, 1, &varianceSet, 0, null);
                _vk.CmdPushConstants(cmd, _denoiseVariancePipelineLayout, ShaderStageFlags.ComputeBit, 0, (uint)sizeof(SVGFVarianceParams), &varianceParams);
                _vk.CmdDispatch(cmd, groupsX, groupsY, 1);
                CmdComputeToComputeBarrier(cmd);
            }

            // Save this frame's G-buffer normal/depth as next frame's "previous" data.
            // The temporal pass above has finished reading the old copies (WAR) and the raytracer's
            // writes must be visible to the transfer (RAW).
            CmdComputeMemoryBarrier(cmd,
                PipelineStageFlags.ComputeShaderBit, AccessFlags.ShaderWriteBit,
                PipelineStageFlags.TransferBit, AccessFlags.TransferReadBit | AccessFlags.TransferWriteBit);
            CopyImageGeneral(cmd, _gbufferNormalImage, DenoiseImageHandle(DenoiseResource.PrevNormal));
            CopyImageGeneral(cmd, _raytracingDepthImage, DenoiseImageHandle(DenoiseResource.PrevDepth));
            CmdComputeMemoryBarrier(cmd,
                PipelineStageFlags.TransferBit, AccessFlags.TransferWriteBit,
                PipelineStageFlags.ComputeShaderBit, AccessFlags.ShaderReadBit);

            firstSource = parity == 0 ? DenoiseImageSlot.HistoryA : DenoiseImageSlot.HistoryB;
        }

        // Spatial chain (ping-pong); last iteration writes the final image
        bool svgf = plan.SpatialFilter == DenoiseSpatialFilter.SVGF;
        _vk!.CmdBindPipeline(cmd, PipelineBindPoint.Compute, svgf ? _denoiseSvgfSpatialPipeline : _denoiseATrousPipeline);
        var steps = DenoisePlan.BuildSpatialChain(firstSource, plan.SpatialIterations);
        foreach (var step in steps)
        {
            var set = _denoiseSpatialSets[SpatialSourceIndex(step.Source) * SpatialDestinationCount + SpatialDestinationIndex(step.Destination)];
            _vk.CmdBindDescriptorSets(cmd, PipelineBindPoint.Compute, _denoiseSpatialPipelineLayout, 0, 1, &set, 0, null);
            if (svgf)
            {
                var p = _denoiser.GetSVGFSpatialParams(step.Iteration);
                _vk.CmdPushConstants(cmd, _denoiseSpatialPipelineLayout, ShaderStageFlags.ComputeBit, 0, (uint)sizeof(SVGFSpatialParams), &p);
            }
            else
            {
                var p = _denoiser.GetATrousParams(step.Iteration);
                _vk.CmdPushConstants(cmd, _denoiseSpatialPipelineLayout, ShaderStageFlags.ComputeBit, 0, (uint)sizeof(ATrousParams), &p);
            }
            _vk.CmdDispatch(cmd, groupsX, groupsY, 1);
            CmdComputeToComputeBarrier(cmd);
        }

        // Final image becomes the blit / readback source
        TransitionImageLayout(cmd, finalImage, ImageLayout.General, ImageLayout.TransferSrcOptimal);
        _denoiseFinalLayout = ImageLayout.TransferSrcOptimal;
        _blitFromDenoised = true;

        if (plan.RunTemporal)
        {
            _denoiseHistoryParity ^= 1;
            _denoiseHistoryValid = true;
        }
        else
        {
            _denoiseHistoryValid = false;
        }
    }

    private void CopyImageGeneral(CommandBuffer cmd, Image source, Image destination)
    {
        var layers = new ImageSubresourceLayers
        {
            AspectMask = ImageAspectFlags.ColorBit,
            MipLevel = 0,
            BaseArrayLayer = 0,
            LayerCount = 1
        };
        var region = new ImageCopy
        {
            SrcSubresource = layers,
            SrcOffset = new Offset3D(0, 0, 0),
            DstSubresource = layers,
            DstOffset = new Offset3D(0, 0, 0),
            Extent = new Extent3D((uint)_renderWidth, (uint)_renderHeight, 1)
        };
        _vk!.CmdCopyImage(cmd, source, ImageLayout.General, destination, ImageLayout.General, 1, &region);
    }
}
