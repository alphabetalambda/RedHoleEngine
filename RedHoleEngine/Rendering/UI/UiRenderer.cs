using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Silk.NET.Vulkan;
using VkBuffer = Silk.NET.Vulkan.Buffer;

namespace RedHoleEngine.Rendering.UI;

public unsafe class UiRenderer : IDisposable
{
    private readonly Vk _vk;
    private readonly Device _device;
    private readonly PhysicalDevice _physicalDevice;
    private readonly uint _graphicsQueueFamily;

    private Pipeline _pipeline;
    private PipelineLayout _pipelineLayout;
    private DescriptorSetLayout _descriptorSetLayout;
    private DescriptorPool _descriptorPool;
    private DescriptorSet _descriptorSet;
    private RenderPass _renderPass;
    private Framebuffer[] _framebuffers = Array.Empty<Framebuffer>();

    private VkBuffer _vertexBuffer;
    private DeviceMemory _vertexBufferMemory;
    private void* _vertexBufferMapped;
    private int _maxVertices = 65536;

    private Image _fontImage;
    private DeviceMemory _fontMemory;
    private ImageView _fontView;
    private Sampler _fontSampler;
    private CommandPool _uploadCommandPool;
    private readonly Dictionary<int, UiTextureGpu> _textures = new();

    private ShaderModule _vertShaderModule;
    private ShaderModule _fragShaderModule;

    private Extent2D _extent;
    private Format _swapchainFormat;
    private bool _initialized;

    public bool IsInitialized => _initialized;

    public UiRenderer(Vk vk, Device device, PhysicalDevice physicalDevice, uint graphicsQueueFamily)
    {
        _vk = vk;
        _device = device;
        _physicalDevice = physicalDevice;
        _graphicsQueueFamily = graphicsQueueFamily;
    }

    public void Initialize(Format swapchainFormat, Extent2D extent, ImageView[] swapchainImageViews)
    {
        _swapchainFormat = swapchainFormat;
        _extent = extent;

        CreateRenderPass();
        CreateFramebuffers(swapchainImageViews);
        CreateUploadCommandPool();
        CreateDescriptorSetLayout();
        LoadShaders();
        CreatePipeline();
        CreateDescriptorPool();
        CreateFontTexture();
        CreateDescriptorSet();
        CreateVertexBuffer();

        _initialized = true;
        Console.WriteLine("UI renderer initialized");
    }

    public void Resize(Extent2D extent, ImageView[] swapchainImageViews)
    {
        _extent = extent;
        foreach (var fb in _framebuffers)
        {
            _vk.DestroyFramebuffer(_device, fb, null);
        }
        CreateFramebuffers(swapchainImageViews);
    }

    public void RecordCommands(CommandBuffer commandBuffer, uint imageIndex, UiDrawData drawData)
    {
        if (!_initialized || drawData.VertexCount == 0)
            return;

        EnsureTextures(drawData);
        UpdateVertexBuffer(drawData);

        var renderPassInfo = new RenderPassBeginInfo
        {
            SType = StructureType.RenderPassBeginInfo,
            RenderPass = _renderPass,
            Framebuffer = _framebuffers[imageIndex],
            RenderArea = new Rect2D { Offset = new Offset2D(0, 0), Extent = _extent },
            ClearValueCount = 0,
            PClearValues = null
        };

        _vk.CmdBeginRenderPass(commandBuffer, &renderPassInfo, SubpassContents.Inline);
        _vk.CmdBindPipeline(commandBuffer, PipelineBindPoint.Graphics, _pipeline);

        var viewport = new Viewport(0, 0, _extent.Width, _extent.Height, 0, 1);
        var scissor = new Rect2D { Offset = new Offset2D(0, 0), Extent = _extent };
        _vk.CmdSetViewport(commandBuffer, 0, 1, &viewport);
        _vk.CmdSetScissor(commandBuffer, 0, 1, &scissor);

        var vertexBuffer = _vertexBuffer;
        ulong offset = 0;
        _vk.CmdBindVertexBuffers(commandBuffer, 0, 1, &vertexBuffer, &offset);

        var push = new Vector2(_extent.Width, _extent.Height);
        _vk.CmdPushConstants(commandBuffer, _pipelineLayout, ShaderStageFlags.VertexBit, 0, (uint)Unsafe.SizeOf<Vector2>(), &push);

        foreach (var command in drawData.Commands)
        {
            if (!_textures.TryGetValue(command.TextureId, out var texture))
                continue;

            var descriptorSet = texture.DescriptorSet;
            _vk.CmdBindDescriptorSets(commandBuffer, PipelineBindPoint.Graphics, _pipelineLayout, 0, 1, &descriptorSet, 0, null);
            _vk.CmdDraw(commandBuffer, (uint)command.VertexCount, 1, (uint)command.VertexOffset, 0);
        }
        _vk.CmdEndRenderPass(commandBuffer);
    }

