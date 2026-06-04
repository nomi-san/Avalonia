/**
 * gl_renderer.c — OpenGL ES 3.0 renderer for Android NDK.
 *
 * Creates an EGL context on a given ANativeWindow surface, compiles a simple
 * shader program, and renders a rotating colored triangle in a background
 * thread. Designed to be called from C# via P/Invoke in a .NET Android app.
 */

#include "gl_renderer.h"

#include <EGL/egl.h>
#include <GLES3/gl3.h>
#include <android/log.h>
#include <android/native_window.h>
#include <math.h>
#include <pthread.h>
#include <stdatomic.h>
#include <string.h>
#include <time.h>
#include <unistd.h>

#define LOG_TAG "GLRenderer"
#define LOGI(...) __android_log_print(ANDROID_LOG_INFO,  LOG_TAG, __VA_ARGS__)
#define LOGE(...) __android_log_print(ANDROID_LOG_ERROR, LOG_TAG, __VA_ARGS__)

/* ---- Shader sources ---- */

static const char *s_vertex_shader_src =
    "#version 300 es\n"
    "layout(location = 0) in vec2 aPos;\n"
    "layout(location = 1) in vec3 aColor;\n"
    "uniform float uAngle;\n"
    "out vec3 vColor;\n"
    "void main() {\n"
    "    float c = cos(uAngle);\n"
    "    float s = sin(uAngle);\n"
    "    vec2 rotated = vec2(aPos.x * c - aPos.y * s,\n"
    "                        aPos.x * s + aPos.y * c);\n"
    "    gl_Position = vec4(rotated, 0.0, 1.0);\n"
    "    vColor = aColor;\n"
    "}\n";

static const char *s_fragment_shader_src =
    "#version 300 es\n"
    "precision mediump float;\n"
    "in vec3 vColor;\n"
    "out vec4 fragColor;\n"
    "void main() {\n"
    "    fragColor = vec4(vColor, 1.0);\n"
    "}\n";

/* ---- Triangle vertex data ---- */

static const float s_vertices[] = {
    /* x      y       r     g     b   */
     0.0f,  0.6f,   1.0f, 0.2f, 0.3f,   /* top    — red   */
    -0.5f, -0.4f,   0.2f, 1.0f, 0.3f,   /* left   — green */
     0.5f, -0.4f,   0.3f, 0.2f, 1.0f,   /* right  — blue  */
};

/* ---- Renderer state ---- */

static struct {
    /* EGL */
    EGLDisplay display;
    EGLSurface surface;
    EGLContext context;
    ANativeWindow *window;

    /* GL */
    GLuint program;
    GLuint vao;
    GLuint vbo;
    GLint  u_angle;

    /* Viewport */
    atomic_int width;
    atomic_int height;
    atomic_int resized;

    /* Render thread */
    pthread_t thread;
    atomic_int running;
    atomic_int initialized;

    /* Animation */
    float angle;
    float speed; /* radians per second */
} g;

/* ---- Helpers ---- */

static GLuint compile_shader(GLenum type, const char *src) {
    GLuint shader = glCreateShader(type);
    glShaderSource(shader, 1, &src, NULL);
    glCompileShader(shader);

    GLint ok = 0;
    glGetShaderiv(shader, GL_COMPILE_STATUS, &ok);
    if (!ok) {
        char buf[512];
        glGetShaderInfoLog(shader, sizeof(buf), NULL, buf);
        LOGE("Shader compile error: %s", buf);
        glDeleteShader(shader);
        return 0;
    }
    return shader;
}

static GLuint create_program(void) {
    GLuint vs = compile_shader(GL_VERTEX_SHADER,   s_vertex_shader_src);
    GLuint fs = compile_shader(GL_FRAGMENT_SHADER, s_fragment_shader_src);
    if (!vs || !fs) return 0;

    GLuint prog = glCreateProgram();
    glAttachShader(prog, vs);
    glAttachShader(prog, fs);
    glLinkProgram(prog);

    GLint ok = 0;
    glGetProgramiv(prog, GL_LINK_STATUS, &ok);
    if (!ok) {
        char buf[512];
        glGetProgramInfoLog(prog, sizeof(buf), NULL, buf);
        LOGE("Program link error: %s", buf);
        glDeleteProgram(prog);
        prog = 0;
    }

    glDeleteShader(vs);
    glDeleteShader(fs);
    return prog;
}

