# RedHoleEngine

**A C#/.NET 10 game engine with scientifically accurate black hole rendering and machine learning integration**

RedHoleEngine is a modern game engine built around a unique specialization: real-time, physically-accurate visualization of black holes using general relativistic raytracing. The engine renders both Schwarzschild (non-rotating) and Kerr (rotating) black holes with proper geodesic integration, frame dragging, ergosphere visualization, and relativistic Doppler effects.

---

## Features

### Black Hole Rendering
- **Kerr Metric Geodesic Integration** — Accurate light paths around rotating black holes
- **Frame Dragging (Lense-Thirring Effect)** — Spacetime dragged by black hole rotation
- **Ergosphere Visualization** — Region where nothing can remain stationary
- **Photon Sphere** — Unstable light orbits at 1.5x Schwarzschild radius
- **Accretion Disk** — Temperature-based coloring with Doppler beaming and gravitational redshift

### Rendering
- **Vulkan Compute Raytracer** — GPU-accelerated raytracing via compute shaders
- **PBR Materials** — Full metallic-roughness workflow (glTF 2.0 compatible)
- **BVH Acceleration** — Fast ray-triangle intersection
- **HDR Environment Mapping** — Image-based lighting support
- **Progressive Accumulation** — High-quality sample accumulation

### Architecture
- **Entity Component System** — Lightweight, performant ECS design
- **Physics System** — Collision detection, constraints, gravitational attraction
- **Acoustic Raytracing** — Physics-based audio with gravitational redshift
- **Particle System** — GPU-accelerated particles with emission shapes

### Machine Learning
- **ML.NET Integration** — Built-in machine learning for game AI
- **Player Analytics** — Track behavior, predict engagement and skill
- **Adaptive Difficulty** — Dynamic difficulty based on player performance
- **Anomaly Detection** — Identify unusual player behavior
- **Standalone Trainer** — ImGui app for visual model training

### Model Import
- **glTF 2.0 / GLB** — Native support via SharpGLTF
- **FBX, OBJ, DAE, USD** — Multi-format support via AssimpNet
- **PBR Material Conversion** — Automatic material mapping
- **Skeletal Animation** — Bone weights and keyframe import

### Editor
- **ImGui-based Scene Editor** — Dockable panels, transform gizmos
- **Material Editor** — Live PBR material preview
- **Raytracer Settings** — Quality presets and lensing options
- **Scene Serialization** — JSON-based scene format

---

## Tech Stack

| Category | Technology |
|----------|------------|
| Runtime | .NET 10, C# 13 |
| Graphics | Vulkan (Silk.NET), MoltenVK for macOS |
| Audio | OpenAL (Silk.NET) |
| UI | ImGui.NET |
| ML | Microsoft.ML, FastTree, LightGBM |
| Models | SharpGLTF, AssimpNet |
| Images | SixLabors.ImageSharp |

---

## Projects

| Project | Description |
|---------|-------------|
| `RedHoleEngine` | Core engine library |
| `RedHoleEngine.Editor` | Scene editor application |
| `RedHoleML.Trainer` | ML model training tool |
| `RedHoleTestScene` | Gravitational lensing demo |
| `RedHoleLaserDemo` | Laser system demo |
| `RedHoleUnpixDemo` | Dissolution effect demo |

---

## Quick Start

```csharp
// Create a black hole with accretion disk
var blackHole = world.CreateEntity();
world.AddComponent(blackHole, new TransformComponent { Position = Vector3.Zero });
world.AddComponent(blackHole, new GravitySourceComponent 
{ 
    Mass = 1e6f,
    SchwarzschildRadius = 5f,
    SpinParameter = 0.9f  // Kerr parameter (0 = Schwarzschild, 1 = extremal)
});
world.AddComponent(blackHole, new AccretionDiskComponent
{
    InnerRadius = 3f,
    OuterRadius = 15f,
    Temperature = 5000f
});
```

---

## Platform Support

- **Windows** — Native Vulkan
- **macOS** — MoltenVK (Metal backend)
- **Linux** — Vulkan

---

## Use Cases

- Space and sci-fi games with realistic gravitational effects
- Educational visualization of general relativity
- Scientific rendering of black hole physics
- Games with ML-powered adaptive difficulty

---

## License

MIT License
