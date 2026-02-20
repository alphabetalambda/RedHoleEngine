using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace RedHoleEngine.Platform;

/// <summary>
/// Detected hardware platform type
/// </summary>
public enum PlatformType
{
    /// <summary>Generic desktop PC (unknown tier)</summary>
    Desktop,
    
    /// <summary>Valve Steam Deck (AMD Van Gogh APU)</summary>
    SteamDeck,
    
    /// <summary>ASUS ROG Ally (AMD Z1/Z1 Extreme)</summary>
    ROGAlly,
    
    /// <summary>Lenovo Legion Go (AMD Z1 Extreme)</summary>
    LegionGo,
    
    /// <summary>AYA NEO handhelds (various AMD APUs)</summary>
    AyaNeo,
    
    /// <summary>OneXPlayer handhelds</summary>
    OneXPlayer,
    
    /// <summary>GPD Win handhelds</summary>
    GPDWin,
    
    /// <summary>Low-end desktop or laptop (integrated graphics, older hardware)</summary>
    LowEndPC,
    
    /// <summary>Mid-range desktop or laptop</summary>
    MidRangePC,
    
    /// <summary>High-end desktop (powerful discrete GPU)</summary>
    HighEndPC,
    
    /// <summary>Low-end gaming laptop (entry-level discrete GPU)</summary>
    GamingLaptopLowEnd,
    
    /// <summary>High-end gaming laptop (powerful mobile GPU)</summary>
    GamingLaptopHighEnd,
    
    /// <summary>Apple Silicon Mac (M1)</summary>
    AppleSiliconM1,
    
    /// <summary>Apple Silicon Mac (M2)</summary>
    AppleSiliconM2,
    
    /// <summary>Apple Silicon Mac (M3)</summary>
    AppleSiliconM3,
    
    /// <summary>Apple Silicon Mac (M4 or newer)</summary>
    AppleSiliconM4Plus,
    
    /// <summary>Generic handheld gaming PC</summary>
    GenericHandheld
}

/// <summary>
/// GPU vendor detection
/// </summary>
public enum GpuVendor
{
    Unknown,
    Nvidia,
    AMD,
    Intel,
    Apple,
    Qualcomm
}

/// <summary>
/// Detected GPU information
/// </summary>
public class GpuInfo
{
    public GpuVendor Vendor { get; init; }
    public string Name { get; init; } = "Unknown GPU";
    public int VramMB { get; init; }
    public bool IsIntegrated { get; init; }
    public bool IsAppleSilicon { get; init; }
    
    /// <summary>
    /// Estimated performance tier (0-100, higher is better)
    /// </summary>
    public int PerformanceTier { get; init; }
}

/// <summary>
/// Detects hardware platform and capabilities for automatic quality adjustment.
/// </summary>
public static partial class PlatformDetector
{
    private static PlatformType? _cachedPlatform;
    private static GpuInfo? _cachedGpuInfo;
    private static bool _detected;
    
    /// <summary>
    /// The detected platform type (cached after first detection)
    /// </summary>
    public static PlatformType CurrentPlatform
    {
        get
        {
            if (!_detected)
            {
                DetectAll();
            }
            return _cachedPlatform!.Value;
        }
    }
    
    /// <summary>
    /// Detected GPU information
    /// </summary>
    public static GpuInfo GpuInfo
    {
        get
        {
            if (!_detected)
            {
                DetectAll();
            }
            return _cachedGpuInfo!;
        }
    }
    
    /// <summary>Whether the current device is a Steam Deck</summary>
    public static bool IsSteamDeck => CurrentPlatform == PlatformType.SteamDeck;
    
    /// <summary>Whether the current device is an ASUS ROG Ally</summary>
    public static bool IsROGAlly => CurrentPlatform == PlatformType.ROGAlly;
    
    /// <summary>Whether the current device is a Lenovo Legion Go</summary>
    public static bool IsLegionGo => CurrentPlatform == PlatformType.LegionGo;
    
    /// <summary>Whether the current device is any handheld gaming PC</summary>
    public static bool IsHandheld => CurrentPlatform is 
        PlatformType.SteamDeck or 
        PlatformType.ROGAlly or 
        PlatformType.LegionGo or 
        PlatformType.AyaNeo or 
        PlatformType.OneXPlayer or 
        PlatformType.GPDWin or
        PlatformType.GenericHandheld;
    