static int init_egl(void) {
    g.display = eglGetDisplay(EGL_DEFAULT_DISPLAY);
    if (g.display == EGL_NO_DISPLAY) {
        LOGE("eglGetDisplay failed");
        return -1;
    }
    if (!eglInitialize(g.display, NULL, NULL)) {
        LOGE("eglInitialize failed");
        return -1;
    }

    const EGLint config_attribs[] = {
        EGL_RENDERABLE_TYPE, EGL_OPENGL_ES3_BIT,
        EGL_SURFACE_TYPE,    EGL_WINDOW_BIT,
        EGL_RED_SIZE,   8,
        EGL_GREEN_SIZE, 8,
        EGL_BLUE_SIZE,  8,
        EGL_ALPHA_SIZE, 8,
        EGL_DEPTH_SIZE, 0,
        EGL_NONE
    };

    EGLConfig config;
    EGLint num_configs;
    if (!eglChooseConfig(g.display, config_attribs, &config, 1, &num_configs) ||
        num_configs == 0) {
        LOGE("eglChooseConfig failed");
        return -1;
    }

    /* Set the native window buffer format to match the EGL config */
    EGLint format;
    eglGetConfigAttrib(g.display, config, EGL_NATIVE_VISUAL_ID, &format);
    ANativeWindow_setBuffersGeometry(g.window,
        atomic_load(&g.width), atomic_load(&g.height), format);

    g.surface = eglCreateWindowSurface(g.display, config, g.window, NULL);
    if (g.surface == EGL_NO_SURFACE) {
        LOGE("eglCreateWindowSurface failed");
        return -1;
    }

    const EGLint context_attribs[] = {
        EGL_CONTEXT_CLIENT_VERSION, 3,
        EGL_NONE
    };
    g.context = eglCreateContext(g.display, config, EGL_NO_CONTEXT, context_attribs);
    if (g.context == EGL_NO_CONTEXT) {
        LOGE("eglCreateContext failed");
        return -1;
    }

    if (!eglMakeCurrent(g.display, g.surface, g.surface, g.context)) {
        LOGE("eglMakeCurrent failed");
        return -1;
    }

    return 0;
}

static int init_gl(void) {
    g.program = create_program();
    if (!g.program) return -1;

    g.u_angle = glGetUniformLocation(g.program, "uAngle");

    glGenVertexArrays(1, &g.vao);
    glGenBuffers(1, &g.vbo);

    glBindVertexArray(g.vao);
    glBindBuffer(GL_ARRAY_BUFFER, g.vbo);
    glBufferData(GL_ARRAY_BUFFER, sizeof(s_vertices), s_vertices, GL_STATIC_DRAW);

    /* aPos: location 0, 2 floats, stride 5 floats, offset 0 */
    glVertexAttribPointer(0, 2, GL_FLOAT, GL_FALSE, 5 * sizeof(float), (void *)0);
    glEnableVertexAttribArray(0);

    /* aColor: location 1, 3 floats, stride 5 floats, offset 2 floats */
    glVertexAttribPointer(1, 3, GL_FLOAT, GL_FALSE, 5 * sizeof(float),
                          (void *)(2 * sizeof(float)));
    glEnableVertexAttribArray(1);

    glBindVertexArray(0);

    return 0;
}

static void draw_frame(void) {
    if (atomic_load(&g.resized)) {
        glViewport(0, 0, atomic_load(&g.width), atomic_load(&g.height));
        atomic_store(&g.resized, 0);
    }

    /* Dark background */
    glClearColor(0.08f, 0.08f, 0.15f, 1.0f);
    glClear(GL_COLOR_BUFFER_BIT);

    glUseProgram(g.program);
    glUniform1f(g.u_angle, g.angle);

    glBindVertexArray(g.vao);
    glDrawArrays(GL_TRIANGLES, 0, 3);
    glBindVertexArray(0);

    eglSwapBuffers(g.display, g.surface);
}

static void cleanup_gl(void) {
    if (g.vao) { glDeleteVertexArrays(1, &g.vao); g.vao = 0; }
    if (g.vbo) { glDeleteBuffers(1, &g.vbo); g.vbo = 0; }
    if (g.program) { glDeleteProgram(g.program); g.program = 0; }
}

