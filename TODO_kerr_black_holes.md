# Kerr Black Holes Implementation TODO

## Overview
Implement rotating (Kerr) black holes with frame dragging, ergosphere visualization, and asymmetric gravitational lensing.

## Phase 1: Frame Dragging - COMPLETED

### Core Physics
- [x] Add spin parameter (a) to BlackHole class (0 = Schwarzschild, 1 = extremal Kerr)
- [x] Add spin axis vector to define rotation direction
- [x] Implement Kerr metric geodesic equations in raytracer shader
- [x] Calculate frame dragging velocity field
- [x] Update light ray integration to include frame dragging effect

### Shader Changes
- [x] Add `u_BlackHoleSpin` (float) uniform for spin parameter
- [x] Add `u_BlackHoleSpinAxis` (vec3) uniform for rotation axis
- [x] Implement `kerrAcceleration()` function
- [x] Add frame dragging contribution to ray velocity

### C# Changes
- [x] Extend `BlackHole` class with `Spin` and `SpinAxis` properties
- [x] Update `GravitySourceComponent` to expose Kerr parameters
- [x] Update `RaytracerUniforms` struct with spin parameters
- [x] Update `VulkanBackend` to pass spin to shader

---

## Phase 2: Ergosphere Visualization - COMPLETED

### Physics
- [x] Calculate ergosphere boundary: r_ergo = M + sqrt(M² - a²cos²θ)
- [x] Implement ergosphere check function (`isInsideErgosphere()`)
- [x] `ergosphereRadiusAtTheta()` for angle-dependent radius

### Visual Effects
- [x] Add ergosphere rendering option (`ShowErgosphere`, `ErgosphereOpacity`)
- [x] Translucent volumetric rendering as rays pass through
- [x] Color-code by depth (blue → purple → red near horizon)
- [x] Animated swirl pattern showing frame dragging rotation

---

## Phase 3: Doppler Effects - COMPLETED

### Physics
- [x] Implement prograde vs retrograde light path differences
- [x] Calculate disk orbital velocity with frame dragging boost
- [x] Relativistic Doppler factor calculation

### Visual Effects
- [x] Doppler beaming (approaching side brighter, D³ intensity scaling)
- [x] Relativistic color shift (blueshift/redshift)

---

## Phase 4: Accretion Disk Improvements - COMPLETED

### Physics
- [x] ISCO (Innermost Stable Circular Orbit) passed to shader
- [x] Disk plane perpendicular to spin axis
- [x] Gravitational redshift - inner disk light loses energy
- [x] Disk orbital velocity calculation

### Visual Effects
- [x] Disk rotation animation (differential rotation - inner faster)
- [x] Spiral arm structure
- [x] Disk thickness variation (flared disk - thicker at outer edge)
- [x] Volumetric disk intersection
- [x] Gravitational redshift color/intensity adjustment
- [x] Combined Doppler + gravitational effects

---

## Phase 5: Advanced Features - COMPLETED

### Ring Singularity
- [x] Kerr singularity is a ring, not a point
- [x] Implement ring singularity geometry (`distanceToRingSingularity()`, `ringSingularityColor()`)
- [x] Visual glow near ring singularity (purple/white gradient)
- [ ] Rays passing through ring enter "negative space" (optional sci-fi feature - deferred)

### Photon Sphere
- [x] Photon sphere visualization (`isNearPhotonSphere()`, `photonSphereColor()`)
- [x] Golden glow showing photon orbit radius
- [x] `ShowPhotonSphere` and `PhotonSphereOpacity` settings
- [x] `u_PhotonSphereRadius` uniform passed from C#

### Penrose Process Visualization
- [ ] Show energy extraction region in ergosphere (deferred)
- [ ] Particle splitting visualization (deferred)

### Performance Optimization
- [x] Adaptive step size based on proximity to black hole
- [x] Early termination for rays clearly escaping
- [x] Configurable max distance for far viewing
- [ ] LOD for distant black holes (deferred)

### Additional Visual Features
- [x] Photon sphere visualization
- [ ] Caustic patterns from strong lensing (deferred)
- [ ] Multiple Einstein rings (deferred)

---

## Summary of Implemented Features

### Shader Functions Added:
- `frameDraggingOmega()` - Lense-Thirring angular velocity
- `kerrAcceleration()` - Full Kerr geodesic with frame dragging
- `isInsideErgosphere()` - Ergosphere boundary check
- `ergosphereColor()` - Volumetric ergosphere visualization
- `diskOrbitalVelocity()` - Keplerian + frame dragging velocity
- `diskOrbitalPeriod()` - For rotation animation
- `dopplerFactor()` - Relativistic Doppler calculation
- `dopplerColorShift()` - Blueshift/redshift colors
- `gravitationalRedshift()` - Energy loss in gravity well
- `applyGravitationalRedshift()` - Color/intensity adjustment
- `getDiskThickness()` - Flared disk geometry
- `isInsideDiskVolume()` - Volumetric disk intersection
- `isNearPhotonSphere()` - Proximity to photon orbit
- `photonSphereColor()` - Golden glow visualization
- `distanceToRingSingularity()` - Distance to Kerr ring singularity
- `passedThroughRing()` - Detects ray crossing through ring plane
- `ringSingularityColor()` - Purple/white glow near ring

### Uniforms Added:
- `u_BlackHoleSpin` - Dimensionless spin (0 to ~1)
- `u_KerrParameter` - a = spin × M
- `u_OuterHorizonRadius` - Kerr event horizon
- `u_ErgosphereRadius` - Equatorial ergosphere
- `u_BlackHoleSpinAxis` - Rotation axis
- `u_ShowErgosphere` - Toggle visualization
- `u_ErgosphereOpacity` - Ergosphere transparency
- `u_DiskISCO` - Inner disk edge from ISCO
- `u_DiskThickness` - Disk half-thickness
- `u_ShowPhotonSphere` - Toggle photon sphere visualization
- `u_PhotonSphereOpacity` - Photon sphere transparency
- `u_PhotonSphereRadius` - Calculated photon sphere radius

---

## Testing Checklist

- [x] Spin = 0 produces identical results to Schwarzschild
- [x] Ergosphere visualization works
- [x] Doppler beaming creates asymmetric disk brightness
- [x] Disk rotation animates correctly
- [ ] No visual artifacts at spin = 0.99 (near-extremal)
- [ ] Performance acceptable at all spin values
