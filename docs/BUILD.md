# Build Pipeline Guide

> **Related docs:**
> [Index](INDEX.md) | [Architecture](ARCHITECTURE.md) | [Installation Guide](INSTALLATION.md) | [Contributing](../CONTRIBUTING.md)

This document covers every stage of the Uprooted build pipeline -- from
compiling individual components to producing a self-contained installer
binary. Commands are copy-pasteable and assume you are at the repository
root unless stated otherwise.

---

## Table of Contents

1. [Prerequisites](#prerequisites)
2. [TypeScript Build](#typescript-build)
3. [C# Hook Build](#c-hook-build)
4. [CLR Profiler Build](#clr-profiler-build)
5. [Tauri Installer Build](#tauri-installer-build)
6. [Full Pipeline](#full-pipeline)
7. [Development Workflow](#development-workflow)
8. [CI/CD Workflows](#cicd-workflows)
9. [Build Artifacts Inventory](#build-artifacts-inventory)
10. [Version Management](#version-management)

---

## Prerequisites

### Node.js and pnpm

Node.js 20+ is required (the root `package.json` enforces `"node": ">=20"`).
pnpm is the package manager for both the TypeScript layer and the installer
frontend.

```bash
# Install pnpm globally if not already present
npm install -g pnpm

# Install project dependencies
pnpm install
```

### .NET 10 SDK

The C# hook targets `net10.0` (preview). Install the .NET 10 SDK from the
official dotnet site or via `actions/setup-dotnet` in CI.

```bash
dotnet --version   # Should report 10.x.x
```

### C/C++ Compiler

The native CLR profiler is written in C and must be compiled to a shared
library.

- **Windows:** MSVC (`cl.exe`) from Visual Studio 2022 Build Tools.
  The build script locates `vcvarsall.bat` automatically.
- **Linux:** GCC. Install via your package manager
  (`sudo apt-get install gcc`).

### Rust and Tauri CLI

The installer application is a Tauri 2 app. You need:

- Rust stable toolchain (`rustup install stable`)
- Tauri CLI (installed as a pnpm dev dependency in `installer/`)

```bash
# Tauri CLI is invoked through pnpm:
cd installer && pnpm tauri --version
```

### Summary Table

| Tool            | Minimum Version | Used For                  |
| --------------- | --------------- | ------------------------- |
| Node.js         | 20+             | TypeScript build, scripts |
| pnpm            | 10+             | Package management        |
| .NET SDK        | 10.0 (preview)  | C# hook compilation       |
| MSVC / GCC      | VS 2022 / GCC 9+| Native profiler DLL/SO   |
| Rust            | stable           | Tauri installer backend  |
| Tauri CLI       | 2.x              | Installer build          |

---

## TypeScript Build

The TypeScript layer is the browser-side injection code: the plugin system,
theme engine, and bridge proxies that run inside Root's embedded Chromium.

### Command

```bash
pnpm build
```

This runs `tsx scripts/build.ts`, which uses **esbuild** to produce the
bundle.

### What It Does

1. **Bundles** `src/core/preload.ts` as the single entry point.
2. **Outputs** `dist/uprooted-preload.js` -- an IIFE (Immediately Invoked
   Function Expression) bundle with `globalName: "Uprooted"`.
3. **Collects CSS** from all built-in plugins under `src/plugins/` by
   recursively walking for `.css` files. The combined result is written to
   `dist/uprooted.css` with source path comments for each file.
4. **Injects the version** from `package.json` as a compile-time constant
   (`__UPROOTED_VERSION__`) via esbuild's `define` option.

### Build Configuration

| Setting          | Value          | Why                                             |
| ---------------- | -------------- | ----------------------------------------------- |
| `format`         | `iife`         | Must execute immediately in a `<script>` tag    |
| `globalName`     | `Uprooted`     | Exposes `window.Uprooted` for the plugin API    |
| `platform`       | `browser`      | Targeting DotNetBrowser's embedded Chromium      |
| `target`         | `chrome120`    | Matches Root's Chromium version                  |
| `sourcemap`      | `true`         | Source maps for debugging in DevTools            |
| `external`       | `node:fs`, etc | Node builtins excluded (only used in CLI scripts)|

### Output Files

```
dist/
  uprooted-preload.js      # ~bundled IIFE, injected via <script> tag
  uprooted-preload.js.map  # Source map
  uprooted.css              # Combined plugin CSS
```

### IIFE Format

The bundle wraps all modules in `var Uprooted = (function() { ... })();`.
This means the code executes the moment the `<script>` tag loads -- no
module loader needed. The C# hook's `HtmlPatchVerifier` inserts a
`<script src="uprooted-preload.js">` tag into Root's HTML files so the
bundle loads on every page.

---

## C# Hook Build

The hook is a .NET class library that gets loaded into Root.exe's CLR at
runtime by the native profiler. It injects Avalonia UI elements (sidebar,
settings pages, theme engine) via reflection.

### Command

```bash
dotnet build hook/ -c Release
```

### Project File

`hook/UprootedHook.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>
</Project>
```

No external NuGet packages -- the hook uses only BCL types and accesses
Avalonia entirely through runtime reflection (see `AvaloniaReflection.cs`).

### Output Location

```
hook/bin/Release/net10.0/
  UprootedHook.dll          # The managed hook assembly
  UprootedHook.deps.json    # Dependency metadata
```

### Debug vs Release

- **Debug** (`-c Debug`): Includes debug symbols, no optimizations. Useful
  during development but larger binary.
- **Release** (`-c Release`): Optimized, smaller. Always use Release for
  the installer pipeline and distribution.

In CI, the `-o hook/_out` flag is used to place output in a flat directory
for easier staging:

```bash
dotnet build hook -c Release -o hook/_out
```

---

## CLR Profiler Build

The native profiler is a C shared library that implements the
`ICorProfilerCallback` COM interface. It is loaded by the CLR via
environment variables and injects IL into a JIT-compiled method to
bootstrap the managed hook.

### Windows (cl.exe)

```bash
cl.exe /LD /O2 ^
  /Fe:uprooted_profiler.dll ^
  tools/uprooted_profiler.c ^
  /link ole32.lib kernel32.lib shell32.lib ^
  /DEF:tools/uprooted_profiler.def
```

| Flag    | Purpose                                          |
| ------- | ------------------------------------------------ |
| `/LD`   | Build as a DLL (shared library)                  |
| `/O2`   | Optimize for speed                               |
| `/Fe:`  | Output file path                                 |
| `/DEF:` | Module definition file for exported symbols      |

The `.def` file (`tools/uprooted_profiler.def`) exports two COM entry
points required by the CLR profiler loading mechanism:

```
LIBRARY uprooted_profiler
EXPORTS
    DllGetClassObject = UprootedDllGetClassObject
    DllCanUnloadNow = UprootedDllCanUnloadNow
```

**Important:** You must run from a Visual Studio Developer Command Prompt
or call `vcvarsall.bat x64` first to set up the MSVC environment. The
`build_installer.ps1` script locates `vcvarsall.bat` automatically via
`vswhere.exe`.

### Linux (gcc)

```bash
gcc -shared -fPIC -O2 \
  -o libuprooted_profiler.so \
  tools/uprooted_profiler_linux.c
```

| Flag       | Purpose                                    |
| ---------- | ------------------------------------------ |
| `-shared`  | Build as a shared object (.so)             |
| `-fPIC`    | Position-independent code (required for .so)|
| `-O2`      | Optimize for speed                         |

The Linux version (`uprooted_profiler_linux.c`) is a separate source file
that uses POSIX APIs instead of Win32, but implements the same IL injection
strategy.

### Output

- **Windows:** `uprooted_profiler.dll` (+ `.lib`, `.exp`, `.obj` intermediates)
- **Linux:** `libuprooted_profiler.so`

---

## Tauri Installer Build

The installer is a Tauri 2 application with a Vite-powered frontend and
a Rust backend. It embeds all Uprooted artifacts directly into the binary
via `include_bytes!()`.

### Frontend (Vite)

The installer UI lives in `installer/` and uses Vite for bundling:

```bash
cd installer && pnpm build
```

`installer/vite.config.ts` targets `chrome120` with esbuild minification
and source maps.

### Backend (Rust)

The Rust backend in `installer/src-tauri/` handles:
- Root installation detection (`detection.rs`)
- HTML patching (`patcher.rs`)
- File deployment and environment variable management (`hook.rs`)
- Settings management (`settings.rs`)

### Artifact Embedding

The key mechanism is in `installer/src-tauri/src/embedded.rs`:

```rust
#[cfg(target_os = "windows")]
pub const PROFILER: &[u8] = include_bytes!("../artifacts/uprooted_profiler.dll");
#[cfg(target_os = "linux")]
pub const PROFILER: &[u8] = include_bytes!("../artifacts/libuprooted_profiler.so");

pub const HOOK_DLL: &[u8] = include_bytes!("../artifacts/UprootedHook.dll");
pub const HOOK_DEPS_JSON: &[u8] = include_bytes!("../artifacts/UprootedHook.deps.json");
pub const PRELOAD_JS: &[u8] = include_bytes!("../artifacts/uprooted-preload.js");
pub const THEME_CSS: &[u8] = include_bytes!("../artifacts/uprooted.css");
```

All five artifacts must be present in `installer/src-tauri/artifacts/`
**before** `cargo build` runs, because `include_bytes!()` is resolved at
compile time. The full pipeline script handles staging them.

### Combined Build

```bash
cd installer && pnpm tauri build
```

This triggers Tauri's build process:
1. Runs `pnpm build` (Vite frontend build, per `beforeBuildCommand` in
   `tauri.conf.json`)
2. Compiles the Rust backend with `cargo build --release`
3. Produces the final executable

### Tauri Configuration

`installer/src-tauri/tauri.conf.json` defines:
- Product name: `Uprooted Installer`
- Window: 680x520, no decorations, transparent, centered
- Frontend dist: `../dist` (Vite output)
- Bundle identifier: `sh.uprooted.installer`

### Output

- **Windows:** `installer/src-tauri/target/release/uprooted-installer.exe`
- **Linux:** `.deb` and `.AppImage` in `installer/src-tauri/target/release/bundle/`

---

## Full Pipeline

The `scripts/build_installer.ps1` PowerShell script orchestrates the
entire build from source to distributable installer.

### Command

```powershell
powershell -File scripts/build_installer.ps1
```

### Step-by-Step

1. **Disable CLR profiling** for the build process (prevents `dotnet build`
   from loading the profiler and locking DLLs).

2. **Create artifacts directory** at `installer/src-tauri/artifacts/`.

3. **Build TypeScript** -- runs `pnpm build` from repo root.
   - Validates that `dist/uprooted-preload.js` and `dist/uprooted.css`
     exist.
   - Copies both to the artifacts directory.

4. **Build C# hook** -- runs `dotnet build hook/ -c Release`.
   - Validates `hook/bin/Release/net10.0/UprootedHook.dll` exists.
   - Copies `UprootedHook.dll` and `UprootedHook.deps.json` to artifacts.

5. **Compile native profiler** -- locates MSVC via `vswhere.exe`, calls
   `vcvarsall.bat x64`, then runs `cl.exe` with the flags described in
   [CLR Profiler Build](#clr-profiler-build).
   - Output goes directly to `installer/src-tauri/artifacts/uprooted_profiler.dll`.

6. **Verify artifacts** -- checks that all five required files exist and
   are non-empty:
   - `uprooted_profiler.dll`
   - `UprootedHook.dll`
   - `UprootedHook.deps.json`
   - `uprooted-preload.js`
   - `uprooted.css`

7. **Build Tauri** -- runs `pnpm tauri build` from `installer/`.
   - Vite builds the frontend.
   - Cargo builds the Rust backend (which embeds the five artifacts via
     `include_bytes!()`).
   - Produces the final `Uprooted Installer.exe`.

### Output

The final installer binary is at:

```
installer/src-tauri/target/release/Uprooted Installer.exe
```

The script reports the file size on success.

---

## Development Workflow

### Watch Mode for TypeScript

During active development on the browser-side code, use watch mode to
rebuild automatically on file changes:

```bash
pnpm dev
```

This runs `tsx scripts/build.ts --watch`, which keeps esbuild's context
alive and triggers incremental rebuilds.

### Quick Rebuild Cycle

For iterating on the C# hook without building the full installer:

```bash
# 1. Rebuild the hook DLL
dotnet build hook/ -c Release

# 2. Deploy to local Root installation
powershell -File scripts/install-hook.ps1
```

### Deploying with install-hook.ps1

`scripts/install-hook.ps1` copies the built artifacts to Root's local
data directory and patches shortcuts and protocol handlers:

1. Copies `uprooted_profiler.dll`, `UprootedHook.dll`, and
   `UprootedLauncher.exe` to `%LOCALAPPDATA%\Root\uprooted\`.
2. Patches Root shortcuts (Start Menu, Desktop, Startup, Taskbar) to
   point to `UprootedLauncher.exe` instead of `Root.exe`.
3. Patches the `rootapp://` protocol handler in the registry.
4. Sets `Enabled=true` in `uprooted-settings.ini`.

**Prerequisites for install-hook.ps1:**
- `tools/uprooted_profiler.dll` (pre-compiled or built via `build_all.cmd`)
- `hook/bin/Release/net10.0/UprootedHook.dll` (from `dotnet build`)
- `tools/UprootedLauncher.exe` (from `tools/build_launcher.cmd`)

### Uninstalling

```powershell
powershell -File scripts/uninstall-hook.ps1
```

This reverses everything `install-hook.ps1` did:
- Restores shortcuts from backup
- Restores the `rootapp://` protocol handler
- Sets `Enabled=false` in settings
- Optionally removes Uprooted files

### Diagnosing Issues

```powershell
powershell -File scripts/diagnose.ps1
```

The diagnostic script inspects the current state of a Root installation:
- Lists all Root-related shortcuts and their targets
- Checks taskbar pins
- Displays the last 20 lines of `velopack.log`
- Reports whether Root is currently running
- Shows CLR profiler environment variables (both process and registry)

### Testing with Root

The typical develop-test loop:

```bash
# 1. Make code changes

# 2. Rebuild what changed
pnpm build                          # If you changed TypeScript
dotnet build hook/ -c Release       # If you changed C# hook

# 3. Deploy to Root
powershell -File scripts/install-hook.ps1

# 4. Launch Root (the install script offers to launch at the end)

# 5. Check logs at %LOCALAPPDATA%\Root\uprooted\uprooted.log
```

### TypeScript Installer Scripts

For lightweight TypeScript-only patching (no native hook), there are
also pnpm scripts that call the patcher module directly:

```bash
pnpm install-root     # Runs scripts/install.ts -> src/core/patcher.js
pnpm uninstall-root   # Runs scripts/uninstall.ts -> reverses patches
```

These handle only the HTML/JS injection layer, not the CLR profiler or
shortcuts.

---

## CI/CD Workflows

### build-installer.yml (Windows)

**Location:** `.github/workflows/build-installer.yml`

**Triggers:**
- Manual dispatch (`workflow_dispatch`)
- Push to paths: `hook/**`, `src/**`, `installer/**`,
  `tools/uprooted_profiler.c`, `tools/uprooted_profiler.def`,
  `tools/uprooted_profiler_linux.c`,
  `.github/workflows/build-installer.yml`,
  `.github/workflows/build-linux.yml`

**Environment:** `windows-latest`

**Toolchains installed:**
- MSVC (via `ilammy/msvc-dev-cmd`)
- .NET 10 Preview
- Node.js 22
- pnpm 10
- Rust stable (with Swatinem cache for `installer/src-tauri`)

**Build steps:**
1. `pnpm install --frozen-lockfile`
2. `pnpm build` (TypeScript)
3. Stage `uprooted-preload.js` + `uprooted.css` to artifacts dir
4. `dotnet build hook -c Release -o hook/_out`
5. Stage `UprootedHook.dll` + `UprootedHook.deps.json`
6. `cl.exe /LD /O2 ...` (profiler DLL)
7. Verify all 5 artifacts present and non-empty
8. `pnpm tauri build` in `installer/`
9. Rename output to `Uprooted-{version}.exe`
10. Upload as GitHub Actions artifact
11. Publish to public repo (`watchthelight/uprooted`) as a draft release

### build-linux.yml

**Location:** `.github/workflows/build-linux.yml`

**Triggers:** Same path-based triggers as the Windows workflow, plus
`packaging/arch/**` and its own workflow file.

**Environment:** `ubuntu-latest`

**Additional system packages:** `libwebkit2gtk-4.1-dev`,
`libappindicator3-dev`, `librsvg2-dev`, `patchelf`, `gcc`

**Build steps:**
1. Same TypeScript and hook build as Windows
2. `gcc -shared -fPIC -O2 -o libuprooted_profiler.so tools/uprooted_profiler_linux.c`
3. Verify 5 artifacts (uses `libuprooted_profiler.so` instead of `.dll`)
4. `pnpm tauri build --bundles deb appimage`
5. Upload `.deb` and `.AppImage` as GitHub Actions artifacts
6. Publish to public repo release

**Arch Linux package (`build-arch` job):**
- Runs after `build-linux` completes
- Downloads the `.deb` artifact
- Generates a `PKGBUILD` from `packaging/arch/PKGBUILD` template
  (substituting version, filename, SHA256)
- Builds a `.pkg.tar.zst` using `arch-pkgbuild-builder`
- Uploads and publishes to the public repo release

---

## Build Artifacts Inventory

| Artifact                   | Source                          | Purpose                                              |
| -------------------------- | ------------------------------- | ---------------------------------------------------- |
| `uprooted-preload.js`     | `pnpm build` (esbuild)         | IIFE bundle injected into Chromium via `<script>` tag |
| `uprooted-preload.js.map` | `pnpm build` (esbuild)         | Source map for browser DevTools debugging             |
| `uprooted.css`            | `pnpm build` (CSS collection)  | Combined CSS from all built-in plugins               |
| `UprootedHook.dll`        | `dotnet build hook/ -c Release` | Managed C# hook loaded by the profiler into Root.exe |
| `UprootedHook.deps.json`  | `dotnet build hook/ -c Release` | .NET dependency metadata for the hook                |
| `uprooted_profiler.dll`   | `cl.exe` (Windows)              | Native CLR profiler that injects IL into Root's JIT  |
| `libuprooted_profiler.so` | `gcc` (Linux)                   | Linux equivalent of the profiler                     |
| `uprooted-installer.exe`  | `pnpm tauri build` (Windows)    | Self-contained Windows installer with all artifacts  |
| `*.deb`                   | `pnpm tauri build` (Linux)      | Debian package for Ubuntu/Debian                     |
| `*.AppImage`              | `pnpm tauri build` (Linux)      | Portable Linux binary                                |
| `*.pkg.tar.zst`           | `arch-pkgbuild-builder` (CI)    | Arch Linux package (repackaged from .deb)            |

### Staging Directory

During the full pipeline, all artifacts destined for embedding are staged
in `installer/src-tauri/artifacts/`. This directory must contain exactly
these five files before the Tauri build:

```
installer/src-tauri/artifacts/
  uprooted_profiler.dll       # (or libuprooted_profiler.so on Linux)
  UprootedHook.dll
  UprootedHook.deps.json
  uprooted-preload.js
  uprooted.css
```

They are compiled into the final binary via Rust's `include_bytes!()` in
`installer/src-tauri/src/embedded.rs`.

See [Architecture](ARCHITECTURE.md) for how these components fit together
at runtime.

---

## Version Management

The version string appears in multiple locations and must be kept in sync.

### Where Versions Live

| Location                                    | Current  | Format     | Used By                              |
| ------------------------------------------- | -------- | ---------- | ------------------------------------ |
| `package.json` (`version`)                  | `0.2.2` | semver     | TypeScript build (`__UPROOTED_VERSION__`) |
| `installer/src-tauri/Cargo.toml` (`version`)| `0.2.2` | semver     | Rust crate version, Tauri build      |
| `installer/src-tauri/tauri.conf.json` (`version`) | `0.2.2` | semver | Installer product version, CI rename |
| `installer/package.json` (`version`)        | `0.2.2` | semver     | Installer frontend package           |
| `hook/UprootedSettings.cs` (`Version`)      | `0.2.2` | string     | Default version in settings INI file |
| `scripts/install-hook.ps1` (settings write) | `0.2.2` | string     | Written to `uprooted-settings.ini`   |

### How Versions Propagate

1. **TypeScript:** `scripts/build.ts` reads `package.json`'s `version` and
   defines `__UPROOTED_VERSION__` at compile time. This becomes available
   as `window.__UPROOTED_VERSION__` in the browser context.

2. **C# hook:** `UprootedSettings.cs` has a hardcoded default
   (`Version = "0.2.2"`) that gets written to `uprooted-settings.ini`.
   The settings page displays this version as a badge via `ContentPages.cs`,
   and the sidebar injector adds it to the version info box.

3. **Installer:** The version in `tauri.conf.json` determines the output
   filename in CI (`Uprooted-{version}.exe`) and the release tag
   (`v{version}`).

### Bumping the Version

When bumping the version, update all six locations listed above. A quick
check:

```bash
# Verify all versions match
grep '"version"' package.json installer/package.json installer/src-tauri/Cargo.toml
grep '"version"' installer/src-tauri/tauri.conf.json
grep 'Version.*=' hook/UprootedSettings.cs
grep 'Version.*=' scripts/install-hook.ps1
```

See [Installation Guide](INSTALLATION.md) for deploying built artifacts
to a Root installation.
