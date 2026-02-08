using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using RedHoleEngine.Engine;
using RedHoleEngine.Physics;
using Silk.NET.Core;
using Silk.NET.Core.Native;
using Silk.NET.Vulkan;
using Silk.NET.Vulkan.Extensions.KHR;
using Silk.NET.Windowing;
using Silk.NET.Shaderc;
using VkBuffer = Silk.NET.Vulkan.Buffer;
using VkSemaphore = Silk.NET.Vulkan.Semaphore;

namespace RedHoleEngine.Rendering.Backends;

/// <summary>
/// Vulkan graphics backend - uses MoltenVK on macOS for Metal support
/// </summary>
public unsafe class VulkanBackend : IGraphicsBackend
{
    private readonly IWindow _window;
    private readonly int _width;
    private readonly int _height;

    private Vk? _vk;
    private Instance _instance;
    private PhysicalDevice _physicalDevice;
    private Device _device;
    private Queue _computeQueue;
    private Queue _graphicsQueue;
    private Queue _presentQueue;
    private SurfaceKHR _surface;
    private SwapchainKHR _swapchain;
    private Image[] _swapchainImages = Array.Empty<Image>();
    private ImageView[] _swapchainImageViews = Array.Empty<ImageView>();
    private Format _swapchainFormat;
    private Extent2D _swapchainExtent;

    private KhrSurface? _khrSurface;
    private KhrSwapchain? _khrSwapchain;

    // Compute pipeline
    private DescriptorSetLayout _computeDescriptorSetLayout;
    private PipelineLayout _computePipelineLayout;
    private Pipeline _computePipeline;
    private DescriptorPool _descriptorPool;
    private DescriptorSet _computeDescriptorSet;

    // Output image for compute shader
    private Image _outputImage;
    private DeviceMemory _outputImageMemory;
    private ImageView _outputImageView;

    // Command buffers
    private CommandPool _commandPool;
    private CommandBuffer _computeCommandBuffer;
    private CommandBuffer _graphicsCommandBuffer;

    // Synchronization
    private VkSemaphore _imageAvailableSemaphore;
    private VkSemaphore _renderFinishedSemaphore;
    private VkSemaphore _computeFinishedSemaphore;
    private Fence _inFlightFence;

    // Uniform buffer for shader parameters
    private VkBuffer _uniformBuffer;
    private DeviceMemory _uniformBufferMemory;
    private void* _uniformBufferMapped;

    private uint _computeQueueFamily;
    private uint _graphicsQueueFamily;
    private uint _presentQueueFamily;

    public GraphicsBackendType BackendType => GraphicsBackendType.Vulkan;
    public bool SupportsComputeShaders => true;

    public VulkanBackend(IWindow window, int width, int height)
    {
        _window = window;
        _width = width;
        _height = height;
    }

    public void Initialize()
    {
        _vk = Vk.GetApi();
        
        CreateInstance();
        CreateSurface();
        PickPhysicalDevice();
        CreateLogicalDevice();
        CreateSwapchain();
        CreateImageViews();
        CreateOutputImage();
        CreateDescriptorSetLayout();
        CreateComputePipeline();
        CreateDescriptorPool();
        CreateDescriptorSets();
        CreateUniformBuffer();
        CreateCommandPool();
        CreateCommandBuffers();
        CreateSyncObjects();

        Console.WriteLine("Vulkan initialized successfully!");
        Console.WriteLine($"Using device: {GetDeviceName()}");
    }

    private void CreateInstance()
    {
        var appInfo = new ApplicationInfo
        {
            SType = StructureType.ApplicationInfo,
            PApplicationName = (byte*)Marshal.StringToHGlobalAnsi("RedHole Engine"),
            ApplicationVersion = new Version32(1, 0, 0),
            PEngineName = (byte*)Marshal.StringToHGlobalAnsi("RedHole"),
            EngineVersion = new Version32(1, 0, 0),
            ApiVersion = Vk.Version12
        };

        // Get required extensions from window
        var windowExtensions = _window.VkSurface!.GetRequiredExtensions(out uint extCount);
        
        var createInfo = new InstanceCreateInfo
        {
            SType = StructureType.InstanceCreateInfo,
            PApplicationInfo = &appInfo,
            EnabledExtensionCount = extCount,
            PpEnabledExtensionNames = windowExtensions,
            // MoltenVK on macOS may require portability enumeration
            Flags = InstanceCreateFlags.EnumeratePortabilityBitKhr
        };

        fixed (Instance* instance = &_instance)
        {
            if (_vk!.CreateInstance(&createInfo, null, instance) != Result.Success)
            {
                throw new Exception("Failed to create Vulkan instance");
            }
        }

        if (!_vk.TryGetInstanceExtension(_instance, out _khrSurface))
        {
            throw new Exception("Failed to get KHR_surface extension");
        }
    }

    private void CreateSurface()
    {
        _surface = _window.VkSurface!.Create<AllocationCallbacks>(_instance.ToHandle(), null).ToSurface();
    }

    private void PickPhysicalDevice()
    {
        uint deviceCount = 0;
        _vk!.EnumeratePhysicalDevices(_instance, &deviceCount, null);

        if (deviceCount == 0)
        {
            throw new Exception("No Vulkan-capable GPU found");
        }

        var devices = new PhysicalDevice[deviceCount];
        fixed (PhysicalDevice* devicesPtr = devices)
        {
            _vk.EnumeratePhysicalDevices(_instance, &deviceCount, devicesPtr);
        }

        foreach (var device in devices)
        {
            if (IsDeviceSuitable(device))
            {
                _physicalDevice = device;
                return;
            }
        }

        throw new Exception("No suitable GPU found");
    }

