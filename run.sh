#!/bin/bash
# Run RedHoleEngine. No Vulkan/MoltenVK environment setup is needed: on macOS the engine loads
# the bundled libMoltenVK.dylib directly (see RedHoleEngine/Rendering/Backends/MoltenVkLoader.cs).

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"

cd "$SCRIPT_DIR/RedHoleEngine"
dotnet run "$@"
