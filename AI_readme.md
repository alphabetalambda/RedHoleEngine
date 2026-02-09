# AI Readme (RedHoleEngine)

This file is a concise, AI-oriented guide to the RedHoleEngine codebase: architecture, style, and conventions.

## High-level overview
- C# .NET engine + editor + tests, targeting `net10.0`.
- Core runtime is an ECS-based engine with rendering (Vulkan), physics, audio, particles, and resource management.
- Editor is an ImGui-based docked UI for scene/entity manipulation.
- Tests are xUnit-based, focused on physics, ECS, audio, and integration behaviors.

## Repo layout
- `RedHoleEngine/`: main engine runtime (core loop, ECS, rendering, physics, audio, resources).
- `RedHoleEngine.Editor/`: editor app using ImGui + OpenGL for tooling UI.
- `RedHoleEngine.Tests/`: unit/integration tests.
- `RedHoleEngine.sln`: solution with three projects.

## Key runtime flow
- Entry point: `RedHoleEngine/Program.cs` creates `Application` and calls `Run(...)`.
- `Core/Application.cs` boots windowing, input, ECS world, systems, and backend.
- `Core/GameLoop.cs` drives fixed-step physics and variable update/render.
- `Core/Scene/Scene.cs` holds the active ECS `World` and orchestrates scene updates.

## ECS conventions
- ECS is in `RedHoleEngine/Core/ECS/`.
- Components are `struct`s implementing `IComponent` (see `RedHoleEngine/Components/`).
- Systems derive from `GameSystem` (or `ComponentSystem<...>`) and are added to `World`.
- Query patterns:
  - `World.Query<T>()`, `World.Query<T1, T2>()`, etc.
  - Access components via `ref var c = ref World.GetComponent<T>(entity)`.
- System priority uses `GameSystem.Priority` (lower runs earlier).

## Rendering conventions
- `Rendering/Backends/VulkanBackend.cs` is the primary graphics backend.
- `Rendering/IGraphicsBackend.cs` defines backend interface.
- Raytracing settings live in `Rendering/RaytracerSettings.cs` and `Rendering/RenderSettings.cs`.
- Shaders are in `RedHoleEngine/Rendering/Shaders/` and copied to output by the project file.
- Vulkan compute uses precompiled SPIR-V for `raytracer_vulkan.spv`.

## Physics conventions
- Physics system in `Physics/PhysicsSystem.cs` synchronizes ECS and physics world.
- Collider + rigid body components live in `Components/PhysicsComponents.cs` and `Physics/Collision/`.
- Physics debug draw uses `Rendering/Debug/`.

## Audio conventions
- Audio systems and backends are in `Audio/`.
- Clips are in `Audio/Clips/` and copied to output by the project file.

## Editor conventions
- Editor entry: `RedHoleEngine.Editor/Program.cs`.
- ImGui controller wrapper in `RedHoleEngine.Editor/UI/ImGuiController.cs`.
- Panels live in `RedHoleEngine.Editor/UI/Panels/` and are added in `EditorApplication`.
- Editor uses OpenGL for UI, separate from runtime Vulkan backend.

## Code style
- File-scoped namespaces are used (`namespace X;`).
- 4-space indentation, braces on new lines.
- `PascalCase` for types/methods/properties; `camelCase` for locals and parameters.
- Private fields often use `_underscore` prefix.
- Nullable reference types enabled (`<Nullable>enable</Nullable>`), avoid null where possible.
- XML doc comments used for public APIs and important classes.
- `var` used for local inference when clear.

## Project/build details
- Engine project: `RedHoleEngine/RedHoleEngine.csproj` (TargetFramework `net10.0`).
- Editor project: `RedHoleEngine.Editor/RedHoleEngine.Editor.csproj`.
- Test project: `RedHoleEngine.Tests/RedHoleEngine.Tests.csproj`.
- On macOS, `RedHoleEngine.csproj` copies Vulkan loader and builds a native launcher.

## AI contribution guidance
- Prefer adding new runtime systems under `RedHoleEngine/` and editor-only tooling under `RedHoleEngine.Editor/`.
- Match existing naming patterns for components (`*Component`) and systems (`*System`).
- Use ECS helpers in `Application` when adding gameplay-facing helper APIs.
- If adding shaders or audio clips, ensure they are under existing folders so they are copied to output.
- Keep changes isolated to relevant projects; avoid mixing editor UI code into runtime.

## Useful file entry points
- Engine entry: `RedHoleEngine/Program.cs`
- App lifecycle: `RedHoleEngine/Core/Application.cs`
- ECS: `RedHoleEngine/Core/ECS/World.cs`
- Physics: `RedHoleEngine/Physics/PhysicsSystem.cs`
- Rendering backend: `RedHoleEngine/Rendering/Backends/VulkanBackend.cs`
- Editor app: `RedHoleEngine.Editor/EditorApplication.cs`

## Tests
- xUnit tests in `RedHoleEngine.Tests/`.
- Test focus areas: physics, ECS, audio, integration scenarios.

If anything in this file conflicts with the current code, prefer the code as the source of truth and update this document accordingly.