    private bool IsDeviceSuitable(PhysicalDevice device)
    {
        var indices = FindQueueFamilies(device);
        bool extensionsSupported = CheckDeviceExtensionSupport(device);
        
        bool swapchainAdequate = false;
        if (extensionsSupported)
        {
            var swapchainSupport = QuerySwapchainSupport(device);
            swapchainAdequate = swapchainSupport.Formats.Length > 0 && swapchainSupport.PresentModes.Length > 0;
        }

        return indices.IsComplete && extensionsSupported && swapchainAdequate;
    }

    private QueueFamilyIndices FindQueueFamilies(PhysicalDevice device)
    {
        var indices = new QueueFamilyIndices();

        uint queueFamilyCount = 0;
        _vk!.GetPhysicalDeviceQueueFamilyProperties(device, &queueFamilyCount, null);

        var queueFamilies = new QueueFamilyProperties[queueFamilyCount];
        fixed (QueueFamilyProperties* qfPtr = queueFamilies)
        {
            _vk.GetPhysicalDeviceQueueFamilyProperties(device, &queueFamilyCount, qfPtr);
        }

        for (uint i = 0; i < queueFamilies.Length; i++)
        {
            if (queueFamilies[i].QueueFlags.HasFlag(QueueFlags.GraphicsBit))
            {
                indices.GraphicsFamily = i;
            }

            if (queueFamilies[i].QueueFlags.HasFlag(QueueFlags.ComputeBit))
            {
                indices.ComputeFamily = i;
            }

            Bool32 presentSupport = false;
            _khrSurface!.GetPhysicalDeviceSurfaceSupport(device, i, _surface, &presentSupport);
            if (presentSupport)
            {
                indices.PresentFamily = i;
            }

            if (indices.IsComplete) break;
        }

        return indices;
    }

    private bool CheckDeviceExtensionSupport(PhysicalDevice device)
    {
        uint extCount = 0;
        _vk!.EnumerateDeviceExtensionProperties(device, (byte*)null, &extCount, null);

        var availableExtensions = new ExtensionProperties[extCount];
        fixed (ExtensionProperties* extPtr = availableExtensions)
        {
            _vk.EnumerateDeviceExtensionProperties(device, (byte*)null, &extCount, extPtr);
        }

        var requiredExtensions = new HashSet<string> { KhrSwapchain.ExtensionName };

        foreach (var ext in availableExtensions)
        {
            requiredExtensions.Remove(Marshal.PtrToStringAnsi((IntPtr)ext.ExtensionName)!);
        }

        return requiredExtensions.Count == 0;
    }

    private SwapchainSupportDetails QuerySwapchainSupport(PhysicalDevice device)
    {
        var details = new SwapchainSupportDetails();

        _khrSurface!.GetPhysicalDeviceSurfaceCapabilities(device, _surface, out details.Capabilities);

        uint formatCount = 0;
        _khrSurface.GetPhysicalDeviceSurfaceFormats(device, _surface, &formatCount, null);

        if (formatCount > 0)
        {
            details.Formats = new SurfaceFormatKHR[formatCount];
            fixed (SurfaceFormatKHR* formatsPtr = details.Formats)
            {
                _khrSurface.GetPhysicalDeviceSurfaceFormats(device, _surface, &formatCount, formatsPtr);
            }
        }

        uint presentModeCount = 0;
        _khrSurface.GetPhysicalDeviceSurfacePresentModes(device, _surface, &presentModeCount, null);

        if (presentModeCount > 0)
        {
            details.PresentModes = new PresentModeKHR[presentModeCount];
            fixed (PresentModeKHR* modesPtr = details.PresentModes)
            {
                _khrSurface.GetPhysicalDeviceSurfacePresentModes(device, _surface, &presentModeCount, modesPtr);
            }
        }

        return details;
    }

    private void CreateLogicalDevice()
    {
        var indices = FindQueueFamilies(_physicalDevice);
        _computeQueueFamily = indices.ComputeFamily!.Value;
        _graphicsQueueFamily = indices.GraphicsFamily!.Value;
        _presentQueueFamily = indices.PresentFamily!.Value;

        var uniqueQueueFamilies = new HashSet<uint> { _computeQueueFamily, _graphicsQueueFamily, _presentQueueFamily };
        var queueCreateInfos = new DeviceQueueCreateInfo[uniqueQueueFamilies.Count];

        float queuePriority = 1.0f;
        int i = 0;
        foreach (var queueFamily in uniqueQueueFamilies)
        {
            queueCreateInfos[i++] = new DeviceQueueCreateInfo
            {
                SType = StructureType.DeviceQueueCreateInfo,
                QueueFamilyIndex = queueFamily,
                QueueCount = 1,
                PQueuePriorities = &queuePriority
            };
        }

        var deviceFeatures = new PhysicalDeviceFeatures();

        var extensionName = (byte*)Marshal.StringToHGlobalAnsi(KhrSwapchain.ExtensionName);

        fixed (DeviceQueueCreateInfo* queueCreateInfosPtr = queueCreateInfos)
        {
            var createInfo = new DeviceCreateInfo
            {
                SType = StructureType.DeviceCreateInfo,
                QueueCreateInfoCount = (uint)queueCreateInfos.Length,
                PQueueCreateInfos = queueCreateInfosPtr,
                PEnabledFeatures = &deviceFeatures,
                EnabledExtensionCount = 1,
                PpEnabledExtensionNames = &extensionName
            };

            fixed (Device* device = &_device)
            {
                if (_vk!.CreateDevice(_physicalDevice, &createInfo, null, device) != Result.Success)
                {
                    throw new Exception("Failed to create logical device");
                }
            }
        }

        _vk!.GetDeviceQueue(_device, _computeQueueFamily, 0, out _computeQueue);
        _vk.GetDeviceQueue(_device, _graphicsQueueFamily, 0, out _graphicsQueue);
        _vk.GetDeviceQueue(_device, _presentQueueFamily, 0, out _presentQueue);

        if (!_vk.TryGetDeviceExtension(_instance, _device, out _khrSwapchain))
        {
            throw new Exception("Failed to get swapchain extension");
        }
    }

