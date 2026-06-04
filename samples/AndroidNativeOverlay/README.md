# AndroidNativeOverlay Sample

This sample demonstrates how to layer **native Android views** with **Avalonia UI** on Android — embedding a native OpenGL ES surface rendered by C++ NDK code, and then showing an Avalonia XAML overlay/popup on top of it. This is analogous to using frameless child windows as overlays on desktop platforms.

Related discussion: [AvaloniaUI/Avalonia#10228](https://github.com/AvaloniaUI/Avalonia/discussions/10228)

## Architecture

The sample uses a three-layer view hierarchy managed by a single Android `Activity`:

```
┌──────────────────────────────────────────┐
│  Layer 3: Overlay AvaloniaView (top)     │  ← Avalonia XAML popup/overlay
│  ┌────────────────────────────────────┐  │     (click-through capable)
│  │  OverlayPanel.axaml               │  │
│  │  Semi-transparent floating card    │  │
│  └────────────────────────────────────┘  │
├──────────────────────────────────────────┤
│  Layer 2: SurfaceView + C++ OpenGL ES    │  ← Native NDK rendering
│  ┌────────────────────────────────────┐  │     (own EGL context, own thread)
│  │  Rotating triangle via gl_renderer│  │
│  └────────────────────────────────────┘  │
├──────────────────────────────────────────┤
│  Layer 1: Base AvaloniaView (bottom)     │  ← Main Avalonia content
│  ┌────────────────────────────────────┐  │
│  │  MainView.axaml                   │  │
│  │  Title, buttons, status           │  │
│  └────────────────────────────────────┘  │
└──────────────────────────────────────────┘
```

### Key Concepts

1. **`AvaloniaView`** is a native Android `FrameLayout` widget. You can create multiple instances and place them in your own Android view hierarchy — you don't need `AvaloniaMainActivity`.

2. **Native GL rendering** uses a plain Android `SurfaceView`. The C++ code obtains an `ANativeWindow*` from the surface, creates its own EGL context, and renders OpenGL ES 3.0 content on a dedicated background thread. This is completely independent of Avalonia's rendering pipeline.

3. **Overlay/popup** is achieved by adding a second `AvaloniaView` on top of the native surface in the Android `FrameLayout` z-order. The overlay panel is defined in Avalonia XAML and supports click-through mode.

## Project Structure

```
AndroidNativeOverlay/                        # Shared Avalonia UI (netX.0)
├── App.axaml / App.axaml.cs                 # Avalonia Application
├── MainView.axaml / MainView.axaml.cs       # Base layer content
├── OverlayPanel.axaml / OverlayPanel.axaml.cs  # Overlay panel content
├── OverlayService.cs                        # Cross-view event bus
└── AndroidNativeOverlay.csproj

AndroidNativeOverlay.Android/                # Android entry point
├── Application.cs                           # AvaloniaAndroidApplication<App>
├── MainActivity.cs                          # 3-layer view hierarchy manager
├── NativeGLRenderer.cs                      # P/Invoke bridge to C++ renderer
├── Properties/AndroidManifest.xml
├── Resources/values/styles.xml
├── AndroidNativeOverlay.Android.csproj      # Includes NDK build targets
└── jni/                                     # C++ NDK source
    ├── CMakeLists.txt                       # CMake build for libgl_renderer.so
    ├── gl_renderer.h                        # Public API header
    └── gl_renderer.c                        # OpenGL ES 3.0 renderer
```

## Building

### Prerequisites

- .NET 10 SDK (or the version matching the repo's `global.json`)
- Android SDK with platform API 36+
- Android NDK (r25+ recommended) — install via Android SDK Manager

### Build Steps

1. **Set NDK path** (if not auto-detected):
   ```bash
   export ANDROID_NDK_HOME=/path/to/android-ndk
   ```

2. **Build the project**:
   ```bash
   dotnet build samples/AndroidNativeOverlay.Android/AndroidNativeOverlay.Android.csproj
   ```
   The MSBuild targets in the `.csproj` will automatically invoke CMake to build `libgl_renderer.so` for `arm64-v8a` and `x86_64`.

3. **Run on device/emulator**:
   ```bash
   dotnet build -t:Run samples/AndroidNativeOverlay.Android/AndroidNativeOverlay.Android.csproj
   ```

### Manual NDK Build

If the automatic build doesn't work, you can build the native library manually:

```bash
cd samples/AndroidNativeOverlay.Android

# For arm64-v8a
cmake -S jni -B build/arm64 \
  -DCMAKE_TOOLCHAIN_FILE=$ANDROID_NDK_HOME/build/cmake/android.toolchain.cmake \
  -DANDROID_ABI=arm64-v8a \
  -DANDROID_PLATFORM=android-24 \
  -DCMAKE_BUILD_TYPE=Release

cmake --build build/arm64 --config Release

# Copy the .so to the project
mkdir -p libs/arm64-v8a
cp build/arm64/libgl_renderer.so libs/arm64-v8a/
```

Then add to the `.csproj`:
```xml
<ItemGroup>
  <AndroidNativeLibrary Include="libs\arm64-v8a\libgl_renderer.so" Abi="arm64-v8a" />
</ItemGroup>
```

## How It Works

### Native GL Rendering (C++ side)

The C++ renderer (`jni/gl_renderer.c`) follows this lifecycle:

1. `renderer_init(ANativeWindow*, w, h)` — Creates EGL display, surface, and context on the provided native window. Compiles GLSL shaders and sets up vertex buffers.
2. `renderer_start()` — Spawns a `pthread` that runs the render loop at ~60fps. Each frame rotates a colored triangle and calls `eglSwapBuffers`.
3. `renderer_resize(w, h)` — Thread-safe viewport update via atomics.
4. `renderer_stop()` — Signals the thread to exit and joins it.
5. `renderer_destroy()` — Cleans up all EGL/GL resources and releases the native window.

### View Hierarchy (C# side)

`MainActivity` extends `AppCompatActivity` (not `AvaloniaMainActivity`) for full control over the view hierarchy:

```
FrameLayout (root)
├── AvaloniaView { Content = MainView }      ← MATCH_PARENT
├── SurfaceView + ISurfaceHolderCallback     ← centered with margins
└── FrameLayout (overlay container)          ← MATCH_PARENT, toggled visibility
    └── AvaloniaView { Content = OverlayPanel }
```

### Click-Through Overlay

The overlay supports two modes:
- **Interactive** (default): The overlay panel receives touch events normally.
- **Click-through**: A `PassthroughTouchListener` is attached that returns `false` for all touch events, allowing them to fall through to the GL surface and base Avalonia view below.
