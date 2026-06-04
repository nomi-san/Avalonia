#ifndef GL_RENDERER_H
#define GL_RENDERER_H

#include <android/native_window.h>

#ifdef __cplusplus
extern "C" {
#endif

/**
 * Initialize the OpenGL ES renderer with an ANativeWindow.
 * Creates an EGL display/surface/context and compiles shaders.
 *
 * @param window  ANativeWindow* obtained from a SurfaceView's Surface.
 * @param width   Initial surface width in pixels.
 * @param height  Initial surface height in pixels.
 * @return 0 on success, -1 on failure.
 */
int renderer_init(ANativeWindow *window, int width, int height);

/**
 * Start the background render thread.
 * Frames are rendered continuously at ~60fps using nanosleep.
 */
void renderer_start(void);

/**
 * Stop the background render thread.
 * The thread is joined; EGL/GL resources are NOT released.
 */
void renderer_stop(void);

/**
 * Update the viewport after a surface resize.
 *
 * @param width   New surface width in pixels.
 * @param height  New surface height in pixels.
 */
void renderer_resize(int width, int height);

/**
 * Set the rotation speed of the rendered triangle.
 *
 * @param radians_per_second  Rotation speed in radians per second.
 */
void renderer_set_speed(float radians_per_second);

/**
 * Destroy the renderer and release all EGL/GL resources.
 * Stops the render thread if it is still running.
 */
void renderer_destroy(void);

#ifdef __cplusplus
}
#endif

#endif /* GL_RENDERER_H */