    private void CreateRenderPass()
    {
        var colorAttachment = new AttachmentDescription
        {
            Format = _swapchainFormat,
            Samples = SampleCountFlags.Count1Bit,
            LoadOp = AttachmentLoadOp.Load,
            StoreOp = AttachmentStoreOp.Store,
            StencilLoadOp = AttachmentLoadOp.DontCare,
            StencilStoreOp = AttachmentStoreOp.DontCare,
            InitialLayout = ImageLayout.PresentSrcKhr,
            FinalLayout = ImageLayout.PresentSrcKhr
        };

        var colorAttachmentRef = new AttachmentReference
        {
            Attachment = 0,
            Layout = ImageLayout.ColorAttachmentOptimal
        };

        var subpass = new SubpassDescription
        {
            PipelineBindPoint = PipelineBindPoint.Graphics,
            ColorAttachmentCount = 1,
            PColorAttachments = &colorAttachmentRef
        };

        var dependency = new SubpassDependency
        {
            SrcSubpass = Vk.SubpassExternal,
            DstSubpass = 0,
            SrcStageMask = PipelineStageFlags.ColorAttachmentOutputBit,
            DstStageMask = PipelineStageFlags.ColorAttachmentOutputBit,
            SrcAccessMask = 0,
            DstAccessMask = AccessFlags.ColorAttachmentWriteBit
        };

        var renderPassInfo = new RenderPassCreateInfo
        {
            SType = StructureType.RenderPassCreateInfo,
            AttachmentCount = 1,
            PAttachments = &colorAttachment,
            SubpassCount = 1,
            PSubpasses = &subpass,
            DependencyCount = 1,
            PDependencies = &dependency
        };

        fixed (RenderPass* rpPtr = &_renderPass)
        {
            if (_vk.CreateRenderPass(_device, &renderPassInfo, null, rpPtr) != Result.Success)
                throw new Exception("Failed to create UI render pass");
        }
    }

    private void CreateFramebuffers(ImageView[] swapchainImageViews)
    {
        _framebuffers = new Framebuffer[swapchainImageViews.Length];
        for (int i = 0; i < swapchainImageViews.Length; i++)
        {
            var attachment = swapchainImageViews[i];
            var framebufferInfo = new FramebufferCreateInfo
            {
                SType = StructureType.FramebufferCreateInfo,
                RenderPass = _renderPass,
                AttachmentCount = 1,
                PAttachments = &attachment,
                Width = _extent.Width,
                Height = _extent.Height,
                Layers = 1
            };

            fixed (Framebuffer* fb = &_framebuffers[i])
            {
                if (_vk.CreateFramebuffer(_device, &framebufferInfo, null, fb) != Result.Success)
                    throw new Exception("Failed to create UI framebuffer");
            }
        }
    }

    private void CreateDescriptorSetLayout()
    {
        var samplerBinding = new DescriptorSetLayoutBinding
        {
            Binding = 0,
            DescriptorType = DescriptorType.CombinedImageSampler,
            DescriptorCount = 1,
            StageFlags = ShaderStageFlags.FragmentBit
        };

        var layoutInfo = new DescriptorSetLayoutCreateInfo
        {
            SType = StructureType.DescriptorSetLayoutCreateInfo,
            BindingCount = 1,
            PBindings = &samplerBinding
        };

        fixed (DescriptorSetLayout* layout = &_descriptorSetLayout)
        {
            if (_vk.CreateDescriptorSetLayout(_device, &layoutInfo, null, layout) != Result.Success)
                throw new Exception("Failed to create UI descriptor set layout");
        }
    }

    private void LoadShaders()
    {
        var vertCode = LoadShaderBytes(Path.Combine("Rendering", "Shaders", "ui.vert.spv"));
        var fragCode = LoadShaderBytes(Path.Combine("Rendering", "Shaders", "ui.frag.spv"));

        _vertShaderModule = CreateShaderModule(vertCode);
        _fragShaderModule = CreateShaderModule(fragCode);
    }

