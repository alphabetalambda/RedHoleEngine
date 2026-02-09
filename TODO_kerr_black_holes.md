# Kerr Black Holes Implementation TODO

## Overview
Implement rotating (Kerr) black holes with frame dragging, ergosphere visualization, and asymmetric gravitational lensing.

## Phase 1: Frame Dragging (Current)

### Core Physics
- [ ] Add spin parameter (a) to BlackHole class (0 = Schwarzschild, 1 = extremal Kerr)
- [ ] Add spin axis vector to define rotation direction
- [ ] Implement Kerr metric geodesic equations in raytracer shader
- [ ] Calculate frame dragging velocity field
- [ ] Update light ray integration to include frame dragging effect

### Shader Changes
- [ ] Add `u_BlackHoleSpin` (float) uniform for spin parameter
- [ ] Add `u_BlackHoleSpinAxis` (vec3) uniform for rotation axis
- [ ] Implement `kerrAcceleration()` function replacing/extending `schwarzschildAcceleration()`
- [ ] Add frame dragging contribution to ray velocity

### C# Changes
- [ ] Extend `BlackHole` class with `Spin` and `SpinAxis` properties
- [ ] Update `GravitySourceComponent` to expose Kerr parameters
- [ ] Update `RaytracerUniforms` struct with spin parameters
- [ ] Update `VulkanBackend` to pass spin to shader

---

## Phase 2: Ergosphere Visualization

### Physics
- [ ] Calculate ergosphere boundary: r_ergo = M + sqrt(M² - a²cos²θ)
- [ ] Implement ergosphere intersection test

### Visual Effects
- [ ] Add ergosphere rendering option (wireframe or translucent surface)
- [ ] Color-code regions (event horizon vs ergosphere)
- [ ] Visualize frame dragging with particle trails or flow lines

---

## Phase 3: Asymmetric Lensing

### Physics
- [ ] Implement prograde vs retrograde light path differences
- [ ] Light co-rotating with black hole bends less
- [ ] Light counter-rotating bends more (can get captured easier)

### Visual Effects
- [ ] Einstein ring becomes asymmetric
- [ ] Accretion disk appears brighter on approaching side (Doppler beaming)
- [ ] Implement relativistic Doppler shift for disk color

---

## Phase 4: Accretion Disk Improvements

### Physics
- [ ] ISCO (Innermost Stable Circular Orbit) depends on spin
  - Prograde ISCO: r = M(3 + Z2 - sqrt((3-Z1)(3+Z1+2*Z2)))
  - Retrograde ISCO: r = M(3 + Z2 + sqrt((3-Z1)(3+Z1+2*Z2)))
- [ ] Disk rotation direction matches black hole spin
- [ ] Implement disk thickness variation

### Visual Effects
- [ ] Doppler beaming (approaching side brighter)
- [ ] Gravitational redshift variation across disk
- [ ] Light bending creates "top" and "bottom" images of disk

---

## Phase 5: Advanced Features

### Ring Singularity
- [ ] Kerr singularity is a ring, not a point
- [ ] Implement ring singularity geometry
- [ ] Rays passing through ring enter "negative space" (optional sci-fi feature)

### Penrose Process Visualization
- [ ] Show energy extraction region in ergosphere
- [ ] Particle splitting visualization (optional)

### Performance Optimization
- [ ] Adaptive step size based on proximity to ergosphere
- [ ] Early termination for rays clearly escaping
- [ ] LOD for distant black holes

---

## Reference Formulas

### Kerr Metric (Boyer-Lindquist coordinates)
```
Σ = r² + a²cos²θ
Δ = r² - 2Mr + a²

ds² = -(1 - 2Mr/Σ)dt² - (4Mar sin²θ/Σ)dtdφ + (Σ/Δ)dr² + Σdθ² + (r² + a² + 2Ma²r sin²θ/Σ)sin²θ dφ²
```

### Event Horizon
```
r_+ = M + sqrt(M² - a²)  (outer horizon)
r_- = M - sqrt(M² - a²)  (inner horizon)
```

### Ergosphere
```
r_ergo = M + sqrt(M² - a²cos²θ)
```

### Frame Dragging Angular Velocity
```
ω = 2Mar / ((r² + a²)² - a²Δsin²θ)
```

---

## Files to Modify

### Shaders
- `RedHoleEngine/Rendering/Shaders/raytracer_vulkan.comp`

### C# Core
- `RedHoleEngine/Physics/BlackHole.cs`
- `RedHoleEngine/Components/GravitySourceComponent.cs`
- `RedHoleEngine/Rendering/Backends/VulkanBackend.cs`
- `RedHoleEngine/Rendering/RaytracerSettings.cs`

### Test Scene
- `RedHoleTestScene/TestSceneModule.cs`

---

## Testing Checklist

- [ ] Spin = 0 produces identical results to current Schwarzschild implementation
- [ ] Frame dragging visible at high spin values
- [ ] No visual artifacts at spin = 0.99 (near-extremal)
- [ ] Performance acceptable at all spin values
- [ ] Accretion disk asymmetry visible
- [ ] Ergosphere correctly sized and positioned