    private void CreateSwapchain()
    {
        var swapchainSupport = QuerySwapchainSupport(_physicalDevice);

        var surfaceFormat = ChooseSwapSurfaceFormat(swapchainSupport.Formats);
        var presentMode = ChooseSwapPresentMode(swapchainSupport.PresentModes);
        var extent = ChooseSwapExtent(swapchainSupport.Capabilities);

        uint imageCount = swapchainSupport.Capabilities.MinImageCount + 1;
        if (swapchainSupport.Capabilities.MaxImageCount > 0 && imageCount > swapchainSupport.Capabilities.MaxImageCount)
        {
            imageCount = swapchainSupport.Capabilities.MaxImageCount;
        }

        var createInfo = new SwapchainCreateInfoKHR
        {
            SType = StructureType.SwapchainCreateInfoKhr,
            Surface = _surface,
            MinImageCount = imageCount,
            ImageFormat = surfaceFormat.Format,
            ImageColorSpace = surfaceFormat.ColorSpace,
            ImageExtent = extent,
            ImageArrayLayers = 1,
            ImageUsage = ImageUsageFlags.ColorAttachmentBit | ImageUsageFlags.TransferDstBit,
            PreTransform = swapchainSupport.Capabilities.CurrentTransform,
            CompositeAlpha = CompositeAlphaFlagsKHR.OpaqueBitKhr,
            PresentMode = presentMode,
            Clipped = true,
            OldSwapchain = default
        };

        var indices = FindQueueFamilies(_physicalDevice);
        uint* queueFamilyIndices = stackalloc uint[] { indices.GraphicsFamily!.Value, indices.PresentFamily!.Value };

        if (indices.GraphicsFamily != indices.PresentFamily)
        {
            createInfo.ImageSharingMode = SharingMode.Concurrent;
            createInfo.QueueFamilyIndexCount = 2;
            createInfo.PQueueFamilyIndices = queueFamilyIndices;
        }
        else
        {
            createInfo.ImageSharingMode = SharingMode.Exclusive;
        }

        fixed (SwapchainKHR* swapchain = &_swapchain)
        {
            if (_khrSwapchain!.CreateSwapchain(_device, &createInfo, null, swapchain) != Result.Success)
            {
                throw new Exception("Failed to create swapchain");
            }
        }

        _khrSwapchain.GetSwapchainImages(_device, _swapchain, &imageCount, null);
        _swapchainImages = new Image[imageCount];
        fixed (Image* imagesPtr = _swapchainImages)
        {
            _khrSwapchain.GetSwapchainImages(_device, _swapchain, &imageCount, imagesPtr);
        }

        _swapchainFormat = surfaceFormat.Format;
        _swapchainExtent = extent;
    }

    private SurfaceFormatKHR ChooseSwapSurfaceFormat(SurfaceFormatKHR[] formats)
    {
        foreach (var format in formats)
        {
            if (format.Format == Format.B8G8R8A8Srgb && format.ColorSpace == ColorSpaceKHR.SpaceSrgbNonlinearKhr)
            {
                return format;
            }
        }
        return formats[0];
    }

    private PresentModeKHR ChooseSwapPresentMode(PresentModeKHR[] presentModes)
    {
        foreach (var mode in presentModes)
        {
            if (mode == PresentModeKHR.MailboxKhr)
            {
                return mode;
            }
        }
        return PresentModeKHR.FifoKhr;
    }

    private Extent2D ChooseSwapExtent(SurfaceCapabilitiesKHR capabilities)
    {
        if (capabilities.CurrentExtent.Width != uint.MaxValue)
        {
            return capabilities.CurrentExtent;
        }

        return new Extent2D
        {
            Width = Math.Clamp((uint)_width, capabilities.MinImageExtent.Width, capabilities.MaxImageExtent.Width),
            Height = Math.Clamp((uint)_height, capabilities.MinImageExtent.Height, capabilities.MaxImageExtent.Height)
        };
    }

    private void CreateImageViews()
    {
        _swapchainImageViews = new ImageView[_swapchainImages.Length];

        for (int i = 0; i < _swapchainImages.Length; i++)
        {
            _swapchainImageViews[i] = CreateImageView(_swapchainImages[i], _swapchainFormat);
        }
    }

