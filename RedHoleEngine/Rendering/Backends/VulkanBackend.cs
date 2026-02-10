using System.Diagnostics;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Text;
using Silk.NET.Core.Native;
using System.Runtime.InteropServices;
using RedHoleEngine.Engine;
using RedHoleEngine.Particles;
using RedHoleEngine.Physics;
using RedHoleEngine.Profiling;
using RedHoleEngine.Rendering.Debug;
using RedHoleEngine.Rendering.UI;
using RedHoleEngine.Rendering.Particles;
using RedHoleEngine.Rendering;
using RedHoleEngine.Rendering.Raytracing;
using RedHoleEngine.Rendering.Rasterization;
using RedHoleEngine.Rendering.PBR;
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
    public RaytracerSettings RaytracerSettings { get; } = new();
    public RenderSettings RenderSettings { get; } = new();

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
    private ImageLayout _outputImageLayout = ImageLayout.Undefined;

    // Command buffers
    private CommandPool _computeCommandPool;
    private CommandPool _graphicsCommandPool;
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

    // Raytracer mesh buffers
    private VkBuffer _bvhNodeBuffer;
    private DeviceMemory _bvhNodeBufferMemory;
    private ulong _bvhNodeBufferSize;

    private VkBuffer _triangleBuffer;
    private DeviceMemory _triangleBufferMemory;
    private ulong _triangleBufferSize;

    // PBR Material buffer
    private VkBuffer _materialBuffer;
    private DeviceMemory _materialBufferMemory;
    private ulong _materialBufferSize;
    private int _materialCount;

    private Image _accumImage;
    private DeviceMemory _accumImageMemory;
    private ImageView _accumImageView;

    private VkBuffer _readbackBuffer;
    private DeviceMemory _readbackBufferMemory;
    private ulong _readbackBufferSize;
    private bool _loggedReadbackStats;

    private int _bvhNodeCount;
    private int _triangleCount;
    private uint _frameIndex;
    private bool _accumInitialized;
    private Vector3 _lastCameraPos;
    private Vector3 _lastCameraForward;
    private float _lastCameraFov;
    private int _lastSettingsHash;

    // Rasterization
    private RenderPass _rasterRenderPass;
    private RenderPass _overlayRenderPass;
    private Framebuffer[] _rasterFramebuffers = Array.Empty<Framebuffer>();
    private PipelineLayout _rasterPipelineLayout;
    private Pipeline _rasterPipeline;
    private VkBuffer _rasterVertexBuffer;
    private DeviceMemory _rasterVertexBufferMemory;
    private ulong _rasterVertexBufferSize;
    private VkBuffer _rasterIndexBuffer;
    private DeviceMemory _rasterIndexBufferMemory;
    private ulong _rasterIndexBufferSize;
    private int _rasterVertexCount;
    private int _rasterIndexCount;
    
    // Depth buffer
    private Image _depthImage;
    private DeviceMemory _depthImageMemory;
    private ImageView _depthImageView;
    private const Format DepthFormat = Format.D32Sfloat;

    private uint _computeQueueFamily;
    private uint _graphicsQueueFamily;
    private uint _presentQueueFamily;
    
    // Debug renderer
    private DebugRenderer? _debugRenderer;
    
    // Particle renderer
    private ParticleRenderer? _particleRenderer;

    // UI renderer
    private UiRenderer? _uiRenderer;
    private UiDrawData _uiDrawData = new();

    public GraphicsBackendType BackendType => GraphicsBackendType.Vulkan;
    public bool SupportsComputeShaders => true;
    public bool SupportsDebugRendering => _debugRenderer?.IsInitialized ?? false;
    public bool SupportsParticleRendering => _particleRenderer?.IsInitialized ?? false;
    public void SetUiDrawData(UiDrawData drawData) => _uiDrawData = drawData;

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
        CreateRasterRenderPass();
        CreateDepthResources();
        CreateRasterFramebuffers();
        CreateRasterPipeline();
        CreateDescriptorSetLayout();
        CreateComputePipeline();
        CreateDescriptorPool();
        CreateDescriptorSets();
        CreateUniformBuffer();
        CreateCommandPools();
        CreateCommandBuffers();
        CreateSyncObjects();
        InitializeDebugRenderer();
        InitializeUiRenderer();

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
            ApiVersion = Vk.Version10
        };

        // Query available instance extensions
        uint availableExtCount = 0;
        _vk!.EnumerateInstanceExtensionProperties((byte*)null, &availableExtCount, null);
        var availableExtensions = new ExtensionProperties[availableExtCount];
        fixed (ExtensionProperties* extPtr = availableExtensions)
        {
            _vk.EnumerateInstanceExtensionProperties((byte*)null, &availableExtCount, extPtr);
        }
        
        var availableExtNames = new HashSet<string>();
        for (int i = 0; i < availableExtensions.Length; i++)
        {
            fixed (ExtensionProperties* extPtr = &availableExtensions[i])
            {
                availableExtNames.Add(Marshal.PtrToStringAnsi((IntPtr)extPtr->ExtensionName)!);
            }
        }

        // Get required extensions from window
        var windowExtensions = _window.VkSurface!.GetRequiredExtensions(out uint extCount);
        
        // Build extension list
        var extensions = new List<string>();
        for (uint i = 0; i < extCount; i++)
        {
            extensions.Add(Marshal.PtrToStringAnsi((IntPtr)windowExtensions[i])!);
        }
        
        // Check if portability enumeration extension is available (MoltenVK on macOS)
        const string portabilityEnumExt = "VK_KHR_portability_enumeration";
        bool hasPortabilityEnum = availableExtNames.Contains(portabilityEnumExt);
        
        if (hasPortabilityEnum && !extensions.Contains(portabilityEnumExt))
        {
            extensions.Add(portabilityEnumExt);
        }
        

        // Convert to native pointers
        var extensionPtrs = new byte*[extensions.Count];
        for (int i = 0; i < extensions.Count; i++)
        {
            extensionPtrs[i] = (byte*)Marshal.StringToHGlobalAnsi(extensions[i]);
        }

        fixed (byte** extensionsPtr = extensionPtrs)
        {
            var createInfo = new InstanceCreateInfo
            {
                SType = StructureType.InstanceCreateInfo,
                PApplicationInfo = &appInfo,
                EnabledExtensionCount = (uint)extensions.Count,
                PpEnabledExtensionNames = extensionsPtr,
                // Only set portability flag if the extension is available
                Flags = hasPortabilityEnum ? InstanceCreateFlags.EnumeratePortabilityBitKhr : 0
            };

            fixed (Instance* instance = &_instance)
            {
                var result = _vk!.CreateInstance(&createInfo, null, instance);
                if (result != Result.Success)
                {
                    throw new Exception($"Failed to create Vulkan instance: {result}");
                }
            }
        }
        
        // Free allocated strings
        foreach (var ptr in extensionPtrs)
        {
            Marshal.FreeHGlobal((IntPtr)ptr);
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
        CreateStorageImage(_width, _height, out _outputImage, out _outputImageMemory, out _outputImageView);
        CreateStorageImage(_width, _height, out _accumImage, out _accumImageMemory, out _accumImageView);
        _accumInitialized = false;
        _outputImageLayout = ImageLayout.Undefined;
    }

    private void CreateStorageImage(int width, int height, out Image image, out DeviceMemory memory, out ImageView view)
    {
        var imageInfo = new ImageCreateInfo
        {
            SType = StructureType.ImageCreateInfo,
            ImageType = ImageType.Type2D,
            Format = Format.R32G32B32A32Sfloat,
            Extent = new Extent3D((uint)width, (uint)height, 1),
            MipLevels = 1,
            ArrayLayers = 1,
            Samples = SampleCountFlags.Count1Bit,
            Tiling = ImageTiling.Optimal,
            Usage = ImageUsageFlags.StorageBit | ImageUsageFlags.TransferSrcBit,
            SharingMode = SharingMode.Exclusive,
            InitialLayout = ImageLayout.Undefined
        };

        fixed (Image* imagePtr = &image)
        {
            if (_vk!.CreateImage(_device, &imageInfo, null, imagePtr) != Result.Success)
            {
                throw new Exception("Failed to create storage image");
            }
        }

        _vk!.GetImageMemoryRequirements(_device, image, out var memRequirements);

        var allocInfo = new MemoryAllocateInfo
        {
            SType = StructureType.MemoryAllocateInfo,
            AllocationSize = memRequirements.Size,
            MemoryTypeIndex = FindMemoryType(memRequirements.MemoryTypeBits, MemoryPropertyFlags.DeviceLocalBit)
        };

        fixed (DeviceMemory* memoryPtr = &memory)
        {
            if (_vk.AllocateMemory(_device, &allocInfo, null, memoryPtr) != Result.Success)
            {
                throw new Exception("Failed to allocate image memory");
            }
        }

        _vk.BindImageMemory(_device, image, memory, 0);
        view = CreateImageView(image, Format.R32G32B32A32Sfloat);
    }

    private void CreateRasterRenderPass()
    {
        var colorAttachment = new AttachmentDescription
        {
            Format = _swapchainFormat,
            Samples = SampleCountFlags.Count1Bit,
            LoadOp = AttachmentLoadOp.Clear,
            StoreOp = AttachmentStoreOp.Store,
            StencilLoadOp = AttachmentLoadOp.DontCare,
            StencilStoreOp = AttachmentStoreOp.DontCare,
            InitialLayout = ImageLayout.Undefined,
            FinalLayout = ImageLayout.PresentSrcKhr
        };

        var depthAttachment = new AttachmentDescription
        {
            Format = DepthFormat,
            Samples = SampleCountFlags.Count1Bit,
            LoadOp = AttachmentLoadOp.Clear,
            StoreOp = AttachmentStoreOp.DontCare,
            StencilLoadOp = AttachmentLoadOp.DontCare,
            StencilStoreOp = AttachmentStoreOp.DontCare,
            InitialLayout = ImageLayout.Undefined,
            FinalLayout = ImageLayout.DepthStencilAttachmentOptimal
        };

        var attachments = stackalloc AttachmentDescription[] { colorAttachment, depthAttachment };

        var colorAttachmentRef = new AttachmentReference
        {
            Attachment = 0,
            Layout = ImageLayout.ColorAttachmentOptimal
        };

        var depthAttachmentRef = new AttachmentReference
        {
            Attachment = 1,
            Layout = ImageLayout.DepthStencilAttachmentOptimal
        };

        var subpass = new SubpassDescription
        {
            PipelineBindPoint = PipelineBindPoint.Graphics,
            ColorAttachmentCount = 1,
            PColorAttachments = &colorAttachmentRef,
            PDepthStencilAttachment = &depthAttachmentRef
        };

        var dependencies = stackalloc SubpassDependency[]
        {
            new SubpassDependency
            {
                SrcSubpass = Vk.SubpassExternal,
                DstSubpass = 0,
                SrcStageMask = PipelineStageFlags.ColorAttachmentOutputBit | PipelineStageFlags.EarlyFragmentTestsBit,
                DstStageMask = PipelineStageFlags.ColorAttachmentOutputBit | PipelineStageFlags.EarlyFragmentTestsBit,
                SrcAccessMask = 0,
                DstAccessMask = AccessFlags.ColorAttachmentWriteBit | AccessFlags.DepthStencilAttachmentWriteBit
            }
        };

        var renderPassInfo = new RenderPassCreateInfo
        {
            SType = StructureType.RenderPassCreateInfo,
            AttachmentCount = 2,
            PAttachments = attachments,
            SubpassCount = 1,
            PSubpasses = &subpass,
            DependencyCount = 1,
            PDependencies = dependencies
        };

        fixed (RenderPass* rpPtr = &_rasterRenderPass)
        {
            if (_vk!.CreateRenderPass(_device, &renderPassInfo, null, rpPtr) != Result.Success)
            {
                throw new Exception("Failed to create raster render pass");
            }
        }
        
        // Create overlay render pass (loads existing content, doesn't clear)
        var overlayColorAttachment = new AttachmentDescription
        {
            Format = _swapchainFormat,
            Samples = SampleCountFlags.Count1Bit,
            LoadOp = AttachmentLoadOp.Load,  // Load existing content
            StoreOp = AttachmentStoreOp.Store,
            StencilLoadOp = AttachmentLoadOp.DontCare,
            StencilStoreOp = AttachmentStoreOp.DontCare,
            InitialLayout = ImageLayout.ColorAttachmentOptimal,
            FinalLayout = ImageLayout.ColorAttachmentOptimal
        };

        var overlayDepthAttachment = new AttachmentDescription
        {
            Format = DepthFormat,
            Samples = SampleCountFlags.Count1Bit,
            LoadOp = AttachmentLoadOp.Clear,  // Clear depth for proper overlay
            StoreOp = AttachmentStoreOp.DontCare,
            StencilLoadOp = AttachmentLoadOp.DontCare,
            StencilStoreOp = AttachmentStoreOp.DontCare,
            InitialLayout = ImageLayout.Undefined,
            FinalLayout = ImageLayout.DepthStencilAttachmentOptimal
        };

        var overlayAttachments = stackalloc AttachmentDescription[] { overlayColorAttachment, overlayDepthAttachment };

        var overlayRenderPassInfo = new RenderPassCreateInfo
        {
            SType = StructureType.RenderPassCreateInfo,
            AttachmentCount = 2,
            PAttachments = overlayAttachments,
            SubpassCount = 1,
            PSubpasses = &subpass,
            DependencyCount = 1,
            PDependencies = dependencies
        };

        fixed (RenderPass* rpPtr = &_overlayRenderPass)
        {
            if (_vk!.CreateRenderPass(_device, &overlayRenderPassInfo, null, rpPtr) != Result.Success)
            {
                throw new Exception("Failed to create overlay render pass");
            }
        }
    }

    private void CreateDepthResources()
    {
        var imageInfo = new ImageCreateInfo
        {
            SType = StructureType.ImageCreateInfo,
            ImageType = ImageType.Type2D,
            Extent = new Extent3D(_swapchainExtent.Width, _swapchainExtent.Height, 1),
            MipLevels = 1,
            ArrayLayers = 1,
            Format = DepthFormat,
            Tiling = ImageTiling.Optimal,
            InitialLayout = ImageLayout.Undefined,
            Usage = ImageUsageFlags.DepthStencilAttachmentBit,
            Samples = SampleCountFlags.Count1Bit,
            SharingMode = SharingMode.Exclusive
        };

        fixed (Image* imagePtr = &_depthImage)
        {
            if (_vk!.CreateImage(_device, &imageInfo, null, imagePtr) != Result.Success)
            {
                throw new Exception("Failed to create depth image");
            }
        }

        _vk!.GetImageMemoryRequirements(_device, _depthImage, out var memRequirements);

        var allocInfo = new MemoryAllocateInfo
        {
            SType = StructureType.MemoryAllocateInfo,
            AllocationSize = memRequirements.Size,
            MemoryTypeIndex = FindMemoryType(memRequirements.MemoryTypeBits, MemoryPropertyFlags.DeviceLocalBit)
        };

        fixed (DeviceMemory* memPtr = &_depthImageMemory)
        {
            if (_vk.AllocateMemory(_device, &allocInfo, null, memPtr) != Result.Success)
            {
                throw new Exception("Failed to allocate depth image memory");
            }
        }

        _vk.BindImageMemory(_device, _depthImage, _depthImageMemory, 0);

        var viewInfo = new ImageViewCreateInfo
        {
            SType = StructureType.ImageViewCreateInfo,
            Image = _depthImage,
            ViewType = ImageViewType.Type2D,
            Format = DepthFormat,
            SubresourceRange = new ImageSubresourceRange
            {
                AspectMask = ImageAspectFlags.DepthBit,
                BaseMipLevel = 0,
                LevelCount = 1,
                BaseArrayLayer = 0,
                LayerCount = 1
            }
        };

        fixed (ImageView* viewPtr = &_depthImageView)
        {
            if (_vk.CreateImageView(_device, &viewInfo, null, viewPtr) != Result.Success)
            {
                throw new Exception("Failed to create depth image view");
            }
        }
    }

    private void CreateRasterFramebuffers()
    {
        _rasterFramebuffers = new Framebuffer[_swapchainImageViews.Length];

        for (int i = 0; i < _swapchainImageViews.Length; i++)
        {
            var attachments = stackalloc ImageView[] { _swapchainImageViews[i], _depthImageView };

            var framebufferInfo = new FramebufferCreateInfo
            {
                SType = StructureType.FramebufferCreateInfo,
                RenderPass = _rasterRenderPass,
                AttachmentCount = 2,
                PAttachments = attachments,
                Width = _swapchainExtent.Width,
                Height = _swapchainExtent.Height,
                Layers = 1
            };

            fixed (Framebuffer* fbPtr = &_rasterFramebuffers[i])
            {
                if (_vk!.CreateFramebuffer(_device, &framebufferInfo, null, fbPtr) != Result.Success)
                {
                    throw new Exception("Failed to create raster framebuffer");
                }
            }
        }
    }

    private void CreateRasterPipeline()
    {
        var vertCode = LoadShaderBytes(Path.Combine("Rendering", "Shaders", "raster.vert.spv"));
        var fragCode = LoadShaderBytes(Path.Combine("Rendering", "Shaders", "raster.frag.spv"));

        var vertModule = CreateShaderModule(vertCode);
        var fragModule = CreateShaderModule(fragCode);

        var vertStage = new PipelineShaderStageCreateInfo
        {
            SType = StructureType.PipelineShaderStageCreateInfo,
            Stage = ShaderStageFlags.VertexBit,
            Module = vertModule,
            PName = (byte*)Marshal.StringToHGlobalAnsi("main")
        };

        var fragStage = new PipelineShaderStageCreateInfo
        {
            SType = StructureType.PipelineShaderStageCreateInfo,
            Stage = ShaderStageFlags.FragmentBit,
            Module = fragModule,
            PName = (byte*)Marshal.StringToHGlobalAnsi("main")
        };

        var shaderStages = stackalloc[] { vertStage, fragStage };

        var bindingDescription = new VertexInputBindingDescription
        {
            Binding = 0,
            Stride = (uint)Unsafe.SizeOf<RasterVertex>(),
            InputRate = VertexInputRate.Vertex
        };

        var attributeDescriptions = stackalloc VertexInputAttributeDescription[2];
        attributeDescriptions[0] = new VertexInputAttributeDescription
        {
            Binding = 0,
            Location = 0,
            Format = Format.R32G32B32Sfloat,
            Offset = 0
        };
        attributeDescriptions[1] = new VertexInputAttributeDescription
        {
            Binding = 0,
            Location = 1,
            Format = Format.R32G32B32A32Sfloat,
            Offset = 12
        };

        var vertexInputInfo = new PipelineVertexInputStateCreateInfo
        {
            SType = StructureType.PipelineVertexInputStateCreateInfo,
            VertexBindingDescriptionCount = 1,
            PVertexBindingDescriptions = &bindingDescription,
            VertexAttributeDescriptionCount = 2,
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
            PolygonMode = PolygonMode.Fill,
            CullMode = CullModeFlags.None, // Disabled for debugging
            FrontFace = FrontFace.Clockwise, // Flipped for Vulkan Y-flip
            LineWidth = 1f
        };

        var multisampling = new PipelineMultisampleStateCreateInfo
        {
            SType = StructureType.PipelineMultisampleStateCreateInfo,
            RasterizationSamples = SampleCountFlags.Count1Bit
        };

        var depthStencil = new PipelineDepthStencilStateCreateInfo
        {
            SType = StructureType.PipelineDepthStencilStateCreateInfo,
            DepthTestEnable = true,
            DepthWriteEnable = true,
            DepthCompareOp = CompareOp.Less,
            DepthBoundsTestEnable = false,
            StencilTestEnable = false
        };

        var colorBlendAttachment = new PipelineColorBlendAttachmentState
        {
            ColorWriteMask = ColorComponentFlags.RBit | ColorComponentFlags.GBit | ColorComponentFlags.BBit | ColorComponentFlags.ABit,
            BlendEnable = false
        };

        var colorBlending = new PipelineColorBlendStateCreateInfo
        {
            SType = StructureType.PipelineColorBlendStateCreateInfo,
            AttachmentCount = 1,
            PAttachments = &colorBlendAttachment
        };

        var dynamicStates = stackalloc[] { DynamicState.Viewport, DynamicState.Scissor };
        var dynamicState = new PipelineDynamicStateCreateInfo
        {
            SType = StructureType.PipelineDynamicStateCreateInfo,
            DynamicStateCount = 2,
            PDynamicStates = dynamicStates
        };

        var pushConstantRange = new PushConstantRange
        {
            StageFlags = ShaderStageFlags.VertexBit,
            Offset = 0,
            Size = (uint)Unsafe.SizeOf<Matrix4x4>()
        };

        var pipelineLayoutInfo = new PipelineLayoutCreateInfo
        {
            SType = StructureType.PipelineLayoutCreateInfo,
            PushConstantRangeCount = 1,
            PPushConstantRanges = &pushConstantRange
        };

        fixed (PipelineLayout* layoutPtr = &_rasterPipelineLayout)
        {
            if (_vk!.CreatePipelineLayout(_device, &pipelineLayoutInfo, null, layoutPtr) != Result.Success)
            {
                throw new Exception("Failed to create raster pipeline layout");
            }
        }

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
            PDepthStencilState = &depthStencil,
            PColorBlendState = &colorBlending,
            PDynamicState = &dynamicState,
            Layout = _rasterPipelineLayout,
            RenderPass = _rasterRenderPass,
            Subpass = 0
        };

        fixed (Pipeline* pipelinePtr = &_rasterPipeline)
        {
            if (_vk!.CreateGraphicsPipelines(_device, default, 1, &pipelineInfo, null, pipelinePtr) != Result.Success)
            {
                throw new Exception("Failed to create raster pipeline");
            }
        }

        _vk.DestroyShaderModule(_device, vertModule, null);
        _vk.DestroyShaderModule(_device, fragModule, null);
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
            new() // Accumulation image
            {
                Binding = 4,
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
            },
            new() // BVH nodes
            {
                Binding = 2,
                DescriptorType = DescriptorType.StorageBuffer,
                DescriptorCount = 1,
                StageFlags = ShaderStageFlags.ComputeBit
            },
            new() // Triangles
            {
                Binding = 3,
                DescriptorType = DescriptorType.StorageBuffer,
                DescriptorCount = 1,
                StageFlags = ShaderStageFlags.ComputeBit
            },
            new() // PBR Materials
            {
                Binding = 5,
                DescriptorType = DescriptorType.StorageBuffer,
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
        string basePath = AppContext.BaseDirectory;
        string spvPath = Path.Combine(basePath, "Rendering", "Shaders", "raytracer_vulkan.spv");
        string sourcePath = Path.Combine(basePath, "Rendering", "Shaders", "raytracer_vulkan.comp");

        if (File.Exists(sourcePath))
        {
            try
            {
                var shaderc = Shaderc.GetApi();
                var compiler = shaderc.CompilerInitialize();
                var options = shaderc.CompileOptionsInitialize();

                var source = File.ReadAllText(sourcePath);
                var sourceBytes = Encoding.UTF8.GetBytes(source);
                var fileNamePtr = SilkMarshal.StringToPtr("raytracer_vulkan.comp", NativeStringEncoding.UTF8);
                var entryPointPtr = SilkMarshal.StringToPtr("main", NativeStringEncoding.UTF8);

                CompilationResult* result = null;
                fixed (byte* sourcePtr = sourceBytes)
                {
                    result = shaderc.CompileIntoSpv(compiler, sourcePtr, (nuint)sourceBytes.Length, ShaderKind.ComputeShader, (byte*)fileNamePtr, (byte*)entryPointPtr, options);
                }

                var status = shaderc.ResultGetCompilationStatus(result);
                if (status == CompilationStatus.Success)
                {
                    var length = shaderc.ResultGetLength(result);
                    var bytesPtr = shaderc.ResultGetBytes(result);
                    var spirv = new byte[(int)length];
                    Marshal.Copy((IntPtr)bytesPtr, spirv, 0, (int)length);
                    Console.WriteLine($"Compiled Vulkan shader from source {sourcePath} ({spirv.Length} bytes)");

                    shaderc.ResultRelease(result);
                    shaderc.CompileOptionsRelease(options);
                    shaderc.CompilerRelease(compiler);
                    SilkMarshal.Free(fileNamePtr);
                    SilkMarshal.Free(entryPointPtr);

                    return spirv;
                }

                var error = shaderc.ResultGetErrorMessageS(result);
                Console.WriteLine($"Shaderc failed to compile {sourcePath}: {error}");

                shaderc.ResultRelease(result);
                shaderc.CompileOptionsRelease(options);
                shaderc.CompilerRelease(compiler);
                SilkMarshal.Free(fileNamePtr);
                SilkMarshal.Free(entryPointPtr);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Shaderc compile failed, falling back to SPIR-V: {ex.Message}");
            }
        }

        if (!File.Exists(spvPath))
        {
            throw new FileNotFoundException($"Pre-compiled shader not found: {spvPath}. Run: glslangValidator --target-env vulkan1.0 -S comp -o raytracer_vulkan.spv raytracer_vulkan.comp");
        }

        byte[] precompiled = File.ReadAllBytes(spvPath);
        Console.WriteLine($"Loaded pre-compiled SPIR-V shader from {spvPath} ({precompiled.Length} bytes)");

        return precompiled;
    }

    private static byte[] LoadShaderBytes(string relativePath)
    {
        string basePath = AppContext.BaseDirectory;
        string[] possiblePaths =
        {
            Path.Combine(basePath, relativePath),
            Path.Combine(basePath, "..", "..", "..", relativePath),
            relativePath
        };

        foreach (string path in possiblePaths)
        {
            if (File.Exists(path))
            {
                return File.ReadAllBytes(path);
            }
        }

        throw new FileNotFoundException($"Shader file not found: {relativePath}");
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
    vec4 u_RaySettings;
    vec4 u_FrameSettings;
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
            new() { Type = DescriptorType.StorageImage, DescriptorCount = 2 },
            new() { Type = DescriptorType.UniformBuffer, DescriptorCount = 1 },
            new() { Type = DescriptorType.StorageBuffer, DescriptorCount = 3 } // BVH, Triangles, Materials
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

        var accumInfo = new DescriptorImageInfo
        {
            ImageView = _accumImageView,
            ImageLayout = ImageLayout.General
        };

        var bufferDescInfo = new DescriptorBufferInfo
        {
            Buffer = _uniformBuffer,
            Offset = 0,
            Range = bufferSize
        };

        EnsureRaytracerBuffers(1, 1);
        EnsureMaterialBuffer(1); // Ensure at least 1 material slot

        var bvhDescInfo = new DescriptorBufferInfo
        {
            Buffer = _bvhNodeBuffer,
            Offset = 0,
            Range = _bvhNodeBufferSize
        };

        var triDescInfo = new DescriptorBufferInfo
        {
            Buffer = _triangleBuffer,
            Offset = 0,
            Range = _triangleBufferSize
        };

        var matDescInfo = new DescriptorBufferInfo
        {
            Buffer = _materialBuffer,
            Offset = 0,
            Range = _materialBufferSize
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
                DstBinding = 4,
                DstArrayElement = 0,
                DescriptorType = DescriptorType.StorageImage,
                DescriptorCount = 1,
                PImageInfo = &accumInfo
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
            },
            new()
            {
                SType = StructureType.WriteDescriptorSet,
                DstSet = _computeDescriptorSet,
                DstBinding = 2,
                DstArrayElement = 0,
                DescriptorType = DescriptorType.StorageBuffer,
                DescriptorCount = 1,
                PBufferInfo = &bvhDescInfo
            },
            new()
            {
                SType = StructureType.WriteDescriptorSet,
                DstSet = _computeDescriptorSet,
                DstBinding = 3,
                DstArrayElement = 0,
                DescriptorType = DescriptorType.StorageBuffer,
                DescriptorCount = 1,
                PBufferInfo = &triDescInfo
            },
            new()
            {
                SType = StructureType.WriteDescriptorSet,
                DstSet = _computeDescriptorSet,
                DstBinding = 5,
                DstArrayElement = 0,
                DescriptorType = DescriptorType.StorageBuffer,
                DescriptorCount = 1,
                PBufferInfo = &matDescInfo
            }
        };

        fixed (WriteDescriptorSet* writesPtr = writes)
        {
            _vk.UpdateDescriptorSets(_device, (uint)writes.Length, writesPtr, 0, null);
        }
    }

    private void EnsureRaytracerBuffers(ulong minNodeSize, ulong minTriSize)
    {
        bool nodesChanged = EnsureStorageBuffer(ref _bvhNodeBuffer, ref _bvhNodeBufferMemory, ref _bvhNodeBufferSize, minNodeSize);
        bool trisChanged = EnsureStorageBuffer(ref _triangleBuffer, ref _triangleBufferMemory, ref _triangleBufferSize, minTriSize);

        if (nodesChanged || trisChanged)
        {
            UpdateRaytracerBufferDescriptors();
        }
    }

    private void EnsureRasterBuffers(ulong vertexSize, ulong indexSize)
    {
        EnsureRasterBuffer(ref _rasterVertexBuffer, ref _rasterVertexBufferMemory, ref _rasterVertexBufferSize, vertexSize, BufferUsageFlags.VertexBufferBit);
        EnsureRasterBuffer(ref _rasterIndexBuffer, ref _rasterIndexBufferMemory, ref _rasterIndexBufferSize, indexSize, BufferUsageFlags.IndexBufferBit);
    }

    private void EnsureRasterBuffer(ref VkBuffer buffer, ref DeviceMemory memory, ref ulong bufferSize, ulong requiredSize, BufferUsageFlags usage)
    {
        if (requiredSize == 0)
            requiredSize = (ulong)Unsafe.SizeOf<int>();

        if (buffer.Handle != 0 && bufferSize >= requiredSize)
            return;

        if (buffer.Handle != 0)
        {
            _vk.DestroyBuffer(_device, buffer, null);
            _vk.FreeMemory(_device, memory, null);
        }

        CreateBuffer(requiredSize, usage | BufferUsageFlags.TransferDstBit, MemoryPropertyFlags.DeviceLocalBit, out buffer, out memory);
        bufferSize = requiredSize;
    }

    private bool EnsureStorageBuffer(ref VkBuffer buffer, ref DeviceMemory memory, ref ulong bufferSize, ulong requiredSize)
    {
        if (requiredSize == 0)
            requiredSize = (ulong)Unsafe.SizeOf<int>();

        if (buffer.Handle != 0 && bufferSize >= requiredSize)
            return false;

        if (buffer.Handle != 0)
        {
            _vk.DestroyBuffer(_device, buffer, null);
            _vk.FreeMemory(_device, memory, null);
        }

        CreateBuffer(requiredSize, BufferUsageFlags.StorageBufferBit | BufferUsageFlags.TransferDstBit, MemoryPropertyFlags.DeviceLocalBit, out buffer, out memory);
        bufferSize = requiredSize;
        return true;
    }

    private void UpdateRaytracerBufferDescriptors()
    {
        var bvhDescInfo = new DescriptorBufferInfo
        {
            Buffer = _bvhNodeBuffer,
            Offset = 0,
            Range = _bvhNodeBufferSize
        };

        var triDescInfo = new DescriptorBufferInfo
        {
            Buffer = _triangleBuffer,
            Offset = 0,
            Range = _triangleBufferSize
        };

        var writes = new WriteDescriptorSet[]
        {
            new()
            {
                SType = StructureType.WriteDescriptorSet,
                DstSet = _computeDescriptorSet,
                DstBinding = 2,
                DstArrayElement = 0,
                DescriptorType = DescriptorType.StorageBuffer,
                DescriptorCount = 1,
                PBufferInfo = &bvhDescInfo
            },
            new()
            {
                SType = StructureType.WriteDescriptorSet,
                DstSet = _computeDescriptorSet,
                DstBinding = 3,
                DstArrayElement = 0,
                DescriptorType = DescriptorType.StorageBuffer,
                DescriptorCount = 1,
                PBufferInfo = &triDescInfo
            }
        };

        fixed (WriteDescriptorSet* writesPtr = writes)
        {
            _vk.UpdateDescriptorSets(_device, (uint)writes.Length, writesPtr, 0, null);
        }
    }

    private void EnsureMaterialBuffer(int minMaterialCount)
    {
        ulong minSize = (ulong)(minMaterialCount * Unsafe.SizeOf<GpuMaterial>());
        if (minSize == 0) minSize = (ulong)Unsafe.SizeOf<GpuMaterial>();

        bool changed = EnsureStorageBuffer(ref _materialBuffer, ref _materialBufferMemory, ref _materialBufferSize, minSize);
        
        if (changed)
        {
            UpdateMaterialBufferDescriptor();
        }
    }

    private void UpdateMaterialBufferDescriptor()
    {
        if (_materialBuffer.Handle == 0) return;

        var matDescInfo = new DescriptorBufferInfo
        {
            Buffer = _materialBuffer,
            Offset = 0,
            Range = _materialBufferSize
        };

        var write = new WriteDescriptorSet
        {
            SType = StructureType.WriteDescriptorSet,
            DstSet = _computeDescriptorSet,
            DstBinding = 5,
            DstArrayElement = 0,
            DescriptorType = DescriptorType.StorageBuffer,
            DescriptorCount = 1,
            PBufferInfo = &matDescInfo
        };

        _vk!.UpdateDescriptorSets(_device, 1, &write, 0, null);
    }

    /// <summary>
    /// Upload PBR materials to the GPU
    /// </summary>
    public void UploadMaterials(GpuMaterial[] materials)
    {
        if (materials.Length == 0)
        {
            // Upload a single default material
            materials = new[] { GpuMaterial.Default };
        }

        _materialCount = materials.Length;
        EnsureMaterialBuffer(materials.Length);
        UploadBufferData(materials, _materialBuffer);
        Console.WriteLine($"Uploaded {materials.Length} PBR materials to GPU");
    }

    /// <summary>
    /// Upload materials from a MaterialLibrary
    /// </summary>
    public void UploadMaterials(MaterialLibrary library)
    {
        var gpuMaterials = library.GetGpuMaterials();
        UploadMaterials(gpuMaterials);
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
            if (_vk!.CreateBuffer(_device, &bufferInfo, null, bufferPtr) != Result.Success)
            {
                throw new Exception("Failed to create buffer");
            }
        }

        _vk!.GetBufferMemoryRequirements(_device, buffer, out var memRequirements);

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

    private void EnsureReadbackBuffer(ulong size)
    {
        if (_readbackBuffer.Handle != 0 && _readbackBufferSize >= size)
            return;

        if (_readbackBuffer.Handle != 0)
        {
            _vk!.DestroyBuffer(_device, _readbackBuffer, null);
            _vk.FreeMemory(_device, _readbackBufferMemory, null);
            _readbackBuffer = default;
            _readbackBufferMemory = default;
        }

        CreateBuffer(size, BufferUsageFlags.TransferDstBit, MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit, out _readbackBuffer, out _readbackBufferMemory);
        _readbackBufferSize = size;
    }

    private static byte ToByte(float value)
    {
        value = Math.Clamp(value, 0f, 1f);
        return (byte)(value * 255f + 0.5f);
    }

    private void UploadBufferData<T>(T[] data, VkBuffer dstBuffer) where T : unmanaged
    {
        if (data.Length == 0)
            return;

        ulong size = (ulong)(data.Length * Unsafe.SizeOf<T>());

        CreateBuffer(size, BufferUsageFlags.TransferSrcBit, MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit, out var stagingBuffer, out var stagingMemory);

        void* mapped = null;
        _vk.MapMemory(_device, stagingMemory, 0, size, 0, &mapped);

        fixed (T* src = data)
        {
            System.Buffer.MemoryCopy(src, mapped, size, size);
        }

        _vk.UnmapMemory(_device, stagingMemory);

        var allocInfo = new CommandBufferAllocateInfo
        {
            SType = StructureType.CommandBufferAllocateInfo,
            CommandPool = _graphicsCommandPool,
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

        var copyRegion = new BufferCopy { Size = size };
        _vk.CmdCopyBuffer(commandBuffer, stagingBuffer, dstBuffer, 1, &copyRegion);

        _vk.EndCommandBuffer(commandBuffer);

        var submitInfo = new SubmitInfo
        {
            SType = StructureType.SubmitInfo,
            CommandBufferCount = 1,
            PCommandBuffers = &commandBuffer
        };

        _vk.QueueSubmit(_graphicsQueue, 1, &submitInfo, default);
        _vk.QueueWaitIdle(_graphicsQueue);

        _vk.FreeCommandBuffers(_device, _graphicsCommandPool, 1, &commandBuffer);

        _vk.DestroyBuffer(_device, stagingBuffer, null);
        _vk.FreeMemory(_device, stagingMemory, null);
    }

    private void CreateCommandPools()
    {
        var graphicsPoolInfo = new CommandPoolCreateInfo
        {
            SType = StructureType.CommandPoolCreateInfo,
            QueueFamilyIndex = _graphicsQueueFamily,
            Flags = CommandPoolCreateFlags.ResetCommandBufferBit
        };

        fixed (CommandPool* pool = &_graphicsCommandPool)
        {
            if (_vk!.CreateCommandPool(_device, &graphicsPoolInfo, null, pool) != Result.Success)
            {
                throw new Exception("Failed to create graphics command pool");
            }
        }

        var computePoolInfo = new CommandPoolCreateInfo
        {
            SType = StructureType.CommandPoolCreateInfo,
            QueueFamilyIndex = _computeQueueFamily,
            Flags = CommandPoolCreateFlags.ResetCommandBufferBit
        };

        fixed (CommandPool* pool = &_computeCommandPool)
        {
            if (_vk!.CreateCommandPool(_device, &computePoolInfo, null, pool) != Result.Success)
            {
                throw new Exception("Failed to create compute command pool");
            }
        }
    }

    private void CreateCommandBuffers()
    {
        var computeAlloc = new CommandBufferAllocateInfo
        {
            SType = StructureType.CommandBufferAllocateInfo,
            CommandPool = _computeCommandPool,
            Level = CommandBufferLevel.Primary,
            CommandBufferCount = 1
        };

        var graphicsAlloc = new CommandBufferAllocateInfo
        {
            SType = StructureType.CommandBufferAllocateInfo,
            CommandPool = _graphicsCommandPool,
            Level = CommandBufferLevel.Primary,
            CommandBufferCount = 1
        };

        fixed (CommandBuffer* computeCmdPtr = &_computeCommandBuffer)
        {
            if (_vk!.AllocateCommandBuffers(_device, &computeAlloc, computeCmdPtr) != Result.Success)
            {
                throw new Exception("Failed to allocate compute command buffer");
            }
        }

        fixed (CommandBuffer* graphicsCmdPtr = &_graphicsCommandBuffer)
        {
            if (_vk!.AllocateCommandBuffers(_device, &graphicsAlloc, graphicsCmdPtr) != Result.Success)
            {
                throw new Exception("Failed to allocate graphics command buffer");
            }
        }
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

    private void InitializeDebugRenderer()
    {
        try
        {
            _debugRenderer = new DebugRenderer(_vk!, _device, _physicalDevice, _graphicsQueueFamily);
            _debugRenderer.Initialize(_swapchainFormat, _swapchainExtent, _swapchainImageViews);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Warning: Debug renderer initialization failed: {ex.Message}");
            Console.WriteLine("Debug visualization will be disabled.");
            _debugRenderer = null;
        }
        
        // Initialize particle renderer
        try
        {
            _particleRenderer = new ParticleRenderer(_vk!, _device, _physicalDevice, _graphicsQueueFamily);
            _particleRenderer.Initialize(_swapchainFormat, _swapchainExtent, _swapchainImageViews);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Warning: Particle renderer initialization failed: {ex.Message}");
            Console.WriteLine("Particle rendering will be disabled.");
            _particleRenderer = null;
        }
    }

    private void InitializeUiRenderer()
    {
        try
        {
            _uiRenderer = new UiRenderer(_vk!, _device, _physicalDevice, _graphicsQueueFamily);
            _uiRenderer.Initialize(_swapchainFormat, _swapchainExtent, _swapchainImageViews);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Warning: UI renderer initialization failed: {ex.Message}");
            Console.WriteLine("UI overlay will be disabled.");
            _uiRenderer = null;
        }
    }

    public void SetRaytracerMeshData(RaytracerMeshData data)
    {
        if (data == null)
        {
            _bvhNodeCount = 0;
            _triangleCount = 0;
            return;
        }

        _bvhNodeCount = data.Nodes.Length;
        _triangleCount = data.Triangles.Length;

        if (_bvhNodeCount == 0 || _triangleCount == 0)
            return;

        ulong nodeSize = (ulong)(_bvhNodeCount * Unsafe.SizeOf<RaytracerBvhNode>());
        ulong triSize = (ulong)(_triangleCount * Unsafe.SizeOf<RaytracerTriangle>());

        EnsureRaytracerBuffers(nodeSize, triSize);
        UploadBufferData(data.Nodes, _bvhNodeBuffer);
        UploadBufferData(data.Triangles, _triangleBuffer);
    }

    public void SetRasterMeshData(RasterMeshData data)
    {
        if (data == null)
        {
            _rasterVertexCount = 0;
            _rasterIndexCount = 0;
            return;
        }

        _rasterVertexCount = data.Vertices.Length;
        _rasterIndexCount = data.Indices.Length;

        if (_rasterVertexCount == 0 || _rasterIndexCount == 0)
            return;

        ulong vertexSize = (ulong)(_rasterVertexCount * Unsafe.SizeOf<RasterVertex>());
        ulong indexSize = (ulong)(_rasterIndexCount * sizeof(uint));

        EnsureRasterBuffers(vertexSize, indexSize);
        UploadBufferData(data.Vertices, _rasterVertexBuffer);
        UploadBufferData(data.Indices, _rasterIndexBuffer);
    }

    public void Render(Camera camera, BlackHole? blackHole, float time)
    {
        Render(camera, blackHole, time, null, null);
    }

    public void Render(Camera camera, BlackHole? blackHole, float time, DebugDrawManager? debugDraw)
    {
        Render(camera, blackHole, time, debugDraw, null);
    }

    public void Render(Camera camera, BlackHole? blackHole, float time, DebugDrawManager? debugDraw, ParticlePool? particles)
    {
        var renderTimer = Profiler.Instance.GetOrCreateTimer("VulkanRender", "GPU");
        renderTimer.Start();
        
        fixed (Fence* fence = &_inFlightFence)
        {
            _vk!.WaitForFences(_device, 1, fence, true, ulong.MaxValue);
            _vk.ResetFences(_device, 1, fence);
        }

        bool useRaytracer = RenderSettings.Mode == RenderMode.Raytraced;

        UpdateAccumulationState(camera);

        // Update uniforms
        RaytracerSettings.Clamp();
        var uniforms = new RaytracerUniforms
        {
            Resolution = new Vector2(_width, _height),
            Time = time,
            CameraPos = camera.Position,
            CameraForward = camera.Forward,
            CameraRight = camera.Right,
            CameraUp = camera.Up,
            Fov = camera.FieldOfView,
            // When no black hole, set all parameters to 0 to disable lensing
            BlackHolePos = blackHole?.Position ?? Vector3.Zero,
            BlackHoleMass = blackHole?.Mass ?? 0f,
            SchwarzschildRadius = blackHole?.SchwarzschildRadius ?? 0f,
            DiskInnerRadius = blackHole?.DiskInnerRadius ?? 0f,
            DiskOuterRadius = blackHole?.DiskOuterRadius ?? 0f,
            RaySettings = new Vector4(
                RaytracerSettings.RaysPerPixel,
                RaytracerSettings.MaxBounces,
                _bvhNodeCount,
                _triangleCount),
            FrameSettings = new Vector4(
                RaytracerSettings.SamplesPerFrame,
                _frameIndex,
                RaytracerSettings.Accumulate ? 1f : 0f,
                RaytracerSettings.Denoise ? 1f : 0f),
            LensingSettings = new Vector4(
                RaytracerSettings.LensingMaxSteps,
                RaytracerSettings.LensingStepSize,
                RaytracerSettings.LensingBvhCheckInterval,
                RaytracerSettings.LensingMaxDistance),
            // Kerr parameters - all zero when no black hole
            BlackHoleSpin = blackHole?.Spin ?? 0f,
            KerrParameter = blackHole?.KerrParameter ?? 0f,
            OuterHorizonRadius = blackHole?.OuterHorizonRadius ?? 0f,
            ErgosphereRadius = blackHole?.ErgosphereEquatorialRadius ?? 0f,
            BlackHoleSpinAxis = blackHole?.SpinAxis ?? Vector3.UnitY,
            ShowErgosphere = RaytracerSettings.ShowErgosphere ? 1f : 0f,
            ErgosphereOpacity = RaytracerSettings.ErgosphereOpacity,
            DiskISCO = blackHole?.CalculateProgradeISCO() ?? 0f,
            DiskThickness = 0f,  // Thin disk only - volumetric causes rendering artifacts
            ShowPhotonSphere = RaytracerSettings.ShowPhotonSphere ? 1f : 0f,
            PhotonSphereOpacity = RaytracerSettings.PhotonSphereOpacity,
            PhotonSphereRadius = blackHole?.CalculatePhotonSphereRadius() ?? 0f
        };
        Unsafe.Copy(_uniformBufferMapped, ref uniforms);


        // Acquire swapchain image
        uint imageIndex = 0;
        _khrSwapchain!.AcquireNextImage(_device, _swapchain, ulong.MaxValue, _imageAvailableSemaphore, default, &imageIndex);

        // Record and submit compute commands (raytracer only)
        if (useRaytracer)
        {
            RecordComputeCommands();
            SubmitCompute();
        }

        // Record and submit graphics commands
        RecordGraphicsCommands(imageIndex, camera, time, debugDraw, particles);
        SubmitGraphics(useRaytracer);

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

        if (RaytracerSettings.Accumulate)
        {
            _frameIndex++;
        }
        else
        {
            _frameIndex = 0;
        }
        
        renderTimer.Stop();
        
        // Update profiler counters
        Profiler.Instance.SetCounter("BVHNodes", _bvhNodeCount, "Raytracer");
        Profiler.Instance.SetCounter("Triangles", _triangleCount, "Raytracer");
    }

    public void RenderToReadback(Camera camera, BlackHole? blackHole, float time, byte[] rgbaBuffer)
    {
        UpdateAccumulationState(camera);

        RaytracerSettings.Clamp();
        var uniforms = new RaytracerUniforms
        {
            Resolution = new Vector2(_width, _height),
            Time = time,
            CameraPos = camera.Position,
            CameraForward = camera.Forward,
            CameraRight = camera.Right,
            CameraUp = camera.Up,
            Fov = camera.FieldOfView,
            // When no black hole, set all parameters to 0 to disable lensing
            BlackHolePos = blackHole?.Position ?? Vector3.Zero,
            BlackHoleMass = blackHole?.Mass ?? 0f,
            SchwarzschildRadius = blackHole?.SchwarzschildRadius ?? 0f,
            DiskInnerRadius = blackHole?.DiskInnerRadius ?? 0f,
            DiskOuterRadius = blackHole?.DiskOuterRadius ?? 0f,
            RaySettings = new Vector4(
                RaytracerSettings.RaysPerPixel,
                RaytracerSettings.MaxBounces,
                _bvhNodeCount,
                _triangleCount),
            FrameSettings = new Vector4(
                RaytracerSettings.SamplesPerFrame,
                _frameIndex,
                RaytracerSettings.Accumulate ? 1f : 0f,
                RaytracerSettings.Denoise ? 1f : 0f),
            LensingSettings = new Vector4(
                RaytracerSettings.LensingMaxSteps,
                RaytracerSettings.LensingStepSize,
                RaytracerSettings.LensingBvhCheckInterval,
                RaytracerSettings.LensingMaxDistance),
            // Kerr parameters - all zero when no black hole
            BlackHoleSpin = blackHole?.Spin ?? 0f,
            KerrParameter = blackHole?.KerrParameter ?? 0f,
            OuterHorizonRadius = blackHole?.OuterHorizonRadius ?? 0f,
            ErgosphereRadius = blackHole?.ErgosphereEquatorialRadius ?? 0f,
            BlackHoleSpinAxis = blackHole?.SpinAxis ?? Vector3.UnitY,
            ShowErgosphere = RaytracerSettings.ShowErgosphere ? 1f : 0f,
            ErgosphereOpacity = RaytracerSettings.ErgosphereOpacity,
            DiskISCO = blackHole?.CalculateProgradeISCO() ?? 0f,
            DiskThickness = 0f,  // Thin disk only - volumetric causes rendering artifacts
            ShowPhotonSphere = RaytracerSettings.ShowPhotonSphere ? 1f : 0f,
            PhotonSphereOpacity = RaytracerSettings.PhotonSphereOpacity,
            PhotonSphereRadius = blackHole?.CalculatePhotonSphereRadius() ?? 0f
        };
        Unsafe.Copy(_uniformBufferMapped, ref uniforms);

        ulong requiredSize = (ulong)(_width * _height * 4 * sizeof(float));
        EnsureReadbackBuffer(requiredSize);

        RecordComputeCommands(_readbackBuffer);

        fixed (CommandBuffer* cmd = &_computeCommandBuffer)
        {
            var submitInfo = new SubmitInfo
            {
                SType = StructureType.SubmitInfo,
                CommandBufferCount = 1,
                PCommandBuffers = cmd
            };

            _vk!.QueueSubmit(_computeQueue, 1, &submitInfo, default);
            _vk.QueueWaitIdle(_computeQueue);
        }

        int requiredBytes = _width * _height * 4;
        if (rgbaBuffer.Length < requiredBytes)
            throw new ArgumentException("Readback buffer is too small", nameof(rgbaBuffer));

        void* mapped = null;
        _vk.MapMemory(_device, _readbackBufferMemory, 0, _readbackBufferSize, 0, &mapped);

        var floatData = (float*)mapped;
        int pixelCount = _width * _height;
        for (int i = 0; i < pixelCount; i++)
        {
            int src = i * 4;
            int dst = i * 4;
            rgbaBuffer[dst] = ToByte(floatData[src]);
            rgbaBuffer[dst + 1] = ToByte(floatData[src + 1]);
            rgbaBuffer[dst + 2] = ToByte(floatData[src + 2]);
            rgbaBuffer[dst + 3] = ToByte(floatData[src + 3]);
        }


        _vk.UnmapMemory(_device, _readbackBufferMemory);

        if (RaytracerSettings.Accumulate)
        {
            _frameIndex++;
        }
        else
        {
            _frameIndex = 0;
        }

        return;
    }

    private void RecordComputeCommands(VkBuffer? readbackBuffer = null)
    {
        _vk!.ResetCommandBuffer(_computeCommandBuffer, 0);

        var beginInfo = new CommandBufferBeginInfo { SType = StructureType.CommandBufferBeginInfo };
        _vk.BeginCommandBuffer(_computeCommandBuffer, &beginInfo);

        // Transition output image to general layout for compute shader
        if (_outputImageLayout != ImageLayout.General)
        {
            TransitionImageLayout(_computeCommandBuffer, _outputImage, _outputImageLayout, ImageLayout.General);
            _outputImageLayout = ImageLayout.General;
        }

        if (!_accumInitialized)
        {
            TransitionImageLayout(_computeCommandBuffer, _accumImage, ImageLayout.Undefined, ImageLayout.General);
            _accumInitialized = true;
        }

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
        _outputImageLayout = ImageLayout.TransferSrcOptimal;

        if (readbackBuffer.HasValue)
        {
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
                ImageExtent = new Extent3D((uint)_width, (uint)_height, 1)
            };

            _vk.CmdCopyImageToBuffer(_computeCommandBuffer, _outputImage, ImageLayout.TransferSrcOptimal, readbackBuffer.Value, 1, &region);
            TransitionImageLayout(_computeCommandBuffer, _outputImage, ImageLayout.TransferSrcOptimal, ImageLayout.General);
            _outputImageLayout = ImageLayout.General;
        }

        _vk.EndCommandBuffer(_computeCommandBuffer);
    }

    private void UpdateAccumulationState(Camera camera)
    {
        int settingsHash = HashCode.Combine(
            RaytracerSettings.RaysPerPixel,
            RaytracerSettings.MaxBounces,
            RaytracerSettings.SamplesPerFrame,
            RaytracerSettings.Accumulate,
            RaytracerSettings.Denoise,
            _bvhNodeCount,
            _triangleCount);

        bool cameraChanged = camera.Position != _lastCameraPos ||
                             camera.Forward != _lastCameraForward ||
                             Math.Abs(camera.FieldOfView - _lastCameraFov) > 0.001f;

        if (RaytracerSettings.ResetAccumulation || cameraChanged || settingsHash != _lastSettingsHash || !RaytracerSettings.Accumulate)
        {
            _frameIndex = 0;
            _accumInitialized = false;
            RaytracerSettings.ResetAccumulation = false;
        }

        _lastCameraPos = camera.Position;
        _lastCameraForward = camera.Forward;
        _lastCameraFov = camera.FieldOfView;
        _lastSettingsHash = settingsHash;
    }

    private void RecordGraphicsCommands(uint imageIndex, Camera camera, float time, DebugDrawManager? debugDraw, ParticlePool? particles)
    {
        _vk!.ResetCommandBuffer(_graphicsCommandBuffer, 0);

        var beginInfo = new CommandBufferBeginInfo { SType = StructureType.CommandBufferBeginInfo };
        _vk.BeginCommandBuffer(_graphicsCommandBuffer, &beginInfo);

        if (RenderSettings.Mode == RenderMode.Rasterized)
        {
            var clearValues = stackalloc ClearValue[]
            {
                new ClearValue { Color = new ClearColorValue(0.02f, 0.02f, 0.03f, 1f) },
                new ClearValue { DepthStencil = new ClearDepthStencilValue(1.0f, 0) }
            };

            var renderPassInfo = new RenderPassBeginInfo
            {
                SType = StructureType.RenderPassBeginInfo,
                RenderPass = _rasterRenderPass,
                Framebuffer = _rasterFramebuffers[imageIndex],
                RenderArea = new Rect2D
                {
                    Offset = new Offset2D(0, 0),
                    Extent = _swapchainExtent
                },
                ClearValueCount = 2,
                PClearValues = clearValues
            };

            _vk.CmdBeginRenderPass(_graphicsCommandBuffer, &renderPassInfo, SubpassContents.Inline);
            _vk.CmdBindPipeline(_graphicsCommandBuffer, PipelineBindPoint.Graphics, _rasterPipeline);

            var viewport = new Viewport
            {
                X = 0,
                Y = 0,
                Width = _swapchainExtent.Width,
                Height = _swapchainExtent.Height,
                MinDepth = 0f,
                MaxDepth = 1f
            };
            _vk.CmdSetViewport(_graphicsCommandBuffer, 0, 1, &viewport);

            var scissor = new Rect2D
            {
                Offset = new Offset2D(0, 0),
                Extent = _swapchainExtent
            };
            _vk.CmdSetScissor(_graphicsCommandBuffer, 0, 1, &scissor);

            if (_rasterIndexCount > 0)
            {
                ulong offset = 0;
                fixed (VkBuffer* vertexBufferPtr = &_rasterVertexBuffer)
                {
                    _vk.CmdBindVertexBuffers(_graphicsCommandBuffer, 0, 1, vertexBufferPtr, &offset);
                }
                _vk.CmdBindIndexBuffer(_graphicsCommandBuffer, _rasterIndexBuffer, 0, IndexType.Uint32);

                float aspectRaster = (float)_width / _height;
                var viewRaster = camera.GetViewMatrix();
                var projectionRaster = Matrix4x4.CreatePerspectiveFieldOfView(
                    camera.FieldOfView * MathF.PI / 180f,
                    aspectRaster,
                    0.1f,
                    10000f);
                
                // Flip Y for Vulkan's coordinate system (Y points down in clip space)
                projectionRaster.M22 *= -1;

                var viewProj = viewRaster * projectionRaster;
                void* viewProjPtr = Unsafe.AsPointer(ref viewProj);
                _vk.CmdPushConstants(_graphicsCommandBuffer, _rasterPipelineLayout, ShaderStageFlags.VertexBit, 0, (uint)Unsafe.SizeOf<Matrix4x4>(), viewProjPtr);

                _vk.CmdDrawIndexed(_graphicsCommandBuffer, (uint)_rasterIndexCount, 1, 0, 0, 0);
            }

            _vk.CmdEndRenderPass(_graphicsCommandBuffer);
        }
        else
        {
            if (_outputImageLayout != ImageLayout.TransferSrcOptimal)
            {
                TransitionImageLayout(_graphicsCommandBuffer, _outputImage, _outputImageLayout, ImageLayout.TransferSrcOptimal);
                _outputImageLayout = ImageLayout.TransferSrcOptimal;
            }
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

            // Transition to present layout
            TransitionImageLayout(_graphicsCommandBuffer, _swapchainImages[imageIndex], ImageLayout.TransferDstOptimal, ImageLayout.PresentSrcKhr);
        }
        
        // Build view and projection matrices from camera
        float aspect = (float)_width / _height;
        var view = camera.GetViewMatrix();
        var projection = Matrix4x4.CreatePerspectiveFieldOfView(
            camera.FieldOfView * MathF.PI / 180f,
            aspect,
            0.1f,
            10000f);
        
        // Render particles (before debug so debug draws on top)
        if (particles != null && particles.AliveCount > 0 && _particleRenderer?.IsInitialized == true)
        {
            _particleRenderer.UpdateParticles(particles);
            _particleRenderer.UpdateUniforms(view, projection, camera.Position, time);
            _particleRenderer.RecordCommands(_graphicsCommandBuffer, imageIndex);
        }

        // Render debug overlay
        if (debugDraw != null && debugDraw.HasContent && _debugRenderer?.IsInitialized == true)
        {
            _debugRenderer.RecordCommands(_graphicsCommandBuffer, imageIndex, debugDraw, view, projection);
        }

        if (_uiRenderer?.IsInitialized == true && _uiDrawData.VertexCount > 0)
        {
            _uiRenderer.RecordCommands(_graphicsCommandBuffer, imageIndex, _uiDrawData);
        }

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
        else if (oldLayout == ImageLayout.TransferSrcOptimal && newLayout == ImageLayout.General)
        {
            barrier.SrcAccessMask = AccessFlags.TransferReadBit;
            barrier.DstAccessMask = AccessFlags.ShaderWriteBit;
            srcStage = PipelineStageFlags.TransferBit;
            dstStage = PipelineStageFlags.ComputeShaderBit;
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
        else if (oldLayout == ImageLayout.TransferDstOptimal && newLayout == ImageLayout.ColorAttachmentOptimal)
        {
            barrier.SrcAccessMask = AccessFlags.TransferWriteBit;
            barrier.DstAccessMask = AccessFlags.ColorAttachmentWriteBit;
            srcStage = PipelineStageFlags.TransferBit;
            dstStage = PipelineStageFlags.ColorAttachmentOutputBit;
        }
        else if (oldLayout == ImageLayout.ColorAttachmentOptimal && newLayout == ImageLayout.PresentSrcKhr)
        {
            barrier.SrcAccessMask = AccessFlags.ColorAttachmentWriteBit;
            barrier.DstAccessMask = 0;
            srcStage = PipelineStageFlags.ColorAttachmentOutputBit;
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

    private void SubmitGraphics(bool waitForCompute)
    {
        var signalSemaphore = _renderFinishedSemaphore;

        fixed (CommandBuffer* cmd = &_graphicsCommandBuffer)
        {
            SubmitInfo submitInfo;
            
            if (waitForCompute)
            {
                // Raytraced mode: wait for compute shader to finish
                var waitSemaphore = _computeFinishedSemaphore;
                var waitStage = PipelineStageFlags.TransferBit;
                
                submitInfo = new SubmitInfo
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
            }
            else
            {
                // Rasterized mode: wait for image acquisition only
                var waitSemaphore = _imageAvailableSemaphore;
                var waitStage = PipelineStageFlags.ColorAttachmentOutputBit;
                
                submitInfo = new SubmitInfo
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
            }

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
        if (_vk == null)
            return;

        if (_device.Handle != 0)
        {
            _vk.DeviceWaitIdle(_device);
        }

        _particleRenderer?.Dispose();
        _debugRenderer?.Dispose();
        _uiRenderer?.Dispose();
        
        if (_device.Handle != 0)
        {
            _vk.DestroySemaphore(_device, _imageAvailableSemaphore, null);
            _vk.DestroySemaphore(_device, _renderFinishedSemaphore, null);
            _vk.DestroySemaphore(_device, _computeFinishedSemaphore, null);
            _vk.DestroyFence(_device, _inFlightFence, null);
            _vk.DestroyCommandPool(_device, _computeCommandPool, null);
            _vk.DestroyCommandPool(_device, _graphicsCommandPool, null);
        }
        if (_device.Handle != 0 && _rasterPipeline.Handle != 0)
        {
            _vk.DestroyPipeline(_device, _rasterPipeline, null);
        }
        if (_device.Handle != 0 && _rasterPipelineLayout.Handle != 0)
        {
            _vk.DestroyPipelineLayout(_device, _rasterPipelineLayout, null);
        }
        if (_device.Handle != 0 && _rasterRenderPass.Handle != 0)
        {
            _vk.DestroyRenderPass(_device, _rasterRenderPass, null);
        }
        if (_device.Handle != 0 && _overlayRenderPass.Handle != 0)
        {
            _vk.DestroyRenderPass(_device, _overlayRenderPass, null);
        }
        if (_device.Handle != 0 && _depthImageView.Handle != 0)
        {
            _vk.DestroyImageView(_device, _depthImageView, null);
        }
        if (_device.Handle != 0 && _depthImage.Handle != 0)
        {
            _vk.DestroyImage(_device, _depthImage, null);
            _vk.FreeMemory(_device, _depthImageMemory, null);
        }
        if (_device.Handle != 0 && _rasterFramebuffers.Length > 0)
        {
            foreach (var fb in _rasterFramebuffers)
            {
                _vk.DestroyFramebuffer(_device, fb, null);
            }
        }
        if (_device.Handle != 0 && _rasterVertexBuffer.Handle != 0)
        {
            _vk.DestroyBuffer(_device, _rasterVertexBuffer, null);
            _vk.FreeMemory(_device, _rasterVertexBufferMemory, null);
        }
        if (_device.Handle != 0 && _rasterIndexBuffer.Handle != 0)
        {
            _vk.DestroyBuffer(_device, _rasterIndexBuffer, null);
            _vk.FreeMemory(_device, _rasterIndexBufferMemory, null);
        }
        if (_device.Handle != 0)
        {
            _vk.UnmapMemory(_device, _uniformBufferMemory);
            _vk.DestroyBuffer(_device, _uniformBuffer, null);
            _vk.FreeMemory(_device, _uniformBufferMemory, null);
        }
        if (_device.Handle != 0 && _bvhNodeBuffer.Handle != 0)
        {
            _vk.DestroyBuffer(_device, _bvhNodeBuffer, null);
            _vk.FreeMemory(_device, _bvhNodeBufferMemory, null);
        }
        if (_device.Handle != 0 && _triangleBuffer.Handle != 0)
        {
            _vk.DestroyBuffer(_device, _triangleBuffer, null);
            _vk.FreeMemory(_device, _triangleBufferMemory, null);
        }
        if (_device.Handle != 0 && _readbackBuffer.Handle != 0)
        {
            _vk.DestroyBuffer(_device, _readbackBuffer, null);
            _vk.FreeMemory(_device, _readbackBufferMemory, null);
        }
        if (_device.Handle != 0)
        {
            _vk.DestroyDescriptorPool(_device, _descriptorPool, null);
            _vk.DestroyPipeline(_device, _computePipeline, null);
            _vk.DestroyPipelineLayout(_device, _computePipelineLayout, null);
            _vk.DestroyDescriptorSetLayout(_device, _computeDescriptorSetLayout, null);
            _vk.DestroyImageView(_device, _outputImageView, null);
            _vk.DestroyImage(_device, _outputImage, null);
            _vk.FreeMemory(_device, _outputImageMemory, null);
            _vk.DestroyImageView(_device, _accumImageView, null);
            _vk.DestroyImage(_device, _accumImage, null);
            _vk.FreeMemory(_device, _accumImageMemory, null);
        }

        if (_device.Handle != 0)
        {
            foreach (var imageView in _swapchainImageViews)
            {
                _vk.DestroyImageView(_device, imageView, null);
            }
        }

        if (_device.Handle != 0)
        {
            _khrSwapchain?.DestroySwapchain(_device, _swapchain, null);
            _vk.DestroyDevice(_device, null);
        }

        if (_instance.Handle != 0)
        {
            _khrSurface?.DestroySurface(_instance, _surface, null);
            _vk.DestroyInstance(_instance, null);
        }
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

[StructLayout(LayoutKind.Explicit, Size = 224)]
public struct RaytracerUniforms
{
    [FieldOffset(0)] public Vector2 Resolution;
    [FieldOffset(8)] public float Time;
    [FieldOffset(12)] public float Pad1;
    [FieldOffset(16)] public Vector3 CameraPos;
    [FieldOffset(28)] public float Pad2;
    [FieldOffset(32)] public Vector3 CameraForward;
    [FieldOffset(44)] public float Pad3;
    [FieldOffset(48)] public Vector3 CameraRight;
    [FieldOffset(60)] public float Pad4;
    [FieldOffset(64)] public Vector3 CameraUp;
    [FieldOffset(76)] public float Fov;
    [FieldOffset(80)] public Vector3 BlackHolePos;
    [FieldOffset(92)] public float BlackHoleMass;
    [FieldOffset(96)] public float SchwarzschildRadius;
    [FieldOffset(100)] public float DiskInnerRadius;
    [FieldOffset(104)] public float DiskOuterRadius;
    [FieldOffset(112)] public Vector4 RaySettings;
    [FieldOffset(128)] public Vector4 FrameSettings;
    [FieldOffset(144)] public Vector4 LensingSettings; // x=maxSteps, y=stepSize, z=bvhCheckInterval, w=maxDistance
    
    // Kerr black hole parameters
    [FieldOffset(160)] public float BlackHoleSpin;      // Dimensionless spin parameter a* (0 to ~1)
    [FieldOffset(164)] public float KerrParameter;      // a = a* × M (spin in length units)
    [FieldOffset(168)] public float OuterHorizonRadius; // r+ for Kerr
    [FieldOffset(172)] public float ErgosphereRadius;   // Equatorial ergosphere radius
    [FieldOffset(176)] public Vector3 BlackHoleSpinAxis; // Rotation axis (normalized)
    [FieldOffset(188)] public float ShowErgosphere;     // 1.0 = show, 0.0 = hide
    
    // Visualization settings
    [FieldOffset(192)] public float ErgosphereOpacity;  // 0.0 - 1.0
    [FieldOffset(196)] public float DiskISCO;           // Innermost Stable Circular Orbit radius
    [FieldOffset(200)] public float DiskThickness;      // Disk half-thickness at outer edge
    [FieldOffset(204)] public float ShowPhotonSphere;   // 1.0 = show, 0.0 = hide
    [FieldOffset(208)] public float PhotonSphereOpacity;// 0.0 - 1.0
    [FieldOffset(212)] public float PhotonSphereRadius; // Calculated from mass and spin
    [FieldOffset(216)] public float Pad9;
    [FieldOffset(220)] public float Pad10;
}
