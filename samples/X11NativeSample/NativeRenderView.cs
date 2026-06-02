using System;
using Avalonia.Controls;
using Avalonia.Controls.Platform;
using Avalonia.Platform;
using Avalonia.Threading;
using static X11NativeSample.NativeX11Interop;

namespace X11NativeSample;

/// <summary>
/// A NativeControlHost that creates a native X11 child window for Vulkan/GLX rendering.
/// All mouse events (absolute coordinates bound to this view), relative mouse, and keyboard
/// events are processed in native C++ code to avoid GC pressure.
/// Rendering and input are only active when explicitly enabled via <see cref="SetEnabled"/>.
/// </summary>
internal unsafe class NativeRenderView : NativeControlHost, IDisposable
{
    private IntPtr _display;
    private ulong _childHandle;
    private bool _enabled;
    private DispatcherTimer? _renderTimer;

    // Prevent GC collection of native callbacks
    private static MouseEventCallback? s_mouseCallback;
    private static KeyEventCallback? s_keyCallback;

    /// <summary>
    /// Fired when the native view receives a mouse event (only when enabled).
    /// </summary>
    public event Action<NativeMouseEventType, NativeMouseEvent>? ViewMouseEvent;

    /// <summary>
    /// Fired when the native view receives a keyboard event (only when enabled).
    /// </summary>
    public event Action<NativeKeyEventType, NativeKeyEvent>? ViewKeyEvent;

    public NativeRenderView()
    {
    }

    /// <summary>
    /// Sets the X11 display pointer (obtained via runtime reflection on Avalonia.X11).
    /// Must be called before the control is attached to the visual tree.
    /// </summary>
    public void SetDisplay(IntPtr display)
    {
        _display = display;
    }

    /// <summary>
    /// Creates the native X11 child window under the given parent.
    /// This is called by Avalonia's NativeControlHost infrastructure.
    /// </summary>
    protected override IPlatformHandle CreateNativeControlCore(IPlatformHandle parent)
    {
        if (_display == IntPtr.Zero)
        {
            Console.WriteLine("[NativeRenderView] ERROR: Display not set, falling back to default.");
            return base.CreateNativeControlCore(parent);
        }

        ulong parentXid = (ulong)parent.Handle;
        int width = Math.Max((int)Bounds.Width, 320);
        int height = Math.Max((int)Bounds.Height, 240);

        _childHandle = nativeview_create(_display, parentXid, width, height);
        if (_childHandle == 0)
        {
            Console.WriteLine("[NativeRenderView] ERROR: nativeview_create failed.");
            return base.CreateNativeControlCore(parent);
        }

        // Set up callbacks
        s_mouseCallback = OnNativeMouseEvent;
        s_keyCallback = OnNativeKeyEvent;
        nativeview_set_mouse_callback(s_mouseCallback);
        nativeview_set_key_callback(s_keyCallback);

        Console.WriteLine($"[NativeRenderView] Created native view: 0x{_childHandle:X} ({width}x{height})");

        return new PlatformHandle((IntPtr)_childHandle, "XID");
    }

    /// <summary>
    /// Destroys the native child window.
    /// </summary>
    protected override void DestroyNativeControlCore(IPlatformHandle control)
    {
        StopRenderLoop();
        nativeview_destroy();
        _childHandle = 0;
        s_mouseCallback = null;
        s_keyCallback = null;
        Console.WriteLine("[NativeRenderView] Destroyed native view.");
    }

    /// <summary>
    /// Enable or disable the native view's input and rendering.
    /// When disabled, mouse/keyboard/rendering are all inactive.
    /// </summary>
    public void SetEnabled(bool enabled)
    {
        _enabled = enabled;
        nativeview_set_enabled(enabled ? 1 : 0);

        if (enabled)
        {
            StartRenderLoop();
        }
        else
        {
            StopRenderLoop();
            // Also disable relative mouse mode
            nativeview_set_relative_mouse_mode(0);
        }
    }

    public bool IsEnabled => _enabled;

    /// <summary>
    /// Enable/disable relative mouse mode on the view.
    /// Only works when the view is enabled.
    /// </summary>
    public void SetRelativeMouseMode(bool enabled)
    {
        if (!_enabled && enabled) return;
        nativeview_set_relative_mouse_mode(enabled ? 1 : 0);
    }

    public bool IsRelativeMouseMode => nativeview_get_relative_mouse_mode() != 0;

    /// <summary>
    /// Get the last known mouse position relative to this view (0.0-1.0).
    /// </summary>
    public (double relX, double relY) GetMousePosition()
    {
        nativeview_get_mouse_position(out double rx, out double ry);
        return (rx, ry);
    }

    /// <summary>
    /// Get accumulated mouse delta since last call.
    /// </summary>
    public (double deltaX, double deltaY) GetMouseDelta()
    {
        nativeview_get_mouse_delta(out double dx, out double dy);
        return (dx, dy);
    }

    private void StartRenderLoop()
    {
        if (_renderTimer != null) return;
        // ~60 FPS render loop (stub rendering; replace with Vulkan/GLX present calls)
        _renderTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        _renderTimer.Tick += (_, _) => nativeview_render_frame();
        _renderTimer.Start();
    }

    private void StopRenderLoop()
    {
        _renderTimer?.Stop();
        _renderTimer = null;
    }

    private void OnNativeMouseEvent(NativeMouseEventType type, NativeMouseEvent* ev)
    {
        NativeMouseEvent copy = *ev;
        ViewMouseEvent?.Invoke(type, copy);
    }

    private void OnNativeKeyEvent(NativeKeyEventType type, NativeKeyEvent* ev)
    {
        NativeKeyEvent copy = *ev;
        ViewKeyEvent?.Invoke(type, copy);
    }

    public void Dispose()
    {
        StopRenderLoop();
        if (_childHandle != 0)
        {
            nativeview_destroy();
            _childHandle = 0;
        }
        s_mouseCallback = null;
        s_keyCallback = null;
    }

    /// <summary>
    /// A simple IPlatformHandle implementation for the native XID.
    /// </summary>
    private class PlatformHandle : IPlatformHandle, INativeControlHostDestroyableControlHandle
    {
        public PlatformHandle(IntPtr handle, string descriptor)
        {
            Handle = handle;
            HandleDescriptor = descriptor;
        }

        public IntPtr Handle { get; }
        public string HandleDescriptor { get; }

        public void Destroy()
        {
            // Destruction is handled by nativeview_destroy
        }
    }
}
