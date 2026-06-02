#include "x11_native.h"
#include <X11/Xlib.h>
#include <X11/Xutil.h>
#include <X11/keysym.h>
#include <cstring>
#include <cstdio>
#include <atomic>
#include <cmath>

// ============================================================================
// Native child view state (for NativeControlHost embedding)
// ============================================================================

static Display* v_display = nullptr;
static Window v_window = 0;
static Window v_parent = 0;
static int v_width = 1;
static int v_height = 1;
static std::atomic<bool> v_enabled{false};
static std::atomic<bool> v_relative_mode{false};
static MouseEventCallback v_mouse_callback = nullptr;
static KeyEventCallback v_key_callback = nullptr;

// View mouse state
static double v_last_x = 0.0;
static double v_last_y = 0.0;
static double v_delta_x = 0.0;
static double v_delta_y = 0.0;
static int v_center_x = 0;
static int v_center_y = 0;
static bool v_ignore_next_motion = false;

// Render state (stub: animated color)
static unsigned long v_frame_count = 0;
static GC v_gc = 0;

static uint32_t view_translate_modifiers(unsigned int state) {
    uint32_t mods = 0;
    if (state & ShiftMask)   mods |= (1 << 0);
    if (state & ControlMask) mods |= (1 << 1);
    if (state & Mod1Mask)    mods |= (1 << 2);
    if (state & Mod4Mask)    mods |= (1 << 3);
    return mods;
}

static uint32_t view_translate_buttons(unsigned int state) {
    uint32_t buttons = 0;
    if (state & Button1Mask) buttons |= (1 << 0);
    if (state & Button2Mask) buttons |= (1 << 1);
    if (state & Button3Mask) buttons |= (1 << 2);
    return buttons;
}

static void view_dispatch_mouse(NativeMouseEventType type, double x, double y,
                                 unsigned int state, unsigned long time) {
    if (!v_mouse_callback || !v_enabled) return;

    NativeMouseEvent ev;
    memset(&ev, 0, sizeof(ev));

    // Absolute position relative to the view
    ev.abs_x = x;
    ev.abs_y = y;
    // Relative position normalized to view dimensions (0.0-1.0)
    if (v_width > 0 && v_height > 0) {
        ev.rel_x = x / (double)v_width;
        ev.rel_y = y / (double)v_height;
    }
    ev.delta_x = v_delta_x;
    ev.delta_y = v_delta_y;
    ev.buttons = view_translate_buttons(state);
    ev.modifiers = view_translate_modifiers(state);
    ev.timestamp = (uint64_t)time;

    v_mouse_callback(type, &ev);
}

static void view_dispatch_key(NativeKeyEventType type, XKeyEvent* xkey) {
    if (!v_key_callback || !v_enabled) return;

    KeySym keysym = XLookupKeysym(xkey, 0);

    NativeKeyEvent ev;
    memset(&ev, 0, sizeof(ev));
    ev.keycode = xkey->keycode;
    ev.keysym = (uint32_t)keysym;
    ev.modifiers = view_translate_modifiers(xkey->state);
    ev.timestamp = (uint64_t)xkey->time;

    v_key_callback(type, &ev);
}

// ============================================================================
// Legacy top-level window state (original API)
// ============================================================================

static Display* s_display = nullptr;
static Window s_window = 0;
static MouseEventCallback s_callback = nullptr;
static std::atomic<bool> s_relative_mode{false};

static double s_last_x = 0.0;
static double s_last_y = 0.0;
static double s_delta_x = 0.0;
static double s_delta_y = 0.0;

static int s_center_x = 0;
static int s_center_y = 0;
static int s_window_width = 0;
static int s_window_height = 0;
static bool s_ignore_next_motion = false;

static uint32_t translate_x11_modifiers(unsigned int state) {
    uint32_t mods = 0;
    if (state & ShiftMask)   mods |= (1 << 0);
    if (state & ControlMask) mods |= (1 << 1);
    if (state & Mod1Mask)    mods |= (1 << 2); // Alt
    if (state & Mod4Mask)    mods |= (1 << 3); // Super
    return mods;
}