    private ImageView CreateImageView(Image image, Format format)
    {
        var createInfo = new ImageViewCreateInfo
        {
            SType = StructureType.ImageViewCreateInfo,
            Image = image,
            ViewType = ImageViewType.Type2D,
            Format = format,
            Components = new ComponentMapping
            {
                R = ComponentSwizzle.Identity,
                G = ComponentSwizzle.Identity,
                B = ComponentSwizzle.Identity,
                A = ComponentSwizzle.Identity
            },
            SubresourceRange = new ImageSubresourceRange
            {
                AspectMask = ImageAspectFlags.ColorBit,
                BaseMipLevel = 0,
                LevelCount = 1,
                BaseArrayLayer = 0,
                LayerCount = 1
            }
        };

        ImageView imageView;
        if (_vk!.CreateImageView(_device, &createInfo, null, &imageView) != Result.Success)
        {
            throw new Exception("Failed to create image view");
        }

        return imageView;
    }

    private void CreateOutputImage()
    {
        var imageInfo = new ImageCreateInfo
        {
            SType = StructureType.ImageCreateInfo,
            ImageType = ImageType.Type2D,
            Format = Format.R32G32B32A32Sfloat,
            Extent = new Extent3D((uint)_width, (uint)_height, 1),
            MipLevels = 1,
            ArrayLayers = 1,
            Samples = SampleCountFlags.Count1Bit,
            Tiling = ImageTiling.Optimal,
            Usage = ImageUsageFlags.StorageBit | ImageUsageFlags.TransferSrcBit,
            SharingMode = SharingMode.Exclusive,
            InitialLayout = ImageLayout.Undefined
        };

        fixed (Image* image = &_outputImage)
        {
            if (_vk!.CreateImage(_device, &imageInfo, null, image) != Result.Success)
            {
                throw new Exception("Failed to create output image");
            }
        }

        _vk!.GetImageMemoryRequirements(_device, _outputImage, out var memRequirements);

        var allocInfo = new MemoryAllocateInfo
        {
            SType = StructureType.MemoryAllocateInfo,
            AllocationSize = memRequirements.Size,
            MemoryTypeIndex = FindMemoryType(memRequirements.MemoryTypeBits, MemoryPropertyFlags.DeviceLocalBit)
        };

        fixed (DeviceMemory* memory = &_outputImageMemory)
        {
            if (_vk.AllocateMemory(_device, &allocInfo, null, memory) != Result.Success)
            {
                throw new Exception("Failed to allocate image memory");
            }
        }

        _vk.BindImageMemory(_device, _outputImage, _outputImageMemory, 0);
        _outputImageView = CreateImageView(_outputImage, Format.R32G32B32A32Sfloat);
    }

