# RedHoleEngine Raytracer Performance Analysis

## Executive Summary

The RedHoleEngine raytracer is a compute shader-based renderer designed for real-time black hole visualization with physically accurate gravitational lensing. This document analyzes its performance characteristics, bottlenecks, and optimization opportunities.

**Key Metrics:**
- Rendering Method: GPU Compute Shader (Vulkan/OpenGL)
- Ray Tracing Algorithm: Geodesic ray marching with Verlet integration
- Acceleration Structure: Software BVH (Bounding Volume Hierarchy)
- Upscaling Support: DLSS 3.5, FSR 2.2, XeSS 1.3

---

## 1. Architecture Overview

### Rendering Pipeline

```
Scene Data --> BVH Build (CPU) --> GPU Upload --> Compute Shader --> Post-Process --> Upscale --> Display
                                                       |
                                                       v
                                              Motion Vector Gen
```

### Core Components

| Component | File | Purpose |
|-----------|------|---------|
| Main Shader | `Rendering/Shaders/raytracer_vulkan.comp` | Geodesic ray marching, BVH traversal |
| BVH Builder | `Rendering/Raytracing/RaytracerMeshBuilder.cs` | CPU-side acceleration structure |
| Settings | `Rendering/RaytracerSettings.cs` | Quality presets and parameters |
| Backend | `Rendering/Backends/VulkanBackend.cs` | GPU resource management |
| Upscalers | `Rendering/Upscaling/*.cs` | Temporal reconstruction |

---

## 2. Ray Tracing Algorithm Analysis

### Geodesic Integration

Unlike traditional raytracers that cast straight rays, this engine traces **geodesics** (curved light paths) through spacetime warped by black hole gravity.

**Algorithm:** Verlet Integration
```
For each pixel:
    1. Generate ray from camera
    2. Check if ray is far from black hole and pointing away (early exit)
    3. For N steps (64-256):
        a. Calculate gravitational acceleration (Schwarzschild or Kerr metric)
        b. Update ray position and direction via Verlet integration
        c. Every K steps, check BVH for mesh intersection
        d. Adapt step size based on proximity to event horizon
    4. Sample environment map or mesh color
    5. Apply post-processing
```

**Performance Impact:**
- Each ray requires 64-256 integration steps in lensing regions
- Step count is the primary performance driver
- Rays far from black hole use fast path (simple BVH intersection)

### Schwarzschild vs Kerr Metrics

| Metric | Complexity | Use Case |
|--------|------------|----------|
| Schwarzschild | O(1) per step | Non-rotating black holes |
| Kerr | O(1) per step, ~2x cost | Rotating black holes with frame dragging |

---

## 3. BVH Performance Analysis

### Structure

```
Node Size: 32 bytes
- BoundsMin: vec3 (12 bytes)
- LeftFirst: int (4 bytes)  
- BoundsMax: vec3 (12 bytes)
- TriCount: int (4 bytes)
```

### Build Algorithm

- **Method:** Top-down recursive median split
- **Split Heuristic:** Largest centroid extent (not SAH)
- **Max Leaf Size:** 4 triangles
- **Build Complexity:** O(n log n)

### Traversal Performance

**Current Implementation:** Stack-based iterative
```glsl
int stack[64];  // Fixed stack size
while (stackPtr > 0) {
    // Pop node
    // AABB test with early-out if tmin > closest.t
    // Leaf: test triangles
    // Internal: push children (front-to-back order)
}
```

**Observations:**
- 64-entry stack limits tree depth
- No SIMD/packet traversal
- Single ray at a time (no coherent batching)

---

## 4. Quality Presets Performance

### Lensing Quality

| Preset | Max Steps | Step Size | BVH Interval | Relative Cost |
|--------|-----------|-----------|--------------|---------------|
| Low | 48 | 0.5 | 6 | 1.0x |
| Medium | 96 | 0.35 | 4 | 2.5x |
| High | 160 | 0.2 | 3 | 5.0x |
| Ultra | 256 | 0.12 | 2 | 10.0x |

### Upscaling Performance Gains

