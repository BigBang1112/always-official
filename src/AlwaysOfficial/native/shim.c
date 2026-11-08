// The native part of the mod: the few things C# cannot do in a Native AOT DLL.
//
// 1. The DLL entry point. Native AOT links its own DllMain, so ours is a separate entry point
//    (/ENTRY:ModEntry in the .csproj). It runs the CRT and AOT start-up first, then starts the
//    boot thread. Managed code never runs under the loader lock: the boot thread waits for it.
// 2. STANDALONE_PROXY builds a separate d3d9.dll that loads the adjacent AlwaysOfficial.dll
//    and forwards Direct3D to Windows. The mod retains forwarding exports for compatibility.
// 3. Naked forwarding stubs: they leave every register and stack argument as the caller left them,
//    so the exports with undocumented signatures forward correctly.
//
// No <windows.h>: the handful of declarations below keep this file buildable with cl.exe alone
// (no INCLUDE paths needed). x86 only (inline asm), like the game.

typedef int BOOL;
typedef unsigned long DWORD;
typedef unsigned int UINT;
typedef long LONG;
typedef long HRESULT;
typedef void* HANDLE;
typedef void* HMODULE;
typedef unsigned short WCHAR;

#define WINAPI __stdcall
#define IMPORT __declspec(dllimport)
#define DLL_PROCESS_ATTACH 1
#define D3DERR_NOTAVAILABLE ((HRESULT)0x8876086AL)

IMPORT HANDLE WINAPI CreateThread(void* attributes, UINT stackSize, DWORD(WINAPI* start)(void*), void* parameter,
                                  DWORD flags, DWORD* threadId);
IMPORT BOOL WINAPI CloseHandle(HANDLE handle);
IMPORT void* WINAPI GetProcAddress(HMODULE module, const char* name);
IMPORT HMODULE WINAPI LoadLibraryW(const WCHAR* path);
IMPORT UINT WINAPI GetSystemDirectoryW(WCHAR* buffer, UINT size);
IMPORT DWORD WINAPI GetModuleFileNameW(HMODULE module, WCHAR* buffer, DWORD size);
IMPORT DWORD WINAPI GetCurrentThreadId(void);
IMPORT void WINAPI Sleep(DWORD milliseconds);

long _InterlockedCompareExchange(long volatile* target, long value, long comparand);
long _InterlockedExchange(long volatile* target, long value);
#pragma intrinsic(_InterlockedCompareExchange, _InterlockedExchange)

// The CRT's own entry point. It initialises the CRT and calls DllMain (Native AOT's).
#ifndef STANDALONE_PROXY
BOOL WINAPI _DllMainCRTStartup(HMODULE module, DWORD reason, void* reserved);
#endif

static HMODULE g_module;

// --- Boot ---------------------------------------------------------------------------------------

// ModBoot in Exports.cs, looked up by name: the export table is the one stable link between the C
// and the C# side (ILC's symbol decoration on x86 is its own business).
typedef int(WINAPI* ModBootFn)(HMODULE module);

static volatile long g_bootState; // 0 not started, 1 booting, 2 done
static DWORD g_bootThread;

// Runs the managed boot exactly once. Called from the boot thread and from the d3d9 exports,
// whichever comes first; the others wait for it (at most 5 s).
static void ensureBooted(void) {
    if (g_bootState == 2) return;
    if (_InterlockedCompareExchange(&g_bootState, 1, 0) == 0) {
        g_bootThread = GetCurrentThreadId();
        HMODULE mod = g_module;
#ifdef STANDALONE_PROXY
        // Resolve beside this proxy, independently of the process working directory.
        static WCHAR path[32768];
        DWORD length = GetModuleFileNameW(g_module, path, 32768);
        if (length && length < 32768) {
            while (length && path[length - 1] != L'\\') length--;
            const WCHAR* name = L"AlwaysOfficial.dll";
            if (length && length + 19 < 32768) {
                for (DWORD i = 0; (path[length + i] = name[i]) != 0; i++) {}
                mod = LoadLibraryW(path);
            } else mod = 0;
        } else mod = 0;
#endif
        ModBootFn boot = mod ? (ModBootFn)GetProcAddress(mod, "ModBoot") : 0;
        if (boot) boot(mod);
        _InterlockedExchange(&g_bootState, 2);
        return;
    }
    if (g_bootThread == GetCurrentThreadId()) return; // re-entered from inside the boot
    for (int waited = 0; g_bootState != 2 && waited < 5000; waited++) Sleep(1);
}

static DWORD WINAPI bootThread(void* unused) {
    (void)unused;
    ensureBooted();
    return 0;
}

