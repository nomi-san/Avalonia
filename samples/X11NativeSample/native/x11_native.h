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

// Callback from native to managed code
typedef void (*MouseEventCallback)(NativeMouseEventType type, const NativeMouseEvent* event);

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
