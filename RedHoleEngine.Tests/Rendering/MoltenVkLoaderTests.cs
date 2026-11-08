using System.Runtime.InteropServices;
using RedHoleEngine.Rendering.Backends;
using Xunit;

namespace RedHoleEngine.Tests.Rendering;

public class MoltenVkLoaderTests
{
    [Fact]
    public void GetCandidatePaths_OrdersOverrideThenBaseDirThenRuntimes()
    {
        var paths = MoltenVkLoader.GetCandidatePaths("/app", "/custom/libMoltenVK.dylib");

        Assert.Equal("/custom/libMoltenVK.dylib", paths[0]);
        Assert.Equal(Path.Combine("/app", "libMoltenVK.dylib"), paths[1]);
        Assert.Contains(Path.Combine("/app", "runtimes", "osx-arm64", "native", "libMoltenVK.dylib"), paths);
        Assert.Contains(Path.Combine("/app", "runtimes", "osx", "native", "libMoltenVK.dylib"), paths);
    }

    [Fact]
    public void GetCandidatePaths_WithoutOverride_StartsAtBaseDirAndHasNoDuplicates()
    {
        var paths = MoltenVkLoader.GetCandidatePaths("/app", "  ");

        Assert.Equal(Path.Combine("/app", "libMoltenVK.dylib"), paths[0]);
        Assert.Equal(paths.Count, paths.Distinct().Count());
    }

    [Fact]
    public void FindLibrary_ReturnsFirstExisting_OrNull()
    {
        var target = Path.Combine("/app", "runtimes", "osx", "native", "libMoltenVK.dylib");
        Assert.Equal(target, MoltenVkLoader.FindLibrary("/app", null, p => p == target));
        Assert.Null(MoltenVkLoader.FindLibrary("/app", null, _ => false));
    }

    [Fact]
    public void BuiltOutput_ContainsMoltenVk_OnMacOS()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.OSX)) return;

        var found = MoltenVkLoader.FindLibrary(AppContext.BaseDirectory, null, File.Exists);
        Assert.NotNull(found);
    }
}
