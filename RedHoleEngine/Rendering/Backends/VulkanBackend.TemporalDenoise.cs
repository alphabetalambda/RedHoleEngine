using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using RedHoleEngine.Physics;
using RedHoleEngine.Rendering.Denoising;
using Silk.NET.Core.Native;
using Silk.NET.Shaderc;
using Silk.NET.Vulkan;
using VkBuffer = Silk.NET.Vulkan.Buffer;

namespace RedHoleEngine.Rendering.Backends;

/// <summary>
/// Temporal denoise (SVGF temporal reprojection pass) for the Vulkan backend.
///
/// HARD SAFETY RULE: this pass is only ever recorded when TemporalDenoiseGate allows it (no gravitational
/// lensing). See docs/TEMPORAL_DENOISE.md. Three layers of enforcement:
///   1. PlanTemporalDenoise() -> RecordTemporalDenoisePass() never records the pass (and never touches history) when gated.
///   2. The shader checks params.flags bit0 and passes through otherwise.
///   3. The editor UI shows the toggle disabled with the reason (RaytracerSettingsPanel, uses LastTemporalGate).
///
/// NOTE: this code path has only been verified by build/unit tests; it has not been exercised on a GPU.
/// </summary>
public unsafe partial class VulkanBackend
{
    private struct TemporalImage
    {
        public Image Image;
        public DeviceMemory Memory;
        public ImageView View;
    }

    private const string TemporalShaderFile = "denoise_svgf_temporal.comp";

    private readonly TemporalHistoryTracker _temporalTracker = new();
    private TemporalFramePlan _temporalPlan;
    private bool _temporalForceReset;
    private TemporalGateDecision _lastTemporalGate = new(true, TemporalDenoiseGate.AllowedReason);

    private DescriptorSetLayout _temporalSetLayout;
    private PipelineLayout _temporalPipelineLayout;
    private Pipeline _temporalPipeline;
    private readonly DescriptorSet[] _temporalSets = new DescriptorSet[2];
    private VkBuffer _temporalUniformBuffer;
    private DeviceMemory _temporalUniformMemory;
    private void* _temporalUniformMapped;

    private readonly TemporalImage[] _temporalColor = new TemporalImage[2];
    private readonly TemporalImage[] _temporalMoments = new TemporalImage[2];
    private readonly TemporalImage[] _temporalLength = new TemporalImage[2];
    private TemporalImage _temporalPrevNormal;
    private TemporalImage _temporalPrevDepth;
    private bool _temporalResourcesCreated;
    private bool _temporalLayoutsPending;
    private int _temporalParity;
    private (int W, int H, ulong Out, ulong Normal, ulong Depth, ulong Motion) _temporalSignature;

    /// <summary>Most recent gate decision (for the editor UI: shows the toggle disabled with this reason).</summary>
    public TemporalGateDecision LastTemporalGate => _lastTemporalGate;

    /// <summary>True if the temporal pipeline was created successfully.</summary>
    public bool TemporalDenoiseAvailable => _temporalPipeline.Handle != 0;

    /// <summary>
    /// Decide, for this frame, whether the temporal pass runs. Must be called once per frame before recording.
    /// </summary>
    private void PlanTemporalDenoise(BlackHole? blackHole)
    {
        var gate = TemporalDenoiseGate.Evaluate(blackHole, null, RaytracerSettings);
        _lastTemporalGate = gate;

        bool requested = RaytracerSettings.Denoise && RaytracerSettings.TemporalDenoise
                         && RenderSettings.Mode == RenderMode.Raytraced
                         && TemporalDenoiseAvailable;

        _temporalPlan = _temporalTracker.BeginFrame(requested, gate, _temporalForceReset);
        _temporalForceReset = false;
    }

