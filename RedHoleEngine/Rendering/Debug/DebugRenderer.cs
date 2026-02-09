using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Silk.NET.Vulkan;
using VkBuffer = Silk.NET.Vulkan.Buffer;

namespace RedHoleEngine.Rendering.Debug;

/// <summary>
/// Uniform buffer object for debug rendering
/// </summary>
[StructLayout(LayoutKind.Explicit, Size = 144)]
public struct DebugUniforms
{
    [FieldOffset(0)] public Matrix4x4 View;
    [FieldOffset(64)] public Matrix4x4 Projection;
    [FieldOffset(128)] public Matrix4x4 ViewProjection;
}

/// <summary>
/// Vulkan-based renderer for debug primitives (lines and points).
/// Renders on top of the main scene after the blit operation.
/// </summary>
public unsafe class DebugRenderer : IDisposable
{
    private readonly Vk _vk;
    private readonly Device _device;
    private readonly PhysicalDevice _physicalDevice;
    private readonly uint _graphicsQueueFamily;
    
    // Pipeline resources
    private Pipeline _linePipeline;
    private Pipeline _pointPipeline;
    private PipelineLayout _pipelineLayout;
    private DescriptorSetLayout _descriptorSetLayout;
    private DescriptorPool _descriptorPool;
    private DescriptorSet _descriptorSet;
    
    // Render pass for overlay rendering
    private RenderPass _renderPass;
    private Framebuffer[] _framebuffers = Array.Empty<Framebuffer>();
    
    // Vertex buffer (dynamic, updated each frame)
    private VkBuffer _vertexBuffer;
    private DeviceMemory _vertexBufferMemory;
    private void* _vertexBufferMapped;
    private int _maxVertices = 65536; // Initial capacity
    
    // Uniform buffer
    private VkBuffer _uniformBuffer;
    private DeviceMemory _uniformBufferMemory;
    private void* _uniformBufferMapped;
    
    // Shader modules
    private ShaderModule _vertShaderModule;
    private ShaderModule _fragShaderModule;
    
    // State
    private Extent2D _extent;
    private Format _swapchainFormat;
    private bool _initialized;

    public bool IsInitialized => _initialized;

    public DebugRenderer(Vk vk, Device device, PhysicalDevice physicalDevice, uint graphicsQueueFamily)
    {
        _vk = vk;
        _device = device;
        _physicalDevice = physicalDevice;
        _graphicsQueueFamily = graphicsQueueFamily;
    }

    /// <summary>
    /// Initialize the debug renderer with swapchain info
    /// </summary>
    public void Initialize(Format swapchainFormat, Extent2D extent, ImageView[] swapchainImageViews)
    {
        _swapchainFormat = swapchainFormat;
        _extent = extent;
        
        CreateRenderPass();
        CreateFramebuffers(swapchainImageViews);
        CreateDescriptorSetLayout();
        LoadShaders();
        CreatePipelines();
        CreateDescriptorPool();
        CreateDescriptorSet();
        CreateVertexBuffer();
        CreateUniformBuffer();
        
        _initialized = true;
        Console.WriteLine("Debug renderer initialized");
    }

    /// <summary>
    /// Recreate framebuffers after swapchain resize
    /// </summary>
    public void Resize(Extent2D extent, ImageView[] swapchainImageViews)
    {
        _extent = extent;
        
        // Destroy old framebuffers
        foreach (var fb in _framebuffers)
        {
            _vk.DestroyFramebuffer(_device, fb, null);
        }
        
        CreateFramebuffers(swapchainImageViews);
    }