    private uint FindMemoryType(uint typeFilter, MemoryPropertyFlags properties)
    {
        _vk!.GetPhysicalDeviceMemoryProperties(_physicalDevice, out var memProperties);

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

    private void CreateDescriptorSetLayout()
    {
        var bindings = new DescriptorSetLayoutBinding[]
        {
            new() // Output image
            {
                Binding = 0,
                DescriptorType = DescriptorType.StorageImage,
                DescriptorCount = 1,
                StageFlags = ShaderStageFlags.ComputeBit
            },
            new() // Uniform buffer
            {
                Binding = 1,
                DescriptorType = DescriptorType.UniformBuffer,
                DescriptorCount = 1,
                StageFlags = ShaderStageFlags.ComputeBit
            }
        };

        fixed (DescriptorSetLayoutBinding* bindingsPtr = bindings)
        {
            var layoutInfo = new DescriptorSetLayoutCreateInfo
            {
                SType = StructureType.DescriptorSetLayoutCreateInfo,
                BindingCount = (uint)bindings.Length,
                PBindings = bindingsPtr
            };

            fixed (DescriptorSetLayout* layout = &_computeDescriptorSetLayout)
            {
                if (_vk!.CreateDescriptorSetLayout(_device, &layoutInfo, null, layout) != Result.Success)
                {
                    throw new Exception("Failed to create descriptor set layout");
                }
            }
        }
    }

    private void CreateComputePipeline()
    {
        // Load and compile shader
        byte[] shaderCode = CompileShader();

        var shaderModule = CreateShaderModule(shaderCode);

        var shaderStageInfo = new PipelineShaderStageCreateInfo
        {
            SType = StructureType.PipelineShaderStageCreateInfo,
            Stage = ShaderStageFlags.ComputeBit,
            Module = shaderModule,
            PName = (byte*)Marshal.StringToHGlobalAnsi("main")
        };

        fixed (DescriptorSetLayout* layout = &_computeDescriptorSetLayout)
        {
            var pipelineLayoutInfo = new PipelineLayoutCreateInfo
            {
                SType = StructureType.PipelineLayoutCreateInfo,
                SetLayoutCount = 1,
                PSetLayouts = layout
            };

            fixed (PipelineLayout* pipelineLayout = &_computePipelineLayout)
            {
                if (_vk!.CreatePipelineLayout(_device, &pipelineLayoutInfo, null, pipelineLayout) != Result.Success)
                {
                    throw new Exception("Failed to create compute pipeline layout");
                }
            }
        }

        var computePipelineInfo = new ComputePipelineCreateInfo
        {
            SType = StructureType.ComputePipelineCreateInfo,
            Stage = shaderStageInfo,
            Layout = _computePipelineLayout
        };

        fixed (Pipeline* pipeline = &_computePipeline)
        {
            if (_vk!.CreateComputePipelines(_device, default, 1, &computePipelineInfo, null, pipeline) != Result.Success)
            {
                throw new Exception("Failed to create compute pipeline");
            }
        }

        _vk.DestroyShaderModule(_device, shaderModule, null);
    }

    private byte[] CompileShader()
    {
        // Load pre-compiled SPIR-V shader
        string shaderPath = Path.Combine(AppContext.BaseDirectory, "Rendering", "Shaders", "raytracer_vulkan.spv");
        
        if (!File.Exists(shaderPath))
        {
            throw new FileNotFoundException($"Pre-compiled shader not found: {shaderPath}. Run: glslangValidator --target-env vulkan1.2 -S comp -o raytracer_vulkan.spv raytracer_vulkan.comp");
        }
        
        byte[] spirv = File.ReadAllBytes(shaderPath);
        Console.WriteLine($"Loaded pre-compiled SPIR-V shader from {shaderPath} ({spirv.Length} bytes)");
        
        return spirv;
    }

    private string ConvertToVulkanGlsl(string source)
    {
        // Replace OpenGL 430 with Vulkan-compatible version
        source = source.Replace("#version 430 core", "#version 450");
        
        // Change image layout binding
        source = source.Replace(
            "layout(rgba32f, binding = 0) uniform image2D outputImage;",
            "layout(set = 0, binding = 0, rgba32f) uniform image2D outputImage;");
        
        // Wrap uniforms in a uniform buffer block
        var uniformBlock = @"
layout(set = 0, binding = 1) uniform UniformBlock {
    vec2 u_Resolution;
    float u_Time;
    float _pad1;
    vec3 u_CameraPos;
    float _pad2;
    vec3 u_CameraForward;
    float _pad3;
    vec3 u_CameraRight;
    float _pad4;
    vec3 u_CameraUp;
    float u_Fov;
    vec3 u_BlackHolePos;
    float u_BlackHoleMass;
    float u_SchwarzschildRadius;
    float u_DiskInnerRadius;
    float u_DiskOuterRadius;
    float _pad5;
};
";
        // Remove individual uniform declarations (handles vec2, vec3, float, etc.)
        source = System.Text.RegularExpressions.Regex.Replace(source, @"uniform\s+\w+\s+u_\w+;\s*\n?", "");
        
        // Remove comment lines that are now orphaned (Camera uniforms, Black hole uniforms)
        source = System.Text.RegularExpressions.Regex.Replace(source, @"// Camera uniforms\s*\n", "");
        source = System.Text.RegularExpressions.Regex.Replace(source, @"// Black hole uniforms\s*\n", "");
        
        // Insert uniform block after version
        var versionEnd = source.IndexOf('\n') + 1;
        source = source.Insert(versionEnd, uniformBlock);
        
        return source;
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
            if (_vk!.CreateShaderModule(_device, &createInfo, null, &shaderModule) != Result.Success)
            {
                throw new Exception("Failed to create shader module");
            }

            return shaderModule;
        }
    }

    private void CreateDescriptorPool()
    {
        var poolSizes = new DescriptorPoolSize[]
        {
            new() { Type = DescriptorType.StorageImage, DescriptorCount = 1 },
            new() { Type = DescriptorType.UniformBuffer, DescriptorCount = 1 }
        };

        fixed (DescriptorPoolSize* poolSizesPtr = poolSizes)
        {
            var poolInfo = new DescriptorPoolCreateInfo
            {
                SType = StructureType.DescriptorPoolCreateInfo,
                PoolSizeCount = (uint)poolSizes.Length,
                PPoolSizes = poolSizesPtr,
                MaxSets = 1
            };

            fixed (DescriptorPool* pool = &_descriptorPool)
            {
                if (_vk!.CreateDescriptorPool(_device, &poolInfo, null, pool) != Result.Success)
                {
                    throw new Exception("Failed to create descriptor pool");
                }
            }
        }
    }

    private void CreateDescriptorSets()
    {
        fixed (DescriptorSetLayout* layout = &_computeDescriptorSetLayout)
        {
            var allocInfo = new DescriptorSetAllocateInfo
            {
                SType = StructureType.DescriptorSetAllocateInfo,
                DescriptorPool = _descriptorPool,
                DescriptorSetCount = 1,
                PSetLayouts = layout
            };

            fixed (DescriptorSet* set = &_computeDescriptorSet)
            {
                if (_vk!.AllocateDescriptorSets(_device, &allocInfo, set) != Result.Success)
                {
                    throw new Exception("Failed to allocate descriptor sets");
                }
            }
        }
    }