static uint32_t translate_x11_buttons(unsigned int state) {
    uint32_t buttons = 0;
    if (state & Button1Mask) buttons |= (1 << 0); // Left
    if (state & Button2Mask) buttons |= (1 << 1); // Middle
    if (state & Button3Mask) buttons |= (1 << 2); // Right
    return buttons;
}

static void update_window_geometry() {
    if (!s_display || !s_window) return;
    XWindowAttributes attrs;
    if (XGetWindowAttributes(s_display, s_window, &attrs)) {
        s_window_width = attrs.width;
        s_window_height = attrs.height;
        s_center_x = attrs.width / 2;
        s_center_y = attrs.height / 2;
    }
}

static void dispatch_mouse_event(NativeMouseEventType type, double x, double y,
                                  unsigned int state, unsigned long time) {
    if (!s_callback) return;

    NativeMouseEvent ev;
    memset(&ev, 0, sizeof(ev));

    if (s_window_width > 0 && s_window_height > 0) {
        ev.rel_x = x / (double)s_window_width;
        ev.rel_y = y / (double)s_window_height;
    }
    ev.abs_x = x;
    ev.abs_y = y;
    ev.delta_x = s_delta_x;
    ev.delta_y = s_delta_y;
    ev.buttons = translate_x11_buttons(state);
    ev.modifiers = translate_x11_modifiers(state);
    ev.timestamp = (uint64_t)time;

    s_callback(type, &ev);
}