    private void CreatePipeline()
    {
        var vertStage = new PipelineShaderStageCreateInfo
        {
            SType = StructureType.PipelineShaderStageCreateInfo,
            Stage = ShaderStageFlags.VertexBit,
            Module = _vertShaderModule,
            PName = (byte*)Silk.NET.Core.Native.SilkMarshal.StringToPtr("main")
        };

        var fragStage = new PipelineShaderStageCreateInfo
        {
            SType = StructureType.PipelineShaderStageCreateInfo,
            Stage = ShaderStageFlags.FragmentBit,
            Module = _fragShaderModule,
            PName = (byte*)Silk.NET.Core.Native.SilkMarshal.StringToPtr("main")
        };

        var stages = stackalloc PipelineShaderStageCreateInfo[2] { vertStage, fragStage };

        var bindingDescription = new VertexInputBindingDescription
        {
            Binding = 0,
            Stride = (uint)Marshal.SizeOf<UiVertex>(),
            InputRate = VertexInputRate.Vertex
        };

        var attributeDescriptions = stackalloc VertexInputAttributeDescription[3];
        attributeDescriptions[0] = new VertexInputAttributeDescription
        {
            Binding = 0,
            Location = 0,
            Format = Format.R32G32Sfloat,
            Offset = 0
        };
        attributeDescriptions[1] = new VertexInputAttributeDescription
        {
            Binding = 0,
            Location = 1,
            Format = Format.R32G32Sfloat,
            Offset = (uint)Marshal.OffsetOf<UiVertex>(nameof(UiVertex.UV))
        };
        attributeDescriptions[2] = new VertexInputAttributeDescription
        {
            Binding = 0,
            Location = 2,
            Format = Format.R32G32B32A32Sfloat,
            Offset = (uint)Marshal.OffsetOf<UiVertex>(nameof(UiVertex.Color))
        };

        var vertexInputInfo = new PipelineVertexInputStateCreateInfo
        {
            SType = StructureType.PipelineVertexInputStateCreateInfo,
            VertexBindingDescriptionCount = 1,
            PVertexBindingDescriptions = &bindingDescription,
            VertexAttributeDescriptionCount = 3,
            PVertexAttributeDescriptions = attributeDescriptions
        };

        var inputAssembly = new PipelineInputAssemblyStateCreateInfo
        {
            SType = StructureType.PipelineInputAssemblyStateCreateInfo,
            Topology = PrimitiveTopology.TriangleList,
            PrimitiveRestartEnable = false
        };

        var viewportState = new PipelineViewportStateCreateInfo
        {
            SType = StructureType.PipelineViewportStateCreateInfo,
            ViewportCount = 1,
            ScissorCount = 1
        };

        var rasterizer = new PipelineRasterizationStateCreateInfo
        {
            SType = StructureType.PipelineRasterizationStateCreateInfo,
            DepthClampEnable = false,
            RasterizerDiscardEnable = false,
            PolygonMode = PolygonMode.Fill,
            LineWidth = 1.0f,
            CullMode = CullModeFlags.None,
            FrontFace = FrontFace.Clockwise,
            DepthBiasEnable = false
        };

        var multisampling = new PipelineMultisampleStateCreateInfo
        {
            SType = StructureType.PipelineMultisampleStateCreateInfo,
            SampleShadingEnable = false,
            RasterizationSamples = SampleCountFlags.Count1Bit
        };

        var colorBlendAttachment = new PipelineColorBlendAttachmentState
        {
            ColorWriteMask = ColorComponentFlags.RBit | ColorComponentFlags.GBit | ColorComponentFlags.BBit | ColorComponentFlags.ABit,
            BlendEnable = true,
            SrcColorBlendFactor = BlendFactor.SrcAlpha,
            DstColorBlendFactor = BlendFactor.OneMinusSrcAlpha,
            ColorBlendOp = BlendOp.Add,
            SrcAlphaBlendFactor = BlendFactor.One,
            DstAlphaBlendFactor = BlendFactor.OneMinusSrcAlpha,
            AlphaBlendOp = BlendOp.Add
        };

        var colorBlending = new PipelineColorBlendStateCreateInfo
        {
            SType = StructureType.PipelineColorBlendStateCreateInfo,
            LogicOpEnable = false,
            AttachmentCount = 1,
            PAttachments = &colorBlendAttachment
        };

        var pushRange = new PushConstantRange
        {
            StageFlags = ShaderStageFlags.VertexBit,
            Offset = 0,
            Size = (uint)Unsafe.SizeOf<Vector2>()
        };

        fixed (DescriptorSetLayout* layoutPtr = &_descriptorSetLayout)
        {
            var pipelineLayoutInfo = new PipelineLayoutCreateInfo
            {
                SType = StructureType.PipelineLayoutCreateInfo,
                SetLayoutCount = 1,
                PSetLayouts = layoutPtr,
                PushConstantRangeCount = 1,
                PPushConstantRanges = &pushRange
            };

            fixed (PipelineLayout* pipelineLayout = &_pipelineLayout)
            {
                if (_vk.CreatePipelineLayout(_device, &pipelineLayoutInfo, null, pipelineLayout) != Result.Success)
                    throw new Exception("Failed to create UI pipeline layout");
            }
        }

        var dynamicStates = stackalloc DynamicState[2] { DynamicState.Viewport, DynamicState.Scissor };
        var dynamicState = new PipelineDynamicStateCreateInfo
        {
            SType = StructureType.PipelineDynamicStateCreateInfo,
            DynamicStateCount = 2,
            PDynamicStates = dynamicStates
        };

        var pipelineInfo = new GraphicsPipelineCreateInfo
        {
            SType = StructureType.GraphicsPipelineCreateInfo,
            StageCount = 2,
            PStages = stages,
            PVertexInputState = &vertexInputInfo,
            PInputAssemblyState = &inputAssembly,
            PViewportState = &viewportState,
            PRasterizationState = &rasterizer,
            PMultisampleState = &multisampling,
            PColorBlendState = &colorBlending,
            PDynamicState = &dynamicState,
            Layout = _pipelineLayout,
            RenderPass = _renderPass,
            Subpass = 0
        };

        fixed (Pipeline* pipeline = &_pipeline)
        {
            if (_vk.CreateGraphicsPipelines(_device, default, 1, &pipelineInfo, null, pipeline) != Result.Success)
                throw new Exception("Failed to create UI pipeline");
        }

        Silk.NET.Core.Native.SilkMarshal.Free((nint)vertStage.PName);
        Silk.NET.Core.Native.SilkMarshal.Free((nint)fragStage.PName);
    }