    /// <summary>Whether the current device is an Apple Silicon Mac</summary>
    public static bool IsAppleSilicon => CurrentPlatform is 
        PlatformType.AppleSiliconM1 or 
        PlatformType.AppleSiliconM2 or 
        PlatformType.AppleSiliconM3 or 
        PlatformType.AppleSiliconM4Plus;
    
    /// <summary>Whether the current device is a gaming laptop</summary>
    public static bool IsGamingLaptop => CurrentPlatform is 
        PlatformType.GamingLaptopLowEnd or 
        PlatformType.GamingLaptopHighEnd;
    
    /// <summary>
    /// Force a specific platform (useful for testing or user override)
    /// </summary>
    public static void ForcePlatform(PlatformType platform)
    {
        _cachedPlatform = platform;
        _cachedGpuInfo ??= new GpuInfo { Vendor = GpuVendor.Unknown };
        _detected = true;
        Console.WriteLine($"[PlatformDetector] Platform forced to: {platform}");
    }
    
    /// <summary>
    /// Reset detection to re-detect on next access
    /// </summary>
    public static void ResetDetection()
    {
        _detected = false;
        _cachedPlatform = null;
        _cachedGpuInfo = null;
    }

    private static void DetectAll()
    {
        _cachedGpuInfo = DetectGpu();
        _cachedPlatform = DetectPlatform(_cachedGpuInfo);
        _detected = true;
    }

    private static PlatformType DetectPlatform(GpuInfo gpuInfo)
    {
        // PRIORITY 1: Handheld gaming devices (Steam Deck, ROG Ally, etc.)
        // These ALWAYS take priority over any other detection - even if GPU detection
        // would classify them differently. Handhelds need specific optimizations.
        var handheld = DetectHandheld();
        if (handheld != null)
        {
            Console.WriteLine($"[PlatformDetector] {GetPlatformDescription(handheld.Value)} (handheld override)");
            return handheld.Value;
        }
        
        // PRIORITY 2: Apple Silicon (macOS only)
        // Apple Silicon Macs have unique GPU architecture that doesn't fit standard tiers
        if (OperatingSystem.IsMacOS())
        {
            var applePlatform = DetectAppleSilicon();
            if (applePlatform != null)
            {
                Console.WriteLine($"[PlatformDetector] {GetPlatformDescription(applePlatform.Value)}");
                return applePlatform.Value;
            }
        }
        
        // PRIORITY 3: GPU-based classification for desktops and laptops
        bool isLaptop = DetectIsLaptop();
        var platform = ClassifyByGpuPerformance(gpuInfo, isLaptop);
        Console.WriteLine($"[PlatformDetector] {GetPlatformDescription(platform)}");
        return platform;
    }
    
    private static PlatformType? DetectHandheld()
    {
        // Handheld detection takes HIGHEST PRIORITY
        // These devices have specific hardware constraints and need tailored profiles
        // regardless of what GPU classification would say
        
        // Steam Deck - most common handheld, check first
        if (DetectSteamDeck())
            return PlatformType.SteamDeck;
        
        // ROG Ally - second most popular Windows handheld
        if (DetectROGAlly())
            return PlatformType.ROGAlly;
        
        // Legion Go (Windows)
        if (DetectLegionGo())
            return PlatformType.LegionGo;
        
        // AYA NEO
        if (DetectAyaNeo())
            return PlatformType.AyaNeo;
        
        // OneXPlayer
        if (DetectOneXPlayer())
            return PlatformType.OneXPlayer;
        
        // GPD Win
        if (DetectGPDWin())
            return PlatformType.GPDWin;
        
        return null;
    }

    #region Steam Deck Detection
    
    private static bool DetectSteamDeck()
    {
        if (!OperatingSystem.IsLinux())
            return false;

        // Method 1: Environment variables
        if (Environment.GetEnvironmentVariable("SteamDeck") == "1" ||
            Environment.GetEnvironmentVariable("STEAM_DECK") == "1")
            return true;

        // Method 2: DMI board vendor
        if (ReadDmiField("board_vendor")?.Contains("Valve", StringComparison.OrdinalIgnoreCase) == true)
            return true;

        // Method 3: Product name
        var productName = ReadDmiField("product_name");
        if (productName?.Contains("Jupiter", StringComparison.OrdinalIgnoreCase) == true ||
            productName?.Contains("Steam Deck", StringComparison.OrdinalIgnoreCase) == true)
            return true;

        // Method 4: CPU model
        var cpuInfo = ReadProcFile("cpuinfo");
        if (cpuInfo?.Contains("AMD Custom APU 0405", StringComparison.OrdinalIgnoreCase) == true ||
            cpuInfo?.Contains("VanGogh", StringComparison.OrdinalIgnoreCase) == true)
            return true;

        return false;
    }
    
