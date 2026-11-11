# Builds the two native libraries, spcaudio over miniaudio and spcplay over snes_spc, each from its
# vendored source plus a shim in src/.
#
# Build it (from this folder):
#   cmake -B build -DCMAKE_BUILD_TYPE=Release
#   cmake --build build --config Release
# On Windows that wants a developer prompt, cl not being on PATH otherwise. The output lands in
# bin/<rid>/.

cmake_minimum_required(VERSION 3.16)

# Before project(), which is the only place it is read.
set(CMAKE_OSX_ARCHITECTURES "arm64;x86_64" CACHE STRING "macOS is one universal binary")

# CXX is snes_spc's; the two shims and miniaudio are C.
project(spcaudio C CXX)

# The .NET runtime identifier the binary is for, which is the build host's.
if(WIN32)
    set(SPC_AUDIO_RID "win-x64")
elseif(APPLE)
    set(SPC_AUDIO_RID "osx")
else()
    set(SPC_AUDIO_RID "linux-x64")
endif()

set(SPC_AUDIO_BIN "${CMAKE_CURRENT_SOURCE_DIR}/bin/${SPC_AUDIO_RID}")

add_library(spcaudio SHARED src/spcaudio.c)

set_target_properties(spcaudio PROPERTIES
    C_STANDARD 99
    C_VISIBILITY_PRESET hidden
    # DllImport("spcaudio") probes spcaudio.dll, libspcaudio.so and libspcaudio.dylib beside the
    # assembly, which each platform's own default naming already produces. That is the whole of
    # the resolution - there is no DllImportResolver anywhere in the app.
    OUTPUT_NAME "spcaudio"
    RUNTIME_OUTPUT_DIRECTORY "${SPC_AUDIO_BIN}"
    LIBRARY_OUTPUT_DIRECTORY "${SPC_AUDIO_BIN}")

# Multi-config generators - the Visual Studio one is what this repo builds with - append the
# configuration to the output directory unless the per-config variables are set too. Without
# this the Windows binary lands in bin/win-x64/Release/ and nothing copies it.
foreach(config IN ITEMS DEBUG RELEASE RELWITHDEBINFO MINSIZEREL)
    set_target_properties(spcaudio PROPERTIES
        RUNTIME_OUTPUT_DIRECTORY_${config} "${SPC_AUDIO_BIN}"
        LIBRARY_OUTPUT_DIRECTORY_${config} "${SPC_AUDIO_BIN}")
endforeach()

# The import library and its .exp are build intermediates, so they stay in the build tree rather
# than beside the .dll that gets committed. ARCHIVE_OUTPUT_DIRECTORY is deliberately unset.

target_include_directories(spcaudio PRIVATE vendor/miniaudio)

# Nothing to link on Windows: miniaudio loads WASAPI itself.
#
# Everywhere else it is dl, pthread and m and *no frameworks*, which is what miniaudio's own
# CMakeLists does and is worth stating because the obvious guess is wrong. On macOS miniaudio does
# not link CoreAudio at build time at all - it dlopens
# /System/Library/Frameworks/CoreAudio.framework/CoreAudio and its two neighbours by absolute path
# at runtime, MA_NO_RUNTIME_LINKING being off. So CMake's own UNIX, which is true on Apple too, is
# the correct branch for both, and an APPLE branch listing -framework was a belief rather than a
# requirement.
#
# On Linux, ALSA is the one backend that needs headers at build time (libasound2-dev); PulseAudio
# and JACK are dlopen'd the same way and want no dev package.
if(UNIX)
    find_package(Threads REQUIRED)
    target_link_libraries(spcaudio PRIVATE Threads::Threads ${CMAKE_DL_LIBS} m)
endif()

# Default to Release for single-config generators, a debug build of miniaudio being both large and
# slow enough to matter in an audio callback.
if(NOT CMAKE_BUILD_TYPE AND NOT CMAKE_CONFIGURATION_TYPES)
    set(CMAKE_BUILD_TYPE Release CACHE STRING "" FORCE)
endif()

# SPC playback: the vendored snes_spc plus the shim over it.
#
# The library is C++ and carries no export annotations of its own, which is the whole reason
# src/spcplay.c exists. Its own visibility stays hidden for the same reason spcaudio's does: the
# shim's own functions are the surface, and nothing of snes_spc's C++ internals belongs in the
# export table.
file(GLOB SPC_PLAY_SOURCES "${CMAKE_CURRENT_SOURCE_DIR}/vendor/snes_spc/*.cpp")

add_library(spcplay SHARED src/spcplay.c ${SPC_PLAY_SOURCES})

set_target_properties(spcplay PROPERTIES
    C_STANDARD 99
    CXX_STANDARD 11
    C_VISIBILITY_PRESET hidden
    CXX_VISIBILITY_PRESET hidden
    VISIBILITY_INLINES_HIDDEN ON
    OUTPUT_NAME "spcplay"
    RUNTIME_OUTPUT_DIRECTORY "${SPC_AUDIO_BIN}"
    LIBRARY_OUTPUT_DIRECTORY "${SPC_AUDIO_BIN}")

foreach(config IN ITEMS DEBUG RELEASE RELWITHDEBINFO MINSIZEREL)
    set_target_properties(spcplay PROPERTIES
        RUNTIME_OUTPUT_DIRECTORY_${config} "${SPC_AUDIO_BIN}"
        LIBRARY_OUTPUT_DIRECTORY_${config} "${SPC_AUDIO_BIN}")
endforeach()

target_include_directories(spcplay PRIVATE vendor/snes_spc)

# snes_spc allocates and does its own arithmetic and touches no OS facility at all, so unlike
# miniaudio there is nothing to link here on any platform - not even pthreads.