    private void CreateDescriptorPool()
    {
        var poolSize = new DescriptorPoolSize
        {
            Type = DescriptorType.CombinedImageSampler,
            DescriptorCount = 64
        };

        var poolInfo = new DescriptorPoolCreateInfo
        {
            SType = StructureType.DescriptorPoolCreateInfo,
            PoolSizeCount = 1,
            PPoolSizes = &poolSize,
            MaxSets = 64
        };

        fixed (DescriptorPool* pool = &_descriptorPool)
        {
            if (_vk.CreateDescriptorPool(_device, &poolInfo, null, pool) != Result.Success)
                throw new Exception("Failed to create UI descriptor pool");
        }
    }

    private void CreateDescriptorSet()
    {
        var allocInfo = new DescriptorSetAllocateInfo
        {
            SType = StructureType.DescriptorSetAllocateInfo,
            DescriptorPool = _descriptorPool,
            DescriptorSetCount = 1,
            PSetLayouts = null
        };

        fixed (DescriptorSetLayout* layout = &_descriptorSetLayout)
        fixed (DescriptorSet* set = &_descriptorSet)
        {
            allocInfo.PSetLayouts = layout;
            if (_vk.AllocateDescriptorSets(_device, &allocInfo, set) != Result.Success)
                throw new Exception("Failed to allocate UI descriptor set");
        }

        var imageInfo = new DescriptorImageInfo
        {
            ImageLayout = ImageLayout.ShaderReadOnlyOptimal,
            ImageView = _fontView,
            Sampler = _fontSampler
        };

        var write = new WriteDescriptorSet
        {
            SType = StructureType.WriteDescriptorSet,
            DstSet = _descriptorSet,
            DstBinding = 0,
            DescriptorCount = 1,
            DescriptorType = DescriptorType.CombinedImageSampler,
            PImageInfo = &imageInfo
        };

        _vk.UpdateDescriptorSets(_device, 1, &write, 0, null);

        if (_textures.TryGetValue(0, out var fontTexture))
        {
            fontTexture.DescriptorSet = _descriptorSet;
        }
    }

    private void EnsureTextures(UiDrawData drawData)
    {
        foreach (var pair in drawData.Textures)
        {
            if (_textures.TryGetValue(pair.Key, out var existing) && existing.Version == pair.Value.Version)
                continue;

            if (pair.Key == 0)
                continue;

            if (!_textures.TryGetValue(pair.Key, out var texture))
            {
                texture = CreateTexture(pair.Key, pair.Value);
                _textures[pair.Key] = texture;
            }
            else
            {
                UpdateTexture(texture, pair.Value);
                _textures[pair.Key] = texture;
            }
        }
    }

    private UiTextureGpu CreateTexture(int id, UiTextureFrame frame)
    {
        CreateImage((uint)frame.Width, (uint)frame.Height, Format.R8G8B8A8Unorm,
            ImageTiling.Optimal,
            ImageUsageFlags.TransferDstBit | ImageUsageFlags.SampledBit,
            MemoryPropertyFlags.DeviceLocalBit,
            out var image, out var memory);

        var texture = new UiTextureGpu(id, frame.Width, frame.Height)
        {
            Image = image,
            Memory = memory,
            View = CreateImageView(image, Format.R8G8B8A8Unorm),
            Layout = ImageLayout.Undefined
        };

        UploadTextureData(texture, frame);
        var samplerInfo = new SamplerCreateInfo
        {
            SType = StructureType.SamplerCreateInfo,
            MagFilter = Filter.Linear,
            MinFilter = Filter.Linear,
            AddressModeU = SamplerAddressMode.ClampToEdge,
            AddressModeV = SamplerAddressMode.ClampToEdge,
            AddressModeW = SamplerAddressMode.ClampToEdge,
            MipmapMode = SamplerMipmapMode.Linear,
            MaxLod = 1f
        };

        Sampler sampler;
        if (_vk.CreateSampler(_device, &samplerInfo, null, &sampler) != Result.Success)
            throw new Exception("Failed to create UI sampler");

        texture.Sampler = sampler;
        texture.DescriptorSet = AllocateDescriptorSet(texture.View, sampler);
        texture.Version = frame.Version;
        return texture;
    }

