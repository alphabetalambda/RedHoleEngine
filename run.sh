#!/bin/bash
# Run RedHoleEngine with proper Vulkan/MoltenVK environment
# This script sets up DYLD_LIBRARY_PATH which must be set before process start on macOS

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
BUILD_DIR="$SCRIPT_DIR/RedHoleEngine/bin/Debug/net10.0"

# Add build directory to library path (contains libvulkan.dylib)
export DYLD_LIBRARY_PATH="$BUILD_DIR:${DYLD_LIBRARY_PATH:-}"

# Point Vulkan loader to bundled MoltenVK
export VK_ICD_FILENAMES="$BUILD_DIR/runtimes/osx/native/MoltenVK_icd.json"
export VK_DRIVER_FILES="$BUILD_DIR/runtimes/osx/native/MoltenVK_icd.json"

cd "$SCRIPT_DIR/RedHoleEngine"
dotnet run "$@"
