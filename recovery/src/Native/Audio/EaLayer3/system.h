// NewSdk allocator adapter for the selected sagetools decoder (GPL-3.0-or-later).
#pragma once
#include <cstdint>
#include <cstdlib>
#include <cstring>
#include <stdexcept>
#include "base.h"
#ifdef _MSC_VER
#include <malloc.h>
#else
#define __declspec(x) __attribute__((x))
#define align(x) aligned(x)
#endif
class System {
public:
    static System* GetInstance() { static System allocator; return &allocator; }
    void* Alloc(unsigned int bytes, unsigned int alignment = 16) {
#ifdef _MSC_VER
        void* p = _aligned_malloc(bytes, alignment);
#else
        void* p = nullptr; if (posix_memalign(&p, alignment, bytes)) p = nullptr;
#endif
        if (!p) throw std::bad_alloc();
        return p;
    }
    void Free(void* p) {
#ifdef _MSC_VER
        _aligned_free(p);
#else
        free(p);
#endif
    }
};