    private void UpdateTexture(UiTextureGpu texture, UiTextureFrame frame)
    {
        if (texture.Width != frame.Width || texture.Height != frame.Height)
        {
            DestroyTexture(texture);
            var recreated = CreateTexture(texture.TextureId, frame);
            _textures[texture.TextureId] = recreated;
            return;
        }

        UploadTextureData(texture, frame);
        texture.Version = frame.Version;
    }

    private void UploadTextureData(UiTextureGpu texture, UiTextureFrame frame)
    {
        ulong imageSize = (ulong)(frame.Width * frame.Height * 4);
        CreateBuffer(imageSize, BufferUsageFlags.TransferSrcBit, MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit, out var stagingBuffer, out var stagingMemory);
        void* data;
        _vk.MapMemory(_device, stagingMemory, 0, imageSize, 0, &data);
        fixed (byte* src = frame.Rgba)
        {
            Unsafe.CopyBlock(data, src, (uint)imageSize);
        }
        _vk.UnmapMemory(_device, stagingMemory);

        TransitionImageLayout(texture.Image, Format.R8G8B8A8Unorm, texture.Layout, ImageLayout.TransferDstOptimal);
        texture.Layout = ImageLayout.TransferDstOptimal;
        CopyBufferToImage(stagingBuffer, texture.Image, (uint)frame.Width, (uint)frame.Height);
        TransitionImageLayout(texture.Image, Format.R8G8B8A8Unorm, ImageLayout.TransferDstOptimal, ImageLayout.ShaderReadOnlyOptimal);
        texture.Layout = ImageLayout.ShaderReadOnlyOptimal;

        _vk.DestroyBuffer(_device, stagingBuffer, null);
        _vk.FreeMemory(_device, stagingMemory, null);
    }

    private DescriptorSet AllocateDescriptorSet(ImageView view, Sampler sampler)
    {
        var allocInfo = new DescriptorSetAllocateInfo
        {
            SType = StructureType.DescriptorSetAllocateInfo,
            DescriptorPool = _descriptorPool,
            DescriptorSetCount = 1,
            PSetLayouts = null
        };

        DescriptorSet set;
        fixed (DescriptorSetLayout* layout = &_descriptorSetLayout)
        {
            allocInfo.PSetLayouts = layout;
            if (_vk.AllocateDescriptorSets(_device, &allocInfo, &set) != Result.Success)
                throw new Exception("Failed to allocate UI descriptor set");
        }

        var imageInfo = new DescriptorImageInfo
        {
            ImageLayout = ImageLayout.ShaderReadOnlyOptimal,
            ImageView = view,
            Sampler = sampler
        };

        var write = new WriteDescriptorSet
        {
            SType = StructureType.WriteDescriptorSet,
            DstSet = set,
            DstBinding = 0,
            DescriptorCount = 1,
            DescriptorType = DescriptorType.CombinedImageSampler,
            PImageInfo = &imageInfo
        };

        _vk.UpdateDescriptorSets(_device, 1, &write, 0, null);
        return set;
    }

    private void DestroyTexture(UiTextureGpu texture)
    {
        if (texture.TextureId == 0)
            return;
        _vk.DestroySampler(_device, texture.Sampler, null);
        _vk.DestroyImageView(_device, texture.View, null);
        _vk.DestroyImage(_device, texture.Image, null);
        _vk.FreeMemory(_device, texture.Memory, null);
    }

    private void CreateVertexBuffer()
    {
        var bufferSize = (ulong)(_maxVertices * Marshal.SizeOf<UiVertex>());
        CreateBuffer(bufferSize, BufferUsageFlags.VertexBufferBit, MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit, out _vertexBuffer, out _vertexBufferMemory);
        void* mapped = null;
        _vk.MapMemory(_device, _vertexBufferMemory, 0, bufferSize, 0, &mapped);
        _vertexBufferMapped = mapped;
    }

    private void UpdateVertexBuffer(UiDrawData drawData)
    {
        int vertexCount = drawData.VertexCount;
        if (vertexCount > _maxVertices)
        {
            _vk.DestroyBuffer(_device, _vertexBuffer, null);
            _vk.FreeMemory(_device, _vertexBufferMemory, null);
            _maxVertices = Math.Max(vertexCount, _maxVertices * 2);
            CreateVertexBuffer();
        }

        var size = vertexCount * Marshal.SizeOf<UiVertex>();
        fixed (UiVertex* src = drawData.Vertices.ToArray())
        {
            Unsafe.CopyBlock(_vertexBufferMapped, src, (uint)size);
        }
    }

