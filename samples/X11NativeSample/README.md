# X11 Native Sample

A sample app demonstrating how to use a native C++ library for high-performance X11 event handling with Avalonia, embedding a native child view inside a `NativeControlHost` for Vulkan/GLX rendering.

## Architecture

```
┌─────────────────────────────────────────────┐
│           C# Avalonia App                   │
│  (Window, Buttons, Status display)          │
├─────────────────────────────────────────────┤
│    NativeRenderView : NativeControlHost     │
│  (creates native child X11 window)          │
├─────────────────────────────────────────────┤
│    X11RuntimePatcher.cs                     │
│  (runtime reflection into Avalonia.X11)     │
├─────────────────────────────────────────────┤
│    NativeX11Interop.cs (P/Invoke)           │
├─────────────────────────────────────────────┤
│    libx11_native.so (C++)                   │
│  ┌─────────────────────────────────────┐    │
│  │  Native Child View (X11 Window)     │    │
│  │  - Mouse events (abs coord bound)   │    │
│  │  - Relative mouse mode              │    │
│  │  - Keyboard events                  │    │
│  │  - Vulkan/GLX rendering (stub)      │    │
│  │  - Enable/disable control           │    │
│  └─────────────────────────────────────┘    │
└─────────────────────────────────────────────┘
```

## Key Features

- **NativeControlHost integration**: The native X11 child window lives inside Avalonia's `NativeControlHost`, properly managed by the framework.
- **No GC pressure**: Mouse/keyboard events are processed in C++ with zero managed allocations per event.
- **Mouse absolute coordinates bound to view**: Position is always relative to the native view bounds.
- **Enable/disable via button**: Rendering, mouse, relative mouse, and keyboard events only activate when the user clicks "Enable View".
- **Relative mouse mode**: Pointer lock with delta accumulation for FPS-style input.
- **Vulkan/GLX ready**: The stub renderer uses X11 drawing; replace `nativeview_render_frame()` with your Vulkan/GLX present calls.

## Building

### Prerequisites

- .NET 10.0 SDK
- CMake 3.16+
- X11 development libraries (`libx11-dev` on Debian/Ubuntu)
- C++ compiler (g++ or clang++)

### Build the native library

```bash
chmod +x build-native.sh
./build-native.sh
```

### Build and run the C# app

```bash
dotnet run
```

## How It Works

1. **Native C++ library** (`native/x11_native.cpp`):
   - Creates a child X11 window under the NativeControlHost parent
   - Processes mouse events with absolute coordinates bound to the child view
   - Tracks relative mouse position (0.0–1.0) and delta accumulation
   - Handles keyboard events (KeyPress/KeyRelease with keysym)
   - Implements pointer lock for relative mouse mode
   - Renders frames (stub with animated gradient + crosshair)
   - Only processes input when enabled

2. **NativeRenderView** (`NativeRenderView.cs`):
   - Subclass of `NativeControlHost`
   - Calls `nativeview_create()` in `CreateNativeControlCore()` to make the child window
   - Manages enable/disable state and render loop
   - Exposes events for mouse and keyboard

3. **Runtime patching** (`X11RuntimePatcher.cs`):
   - Reflects into `Avalonia.X11` internals to get the X11 Display pointer
   - Passes it to the native library

## Controls

- **Enable View** button: Activates rendering + input on the native view
- **Toggle Relative Mouse** button: Locks pointer inside the view for delta input
- Mouse crosshair visible inside the native view shows absolute position
