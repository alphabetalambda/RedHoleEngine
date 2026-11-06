using System.Runtime.InteropServices;
using RedHoleEngine.Platform;

namespace RedHoleEngine.Tests.Platform;

[Collection("PlatformDetector")]
public class PlatformDetectorCoverageTests
{
    [Fact]
    public void LinuxDetection_RejectsWindowsAndAppleOnlyPlatforms()
    {
        PlatformDetector.HostOsOverride = OSPlatform.Linux;
        PlatformDetector.ResetDetection();
        try
        {
            var platform = PlatformDetector.CurrentPlatform;
            var gpu = PlatformDetector.GpuInfo;

            Assert.True(Enum.IsDefined(platform));
            Assert.NotEqual(PlatformType.ROGAlly, platform);
            Assert.NotEqual(PlatformType.LegionGo, platform);
            Assert.False(PlatformDetector.IsROGAlly);
            Assert.False(PlatformDetector.IsLegionGo);
            Assert.False(PlatformDetector.IsAppleSilicon);
            Assert.False(gpu.IsAppleSilicon);
            Assert.False(string.IsNullOrWhiteSpace(gpu.Name));
            Assert.True(Enum.IsDefined(gpu.Vendor));
            Assert.InRange(gpu.PerformanceTier, 0, 100);
            Assert.False(string.IsNullOrWhiteSpace(PlatformDetector.GetPlatformDescription()));

            if (!LooksLikeSteamDeckHardware())
                Assert.NotEqual(PlatformType.SteamDeck, platform);
        }
        finally
        {
            PlatformDetector.HostOsOverride = null;
            PlatformDetector.ResetDetection();
        }
    }

    [Fact]
    public void SteamDeckEnvironment_OverridesLinuxDetection()
    {
        var previousSteamDeck = Environment.GetEnvironmentVariable("SteamDeck");
        var previousUpper = Environment.GetEnvironmentVariable("STEAM_DECK");
        try
        {
            PlatformDetector.HostOsOverride = OSPlatform.Linux;
            Environment.SetEnvironmentVariable("SteamDeck", null);
            Environment.SetEnvironmentVariable("STEAM_DECK", "1");
            PlatformDetector.ResetDetection();

            Assert.Equal(PlatformType.SteamDeck, PlatformDetector.CurrentPlatform);
            Assert.True(PlatformDetector.IsSteamDeck);
            Assert.True(PlatformDetector.IsHandheld);
            Assert.Contains("Steam Deck", PlatformDetector.GetPlatformDescription());
        }
        finally
        {
            PlatformDetector.HostOsOverride = null;
            Environment.SetEnvironmentVariable("SteamDeck", previousSteamDeck);
            Environment.SetEnvironmentVariable("STEAM_DECK", previousUpper);
            PlatformDetector.ResetDetection();
        }
    }

    [Fact]
    public void ForcePlatform_PreservesDetectedGpu_AndUnknownValueDescribesItself()
    {
        PlatformDetector.ResetDetection();
        try
        {
            var detected = PlatformDetector.GpuInfo;
            PlatformDetector.ForcePlatform(PlatformType.HighEndPC);
            Assert.Equal(PlatformType.HighEndPC, PlatformDetector.CurrentPlatform);
            Assert.Equal(detected.Name, PlatformDetector.GpuInfo.Name);
            Assert.Equal(detected.Vendor, PlatformDetector.GpuInfo.Vendor);
            Assert.False(PlatformDetector.IsHandheld);
            Assert.False(PlatformDetector.IsGamingLaptop);

            PlatformDetector.ResetDetection();
            PlatformDetector.ForcePlatform((PlatformType)999);
            Assert.Equal("Unknown Platform", PlatformDetector.GetPlatformDescription());
            Assert.Equal(GpuVendor.Unknown, PlatformDetector.GpuInfo.Vendor);
            Assert.Equal("Unknown GPU", PlatformDetector.GpuInfo.Name);
        }
        finally
        {
            PlatformDetector.ResetDetection();
        }
    }

    [Theory]
    [InlineData(PlatformType.LowEndPC, false, false, false)]
    [InlineData(PlatformType.MidRangePC, false, false, false)]
    [InlineData(PlatformType.GamingLaptopLowEnd, false, true, false)]
    [InlineData(PlatformType.AppleSiliconM2, false, false, true)]
    [InlineData(PlatformType.GenericHandheld, true, false, false)]
    public void ForcedFlags_FillHandheldLaptopAndAppleGaps(PlatformType type, bool handheld, bool laptop, bool apple)
    {
        try
        {
            PlatformDetector.ForcePlatform(type);
            Assert.Equal(handheld, PlatformDetector.IsHandheld);
            Assert.Equal(laptop, PlatformDetector.IsGamingLaptop);
            Assert.Equal(apple, PlatformDetector.IsAppleSilicon);
            Assert.False(string.IsNullOrWhiteSpace(PlatformDetector.GetPlatformDescription()));
        }
        finally
        {
            PlatformDetector.ResetDetection();
        }
    }

    private static bool LooksLikeSteamDeckHardware()
    {
        if (Environment.GetEnvironmentVariable("SteamDeck") == "1" ||
            Environment.GetEnvironmentVariable("STEAM_DECK") == "1")
            return true;

        if (ReadText("/sys/devices/virtual/dmi/id/board_vendor").Contains("Valve", StringComparison.OrdinalIgnoreCase))
            return true;

        var product = ReadText("/sys/devices/virtual/dmi/id/product_name");
        if (product.Contains("Jupiter", StringComparison.OrdinalIgnoreCase) ||
            product.Contains("Steam Deck", StringComparison.OrdinalIgnoreCase))
            return true;

        var cpu = ReadText("/proc/cpuinfo");
        return cpu.Contains("AMD Custom APU 0405", StringComparison.OrdinalIgnoreCase) ||
               cpu.Contains("VanGogh", StringComparison.OrdinalIgnoreCase);
    }

    private static string ReadText(string path)
    {
        try
        {
            return File.Exists(path) ? File.ReadAllText(path) : string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }
}

[CollectionDefinition("PlatformDetector", DisableParallelization = true)]
public class PlatformDetectorCollection { }