    private void CreateFontTexture()
    {
        var atlas = UiFontAtlas.BuildAtlas();
        uint width = UiFontAtlas.AtlasSize;
        uint height = UiFontAtlas.AtlasSize;
        ulong imageSize = width * height;

        CreateBuffer(imageSize, BufferUsageFlags.TransferSrcBit, MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit, out var stagingBuffer, out var stagingMemory);
        void* data;
        _vk.MapMemory(_device, stagingMemory, 0, imageSize, 0, &data);
        fixed (byte* src = atlas)
        {
            Unsafe.CopyBlock(data, src, (uint)imageSize);
        }
        _vk.UnmapMemory(_device, stagingMemory);

        CreateImage(width, height, Format.R8Unorm, ImageTiling.Optimal,
            ImageUsageFlags.TransferDstBit | ImageUsageFlags.SampledBit,
            MemoryPropertyFlags.DeviceLocalBit,
            out _fontImage, out _fontMemory);

        TransitionImageLayout(_fontImage, Format.R8Unorm, ImageLayout.Undefined, ImageLayout.TransferDstOptimal);
        CopyBufferToImage(stagingBuffer, _fontImage, width, height);
        TransitionImageLayout(_fontImage, Format.R8Unorm, ImageLayout.TransferDstOptimal, ImageLayout.ShaderReadOnlyOptimal);

        _vk.DestroyBuffer(_device, stagingBuffer, null);
        _vk.FreeMemory(_device, stagingMemory, null);

        _fontView = CreateImageView(_fontImage, Format.R8Unorm);

        var samplerInfo = new SamplerCreateInfo
        {
            SType = StructureType.SamplerCreateInfo,
            MagFilter = Filter.Nearest,
            MinFilter = Filter.Nearest,
            AddressModeU = SamplerAddressMode.ClampToEdge,
            AddressModeV = SamplerAddressMode.ClampToEdge,
            AddressModeW = SamplerAddressMode.ClampToEdge,
            MipmapMode = SamplerMipmapMode.Nearest,
            MaxLod = 1f
        };

        fixed (Sampler* sampler = &_fontSampler)
        {
            if (_vk.CreateSampler(_device, &samplerInfo, null, sampler) != Result.Success)
                throw new Exception("Failed to create UI font sampler");
        }

        var fontTexture = new UiTextureGpu(0, UiFontAtlas.AtlasSize, UiFontAtlas.AtlasSize)
        {
            Image = _fontImage,
            Memory = _fontMemory,
            View = _fontView,
            Sampler = _fontSampler,
            DescriptorSet = default,
            Version = 1,
            Layout = ImageLayout.ShaderReadOnlyOptimal
        };
        _textures[0] = fontTexture;
    }

    private void CreateBuffer(ulong size, BufferUsageFlags usage, MemoryPropertyFlags properties, out VkBuffer buffer, out DeviceMemory memory)
    {
        var bufferInfo = new BufferCreateInfo
        {
            SType = StructureType.BufferCreateInfo,
            Size = size,
            Usage = usage,
            SharingMode = SharingMode.Exclusive
        };

        fixed (VkBuffer* bufferPtr = &buffer)
        {
            if (_vk.CreateBuffer(_device, &bufferInfo, null, bufferPtr) != Result.Success)
                throw new Exception("Failed to create UI buffer");
        }

        _vk.GetBufferMemoryRequirements(_device, buffer, out var memRequirements);
        var allocInfo = new MemoryAllocateInfo
        {
            SType = StructureType.MemoryAllocateInfo,
            AllocationSize = memRequirements.Size,
            MemoryTypeIndex = FindMemoryType(memRequirements.MemoryTypeBits, properties)
        };

        fixed (DeviceMemory* memPtr = &memory)
        {
            if (_vk.AllocateMemory(_device, &allocInfo, null, memPtr) != Result.Success)
                throw new Exception("Failed to allocate UI buffer memory");
        }

        _vk.BindBufferMemory(_device, buffer, memory, 0);
    }

    private void CreateImage(uint width, uint height, Format format, ImageTiling tiling, ImageUsageFlags usage, MemoryPropertyFlags properties, out Image image, out DeviceMemory memory)
    {
        var imageInfo = new ImageCreateInfo
        {
            SType = StructureType.ImageCreateInfo,
            ImageType = ImageType.Type2D,
            Extent = new Extent3D(width, height, 1),
            MipLevels = 1,
            ArrayLayers = 1,
            Format = format,
            Tiling = tiling,
            InitialLayout = ImageLayout.Undefined,
            Usage = usage,
            Samples = SampleCountFlags.Count1Bit,
            SharingMode = SharingMode.Exclusive
        };

        fixed (Image* imagePtr = &image)
        {
            if (_vk.CreateImage(_device, &imageInfo, null, imagePtr) != Result.Success)
                throw new Exception("Failed to create UI image");
        }

        _vk.GetImageMemoryRequirements(_device, image, out var memRequirements);
        var allocInfo = new MemoryAllocateInfo
        {
            SType = StructureType.MemoryAllocateInfo,
            AllocationSize = memRequirements.Size,
            MemoryTypeIndex = FindMemoryType(memRequirements.MemoryTypeBits, properties)
        };

        fixed (DeviceMemory* memPtr = &memory)
        {
            if (_vk.AllocateMemory(_device, &allocInfo, null, memPtr) != Result.Success)
                throw new Exception("Failed to allocate UI image memory");
        }

        _vk.BindImageMemory(_device, image, memory, 0);
    }

