# X11 Native Sample

A sample app demonstrating how to use a native C++ library for high-performance X11 event handling with Avalonia, while patching `Avalonia.X11` at runtime via reflection.

## Architecture

```
┌─────────────────────────────────┐
│       C# Avalonia App           │
│  (UI, window management)        │
├─────────────────────────────────┤
│    X11RuntimePatcher.cs         │
│  (runtime reflection patch)     │
├─────────────────────────────────┤
│    NativeX11Interop.cs          │
│  (P/Invoke bindings)            │
├─────────────────────────────────┤
│    libx11_native.so (C++)       │
│  (events, relative mouse,      │
│   zero GC allocation)           │
└─────────────────────────────────┘
```

## Why C++ for Events?

- **No GC pressure**: Mouse events fire at high frequency (hundreds/sec). Processing them in C++ avoids allocating managed objects that would trigger garbage collection pauses.
- **Relative mouse mode**: Pointer locking and delta accumulation are handled natively with no managed overhead.
- **High performance**: Direct X11 event processing without marshaling through the .NET runtime for every event.

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
   - Processes raw X11 events (MotionNotify, ButtonPress, ButtonRelease, etc.)
   - Tracks relative mouse position (0.0–1.0 range)
   - Accumulates mouse deltas for relative mode
   - Implements pointer lock (grab/warp) for FPS-style input
   - Calls back to C# via function pointer (no GC allocation)

2. **Runtime patching** (`X11RuntimePatcher.cs`):
   - Uses reflection to access internal `Avalonia.X11` types at runtime
   - Gets the X11 `Display` pointer and window handle from Avalonia
   - Passes them to the native library for initialization

3. **P/Invoke bindings** (`NativeX11Interop.cs`):
   - Thin layer mapping C function signatures to C# delegates
   - Uses `unsafe` and raw pointers for zero-copy event data

## Controls

- **R**: Toggle relative mouse mode (pointer lock)
- **Escape**: Exit relative mouse mode
