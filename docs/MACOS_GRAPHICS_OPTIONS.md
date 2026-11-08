# macOS Graphics Options

Goal: macOS users must not need to install MoltenVK, the Vulkan SDK, or Homebrew's `vulkan-loader` to build or run RedHoleEngine. This document records the approach taken (Option A) and the alternatives that were considered but not chosen, in case requirements change.

## Option A (chosen): load the bundled MoltenVK directly as the Vulkan loader

`Silk.NET.MoltenVK.Native` (referenced by `RedHoleEngine.csproj`) ships `libMoltenVK.dylib`. MoltenVK exports the Vulkan core entry points, including `vkGetInstanceProcAddr`, so it can act as the loader itself and the Khronos loader plus ICD JSON indirection is unnecessary.

How it works (`RedHoleEngine/Rendering/Backends/MoltenVkLoader.cs`):

1. `MoltenVkLoader.GetCandidatePaths` is a pure function that lists where to look: `REDHOLE_MOLTENVK_PATH` (override), the app directory, then `runtimes/osx-arm64/native`, `runtimes/osx-x64/native`, `runtimes/osx/native`. `dotnet build` leaves the dylib under `runtimes/osx/native`; `dotnet publish -r osx-arm64` flattens it next to the executable.
2. `MoltenVkLoader.CreateApi()` (used instead of `Vk.GetApi()` in `VulkanBackend.Initialize`) loads the dylib with `NativeLibrary` and builds `new Vk(new LamdaNativeContext(...))` that resolves symbols from it. Silk.NET's `Vk.GetApi()` uses its own `dlopen`-based loader (not `DllImport`), so a `SetDllImportResolver` hook would not apply; supplying a native context is the supported seam.
3. `MoltenVkLoader.ConfigureGlfw()` (called after `Window.PrioritizeGlfw()`) calls GLFW 3.4's `glfwInitVulkanLoader(vkGetInstanceProcAddr)` before GLFW initialises. Without this, GLFW does its own `dlopen("libvulkan.1.dylib")`, fails, and Silk.NET throws "GLFW doesn't support Vulkan on this computer".
4. If `libMoltenVK.dylib` is missing, a clear message is logged and the default Silk.NET loader is used, so a system `libvulkan` still works. Non-macOS platforms are unchanged.
5. `VK_KHR_portability_enumeration` handling in `VulkanBackend.CreateInstance` is unchanged and still needed.

The old per-project macOS build target (copy Homebrew `libvulkan*.dylib`, copy ICD JSON, compile a native C launcher with `clang`) was removed. The launcher existed only to set `DYLD_LIBRARY_PATH`, `VK_ICD_FILENAMES` and `VK_DRIVER_FILES`, which are no longer used.

Known risks and limitations:

- Relies on MoltenVK exporting loader-level entry points (`vkGetInstanceProcAddr`, `vkEnumerateInstance*`, `vkCreateInstance`). True for current MoltenVK; a future change would break this.
- No ICD enumeration: only MoltenVK is used. Other drivers (for example a software ICD) are not discoverable on macOS unless the fallback path is taken.
- Vulkan layers (`VK_LAYER_*`, validation layers) are not supported, since there is no loader to inject them. Developers needing validation layers must use the Khronos loader (Option B) via the fallback path.
- Needs GLFW 3.4+ for `glfwInitVulkanLoader` (the bundled `libglfw.3.dylib` is 3.4.0). The call must hit the same GLFW image Silk.NET loads.
- Version is pinned by the NuGet package (MoltenVK 1.4.0 / Vulkan 1.4.323 at the time of writing).

## Option B (not chosen): bundle the Khronos loader

Ship `libvulkan.1.dylib` (Khronos Vulkan-Loader) and `MoltenVK_icd.json` with the app, and point the loader at it with `VK_ICD_FILENAMES` / `VK_DRIVER_FILES` (set by a launcher or `setenv` before the first Vulkan call) and an `@rpath`/`DYLD_LIBRARY_PATH` setup.

Pros:
- Standard loader behaviour: ICD enumeration, layers (validation, `VK_LOADER_DEBUG`), multiple drivers.
- No reliance on MoltenVK exporting loader symbols.

Cons:
- We would have to obtain and redistribute the loader binary ourselves (the NuGet packages do not include it), build or fetch it per architecture, and sign/notarise it.
- Needs environment setup before process start or `setenv` plus relative-path ICD JSON, which is what forced the native launcher.
- Extra moving parts (loader version, ICD JSON `library_path` relative to the JSON location) that break easily in publish layouts and app bundles.

Licensing: the Khronos Vulkan-Loader is Apache-2.0, so redistribution is permitted with the licence text and NOTICE retained. MoltenVK is also Apache-2.0.

Why A was preferred: fewer files, no launcher, no environment variables, nothing outside the existing NuGet dependency, and layers are not needed in shipping builds. B remains the fallback if layers or multi-ICD support become necessary on macOS.

## Option C (not chosen): native Metal backend

Scope:
- `VulkanBackend.cs` is about 4.4k lines and sits behind `IGraphicsBackend`, which is the seam where a `MetalBackend` would plug in.
- Roughly 10 GLSL shaders would need Metal equivalents: the compute raytracer, motion vectors, SVGF denoise, particles, UI, raster, and debug lines.
- Shaders require either hand-written MSL rewrites or an automated SPIR-V to MSL step via SPIRV-Cross (which is essentially what MoltenVK does internally).
- There are no official Silk.NET Metal bindings. Options: `objc_msgSend` interop from C#, a thin native (Objective-C/Swift) shim exposed through P/Invoke, or Microsoft's macOS workload bindings (`Metal` namespace in `net-macos`), which would tie the build to that workload.
- Every rendering feature would need to be implemented and maintained twice, and tested on both backends.
- Estimate: weeks to months for feature parity with the Vulkan path, plus ongoing cost.

When it would be worth it:
- Needing Metal-only features (MetalFX upscaling, hardware ray tracing via Metal acceleration structures, mesh shaders, tile shaders) that MoltenVK does not expose.
- Measured MoltenVK translation overhead that matters for the target hardware.
- App Store or platform constraints that rule out the MoltenVK layer or require pure Metal.

Until one of those applies, Option A gives macOS support at the lowest cost.