    #endregion

    #region ROG Ally Detection
    
    private static bool DetectROGAlly()
    {
        // ROG Ally runs Windows
        if (!OperatingSystem.IsWindows())
            return false;

        // Check for ASUS ROG Ally specific identifiers
        var productName = GetWindowsProductName();
        if (productName?.Contains("ROG Ally", StringComparison.OrdinalIgnoreCase) == true)
            return true;
        
        // Check for AMD Z1 APU (ROG Ally uses Z1 or Z1 Extreme)
        var cpuName = GetWindowsCpuName();
        if (cpuName?.Contains("AMD Z1", StringComparison.OrdinalIgnoreCase) == true)
        {
            // Could be Legion Go too, check manufacturer
            var manufacturer = GetWindowsManufacturer();
            if (manufacturer?.Contains("ASUS", StringComparison.OrdinalIgnoreCase) == true)
                return true;
        }

        return false;
    }
    
    #endregion

    #region Legion Go Detection
    
    private static bool DetectLegionGo()
    {
        if (!OperatingSystem.IsWindows())
            return false;

        var productName = GetWindowsProductName();
        if (productName?.Contains("Legion Go", StringComparison.OrdinalIgnoreCase) == true)
            return true;
        
        var cpuName = GetWindowsCpuName();
        if (cpuName?.Contains("AMD Z1", StringComparison.OrdinalIgnoreCase) == true)
        {
            var manufacturer = GetWindowsManufacturer();
            if (manufacturer?.Contains("Lenovo", StringComparison.OrdinalIgnoreCase) == true)
                return true;
        }

        return false;
    }
    
    #endregion

    #region AYA NEO Detection
    
    private static bool DetectAyaNeo()
    {
        string? productName = null;
        string? manufacturer = null;
        
        if (OperatingSystem.IsWindows())
        {
            productName = GetWindowsProductName();
            manufacturer = GetWindowsManufacturer();
        }
        else if (OperatingSystem.IsLinux())
        {
            productName = ReadDmiField("product_name");
            manufacturer = ReadDmiField("board_vendor");
        }
        
        if (manufacturer?.Contains("AYANEO", StringComparison.OrdinalIgnoreCase) == true ||
            manufacturer?.Contains("AYA NEO", StringComparison.OrdinalIgnoreCase) == true)
            return true;
        
        if (productName?.Contains("AYANEO", StringComparison.OrdinalIgnoreCase) == true ||
            productName?.Contains("AYA NEO", StringComparison.OrdinalIgnoreCase) == true)
            return true;

        return false;
    }
    
    #endregion

    #region OneXPlayer Detection
    
    private static bool DetectOneXPlayer()
    {
        string? productName = null;
        string? manufacturer = null;
        
        if (OperatingSystem.IsWindows())
        {
            productName = GetWindowsProductName();
            manufacturer = GetWindowsManufacturer();
        }
        else if (OperatingSystem.IsLinux())
        {
            productName = ReadDmiField("product_name");
            manufacturer = ReadDmiField("board_vendor");
        }
        
        if (manufacturer?.Contains("OneXPlayer", StringComparison.OrdinalIgnoreCase) == true ||
            manufacturer?.Contains("ONE-NETBOOK", StringComparison.OrdinalIgnoreCase) == true)
            return true;
        
        if (productName?.Contains("OneXPlayer", StringComparison.OrdinalIgnoreCase) == true ||
            productName?.Contains("ONEXPLAYER", StringComparison.OrdinalIgnoreCase) == true)
            return true;

        return false;
    }
    
    #endregion

    #region GPD Win Detection
    