| Quality | Render Scale | Pixel Reduction | Typical FPS Gain |
|---------|--------------|-----------------|------------------|
| Native | 100% | 0% | Baseline |
| Ultra Quality | 77% | 41% | 1.4x |
| Quality | 67% | 55% | 1.8x |
| Balanced | 58% | 66% | 2.0x |
| Performance | 50% | 75% | 2.5x |
| Ultra Performance | 33% | 89% | 4.0x |

---

## 5. Memory Analysis

### GPU Buffer Allocation

| Buffer | Format | Size Formula | Typical Size (1M tris) |
|--------|--------|--------------|------------------------|
| BVH Nodes | Structured | nodes * 32B | ~64 MB |
| Triangles | Structured | tris * 144B | ~144 MB |
| Output Image | RGBA32F | W * H * 16B | 32 MB (1080p) |
| Depth Image | R32F | W * H * 4B | 8 MB (1080p) |
| Motion Vectors | RG16F | W * H * 4B | 8 MB (1080p) |
| Accumulation | RGBA32F | W * H * 16B | 32 MB (1080p) |

**Total VRAM (typical):** ~300-500 MB for moderate scenes

### Memory Bandwidth

Primary bandwidth consumers:
1. BVH traversal (random access pattern)
2. Triangle data fetch (144 bytes per triangle tested)
3. Image stores (output, depth, motion vectors)

---

## 6. Identified Bottlenecks

### Critical Path Analysis

```
Frame Time Breakdown (estimated for complex scene @ 1080p):

Geodesic Integration:     45-60%  [PRIMARY BOTTLENECK]
BVH Traversal:            15-25%
Triangle Intersection:    10-15%
Post-Processing:           5-10%
Motion Vector Gen:         3-5%
Upscaling:                 2-5%
CPU Overhead:              2-5%
```

### Specific Bottlenecks

#### 1. Geodesic Integration Cost (High Impact)
- **Problem:** 64-256 iterations per ray in lensing regions
- **Cause:** Physical accuracy requirements for curved spacetime
- **Impact:** ~50% of frame time

#### 2. BVH Check Frequency (Medium Impact)
- **Problem:** BVH intersection every 2-6 steps during geodesic march
- **Cause:** Need to detect mesh hits along curved path
- **Impact:** ~15% of frame time

#### 3. Non-SAH BVH Split (Medium Impact)
- **Problem:** Median split doesn't optimize traversal cost
- **Cause:** Simpler implementation
- **Impact:** ~10-20% more traversal steps than optimal

#### 4. No Hardware RT (Medium Impact)
- **Problem:** Software BVH on compute shader
- **Cause:** Cross-platform compatibility requirement
- **Impact:** 2-5x slower than hardware RT cores

#### 5. Single-Pass Bloom (Low Impact)
- **Problem:** 5x5 gather per pixel
- **Cause:** Simplicity
- **Impact:** ~3% of frame time, could be 0.5% with separable

---

## 7. Optimization Recommendations

### High Priority

#### 1. Implement SAH BVH Building
**Current:** Median split on largest axis
**Proposed:** Surface Area Heuristic (SAH)

```csharp
// Pseudocode for SAH split
float bestCost = float.MaxValue;
int bestAxis = 0, bestSplit = 0;

for (int axis = 0; axis < 3; axis++) {
    for (int i = 0; i < binCount; i++) {
        float cost = leftArea * leftCount + rightArea * rightCount;
        if (cost < bestCost) {
            bestCost = cost;
            bestAxis = axis;
            bestSplit = i;
        }
    }
}
```

**Expected Gain:** 15-30% faster BVH traversal

#### 2. Adaptive Geodesic Precision
**Current:** Fixed step count based on quality preset
**Proposed:** Per-ray adaptive based on visual importance

```glsl
// Reduce precision for rays that won't show lensing
float importance = calculateRayImportance(rayDir, blackHolePos);
int maxSteps = int(mix(16.0, 256.0, importance));
```

**Expected Gain:** 20-40% fewer integration steps on average

#### 3. Ray Binning for Coherence
**Current:** Each pixel traced independently
**Proposed:** Bin rays by direction, trace coherent groups

**Expected Gain:** 10-20% better cache utilization

### Medium Priority