#ifdef STANDALONE_PROXY
// A small native proxy, without the managed runtime or game hooks.
__declspec(dllexport) void AlwaysOfficialStandaloneProxy(void) {}
BOOL WINAPI ProxyEntry(HMODULE module, DWORD reason, void* reserved) {
    (void)reserved;
    BOOL ok = 1;
#else
BOOL WINAPI ModEntry(HMODULE module, DWORD reason, void* reserved) {
    BOOL ok = _DllMainCRTStartup(module, reason, reserved);
#endif
    if (ok && reason == DLL_PROCESS_ATTACH) {
        g_module = module;
        HANDLE thread = CreateThread(0, 0, bootThread, 0, 0, 0);
        if (thread) CloseHandle(thread);
    }
    return ok;
}

// --- Standalone mode: d3d9.dll exports ----------------------------------------------------------

// The system's d3d9.dll by full path (SysWOW64 for this 32-bit process): in standalone mode this
// DLL *is* "d3d9.dll", so loading it by name would give us back ourselves.
static HMODULE systemD3D9(void) {
    static HMODULE module;
    if (!module) {
        WCHAR path[300];
        const WCHAR* file = L"\\d3d9.dll";
        UINT n = GetSystemDirectoryW(path, 280);
        if (!n || n >= 280) return 0;
        for (int i = 0; file[i]; i++) path[n++] = file[i];
        path[n] = 0;
        module = LoadLibraryW(path);
    }
    return module;
}

typedef void*(WINAPI* Create9Fn)(UINT sdkVersion);
typedef HRESULT(WINAPI* Create9ExFn)(UINT sdkVersion, void** out);

void* WINAPI MOD_Direct3DCreate9(UINT sdkVersion) {
    ensureBooted(); // install the solo hooks before returning control to the game
    HMODULE d3d9 = systemD3D9();
    Create9Fn fn = d3d9 ? (Create9Fn)GetProcAddress(d3d9, "Direct3DCreate9") : 0;
    return fn ? fn(sdkVersion) : 0;
}

HRESULT WINAPI MOD_Direct3DCreate9Ex(UINT sdkVersion, void** out) {
    ensureBooted();
    HMODULE d3d9 = systemD3D9();
    Create9ExFn fn = d3d9 ? (Create9ExFn)GetProcAddress(d3d9, "Direct3DCreate9Ex") : 0;
    return fn ? fn(sdkVersion, out) : D3DERR_NOTAVAILABLE;
}

#pragma comment(linker, "/EXPORT:Direct3DCreate9=_MOD_Direct3DCreate9@4")
#pragma comment(linker, "/EXPORT:Direct3DCreate9Ex=_MOD_Direct3DCreate9Ex@8")

// Forwarded as they are: resolved once, then jumped to with the caller's arguments untouched.
#define MOD_FORWARDS(X)                        \
    X(D3DPERF_BeginEvent)                      \
    X(D3DPERF_EndEvent)                        \
    X(D3DPERF_GetStatus)                       \
    X(D3DPERF_QueryRepeatFrame)                \
    X(D3DPERF_SetMarker)                       \
    X(D3DPERF_SetOptions)                      \
    X(D3DPERF_SetRegion)                       \
    X(DebugSetLevel)                           \
    X(DebugSetMute)                            \
    X(Direct3D9EnableMaximizedWindowedModeShim) \
    X(Direct3DCreate9On12)                     \
    X(Direct3DCreate9On12Ex)                   \
    X(Direct3DShaderValidatorCreate9)          \
    X(PSGPError)                               \
    X(PSGPSampleTexture)

#define MOD_DECLARE_POINTER(name) static void* g_##name;
MOD_FORWARDS(MOD_DECLARE_POINTER)

static void __cdecl resolveForwards(void) {
    static int resolved;
    if (resolved) return;
    HMODULE d3d9 = systemD3D9();
#define MOD_RESOLVE(name) g_##name = d3d9 ? GetProcAddress(d3d9, #name) : 0;
    MOD_FORWARDS(MOD_RESOLVE)
    resolved = 1;
}

#define MOD_DEFINE_STUB(name)                                          \
    __pragma(comment(linker, "/EXPORT:" #name "=_MOD_" #name))         \
    __declspec(naked) void MOD_##name(void) {                          \
        __asm pushad                                                   \
        __asm call resolveForwards                                     \
        __asm popad                                                    \
        __asm jmp dword ptr [g_##name]                                 \
    }
MOD_FORWARDS(MOD_DEFINE_STUB)
