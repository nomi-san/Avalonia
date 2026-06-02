cmake_minimum_required(VERSION 3.16)
project(x11_native LANGUAGES CXX)

set(CMAKE_CXX_STANDARD 17)
set(CMAKE_POSITION_INDEPENDENT_CODE ON)

find_package(X11 REQUIRED)

add_library(x11_native SHARED x11_native.cpp)
target_include_directories(x11_native PRIVATE ${X11_INCLUDE_DIR})
target_link_libraries(x11_native PRIVATE ${X11_LIBRARIES} m)

# Install to the C# output directory
install(TARGETS x11_native DESTINATION lib)
