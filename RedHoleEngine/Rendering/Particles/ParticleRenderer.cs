using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using RedHoleEngine.Particles;
using Silk.NET.Vulkan;
using VkBuffer = Silk.NET.Vulkan.Buffer;

namespace RedHoleEngine.Rendering.Particles;

/// <summary>
/// GPU instance data for a single particle (matches shader layout)
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct ParticleInstanceData
{
    public Vector3 Position;   // location 1
    public float Size;         // location 2
    public Vector4 Color;      // location 3
    public float Rotation;     // location 4
    
    public static readonly int SizeInBytes = Marshal.SizeOf<ParticleInstanceData>();

    public static ParticleInstanceData FromParticle(in Particle p)
    {
        return new ParticleInstanceData
        {
            Position = p.Position,
            Size = p.Size,
            Color = p.Color,
            Rotation = p.Rotation
        };
    }
}

/// <summary>
/// Uniform buffer for particle rendering (matches debug uniforms)
/// </summary>
[StructLayout(LayoutKind.Explicit, Size = 160)]
public struct ParticleUniforms
{
    [FieldOffset(0)] public Matrix4x4 View;           // 64 bytes
    [FieldOffset(64)] public Matrix4x4 Projection;    // 64 bytes  
    [FieldOffset(128)] public Matrix4x4 ViewProjection; // 64 bytes (unused here, can reuse for camera pos)
    [FieldOffset(192)] public Vector3 CameraPosition; // 12 bytes
    [FieldOffset(204)] public float Time;             // 4 bytes
}

/// <summary>
/// Vulkan-based GPU particle renderer using instanced billboard quads.
/// Renders particles as camera-facing sprites with alpha blending.
/// </summary>
public unsafe class ParticleRenderer : IDisposable
{
    private readonly Vk _vk;
    private readonly Device _device;
    private readonly PhysicalDevice _physicalDevice;
    private readonly uint _graphicsQueueFamily;
    
    // Pipeline resources
    private Pipeline _pipeline;
    private PipelineLayout _pipelineLayout;
    private DescriptorSetLayout _descriptorSetLayout;
    private DescriptorPool _descriptorPool;
    private DescriptorSet _descriptorSet;
    
    // Render pass (shared with debug renderer)
    private RenderPass _renderPass;
    private Framebuffer[] _framebuffers = Array.Empty<Framebuffer>();
    
    // Quad vertex buffer (static, 4 vertices for billboard)
    private VkBuffer _quadVertexBuffer;
    private DeviceMemory _quadVertexMemory;
    
    // Index buffer for quad
    private VkBuffer _indexBuffer;
    private DeviceMemory _indexBufferMemory;
    
    // Instance buffer (dynamic, updated each frame with particle data)
    private VkBuffer _instanceBuffer;
    private DeviceMemory _instanceBufferMemory;
    private void* _instanceBufferMapped;
    private int _maxInstances = 100000;
    
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
    
    // Current frame particle count
    private int _instanceCount;

    public bool IsInitialized => _initialized;
    public int MaxParticles => _maxInstances;
    public int CurrentParticleCount => _instanceCount;

    public ParticleRenderer(Vk vk, Device device, PhysicalDevice physicalDevice, uint graphicsQueueFamily)
    {
        _vk = vk;
        _device = device;
        _physicalDevice = physicalDevice;
        _graphicsQueueFamily = graphicsQueueFamily;
    }

    /// <summary>
    /// Initialize the particle renderer with swapchain info
    /// </summary>
    public void Initialize(Format swapchainFormat, Extent2D extent, ImageView[] swapchainImageViews)
    {
        _swapchainFormat = swapchainFormat;
        _extent = extent;
        
        CreateRenderPass();
        CreateFramebuffers(swapchainImageViews);
        CreateDescriptorSetLayout();
        LoadShaders();
        CreatePipeline();
        CreateDescriptorPool();
        CreateDescriptorSet();
        CreateQuadVertexBuffer();
        CreateIndexBuffer();
        CreateInstanceBuffer();
        CreateUniformBuffer();
        
        _initialized = true;
        Console.WriteLine($"Particle renderer initialized (max {_maxInstances} particles)");
    }

    /// <summary>
    /// Recreate framebuffers after swapchain resize
    /// </summary>
    public void Resize(Extent2D extent, ImageView[] swapchainImageViews)
    {
        _extent = extent;
        
        foreach (var fb in _framebuffers)
        {
            _vk.DestroyFramebuffer(_device, fb, null);
        }
        
        CreateFramebuffers(swapchainImageViews);
    }

