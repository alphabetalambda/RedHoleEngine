using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using RedHoleEngine.Input.Devices;

namespace RedHoleEngine.Input.Providers;

/// <summary>
/// Isolates Steam Input loading to prevent assembly load exceptions
/// from propagating to the main InputManager.
/// This class uses NoInlining to ensure the JIT doesn't load Steamworks.NET
/// until these methods are actually called.
/// </summary>
internal static class SteamInputLoader
{
    private static bool _resolverRegistered;
    
    /// <summary>
    /// Register the native library resolver for Steam libraries.
    /// This helps .NET find libsteam_api on different platforms.
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void EnsureResolverRegistered()
    {
        if (_resolverRegistered) return;
        _resolverRegistered = true;
        
        try
        {
            // Load the Steamworks.NET assembly and register our resolver
            var steamworksAssembly = Assembly.Load("Steamworks.NET");
            NativeLibrary.SetDllImportResolver(steamworksAssembly, SteamLibraryResolver);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[SteamInputLoader] Could not register resolver: {ex.Message}");
        }
    }
    
    private static IntPtr SteamLibraryResolver(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
    {
        // Only handle steam_api libraries
        if (!libraryName.Contains("steam_api", StringComparison.OrdinalIgnoreCase))
            return IntPtr.Zero;
        
        // Get the directory where the executable is running
        var basePath = AppContext.BaseDirectory;
        
        // Try different library names based on platform
        string[] candidates;
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            candidates = new[]
            {
                Path.Combine(basePath, "steam_api64.dll"),
                Path.Combine(basePath, "steam_api.dll"),
                "steam_api64.dll",
                "steam_api.dll"
            };
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            candidates = new[]
            {
                Path.Combine(basePath, "libsteam_api.so"),
                Path.Combine(basePath, "libsteam_api64.so"),
                "libsteam_api.so",
                "libsteam_api64.so",
                "/usr/lib/libsteam_api.so",
                "/usr/local/lib/libsteam_api.so"
            };
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            candidates = new[]
            {
                Path.Combine(basePath, "libsteam_api.dylib"),
                "libsteam_api.dylib",
                "/usr/local/lib/libsteam_api.dylib"
            };
        }
        else
        {
            return IntPtr.Zero;
        }
        
        foreach (var candidate in candidates)
        {
            if (NativeLibrary.TryLoad(candidate, out var handle))
            {
                Console.WriteLine($"[SteamInputLoader] Loaded Steam library from: {candidate}");
                return handle;
            }
        }
        
        Console.WriteLine($"[SteamInputLoader] Could not find Steam native library. Tried: {string.Join(", ", candidates)}");
        return IntPtr.Zero;
    }
    
    /// <summary>
    /// Try to create and initialize a Steam Input provider.
    /// Returns null if Steam isn't available or if the assembly can't be loaded.
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static IInputProvider? TryCreateProvider()
    {
        try
        {
            EnsureResolverRegistered();
            return CreateProviderInternal();
        }
        catch (Exception ex) when (ex is FileNotFoundException or TypeLoadException or DllNotFoundException)
        {
            Console.WriteLine($"[SteamInputLoader] Steam Input not available: {ex.GetType().Name} - {ex.Message}");
            return null;
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static IInputProvider? CreateProviderInternal()
    {
        var provider = new SteamInputProvider();
        if (provider.Initialize())
        {
            return provider;
        }
        return null;
    }

    /// <summary>
    /// Get gamepads from a Steam provider (cast to correct type internally).
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static IEnumerable<GamepadDevice> GetGamepads(IInputProvider provider)
    {
        try
        {
            return GetGamepadsInternal(provider);
        }
        catch (Exception ex) when (ex is FileNotFoundException or TypeLoadException or DllNotFoundException)
        {
            return Enumerable.Empty<GamepadDevice>();
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static IEnumerable<GamepadDevice> GetGamepadsInternal(IInputProvider provider)
    {
        if (provider is SteamInputProvider steamProvider)
        {
            return steamProvider.GetGamepads();
        }
        return Enumerable.Empty<GamepadDevice>();
    }

    /// <summary>
    /// Get gyros from a Steam provider.
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static IEnumerable<GyroDevice> GetGyros(IInputProvider provider)
    {
        try
        {
            return GetGyrosInternal(provider);
        }
        catch (Exception ex) when (ex is FileNotFoundException or TypeLoadException or DllNotFoundException)
        {
            return Enumerable.Empty<GyroDevice>();
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static IEnumerable<GyroDevice> GetGyrosInternal(IInputProvider provider)
    {
        if (provider is SteamInputProvider steamProvider)
        {
            return steamProvider.GetGyros();
        }
        return Enumerable.Empty<GyroDevice>();
    }

    /// <summary>
    /// Activate a Steam action set.
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void ActivateActionSet(IInputProvider provider, string actionSetName)
    {
        try
        {
            ActivateActionSetInternal(provider, actionSetName);
        }
        catch (Exception ex) when (ex is FileNotFoundException or TypeLoadException or DllNotFoundException)
        {
            // Silently ignore
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ActivateActionSetInternal(IInputProvider provider, string actionSetName)
    {
        if (provider is SteamInputProvider steamProvider)
        {
            foreach (var gamepad in steamProvider.GetGamepads())
            {
                if (gamepad is SteamGamepad steamGamepad)
                {
                    steamProvider.ActivateActionSet(steamGamepad, actionSetName);
                }
            }
        }
    }

    /// <summary>
    /// Show the Steam binding panel.
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void ShowBindingPanel(IInputProvider provider)
    {
        try
        {
            ShowBindingPanelInternal(provider);
        }
        catch (Exception ex) when (ex is FileNotFoundException or TypeLoadException or DllNotFoundException)
        {
            // Silently ignore
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ShowBindingPanelInternal(IInputProvider provider)
    {
        if (provider is SteamInputProvider steamProvider)
        {
            var gamepad = steamProvider.GetGamepads().FirstOrDefault() as SteamGamepad;
            if (gamepad != null)
            {
                steamProvider.ShowBindingPanel(gamepad);
            }
        }
    }
}