static void cleanup_egl(void) {
    if (g.display != EGL_NO_DISPLAY) {
        eglMakeCurrent(g.display, EGL_NO_SURFACE, EGL_NO_SURFACE, EGL_NO_CONTEXT);
        if (g.context != EGL_NO_CONTEXT)
            eglDestroyContext(g.display, g.context);
        if (g.surface != EGL_NO_SURFACE)
            eglDestroySurface(g.display, g.surface);
        eglTerminate(g.display);
    }
    g.display = EGL_NO_DISPLAY;
    g.surface = EGL_NO_SURFACE;
    g.context = EGL_NO_CONTEXT;
}

/* ---- Render thread ---- */

static void *render_thread_func(void *arg) {
    (void)arg;

    /* Make the EGL context current on this thread */
    if (!eglMakeCurrent(g.display, g.surface, g.surface, g.context)) {
        LOGE("eglMakeCurrent failed on render thread");
        return NULL;
    }

    LOGI("Render thread started");

    struct timespec prev_time;
    clock_gettime(CLOCK_MONOTONIC, &prev_time);

    while (atomic_load(&g.running)) {
        struct timespec now;
        clock_gettime(CLOCK_MONOTONIC, &now);
        float dt = (float)(now.tv_sec - prev_time.tv_sec)
                 + (float)(now.tv_nsec - prev_time.tv_nsec) * 1e-9f;
        prev_time = now;

        g.angle += g.speed * dt;
        if (g.angle > 2.0f * (float)M_PI)
            g.angle -= 2.0f * (float)M_PI;

        draw_frame();

        /* ~60 fps throttle */
        struct timespec sleep_time = { 0, 16000000 }; /* 16ms */
        nanosleep(&sleep_time, NULL);
    }

    /* Release EGL context from this thread */
    eglMakeCurrent(g.display, EGL_NO_SURFACE, EGL_NO_SURFACE, EGL_NO_CONTEXT);
    LOGI("Render thread stopped");
    return NULL;
}

/* ---- Public API ---- */

int renderer_init(ANativeWindow *window, int width, int height) {
    memset(&g, 0, sizeof(g));

    g.window  = window;
    g.speed   = 1.5f; /* default rotation speed */
    g.display = EGL_NO_DISPLAY;
    g.surface = EGL_NO_SURFACE;
    g.context = EGL_NO_CONTEXT;

    atomic_store(&g.width,  width);
    atomic_store(&g.height, height);
    atomic_store(&g.resized, 1);

    ANativeWindow_acquire(window);

    if (init_egl() != 0) {
        LOGE("EGL initialization failed");
        ANativeWindow_release(window);
        return -1;
    }

    if (init_gl() != 0) {
        LOGE("GL initialization failed");
        cleanup_egl();
        ANativeWindow_release(window);
        return -1;
    }

    /* Release context from the init thread — the render thread will claim it */
    eglMakeCurrent(g.display, EGL_NO_SURFACE, EGL_NO_SURFACE, EGL_NO_CONTEXT);

    atomic_store(&g.initialized, 1);
    LOGI("Renderer initialized (%dx%d)", width, height);
    return 0;
}

void renderer_start(void) {
    if (!atomic_load(&g.initialized) || atomic_load(&g.running))
        return;

    atomic_store(&g.running, 1);
    pthread_create(&g.thread, NULL, render_thread_func, NULL);
    LOGI("Renderer started");
}

void renderer_stop(void) {
    if (!atomic_load(&g.running))
        return;

    atomic_store(&g.running, 0);
    pthread_join(g.thread, NULL);
    LOGI("Renderer stopped");
}

void renderer_resize(int width, int height) {
    atomic_store(&g.width,  width);
    atomic_store(&g.height, height);
    atomic_store(&g.resized, 1);
}

void renderer_set_speed(float radians_per_second) {
    g.speed = radians_per_second;
}

void renderer_destroy(void) {
    if (atomic_load(&g.running))
        renderer_stop();

    if (atomic_load(&g.initialized)) {
        /* Re-acquire context to clean up GL resources */
        eglMakeCurrent(g.display, g.surface, g.surface, g.context);
        cleanup_gl();
        cleanup_egl();

        if (g.window) {
            ANativeWindow_release(g.window);
            g.window = NULL;
        }

        atomic_store(&g.initialized, 0);
        LOGI("Renderer destroyed");
    }
}