    private void CreateUniformBuffer()
    {
        ulong bufferSize = (ulong)Unsafe.SizeOf<RaytracerUniforms>();

        var bufferInfo = new BufferCreateInfo
        {
            SType = StructureType.BufferCreateInfo,
            Size = bufferSize,
            Usage = BufferUsageFlags.UniformBufferBit,
            SharingMode = SharingMode.Exclusive
        };

        fixed (VkBuffer* buffer = &_uniformBuffer)
        {
            if (_vk!.CreateBuffer(_device, &bufferInfo, null, buffer) != Result.Success)
            {
                throw new Exception("Failed to create uniform buffer");
            }
        }

        _vk!.GetBufferMemoryRequirements(_device, _uniformBuffer, out var memRequirements);

        var allocInfo = new MemoryAllocateInfo
        {
            SType = StructureType.MemoryAllocateInfo,
            AllocationSize = memRequirements.Size,
            MemoryTypeIndex = FindMemoryType(memRequirements.MemoryTypeBits,
                MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit)
        };

        fixed (DeviceMemory* memory = &_uniformBufferMemory)
        {
            if (_vk.AllocateMemory(_device, &allocInfo, null, memory) != Result.Success)
            {
                throw new Exception("Failed to allocate uniform buffer memory");
            }
        }

        _vk.BindBufferMemory(_device, _uniformBuffer, _uniformBufferMemory, 0);

        fixed (void** mapped = &_uniformBufferMapped)
        {
            _vk.MapMemory(_device, _uniformBufferMemory, 0, bufferSize, 0, mapped);
        }

        // Update descriptor set with buffer and image
        var imageInfo = new DescriptorImageInfo
        {
            ImageView = _outputImageView,
            ImageLayout = ImageLayout.General
        };

        var bufferDescInfo = new DescriptorBufferInfo
        {
            Buffer = _uniformBuffer,
            Offset = 0,
            Range = bufferSize
        };

        var writes = new WriteDescriptorSet[]
        {
            new()
            {
                SType = StructureType.WriteDescriptorSet,
                DstSet = _computeDescriptorSet,
                DstBinding = 0,
                DstArrayElement = 0,
                DescriptorType = DescriptorType.StorageImage,
                DescriptorCount = 1,
                PImageInfo = &imageInfo
            },
            new()
            {
                SType = StructureType.WriteDescriptorSet,
                DstSet = _computeDescriptorSet,
                DstBinding = 1,
                DstArrayElement = 0,
                DescriptorType = DescriptorType.UniformBuffer,
                DescriptorCount = 1,
                PBufferInfo = &bufferDescInfo
            }
        };

        fixed (WriteDescriptorSet* writesPtr = writes)
        {
            _vk.UpdateDescriptorSets(_device, (uint)writes.Length, writesPtr, 0, null);
        }
    }

    private void CreateCommandPool()
    {
        var poolInfo = new CommandPoolCreateInfo
        {
            SType = StructureType.CommandPoolCreateInfo,
            QueueFamilyIndex = _computeQueueFamily,
            Flags = CommandPoolCreateFlags.ResetCommandBufferBit
        };

        fixed (CommandPool* pool = &_commandPool)
        {
            if (_vk!.CreateCommandPool(_device, &poolInfo, null, pool) != Result.Success)
            {
                throw new Exception("Failed to create command pool");
            }
        }
    }

    private void CreateCommandBuffers()
    {
        var allocInfo = new CommandBufferAllocateInfo
        {
            SType = StructureType.CommandBufferAllocateInfo,
            CommandPool = _commandPool,
            Level = CommandBufferLevel.Primary,
            CommandBufferCount = 2
        };

        var buffers = new CommandBuffer[2];
        fixed (CommandBuffer* buffersPtr = buffers)
        {
            if (_vk!.AllocateCommandBuffers(_device, &allocInfo, buffersPtr) != Result.Success)
            {
                throw new Exception("Failed to allocate command buffers");
            }
        }

        _computeCommandBuffer = buffers[0];
        _graphicsCommandBuffer = buffers[1];
    }

    private void CreateSyncObjects()
    {
        var semaphoreInfo = new SemaphoreCreateInfo { SType = StructureType.SemaphoreCreateInfo };
        var fenceInfo = new FenceCreateInfo 
        { 
            SType = StructureType.FenceCreateInfo,
            Flags = FenceCreateFlags.SignaledBit
        };

        fixed (VkSemaphore* imageAvailable = &_imageAvailableSemaphore)
        fixed (VkSemaphore* renderFinished = &_renderFinishedSemaphore)
        fixed (VkSemaphore* computeFinished = &_computeFinishedSemaphore)
        fixed (Fence* fence = &_inFlightFence)
        {
            if (_vk!.CreateSemaphore(_device, &semaphoreInfo, null, imageAvailable) != Result.Success ||
                _vk.CreateSemaphore(_device, &semaphoreInfo, null, renderFinished) != Result.Success ||
                _vk.CreateSemaphore(_device, &semaphoreInfo, null, computeFinished) != Result.Success ||
                _vk.CreateFence(_device, &fenceInfo, null, fence) != Result.Success)
            {
                throw new Exception("Failed to create sync objects");
            }
        }
    }

    public void Render(Camera camera, BlackHole blackHole, float time)
    {
        fixed (Fence* fence = &_inFlightFence)
        {
            _vk!.WaitForFences(_device, 1, fence, true, ulong.MaxValue);
            _vk.ResetFences(_device, 1, fence);
        }

        // Update uniforms
        var uniforms = new RaytracerUniforms
        {
            Resolution = new Vector2(_width, _height),
            Time = time,
            CameraPos = camera.Position,
            CameraForward = camera.Forward,
            CameraRight = camera.Right,
            CameraUp = camera.Up,
            Fov = camera.FieldOfView,
            BlackHolePos = blackHole.Position,
            BlackHoleMass = blackHole.Mass,
            SchwarzschildRadius = blackHole.SchwarzschildRadius,
            DiskInnerRadius = blackHole.DiskInnerRadius,
            DiskOuterRadius = blackHole.DiskOuterRadius
        };
        Unsafe.Copy(_uniformBufferMapped, ref uniforms);

        // Acquire swapchain image
        uint imageIndex = 0;
        _khrSwapchain!.AcquireNextImage(_device, _swapchain, ulong.MaxValue, _imageAvailableSemaphore, default, &imageIndex);

        // Record and submit compute commands
        RecordComputeCommands();
        SubmitCompute();

        // Record and submit graphics commands (copy compute output to swapchain)
        RecordGraphicsCommands(imageIndex);
        SubmitGraphics();

        // Present
        var swapchain = _swapchain;
        var waitSemaphore = _renderFinishedSemaphore;
        var presentInfo = new PresentInfoKHR
        {
            SType = StructureType.PresentInfoKhr,
            WaitSemaphoreCount = 1,
            PWaitSemaphores = &waitSemaphore,
            SwapchainCount = 1,
            PSwapchains = &swapchain,
            PImageIndices = &imageIndex
        };

        _khrSwapchain.QueuePresent(_presentQueue, &presentInfo);
    }

