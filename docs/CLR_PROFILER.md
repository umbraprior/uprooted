# CLR Profiler Reference

> **Related docs:**
> [Architecture](ARCHITECTURE.md) |
> [Hook Reference](HOOK_REFERENCE.md) |
> [Installation Guide](INSTALLATION.md) |
> [Build Guide](BUILD.md)

---

## Table of Contents

1. [Overview](#overview)
2. [GUID and Environment Variables](#guid-and-environment-variables)
3. [ICorProfilerCallback Implementation](#icorprofilercallback-implementation)
4. [IL Injection in Detail](#il-injection-in-detail)
5. [Why DOTNET_ReadyToRun=0](#why-dotnet_readytorun0)
6. [Process Guard](#process-guard)
7. [Windows vs Linux Differences](#windows-vs-linux-differences)
8. [Export Definitions](#export-definitions)
9. [Building](#building)
10. [Debugging](#debugging)

---

## Overview

The Uprooted CLR profiler is a native C shared library (DLL on Windows, SO on Linux)
that implements the **ICorProfilerCallback** COM interface. It is the first link in
the injection chain: before any managed C# code runs, before any Avalonia UI is
touched, and before any TypeScript is injected into the browser -- the profiler is
already loaded into the .NET runtime.

Its single purpose is to intercept method JIT compilation and **inject IL bytecode**
into a target method. That IL calls `Assembly.LoadFrom()` to load the managed hook
DLL (`UprootedHook.dll`) and `Assembly.CreateInstance()` to instantiate its entry
point. From there, the managed hook takes over. See [Hook Reference](HOOK_REFERENCE.md)
for what happens after the hook DLL loads.

The injection strategy:

```
1. Prof_Initialize    -- set event mask for JIT + module load notifications
2. ModuleLoadFinished -- track CoreLib; try each app module as injection target
   - Root.dll (single-file host) has no TypeRefs, skipped automatically
   - First module with a System.Object TypeRef wins (e.g. Sentry.dll)
3. PrepareTargetModule -- create metadata tokens, inject IL immediately
   - Assembly.LoadFrom("path/to/UprootedHook.dll")
   - Assembly.CreateInstance("UprootedHook.Entry")
   - Wrapped in try/catch for safety
4. CreateInstance triggers [ModuleInitializer] and constructor in hook DLL
5. Managed hook spawns background thread to inject Avalonia UI
```

Source files:

| File | Platform | Description |
|------|----------|-------------|
| `tools/uprooted_profiler.c` | Windows | Win32 APIs, WCHAR strings, DLL exports via .def |
| `tools/uprooted_profiler_linux.c` | Linux | POSIX APIs, UTF-16 helpers, SO exports via visibility |
| `tools/uprooted_profiler.def` | Windows | Module definition file for DLL export renaming |

---

## GUID and Environment Variables

### Profiler GUID

```
{D1A6F5A0-1234-4567-89AB-CDEF01234567}
```

```c
static const MYGUID CLSID_UprootedProfiler =
    { 0xD1A6F5A0, 0x1234, 0x4567, { 0x89, 0xAB, 0xCD, 0xEF, 0x01, 0x23, 0x45, 0x67 } };
```

### Required Environment Variables

| Variable | Value | Purpose |
|----------|-------|---------|
| `CORECLR_ENABLE_PROFILING` | `1` | Master switch. Tells CoreCLR to look for a profiler. |
| `CORECLR_PROFILER` | `{D1A6F5A0-...}` | CLSID passed to `DllGetClassObject` for COM class verification. |
| `CORECLR_PROFILER_PATH` | Path to DLL/SO | Filesystem path the runtime loads via `LoadLibrary`/`dlopen`. |
| `DOTNET_ReadyToRun` | `0` | Disables R2R precompiled images. See [Why DOTNET_ReadyToRun=0](#why-dotnet_readytorun0). |

See [Installation Guide](INSTALLATION.md) for deployment details on each platform.

---

## ICorProfilerCallback Implementation

The profiler implements COM from scratch using hand-built vtables in plain C, without
any Windows SDK headers.

### COM Vtable Setup

```c
#define TOTAL_VTABLE_SIZE 128
static void* g_vtable[TOTAL_VTABLE_SIZE];

struct UprootedProfiler {
    void** vtable;
};
```

All 128 slots default to `Stub_OK` (returns S_OK). The four real callbacks are placed
at their correct vtable indices:

```c
static UprootedProfiler* CreateProfiler(void) {
    for (int i = 0; i < TOTAL_VTABLE_SIZE; i++)
        g_vtable[i] = (void*)Stub_OK;

    g_vtable[0] = (void*)Prof_QueryInterface;   /* IUnknown */
    g_vtable[1] = (void*)Prof_AddRef;
    g_vtable[2] = (void*)Prof_Release;
    g_vtable[3]  = (void*)Prof_Initialize;       /* ICorProfilerCallback */
    g_vtable[4]  = (void*)Prof_Shutdown;
    g_vtable[14] = (void*)Prof_ModuleLoadFinished;
    g_vtable[23] = (void*)Prof_JITCompilationStarted;

    g_profilerInstance.vtable = g_vtable;
    return &g_profilerInstance;
}
```

Any unimplemented callback the runtime invokes simply returns success.

### QueryInterface

Accepts `IID_IUnknown` and all `ICorProfilerCallback` versions (1 through 11),
ensuring compatibility across CoreCLR releases:

```c
static int IsProfilerCallbackGUID(const MYGUID* riid) {
    return MyIsEqualGUID(riid, &IID_ICorProfilerCallback) ||
           MyIsEqualGUID(riid, &IID_ICorProfilerCallback2) ||
           /* ... through ICorProfilerCallback11 ... */
           MyIsEqualGUID(riid, &IID_ICorProfilerCallback11);
}
```

Reference counting uses `InterlockedIncrement`/`InterlockedDecrement` on Windows and
GCC `__sync_add_and_fetch`/`__sync_sub_and_fetch` on Linux.

### Prof_Initialize

Called immediately after instantiation. The profiler:

1. Runs the process guard (see [Process Guard](#process-guard))
2. Obtains `ICorProfilerInfo` via `QueryInterface` on the passed-in `IUnknown`
3. Sets the event mask:

```c
DWORD mask = COR_PRF_MONITOR_JIT_COMPILATION   /* 0x00000020 */
           | COR_PRF_MONITOR_MODULE_LOADS       /* 0x00000004 */
           | 0x00080000;   /* COR_PRF_DISABLE_ALL_NGEN_IMAGES */
hr = setMask(g_profilerInfo, mask);
```

Returning `E_FAIL` (0x80004005) causes the runtime to detach the profiler cleanly.

### Prof_ModuleLoadFinished

Called each time a .NET module finishes loading. The profiler:

1. **Tracks CoreLib** -- saves `System.Private.CoreLib`'s module ID (never used as
   injection target -- recursive calls during `Assembly.LoadFrom` cause stack overflow)
2. **Selects injection target** -- tries each non-system module via `PrepareTargetModule()`,
   which checks for a `System.Object` TypeRef. First module with valid metadata wins.
3. **Injects immediately** -- enumerates methods, finds the first with a body
   (non-abstract, non-P/Invoke, CodeRVA != 0), and rewrites it via `DoInjectIL`.

```c
if (!g_targetReady && moduleId != g_corelibModuleId &&
    wcsncmp(modName, L"System.", 7) != 0 &&
    wcsncmp(modName, L"Microsoft.", 10) != 0) {
    PrepareTargetModule(moduleId);
}
```

### Prof_JITCompilationStarted

Fallback injection path. If `ModuleLoadFinished` injection did not succeed, this
callback intercepts JIT compilation of methods in the target module. Uses atomic
compare-and-swap to claim the one-shot injection slot:

```c
if (InterlockedCompareExchange(&g_injectionCount, 1, 0) != 0) return 0;
```

---

## IL Injection in Detail

The `DoInjectIL` function rewrites a target method's IL body, prepending 26 bytes of
injection code wrapped in try/catch, followed by the original method body.

### Token Creation

Before injection, metadata tokens are created in the target module via `IMetaDataEmit`:

| Token | References | Created by |
|-------|-----------|------------|
| `g_tokAssemblyTR` | `System.Reflection.Assembly` TypeRef | `SearchTypeRef` or `DefineTypeRefByName` |
| `g_tokLoadFromMR` | `Assembly.LoadFrom(string)` MemberRef | `DefineMemberRef` with signature blob |
| `g_tokCreateInstMR` | `Assembly.CreateInstance(string)` MemberRef | `DefineMemberRef` with signature blob |
| `g_tokExceptionTR` | `System.Exception` TypeRef | `SearchTypeRef` or `DefineTypeRefByName` |
| `g_tokPathString` | UserString: hook DLL path | `DefineUserString` |
| `g_tokTypeString` | UserString: `"UprootedHook.Entry"` | `DefineUserString` |

Method signature blobs are built manually:

```c
/* LoadFrom: static, 1 param, returns CLASS(Assembly), takes STRING */
sig[0] = 0x00;  /* DEFAULT calling convention */
sig[1] = 0x01;  /* 1 parameter */
sig[2] = 0x12;  /* ELEMENT_TYPE_CLASS */
/* + compressed Assembly TypeRef token */
sig[N] = 0x0E;  /* ELEMENT_TYPE_STRING */

/* CreateInstance: instance, 1 param, returns OBJECT, takes STRING */
BYTE sig[] = { 0x20, 0x01, 0x1C, 0x0E };
/* 0x20=HASTHIS, 0x01=1 param, 0x1C=OBJECT, 0x0E=STRING */
```

### The 26-Byte IL Injection

```
Offset  Bytes          Instruction              Notes
------  -----          -----------              -----
 0      72 XX XX XX XX ldstr <pathString>        TRY START - push DLL path
 5      28 XX XX XX XX call  <LoadFrom>          Assembly.LoadFrom -> Assembly
10      72 XX XX XX XX ldstr <typeString>        push "UprootedHook.Entry"
15      6F XX XX XX XX callvirt <CreateInstance>  Assembly.CreateInstance -> object
20      26             pop                       discard return value
21      DE 03          leave.s +3                jump to offset 26
23      26             pop                       CATCH START - discard exception
24      DE 00          leave.s +0                jump to offset 26
26      ...            <original code>           method continues normally
```

Equivalent C#:

```csharp
try {
    Assembly asm = Assembly.LoadFrom(@"C:\...\UprootedHook.dll");
    asm.CreateInstance("UprootedHook.Entry");
} catch (Exception) { }
// original method body
```

### New Method Body Layout

```
+------------------------------+
| Fat Header (12 bytes)        |  Flags, MaxStack, CodeSize, LocalsSig
+------------------------------+
| Injection IL (26 bytes)      |  try { LoadFrom + CreateInstance } catch { }
+------------------------------+
| Original IL Code (N bytes)   |  Unchanged original method body
+------------------------------+
| Padding (0-3 bytes)          |  Align to 4-byte boundary
+------------------------------+
| EH Section Header (4 bytes)  |  Kind=0x41, DataSize=28
+------------------------------+
| Fat EH Clause (24 bytes)     |  try/catch bounds + Exception token
+------------------------------+
```

The fat header is always used (even if the original had a tiny header) because the
`MoreSects` flag is needed for the EH table:

```c
USHORT fatFlags = (3 << 12) | CorILMethod_FatFormat | CorILMethod_MoreSects;
```

The EH section uses fat format clauses (24 bytes each):

```c
ehSection[0] = 0x41;  /* EHTable | FatFormat */
/* Fat clause: */
*(uint32_t*)(clause + 0)  = 0;   /* Flags: catch */
*(uint32_t*)(clause + 4)  = 0;   /* TryOffset */
*(uint32_t*)(clause + 8)  = 23;  /* TryLength */
*(uint32_t*)(clause + 12) = 23;  /* HandlerOffset */
*(uint32_t*)(clause + 16) = 3;   /* HandlerLength */
*(uint32_t*)(clause + 20) = g_tokExceptionTR;  /* ClassToken */
```

### SetILFunctionBody

The rewritten body is allocated via `IMethodMalloc` and installed with
`SetILFunctionBody`. The runtime takes ownership of the memory and will JIT the new
IL when the method is called, even if the original had R2R precompiled code.

---

## Why DOTNET_ReadyToRun=0

Ready-to-Run (R2R) assemblies contain precompiled native code that the runtime can
execute directly, skipping JIT compilation. If a method is never JIT-compiled, the
`JITCompilationStarted` callback never fires.

The profiler defends against this three ways:

1. **Environment variable** -- `DOTNET_ReadyToRun=0` disables R2R globally.
2. **Event mask flag** -- `COR_PRF_DISABLE_ALL_NGEN_IMAGES` (0x00080000) tells the
   runtime to ignore precompiled images for this profiler session.
3. **Direct injection from ModuleLoadFinished** -- the profiler does not wait for JIT
   at all. It injects IL immediately when the target module loads, and
   `SetILFunctionBody` forces the runtime to JIT the modified IL when eventually called.

---

## Process Guard

The profiler environment variables are set globally, so every .NET process would load
it. To avoid interfering with dotnet CLI, MSBuild, NuGet, etc., `Prof_Initialize`
checks the host process name and returns `E_FAIL` to detach for non-Root processes.

**Windows** -- `GetModuleFileNameW(NULL, ...)`, case-insensitive comparison against `L"Root.exe"`:

```c
WCHAR exePath[MAX_PATH];
GetModuleFileNameW(NULL, exePath, MAX_PATH);
WCHAR* exeName = wcsrchr(exePath, L'\\');
exeName = exeName ? exeName + 1 : exePath;
if (_wcsicmp(exeName, L"Root.exe") != 0)
    return 0x80004005; /* E_FAIL = detach */
```

**Linux** -- `readlink("/proc/self/exe", ...)`, case-sensitive comparison against `"Root"`:

```c
char exePath[PATH_MAX];
ssize_t len = readlink("/proc/self/exe", exePath, sizeof(exePath) - 1);
exePath[len] = '\0';
const char* exeName = strrchr(exePath, '/');
exeName = exeName ? exeName + 1 : exePath;
if (strcmp(exeName, "Root") != 0)
    return 0x80004005; /* E_FAIL = detach */
```

---

## Windows vs Linux Differences

The CoreCLR profiling API is cross-platform -- COM vtable layout, metadata APIs, IL
opcodes, and injection strategy are identical. Differences are in OS-level APIs only.

| Concern | Windows | Linux |
|---------|---------|-------|
| **Wide strings** | Native `WCHAR` (UTF-16), `L"..."` | `uint16_t` arrays, char-by-char |
| **String ops** | `wcscmp`, `wcsstr`, `_wcsicmp` | Custom `u16cmp`, `u16str` |
| **UTF-16/8 conversion** | `WideCharToMultiByte` | Custom `u16_to_utf8`, `utf8_to_u16` |
| **Executable path** | `GetModuleFileNameW(NULL, ...)` | `readlink("/proc/self/exe")` |
| **Process ID** | `GetCurrentProcessId()` | `getpid()` |
| **Config dir** | `SHGetKnownFolderPath(&FOLDERID_LocalAppData)` | `getenv("HOME")/.local/share/uprooted/` |
| **Hook DLL path** | `%LocalAppData%\Root\uprooted\UprootedHook.dll` | `~/.local/share/uprooted/UprootedHook.dll` |
| **Log path** | `%LocalAppData%\Root\uprooted\profiler.log` | `~/.local/share/uprooted/profiler.log` |
| **Timestamps** | `GetLocalTime` / `SYSTEMTIME` | `gettimeofday` / `localtime` |
| **Atomics** | `InterlockedCompareExchange`, etc. | `__sync_val_compare_and_swap`, etc. |
| **Calling convention** | `__stdcall` (explicit) | Default (cdecl/System V) |
| **DllMain** | Present; `DisableThreadLibraryCalls` | Not needed; no equivalent |
| **Exports** | `.def` file renames symbols | `__attribute__((visibility("default")))` |
| **Library loading** | `LoadLibrary` | `dlopen` |

On Linux, `wchar_t` is 32 bits but CoreCLR metadata needs 16-bit UTF-16. The Linux
port defines `typedef uint16_t WCHAR` and builds string constants as static arrays:

```c
static const WCHAR W_System_Object[] = {
    'S','y','s','t','e','m','.','O','b','j','e','c','t', 0
};
```

---

## Export Definitions

`tools/uprooted_profiler.def` maps internal names to standard COM entry points:

```
LIBRARY uprooted_profiler
EXPORTS
    DllGetClassObject = UprootedDllGetClassObject
    DllCanUnloadNow = UprootedDllCanUnloadNow
```

### DllGetClassObject

The runtime calls this after loading the DLL. Verifies the CLSID matches and returns
the class factory, which in turn creates the profiler instance:

```c
HRESULT UprootedDllGetClassObject(
    const MYGUID* rclsid, const MYGUID* riid, void** ppv) {
    if (MyIsEqualGUID(rclsid, &CLSID_UprootedProfiler)) {
        *ppv = &g_classFactory;
        return 0;
    }
    return 0x80040111; /* CLASS_E_CLASSNOTAVAILABLE */
}
```

### DllCanUnloadNow

Returns `S_FALSE` (1) -- the profiler stays loaded for the process lifetime.

### Why the Renaming?

Windows uses `UprootedDllGetClassObject` internally to avoid symbol conflicts, with
the `.def` file mapping to `DllGetClassObject`. On Linux, the functions are named
`DllGetClassObject`/`DllCanUnloadNow` directly, with visibility controlled by
`__attribute__((visibility("default")))`.

---

## Building

### Windows (MSVC)

```
cl.exe /LD /O2 /Fe:uprooted_profiler.dll uprooted_profiler.c ^
       /link ole32.lib kernel32.lib shell32.lib /DEF:uprooted_profiler.def
```

| Flag | Meaning |
|------|---------|
| `/LD` | Create DLL |
| `/O2` | Optimize for speed |
| `/Fe:` | Output filename |
| `ole32.lib` | `CoTaskMemFree` |
| `kernel32.lib` | `GetModuleFileNameW`, `InterlockedCompareExchange`, etc. |
| `shell32.lib` | `SHGetKnownFolderPath` |
| `/DEF:` | Export definition file |

The source also has `#pragma comment(lib, ...)` for automatic linking.

**Output:** `uprooted_profiler.dll` (deploy), plus `.lib`/`.exp` (not needed).

### Linux (GCC)

```
gcc -shared -fPIC -O2 -o libuprooted_profiler.so tools/uprooted_profiler_linux.c
```

| Flag | Meaning |
|------|---------|
| `-shared` | Create shared object |
| `-fPIC` | Position-independent code |
| `-O2` | Optimize for speed |

No extra libraries needed -- only standard C and POSIX APIs.

**Output:** `libuprooted_profiler.so`

---

## Debugging

### The profiler.log File

The profiler writes timestamped logs to `profiler.log` in append mode:

| Platform | Path |
|----------|------|
| Windows | `%LocalAppData%\Root\uprooted\profiler.log` |
| Linux | `~/.local/share/uprooted/profiler.log` |

### Example Successful Log

```
[12:00:00.001] DllGetClassObject called           <-- Runtime loaded profiler
[12:00:00.002] ClassFactory::CreateInstance
[12:00:00.003] === Uprooted Profiler Initialize ===
[12:00:00.003] PID: 12345
[12:00:00.003] Process: Root.exe                   <-- Process guard passed
[12:00:00.004] ICorProfilerInfo: hr=0x00000000
[12:00:00.004] SetEventMask(0x00080024): hr=0x00000000
[12:00:00.005] === Profiler Initialize done ===

[12:00:00.050] Module #1: System.Private.CoreLib
[12:00:00.100] Module #2: Root
[12:00:00.100] Trying as injection target: Root
[12:00:00.100]   Total TypeRefs: 0                 <-- No metadata, skipped
[12:00:00.100]   No System.Object TypeRef, skipping

[12:00:00.200] Module #5: Sentry
[12:00:00.200] Trying as injection target: Sentry
[12:00:00.201]   System.Object TypeRef=0x01000001 scope=0x23000001
[12:00:00.202]   LoadFrom MemberRef hr=0x00000000 token=0x0A000001
[12:00:00.202]   CreateInstance MemberRef hr=0x00000000 token=0x0A000002
[12:00:00.203]   ALL tokens created successfully!
[12:00:00.204]   Injecting into method 0x06000001: SomeMethod (RVA=0x2050)
[12:00:00.205] DoInjectIL: IL bytes: 72 01 00 00 70 28 01 00 0A 00 ...
[12:00:00.206] DoInjectIL: SetILFunctionBody hr=0x00000000
[12:00:00.206] DoInjectIL: *** IL INJECTION SUCCESSFUL ***
[12:00:00.206] *** TARGET MODULE: Sentry ***
```

### Diagnosing Common Failures

| Log message | Cause | Fix |
|-------------|-------|-----|
| No log file at all | Profiler not loading | Check all 4 env vars; verify DLL/SO path and permissions |
| "Not Root.exe, detaching" | Wrong process | Normal -- profiler detaches from non-Root processes |
| "No System.Object TypeRef" for all modules | No valid target | Verify Root's assemblies are loading |
| "IMetaDataImport/Emit failed" | Metadata access denied | Check HRESULT; `0x80131130` = in-memory module |
| "Token creation FAILED" | Bad resolution scope | Inspect logged HRESULT per token |
| "Method has MoreSects, skipping" | Method has existing EH | Normal -- tries next method |
| "SetILFunctionBody FAILED" | Invalid IL body | Check header values, code size, EH alignment |
| "No suitable method found" | All methods abstract/P/Invoke | Falls back to JITCompilationStarted |
| Injection OK but hook absent | DLL not at expected path | Verify UprootedHook.dll exists at PathString path |

### Diagnostic NOP Mode

The Windows source has a `DIAGNOSTIC_NOP_ONLY` compile-time flag that replaces the
injection IL with NOPs, verifying header/allocation/SetILFunctionBody without side
effects:

```c
#if DIAGNOSTIC_NOP_ONLY
    for (p = 0; p < INJECT_SIZE; p++) injection[p] = IL_NOP;
#endif
```

### Vtable Index Reference

These indices from `corprof.idl` and `cor.h` must match the runtime version:

```c
/* ICorProfilerInfo */
#define VT_PI_GetFunctionInfo            15
#define VT_PI_SetEventMask               16
#define VT_PI_GetModuleInfo              20
#define VT_PI_GetModuleMetaData          21
#define VT_PI_GetILFunctionBody          22
#define VT_PI_GetILFunctionBodyAllocator 23
#define VT_PI_SetILFunctionBody          24

/* IMetaDataImport */
#define VT_MI_CloseEnum         3
#define VT_MI_EnumTypeDefs      6
#define VT_MI_EnumTypeRefs      8
#define VT_MI_GetTypeRefProps  14
#define VT_MI_EnumMethods      18
#define VT_MI_GetMethodProps    30
#define VT_MI_FindTypeRef      55

/* IMetaDataEmit */
#define VT_ME_DefineTypeRefByName 12
#define VT_ME_DefineMemberRef    14
#define VT_ME_DefineUserString   28
```

If profiler behavior changes after a .NET runtime update, verify these indices have
not shifted in the new runtime's header files.
