#ifndef X11_NATIVE_H
#define X11_NATIVE_H

#include <stdint.h>

#ifdef __cplusplus
extern "C" {
#endif

// Mouse event types matching Avalonia's RawPointerEventType
typedef enum {
    NativeMouseMove = 0,
    NativeMouseLeftDown = 1,
    NativeMouseLeftUp = 2,
    NativeMouseRightDown = 3,
    NativeMouseRightUp = 4,
    NativeMouseMiddleDown = 5,
    NativeMouseMiddleUp = 6,
    NativeMouseWheel = 7,
    NativeMouseLeave = 8,
} NativeMouseEventType;

// Keyboard event types
typedef enum {
    NativeKeyDown = 0,
    NativeKeyUp = 1,
} NativeKeyEventType;

// Relative mouse position data (avoids GC allocation)
typedef struct {
    double rel_x;
    double rel_y;
    double abs_x;
    double abs_y;
    double delta_x;
    double delta_y;
    uint32_t buttons;
    uint32_t modifiers;
    uint64_t timestamp;
} NativeMouseEvent;

// Keyboard event data
typedef struct {
    uint32_t keycode;
    uint32_t keysym;
    uint32_t modifiers;
    uint64_t timestamp;
} NativeKeyEvent;

// Callback from native to managed code
typedef void (*MouseEventCallback)(NativeMouseEventType type, const NativeMouseEvent* event);
typedef void (*KeyEventCallback)(NativeKeyEventType type, const NativeKeyEvent* event);

// --- View API: creates a native child window for Vulkan/GLX rendering ---

// Create a native child view window under a given parent XID.
// Returns the child window XID (for NativeControlHost), or 0 on failure.
unsigned long nativeview_create(void* display, unsigned long parent_window, int width, int height);

// Destroy the native child view
void nativeview_destroy(void);

// Get the native child view window XID
unsigned long nativeview_get_handle(void);

// Enable/disable the view (activates mouse, keyboard, and rendering)
void nativeview_set_enabled(int enabled);

// Check if the view is enabled
int nativeview_get_enabled(void);

// Resize the native view
void nativeview_resize(int width, int height);

// Set mouse event callback for the view
void nativeview_set_mouse_callback(MouseEventCallback callback);

// Set keyboard event callback for the view
void nativeview_set_key_callback(KeyEventCallback callback);

// Get last known mouse position relative to the view (0.0-1.0)
void nativeview_get_mouse_position(double* rel_x, double* rel_y);

// Get accumulated mouse delta since last call
void nativeview_get_mouse_delta(double* delta_x, double* delta_y);

// Enable/disable relative mouse mode on the view
void nativeview_set_relative_mouse_mode(int enabled);

// Check if relative mouse mode is active on the view
int nativeview_get_relative_mouse_mode(void);

// Render one frame (stub: clears with a color; replace with Vulkan/GLX)
void nativeview_render_frame(void);

// Process an X11 event for the view, returns 1 if handled, 0 otherwise
int nativeview_process_xevent(void* xevent);

// --- Legacy top-level window API (unchanged) ---

// Initialize the native event handler for a given X11 display and window
int native_init(void* display, unsigned long window);

// Dispose the native event handler
void native_dispose(void);

// Process a raw X11 event, returns 1 if handled, 0 otherwise
int native_process_xevent(void* xevent);

// Set the callback for mouse events
void native_set_mouse_callback(MouseEventCallback callback);

// Get the last known relative mouse position (high-perf, no GC)
void native_get_mouse_position(double* rel_x, double* rel_y);

// Get accumulated mouse delta since last call (for relative mouse mode)
void native_get_mouse_delta(double* delta_x, double* delta_y);

// Enable/disable relative mouse mode (pointer lock)
void native_set_relative_mouse_mode(int enabled);

// Check if relative mouse mode is active
int native_get_relative_mouse_mode(void);

#ifdef __cplusplus
}
#endif

#endif // X11_NATIVE_H
