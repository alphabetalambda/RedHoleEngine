using RedHoleEngine.Platform;
using Xunit.Abstractions;

namespace RedHoleEngine.Tests.Platform;

/// <summary>
/// Unit tests for PlatformDetector - tests platform detection and forcing.
/// </summary>
public class PlatformDetectorTests
{
    private readonly ITestOutputHelper _output;

    public PlatformDetectorTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void CurrentPlatform_ReturnsValidPlatformType()
    {
        PlatformDetector.ResetDetection();
        
        var platform = PlatformDetector.CurrentPlatform;
        
        Assert.True(Enum.IsDefined(typeof(PlatformType), platform));
        _output.WriteLine($"Detected platform: {platform}");
    }

    [Fact]
    public void CurrentPlatform_IsCached()
    {
        PlatformDetector.ResetDetection();
        
        var first = PlatformDetector.CurrentPlatform;
        var second = PlatformDetector.CurrentPlatform;
        
        Assert.Equal(first, second);
    }

    [Fact]
    public void GpuInfo_IsDetected()
    {
        PlatformDetector.ResetDetection();
        
        var gpuInfo = PlatformDetector.GpuInfo;
        
        Assert.NotNull(gpuInfo);
        _output.WriteLine($"GPU: {gpuInfo.Name} ({gpuInfo.Vendor}), Tier: {gpuInfo.PerformanceTier}");
    }

    [Fact]
    public void ForcePlatform_OverridesDetection()
    {
        PlatformDetector.ResetDetection();
        
        PlatformDetector.ForcePlatform(PlatformType.SteamDeck);
        
        Assert.Equal(PlatformType.SteamDeck, PlatformDetector.CurrentPlatform);
        Assert.True(PlatformDetector.IsSteamDeck);
        
        PlatformDetector.ResetDetection();
    }

    [Fact]
    public void ForcePlatform_Desktop_NotSteamDeck()
    {
        PlatformDetector.ResetDetection();
        
        PlatformDetector.ForcePlatform(PlatformType.Desktop);
        
        Assert.Equal(PlatformType.Desktop, PlatformDetector.CurrentPlatform);
        Assert.False(PlatformDetector.IsSteamDeck);
        
        PlatformDetector.ResetDetection();
    }

    [Fact]
    public void ResetDetection_ClearsCache()
    {
        PlatformDetector.ForcePlatform(PlatformType.SteamDeck);
        Assert.True(PlatformDetector.IsSteamDeck);
        
        PlatformDetector.ResetDetection();
        
        var platform = PlatformDetector.CurrentPlatform;
        Assert.True(Enum.IsDefined(typeof(PlatformType), platform));
        
        _output.WriteLine($"After reset, detected: {platform}");
    }

    #region Platform Descriptions

    [Fact]
    public void GetPlatformDescription_SteamDeck_ContainsExpectedText()
    {
        PlatformDetector.ForcePlatform(PlatformType.SteamDeck);
        
        var description = PlatformDetector.GetPlatformDescription();
        
        Assert.Contains("Steam Deck", description);
        _output.WriteLine($"Steam Deck description: {description}");
        
        PlatformDetector.ResetDetection();
    }

    [Fact]
    public void GetPlatformDescription_ROGAlly_ContainsExpectedText()
    {
        PlatformDetector.ForcePlatform(PlatformType.ROGAlly);
        
        var description = PlatformDetector.GetPlatformDescription();
        
        Assert.Contains("ROG Ally", description);
        _output.WriteLine($"ROG Ally description: {description}");
        
        PlatformDetector.ResetDetection();
    }