    /// <summary>
    /// Update particle data from pools
    /// </summary>
    public void UpdateParticles(ReadOnlySpan<ParticleRenderData> particles)
    {
        _instanceCount = Math.Min(particles.Length, _maxInstances);
        
        if (_instanceCount == 0)
            return;
        
        // Copy particle data to instance buffer
        var dest = new Span<ParticleInstanceData>(_instanceBufferMapped, _instanceCount);
        for (int i = 0; i < _instanceCount; i++)
        {
            dest[i] = new ParticleInstanceData
            {
                Position = particles[i].Position,
                Size = particles[i].Size,
                Color = particles[i].Color,
                Rotation = 0f // ParticleRenderData doesn't include rotation currently
            };
        }
    }

    /// <summary>
    /// Update particles directly from a particle pool
    /// </summary>
    public void UpdateParticles(ParticlePool pool)
    {
        var particles = pool.GetAliveParticlesReadOnly();
        _instanceCount = Math.Min(particles.Length, _maxInstances);
        
        if (_instanceCount == 0)
            return;
        
        var dest = new Span<ParticleInstanceData>(_instanceBufferMapped, _instanceCount);
        for (int i = 0; i < _instanceCount; i++)
        {
            dest[i] = ParticleInstanceData.FromParticle(in particles[i]);
        }
    }

    /// <summary>
    /// Update particles with sorting for correct transparency
    /// </summary>
    public void UpdateParticles(ParticlePool pool, ParticleSortMode sortMode, Vector3 cameraPosition, Vector3 cameraForward)
    {
        if (pool.AliveCount == 0)
        {
            _instanceCount = 0;
            return;
        }

        // Sort particles if needed (modifies pool in place)
        if (sortMode != ParticleSortMode.None)
        {
            pool.Sort(sortMode, cameraPosition, cameraForward);
        }

        // Upload sorted particles
        UpdateParticles(pool);
    }

    /// <summary>
    /// Update uniform buffer with camera matrices
    /// </summary>
    public void UpdateUniforms(Matrix4x4 view, Matrix4x4 projection, Vector3 cameraPosition, float time)
    {
        var uniforms = new ParticleUniforms
        {
            View = view,
            Projection = projection,
            ViewProjection = view * projection,
            CameraPosition = cameraPosition,
            Time = time
        };
        
        Unsafe.Copy(_uniformBufferMapped, ref uniforms);
    }

    /// <summary>
    /// Record render commands into command buffer
    /// </summary>
    public void RecordCommands(CommandBuffer commandBuffer, uint imageIndex)
    {
        if (!_initialized || _instanceCount == 0)
            return;
        
        // Begin render pass
        var renderPassBeginInfo = new RenderPassBeginInfo
        {
            SType = StructureType.RenderPassBeginInfo,
            RenderPass = _renderPass,
            Framebuffer = _framebuffers[imageIndex],
            RenderArea = new Rect2D { Offset = new Offset2D(0, 0), Extent = _extent },
            ClearValueCount = 0,
            PClearValues = null
        };
        
        _vk.CmdBeginRenderPass(commandBuffer, &renderPassBeginInfo, SubpassContents.Inline);
        
        // Bind pipeline
        _vk.CmdBindPipeline(commandBuffer, PipelineBindPoint.Graphics, _pipeline);
        
        // Set dynamic viewport and scissor
        var viewport = new Viewport
        {
            X = 0,
            Y = 0,
            Width = _extent.Width,
            Height = _extent.Height,
            MinDepth = 0f,
            MaxDepth = 1f
        };
        _vk.CmdSetViewport(commandBuffer, 0, 1, &viewport);
        
        var scissor = new Rect2D
        {
            Offset = new Offset2D(0, 0),
            Extent = _extent
        };
        _vk.CmdSetScissor(commandBuffer, 0, 1, &scissor);
        
        // Bind descriptor set (uniforms)
        var descriptorSet = _descriptorSet;
        _vk.CmdBindDescriptorSets(commandBuffer, PipelineBindPoint.Graphics, _pipelineLayout, 
            0, 1, &descriptorSet, 0, null);
        
        // Bind vertex buffers (quad vertices at binding 0, instances at binding 1)
        var vertexBuffers = stackalloc VkBuffer[] { _quadVertexBuffer, _instanceBuffer };
        var offsets = stackalloc ulong[] { 0, 0 };
        _vk.CmdBindVertexBuffers(commandBuffer, 0, 2, vertexBuffers, offsets);
        
        // Bind index buffer
        _vk.CmdBindIndexBuffer(commandBuffer, _indexBuffer, 0, IndexType.Uint16);
        
        // Draw instanced quads (6 indices per quad, _instanceCount instances)
        _vk.CmdDrawIndexed(commandBuffer, 6, (uint)_instanceCount, 0, 0, 0);
        
        _vk.CmdEndRenderPass(commandBuffer);
    }

