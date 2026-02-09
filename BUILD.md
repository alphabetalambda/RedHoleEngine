# RedHoleEngine Build Guide

## Prerequisites

### All Platforms
- [.NET 10 SDK](https://dotnet.microsoft.com/download) (or later)
- [Vulkan SDK](https://vulkan.lunarg.com/) (for shader compilation)

### Windows
- Visual Studio 2022 (optional, for IDE support)
- Windows 10/11 with Vulkan-capable GPU

### macOS
- Xcode Command Line Tools (`xcode-select --install`)
- MoltenVK (included via Silk.NET)

### Linux
- Vulkan drivers for your GPU
- X11 or Wayland development libraries

## Building

### Quick Build (All Projects)

```bash
dotnet build
```

### Build Specific Project

```bash
# Main engine library
dotnet build RedHoleEngine/RedHoleEngine.csproj

# Test scene with gravitational lensing demo
dotnet build RedHoleTestScene/RedHoleTestScene.csproj

# Editor
dotnet build RedHoleEngine.Editor/RedHoleEngine.Editor.csproj

# Game template
dotnet build RedHoleGame/RedHoleGame.csproj
```

### Release Build

```bash
dotnet build -c Release
```

## Running

### Test Scene (Gravitational Lensing Demo)

```bash
# Windows
dotnet run --project RedHoleTestScene/RedHoleTestScene.csproj

# macOS/Linux
cd RedHoleTestScene/bin/Debug/net10.0
./RedHoleTestScene
```

### Editor

```bash
dotnet run --project RedHoleEngine.Editor/RedHoleEngine.Editor.csproj
```

### Game Template

```bash
dotnet run --project RedHoleGame/RedHoleGame.csproj
```

## Shader Compilation

The raytracer uses Vulkan compute shaders that need to be compiled to SPIR-V format.

### Prerequisites
Ensure `glslangValidator` is in your PATH (comes with Vulkan SDK).

### Compile Shaders

```bash
cd RedHoleEngine/Rendering/Shaders

# Raytracer compute shader
glslangValidator --target-env vulkan1.0 -S comp -o raytracer_vulkan.spv raytracer_vulkan.comp

# Rasterizer shaders
glslangValidator --target-env vulkan1.0 -S vert -o raster.vert.spv raster.vert
glslangValidator --target-env vulkan1.0 -S frag -o raster.frag.spv raster.frag

# UI shaders
glslangValidator --target-env vulkan1.0 -S vert -o ui.vert.spv ui.vert
glslangValidator --target-env vulkan1.0 -S frag -o ui.frag.spv ui.frag

# Particle shaders
glslangValidator --target-env vulkan1.0 -S vert -o particle.vert.spv particle.vert
glslangValidator --target-env vulkan1.0 -S frag -o particle.frag.spv particle.frag

# Debug line shaders
glslangValidator --target-env vulkan1.0 -S vert -o debug_line.vert.spv debug_line.vert
glslangValidator --target-env vulkan1.0 -S frag -o debug_line.frag.spv debug_line.frag
```

### Compile All Shaders (Script)

```bash
cd RedHoleEngine/Rendering/Shaders
for f in *.vert; do glslangValidator --target-env vulkan1.0 -S vert -o "${f}.spv" "$f"; done
for f in *.frag; do glslangValidator --target-env vulkan1.0 -S frag -o "${f}.spv" "$f"; done
for f in *.comp; do glslangValidator --target-env vulkan1.0 -S comp -o "${f%.comp}.spv" "$f"; done
```

## Project Structure

```
RedHoleEngine/
├── RedHoleEngine/           # Core engine library
│   ├── Components/          # ECS components
│   ├── Core/                # Application, ECS, GameLoop
│   ├── Physics/             # Physics system, constraints
│   ├── Rendering/           # Vulkan backend, raytracer, shaders
│   └── Resources/           # Asset loading
├── RedHoleEngine.Editor/    # Scene editor with ImGui
├── RedHoleEngine.Tests/     # Unit tests
├── RedHoleGame/             # Game template project
├── RedHoleTestScene/        # Gravitational lensing demo
├── RedHoleLaserDemo/        # Laser system demo
└── RedHoleUnpixDemo/        # Dissolution effect demo
```

## Configuration

### Raytracer Quality Settings

The raytracer supports quality presets that can be configured in code:

```csharp
// In your scene setup
world.AddComponent(entity, new RenderSettingsComponent(RenderMode.Raytraced)
{
    RaysPerPixel = 1,
    MaxBounces = 2,
    Accumulate = false,
    
    // Lensing quality (Low/Medium/High/Ultra)
    LensingQuality = LensingQuality.Medium,
    LensingMaxSteps = 64,
    LensingStepSize = 0.4f,
    LensingBvhCheckInterval = 6
});
```

### Lensing Quality Presets

| Preset | Max Steps | Step Size | BVH Check Interval | Use Case |
|--------|-----------|-----------|-------------------|----------|
| Low    | 32        | 0.6       | 8                 | Real-time, lower-end GPUs |
| Medium | 64        | 0.4       | 6                 | Balanced (default) |
| High   | 128       | 0.25      | 4                 | High quality |
| Ultra  | 200       | 0.15      | 2                 | Screenshots, offline |

## Troubleshooting

### Windows: Initial Stuttering
NVIDIA drivers compile shaders on first use. This causes brief stuttering that fades as the shader cache builds. Use `LensingQuality.Low` for smoother initial experience.

### macOS: Vulkan Not Found
Ensure MoltenVK is properly loaded. The engine uses Silk.NET which bundles MoltenVK, but you may need to set:
```bash
export DYLD_LIBRARY_PATH=/path/to/vulkan/lib:$DYLD_LIBRARY_PATH
```

### Shader Compilation Errors
Ensure you have the Vulkan SDK installed and `glslangValidator` is in your PATH:
```bash
# Check installation
glslangValidator --version

# Windows: Add to PATH
set PATH=%VULKAN_SDK%\Bin;%PATH%

# macOS/Linux: Add to PATH
export PATH=$VULKAN_SDK/bin:$PATH
```

### Missing Debug Files
If you see errors about `DebugDrawManager` or `DebugRenderer`, ensure the `RedHoleEngine/Rendering/Debug/` folder is present. These files may be excluded by `.gitignore` patterns - use `git add -f` to force-add them.

## Running Tests

```bash
dotnet test RedHoleEngine.Tests/RedHoleEngine.Tests.csproj
```

## Package Warnings

The project uses `SixLabors.ImageSharp` which has known vulnerabilities. These are display-only warnings and don't affect the build. To suppress:

```bash
dotnet build -p:NoWarn=NU1902;NU1903
```

## IDE Support

### Visual Studio / Rider
Open `RedHoleEngine.sln` directly.

### VS Code
Install the C# extension and open the folder. A `tasks.json` for building can be created:

```json
{
    "version": "2.0.0",
    "tasks": [
        {
            "label": "build",
            "command": "dotnet",
            "type": "process",
            "args": ["build"],
            "problemMatcher": "$msCompile"
        }
    ]
}
```