extern "C" {

// ============================================================================
// Native View API implementation
// ============================================================================

unsigned long nativeview_create(void* display, unsigned long parent_window, int width, int height) {
    v_display = (Display*)display;
    v_parent = (Window)parent_window;
    v_width = width > 0 ? width : 1;
    v_height = height > 0 ? height : 1;
    v_center_x = v_width / 2;
    v_center_y = v_height / 2;
    v_enabled = false;
    v_relative_mode = false;
    v_last_x = 0.0;
    v_last_y = 0.0;
    v_delta_x = 0.0;
    v_delta_y = 0.0;
    v_ignore_next_motion = false;
    v_frame_count = 0;

    // Create child window
    XSetWindowAttributes attrs;
    memset(&attrs, 0, sizeof(attrs));
    attrs.background_pixel = BlackPixel(v_display, DefaultScreen(v_display));
    attrs.event_mask = ExposureMask | StructureNotifyMask;
    // Mouse and keyboard events are only selected when enabled
    attrs.bit_gravity = NorthWestGravity;

    v_window = XCreateWindow(
        v_display, v_parent,
        0, 0, v_width, v_height,
        0, // border width
        CopyFromParent, // depth
        InputOutput,
        CopyFromParent, // visual
        CWBackPixel | CWEventMask | CWBitGravity,
        &attrs
    );

    if (!v_window) {
        fprintf(stderr, "[nativeview] Failed to create child window\n");
        return 0;
    }

    // Create GC for rendering
    v_gc = XCreateGC(v_display, v_window, 0, nullptr);

    XMapWindow(v_display, v_window);
    XFlush(v_display);

    fprintf(stdout, "[nativeview] Created: parent=0x%lx child=0x%lx size=%dx%d\n",
            parent_window, v_window, v_width, v_height);
    return v_window;
}

void nativeview_destroy(void) {
    if (v_relative_mode && v_display && v_window) {
        XUngrabPointer(v_display, CurrentTime);
    }
    if (v_gc && v_display) {
        XFreeGC(v_display, v_gc);
        v_gc = 0;
    }
    if (v_window && v_display) {
        XDestroyWindow(v_display, v_window);
        XFlush(v_display);
    }
    v_window = 0;
    v_display = nullptr;
    v_parent = 0;
    v_enabled = false;
    v_mouse_callback = nullptr;
    v_key_callback = nullptr;
    fprintf(stdout, "[nativeview] Destroyed\n");
}

unsigned long nativeview_get_handle(void) {
    return v_window;
}

void nativeview_set_enabled(int enabled) {
    bool was_enabled = v_enabled.exchange(enabled != 0);

    if (!v_display || !v_window) return;

    if (enabled && !was_enabled) {
        // Select mouse + keyboard events
        XSelectInput(v_display, v_window,
            ExposureMask | StructureNotifyMask |
            PointerMotionMask | ButtonPressMask | ButtonReleaseMask |
            EnterWindowMask | LeaveWindowMask |
            KeyPressMask | KeyReleaseMask | FocusChangeMask);
        XFlush(v_display);
        fprintf(stdout, "[nativeview] Enabled: events active\n");
    } else if (!enabled && was_enabled) {
        // Deselect mouse + keyboard events, disable relative mode
        if (v_relative_mode) {
            v_relative_mode = false;
            XUngrabPointer(v_display, CurrentTime);
        }
        XSelectInput(v_display, v_window,
            ExposureMask | StructureNotifyMask);
        XFlush(v_display);
        fprintf(stdout, "[nativeview] Disabled: events inactive\n");
    }
}

int nativeview_get_enabled(void) {
    return v_enabled ? 1 : 0;
}

void nativeview_resize(int width, int height) {
    v_width = width > 0 ? width : 1;
    v_height = height > 0 ? height : 1;
    v_center_x = v_width / 2;
    v_center_y = v_height / 2;

    if (v_display && v_window) {
        XResizeWindow(v_display, v_window, v_width, v_height);
        XFlush(v_display);
    }
}

void nativeview_set_mouse_callback(MouseEventCallback callback) {
    v_mouse_callback = callback;
}

void nativeview_set_key_callback(KeyEventCallback callback) {
    v_key_callback = callback;
}

void nativeview_get_mouse_position(double* rel_x, double* rel_y) {
    if (rel_x) *rel_x = (v_width > 0) ? v_last_x / (double)v_width : 0.0;
    if (rel_y) *rel_y = (v_height > 0) ? v_last_y / (double)v_height : 0.0;
}

void nativeview_get_mouse_delta(double* delta_x, double* delta_y) {
    if (delta_x) *delta_x = v_delta_x;
    if (delta_y) *delta_y = v_delta_y;
    v_delta_x = 0.0;
    v_delta_y = 0.0;
}

void nativeview_set_relative_mouse_mode(int enabled) {
    if (!v_display || !v_window || !v_enabled) return;

    bool was_enabled = v_relative_mode.exchange(enabled != 0);

    if (enabled && !was_enabled) {
        v_center_x = v_width / 2;
        v_center_y = v_height / 2;
        XGrabPointer(v_display, v_window, True,
                     ButtonPressMask | ButtonReleaseMask | PointerMotionMask,
                     GrabModeAsync, GrabModeAsync,
                     v_window, None, CurrentTime);
        v_ignore_next_motion = true;
        XWarpPointer(v_display, None, v_window, 0, 0, 0, 0, v_center_x, v_center_y);
        XFlush(v_display);
        v_last_x = (double)v_center_x;
        v_last_y = (double)v_center_y;
        v_delta_x = 0.0;
        v_delta_y = 0.0;
    } else if (!enabled && was_enabled) {
        XUngrabPointer(v_display, CurrentTime);
        XFlush(v_display);
    }
}

int nativeview_get_relative_mouse_mode(void) {
    return v_relative_mode ? 1 : 0;
}

void nativeview_render_frame(void) {
    if (!v_display || !v_window || !v_gc || !v_enabled) return;

    v_frame_count++;

    // Stub rendering: animated gradient using X11 drawing primitives.
    // In production, this would be replaced by Vulkan/GLX rendering calls.
    int r = (int)(127.5 * (1.0 + sin((double)v_frame_count * 0.02)));
    int g = (int)(127.5 * (1.0 + sin((double)v_frame_count * 0.03 + 2.0)));
    int b = (int)(127.5 * (1.0 + sin((double)v_frame_count * 0.05 + 4.0)));

    unsigned long color = ((unsigned long)r << 16) | ((unsigned long)g << 8) | (unsigned long)b;

    XSetForeground(v_display, v_gc, color);
    XFillRectangle(v_display, v_window, v_gc, 0, 0, v_width, v_height);

    // Draw crosshair at mouse position (shows mouse abs coord bound to view)
    XSetForeground(v_display, v_gc, 0xFFFFFF);
    int mx = (int)v_last_x;
    int my = (int)v_last_y;
    XDrawLine(v_display, v_window, v_gc, mx - 10, my, mx + 10, my);
    XDrawLine(v_display, v_window, v_gc, mx, my - 10, mx, my + 10);

    // Draw border to indicate enabled state
    XSetForeground(v_display, v_gc, 0x00FF00);
    XDrawRectangle(v_display, v_window, v_gc, 0, 0, v_width - 1, v_height - 1);

    XFlush(v_display);
}

// ============================================================================
// View event processing (called from the X11 event loop)
// This processes events for the native child view window.
// ============================================================================

int nativeview_process_xevent(void* xevent) {
    if (!v_display || !v_window) return 0;

    XEvent* ev = (XEvent*)xevent;

    // Only handle events for our view window
    if (ev->xany.window != v_window) return 0;

    // Always handle structural events
    if (ev->type == ConfigureNotify) {
        v_width = ev->xconfigure.width;
        v_height = ev->xconfigure.height;
        v_center_x = v_width / 2;
        v_center_y = v_height / 2;
        return 1;
    }

    if (ev->type == Expose) {
        if (v_enabled) nativeview_render_frame();
        return 1;
    }

    // If not enabled, don't process input events
    if (!v_enabled) return 0;

    switch (ev->type) {
        case MotionNotify: {
            double x = (double)ev->xmotion.x;
            double y = (double)ev->xmotion.y;

            if (v_ignore_next_motion) {
                v_ignore_next_motion = false;
                v_last_x = x;
                v_last_y = y;
                return 1;
            }

            double dx = x - v_last_x;
            double dy = y - v_last_y;
            v_delta_x += dx;
            v_delta_y += dy;
            v_last_x = x;
            v_last_y = y;

            view_dispatch_mouse(NativeMouseMove, x, y, ev->xmotion.state, ev->xmotion.time);

            if (v_relative_mode) {
                v_ignore_next_motion = true;
                XWarpPointer(v_display, None, v_window, 0, 0, 0, 0, v_center_x, v_center_y);
                XFlush(v_display);
                v_last_x = (double)v_center_x;
                v_last_y = (double)v_center_y;
            }
            return 1;
        }

        case ButtonPress: {
            NativeMouseEventType type;
            unsigned int button = ev->xbutton.button;
            if (button == 1) type = NativeMouseLeftDown;
            else if (button == 2) type = NativeMouseMiddleDown;
            else if (button == 3) type = NativeMouseRightDown;
            else if (button >= 4 && button <= 7) {
                NativeMouseEvent mev;
                memset(&mev, 0, sizeof(mev));
                mev.abs_x = (double)ev->xbutton.x;
                mev.abs_y = (double)ev->xbutton.y;
                mev.delta_x = (button == 6) ? 1.0 : (button == 7) ? -1.0 : 0.0;
                mev.delta_y = (button == 4) ? 1.0 : (button == 5) ? -1.0 : 0.0;
                mev.buttons = view_translate_buttons(ev->xbutton.state);
                mev.modifiers = view_translate_modifiers(ev->xbutton.state);
                mev.timestamp = (uint64_t)ev->xbutton.time;
                if (v_width > 0 && v_height > 0) {
                    mev.rel_x = mev.abs_x / (double)v_width;
                    mev.rel_y = mev.abs_y / (double)v_height;
                }
                if (v_mouse_callback) v_mouse_callback(NativeMouseWheel, &mev);
                return 1;
            }
            else return 0;

            view_dispatch_mouse(type, (double)ev->xbutton.x, (double)ev->xbutton.y,
                                ev->xbutton.state, ev->xbutton.time);
            return 1;
        }

        case ButtonRelease: {
            NativeMouseEventType type;
            unsigned int button = ev->xbutton.button;
            if (button == 1) type = NativeMouseLeftUp;
            else if (button == 2) type = NativeMouseMiddleUp;
            else if (button == 3) type = NativeMouseRightUp;
            else return 0;

            view_dispatch_mouse(type, (double)ev->xbutton.x, (double)ev->xbutton.y,
                                ev->xbutton.state, ev->xbutton.time);
            return 1;
        }

        case EnterNotify: {
            // Focus the view for keyboard events
            XSetInputFocus(v_display, v_window, RevertToParent, CurrentTime);
            return 1;
        }

        case LeaveNotify: {
            view_dispatch_mouse(NativeMouseLeave,
                                (double)ev->xcrossing.x, (double)ev->xcrossing.y,
                                ev->xcrossing.state, ev->xcrossing.time);
            return 1;
        }

        case KeyPress: {
            view_dispatch_key(NativeKeyDown, &ev->xkey);
            return 1;
        }

        case KeyRelease: {
            view_dispatch_key(NativeKeyUp, &ev->xkey);
            return 1;
        }

        default:
            return 0;
    }
}

// ============================================================================
// Legacy top-level window API (unchanged)
// ============================================================================

int native_init(void* display, unsigned long window) {
    s_display = (Display*)display;
    s_window = (Window)window;
    s_relative_mode = false;
    s_last_x = 0.0;
    s_last_y = 0.0;
    s_delta_x = 0.0;
    s_delta_y = 0.0;
    s_ignore_next_motion = false;

    update_window_geometry();

    fprintf(stdout, "[x11_native] Initialized: display=%p window=0x%lx size=%dx%d\n",
            display, window, s_window_width, s_window_height);
    return 1;
}

void native_dispose(void) {
    if (s_relative_mode && s_display && s_window) {
        XUngrabPointer(s_display, CurrentTime);
    }
    s_display = nullptr;
    s_window = 0;
    s_callback = nullptr;
    s_relative_mode = false;
}

int native_process_xevent(void* xevent) {
    if (!s_display || !s_window) return 0;

    // First try to handle as a view event
    if (v_window && nativeview_process_xevent(xevent))
        return 1;

    XEvent* ev = (XEvent*)xevent;

    switch (ev->type) {
        case MotionNotify: {
            double x = (double)ev->xmotion.x;
            double y = (double)ev->xmotion.y;

            if (s_ignore_next_motion) {
                s_ignore_next_motion = false;
                s_last_x = x;
                s_last_y = y;
                return 1;
            }

            double dx = x - s_last_x;
            double dy = y - s_last_y;
            s_delta_x += dx;
            s_delta_y += dy;
            s_last_x = x;
            s_last_y = y;

            dispatch_mouse_event(NativeMouseMove, x, y,
                                 ev->xmotion.state, ev->xmotion.time);

            if (s_relative_mode) {
                s_ignore_next_motion = true;
                XWarpPointer(s_display, None, s_window, 0, 0, 0, 0,
                             s_center_x, s_center_y);
                XFlush(s_display);
                s_last_x = (double)s_center_x;
                s_last_y = (double)s_center_y;
            }

            return 1;
        }

        case ButtonPress: {
            NativeMouseEventType type;
            unsigned int button = ev->xbutton.button;
            if (button == 1) type = NativeMouseLeftDown;
            else if (button == 2) type = NativeMouseMiddleDown;
            else if (button == 3) type = NativeMouseRightDown;
            else if (button == 4 || button == 5 || button == 6 || button == 7) {
                NativeMouseEvent mev;
                memset(&mev, 0, sizeof(mev));
                mev.abs_x = (double)ev->xbutton.x;
                mev.abs_y = (double)ev->xbutton.y;
                mev.delta_x = (button == 6) ? 1.0 : (button == 7) ? -1.0 : 0.0;
                mev.delta_y = (button == 4) ? 1.0 : (button == 5) ? -1.0 : 0.0;
                mev.buttons = translate_x11_buttons(ev->xbutton.state);
                mev.modifiers = translate_x11_modifiers(ev->xbutton.state);
                mev.timestamp = (uint64_t)ev->xbutton.time;
                if (s_window_width > 0 && s_window_height > 0) {
                    mev.rel_x = mev.abs_x / (double)s_window_width;
                    mev.rel_y = mev.abs_y / (double)s_window_height;
                }
                if (s_callback) s_callback(NativeMouseWheel, &mev);
                return 1;
            }
            else return 0;

            dispatch_mouse_event(type, (double)ev->xbutton.x, (double)ev->xbutton.y,
                                 ev->xbutton.state, ev->xbutton.time);
            return 1;
        }

        case ButtonRelease: {
            NativeMouseEventType type;
            unsigned int button = ev->xbutton.button;
            if (button == 1) type = NativeMouseLeftUp;
            else if (button == 2) type = NativeMouseMiddleUp;
            else if (button == 3) type = NativeMouseRightUp;
            else return 0;

            dispatch_mouse_event(type, (double)ev->xbutton.x, (double)ev->xbutton.y,
                                 ev->xbutton.state, ev->xbutton.time);
            return 1;
        }

        case LeaveNotify: {
            dispatch_mouse_event(NativeMouseLeave,
                                 (double)ev->xcrossing.x, (double)ev->xcrossing.y,
                                 ev->xcrossing.state, ev->xcrossing.time);
            return 1;
        }

        case ConfigureNotify: {
            s_window_width = ev->xconfigure.width;
            s_window_height = ev->xconfigure.height;
            s_center_x = s_window_width / 2;
            s_center_y = s_window_height / 2;
            return 0;
        }

        default:
            return 0;
    }
}

void native_set_mouse_callback(MouseEventCallback callback) {
    s_callback = callback;
}

void native_get_mouse_position(double* rel_x, double* rel_y) {
    if (rel_x) *rel_x = (s_window_width > 0) ? s_last_x / (double)s_window_width : 0.0;
    if (rel_y) *rel_y = (s_window_height > 0) ? s_last_y / (double)s_window_height : 0.0;
}

void native_get_mouse_delta(double* delta_x, double* delta_y) {
    if (delta_x) *delta_x = s_delta_x;
    if (delta_y) *delta_y = s_delta_y;
    s_delta_x = 0.0;
    s_delta_y = 0.0;
}

void native_set_relative_mouse_mode(int enabled) {
    if (!s_display || !s_window) return;

    bool was_enabled = s_relative_mode.exchange(enabled != 0);

    if (enabled && !was_enabled) {
        update_window_geometry();
        XGrabPointer(s_display, s_window, True,
                     ButtonPressMask | ButtonReleaseMask | PointerMotionMask,
                     GrabModeAsync, GrabModeAsync,
                     s_window, None, CurrentTime);

        s_ignore_next_motion = true;
        XWarpPointer(s_display, None, s_window, 0, 0, 0, 0, s_center_x, s_center_y);
        XFlush(s_display);
        s_last_x = (double)s_center_x;
        s_last_y = (double)s_center_y;
        s_delta_x = 0.0;
        s_delta_y = 0.0;
    } else if (!enabled && was_enabled) {
        XUngrabPointer(s_display, CurrentTime);
        XFlush(s_display);
    }
}

int native_get_relative_mouse_mode(void) {
    return s_relative_mode ? 1 : 0;
}

} // extern "C"
