# .NET Runtime Concepts for Uprooted

> **Related docs:** [CLR Profiler](CLR_PROFILER.md) | [Hook Reference](HOOK_REFERENCE.md) | [Architecture](ARCHITECTURE.md) | [Avalonia Patterns](AVALONIA_PATTERNS.md)

---

## Table of Contents

1. [Overview](#overview)
2. [CLR Profiler API](#clr-profiler-api)
3. [IL Injection](#il-injection)
4. [Single-File Apps](#single-file-apps)
5. [Assembly Scanning](#assembly-scanning)
6. [Startup Hook Mechanism](#startup-hook-mechanism)
7. [Reflection in .NET](#reflection-in-net)
8. [Threading](#threading)
9. [Memory Model](#memory-model)
10. [Debugging](#debugging)
11. [Platform Differences](#platform-differences)

---

## Overview

Uprooted injects code into a running .NET application -- Root Communications -- that
ships as a compiled, single-file Avalonia desktop app. There is no plugin API, no
extension points, and no source code. Every technique Uprooted uses depends on
understanding how the .NET runtime works at a level below normal application development.

This document covers the runtime concepts that make Uprooted's injection possible.
Every section references actual source files with line numbers. For the native profiler
implementation specifically, see the companion [CLR Profiler](CLR_PROFILER.md) document.

---

## CLR Profiler API

### What Is a CLR Profiler?

The Common Language Runtime (CLR) provides a profiling API that allows a native DLL
(or shared object on Linux) to observe and modify a managed .NET process. Originally
designed for performance profilers and code coverage tools, this API gives deep access
to the runtime: intercepting method JIT compilation, inspecting metadata, rewriting IL
bytecode, and receiving module load notifications.

Uprooted uses a small subset of this API -- just enough to inject a single method call
into a target method, which bootstraps the entire managed hook.

### The COM Interface Approach

The CLR profiler API is based on COM (Component Object Model) interfaces. The runtime
never calls C functions directly. Instead, it obtains an object pointer and invokes
methods through a vtable -- an array of function pointers at a known memory layout.

The key interfaces:

| Interface | Purpose |
|-----------|---------|
| `ICorProfilerCallback` | Callbacks the runtime invokes on the profiler (events) |
| `ICorProfilerInfo` | Methods the profiler calls on the runtime (queries, mutations) |
| `IMetaDataImport` | Read-only access to assembly metadata (types, methods, signatures) |
| `IMetaDataEmit` | Write access to assembly metadata (define new tokens) |
| `IClassFactory` | COM factory pattern -- runtime asks factory to create the profiler |

Uprooted builds vtables by hand in plain C, filling 128 slots with a no-op stub and
overriding only the four callbacks it needs (`Initialize`, `Shutdown`,
`ModuleLoadFinished`, `JITCompilationStarted`):

```c
// tools/uprooted_profiler.c, lines 1055-1073
static UprootedProfiler* CreateProfiler(void) {
    for (int i = 0; i < TOTAL_VTABLE_SIZE; i++)
        g_vtable[i] = (void*)Stub_OK;

    g_vtable[0] = (void*)Prof_QueryInterface;   // IUnknown
    g_vtable[1] = (void*)Prof_AddRef;
    g_vtable[2] = (void*)Prof_Release;
    g_vtable[3]  = (void*)Prof_Initialize;       // ICorProfilerCallback
    g_vtable[4]  = (void*)Prof_Shutdown;
    g_vtable[14] = (void*)Prof_ModuleLoadFinished;
    g_vtable[23] = (void*)Prof_JITCompilationStarted;
    // ...
}
```

On Linux, CoreCLR implements the same vtable layout without actual COM infrastructure.
The profiler code is structurally identical; only OS-level APIs differ (see
[Platform Differences](#platform-differences)).

### Environment Variables

The runtime discovers the profiler via environment variables set before the process
starts:

| Variable | Value | Purpose |
|----------|-------|---------|
| `CORECLR_ENABLE_PROFILING` | `1` | Master switch -- tells CoreCLR to look for a profiler |
| `CORECLR_PROFILER` | `{D1A6F5A0-1234-4567-89AB-CDEF01234567}` | CLSID the runtime passes to `DllGetClassObject` |
| `CORECLR_PROFILER_PATH` | Path to DLL/SO | Filesystem path loaded via `LoadLibrary` / `dlopen` |
| `DOTNET_ReadyToRun` | `0` | Disables precompiled native images (see [IL Injection](#il-injection)) |

The legacy `COR_PROFILER` / `COR_ENABLE_PROFILING` variables are for .NET Framework.
CoreCLR (.NET 5+) uses the `CORECLR_` prefix exclusively.

### Loading Sequence

1. Process starts, CoreCLR initializes.
2. Runtime reads `CORECLR_ENABLE_PROFILING=1` and `CORECLR_PROFILER_PATH`.
3. Runtime calls `LoadLibrary` (Windows) or `dlopen` (Linux) on the profiler DLL/SO.
4. Runtime calls `DllGetClassObject(CLSID, IID_IClassFactory, &factory)`.
5. Factory's `CreateInstance` returns a profiler object implementing `ICorProfilerCallback`.
6. Runtime calls `Prof_Initialize`, passing an `ICorProfilerInfo` interface pointer.
7. Profiler sets its event mask and returns S_OK (or E_FAIL to detach).

Because the profiler environment variables are system-wide, every .NET process would
load it. `Prof_Initialize` checks the host process name and returns E_FAIL for anything
that is not Root.exe (see `tools/uprooted_profiler.c`, lines 891-905).

### Event Mask

The profiler requests three event categories via `SetEventMask`:

- `COR_PRF_MONITOR_JIT_COMPILATION` (0x20) -- fires `JITCompilationStarted` per method.
- `COR_PRF_MONITOR_MODULE_LOADS` (0x04) -- fires `ModuleLoadFinished` per module.
- `COR_PRF_DISABLE_ALL_NGEN_IMAGES` (0x80000) -- forces the runtime to ignore
  precompiled (R2R) code.

For full details on the vtable indices, callback implementations, and the
`DllGetClassObject` / `IClassFactory` chain, see [CLR Profiler](CLR_PROFILER.md).

---

## IL Injection

### Concept

Every .NET method has an IL (Intermediate Language) body -- a sequence of bytecode
instructions stored in the assembly's metadata. When a method is first called, the JIT
compiler translates its IL into native machine code. The profiler API lets you replace a
method's IL body before or during JIT compilation.

Uprooted prepends 26 bytes of new IL to a target method. These 26 bytes call
`Assembly.LoadFrom()` to load the managed hook DLL and `Assembly.CreateInstance()` to
instantiate the entry point class. The original method body follows unchanged.

### GetILFunctionBody and SetILFunctionBody

These are the two `ICorProfilerInfo` methods at the core of IL injection:

- `GetILFunctionBody(moduleId, methodToken, &body, &size)` -- returns a pointer to the
  original IL bytes.
- `SetILFunctionBody(moduleId, methodToken, newBody)` -- replaces the IL body. Memory
  must be allocated via `IMethodMalloc` from `GetILFunctionBodyAllocator`.

### What Gets Rewritten and Why

The profiler does not target a specific known method. Instead, it picks the first
suitable method in the first suitable module:

1. **Module selection** (`ModuleLoadFinished`, line 942): Each non-system module is
   tested. Root.dll (the single-file host) has no TypeRef metadata and is skipped.
   The first module with a `System.Object` TypeRef wins -- typically Sentry.dll.

2. **Method selection** (`PrepareTargetModule`, lines 570-624): Within the target
   module, the profiler enumerates TypeDefs and methods, selecting the first with a
   real code body (CodeRVA != 0, not abstract, not P/Invoke).

3. **Why not target CoreLib?** (comment at line 19): Any CoreLib method can be called
   recursively during `Assembly.LoadFrom`. If the injection target is itself a CoreLib
   method, this creates infinite recursion and a stack overflow.

### Token Creation

Before injecting IL, the profiler creates metadata tokens in the target module via
`IMetaDataEmit` for the types, methods, and strings the injected code references:

- `DefineTypeRefByName` -- TypeRefs for `System.Reflection.Assembly` and
  `System.Exception`.
- `DefineMemberRef` -- MemberRefs for `Assembly.LoadFrom(string)` and
  `Assembly.CreateInstance(string)`, with hand-built method signature blobs.
- `DefineUserString` -- string tokens for the hook DLL path and `"UprootedHook.Entry"`.

### ReadyToRun and JIT Bypass

Ready-to-Run (R2R) assemblies contain precompiled native code. If a method has R2R
code, the runtime skips JIT entirely -- `JITCompilationStarted` never fires and
`SetILFunctionBody` has no effect. Uprooted defends against this three ways:

1. `DOTNET_ReadyToRun=0` environment variable disables R2R globally.
2. `COR_PRF_DISABLE_ALL_NGEN_IMAGES` event mask flag in the profiler.
3. Direct injection from `ModuleLoadFinished` -- the profiler calls `SetILFunctionBody`
   immediately when the target module loads, before JIT is relevant.

For the full 26-byte IL payload layout, fat header construction, and exception handling
section details, see [CLR Profiler -- IL Injection in Detail](CLR_PROFILER.md#il-injection-in-detail).

---

## Single-File Apps

### How Root Ships

Root Communications ships as a single-file .NET application. The `dotnet publish`
command with `/p:PublishSingleFile=true` bundles assemblies, configuration, and native
libraries into a single executable. At runtime, the single-file host extracts bundled
assemblies into memory or a temporary directory.

The executable itself (`Root.dll`) appears as a module with minimal metadata -- no
TypeRef entries, no meaningful type definitions, and zero usable injection surface.

### Challenges for Injection

Single-file apps break several standard .NET patterns:

1. **`Type.GetType("Namespace.Type, AssemblyName")` fails.** Assembly resolution does
   not know how to find assemblies extracted from a bundle. This is why
   `AvaloniaReflection` never uses `Type.GetType()` and scans loaded assemblies directly.

2. **`Assembly.Load("AssemblyName")` may fail.** The default `AssemblyLoadContext` does
   not always know about bundled assemblies until the host loads them.

3. **Root.dll has no metadata.** The profiler detects this and skips to the next module:

   ```c
   // tools/uprooted_profiler.c, lines 440-445
   unsigned int tokObjectTR = SearchTypeRef(pImport, importVt,
                                             L"System.Object", &runtimeScope);
   if (!tokObjectTR) {
       PLog("  No System.Object TypeRef, skipping");
       return FALSE;
   }
   ```

4. **Assembly paths are not on disk.** `Assembly.Location` returns empty for in-memory
   assemblies. Uprooted's hook DLL is deployed separately, so `Assembly.LoadFrom()` uses
   a known filesystem path.

### How Uprooted Handles This

The profiler targets a third-party assembly (like Sentry.dll) that has normal metadata.
The injected IL calls `Assembly.LoadFrom()` with an absolute path to `UprootedHook.dll`,
bypassing bundle resolution entirely. Once loaded, the hook runs in Root's process and
sees all of Root's assemblies through `AppDomain.CurrentDomain.GetAssemblies()`.

---

## Assembly Scanning

### The Problem

Uprooted has no compile-time reference to any Avalonia assembly. All types must be
discovered at runtime from whatever assemblies Root has loaded.

### AppDomain.CurrentDomain.GetAssemblies()

This returns every assembly loaded into the current application domain. Because
Uprooted's hook runs inside Root's process, it sees Root's assemblies. The hook polls
every 250ms (`hook/StartupHook.cs`, lines 149-163) because assembly loading is
asynchronous -- Avalonia assemblies may not be loaded when the hook starts executing.

### AppDomain vs AssemblyLoadContext

.NET 5+ replaced the multi-AppDomain model with `AssemblyLoadContext` (ALC). There is
only one AppDomain, but there can be multiple ALCs. Uprooted uses
`AppDomain.CurrentDomain.GetAssemblies()` because:

- It returns assemblies from all ALCs.
- Root loads everything into the default ALC.
- `Assembly.LoadFrom()` loads into the default ALC, so the hook sees Root's types.

If `NativeEntry` is used (via `hostfxr`), a separate ALC may be created, causing the
hook to miss Root's assemblies (`hook/NativeEntry.cs`, lines 48-52). This is why
profiler-based IL injection is preferred over the native entry approach.

### Assembly.LoadFrom vs Assembly.Load

- `Assembly.Load("Name")` -- searches trusted platform assemblies and probing paths.
  Fails for assemblies outside those paths.
- `Assembly.LoadFrom("path/to/Assembly.dll")` -- loads from an explicit file path.
  Used by the profiler because the hook DLL is at a known location outside Root's bundle.

---

## Startup Hook Mechanism

### DOTNET_STARTUP_HOOKS

.NET provides an official mechanism for injecting managed code at startup. The
`DOTNET_STARTUP_HOOKS` environment variable specifies assembly paths. The runtime loads
each assembly before `Main()` and invokes a conventional entry point:

```csharp
// hook/StartupHook.cs, lines 1-9
// Must be: internal class StartupHook (no namespace) with public static void Initialize().
internal class StartupHook
{
    public static void Initialize() { /* ... */ }
}
```

Requirements: class named `StartupHook`, no namespace, `internal`, with a
`public static void Initialize()` method.

### Dual Entry Path

Uprooted's `StartupHook.cs` works via both mechanisms:

- **As a startup hook**: `DOTNET_STARTUP_HOOKS` points to `UprootedHook.dll`. The
  runtime calls `StartupHook.Initialize()` before `Main()`.
- **Via profiler injection**: The IL calls `Assembly.CreateInstance("UprootedHook.Entry")`,
  triggering `[ModuleInitializer]` on `Entry.ModuleInit()` (`hook/Entry.cs`, line 16),
  which calls `StartupHook.Initialize()`.

Both paths are guarded by `Interlocked.CompareExchange` on a static `_initialized`
flag (`hook/Entry.cs`, line 18) to prevent double initialization.

### When Startup Hooks Fall Short

1. **Single-file apps may ignore the variable** or fail to resolve the hook assembly.
2. **Detection** -- applications can check for `DOTNET_STARTUP_HOOKS`. The profiler API
   operates at the native level and is harder to detect.
3. **Flexibility** -- the profiler can inject into any method at any time, not just
   before `Main()`.

The profiler is the primary injection path; startup hooks serve as a fallback.

---

## Reflection in .NET

### Why Reflection Is Necessary

Uprooted has no compile-time reference to Avalonia. To create controls, read properties,
or modify the visual tree, every operation goes through `System.Reflection`.

### Type.GetType Limitations

`Type.GetType("Namespace.Type, AssemblyName")` requires the assembly to be resolvable
by the default loader. In a single-file app, this often fails. From the source:

```csharp
// hook/AvaloniaReflection.cs, line 9
// Single-file apps can't use Type.GetType("..., Assembly") so we scan loaded assemblies.
```

### Assembly Scanning Pattern

Instead of `Type.GetType()`, Uprooted scans loaded assemblies and builds a type map.
`AvaloniaReflection.ResolveTypes()` (lines 148-170) iterates
`AppDomain.CurrentDomain.GetAssemblies()`, filters to Avalonia assemblies by name
prefix, calls `asm.GetTypes()` on each, and builds a `Dictionary<string, Type>` keyed
by full name. A helper `Find("Avalonia.Application")` then looks up each of the ~80
needed types.

The `try/catch` around `asm.GetTypes()` is essential -- some assemblies throw
`ReflectionTypeLoadException` if they have unresolvable dependencies.

### BindingFlags

.NET reflection requires explicit `BindingFlags` to find non-public or static members:

| Flag | Purpose |
|------|---------|
| `BindingFlags.Public` | Public members |
| `BindingFlags.NonPublic` | Private, protected, internal members |
| `BindingFlags.Instance` | Instance (non-static) members |
| `BindingFlags.Static` | Static members |
| `BindingFlags.DeclaredOnly` | Exclude inherited members |

Some Avalonia internals require `NonPublic` access:

```csharp
// hook/AvaloniaReflection.cs, lines 537-540
_windowImplSInstances = type.GetField("s_instances",
    BindingFlags.NonPublic | BindingFlags.Static);
```

### PropertyInfo and MethodInfo Caching

Reflection lookups are expensive. `AvaloniaReflection` resolves all properties and
methods once during Phase 1 and caches them as fields:

```csharp
// hook/AvaloniaReflection.cs, lines 72-79 (partial)
private PropertyInfo? _appCurrent;
private PropertyInfo? _appLifetime;
private PropertyInfo? _dispatcherUIThread;
private MethodInfo? _dispatcherPost;
private MethodInfo? _getVisualChildren;
```

A `PropertyInfo.GetValue()` call is still slower than direct access, but the lookup
cost is paid only once.

---

## Threading

### The Injection Thread

Uprooted runs initialization on a dedicated background thread to avoid blocking Root:

```csharp
// hook/StartupHook.cs, lines 20-26
var thread = new Thread(InjectorLoop)
{
    IsBackground = true,
    Name = "Uprooted-Injector"
};
thread.Start();
```

`IsBackground = true` ensures the thread does not prevent process exit. The main
injection logic uses this dedicated thread (rather than the ThreadPool) because it
involves long-running polling loops that would starve pool workers.

### SynchronizationContext and Avalonia's Dispatcher

GUI frameworks use a `SynchronizationContext` to marshal work to the UI thread.
Avalonia's implementation is the `Dispatcher` class. All UI operations -- creating
controls, modifying the visual tree, changing properties -- must run on the UI thread.

`RunOnUIThread` in `AvaloniaReflection` (lines 553-604) handles this via reflection:
it gets the `Dispatcher.UIThread` static property, then invokes `Dispatcher.Post()`.

Key gotcha: `DispatcherPriority` is a struct in Avalonia 11+, not an enum. You cannot
use `Enum.Parse` -- you must find the `Normal` static property on the priority type.
The method tries several resolution strategies (static property, static field, enum
parse, default value) to handle different Avalonia versions.

### Thread Safety in the Profiler

The native profiler uses atomic operations because callbacks fire from any thread:

```c
// tools/uprooted_profiler.c, lines 210-228
static volatile LONG g_injectionCount = 0;
static volatile LONG g_targetReady = 0;
```

The one-shot injection uses `InterlockedCompareExchange` (line 1028) to ensure exactly
one thread performs the injection even if multiple methods JIT concurrently.

---

## Memory Model

### GC Considerations with Reflected Objects

References to Avalonia objects discovered via reflection are normal managed references.
The GC tracks them normally. However, there are subtleties:

**Preventing premature collection.** Objects like `HtmlPatchVerifier` (which owns a
`FileSystemWatcher`) must be rooted to stay alive:

```csharp
// hook/StartupHook.cs, lines 10-11, 47
private static HtmlPatchVerifier? s_patchVerifier;
// ...
s_patchVerifier = verifier; // prevent GC
```

The `static` field ensures the verifier lives for the process lifetime.

**Timer rooting.** The `SidebarInjector`'s 200ms polling timer (line 22) is stored as an
instance field. If it were a local variable, the GC could collect the timer before its
callback fires.

**Circular references.** The GC uses mark-and-sweep (not reference counting), so
circular references between Avalonia controls are handled correctly.

**Event handler leaks.** Subscribing to an event on a long-lived object with a handler
on a short-lived object prevents collection of the short-lived object. The
`SidebarInjector` stores references to Root's controls and uses a `Timer` that holds a
delegate -- the timer must be stopped if injection is torn down.

---

## Debugging

### Log Files

Uprooted produces two log files at different stages:

| Log | Source | Path (Windows) |
|-----|--------|----------------|
| `profiler.log` | Native profiler (C) | `%LocalAppData%\Root\uprooted\` |
| `uprooted-hook.log` | Managed hook (C#) | `%LocalAppData%\Root Communications\Root\profile\default\` |

The native log captures everything before managed code runs (`tools/uprooted_profiler.c`,
lines 172-186). The managed log (`hook/Logger.cs`, lines 15-25) uses `lock` and
`File.AppendAllText` for thread-safe append, with a swallowed `catch` to prevent
logging failures from crashing the hook.

### Attaching a Debugger

- **Managed hook:** Attach Visual Studio or Rider to Root.exe. PDB must be next to
  `UprootedHook.dll` for breakpoints to resolve.
- **Native profiler:** Use Visual Studio's native debugger or WinDbg. The profiler runs
  before managed code, so attach early or use "launch with debugger" mode.
- **Mixed-mode:** Visual Studio supports simultaneous native + managed debugging.

### Common Failure Patterns

| Symptom | Likely Cause | Investigation |
|---------|-------------|---------------|
| No `profiler.log` at all | Profiler not loading | Verify all env vars and DLL path |
| "Not Root.exe, detaching" | Process guard triggered | Normal for non-Root processes |
| "No System.Object TypeRef" for all modules | No valid injection target | Check module list in log |
| `profiler.log` OK but no `uprooted-hook.log` | Hook DLL missing | Verify UprootedHook.dll at expected path |
| "Phase 1 FAILED" in hook log | Avalonia not loading in time | Check Root version; increase timeout |
| "Type resolution failed" | Avalonia types renamed | Check Root's Avalonia version |
| `MissingMethodException` | System.Text.Json used | Never use System.Text.Json in hook |

### Diagnostic NOP Mode

The profiler has a `DIAGNOSTIC_NOP_ONLY` compile flag that replaces injection IL with
NOPs, verifying header construction and `SetILFunctionBody` without side effects
(`tools/uprooted_profiler.c`, lines 734-737).

---

## Platform Differences

### Profiler Loading

| Aspect | Windows | Linux |
|--------|---------|-------|
| Library format | `.dll` (PE) | `.so` (ELF) |
| Load mechanism | `LoadLibrary` | `dlopen` |
| Export mechanism | `.def` file remaps symbols | `__attribute__((visibility("default")))` |
| Calling convention | `__stdcall` (explicit) | System V ABI (default) |

On Windows, `DllMain` runs at load time but Uprooted only calls
`DisableThreadLibraryCalls` -- `SHGetKnownFolderPath` is unsafe inside the loader lock
and can deadlock. On Linux, there is no `DllMain`; initialization happens in
`Prof_Initialize`.

### Path Resolution

The managed hook resolves paths per platform via `PlatformPaths.GetUprootedDir()`
(`hook/PlatformPaths.cs`, lines 16-29):

| Platform | Hook DLL | Profiler Log |
|----------|----------|-------------|
| Windows | `%LocalAppData%\Root\uprooted\UprootedHook.dll` | `%LocalAppData%\Root\uprooted\profiler.log` |
| Linux | `~/.local/share/uprooted/UprootedHook.dll` | `~/.local/share/uprooted/profiler.log` |

### String Handling

CoreCLR's metadata API uses UTF-16 strings. On Windows, `WCHAR` is natively UTF-16.
On Linux, `wchar_t` is 32-bit, so the profiler defines `typedef uint16_t WCHAR` and
builds string constants as static arrays:

```c
// (from uprooted_profiler_linux.c)
static const WCHAR W_System_Object[] = {
    'S','y','s','t','e','m','.','O','b','j','e','c','t', 0
};
```

### Atomics

| Operation | Windows | Linux |
|-----------|---------|-------|
| Compare-and-swap | `InterlockedCompareExchange` | `__sync_val_compare_and_swap` |
| Increment | `InterlockedIncrement` | `__sync_add_and_fetch` |
| Decrement | `InterlockedDecrement` | `__sync_sub_and_fetch` |
| Exchange | `InterlockedExchange` | `__sync_lock_test_and_set` |

### Environment Variable Setup

On Windows, the installer sets machine-wide environment variables via the registry or
`setx`. On Linux, the install script sets variables in shell profile files or systemd
unit overrides. In both cases, the variables must be set before Root.exe launches --
the profiler cannot be loaded on-demand, so a process restart is required after
installation.