    [Theory]
    [InlineData(PlatformType.Desktop)]
    [InlineData(PlatformType.SteamDeck)]
    [InlineData(PlatformType.ROGAlly)]
    [InlineData(PlatformType.LegionGo)]
    [InlineData(PlatformType.AyaNeo)]
    [InlineData(PlatformType.OneXPlayer)]
    [InlineData(PlatformType.GPDWin)]
    [InlineData(PlatformType.LowEndPC)]
    [InlineData(PlatformType.MidRangePC)]
    [InlineData(PlatformType.HighEndPC)]
    [InlineData(PlatformType.GamingLaptopLowEnd)]
    [InlineData(PlatformType.GamingLaptopHighEnd)]
    [InlineData(PlatformType.AppleSiliconM1)]
    [InlineData(PlatformType.AppleSiliconM2)]
    [InlineData(PlatformType.AppleSiliconM3)]
    [InlineData(PlatformType.AppleSiliconM4Plus)]
    [InlineData(PlatformType.GenericHandheld)]
    public void GetPlatformDescription_AllTypes_ReturnNonEmpty(PlatformType type)
    {
        PlatformDetector.ForcePlatform(type);
        
        var description = PlatformDetector.GetPlatformDescription();
        
        Assert.NotEmpty(description);
        _output.WriteLine($"{type}: {description}");
        
        PlatformDetector.ResetDetection();
    }

    #endregion

    #region Handheld Detection Helpers

    [Fact]
    public void IsHandheld_SteamDeck_ReturnsTrue()
    {
        PlatformDetector.ForcePlatform(PlatformType.SteamDeck);
        Assert.True(PlatformDetector.IsHandheld);
        PlatformDetector.ResetDetection();
    }

    [Fact]
    public void IsHandheld_ROGAlly_ReturnsTrue()
    {
        PlatformDetector.ForcePlatform(PlatformType.ROGAlly);
        Assert.True(PlatformDetector.IsHandheld);
        PlatformDetector.ResetDetection();
    }

    [Fact]
    public void IsHandheld_LegionGo_ReturnsTrue()
    {
        PlatformDetector.ForcePlatform(PlatformType.LegionGo);
        Assert.True(PlatformDetector.IsHandheld);
        PlatformDetector.ResetDetection();
    }

    [Theory]
    [InlineData(PlatformType.AyaNeo)]
    [InlineData(PlatformType.OneXPlayer)]
    [InlineData(PlatformType.GPDWin)]
    [InlineData(PlatformType.GenericHandheld)]
    public void IsHandheld_OtherHandhelds_ReturnsTrue(PlatformType type)
    {
        PlatformDetector.ForcePlatform(type);
        Assert.True(PlatformDetector.IsHandheld);
        PlatformDetector.ResetDetection();
    }

    [Theory]
    [InlineData(PlatformType.Desktop)]
    [InlineData(PlatformType.HighEndPC)]
    [InlineData(PlatformType.GamingLaptopHighEnd)]
    public void IsHandheld_NonHandheld_ReturnsFalse(PlatformType type)
    {
        PlatformDetector.ForcePlatform(type);
        Assert.False(PlatformDetector.IsHandheld);
        PlatformDetector.ResetDetection();
    }

    #endregion

    #region Apple Silicon Detection Helpers

    [Theory]
    [InlineData(PlatformType.AppleSiliconM1)]
    [InlineData(PlatformType.AppleSiliconM2)]
    [InlineData(PlatformType.AppleSiliconM3)]
    [InlineData(PlatformType.AppleSiliconM4Plus)]
    public void IsAppleSilicon_AppleMacs_ReturnsTrue(PlatformType type)
    {
        PlatformDetector.ForcePlatform(type);
        Assert.True(PlatformDetector.IsAppleSilicon);
        PlatformDetector.ResetDetection();
    }

    [Theory]
    [InlineData(PlatformType.Desktop)]
    [InlineData(PlatformType.SteamDeck)]
    [InlineData(PlatformType.HighEndPC)]
    public void IsAppleSilicon_NonApple_ReturnsFalse(PlatformType type)
    {
        PlatformDetector.ForcePlatform(type);
        Assert.False(PlatformDetector.IsAppleSilicon);
        PlatformDetector.ResetDetection();
    }

    #endregion

    #region Gaming Laptop Detection Helpers