    private void CreateTemporalDenoisePipeline()
    {
        try
        {
            var bindings = new DescriptorSetLayoutBinding[13];
            for (uint i = 0; i < 12; i++)
            {
                bindings[i] = new DescriptorSetLayoutBinding
                {
                    Binding = i,
                    DescriptorType = DescriptorType.StorageImage,
                    DescriptorCount = 1,
                    StageFlags = ShaderStageFlags.ComputeBit
                };
            }
            bindings[12] = new DescriptorSetLayoutBinding
            {
                Binding = 12,
                DescriptorType = DescriptorType.UniformBuffer,
                DescriptorCount = 1,
                StageFlags = ShaderStageFlags.ComputeBit
            };

            fixed (DescriptorSetLayoutBinding* bindingsPtr = bindings)
            {
                var layoutInfo = new DescriptorSetLayoutCreateInfo
                {
                    SType = StructureType.DescriptorSetLayoutCreateInfo,
                    BindingCount = (uint)bindings.Length,
                    PBindings = bindingsPtr
                };
                fixed (DescriptorSetLayout* layout = &_temporalSetLayout)
                {
                    if (_vk!.CreateDescriptorSetLayout(_device, &layoutInfo, null, layout) != Result.Success)
                        throw new Exception("Failed to create temporal denoise descriptor set layout");
                }
            }

            byte[] code = CompileComputeShaderFile(TemporalShaderFile);
            var module = CreateShaderModule(code);
            var entry = (byte*)Marshal.StringToHGlobalAnsi("main");

            try
            {
                fixed (DescriptorSetLayout* layout = &_temporalSetLayout)
                {
                    var plInfo = new PipelineLayoutCreateInfo
                    {
                        SType = StructureType.PipelineLayoutCreateInfo,
                        SetLayoutCount = 1,
                        PSetLayouts = layout
                    };
                    fixed (PipelineLayout* pl = &_temporalPipelineLayout)
                    {
                        if (_vk!.CreatePipelineLayout(_device, &plInfo, null, pl) != Result.Success)
                            throw new Exception("Failed to create temporal denoise pipeline layout");
                    }
                }

                var pipelineInfo = new ComputePipelineCreateInfo
                {
                    SType = StructureType.ComputePipelineCreateInfo,
                    Stage = new PipelineShaderStageCreateInfo
                    {
                        SType = StructureType.PipelineShaderStageCreateInfo,
                        Stage = ShaderStageFlags.ComputeBit,
                        Module = module,
                        PName = entry
                    },
                    Layout = _temporalPipelineLayout
                };
                fixed (Pipeline* p = &_temporalPipeline)
                {
                    if (_vk!.CreateComputePipelines(_device, default, 1, &pipelineInfo, null, p) != Result.Success)
                        throw new Exception("Failed to create temporal denoise compute pipeline");
                }
            }
            finally
            {
                Marshal.FreeHGlobal((IntPtr)entry);
                _vk!.DestroyShaderModule(_device, module, null);
            }

            CreateTemporalUniformAndSets();
            Console.WriteLine("Temporal denoise pipeline created");
        }
        catch (Exception ex)
        {
            // Never take the renderer down because the optional temporal pass failed to build.
            Console.WriteLine($"[TemporalDenoise] Disabled, pipeline creation failed: {ex.Message}");
            DestroyTemporalPipeline();
        }
    }

    private void CreateTemporalUniformAndSets()
    {
        ulong size = (ulong)Unsafe.SizeOf<SVGFTemporalParams>();
        var bufferInfo = new BufferCreateInfo
        {
            SType = StructureType.BufferCreateInfo,
            Size = size,
            Usage = BufferUsageFlags.UniformBufferBit,
            SharingMode = SharingMode.Exclusive
        };
        fixed (VkBuffer* b = &_temporalUniformBuffer)
        {
            if (_vk!.CreateBuffer(_device, &bufferInfo, null, b) != Result.Success)
                throw new Exception("Failed to create temporal denoise uniform buffer");
        }

        _vk!.GetBufferMemoryRequirements(_device, _temporalUniformBuffer, out var req);
        var alloc = new MemoryAllocateInfo
        {
            SType = StructureType.MemoryAllocateInfo,
            AllocationSize = req.Size,
            MemoryTypeIndex = FindMemoryType(req.MemoryTypeBits,
                MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit)
        };
        fixed (DeviceMemory* m = &_temporalUniformMemory)
        {
            if (_vk.AllocateMemory(_device, &alloc, null, m) != Result.Success)
                throw new Exception("Failed to allocate temporal denoise uniform memory");
        }
        _vk.BindBufferMemory(_device, _temporalUniformBuffer, _temporalUniformMemory, 0);
        fixed (void** mapped = &_temporalUniformMapped)
        {
            _vk.MapMemory(_device, _temporalUniformMemory, 0, size, 0, mapped);
        }

        var layouts = stackalloc DescriptorSetLayout[2] { _temporalSetLayout, _temporalSetLayout };
        var allocInfo = new DescriptorSetAllocateInfo
        {
            SType = StructureType.DescriptorSetAllocateInfo,
            DescriptorPool = _descriptorPool,
            DescriptorSetCount = 2,
            PSetLayouts = layouts
        };
        var sets = stackalloc DescriptorSet[2];
        if (_vk.AllocateDescriptorSets(_device, &allocInfo, sets) != Result.Success)
            throw new Exception("Failed to allocate temporal denoise descriptor sets");
        _temporalSets[0] = sets[0];
        _temporalSets[1] = sets[1];
    }

