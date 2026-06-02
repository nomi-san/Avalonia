using System;
using System.Runtime.InteropServices;

namespace X11NativeSample;

/// <summary>
/// P/Invoke bindings to the native x11_native library.
/// All mouse events and relative position tracking happen in C++ to avoid GC pressure.
/// </summary>
internal static class NativeX11Interop
{
    private const string LibName = "libx11_native";

    public enum NativeMouseEventType
    {
        Move = 0,
        LeftDown = 1,
        LeftUp = 2,
        RightDown = 3,
        RightUp = 4,
        MiddleDown = 5,
        MiddleUp = 6,
        Wheel = 7,
        Leave = 8,
    }

    public enum NativeKeyEventType
    {
        KeyDown = 0,
        KeyUp = 1,
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct NativeMouseEvent
    {
        public double RelX;
        public double RelY;
        public double AbsX;
        public double AbsY;
        public double DeltaX;
        public double DeltaY;
        public uint Buttons;
        public uint Modifiers;
        public ulong Timestamp;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct NativeKeyEvent
    {
        public uint Keycode;
        public uint Keysym;
        public uint Modifiers;
        public ulong Timestamp;
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public unsafe delegate void MouseEventCallback(NativeMouseEventType type, NativeMouseEvent* mouseEvent);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public unsafe delegate void KeyEventCallback(NativeKeyEventType type, NativeKeyEvent* keyEvent);

    // --- View API: native child window for Vulkan/GLX rendering ---

    [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
    public static extern ulong nativeview_create(IntPtr display, ulong parentWindow, int width, int height);

    [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
    public static extern void nativeview_destroy();

    [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
    public static extern ulong nativeview_get_handle();

    [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
    public static extern void nativeview_set_enabled(int enabled);

    [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
    public static extern int nativeview_get_enabled();

    [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
    public static extern void nativeview_resize(int width, int height);

    [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
    public static extern void nativeview_set_mouse_callback(MouseEventCallback callback);

    [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
    public static extern void nativeview_set_key_callback(KeyEventCallback callback);

    [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
    public static extern void nativeview_get_mouse_position(out double relX, out double relY);

    [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
    public static extern void nativeview_get_mouse_delta(out double deltaX, out double deltaY);

    [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
    public static extern void nativeview_set_relative_mouse_mode(int enabled);

    [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
    public static extern int nativeview_get_relative_mouse_mode();

    [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
    public static extern void nativeview_render_frame();

    [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
    public static extern int nativeview_process_xevent(IntPtr xevent);

    // --- Legacy top-level window API ---

    [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
    public static extern int native_init(IntPtr display, ulong window);

    [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
    public static extern void native_dispose();

    [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
    public static extern int native_process_xevent(IntPtr xevent);

    [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
    public static extern void native_set_mouse_callback(MouseEventCallback callback);

    [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
    public static extern void native_get_mouse_position(out double relX, out double relY);

    [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
    public static extern void native_get_mouse_delta(out double deltaX, out double deltaY);

    [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
    public static extern void native_set_relative_mouse_mode(int enabled);

    [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
    public static extern int native_get_relative_mouse_mode();
}