    private void RecordComputeCommands()
    {
        _vk!.ResetCommandBuffer(_computeCommandBuffer, 0);

        var beginInfo = new CommandBufferBeginInfo { SType = StructureType.CommandBufferBeginInfo };
        _vk.BeginCommandBuffer(_computeCommandBuffer, &beginInfo);

        // Transition output image to general layout for compute shader
        TransitionImageLayout(_computeCommandBuffer, _outputImage, ImageLayout.Undefined, ImageLayout.General);

        _vk.CmdBindPipeline(_computeCommandBuffer, PipelineBindPoint.Compute, _computePipeline);

        fixed (DescriptorSet* set = &_computeDescriptorSet)
        {
            _vk.CmdBindDescriptorSets(_computeCommandBuffer, PipelineBindPoint.Compute, _computePipelineLayout, 0, 1, set, 0, null);
        }

        uint groupsX = (uint)Math.Ceiling(_width / 16.0);
        uint groupsY = (uint)Math.Ceiling(_height / 16.0);
        _vk.CmdDispatch(_computeCommandBuffer, groupsX, groupsY, 1);

        // Transition output image for transfer
        TransitionImageLayout(_computeCommandBuffer, _outputImage, ImageLayout.General, ImageLayout.TransferSrcOptimal);

        _vk.EndCommandBuffer(_computeCommandBuffer);
    }

    private void RecordGraphicsCommands(uint imageIndex)
    {
        _vk!.ResetCommandBuffer(_graphicsCommandBuffer, 0);

        var beginInfo = new CommandBufferBeginInfo { SType = StructureType.CommandBufferBeginInfo };
        _vk.BeginCommandBuffer(_graphicsCommandBuffer, &beginInfo);

        // Transition swapchain image to transfer dst
        TransitionImageLayout(_graphicsCommandBuffer, _swapchainImages[imageIndex], ImageLayout.Undefined, ImageLayout.TransferDstOptimal);

        // Blit from compute output to swapchain image
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
        blitRegion.SrcOffsets[1] = new Offset3D(_width, _height, 1);
        blitRegion.DstOffsets[0] = new Offset3D(0, 0, 0);
        blitRegion.DstOffsets[1] = new Offset3D((int)_swapchainExtent.Width, (int)_swapchainExtent.Height, 1);

        _vk.CmdBlitImage(_graphicsCommandBuffer,
            _outputImage, ImageLayout.TransferSrcOptimal,
            _swapchainImages[imageIndex], ImageLayout.TransferDstOptimal,
            1, &blitRegion, Filter.Linear);

        // Transition swapchain image to present
        TransitionImageLayout(_graphicsCommandBuffer, _swapchainImages[imageIndex], ImageLayout.TransferDstOptimal, ImageLayout.PresentSrcKhr);

        _vk.EndCommandBuffer(_graphicsCommandBuffer);
    }

    private void TransitionImageLayout(CommandBuffer cmd, Image image, ImageLayout oldLayout, ImageLayout newLayout)
    {
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

        PipelineStageFlags srcStage, dstStage;

        if (oldLayout == ImageLayout.Undefined && newLayout == ImageLayout.General)
        {
            barrier.SrcAccessMask = 0;
            barrier.DstAccessMask = AccessFlags.ShaderWriteBit;
            srcStage = PipelineStageFlags.TopOfPipeBit;
            dstStage = PipelineStageFlags.ComputeShaderBit;
        }
        else if (oldLayout == ImageLayout.General && newLayout == ImageLayout.TransferSrcOptimal)
        {
            barrier.SrcAccessMask = AccessFlags.ShaderWriteBit;
            barrier.DstAccessMask = AccessFlags.TransferReadBit;
            srcStage = PipelineStageFlags.ComputeShaderBit;
            dstStage = PipelineStageFlags.TransferBit;
        }
        else if (oldLayout == ImageLayout.Undefined && newLayout == ImageLayout.TransferDstOptimal)
        {
            barrier.SrcAccessMask = 0;
            barrier.DstAccessMask = AccessFlags.TransferWriteBit;
            srcStage = PipelineStageFlags.TopOfPipeBit;
            dstStage = PipelineStageFlags.TransferBit;
        }
        else if (oldLayout == ImageLayout.TransferDstOptimal && newLayout == ImageLayout.PresentSrcKhr)
        {
            barrier.SrcAccessMask = AccessFlags.TransferWriteBit;
            barrier.DstAccessMask = 0;
            srcStage = PipelineStageFlags.TransferBit;
            dstStage = PipelineStageFlags.BottomOfPipeBit;
        }
        else
        {
            throw new Exception($"Unsupported layout transition: {oldLayout} -> {newLayout}");
        }

        _vk!.CmdPipelineBarrier(cmd, srcStage, dstStage, 0, 0, null, 0, null, 1, &barrier);
    }