    private static bool DetectGPDWin()
    {
        string? productName = null;
        string? manufacturer = null;
        
        if (OperatingSystem.IsWindows())
        {
            productName = GetWindowsProductName();
            manufacturer = GetWindowsManufacturer();
        }
        else if (OperatingSystem.IsLinux())
        {
            productName = ReadDmiField("product_name");
            manufacturer = ReadDmiField("board_vendor");
        }
        
        if (manufacturer?.Contains("GPD", StringComparison.OrdinalIgnoreCase) == true)
            return true;
        
        if (productName?.Contains("GPD Win", StringComparison.OrdinalIgnoreCase) == true ||
            productName?.Contains("GPD WIN", StringComparison.OrdinalIgnoreCase) == true ||
            productName?.Contains("G1617", StringComparison.OrdinalIgnoreCase) == true || // GPD Win 4
            productName?.Contains("G1619", StringComparison.OrdinalIgnoreCase) == true)   // GPD Win Max 2
            return true;

        return false;
    }
    
    #endregion

    #region Apple Silicon Detection
    
    private static PlatformType? DetectAppleSilicon()
    {
        if (!OperatingSystem.IsMacOS())
            return null;

        try
        {
            // Check CPU brand string using sysctl
            var cpuBrand = GetMacCpuBrand();
            
            if (cpuBrand == null)
                return null;

            if (cpuBrand.Contains("Apple M4", StringComparison.OrdinalIgnoreCase))
                return PlatformType.AppleSiliconM4Plus;
            
            if (cpuBrand.Contains("Apple M3", StringComparison.OrdinalIgnoreCase))
                return PlatformType.AppleSiliconM3;
            
            if (cpuBrand.Contains("Apple M2", StringComparison.OrdinalIgnoreCase))
                return PlatformType.AppleSiliconM2;
            
            if (cpuBrand.Contains("Apple M1", StringComparison.OrdinalIgnoreCase))
                return PlatformType.AppleSiliconM1;
            
            // Generic Apple Silicon detection
            if (RuntimeInformation.ProcessArchitecture == Architecture.Arm64 && 
                cpuBrand.Contains("Apple", StringComparison.OrdinalIgnoreCase))
                return PlatformType.AppleSiliconM1; // Default to M1 for unknown Apple ARM
        }
        catch
        {
            // Ignore detection errors
        }

        return null;
    }
    