    private byte[] CompileComputeShaderFile(string fileName)
    {
        string sourcePath = Path.Combine(AppContext.BaseDirectory, "Rendering", "Shaders", fileName);
        if (!File.Exists(sourcePath))
            throw new FileNotFoundException($"Shader not found: {sourcePath}");

        var shaderc = Shaderc.GetApi();
        var compiler = shaderc.CompilerInitialize();
        var options = shaderc.CompileOptionsInitialize();
        var sourceBytes = Encoding.UTF8.GetBytes(File.ReadAllText(sourcePath));
        var fileNamePtr = SilkMarshal.StringToPtr(fileName, NativeStringEncoding.UTF8);
        var entryPtr = SilkMarshal.StringToPtr("main", NativeStringEncoding.UTF8);

        try
        {
            CompilationResult* result;
            fixed (byte* src = sourceBytes)
            {
                result = shaderc.CompileIntoSpv(compiler, src, (nuint)sourceBytes.Length, ShaderKind.ComputeShader,
                    (byte*)fileNamePtr, (byte*)entryPtr, options);
            }

            var status = shaderc.ResultGetCompilationStatus(result);
            if (status != CompilationStatus.Success)
            {
                var error = shaderc.ResultGetErrorMessageS(result);
                shaderc.ResultRelease(result);
                throw new Exception($"Failed to compile {fileName}: {error}");
            }

            var length = shaderc.ResultGetLength(result);
            var spirv = new byte[(int)length];
            Marshal.Copy((IntPtr)shaderc.ResultGetBytes(result), spirv, 0, (int)length);
            shaderc.ResultRelease(result);
            return spirv;
        }
        finally
        {
            shaderc.CompileOptionsRelease(options);
            shaderc.CompilerRelease(compiler);
            SilkMarshal.Free(fileNamePtr);
            SilkMarshal.Free(entryPtr);
        }
    }

    private void CreateTemporalImage(Format format, out TemporalImage img)
    {
        CreateStorageImage(_renderWidth, _renderHeight, format, out img.Image, out img.Memory, out img.View);
    }

    private void DestroyTemporalImage(ref TemporalImage img)
    {
        if (img.View.Handle != 0) _vk!.DestroyImageView(_device, img.View, null);
        if (img.Image.Handle != 0) _vk!.DestroyImage(_device, img.Image, null);
        if (img.Memory.Handle != 0) _vk!.FreeMemory(_device, img.Memory, null);
        img = default;
    }

    private void DestroyTemporalImages()
    {
        for (int i = 0; i < 2; i++)
        {
            DestroyTemporalImage(ref _temporalColor[i]);
            DestroyTemporalImage(ref _temporalMoments[i]);
            DestroyTemporalImage(ref _temporalLength[i]);
        }
        DestroyTemporalImage(ref _temporalPrevNormal);
        DestroyTemporalImage(ref _temporalPrevDepth);
        _temporalResourcesCreated = false;
    }

    private void DestroyTemporalPipeline()
    {
        if (_device.Handle == 0 || _vk == null) return;
        DestroyTemporalImages();
        if (_temporalPipeline.Handle != 0) _vk.DestroyPipeline(_device, _temporalPipeline, null);
        if (_temporalPipelineLayout.Handle != 0) _vk.DestroyPipelineLayout(_device, _temporalPipelineLayout, null);
        if (_temporalSetLayout.Handle != 0) _vk.DestroyDescriptorSetLayout(_device, _temporalSetLayout, null);
        if (_temporalUniformBuffer.Handle != 0)
        {
            _vk.DestroyBuffer(_device, _temporalUniformBuffer, null);
            _vk.FreeMemory(_device, _temporalUniformMemory, null);
        }
        _temporalPipeline = default;
        _temporalPipelineLayout = default;
        _temporalSetLayout = default;
        _temporalUniformBuffer = default;
    }