    private void CreateRenderPass()
    {
        // Same as debug renderer - overlay on existing content
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
                    throw new Exception("Failed to create particle render pass");
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
                    throw new Exception("Failed to create particle framebuffer");
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
            StageFlags = ShaderStageFlags.VertexBit | ShaderStageFlags.FragmentBit
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
                throw new Exception("Failed to create particle descriptor set layout");
            }
        }
    }

    private void LoadShaders()
    {
        string basePath = Path.Combine(AppContext.BaseDirectory, "Rendering", "Shaders");
        
        byte[] vertCode = File.ReadAllBytes(Path.Combine(basePath, "particle.vert.spv"));
        byte[] fragCode = File.ReadAllBytes(Path.Combine(basePath, "particle.frag.spv"));
        
        _vertShaderModule = CreateShaderModule(vertCode);
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
                throw new Exception("Failed to create shader module");
            }

            return shaderModule;
        }
    }

    private void CreatePipeline()
    {
        // Shader stages
        var vertShaderStageInfo = new PipelineShaderStageCreateInfo
        {
            SType = StructureType.PipelineShaderStageCreateInfo,
            Stage = ShaderStageFlags.VertexBit,
            Module = _vertShaderModule,
            PName = (byte*)Marshal.StringToHGlobalAnsi("main")
        };

        var fragShaderStageInfo = new PipelineShaderStageCreateInfo
        {
            SType = StructureType.PipelineShaderStageCreateInfo,
            Stage = ShaderStageFlags.FragmentBit,
            Module = _fragShaderModule,
            PName = (byte*)Marshal.StringToHGlobalAnsi("main")
        };

        var shaderStages = stackalloc PipelineShaderStageCreateInfo[] { vertShaderStageInfo, fragShaderStageInfo };

        // Vertex input - binding 0: quad vertices (per-vertex), binding 1: instance data (per-instance)
        var bindingDescriptions = new VertexInputBindingDescription[]
        {
            new() // Quad vertex (vec2)
            {
                Binding = 0,
                Stride = sizeof(float) * 2,
                InputRate = VertexInputRate.Vertex
            },
            new() // Instance data
            {
                Binding = 1,
                Stride = (uint)ParticleInstanceData.SizeInBytes,
                InputRate = VertexInputRate.Instance
            }
        };

        var attributeDescriptions = new VertexInputAttributeDescription[]
        {
            // Quad vertex (location 0)
            new() { Binding = 0, Location = 0, Format = Format.R32G32Sfloat, Offset = 0 },
            // Instance: Position (location 1)
            new() { Binding = 1, Location = 1, Format = Format.R32G32B32Sfloat, Offset = 0 },
            // Instance: Size (location 2)
            new() { Binding = 1, Location = 2, Format = Format.R32Sfloat, Offset = 12 },
            // Instance: Color (location 3)
            new() { Binding = 1, Location = 3, Format = Format.R32G32B32A32Sfloat, Offset = 16 },
            // Instance: Rotation (location 4)
            new() { Binding = 1, Location = 4, Format = Format.R32Sfloat, Offset = 32 }
        };

        fixed (VertexInputBindingDescription* bindingsPtr = bindingDescriptions)
        fixed (VertexInputAttributeDescription* attribsPtr = attributeDescriptions)
        {
            var vertexInputInfo = new PipelineVertexInputStateCreateInfo
            {
                SType = StructureType.PipelineVertexInputStateCreateInfo,
                VertexBindingDescriptionCount = (uint)bindingDescriptions.Length,
                PVertexBindingDescriptions = bindingsPtr,
                VertexAttributeDescriptionCount = (uint)attributeDescriptions.Length,
                PVertexAttributeDescriptions = attribsPtr
            };

            var inputAssembly = new PipelineInputAssemblyStateCreateInfo
            {
                SType = StructureType.PipelineInputAssemblyStateCreateInfo,
                Topology = PrimitiveTopology.TriangleList,
                PrimitiveRestartEnable = false
            };

            // Dynamic viewport and scissor
            var dynamicStates = new DynamicState[] { DynamicState.Viewport, DynamicState.Scissor };
            
            fixed (DynamicState* dynPtr = dynamicStates)
            {
                var dynamicState = new PipelineDynamicStateCreateInfo
                {
                    SType = StructureType.PipelineDynamicStateCreateInfo,
                    DynamicStateCount = (uint)dynamicStates.Length,
                    PDynamicStates = dynPtr
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
                    LineWidth = 1f,
                    CullMode = CullModeFlags.None, // No culling for billboards
                    FrontFace = FrontFace.CounterClockwise,
                    DepthBiasEnable = false
                };

                var multisampling = new PipelineMultisampleStateCreateInfo
                {
                    SType = StructureType.PipelineMultisampleStateCreateInfo,
                    SampleShadingEnable = false,
                    RasterizationSamples = SampleCountFlags.Count1Bit
                };

                // Alpha blending for particles
                var colorBlendAttachment = new PipelineColorBlendAttachmentState
                {
                    ColorWriteMask = ColorComponentFlags.RBit | ColorComponentFlags.GBit | 
                                     ColorComponentFlags.BBit | ColorComponentFlags.ABit,
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

                // Pipeline layout
                var layoutCreateInfo = new PipelineLayoutCreateInfo
                {
                    SType = StructureType.PipelineLayoutCreateInfo,
                    SetLayoutCount = 1,
                    PSetLayouts = (DescriptorSetLayout*)Unsafe.AsPointer(ref _descriptorSetLayout)
                };

                fixed (PipelineLayout* layoutPtr = &_pipelineLayout)
                {
                    if (_vk.CreatePipelineLayout(_device, &layoutCreateInfo, null, layoutPtr) != Result.Success)
                    {
                        throw new Exception("Failed to create particle pipeline layout");
                    }
                }

                // Create graphics pipeline
                var pipelineInfo = new GraphicsPipelineCreateInfo
                {
                    SType = StructureType.GraphicsPipelineCreateInfo,
                    StageCount = 2,
                    PStages = shaderStages,
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

                fixed (Pipeline* pipelinePtr = &_pipeline)
                {
                    if (_vk.CreateGraphicsPipelines(_device, default, 1, &pipelineInfo, null, pipelinePtr) != Result.Success)
                    {
                        throw new Exception("Failed to create particle graphics pipeline");
                    }
                }
            }
        }

        // Free shader stage name strings
        Marshal.FreeHGlobal((nint)vertShaderStageInfo.PName);
        Marshal.FreeHGlobal((nint)fragShaderStageInfo.PName);
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
                throw new Exception("Failed to create particle descriptor pool");
            }
        }
    }

    private void CreateDescriptorSet()
    {
        var layout = _descriptorSetLayout;
        var allocInfo = new DescriptorSetAllocateInfo
        {
            SType = StructureType.DescriptorSetAllocateInfo,
            DescriptorPool = _descriptorPool,
            DescriptorSetCount = 1,
            PSetLayouts = &layout
        };

        fixed (DescriptorSet* setPtr = &_descriptorSet)
        {
            if (_vk.AllocateDescriptorSets(_device, &allocInfo, setPtr) != Result.Success)
            {
                throw new Exception("Failed to allocate particle descriptor set");
            }
        }
    }

    private void CreateQuadVertexBuffer()
    {
        // Quad vertices (2D positions, -0.5 to 0.5)
        float[] vertices = 
        {
            -0.5f, -0.5f, // Bottom-left
             0.5f, -0.5f, // Bottom-right
             0.5f,  0.5f, // Top-right
            -0.5f,  0.5f  // Top-left
        };

        ulong bufferSize = (ulong)(vertices.Length * sizeof(float));

        CreateBuffer(bufferSize, BufferUsageFlags.VertexBufferBit,
            MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit,
            out _quadVertexBuffer, out _quadVertexMemory);

        void* data;
        _vk.MapMemory(_device, _quadVertexMemory, 0, bufferSize, 0, &data);
        fixed (float* vertPtr = vertices)
        {
            System.Buffer.MemoryCopy(vertPtr, data, (long)bufferSize, (long)bufferSize);
        }
        _vk.UnmapMemory(_device, _quadVertexMemory);
    }

    private void CreateIndexBuffer()
    {
        // Two triangles for the quad
        ushort[] indices = { 0, 1, 2, 2, 3, 0 };
        ulong bufferSize = (ulong)(indices.Length * sizeof(ushort));

        CreateBuffer(bufferSize, BufferUsageFlags.IndexBufferBit,
            MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit,
            out _indexBuffer, out _indexBufferMemory);

        void* data;
        _vk.MapMemory(_device, _indexBufferMemory, 0, bufferSize, 0, &data);
        fixed (ushort* idxPtr = indices)
        {
            System.Buffer.MemoryCopy(idxPtr, data, (long)bufferSize, (long)bufferSize);
        }
        _vk.UnmapMemory(_device, _indexBufferMemory);
    }

    private void CreateInstanceBuffer()
    {
        ulong bufferSize = (ulong)(_maxInstances * ParticleInstanceData.SizeInBytes);

        CreateBuffer(bufferSize, BufferUsageFlags.VertexBufferBit,
            MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit,
            out _instanceBuffer, out _instanceBufferMemory);

        // Persistently map
        fixed (void** mappedPtr = &_instanceBufferMapped)
        {
            _vk.MapMemory(_device, _instanceBufferMemory, 0, bufferSize, 0, mappedPtr);
        }
    }

    private void CreateUniformBuffer()
    {
        ulong bufferSize = (ulong)Marshal.SizeOf<ParticleUniforms>();

        CreateBuffer(bufferSize, BufferUsageFlags.UniformBufferBit,
            MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit,
            out _uniformBuffer, out _uniformBufferMemory);

        // Persistently map
        fixed (void** mappedPtr = &_uniformBufferMapped)
        {
            _vk.MapMemory(_device, _uniformBufferMemory, 0, bufferSize, 0, mappedPtr);
        }

        // Update descriptor set to point to uniform buffer
        var bufferInfo = new DescriptorBufferInfo
        {
            Buffer = _uniformBuffer,
            Offset = 0,
            Range = bufferSize
        };

        var descriptorWrite = new WriteDescriptorSet
        {
            SType = StructureType.WriteDescriptorSet,
            DstSet = _descriptorSet,
            DstBinding = 0,
            DstArrayElement = 0,
            DescriptorType = DescriptorType.UniformBuffer,
            DescriptorCount = 1,
            PBufferInfo = &bufferInfo
        };

        _vk.UpdateDescriptorSets(_device, 1, &descriptorWrite, 0, null);
    }

    private void CreateBuffer(ulong size, BufferUsageFlags usage, MemoryPropertyFlags properties,
        out VkBuffer buffer, out DeviceMemory memory)
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
            {
                throw new Exception("Failed to create buffer");
            }
        }

        _vk.GetBufferMemoryRequirements(_device, buffer, out var memRequirements);

        var allocInfo = new MemoryAllocateInfo
        {
            SType = StructureType.MemoryAllocateInfo,
            AllocationSize = memRequirements.Size,
            MemoryTypeIndex = FindMemoryType(memRequirements.MemoryTypeBits, properties)
        };

        fixed (DeviceMemory* memoryPtr = &memory)
        {
            if (_vk.AllocateMemory(_device, &allocInfo, null, memoryPtr) != Result.Success)
            {
                throw new Exception("Failed to allocate buffer memory");
            }
        }

        _vk.BindBufferMemory(_device, buffer, memory, 0);
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

        throw new Exception("Failed to find suitable memory type");
    }

    public void Dispose()
    {
        if (!_initialized)
            return;

        _vk.DeviceWaitIdle(_device);

        // Unmap persistent mappings
        _vk.UnmapMemory(_device, _instanceBufferMemory);
        _vk.UnmapMemory(_device, _uniformBufferMemory);

        // Destroy buffers
        _vk.DestroyBuffer(_device, _quadVertexBuffer, null);
        _vk.FreeMemory(_device, _quadVertexMemory, null);
        
        _vk.DestroyBuffer(_device, _indexBuffer, null);
        _vk.FreeMemory(_device, _indexBufferMemory, null);
        
        _vk.DestroyBuffer(_device, _instanceBuffer, null);
        _vk.FreeMemory(_device, _instanceBufferMemory, null);
        
        _vk.DestroyBuffer(_device, _uniformBuffer, null);
        _vk.FreeMemory(_device, _uniformBufferMemory, null);

        // Destroy pipeline resources
        _vk.DestroyPipeline(_device, _pipeline, null);
        _vk.DestroyPipelineLayout(_device, _pipelineLayout, null);
        
        _vk.DestroyDescriptorPool(_device, _descriptorPool, null);
        _vk.DestroyDescriptorSetLayout(_device, _descriptorSetLayout, null);

        // Destroy shaders
        _vk.DestroyShaderModule(_device, _vertShaderModule, null);
        _vk.DestroyShaderModule(_device, _fragShaderModule, null);

        // Destroy framebuffers and render pass
        foreach (var fb in _framebuffers)
        {
            _vk.DestroyFramebuffer(_device, fb, null);
        }
        _vk.DestroyRenderPass(_device, _renderPass, null);

        _initialized = false;
        Console.WriteLine("Particle renderer disposed");
    }
}