    private ImageView CreateImageView(Image image, Format format)
    {
        var viewInfo = new ImageViewCreateInfo
        {
            SType = StructureType.ImageViewCreateInfo,
            Image = image,
            ViewType = ImageViewType.Type2D,
            Format = format,
            SubresourceRange = new ImageSubresourceRange
            {
                AspectMask = ImageAspectFlags.ColorBit,
                BaseMipLevel = 0,
                LevelCount = 1,
                BaseArrayLayer = 0,
                LayerCount = 1
            }
        };

        ImageView view;
        if (_vk.CreateImageView(_device, &viewInfo, null, &view) != Result.Success)
            throw new Exception("Failed to create UI image view");
        return view;
    }

    private uint FindMemoryType(uint typeFilter, MemoryPropertyFlags properties)
    {
        _vk.GetPhysicalDeviceMemoryProperties(_physicalDevice, out var memProperties);
        for (uint i = 0; i < memProperties.MemoryTypeCount; i++)
        {
            if ((typeFilter & (1 << (int)i)) != 0 && (memProperties.MemoryTypes[(int)i].PropertyFlags & properties) == properties)
            {
                return i;
            }
        }

        throw new Exception("Failed to find suitable UI memory type");
    }

    private ShaderModule CreateShaderModule(byte[] code)
    {
        fixed (byte* codePtr = code)
        {
            var createInfo = new ShaderModuleCreateInfo
            {
                SType = StructureType.ShaderModuleCreateInfo,
                CodeSize = new UIntPtr((uint)code.Length),
                PCode = (uint*)codePtr
            };
            ShaderModule shaderModule;
            if (_vk.CreateShaderModule(_device, &createInfo, null, &shaderModule) != Result.Success)
                throw new Exception("Failed to create UI shader module");
            return shaderModule;
        }
    }

    private static byte[] LoadShaderBytes(string relativePath)
    {
        string basePath = AppContext.BaseDirectory;
        string[] candidates =
        {
            Path.Combine(basePath, relativePath),
            Path.Combine(basePath, "..", "..", "..", relativePath)
        };

        foreach (var path in candidates)
        {
            if (File.Exists(path))
            {
                return File.ReadAllBytes(path);
            }
        }

        throw new FileNotFoundException($"UI shader not found: {relativePath}");
    }

    private void TransitionImageLayout(Image image, Format format, ImageLayout oldLayout, ImageLayout newLayout)
    {
        var commandBuffer = BeginSingleTimeCommands();

        var barrier = new ImageMemoryBarrier
        {
            SType = StructureType.ImageMemoryBarrier,
            OldLayout = oldLayout,
            NewLayout = newLayout,
            SrcQueueFamilyIndex = Vk.QueueFamilyIgnored,
            DstQueueFamilyIndex = Vk.QueueFamilyIgnored,
            Image = image,
            SubresourceRange = new ImageSubresourceRange
            {
                AspectMask = ImageAspectFlags.ColorBit,
                BaseMipLevel = 0,
                LevelCount = 1,
                BaseArrayLayer = 0,
                LayerCount = 1
            }
        };

        PipelineStageFlags sourceStage;
        PipelineStageFlags destinationStage;

        if (oldLayout == ImageLayout.Undefined && newLayout == ImageLayout.TransferDstOptimal)
        {
            barrier.SrcAccessMask = 0;
            barrier.DstAccessMask = AccessFlags.TransferWriteBit;
            sourceStage = PipelineStageFlags.TopOfPipeBit;
            destinationStage = PipelineStageFlags.TransferBit;
        }
        else if (oldLayout == ImageLayout.TransferDstOptimal && newLayout == ImageLayout.ShaderReadOnlyOptimal)
        {
            barrier.SrcAccessMask = AccessFlags.TransferWriteBit;
            barrier.DstAccessMask = AccessFlags.ShaderReadBit;
            sourceStage = PipelineStageFlags.TransferBit;
            destinationStage = PipelineStageFlags.FragmentShaderBit;
        }
        else if (oldLayout == ImageLayout.ShaderReadOnlyOptimal && newLayout == ImageLayout.TransferDstOptimal)
        {
            barrier.SrcAccessMask = AccessFlags.ShaderReadBit;
            barrier.DstAccessMask = AccessFlags.TransferWriteBit;
            sourceStage = PipelineStageFlags.FragmentShaderBit;
            destinationStage = PipelineStageFlags.TransferBit;
        }
        else
        {
            sourceStage = PipelineStageFlags.TopOfPipeBit;
            destinationStage = PipelineStageFlags.TopOfPipeBit;
        }

        _vk.CmdPipelineBarrier(commandBuffer, sourceStage, destinationStage, 0, 0, null, 0, null, 1, &barrier);
        EndSingleTimeCommands(commandBuffer);
    }

