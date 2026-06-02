#!/bin/bash
# Build the native C++ library for the X11NativeSample
set -e

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
NATIVE_DIR="$SCRIPT_DIR/native"
BUILD_DIR="$NATIVE_DIR/build"

echo "Building native x11_native library..."

mkdir -p "$BUILD_DIR"
cd "$BUILD_DIR"

cmake .. -DCMAKE_BUILD_TYPE=Release
cmake --build . --config Release

echo ""
echo "Built: $BUILD_DIR/libx11_native.so"
echo ""
echo "To run the sample:"
echo "  cd $SCRIPT_DIR"
echo "  dotnet run"
