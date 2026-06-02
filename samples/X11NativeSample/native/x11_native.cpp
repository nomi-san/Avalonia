#include "x11_native.h"
#include <X11/Xlib.h>
#include <X11/Xutil.h>
#include <cstring>
#include <cstdio>
#include <atomic>

// Internal state - no GC involvement, pure native memory
static Display* s_display = nullptr;
static Window s_window = 0;
static MouseEventCallback s_callback = nullptr;
static std::atomic<bool> s_relative_mode{false};

// Last known mouse position
static double s_last_x = 0.0;
static double s_last_y = 0.0;

// Accumulated delta for relative mouse mode
static double s_delta_x = 0.0;
static double s_delta_y = 0.0;

// Window center for pointer warping in relative mode
static int s_center_x = 0;
static int s_center_y = 0;
static int s_window_width = 0;
static int s_window_height = 0;

// Flag to ignore warp-back motion events
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

    XEvent* ev = (XEvent*)xevent;

    switch (ev->type) {
        case MotionNotify: {
            double x = (double)ev->xmotion.x;
            double y = (double)ev->xmotion.y;

            if (s_ignore_next_motion) {
                s_ignore_next_motion = false;
                s_last_x = x;
                s_last_y = y;
                return 1; // consumed, don't pass to Avalonia
            }

            double dx = x - s_last_x;
            double dy = y - s_last_y;
            s_delta_x += dx;
            s_delta_y += dy;
            s_last_x = x;
            s_last_y = y;

            dispatch_mouse_event(NativeMouseMove, x, y,
                                 ev->xmotion.state, ev->xmotion.time);

            // In relative mode, warp pointer back to center
            if (s_relative_mode) {
                s_ignore_next_motion = true;
                XWarpPointer(s_display, None, s_window, 0, 0, 0, 0,
                             s_center_x, s_center_y);
                XFlush(s_display);
                s_last_x = (double)s_center_x;
                s_last_y = (double)s_center_y;
            }

            return 1; // handled
        }

        case ButtonPress: {
            NativeMouseEventType type;
            unsigned int button = ev->xbutton.button;
            if (button == 1) type = NativeMouseLeftDown;
            else if (button == 2) type = NativeMouseMiddleDown;
            else if (button == 3) type = NativeMouseRightDown;
            else if (button == 4 || button == 5 || button == 6 || button == 7) {
                // Wheel events
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
            else return 0; // unhandled button

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
            // Update window geometry on resize
            s_window_width = ev->xconfigure.width;
            s_window_height = ev->xconfigure.height;
            s_center_x = s_window_width / 2;
            s_center_y = s_window_height / 2;
            return 0; // let Avalonia handle configure too
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
    // Reset accumulator
    s_delta_x = 0.0;
    s_delta_y = 0.0;
}

void native_set_relative_mouse_mode(int enabled) {
    if (!s_display || !s_window) return;

    bool was_enabled = s_relative_mode.exchange(enabled != 0);

    if (enabled && !was_enabled) {
        update_window_geometry();
        // Grab pointer and hide cursor
        XGrabPointer(s_display, s_window, True,
                     ButtonPressMask | ButtonReleaseMask | PointerMotionMask,
                     GrabModeAsync, GrabModeAsync,
                     s_window, None, CurrentTime);

        // Warp to center
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