    private void CreateRenderPass()
    {
        // Attachment for the swapchain image (load existing, store result)
        var colorAttachment = new AttachmentDescription
        {
            Format = _swapchainFormat,
            Samples = SampleCountFlags.Count1Bit,
            LoadOp = AttachmentLoadOp.Load, // Preserve what's already rendered
            StoreOp = AttachmentStoreOp.Store,
            StencilLoadOp = AttachmentLoadOp.DontCare,
            StencilStoreOp = AttachmentStoreOp.DontCare,
            InitialLayout = ImageLayout.PresentSrcKhr, // After blit, it's in present layout
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

        // Dependencies to ensure proper synchronization
        var dependencies = new SubpassDependency[]
        {
            new()
            {
                SrcSubpass = Vk.SubpassExternal,
                DstSubpass = 0,
                SrcStageMask = PipelineStageFlags.ColorAttachmentOutputBit,
                DstStageMask = PipelineStageFlags.ColorAttachmentOutputBit,
                SrcAccessMask = 0,
                DstAccessMask = AccessFlags.ColorAttachmentWriteBit
            },
            new()
            {
                SrcSubpass = 0,
                DstSubpass = Vk.SubpassExternal,
                SrcStageMask = PipelineStageFlags.ColorAttachmentOutputBit,
                DstStageMask = PipelineStageFlags.BottomOfPipeBit,
                SrcAccessMask = AccessFlags.ColorAttachmentWriteBit,
                DstAccessMask = 0
            }
        };

        fixed (SubpassDependency* depsPtr = dependencies)
        {
            var renderPassInfo = new RenderPassCreateInfo
            {
                SType = StructureType.RenderPassCreateInfo,
                AttachmentCount = 1,
                PAttachments = &colorAttachment,
                SubpassCount = 1,
                PSubpasses = &subpass,
                DependencyCount = (uint)dependencies.Length,
                PDependencies = depsPtr
            };

            fixed (RenderPass* rpPtr = &_renderPass)
            {
                if (_vk.CreateRenderPass(_device, &renderPassInfo, null, rpPtr) != Result.Success)
                {
                    throw new Exception("Failed to create debug render pass");
                }
            }
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

            fixed (Framebuffer* fbPtr = &_framebuffers[i])
            {
                if (_vk.CreateFramebuffer(_device, &framebufferInfo, null, fbPtr) != Result.Success)
                {
                    throw new Exception($"Failed to create debug framebuffer {i}");
                }
            }
        }
    }

    private void CreateDescriptorSetLayout()
    {
        var binding = new DescriptorSetLayoutBinding
        {
            Binding = 0,
            DescriptorType = DescriptorType.UniformBuffer,
            DescriptorCount = 1,
            StageFlags = ShaderStageFlags.VertexBit
        };

        var layoutInfo = new DescriptorSetLayoutCreateInfo
        {
            SType = StructureType.DescriptorSetLayoutCreateInfo,
            BindingCount = 1,
            PBindings = &binding
        };

        fixed (DescriptorSetLayout* layoutPtr = &_descriptorSetLayout)
        {
            if (_vk.CreateDescriptorSetLayout(_device, &layoutInfo, null, layoutPtr) != Result.Success)
            {
                throw new Exception("Failed to create debug descriptor set layout");
            }
        }
    }

    private void LoadShaders()
    {
        string basePath = Path.Combine(AppContext.BaseDirectory, "Rendering", "Shaders");
        
        // Load vertex shader
        string vertPath = Path.Combine(basePath, "debug_line.vert.spv");
        if (!File.Exists(vertPath))
        {
            throw new FileNotFoundException($"Debug vertex shader not found: {vertPath}. " +
                "Run: glslangValidator -V debug_line.vert -o debug_line.vert.spv");
        }
        var vertCode = File.ReadAllBytes(vertPath);
        _vertShaderModule = CreateShaderModule(vertCode);

        // Load fragment shader
        string fragPath = Path.Combine(basePath, "debug_line.frag.spv");
        if (!File.Exists(fragPath))
        {
            throw new FileNotFoundException($"Debug fragment shader not found: {fragPath}. " +
                "Run: glslangValidator -V debug_line.frag -o debug_line.frag.spv");
        }
        var fragCode = File.ReadAllBytes(fragPath);
        _fragShaderModule = CreateShaderModule(fragCode);
    }

    private ShaderModule CreateShaderModule(byte[] code)
    {
        fixed (byte* codePtr = code)
        {
            var createInfo = new ShaderModuleCreateInfo
            {
                SType = StructureType.ShaderModuleCreateInfo,
                CodeSize = (nuint)code.Length,
                PCode = (uint*)codePtr
            };

            ShaderModule shaderModule;
            if (_vk.CreateShaderModule(_device, &createInfo, null, &shaderModule) != Result.Success)
            {
                throw new Exception("Failed to create debug shader module");
            }

            return shaderModule;
        }
    }

    private void CreatePipelines()
    {
        // Pipeline layout
        fixed (DescriptorSetLayout* layoutPtr = &_descriptorSetLayout)
        {
            var pipelineLayoutInfo = new PipelineLayoutCreateInfo
            {
                SType = StructureType.PipelineLayoutCreateInfo,
                SetLayoutCount = 1,
                PSetLayouts = layoutPtr
            };

            fixed (PipelineLayout* plPtr = &_pipelineLayout)
            {
                if (_vk.CreatePipelineLayout(_device, &pipelineLayoutInfo, null, plPtr) != Result.Success)
                {
                    throw new Exception("Failed to create debug pipeline layout");
                }
            }
        }

        // Create line pipeline
        _linePipeline = CreateGraphicsPipeline(PrimitiveTopology.LineList);
        
        // Create point pipeline
        _pointPipeline = CreateGraphicsPipeline(PrimitiveTopology.PointList);
    }

    private Pipeline CreateGraphicsPipeline(PrimitiveTopology topology)
    {
        var mainName = (byte*)Marshal.StringToHGlobalAnsi("main");

        var shaderStages = new PipelineShaderStageCreateInfo[]
        {
            new()
            {
                SType = StructureType.PipelineShaderStageCreateInfo,
                Stage = ShaderStageFlags.VertexBit,
                Module = _vertShaderModule,
                PName = mainName
            },
            new()
            {
                SType = StructureType.PipelineShaderStageCreateInfo,
                Stage = ShaderStageFlags.FragmentBit,
                Module = _fragShaderModule,
                PName = mainName
            }
        };

        // Vertex input: position (vec3) + color (vec4)
        var bindingDesc = new VertexInputBindingDescription
        {
            Binding = 0,
            Stride = (uint)DebugVertex.SizeInBytes,
            InputRate = VertexInputRate.Vertex
        };

        var attributeDescs = new VertexInputAttributeDescription[]
        {
            new() // Position
            {
                Binding = 0,
                Location = 0,
                Format = Format.R32G32B32Sfloat,
                Offset = 0
            },
            new() // Color
            {
                Binding = 0,
                Location = 1,
                Format = Format.R32G32B32A32Sfloat,
                Offset = 12 // sizeof(Vector3)
            }
        };

        fixed (VertexInputAttributeDescription* attrPtr = attributeDescs)
        {
            var vertexInputInfo = new PipelineVertexInputStateCreateInfo
            {
                SType = StructureType.PipelineVertexInputStateCreateInfo,
                VertexBindingDescriptionCount = 1,
                PVertexBindingDescriptions = &bindingDesc,
                VertexAttributeDescriptionCount = (uint)attributeDescs.Length,
                PVertexAttributeDescriptions = attrPtr
            };

            var inputAssembly = new PipelineInputAssemblyStateCreateInfo
            {
                SType = StructureType.PipelineInputAssemblyStateCreateInfo,
                Topology = topology,
                PrimitiveRestartEnable = false
            };

            var viewport = new Viewport
            {
                X = 0,
                Y = 0,
                Width = _extent.Width,
                Height = _extent.Height,
                MinDepth = 0,
                MaxDepth = 1
            };

            var scissor = new Rect2D
            {
                Offset = new Offset2D(0, 0),
                Extent = _extent
            };

            var viewportState = new PipelineViewportStateCreateInfo
            {
                SType = StructureType.PipelineViewportStateCreateInfo,
                ViewportCount = 1,
                PViewports = &viewport,
                ScissorCount = 1,
                PScissors = &scissor
            };

            var rasterizer = new PipelineRasterizationStateCreateInfo
            {
                SType = StructureType.PipelineRasterizationStateCreateInfo,
                DepthClampEnable = false,
                RasterizerDiscardEnable = false,
                PolygonMode = PolygonMode.Fill,
                LineWidth = 1.0f,
                CullMode = CullModeFlags.None,
                FrontFace = FrontFace.CounterClockwise,
                DepthBiasEnable = false
            };

            var multisampling = new PipelineMultisampleStateCreateInfo
            {
                SType = StructureType.PipelineMultisampleStateCreateInfo,
                SampleShadingEnable = false,
                RasterizationSamples = SampleCountFlags.Count1Bit
            };

            // Alpha blending for transparent debug lines
            var colorBlendAttachment = new PipelineColorBlendAttachmentState
            {
                ColorWriteMask = ColorComponentFlags.RBit | ColorComponentFlags.GBit | 
                                 ColorComponentFlags.BBit | ColorComponentFlags.ABit,
                BlendEnable = true,
                SrcColorBlendFactor = BlendFactor.SrcAlpha,
                DstColorBlendFactor = BlendFactor.OneMinusSrcAlpha,
                ColorBlendOp = BlendOp.Add,
                SrcAlphaBlendFactor = BlendFactor.One,
                DstAlphaBlendFactor = BlendFactor.Zero,
                AlphaBlendOp = BlendOp.Add
            };

            var colorBlending = new PipelineColorBlendStateCreateInfo
            {
                SType = StructureType.PipelineColorBlendStateCreateInfo,
                LogicOpEnable = false,
                AttachmentCount = 1,
                PAttachments = &colorBlendAttachment
            };

            // Dynamic state for viewport/scissor (allows resize without recreating pipeline)
            var dynamicStates = new DynamicState[] { DynamicState.Viewport, DynamicState.Scissor };
            
            fixed (DynamicState* dynPtr = dynamicStates)
            {
                var dynamicState = new PipelineDynamicStateCreateInfo
                {
                    SType = StructureType.PipelineDynamicStateCreateInfo,
                    DynamicStateCount = (uint)dynamicStates.Length,
                    PDynamicStates = dynPtr
                };

                fixed (PipelineShaderStageCreateInfo* stagesPtr = shaderStages)
                {
                    var pipelineInfo = new GraphicsPipelineCreateInfo
                    {
                        SType = StructureType.GraphicsPipelineCreateInfo,
                        StageCount = (uint)shaderStages.Length,
                        PStages = stagesPtr,
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

                    Pipeline pipeline;
                    if (_vk.CreateGraphicsPipelines(_device, default, 1, &pipelineInfo, null, &pipeline) != Result.Success)
                    {
                        throw new Exception($"Failed to create debug pipeline for {topology}");
                    }

                    return pipeline;
                }
            }
        }
    }

    private void CreateDescriptorPool()
    {
        var poolSize = new DescriptorPoolSize
        {
            Type = DescriptorType.UniformBuffer,
            DescriptorCount = 1
        };

        var poolInfo = new DescriptorPoolCreateInfo
        {
            SType = StructureType.DescriptorPoolCreateInfo,
            PoolSizeCount = 1,
            PPoolSizes = &poolSize,
            MaxSets = 1
        };

        fixed (DescriptorPool* poolPtr = &_descriptorPool)
        {
            if (_vk.CreateDescriptorPool(_device, &poolInfo, null, poolPtr) != Result.Success)
            {
                throw new Exception("Failed to create debug descriptor pool");
            }
        }
    }

    private void CreateDescriptorSet()
    {
        fixed (DescriptorSetLayout* layoutPtr = &_descriptorSetLayout)
        {
            var allocInfo = new DescriptorSetAllocateInfo
            {
                SType = StructureType.DescriptorSetAllocateInfo,
                DescriptorPool = _descriptorPool,
                DescriptorSetCount = 1,
                PSetLayouts = layoutPtr
            };

            fixed (DescriptorSet* setPtr = &_descriptorSet)
            {
                if (_vk.AllocateDescriptorSets(_device, &allocInfo, setPtr) != Result.Success)
                {
                    throw new Exception("Failed to allocate debug descriptor set");
                }
            }
        }
    }

    private void CreateVertexBuffer()
    {
        ulong bufferSize = (ulong)(_maxVertices * DebugVertex.SizeInBytes);

        var bufferInfo = new BufferCreateInfo
        {
            SType = StructureType.BufferCreateInfo,
            Size = bufferSize,
            Usage = BufferUsageFlags.VertexBufferBit,
            SharingMode = SharingMode.Exclusive
        };

        fixed (VkBuffer* bufPtr = &_vertexBuffer)
        {
            if (_vk.CreateBuffer(_device, &bufferInfo, null, bufPtr) != Result.Success)
            {
                throw new Exception("Failed to create debug vertex buffer");
            }
        }

        _vk.GetBufferMemoryRequirements(_device, _vertexBuffer, out var memRequirements);

        var allocInfo = new MemoryAllocateInfo
        {
            SType = StructureType.MemoryAllocateInfo,
            AllocationSize = memRequirements.Size,
            MemoryTypeIndex = FindMemoryType(memRequirements.MemoryTypeBits,
                MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit)
        };

        fixed (DeviceMemory* memPtr = &_vertexBufferMemory)
        {
            if (_vk.AllocateMemory(_device, &allocInfo, null, memPtr) != Result.Success)
            {
                throw new Exception("Failed to allocate debug vertex buffer memory");
            }
        }

        _vk.BindBufferMemory(_device, _vertexBuffer, _vertexBufferMemory, 0);

        fixed (void** mapped = &_vertexBufferMapped)
        {
            _vk.MapMemory(_device, _vertexBufferMemory, 0, bufferSize, 0, mapped);
        }
    }

    private void CreateUniformBuffer()
    {
        ulong bufferSize = (ulong)Unsafe.SizeOf<DebugUniforms>();

        var bufferInfo = new BufferCreateInfo
        {
            SType = StructureType.BufferCreateInfo,
            Size = bufferSize,
            Usage = BufferUsageFlags.UniformBufferBit,
            SharingMode = SharingMode.Exclusive
        };

        fixed (VkBuffer* bufPtr = &_uniformBuffer)
        {
            if (_vk.CreateBuffer(_device, &bufferInfo, null, bufPtr) != Result.Success)
            {
                throw new Exception("Failed to create debug uniform buffer");
            }
        }

        _vk.GetBufferMemoryRequirements(_device, _uniformBuffer, out var memRequirements);

        var allocInfo = new MemoryAllocateInfo
        {
            SType = StructureType.MemoryAllocateInfo,
            AllocationSize = memRequirements.Size,
            MemoryTypeIndex = FindMemoryType(memRequirements.MemoryTypeBits,
                MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit)
        };

        fixed (DeviceMemory* memPtr = &_uniformBufferMemory)
        {
            if (_vk.AllocateMemory(_device, &allocInfo, null, memPtr) != Result.Success)
            {
                throw new Exception("Failed to allocate debug uniform buffer memory");
            }
        }

        _vk.BindBufferMemory(_device, _uniformBuffer, _uniformBufferMemory, 0);

        fixed (void** mapped = &_uniformBufferMapped)
        {
            _vk.MapMemory(_device, _uniformBufferMemory, 0, bufferSize, 0, mapped);
        }

        // Update descriptor set with uniform buffer
        var bufferDescInfo = new DescriptorBufferInfo
        {
            Buffer = _uniformBuffer,
            Offset = 0,
            Range = bufferSize
        };

        var writeDescriptor = new WriteDescriptorSet
        {
            SType = StructureType.WriteDescriptorSet,
            DstSet = _descriptorSet,
            DstBinding = 0,
            DstArrayElement = 0,
            DescriptorType = DescriptorType.UniformBuffer,
            DescriptorCount = 1,
            PBufferInfo = &bufferDescInfo
        };

        _vk.UpdateDescriptorSets(_device, 1, &writeDescriptor, 0, null);
    }

    private uint FindMemoryType(uint typeFilter, MemoryPropertyFlags properties)
    {
        _vk.GetPhysicalDeviceMemoryProperties(_physicalDevice, out var memProperties);

        for (uint i = 0; i < memProperties.MemoryTypeCount; i++)
        {
            if ((typeFilter & (1 << (int)i)) != 0 &&
                (memProperties.MemoryTypes[(int)i].PropertyFlags & properties) == properties)
            {
                return i;
            }
        }

        throw new Exception("Failed to find suitable memory type for debug renderer");
    }

    /// <summary>
    /// Record commands to render debug primitives
    /// </summary>
    public void RecordCommands(
        CommandBuffer commandBuffer, 
        uint imageIndex,
        DebugDrawManager debugDraw,
        Matrix4x4 view,
        Matrix4x4 projection)
    {
        if (!_initialized || !debugDraw.HasContent) return;

        // Update uniforms
        var uniforms = new DebugUniforms
        {
            View = view,
            Projection = projection,
            ViewProjection = view * projection
        };
        Unsafe.Copy(_uniformBufferMapped, ref uniforms);

        // Upload vertices
        int lineCount = debugDraw.LineVertices.Count;
        int pointCount = debugDraw.PointVertices.Count;
        int totalVertices = lineCount + pointCount;

        if (totalVertices == 0) return;

        // Resize buffer if needed
        if (totalVertices > _maxVertices)
        {
            // For now, just clamp - in production you'd reallocate
            Console.WriteLine($"Warning: Debug vertex count {totalVertices} exceeds max {_maxVertices}");
            lineCount = Math.Min(lineCount, _maxVertices);
            pointCount = Math.Min(pointCount, _maxVertices - lineCount);
        }

        // Copy line vertices
        if (lineCount > 0)
        {
            var span = new Span<DebugVertex>(_vertexBufferMapped, lineCount);
            for (int i = 0; i < lineCount; i++)
            {
                span[i] = debugDraw.LineVertices[i];
            }
        }

        // Copy point vertices after lines
        if (pointCount > 0)
        {
            var pointStart = (DebugVertex*)_vertexBufferMapped + lineCount;
            var span = new Span<DebugVertex>(pointStart, pointCount);
            for (int i = 0; i < pointCount; i++)
            {
                span[i] = debugDraw.PointVertices[i];
            }
        }

        // Transition swapchain image from present to color attachment
        TransitionImageLayoutForRenderPass(commandBuffer, imageIndex);

        // Begin render pass (no clear needed since LoadOp is Load)
        var renderPassInfo = new RenderPassBeginInfo
        {
            SType = StructureType.RenderPassBeginInfo,
            RenderPass = _renderPass,
            Framebuffer = _framebuffers[imageIndex],
            RenderArea = new Rect2D
            {
                Offset = new Offset2D(0, 0),
                Extent = _extent
            },
            ClearValueCount = 0,
            PClearValues = null
        };

        _vk.CmdBeginRenderPass(commandBuffer, &renderPassInfo, SubpassContents.Inline);

        // Set dynamic viewport and scissor
        var viewport = new Viewport
        {
            X = 0,
            Y = 0,
            Width = _extent.Width,
            Height = _extent.Height,
            MinDepth = 0,
            MaxDepth = 1
        };
        _vk.CmdSetViewport(commandBuffer, 0, 1, &viewport);

        var scissor = new Rect2D
        {
            Offset = new Offset2D(0, 0),
            Extent = _extent
        };
        _vk.CmdSetScissor(commandBuffer, 0, 1, &scissor);

        // Bind vertex buffer
        var vertexBuffer = _vertexBuffer;
        ulong offset = 0;
        _vk.CmdBindVertexBuffers(commandBuffer, 0, 1, &vertexBuffer, &offset);

        // Bind descriptor set
        var descriptorSet = _descriptorSet;
        _vk.CmdBindDescriptorSets(commandBuffer, PipelineBindPoint.Graphics, _pipelineLayout, 0, 1, &descriptorSet, 0, null);

        // Draw lines
        if (lineCount > 0)
        {
            _vk.CmdBindPipeline(commandBuffer, PipelineBindPoint.Graphics, _linePipeline);
            _vk.CmdDraw(commandBuffer, (uint)lineCount, 1, 0, 0);
        }

        // Draw points
        if (pointCount > 0)
        {
            _vk.CmdBindPipeline(commandBuffer, PipelineBindPoint.Graphics, _pointPipeline);
            _vk.CmdDraw(commandBuffer, (uint)pointCount, 1, (uint)lineCount, 0);
        }

        _vk.CmdEndRenderPass(commandBuffer);
    }

    private void TransitionImageLayoutForRenderPass(CommandBuffer cmd, uint imageIndex)
    {
        // The image is already in PresentSrcKhr layout from the blit operation
        // We need to transition to ColorAttachmentOptimal for our render pass
        // Actually, the render pass handles this automatically via initialLayout/finalLayout
        // So this method is a no-op unless we need explicit barriers
    }

    public void Dispose()
    {
        if (!_initialized) return;

        _vk.DeviceWaitIdle(_device);

        _vk.UnmapMemory(_device, _vertexBufferMemory);
        _vk.DestroyBuffer(_device, _vertexBuffer, null);
        _vk.FreeMemory(_device, _vertexBufferMemory, null);

        _vk.UnmapMemory(_device, _uniformBufferMemory);
        _vk.DestroyBuffer(_device, _uniformBuffer, null);
        _vk.FreeMemory(_device, _uniformBufferMemory, null);

        _vk.DestroyDescriptorPool(_device, _descriptorPool, null);
        _vk.DestroyDescriptorSetLayout(_device, _descriptorSetLayout, null);

        _vk.DestroyPipeline(_device, _linePipeline, null);
        _vk.DestroyPipeline(_device, _pointPipeline, null);
        _vk.DestroyPipelineLayout(_device, _pipelineLayout, null);

        _vk.DestroyShaderModule(_device, _vertShaderModule, null);
        _vk.DestroyShaderModule(_device, _fragShaderModule, null);

        foreach (var fb in _framebuffers)
        {
            _vk.DestroyFramebuffer(_device, fb, null);
        }
        _vk.DestroyRenderPass(_device, _renderPass, null);

        _initialized = false;
    }
}
