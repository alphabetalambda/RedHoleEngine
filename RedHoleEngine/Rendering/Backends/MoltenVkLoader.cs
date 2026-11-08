using System.Runtime.InteropServices;
using Silk.NET.Core.Contexts;
using Silk.NET.Vulkan;

namespace RedHoleEngine.Rendering.Backends;

/// <summary>
/// On macOS, loads the MoltenVK dylib that ships with the Silk.NET.MoltenVK.Native package
/// and uses it directly as the Vulkan loader (MoltenVK exports vkGetInstanceProcAddr and the
/// rest of the core entry points). This removes the need for Homebrew's vulkan-loader or a
/// system-wide MoltenVK install. Other platforms use the default Silk.NET loader unchanged.
/// </summary>
public static class MoltenVkLoader
{
    public const string LibraryFileName = "libMoltenVK.dylib";
    public const string OverrideEnvVar = "REDHOLE_MOLTENVK_PATH";

    public const string GlfwFileName = "libglfw.3.dylib";

    private static IntPtr _handle;
    private static bool _glfwConfigured;

    private static IntPtr LoadMoltenVk()
    {
        if (_handle != IntPtr.Zero) return _handle;

        var path = FindLibrary(AppContext.BaseDirectory,
            Environment.GetEnvironmentVariable(OverrideEnvVar), File.Exists);
        if (path == null)
        {
            Console.WriteLine($"[MoltenVkLoader] {LibraryFileName} not found under '{AppContext.BaseDirectory}'; " +
                              "falling back to the system Vulkan loader (libvulkan), if any.");
            return IntPtr.Zero;
        }
        if (!NativeLibrary.TryLoad(path, out var handle))
        {
            Console.WriteLine($"[MoltenVkLoader] Failed to load '{path}'; falling back to the system Vulkan loader.");
            return IntPtr.Zero;
        }
        Console.WriteLine($"[MoltenVkLoader] Using bundled MoltenVK as Vulkan loader: {path}");
        _handle = handle;
        return handle;
    }

    /// <summary>
    /// GLFW has its own Vulkan loader lookup (libvulkan.1.dylib) and reports "doesn't support Vulkan"
    /// when none is installed. GLFW 3.4 lets us hand it vkGetInstanceProcAddr via glfwInitVulkanLoader,
    /// which must be called before glfwInit. Call this before creating the first window. No-op off macOS.
    /// </summary>
    public static unsafe void ConfigureGlfw()
    {
        if (_glfwConfigured || !RuntimeInformation.IsOSPlatform(OSPlatform.OSX)) return;
        _glfwConfigured = true;

        try
        {
            var mvk = LoadMoltenVk();
            if (mvk == IntPtr.Zero) return;
            if (!NativeLibrary.TryGetExport(mvk, "vkGetInstanceProcAddr", out var getProc)) return;

            var glfwPath = FindLibrary(AppContext.BaseDirectory, null, File.Exists, GlfwFileName);
            if (glfwPath == null || !NativeLibrary.TryLoad(glfwPath, out var glfw)) return;
            if (!NativeLibrary.TryGetExport(glfw, "glfwInitVulkanLoader", out var init))
            {
                Console.WriteLine("[MoltenVkLoader] GLFW build has no glfwInitVulkanLoader (needs 3.4+).");
                return;
            }

            ((delegate* unmanaged<IntPtr, void>)init)(getProc);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[MoltenVkLoader] Could not configure GLFW Vulkan loader: {ex.Message}");
        }
    }

    /// <summary>
    /// Pure candidate-path logic. Returns paths to probe, in priority order.
    /// </summary>
    public static IReadOnlyList<string> GetCandidatePaths(string baseDirectory, string? overridePath = null,
        string fileName = LibraryFileName)
    {
        var result = new List<string>();
        void Add(string p)
        {
            if (!string.IsNullOrWhiteSpace(p) && !result.Contains(p)) result.Add(p);
        }

        if (!string.IsNullOrWhiteSpace(overridePath)) Add(overridePath!);

        Add(Path.Combine(baseDirectory, fileName));
        Add(Path.Combine(baseDirectory, "runtimes", "osx-arm64", "native", fileName));
        Add(Path.Combine(baseDirectory, "runtimes", "osx-x64", "native", fileName));
        Add(Path.Combine(baseDirectory, "runtimes", "osx", "native", fileName));
        return result;
    }

    /// <summary>First candidate that exists according to <paramref name="exists"/>, or null.</summary>
    public static string? FindLibrary(string baseDirectory, string? overridePath, Func<string, bool> exists,
        string fileName = LibraryFileName)
    {
        foreach (var candidate in GetCandidatePaths(baseDirectory, overridePath, fileName))
        {
            if (exists(candidate)) return candidate;
        }
        return null;
    }

    /// <summary>
    /// Creates the Vulkan API. On macOS binds directly to the bundled MoltenVK; otherwise (or if
    /// MoltenVK cannot be found) falls back to the default Silk.NET loader, which can still pick
    /// up a system libvulkan.
    /// </summary>
    public static Vk CreateApi()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            return Vk.GetApi();

        var handle = LoadMoltenVk();
        if (handle == IntPtr.Zero)
            return Vk.GetApi();

        var context = new LamdaNativeContext(name =>
            NativeLibrary.TryGetExport(handle, name, out var addr) ? addr : IntPtr.Zero);
        return new Vk(context);
    }

    /// <summary>
    /// Handle of the loaded MoltenVK library, or zero if not loaded.
    /// </summary>
    public static IntPtr LoadedHandle => _handle;
}
