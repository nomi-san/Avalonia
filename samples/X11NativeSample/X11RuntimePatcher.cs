using System;
using System.Reflection;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform;
using static X11NativeSample.NativeX11Interop;

namespace X11NativeSample;

/// <summary>
/// Patches Avalonia.X11 at runtime to intercept X11 events and delegate
/// mouse/input processing to native C++ code for zero-GC, high-performance handling.
/// </summary>
internal unsafe class X11RuntimePatcher : IDisposable
{
    private readonly Window _window;
    private IntPtr _display;
    private ulong _windowHandle;
    private MouseEventCallback? _nativeCallback;
    private bool _initialized;

    // Keep a strong reference to prevent GC collecting the delegate
    private static MouseEventCallback? s_pinnedCallback;

    public event Action<NativeMouseEventType, NativeMouseEvent>? NativeMouseEvent;

    public X11RuntimePatcher(Window window)
    {
        _window = window;
    }

    /// <summary>
    /// Initialize the native patcher after the window has been shown and the platform handle is available.
    /// Uses reflection to access internal Avalonia.X11 types at runtime.
    /// </summary>
    public bool Initialize()
    {
        if (_initialized) return true;

        try
        {
            var platformHandle = _window.TryGetPlatformHandle();
            if (platformHandle == null)
            {
                Console.WriteLine("[X11RuntimePatcher] No platform handle available yet.");
                return false;
            }

            _windowHandle = (ulong)platformHandle.Handle;
            Console.WriteLine($"[X11RuntimePatcher] Window handle: 0x{_windowHandle:X}");

            // Access the X11 display through reflection on the platform
            _display = GetX11Display();
            if (_display == IntPtr.Zero)
            {
                Console.WriteLine("[X11RuntimePatcher] Could not get X11 display.");
                return false;
            }

            Console.WriteLine($"[X11RuntimePatcher] Display: 0x{_display:X}");

            // Initialize native library
            int result = native_init(_display, _windowHandle);
            if (result != 1)
            {
                Console.WriteLine("[X11RuntimePatcher] native_init failed.");
                return false;
            }

            // Set up the callback from native to managed
            s_pinnedCallback = OnNativeMouseEvent;
            native_set_mouse_callback(s_pinnedCallback);

            _initialized = true;
            Console.WriteLine("[X11RuntimePatcher] Initialized successfully.");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[X11RuntimePatcher] Init error: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Gets the X11 Display pointer by reflecting into Avalonia.X11 internals.
    /// </summary>
    private IntPtr GetX11Display()
    {
        // Try to get X11 platform info via AvaloniaLocator or reflection
        var x11Assembly = FindX11Assembly();
        if (x11Assembly == null)
        {
            Console.WriteLine("[X11RuntimePatcher] Avalonia.X11 assembly not loaded.");
            return IntPtr.Zero;
        }

        // Get AvaloniaX11Platform instance
        var platformType = x11Assembly.GetType("Avalonia.X11.AvaloniaX11Platform");
        if (platformType == null)
        {
            Console.WriteLine("[X11RuntimePatcher] AvaloniaX11Platform type not found.");
            return IntPtr.Zero;
        }

        // Access via AvaloniaLocator
        var locatorType = typeof(AvaloniaLocator);
        var currentProp = locatorType.GetProperty("Current", BindingFlags.Public | BindingFlags.Static);
        var locator = currentProp?.GetValue(null);
        if (locator == null) return IntPtr.Zero;

        // Try to get IWindowingPlatform which is the X11 platform
        var getServiceMethod = locator.GetType().GetMethod("GetService");
        if (getServiceMethod == null) return IntPtr.Zero;

        var windowingType = typeof(Avalonia.Platform.IWindowingPlatform);
        var genericMethod = getServiceMethod.MakeGenericMethod(windowingType);
        var platform = genericMethod.Invoke(locator, null);

        if (platform == null || !platformType.IsAssignableFrom(platform.GetType()))
        {
            // Fallback: try to find it from loaded assemblies
            platform = FindX11PlatformInstance(x11Assembly);
        }

        if (platform == null)
        {
            Console.WriteLine("[X11RuntimePatcher] Could not find X11 platform instance.");
            return IntPtr.Zero;
        }

        // Get Info property -> X11Info.Display
        var infoProp = platformType.GetProperty("Info", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        var info = infoProp?.GetValue(platform);
        if (info == null) return IntPtr.Zero;

        var displayProp = info.GetType().GetProperty("Display", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        var display = displayProp?.GetValue(info);
        if (display is IntPtr ptr)
            return ptr;

        return IntPtr.Zero;
    }

    private static Assembly? FindX11Assembly()
    {
        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (asm.GetName().Name == "Avalonia.X11")
                return asm;
        }
        return null;
    }

    private static object? FindX11PlatformInstance(Assembly x11Assembly)
    {
        var platformType = x11Assembly.GetType("Avalonia.X11.AvaloniaX11Platform");
        if (platformType == null) return null;

        // Try X11Platform static class
        var x11PlatformType = x11Assembly.GetType("Avalonia.X11.X11Platform");
        if (x11PlatformType != null)
        {
            var platformField = x11PlatformType.GetField("Platform",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            if (platformField != null)
                return platformField.GetValue(null);
        }

        return null;
    }

    /// <summary>
    /// Process a raw X11 event through native code.
    /// Returns true if the event was consumed by native code.
    /// </summary>
    public bool ProcessXEvent(IntPtr xevent)
    {
        if (!_initialized) return false;
        return native_process_xevent(xevent) != 0;
    }

    /// <summary>
    /// Get the current relative mouse position (0.0 - 1.0 range).
    /// This is a direct native call with no GC allocation.
    /// </summary>
    public (double relX, double relY) GetRelativeMousePosition()
    {
        native_get_mouse_position(out double rx, out double ry);
        return (rx, ry);
    }

    /// <summary>
    /// Get accumulated mouse delta since last call.
    /// </summary>
    public (double deltaX, double deltaY) GetMouseDelta()
    {
        native_get_mouse_delta(out double dx, out double dy);
        return (dx, dy);
    }

    /// <summary>
    /// Enable or disable relative mouse mode (pointer lock).
    /// </summary>
    public void SetRelativeMouseMode(bool enabled)
    {
        native_set_relative_mouse_mode(enabled ? 1 : 0);
    }

    public bool IsRelativeMouseMode => native_get_relative_mouse_mode() != 0;

    private void OnNativeMouseEvent(NativeMouseEventType type, NativeMouseEvent* ev)
    {
        // This callback is invoked from native code on the same thread
        // No GC allocation here - just copy the struct
        NativeMouseEvent copy = *ev;
        NativeMouseEvent?.Invoke(type, copy);
    }

    public void Dispose()
    {
        if (_initialized)
        {
            native_dispose();
            _initialized = false;
        }
        s_pinnedCallback = null;
    }
}