    private void SubmitCompute()
    {
        var waitSemaphore = _imageAvailableSemaphore;
        var signalSemaphore = _computeFinishedSemaphore;
        var waitStage = PipelineStageFlags.ComputeShaderBit;

        fixed (CommandBuffer* cmd = &_computeCommandBuffer)
        {
            var submitInfo = new SubmitInfo
            {
                SType = StructureType.SubmitInfo,
                WaitSemaphoreCount = 1,
                PWaitSemaphores = &waitSemaphore,
                PWaitDstStageMask = &waitStage,
                CommandBufferCount = 1,
                PCommandBuffers = cmd,
                SignalSemaphoreCount = 1,
                PSignalSemaphores = &signalSemaphore
            };

            _vk!.QueueSubmit(_computeQueue, 1, &submitInfo, default);
        }
    }

    private void SubmitGraphics()
    {
        var waitSemaphore = _computeFinishedSemaphore;
        var signalSemaphore = _renderFinishedSemaphore;
        var waitStage = PipelineStageFlags.TransferBit;

        fixed (CommandBuffer* cmd = &_graphicsCommandBuffer)
        {
            var submitInfo = new SubmitInfo
            {
                SType = StructureType.SubmitInfo,
                WaitSemaphoreCount = 1,
                PWaitSemaphores = &waitSemaphore,
                PWaitDstStageMask = &waitStage,
                CommandBufferCount = 1,
                PCommandBuffers = cmd,
                SignalSemaphoreCount = 1,
                PSignalSemaphores = &signalSemaphore
            };

            _vk!.QueueSubmit(_graphicsQueue, 1, &submitInfo, _inFlightFence);
        }
    }

    private string GetDeviceName()
    {
        _vk!.GetPhysicalDeviceProperties(_physicalDevice, out var properties);
        return Marshal.PtrToStringAnsi((IntPtr)properties.DeviceName) ?? "Unknown";
    }

    public void Resize(int width, int height)
    {
        // TODO: Recreate swapchain and output image
    }

    public void Dispose()
    {
        _vk!.DeviceWaitIdle(_device);

        _vk.DestroySemaphore(_device, _imageAvailableSemaphore, null);
        _vk.DestroySemaphore(_device, _renderFinishedSemaphore, null);
        _vk.DestroySemaphore(_device, _computeFinishedSemaphore, null);
        _vk.DestroyFence(_device, _inFlightFence, null);
        _vk.DestroyCommandPool(_device, _commandPool, null);
        _vk.UnmapMemory(_device, _uniformBufferMemory);
        _vk.DestroyBuffer(_device, _uniformBuffer, null);
        _vk.FreeMemory(_device, _uniformBufferMemory, null);
        _vk.DestroyDescriptorPool(_device, _descriptorPool, null);
        _vk.DestroyPipeline(_device, _computePipeline, null);
        _vk.DestroyPipelineLayout(_device, _computePipelineLayout, null);
        _vk.DestroyDescriptorSetLayout(_device, _computeDescriptorSetLayout, null);
        _vk.DestroyImageView(_device, _outputImageView, null);
        _vk.DestroyImage(_device, _outputImage, null);
        _vk.FreeMemory(_device, _outputImageMemory, null);

        foreach (var imageView in _swapchainImageViews)
        {
            _vk.DestroyImageView(_device, imageView, null);
        }

        _khrSwapchain?.DestroySwapchain(_device, _swapchain, null);
        _vk.DestroyDevice(_device, null);
        _khrSurface?.DestroySurface(_instance, _surface, null);
        _vk.DestroyInstance(_instance, null);
    }

    // Helper structs
    private struct QueueFamilyIndices
    {
        public uint? GraphicsFamily;
        public uint? ComputeFamily;
        public uint? PresentFamily;
        public bool IsComplete => GraphicsFamily.HasValue && ComputeFamily.HasValue && PresentFamily.HasValue;
    }

    private struct SwapchainSupportDetails
    {
        public SurfaceCapabilitiesKHR Capabilities;
        public SurfaceFormatKHR[] Formats;
        public PresentModeKHR[] PresentModes;
    }
}

[StructLayout(LayoutKind.Explicit, Size = 128)]
public struct RaytracerUniforms
{
    [FieldOffset(0)] public Vector2 Resolution;
    [FieldOffset(8)] public float Time;
    // padding at 12
    [FieldOffset(16)] public Vector3 CameraPos;
    // padding at 28
    [FieldOffset(32)] public Vector3 CameraForward;
    // padding at 44
    [FieldOffset(48)] public Vector3 CameraRight;
    // padding at 60
    [FieldOffset(64)] public Vector3 CameraUp;
    [FieldOffset(76)] public float Fov;
    [FieldOffset(80)] public Vector3 BlackHolePos;
    [FieldOffset(92)] public float BlackHoleMass;
    [FieldOffset(96)] public float SchwarzschildRadius;
    [FieldOffset(100)] public float DiskInnerRadius;
    [FieldOffset(104)] public float DiskOuterRadius;
    // padding to 128
}
