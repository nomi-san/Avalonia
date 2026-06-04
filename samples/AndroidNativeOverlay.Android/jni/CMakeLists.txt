cmake_minimum_required(VERSION 3.10)
project(gl_renderer LANGUAGES C)

add_library(gl_renderer SHARED gl_renderer.c)

target_link_libraries(gl_renderer
    android    # ANativeWindow APIs
    EGL        # EGL context management
    GLESv3     # OpenGL ES 3.0
    log        # Android logging (__android_log_print)
    m          # math (sinf, cosf)
)

# Strip symbols in release for smaller binary
if(CMAKE_BUILD_TYPE STREQUAL "Release")
    set_target_properties(gl_renderer PROPERTIES
        LINK_FLAGS "-s"
    )
endif()