    /// <summary>
    /// (Re)create the history images and rewrite descriptors if the render resolution or any bound
    /// input image changed. Returns true if history was (re)created, i.e. any previous history is gone.
    /// </summary>
    private bool EnsureTemporalResources()
    {
        var signature = (_renderWidth, _renderHeight, _outputImageView.Handle, _gbufferNormalView.Handle,
            _raytracingDepthView.Handle, _motionVectorView.Handle);
        if (_temporalResourcesCreated && signature.Equals(_temporalSignature))
            return false;

        _vk!.DeviceWaitIdle(_device);
        DestroyTemporalImages();

        for (int i = 0; i < 2; i++)
        {
            CreateTemporalImage(Format.R32G32B32A32Sfloat, out _temporalColor[i]);
            CreateTemporalImage(Format.R32G32B32A32Sfloat, out _temporalMoments[i]);
            CreateTemporalImage(Format.R16Sfloat, out _temporalLength[i]);
        }
        CreateTemporalImage(Format.R16G16B16A16Sfloat, out _temporalPrevNormal);
        CreateTemporalImage(Format.R32Sfloat, out _temporalPrevDepth);

        for (int p = 0; p < 2; p++)
            WriteTemporalDescriptors(p);

        _temporalSignature = signature;
        _temporalResourcesCreated = true;
        _temporalLayoutsPending = true;
        _temporalParity = 0;
        return true;
    }

    /// <summary>Descriptor set p reads history slot p and writes history slot 1-p.</summary>
    private void WriteTemporalDescriptors(int p)
    {
        int r = p, w = 1 - p;
        var views = new ImageView[]
        {
            _outputImageView,              // 0 currentColor
            _gbufferNormalView,            // 1 currentNormal
            _raytracingDepthView,          // 2 currentDepth
            _motionVectorView,             // 3 motionVectors
            _temporalColor[r].View,        // 4 prevColor
            _temporalMoments[r].View,      // 5 prevMoments
            _temporalLength[r].View,       // 6 prevHistoryLength
            _temporalPrevNormal.View,      // 7 prevNormal
            _temporalPrevDepth.View,       // 8 prevDepth
            _temporalColor[w].View,        // 9 outColor
            _temporalMoments[w].View,      // 10 outMoments
            _temporalLength[w].View        // 11 outHistoryLength
        };

        var infos = stackalloc DescriptorImageInfo[12];
        var writes = stackalloc WriteDescriptorSet[13];
        for (int i = 0; i < 12; i++)
        {
            infos[i] = new DescriptorImageInfo { ImageView = views[i], ImageLayout = ImageLayout.General };
            writes[i] = new WriteDescriptorSet
            {
                SType = StructureType.WriteDescriptorSet,
                DstSet = _temporalSets[p],
                DstBinding = (uint)i,
                DescriptorType = DescriptorType.StorageImage,
                DescriptorCount = 1,
                PImageInfo = &infos[i]
            };
        }
        var bufInfo = new DescriptorBufferInfo
        {
            Buffer = _temporalUniformBuffer,
            Offset = 0,
            Range = (ulong)Unsafe.SizeOf<SVGFTemporalParams>()
        };
        writes[12] = new WriteDescriptorSet
        {
            SType = StructureType.WriteDescriptorSet,
            DstSet = _temporalSets[p],
            DstBinding = 12,
            DescriptorType = DescriptorType.UniformBuffer,
            DescriptorCount = 1,
            PBufferInfo = &bufInfo
        };
        _vk!.UpdateDescriptorSets(_device, 13, writes, 0, null);
    }

    private void CopyTemporalImage(CommandBuffer cmd, Image src, Image dst)
    {
        var region = new ImageCopy
        {
            SrcSubresource = new ImageSubresourceLayers { AspectMask = ImageAspectFlags.ColorBit, LayerCount = 1 },
            DstSubresource = new ImageSubresourceLayers { AspectMask = ImageAspectFlags.ColorBit, LayerCount = 1 },
            Extent = new Extent3D((uint)_renderWidth, (uint)_renderHeight, 1)
        };
        _vk!.CmdCopyImage(cmd, src, ImageLayout.General, dst, ImageLayout.General, 1, &region);
    }

