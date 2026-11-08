# Temporal Denoise (SVGF temporal reprojection) and the Gravity Gate

## Status

- Implemented: reprojection, history validation, variance-guided clamp, exponential blend, settings, editor UI, gravity gate (3 layers), history reset.
- Verified by: build, unit tests (gate, history tracker, reprojection math), and shaderc compiling `denoise_svgf_temporal.comp` and `motion_vectors.comp` to SPIR-V.
- NOT verified: the Vulkan dispatch itself has not been run on a GPU (no headless/GPU run was possible). Treat the first on-GPU run as untested; pipeline creation is wrapped in try/catch and disables the feature (with a log line) instead of crashing.

## Algorithm

One compute pass, `Rendering/Shaders/denoise_svgf_temporal.comp`, run after the raytracer and the motion-vector pass, at render resolution:

1. Inputs: current color (the raytracer output image), G-buffer normal, linear depth (0 = sky), motion vectors, and the previous frame's accumulated color / moments / history length / normal / depth.
2. Reproject: `prev = floor(pixel + 0.5 - motion)` (motion points current -> previous, in pixels).
3. Validate history: in bounds, previous depth > 0, `dot(n, nPrev) >= normalThreshold`, relative depth difference `<= depthThreshold`. Sky / invalid current pixels never accumulate.
4. Variance-guided clamp: history color is clamped to the current 3x3 neighbourhood `mean +/- gamma * sigma` (anti-ghosting).
5. Blend: `historyLength = min(prevLength + 1, maxHistory)`, `alpha = max(strength, 1/historyLength)`, `out = mix(clampedHistory, current, alpha)`; luminance moments are blended the same way (for a future variance/spatial stage).
6. The result is copied over the output image (so display/upscaler see the denoised frame) and the G-buffer normal/depth are copied to "previous" for the next frame. Color/moments/length ping-pong between two slots.

CPU mirror of the math (unit tested): `Denoising/TemporalReprojection.cs`. Keep the shader and mirror in sync.

`motion_vectors.comp` previously reconstructed the world position by treating `depth / farPlane` as NDC z, which is wrong for a perspective projection. It now places the point at `cameraPos + rayDir * depth`, matching what the raytracer stores (distance to first hit).

Pipeline order: raytracer -> motion vectors (also run when temporal is active, not only for upscalers) -> temporal pass -> (upscaler / display).

The previous SVGF scaffolding (spatial / variance / A-trous shaders and `Denoiser` helpers) is still not wired; only the temporal stage is.

## Settings (`RaytracerSettings`)

| Setting | Default | Meaning |
|---|---|---|
| `Denoise` | per preset | Master denoise switch; temporal requires it |
| `TemporalDenoise` | false | Enable temporal history reuse |
| `SVGFTemporalAlpha` | 0.1 | Minimum weight of the current frame (blend strength; lower = smoother, more lag) |
| `TemporalMaxHistory` | 32 | Max frames of history (1-64) |
| `TemporalNormalThreshold` | 0.9 | Min normal dot product to accept history |
| `TemporalDepthThreshold` | 0.1 | Max relative depth difference to accept history |
| `TemporalVarianceClamp` | 1.5 | Neighbourhood clamp width in sigmas |

Editor: Raytracer panel > Denoise > "Temporal (history reuse)".

## The gravity gate (HARD SAFETY RULE)

Temporal reprojection assumes straight camera rays. Gravitational lensing bends rays, so a pixel's world position, depth, normal and therefore its motion vector are no longer functions of the camera alone. Blending history under lensing smears and ghosts. Therefore temporal denoising must never run when gravity can bend rays.

Single decision function: `TemporalDenoiseGate.Evaluate(blackHole, gravitySources, settings)` (`IsTemporalAllowed` is the boolean wrapper). It returns `(Allowed, Reason)`. It is pure and hardware free.

### Exact semantics (conservative: when in doubt, deny)

- A `BlackHole` with `Mass != 0` denies. This includes tiny, negative, NaN and infinite masses. Any spin, disk, ergosphere/photon-sphere overlay, or lensing quality (Low..Ultra, Custom) with such a black hole also denies, because those all act through the mass.
- `BlackHole == null` or `Mass == 0` allows. The renderer uploads all-zero lensing uniforms in that case, so rays are straight. Spin, disk radii, `ShowErgosphere`, `ShowPhotonSphere` and lensing settings cannot bend rays with zero mass and do not deny on their own. (Decision: zero mass is "no lensing". If you would rather deny whenever a black hole object exists, change the first check in `Evaluate`.)
- A `GravitySourceComponent` of type Schwarzschild or Kerr with `Mass != 0` denies, regardless of `AffectsLight` (the app converts every such source into a lensing `BlackHole`).
- Newtonian and Uniform sources deny only if `AffectsLight` is set and the mass / uniform strength is non-zero or non-finite.
- Unknown gravity types deny.

Note: `Application.OnRender` substitutes `BlackHole.CreateDefault()` (mass 2) when the scene has no black hole, so in the game runtime temporal denoise is effectively always gated off until that fallback is changed. The editor viewport passes `null` when the scene has no black hole, so temporal is available there.

### Enforcement layers

1. C# render path: `VulkanBackend.PlanTemporalDenoise` evaluates the gate every frame; `RecordTemporalDenoisePass` records nothing when gated (no dispatch, no history reads or writes). The motion-vector pass is not forced on either.
2. Shader: the uniform `flags` bit0 must be set by the CPU. If it is clear the shader copies the current color through and reads no history.
3. UI: the checkbox is disabled and shows `Disabled: gravitational lensing bends rays (<detail>)`. The setting value is untouched, so it resumes when the gate opens.

### History reset

`TemporalHistoryTracker` invalidates history whenever the pass does not run (gated, user disabled, `Denoise` off), on `ResetAccumulation` or a settings/BVH change, and when the history images are recreated (resolution / upscale change). After a gate flip (allowed -> denied -> allowed) the first allowed frame runs but reads no history (shader flag bit1 clear), so stale history is never blended. Camera motion alone does not reset history; that is the point of reprojection.

### Extending the gate

When adding any effect that changes ray paths (e.g. new metric, wormhole, refractive volumes, other lensing sources):

1. Add the condition to `TemporalDenoiseGate.Evaluate` (deny if active, with a detail string).
2. Add tests to `RedHoleEngine.Tests/Rendering/TemporalDenoiseGateTests.cs` for both sides.
3. If the effect lives in `RaytracerSettings` rather than a body, use the `settings` parameter (currently reserved).
4. If the backend knows about ray-bending sources the gate cannot see (it only receives one `BlackHole`), pass them via the `gravitySources` parameter in `PlanTemporalDenoise`.

## Known limitations

- Nearest-neighbour history fetch (no bilinear / bicubic), so slight softening under motion.
- Motion vectors are camera-only; moving objects rely on depth/normal rejection and the neighbourhood clamp.
- Operates on the tone-mapped, gamma-encoded output (the raytracer's final image), not linear HDR radiance.
- The raytracer's own accumulation still resets whenever the camera moves; temporal denoise is what smooths moving-camera frames.
- Depth is distance to first hit (not view-space z); the thresholds are relative so this is fine, but it is not a standard z-buffer.
- Spatial / variance SVGF stages are not wired; moments are produced but unused.
- GPU path unverified at runtime (see Status).
