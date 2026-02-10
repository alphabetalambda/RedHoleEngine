# PBR System Roadmap

This document outlines the current state and planned features for RedHoleEngine's Physically Based Rendering (PBR) system.

## Current Implementation (v1.0)

### Completed Features

- **PBR Material System**
  - `PbrMaterial` class with metallic-roughness workflow
  - Base color, metallic, roughness, normal, emissive properties
  - Material serialization to `.rhmat` JSON format
  - `MaterialLibrary` for managing material collections

- **GPU Material Buffer**
  - `GpuMaterial` struct (80 bytes) uploaded to compute shader
  - Texture indices for base color, metallic-roughness, normal, and emissive maps
  - Binding 5 in Vulkan descriptor set

- **Texture System**
  - `TextureLibrary` for loading and managing textures
  - Texture array support (up to 32 textures) at binding 6
  - Linear filtering with anisotropic sampling

- **Shader Implementation**
  - GGX normal distribution function
  - Smith geometry function (height-correlated)
  - Schlick Fresnel approximation
  - UV coordinate interpolation via barycentric coordinates
  - Normal mapping with TBN matrix construction

- **Editor Integration**
  - `MaterialEditorPanel` with ImGui interface
  - Texture slot editing (path-based, manual entry)
  - Real-time material property editing

## Planned Features

### Phase 2: Enhanced Editor Experience

- [ ] **Material Preview Sphere**
  - Real-time material preview in editor panel
  - Rotate/zoom controls for inspection
  - Multiple environment lighting presets

- [ ] **File Browser for Textures**
  - Native file dialog integration
  - Thumbnail previews for texture selection
  - Drag-and-drop texture assignment

- [ ] **Material Thumbnails**
  - Generate preview icons for material library
  - Quick visual selection in asset browser

### Phase 3: Image-Based Lighting (IBL)

- [ ] **HDR Environment Maps**
  - Load HDR/EXR environment images
  - Equirectangular to cubemap conversion
  - Sky rendering from environment map

- [ ] **Prefiltered Environment Maps**
  - Generate diffuse irradiance map (Lambert)
  - Generate specular prefiltered maps (split-sum approximation)
  - BRDF integration LUT

- [ ] **Ambient Occlusion**
  - AO texture channel support
  - SSAO (Screen Space Ambient Occlusion) post-process

### Phase 4: Advanced Materials

- [ ] **Additional Texture Types**
  - Height/displacement maps (parallax occlusion mapping)
  - Subsurface scattering approximation
  - Clear coat layer
  - Anisotropic reflections

- [ ] **Material Blending**
  - Blend between multiple materials
  - Height-based blending for terrain
  - Triplanar projection for organic surfaces

### Phase 5: PBR Graph Editor

A visual node-based material editor inspired by Maya's Hypershade and Unreal's Material Editor.

- [ ] **Node System Architecture**
  - Base node class with inputs/outputs
  - Connection validation and type checking
  - Execution order determination

- [ ] **Input Nodes**
  - Texture sampler nodes
  - UV coordinate nodes (with transform options)
  - Vertex data nodes (position, normal, color)
  - Time/animation nodes

- [ ] **Math Nodes**
  - Basic arithmetic (add, multiply, etc.)
  - Vector operations (dot, cross, normalize)
  - Trigonometric functions
  - Interpolation (lerp, smoothstep)

- [ ] **Utility Nodes**
  - Color conversion (RGB/HSV)
  - Noise generators (Perlin, Voronoi, etc.)
  - Gradient mapping
  - Fresnel calculations

- [ ] **Output Nodes**
  - PBR output (base color, metallic, roughness, etc.)
  - Unlit/emissive output
  - Custom shader output

- [ ] **Graph Compilation**
  - Generate optimized GLSL from node graph
  - Automatic uniform/texture binding
  - Hot-reload support for live editing

- [ ] **Graph Serialization**
  - Save/load node graphs to `.rhmatgraph` format
  - Export to standard `.rhmat` materials
  - Template/preset system

### Phase 6: Performance Optimizations

- [ ] **Texture Streaming**
  - Virtual texturing / megatextures
  - Mipmapping with anisotropic filtering
  - Async texture loading

- [ ] **Material LOD**
  - Simplify materials at distance
  - Reduce texture samples for performance

- [ ] **GPU-Driven Rendering**
  - Indirect draw calls
  - Material batching by shader variant

## Architecture Notes

### Current File Structure

```
RedHoleEngine/
├── Rendering/
│   ├── PBR/
│   │   ├── PbrMaterial.cs       # Material data + GpuMaterial struct
│   │   ├── MaterialLibrary.cs   # Collection management
│   │   ├── MaterialSerializer.cs # .rhmat file I/O
│   │   └── TextureLibrary.cs    # Texture loading & GPU upload
│   ├── Raytracing/
│   │   ├── RaytracerMeshTypes.cs # Triangle struct with UVs
│   │   └── RaytracerMeshSystem.cs # Mesh → triangle conversion
│   └── Shaders/
│       └── raytracer_vulkan.comp # PBR BRDF implementation

RedHoleEngine.Editor/
└── UI/Panels/
    └── MaterialEditorPanel.cs    # ImGui material editor
```

### Shader Bindings

| Binding | Description |
|---------|-------------|
| 0 | Output image |
| 1 | BVH nodes buffer |
| 2 | Triangle buffer |
| 3 | Uniform buffer (camera, settings) |
| 4 | Black hole parameters |
| 5 | Material buffer (`GpuMaterial[]`) |
| 6 | Texture array (32 slots) |

### Material File Format (.rhmat)

```json
{
  "Name": "BrushedMetal",
  "BaseColor": [0.8, 0.8, 0.82, 1.0],
  "Metallic": 1.0,
  "Roughness": 0.3,
  "BaseColorTexturePath": "textures/metal_basecolor.png",
  "NormalTexturePath": "textures/metal_normal.png",
  "MetallicRoughnessTexturePath": "textures/metal_roughness.png"
}
```

## Contributing

When adding new PBR features:

1. Update `GpuMaterial` struct if adding new material properties
2. Update shader to sample/use new properties
3. Add UI controls in `MaterialEditorPanel`
4. Update serialization in `MaterialSerializer`
5. Document changes in this roadmap
