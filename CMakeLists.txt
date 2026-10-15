# RumbleRosesReX - ReXGlue Recompiled Project
#
# SDK boilerplate lives in generated/rexglue.cmake.

cmake_minimum_required(VERSION 3.25)
project(rumble_roses_xx LANGUAGES CXX)

set(CMAKE_CXX_STANDARD 23)
set(CMAKE_CXX_STANDARD_REQUIRED ON)

include(generated/rexglue.cmake)

# Sources
set(RUMBLEROSESXX_SOURCES
    src/main.cpp
    src/rumble_roses_xx_flags.cpp
    src/title_hooks.cpp
)

if(WIN32)
    add_executable(rumble_roses_xx WIN32 ${RUMBLEROSESXX_SOURCES})
else()
    add_executable(rumble_roses_xx ${RUMBLEROSESXX_SOURCES})
endif()

rexglue_setup_target(rumble_roses_xx GPU_PLUGINS xenos)
