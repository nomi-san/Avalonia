using System;
using System.Runtime.InteropServices;

namespace AndroidNativeOverlay.Android;

/// <summary>
/// P/Invoke bridge to the native C++ OpenGL ES renderer (libgl_renderer.so).
/// The native library uses EGL to create an OpenGL context on a given ANativeWindow
/// and renders content in a background thread.
/// </summary>
internal static class NativeGLRenderer
{
    private const string LibName = "gl_renderer";

    /// <summary>
    /// Initialize the renderer with an ANativeWindow handle and surface dimensions.
    /// Creates an EGL context, compiles shaders, and prepares for rendering.
    /// </summary>
    /// <returns>0 on success, non-zero on failure.</returns>
    [DllImport(LibName, EntryPoint = "renderer_init")]
    internal static extern int Init(IntPtr nativeWindow, int width, int height);

    /// <summary>
    /// Start the background render thread. Frames are rendered continuously.
    /// </summary>
    [DllImport(LibName, EntryPoint = "renderer_start")]
    internal static extern void Start();

    /// <summary>
    /// Stop the background render thread. Rendering is paused but resources are retained.
    /// </summary>
    [DllImport(LibName, EntryPoint = "renderer_stop")]
    internal static extern void Stop();

    /// <summary>
    /// Handle a surface resize. Updates the GL viewport.
    /// </summary>
    [DllImport(LibName, EntryPoint = "renderer_resize")]
    internal static extern void Resize(int width, int height);

    /// <summary>
    /// Set the rotation speed of the rendered geometry (radians per second).
    /// </summary>
    [DllImport(LibName, EntryPoint = "renderer_set_speed")]
    internal static extern void SetSpeed(float radiansPerSecond);

    /// <summary>
    /// Destroy the renderer, releasing all EGL and GL resources.
    /// Must be called when the surface is destroyed.
    /// </summary>
    [DllImport(LibName, EntryPoint = "renderer_destroy")]
    internal static extern void Destroy();
}
