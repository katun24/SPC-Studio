#ifndef SPC_INTEROP_H
#define SPC_INTEROP_H

#include <stdint.h>

// Marks a function as one the shared library exports, on every platform the libraries are built for.
#if defined(_WIN32)
    #define SPC_EXPORT __declspec(dllexport)
#else
    #define SPC_EXPORT __attribute__((visibility("default")))
#endif

// A count of bytes handed across to the app or back, the same width on every platform.
typedef int32_t spc_byte_count;

#endif
