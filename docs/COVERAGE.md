# Coverage

Line coverage is measured with coverlet (`XPlat Code Coverage`, Cobertura) on `RedHoleEngine.dll`. The February 2026 DotCover note in `coverage-analysis.md` is stale.

## Numbers

| Run | Line coverage | Lines |
| --- | --- | --- |
| Baseline (before this work) | 20.61% | 4,077 / 19,784 |
| Raw (no file exclusions) | 61.33% | 12,144 / 19,799 |
| Filtered (hardware-bound files removed) | 90.51% | 12,107 / 13,377 |

Branch coverage on the raw run is 54.6%. `dotnet test` on this change: 1,139 passed, 83 skipped, 0 failed.

The filtered figure drops the files in `coverlet.runsettings` from both the numerator and the denominator of the raw Cobertura report. Splash-screen setup is not excluded: it is ECS/UI data and is fully covered. Editor, trainer, and demo executables are ImGui/window hosts and are not part of `RedHoleEngine.dll`, so they are not in either number.

## What is excluded, and why

These files talk to Vulkan, OpenGL, OpenAL, a windowing/input context, or the Steam client. They cannot be executed in a headless test run.

| File | Why |
| --- | --- |
| `Rendering/Backends/VulkanBackend.cs` | Vulkan device, swapchain, shaders |
| `Rendering/UI/UiRenderer.cs` | Vulkan UI draw |
| `Rendering/Particles/ParticleRenderer.cs` | Vulkan particle draw |
| `Rendering/Debug/DebugRenderer.cs` | Vulkan debug draw |
| `Rendering/Renderer.cs` | OpenGL context |
| `Rendering/Upscaling/DLSSUpscaler.cs` | NVIDIA/Vulkan upscaler |
| `Rendering/Upscaling/FSR2Upscaler.cs` | Vulkan FSR2 dispatch |
| `Rendering/Upscaling/XeSSUpscaler.cs` | Vulkan XeSS dispatch |
| `Rendering/Upscaling/NoUpscaler.cs` | Vulkan pass-through |
| `Audio/OpenALBackend.cs` | OpenAL device (WAV parsing lives in `WavPcmReader` and is tested) |
| `Input/InputManager.cs` | Silk window input context |
| `Input/Providers/SilkInputProvider.cs` | Window gamepads |
| `Input/Providers/KeyboardMouseProvider.cs` | Window keyboard/mouse |
| `Input/Providers/SteamInputProvider.cs` | Steamworks |
| `Input/Providers/SteamInputLoader.cs` | Native Steam library load |
| `Core/Application.cs` | GLFW window and the engine host loop |

`[ExcludeFromCodeCoverage]` is not used. That attribute is on coverlet's default exclusion list, so it would also disappear from the raw report. The filter file is what keeps the raw and filtered numbers distinct.

## Reproduce

```bash
# Raw
dotnet test RedHoleEngine.Tests/RedHoleEngine.Tests.csproj -c Release \
  --collect:"XPlat Code Coverage" --results-directory coverage-raw \
  -- DataCollectionRunSettings.DataCollectors.DataCollector.Configuration.Format=cobertura \
  -- DataCollectionRunSettings.DataCollectors.DataCollector.Configuration.Include="[RedHoleEngine]*"

# Filtered
dotnet test RedHoleEngine.Tests/RedHoleEngine.Tests.csproj -c Release \
  --settings coverlet.runsettings --collect:"XPlat Code Coverage" \
  --results-directory coverage-filtered
```

## Behavior changes made so code could be tested, or because a check was clearly wrong

- `AudioEngine` accepts an `IAudioBackend`. The existing constructor still creates `OpenALBackend`.
- WAV parsing moved to `WavPcmReader`. A WAV with a data chunk and no `fmt` chunk used to divide by zero; it now fails the parse the same way other malformed files do (`LoadClip` already treated that as "no clip").
- Doppler pitch when source speed equals the speed of sound used `MathF.Sign(0)`, which is 0, and then divided by zero. The clamp now stays finite.
- Parenting a scene node updates the scene's root list. Previously `OnNodeParentChanged` was never called, so a child stayed in `RootNodes` and was walked twice. `DestroyNode` also drops descendants from the lookup.
- `UnpixSystem` snapshots piece entities before updating them. Destroying a piece during a live sparse-set walk skipped the entity that swapped into that slot, so a sibling could miss its age update.
- `EnvironmentMap.Sample` maps +Y to v = 0, matching `CreateProceduralSky` and the usual equirectangular latitude. The previous `asin` offset sampled the ground when asked for the sky.
- `SceneNode.SetParent(self)` throws. It used to parent a node to itself and recurse in `Transform.Rotation` until the stack overflowed.

## Bugs left as skipped tests

- `BlackHole.PhotonSphereRadius` is `1.5 * Rs` for every spin. `CalculatePhotonSphereRadius()` is the spin-aware prograde orbit and is what the Vulkan upload uses. The property comment describes the Schwarzschild value, so the property was not changed.
- `GravitySourceComponent.PhotonSphereRadius` is `3 * Mass` for Kerr sources as well. `AudioDebugVisualizer` draws that radius. There is no separate Kerr accessor, and the comment scopes the formula to Schwarzschild.
- `RecordedFrame` does not store `GamepadConnected`, so playback restores sticks and buttons with the flag still false. Adding a field would break existing binary recordings.
- `HapticPattern.Pulse` passes the pulse duration into `HapticFeedback.Rumble` as the right motor.
- `VelocityOverLifetimeModule` multiplies speed again on every `Apply`. `NoiseModule.Octaves` does not change the sample.
