#pragma once
// Test-only sequential InitOnce substitute for the Native x86 host harness.
// Production Windows builds include the Windows SDK header instead.
using BOOL = int;
using DWORD = unsigned long;
using PVOID = void*;
struct INIT_ONCE { bool done; };
using PINIT_ONCE = INIT_ONCE*;
#define CALLBACK
#define TRUE 1
#define FALSE 0
#define INIT_ONCE_STATIC_INIT {false}
#define ERROR_NOT_ENOUGH_MEMORY 8
inline void SetLastError(DWORD) {}
inline BOOL InitOnceExecuteOnce(PINIT_ONCE once, BOOL (*callback)(PINIT_ONCE,PVOID,PVOID*), PVOID parameter, PVOID* context) {
    if(once->done) return TRUE;
    if(!callback(once,parameter,context)) return FALSE;
    once->done=true;return TRUE;
}