#### 4. Hardware Ray Tracing Support
Add optional VK_KHR_ray_tracing_pipeline path for RTX GPUs

```csharp
if (supportsHardwareRT) {
    // Use TLAS/BLAS for mesh intersection
    // Keep software geodesic integration
}
```

**Expected Gain:** 2-4x faster BVH operations on supported hardware

#### 5. Separable Bloom
**Current:** Single 5x5 gather
**Proposed:** Two-pass Gaussian (horizontal + vertical)

**Expected Gain:** 80% reduction in bloom cost

#### 6. Async Compute Overlap
**Current:** Sequential compute dispatches
**Proposed:** Overlap motion vector generation with graphics work

**Expected Gain:** Hide ~3-5% of frame time

### Low Priority

#### 7. Blue Noise Dithering
Add blue noise texture for better temporal stability

#### 8. Hierarchical Depth Buffer
Skip rays that hit opaque geometry early

#### 9. Variable Rate Shading
Reduce ray density in peripheral regions

---

## 8. Profiling Integration

### Available Metrics

The engine's `Profiler` class tracks:

```csharp
// Frame timing
Profiler.Instance.GetAverageFPS()
Profiler.Instance.GetOnePercentLowFPS()

// Raytracer counters
Profiler.Instance.GetCounter("BVHNodes")
Profiler.Instance.GetCounter("Triangles")
Profiler.Instance.GetCounter("RenderScale")

// Custom timers
Profiler.Instance.Scope("Raytracer", "GPU")
```

### Recommended Additional Metrics

```csharp
// Add these counters for deeper analysis
Profiler.Instance.SetCounter("GeodesicSteps", avgStepsPerRay, "Raytracer");
Profiler.Instance.SetCounter("BVHTraversals", avgNodesVisited, "Raytracer");
Profiler.Instance.SetCounter("RaysPerSecond", totalRays / frameTime, "Raytracer");
Profiler.Instance.SetCounter("LensingPixels", pixelsInLensingRegion, "Raytracer");
```

---

## 9. Benchmark Methodology

### Test Scenarios

1. **Empty Scene:** Environment map only, no meshes
   - Measures pure geodesic integration cost

2. **Static Scene:** Fixed camera, complex geometry
   - Measures BVH traversal efficiency

3. **Dynamic Scene:** Moving camera through geometry
   - Measures overall system performance

4. **Lensing Stress Test:** Camera near event horizon
   - Measures worst-case geodesic cost

### Metrics to Capture

| Metric | Target | Unit |
|--------|--------|------|
| Frame Time | < 16.67ms | ms (60 FPS) |
| Ray Throughput | > 100M | rays/second |
| BVH Traversal | < 5ms | ms per frame |
| Memory Bandwidth | < 80% | % of peak |
| VRAM Usage | < 2GB | bytes |

---

## 10. Conclusion

The RedHoleEngine raytracer achieves real-time black hole rendering through careful optimization of geodesic ray marching. The primary performance bottleneck is the integration step count required for accurate gravitational lensing.

**Key Optimization Priorities:**
1. SAH BVH building (15-30% BVH speedup)
2. Adaptive geodesic precision (20-40% fewer steps)
3. Hardware RT support on compatible GPUs (2-4x BVH speedup)

**Current Strengths:**
- Efficient early-exit for non-lensing rays
- Adaptive step sizing near singularity
- Modern upscaling integration (DLSS/FSR2/XeSS)
- Comprehensive quality presets

With the recommended optimizations, the raytracer could achieve 1.5-2x overall performance improvement while maintaining physical accuracy.

---

## Appendix: File Reference

| Category | Files |
|----------|-------|
| Shaders | `raytracer_vulkan.comp`, `raytracer.comp`, `motion_vectors.comp` |
| Raytracing | `RaytracerMeshBuilder.cs`, `RaytracerMeshTypes.cs`, `RaytracerMeshSystem.cs` |
| Settings | `RaytracerSettings.cs` |
| Upscaling | `IUpscaler.cs`, `DLSSUpscaler.cs`, `FSR2Upscaler.cs`, `XeSSUpscaler.cs`, `UpscalerManager.cs` |
| Backend | `VulkanBackend.cs` |
| Profiling | `Profiler.cs` |