    private static string? GetMacCpuBrand()
    {
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "/usr/sbin/sysctl",
                Arguments = "-n machdep.cpu.brand_string",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            
            using var process = System.Diagnostics.Process.Start(psi);
            if (process == null) return null;
            
            var output = process.StandardOutput.ReadToEnd().Trim();
            process.WaitForExit(1000);
            
            return string.IsNullOrEmpty(output) ? null : output;
        }
        catch
        {
            return null;
        }
    }
    
    #endregion

    #region Laptop Detection
    
    private static bool DetectIsLaptop()
    {
        if (OperatingSystem.IsWindows())
        {
            return DetectIsLaptopWindows();
        }
        else if (OperatingSystem.IsLinux())
        {
            return DetectIsLaptopLinux();
        }
        else if (OperatingSystem.IsMacOS())
        {
            return DetectIsLaptopMacOS();
        }
        
        return false;
    }
    
    private static bool DetectIsLaptopWindows()
    {
        try
        {
            // Check chassis type via WMI (simplified check)
            // Chassis types: 8=Portable, 9=Laptop, 10=Notebook, 14=Sub Notebook
            var chassisType = GetWindowsChassisType();
            if (chassisType is 8 or 9 or 10 or 14 or 31) // 31 = Convertible
                return true;
            
            // Check if battery is present
            if (HasBattery())
                return true;
        }
        catch
        {
            // Ignore errors
        }
        
        return false;
    }
    
    private static bool DetectIsLaptopLinux()
    {
        try
        {
            // Check for battery
            if (Directory.Exists("/sys/class/power_supply"))
            {
                var supplies = Directory.GetDirectories("/sys/class/power_supply");
                foreach (var supply in supplies)
                {
                    var typePath = Path.Combine(supply, "type");
                    if (File.Exists(typePath))
                    {
                        var type = File.ReadAllText(typePath).Trim();
                        if (type.Equals("Battery", StringComparison.OrdinalIgnoreCase))
                            return true;
                    }
                }
            }
            
            // Check DMI chassis type
            var chassisType = ReadDmiField("chassis_type");
            if (int.TryParse(chassisType, out int chassisTypeInt))
            {
                if (chassisTypeInt is 8 or 9 or 10 or 14 or 31)
                    return true;
            }
        }
        catch
        {
            // Ignore errors
        }
        
        return false;
    }
    
    private static bool DetectIsLaptopMacOS()
    {
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "/usr/sbin/system_profiler",
                Arguments = "SPHardwareDataType",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            
            using var process = System.Diagnostics.Process.Start(psi);
            if (process == null) return false;
            
            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit(2000);
            
            // MacBook models are laptops
            return output.Contains("MacBook", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }
    
    #endregion

    #region GPU Detection
    
    private static GpuInfo DetectGpu()
    {
        if (OperatingSystem.IsWindows())
        {
            return DetectGpuWindows();
        }
        else if (OperatingSystem.IsLinux())
        {
            return DetectGpuLinux();
        }
        else if (OperatingSystem.IsMacOS())
        {
            return DetectGpuMacOS();
        }
        
        return new GpuInfo { Vendor = GpuVendor.Unknown };
    }
    
    private static GpuInfo DetectGpuWindows()
    {
        try
        {
            // Use DirectX/DXGI or registry to get GPU info
            // Simplified: check environment or registry
            var gpuName = Environment.GetEnvironmentVariable("GPU_NAME") ?? GetWindowsGpuName();
            
            if (gpuName != null)
            {
                return ParseGpuInfo(gpuName);
            }
        }
        catch
        {
            // Ignore errors
        }
        
        return new GpuInfo { Vendor = GpuVendor.Unknown };
    }
    
    private static GpuInfo DetectGpuLinux()
    {
        try
        {
            // Try lspci for GPU info
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "/usr/bin/lspci",
                Arguments = "-v",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            
            using var process = System.Diagnostics.Process.Start(psi);
            if (process != null)
            {
                var output = process.StandardOutput.ReadToEnd();
                process.WaitForExit(2000);
                
                // Find VGA compatible controller line
                var vgaMatch = Regex.Match(output, @"VGA compatible controller: (.+)$", RegexOptions.Multiline);
                if (vgaMatch.Success)
                {
                    return ParseGpuInfo(vgaMatch.Groups[1].Value);
                }
            }
        }
        catch
        {
            // Ignore errors
        }
        
        return new GpuInfo { Vendor = GpuVendor.Unknown };
    }
    
    private static GpuInfo DetectGpuMacOS()
    {
        try
        {
            var cpuBrand = GetMacCpuBrand();
            
            // Apple Silicon has integrated GPU
            if (cpuBrand?.Contains("Apple", StringComparison.OrdinalIgnoreCase) == true)
            {
                int tier = 50; // Base tier for Apple Silicon
                
                if (cpuBrand.Contains("M1 Pro") || cpuBrand.Contains("M1 Max"))
                    tier = 70;
                else if (cpuBrand.Contains("M2 Pro") || cpuBrand.Contains("M2 Max"))
                    tier = 75;
                else if (cpuBrand.Contains("M3 Pro") || cpuBrand.Contains("M3 Max"))
                    tier = 80;
                else if (cpuBrand.Contains("M4"))
                    tier = 85;
                else if (cpuBrand.Contains("Ultra"))
                    tier = 90;
                
                return new GpuInfo
                {
                    Vendor = GpuVendor.Apple,
                    Name = cpuBrand,
                    IsIntegrated = true,
                    IsAppleSilicon = true,
                    PerformanceTier = tier
                };
            }
        }
        catch
        {
            // Ignore errors
        }
        
        return new GpuInfo { Vendor = GpuVendor.Unknown };
    }
    
    private static GpuInfo ParseGpuInfo(string gpuName)
    {
        var vendor = GpuVendor.Unknown;
        bool isIntegrated = false;
        int tier = 30; // Default tier
        
        var nameLower = gpuName.ToLowerInvariant();
        
        // Detect vendor
        if (nameLower.Contains("nvidia") || nameLower.Contains("geforce") || nameLower.Contains("quadro") || nameLower.Contains("rtx") || nameLower.Contains("gtx"))
        {
            vendor = GpuVendor.Nvidia;
            tier = ClassifyNvidiaGpu(gpuName);
        }
        else if (nameLower.Contains("amd") || nameLower.Contains("radeon") || nameLower.Contains("rx "))
        {
            vendor = GpuVendor.AMD;
            tier = ClassifyAmdGpu(gpuName);
            isIntegrated = nameLower.Contains("vega") || nameLower.Contains("graphics") || 
                          nameLower.Contains("680m") || nameLower.Contains("780m");
        }
        else if (nameLower.Contains("intel"))
        {
            vendor = GpuVendor.Intel;
            tier = ClassifyIntelGpu(gpuName);
            isIntegrated = !nameLower.Contains("arc");
        }
        
        return new GpuInfo
        {
            Vendor = vendor,
            Name = gpuName,
            IsIntegrated = isIntegrated,
            PerformanceTier = tier
        };
    }
    
    private static int ClassifyNvidiaGpu(string name)
    {
        var nameLower = name.ToLowerInvariant();
        
        // RTX 40 series
        if (nameLower.Contains("4090")) return 100;
        if (nameLower.Contains("4080")) return 95;
        if (nameLower.Contains("4070 ti")) return 85;
        if (nameLower.Contains("4070")) return 80;
        if (nameLower.Contains("4060 ti")) return 70;
        if (nameLower.Contains("4060")) return 65;
        if (nameLower.Contains("4050")) return 55;
        
        // RTX 30 series
        if (nameLower.Contains("3090")) return 90;
        if (nameLower.Contains("3080")) return 85;
        if (nameLower.Contains("3070")) return 75;
        if (nameLower.Contains("3060 ti")) return 70;
        if (nameLower.Contains("3060")) return 60;
        if (nameLower.Contains("3050")) return 45;
        
        // RTX 20 series
        if (nameLower.Contains("2080 ti")) return 75;
        if (nameLower.Contains("2080")) return 70;
        if (nameLower.Contains("2070")) return 60;
        if (nameLower.Contains("2060")) return 50;
        
        // GTX 16 series
        if (nameLower.Contains("1660")) return 45;
        if (nameLower.Contains("1650")) return 35;
        
        // GTX 10 series
        if (nameLower.Contains("1080 ti")) return 60;
        if (nameLower.Contains("1080")) return 55;
        if (nameLower.Contains("1070")) return 50;
        if (nameLower.Contains("1060")) return 40;
        if (nameLower.Contains("1050")) return 30;
        
        // Mobile variants (generally lower)
        if (nameLower.Contains("mobile") || nameLower.Contains("laptop"))
            return 40;
        
        return 50; // Unknown NVIDIA
    }
    
    private static int ClassifyAmdGpu(string name)
    {
        var nameLower = name.ToLowerInvariant();
        
        // RX 7000 series
        if (nameLower.Contains("7900 xtx")) return 95;
        if (nameLower.Contains("7900 xt")) return 90;
        if (nameLower.Contains("7900 gre")) return 80;
        if (nameLower.Contains("7800 xt")) return 75;
        if (nameLower.Contains("7700 xt")) return 65;
        if (nameLower.Contains("7600")) return 55;
        
        // RX 6000 series
        if (nameLower.Contains("6950 xt")) return 85;
        if (nameLower.Contains("6900 xt")) return 80;
        if (nameLower.Contains("6800 xt")) return 75;
        if (nameLower.Contains("6800")) return 70;
        if (nameLower.Contains("6700 xt")) return 60;
        if (nameLower.Contains("6600 xt")) return 50;
        if (nameLower.Contains("6600")) return 45;
        if (nameLower.Contains("6500")) return 35;
        if (nameLower.Contains("6400")) return 30;
        
        // Integrated graphics (Ryzen APUs)
        if (nameLower.Contains("780m")) return 40;  // Ryzen 7000 series iGPU
        if (nameLower.Contains("680m")) return 35;  // Ryzen 6000 series iGPU
        if (nameLower.Contains("vega")) return 20;
        
        // Z1 APUs (handhelds)
        if (nameLower.Contains("z1 extreme")) return 40;
        if (nameLower.Contains("z1")) return 35;
        
        return 40; // Unknown AMD
    }
    
    private static int ClassifyIntelGpu(string name)
    {
        var nameLower = name.ToLowerInvariant();
        
        // Arc discrete GPUs
        if (nameLower.Contains("a770")) return 60;
        if (nameLower.Contains("a750")) return 55;
        if (nameLower.Contains("a580")) return 45;
        if (nameLower.Contains("a380")) return 35;
        if (nameLower.Contains("a310")) return 25;
        
        // Integrated graphics
        if (nameLower.Contains("iris xe")) return 25;
        if (nameLower.Contains("iris plus")) return 20;
        if (nameLower.Contains("uhd 7")) return 18;
        if (nameLower.Contains("uhd 6")) return 15;
        if (nameLower.Contains("uhd")) return 12;
        
        return 15; // Unknown Intel (assume integrated)
    }
    
    #endregion

    #region Platform Classification
    
    private static PlatformType ClassifyByGpuPerformance(GpuInfo gpuInfo, bool isLaptop)
    {
        int tier = gpuInfo.PerformanceTier;
        
        if (isLaptop && !gpuInfo.IsIntegrated)
        {
            // Gaming laptop with discrete GPU
            if (tier >= 60)
                return PlatformType.GamingLaptopHighEnd;
            if (tier >= 40)
                return PlatformType.GamingLaptopLowEnd;
        }
        
        // Desktop or laptop with integrated graphics
        if (tier >= 75)
            return PlatformType.HighEndPC;
        if (tier >= 45)
            return PlatformType.MidRangePC;
        if (tier >= 25)
            return PlatformType.LowEndPC;
        
        return PlatformType.Desktop; // Unknown, use default
    }
    
    #endregion

    #region Helper Methods
    
    private static string? ReadDmiField(string field)
    {
        try
        {
            var path = $"/sys/devices/virtual/dmi/id/{field}";
            if (File.Exists(path))
                return File.ReadAllText(path).Trim();
        }
        catch { }
        return null;
    }
    
    private static string? ReadProcFile(string file)
    {
        try
        {
            var path = $"/proc/{file}";
            if (File.Exists(path))
                return File.ReadAllText(path);
        }
        catch { }
        return null;
    }
    
    private static string? GetWindowsProductName()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\BIOS");
            return key?.GetValue("SystemProductName")?.ToString();
        }
        catch { }
        return null;
    }
    
    private static string? GetWindowsManufacturer()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\BIOS");
            return key?.GetValue("SystemManufacturer")?.ToString();
        }
        catch { }
        return null;
    }
    
    private static string? GetWindowsCpuName()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0");
            return key?.GetValue("ProcessorNameString")?.ToString();
        }
        catch { }
        return null;
    }
    
    private static string? GetWindowsGpuName()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}\0000");
            return key?.GetValue("DriverDesc")?.ToString();
        }
        catch { }
        return null;
    }
    
    private static int? GetWindowsChassisType()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\SystemInformation");
            var value = key?.GetValue("ChassisTypes");
            if (value is byte[] bytes && bytes.Length > 0)
                return bytes[0];
        }
        catch { }
        return null;
    }
    
    private static bool HasBattery()
    {
        try
        {
            // Simple check - look for battery in power supply
            if (OperatingSystem.IsWindows())
            {
                using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\ACPI\Enum");
                var count = key?.GetValue("Count");
                return count != null && (int)count > 0;
            }
        }
        catch { }
        return false;
    }
    
    #endregion

    #region Description
    
    /// <summary>
    /// Get a human-readable description of the detected platform
    /// </summary>
    public static string GetPlatformDescription()
    {
        return GetPlatformDescription(CurrentPlatform);
    }
    
    private static string GetPlatformDescription(PlatformType platform)
    {
        return platform switch
        {
            PlatformType.SteamDeck => "Steam Deck (AMD Van Gogh APU)",
            PlatformType.ROGAlly => "ASUS ROG Ally (AMD Z1 APU)",
            PlatformType.LegionGo => "Lenovo Legion Go (AMD Z1 Extreme)",
            PlatformType.AyaNeo => "AYA NEO Handheld",
            PlatformType.OneXPlayer => "OneXPlayer Handheld",
            PlatformType.GPDWin => "GPD Win Handheld",
            PlatformType.GenericHandheld => "Handheld Gaming PC",
            PlatformType.LowEndPC => "Low-End PC",
            PlatformType.MidRangePC => "Mid-Range PC",
            PlatformType.HighEndPC => "High-End PC",
            PlatformType.GamingLaptopLowEnd => "Gaming Laptop (Entry-Level)",
            PlatformType.GamingLaptopHighEnd => "Gaming Laptop (High-End)",
            PlatformType.AppleSiliconM1 => "Apple Silicon M1",
            PlatformType.AppleSiliconM2 => "Apple Silicon M2",
            PlatformType.AppleSiliconM3 => "Apple Silicon M3",
            PlatformType.AppleSiliconM4Plus => "Apple Silicon M4+",
            PlatformType.Desktop => "Desktop PC",
            _ => "Unknown Platform"
        };
    }
    
    #endregion
}