    [Theory]
    [InlineData(PlatformType.GamingLaptopLowEnd)]
    [InlineData(PlatformType.GamingLaptopHighEnd)]
    public void IsGamingLaptop_GamingLaptops_ReturnsTrue(PlatformType type)
    {
        PlatformDetector.ForcePlatform(type);
        Assert.True(PlatformDetector.IsGamingLaptop);
        PlatformDetector.ResetDetection();
    }

    [Theory]
    [InlineData(PlatformType.Desktop)]
    [InlineData(PlatformType.SteamDeck)]
    [InlineData(PlatformType.HighEndPC)]
    [InlineData(PlatformType.LowEndPC)]
    public void IsGamingLaptop_NonGamingLaptop_ReturnsFalse(PlatformType type)
    {
        PlatformDetector.ForcePlatform(type);
        Assert.False(PlatformDetector.IsGamingLaptop);
        PlatformDetector.ResetDetection();
    }

    #endregion

    #region Platform Count and Enum Validation

    [Fact]
    public void PlatformType_HasExpectedPlatforms()
    {
        var types = Enum.GetValues<PlatformType>();
        
        // Core platforms
        Assert.Contains(PlatformType.Desktop, types);
        Assert.Contains(PlatformType.SteamDeck, types);
        Assert.Contains(PlatformType.ROGAlly, types);
        Assert.Contains(PlatformType.LegionGo, types);
        
        // Other handhelds
        Assert.Contains(PlatformType.AyaNeo, types);
        Assert.Contains(PlatformType.OneXPlayer, types);
        Assert.Contains(PlatformType.GPDWin, types);
        Assert.Contains(PlatformType.GenericHandheld, types);
        
        // Desktop tiers
        Assert.Contains(PlatformType.LowEndPC, types);
        Assert.Contains(PlatformType.MidRangePC, types);
        Assert.Contains(PlatformType.HighEndPC, types);
        
        // Gaming laptops
        Assert.Contains(PlatformType.GamingLaptopLowEnd, types);
        Assert.Contains(PlatformType.GamingLaptopHighEnd, types);
        
        // Apple Silicon
        Assert.Contains(PlatformType.AppleSiliconM1, types);
        Assert.Contains(PlatformType.AppleSiliconM2, types);
        Assert.Contains(PlatformType.AppleSiliconM3, types);
        Assert.Contains(PlatformType.AppleSiliconM4Plus, types);
        
        Assert.Equal(17, types.Length);
        _output.WriteLine($"Total platform types: {types.Length}");
    }

    #endregion

    #region OS-Specific Detection

    [Fact]
    public void OnNonLinux_NotSteamDeck()
    {
        PlatformDetector.ResetDetection();
        
        if (!OperatingSystem.IsLinux())
        {
            Assert.False(PlatformDetector.IsSteamDeck);
            _output.WriteLine("Running on non-Linux - Steam Deck detection correctly returns false");
        }
        else
        {
            _output.WriteLine("Running on Linux - Steam Deck detection depends on hardware");
        }
    }

    [Fact]
    public void OnNonWindows_NotROGAlly()
    {
        PlatformDetector.ResetDetection();
        
        if (!OperatingSystem.IsWindows())
        {
            Assert.False(PlatformDetector.IsROGAlly);
            _output.WriteLine("Running on non-Windows - ROG Ally detection correctly returns false");
        }
        else
        {
            _output.WriteLine("Running on Windows - ROG Ally detection depends on hardware");
        }
    }

    [Fact]
    public void OnNonMacOS_NotAppleSilicon()
    {
        PlatformDetector.ResetDetection();
        
        if (!OperatingSystem.IsMacOS())
        {
            Assert.False(PlatformDetector.IsAppleSilicon);
            _output.WriteLine("Running on non-macOS - Apple Silicon detection correctly returns false");
        }
        else
        {
            _output.WriteLine("Running on macOS - Apple Silicon detection depends on hardware");
        }
    }

    #endregion
}