    private void CopyBufferToImage(VkBuffer buffer, Image image, uint width, uint height)
    {
        var commandBuffer = BeginSingleTimeCommands();
        var region = new BufferImageCopy
        {
            BufferOffset = 0,
            BufferRowLength = 0,
            BufferImageHeight = 0,
            ImageSubresource = new ImageSubresourceLayers
            {
                AspectMask = ImageAspectFlags.ColorBit,
                MipLevel = 0,
                BaseArrayLayer = 0,
                LayerCount = 1
            },
            ImageOffset = new Offset3D(0, 0, 0),
            ImageExtent = new Extent3D(width, height, 1)
        };

        _vk.CmdCopyBufferToImage(commandBuffer, buffer, image, ImageLayout.TransferDstOptimal, 1, &region);
        EndSingleTimeCommands(commandBuffer);
    }

    private CommandBuffer BeginSingleTimeCommands()
    {
        var allocInfo = new CommandBufferAllocateInfo
        {
            SType = StructureType.CommandBufferAllocateInfo,
            CommandPool = _uploadCommandPool,
            Level = CommandBufferLevel.Primary,
            CommandBufferCount = 1
        };

        CommandBuffer commandBuffer;
        _vk.AllocateCommandBuffers(_device, &allocInfo, &commandBuffer);

        var beginInfo = new CommandBufferBeginInfo
        {
            SType = StructureType.CommandBufferBeginInfo,
            Flags = CommandBufferUsageFlags.OneTimeSubmitBit
        };

        _vk.BeginCommandBuffer(commandBuffer, &beginInfo);
        return commandBuffer;
    }

    private void EndSingleTimeCommands(CommandBuffer commandBuffer)
    {
        _vk.EndCommandBuffer(commandBuffer);

        var submitInfo = new SubmitInfo
        {
            SType = StructureType.SubmitInfo,
            CommandBufferCount = 1,
            PCommandBuffers = &commandBuffer
        };

        var queue = GetGraphicsQueue();
        _vk.QueueSubmit(queue, 1, &submitInfo, default);
        _vk.QueueWaitIdle(queue);
        _vk.FreeCommandBuffers(_device, _uploadCommandPool, 1, &commandBuffer);
    }

    private Queue GetGraphicsQueue()
    {
        Queue queue;
        _vk.GetDeviceQueue(_device, _graphicsQueueFamily, 0, &queue);
        return queue;
    }

    private void CreateUploadCommandPool()
    {
        var poolInfo = new CommandPoolCreateInfo
        {
            SType = StructureType.CommandPoolCreateInfo,
            QueueFamilyIndex = _graphicsQueueFamily,
            Flags = CommandPoolCreateFlags.ResetCommandBufferBit
        };

        fixed (CommandPool* pool = &_uploadCommandPool)
        {
            if (_vk.CreateCommandPool(_device, &poolInfo, null, pool) != Result.Success)
                throw new Exception("Failed to create UI command pool");
        }
    }

    public void Dispose()
    {
        if (!_initialized)
            return;

        _vk.DestroySampler(_device, _fontSampler, null);
        _vk.DestroyImageView(_device, _fontView, null);
        _vk.DestroyImage(_device, _fontImage, null);
        _vk.FreeMemory(_device, _fontMemory, null);

        foreach (var texture in _textures.Values)
        {
            if (texture.TextureId != 0)
            {
                DestroyTexture(texture);
            }
        }

        _vk.UnmapMemory(_device, _vertexBufferMemory);
        _vk.DestroyBuffer(_device, _vertexBuffer, null);
        _vk.FreeMemory(_device, _vertexBufferMemory, null);

        _vk.DestroyDescriptorPool(_device, _descriptorPool, null);
        _vk.DestroyDescriptorSetLayout(_device, _descriptorSetLayout, null);
        _vk.DestroyPipeline(_device, _pipeline, null);
        _vk.DestroyPipelineLayout(_device, _pipelineLayout, null);
        _vk.DestroyShaderModule(_device, _vertShaderModule, null);
        _vk.DestroyShaderModule(_device, _fragShaderModule, null);
        _vk.DestroyRenderPass(_device, _renderPass, null);
        if (_uploadCommandPool.Handle != 0)
        {
            _vk.DestroyCommandPool(_device, _uploadCommandPool, null);
        }
        foreach (var fb in _framebuffers)
        {
            _vk.DestroyFramebuffer(_device, fb, null);
        }
    }

    private sealed class UiTextureGpu
    {
        public UiTextureGpu(int textureId, int width, int height)
        {
            TextureId = textureId;
            Width = width;
            Height = height;
        }

        public int TextureId { get; }
        public int Width { get; }
        public int Height { get; }
        public int Version { get; set; }

        public Image Image;
        public DeviceMemory Memory;
        public ImageView View;
        public Sampler Sampler;
        public DescriptorSet DescriptorSet;
        public ImageLayout Layout;
    }
}