    private void TemporalMemoryBarrier(CommandBuffer cmd, PipelineStageFlags srcStage, AccessFlags srcAccess,
        PipelineStageFlags dstStage, AccessFlags dstAccess)
    {
        var barrier = new MemoryBarrier
        {
            SType = StructureType.MemoryBarrier,
            SrcAccessMask = srcAccess,
            DstAccessMask = dstAccess
        };
        _vk!.CmdPipelineBarrier(cmd, srcStage, dstStage, 0, 1, &barrier, 0, null, 0, null);
    }

    /// <summary>
    /// Record the temporal pass. Does NOTHING (no dispatch, no history read/write) unless the plan says Run
    /// and the gravity gate allowed it.
    /// </summary>
    private void RecordTemporalDenoisePass(CommandBuffer cmd)
    {
        // Layer 1: C# render path gate.
        if (!_temporalPlan.Run || !_temporalPlan.Gate.Allowed || !TemporalDenoiseAvailable)
            return;

        var plan = _temporalPlan;
        if (EnsureTemporalResources())
        {
            // History images were just (re)created: nothing valid to read.
            plan = plan with { HistoryValid = false };
        }

        if (_temporalLayoutsPending)
        {
            for (int i = 0; i < 2; i++)
            {
                TransitionImageLayout(cmd, _temporalColor[i].Image, ImageLayout.Undefined, ImageLayout.General);
                TransitionImageLayout(cmd, _temporalMoments[i].Image, ImageLayout.Undefined, ImageLayout.General);
                TransitionImageLayout(cmd, _temporalLength[i].Image, ImageLayout.Undefined, ImageLayout.General);
            }
            TransitionImageLayout(cmd, _temporalPrevNormal.Image, ImageLayout.Undefined, ImageLayout.General);
            TransitionImageLayout(cmd, _temporalPrevDepth.Image, ImageLayout.Undefined, ImageLayout.General);
            _temporalLayoutsPending = false;
        }

        // Raytracer + motion vector writes -> temporal pass reads
        TemporalMemoryBarrier(cmd, PipelineStageFlags.ComputeShaderBit, AccessFlags.ShaderWriteBit,
            PipelineStageFlags.ComputeShaderBit, AccessFlags.ShaderReadBit | AccessFlags.ShaderWriteBit);

        var denoiser = _temporalParamsBuilder ??= new Denoiser(RaytracerSettings);
        denoiser.Initialize(_renderWidth, _renderHeight);
        var uniforms = denoiser.GetSVGFTemporalParams(plan.ShaderFlags);
        Unsafe.Copy(_temporalUniformMapped, ref uniforms);

        _vk!.CmdBindPipeline(cmd, PipelineBindPoint.Compute, _temporalPipeline);
        var set = _temporalSets[_temporalParity];
        _vk.CmdBindDescriptorSets(cmd, PipelineBindPoint.Compute, _temporalPipelineLayout, 0, 1, &set, 0, null);
        _vk.CmdDispatch(cmd, (uint)Math.Ceiling(_renderWidth / 16.0), (uint)Math.Ceiling(_renderHeight / 16.0), 1);

        // Dispatch -> copies
        TemporalMemoryBarrier(cmd, PipelineStageFlags.ComputeShaderBit, AccessFlags.ShaderWriteBit,
            PipelineStageFlags.TransferBit, AccessFlags.TransferReadBit | AccessFlags.TransferWriteBit);

        int written = 1 - _temporalParity;
        // Denoised result becomes the visible output; G-buffer is kept as next frame's "previous".
        CopyTemporalImage(cmd, _temporalColor[written].Image, _outputImage);
        CopyTemporalImage(cmd, _gbufferNormalImage, _temporalPrevNormal.Image);
        CopyTemporalImage(cmd, _raytracingDepthImage, _temporalPrevDepth.Image);

        TemporalMemoryBarrier(cmd, PipelineStageFlags.TransferBit, AccessFlags.TransferWriteBit,
            PipelineStageFlags.ComputeShaderBit | PipelineStageFlags.TransferBit,
            AccessFlags.ShaderReadBit | AccessFlags.ShaderWriteBit | AccessFlags.TransferReadBit);

        _temporalParity = written;
        _temporalTracker.EndFrame(plan);
    }

    private Denoiser? _temporalParamsBuilder;
}
