# Installation Guide

Step-by-step instructions for installing, verifying, repairing, and uninstalling Uprooted on Windows and Linux.

> **Related docs:** [Index](INDEX.md) | [Build Guide](BUILD.md) | [Installer Reference](INSTALLER.md) | [Architecture](ARCHITECTURE.md)

---

## Table of Contents

1. [Prerequisites](#prerequisites)
2. [Quick Install (Tauri Installer)](#quick-install-tauri-installer)
3. [PowerShell Install (Manual Windows)](#powershell-install-manual-windows)
4. [Linux Install](#linux-install)
5. [Arch Linux](#arch-linux)
6. [Verification Steps](#verification-steps)
7. [Uninstallation](#uninstallation)
8. [Repair (After Root Updates)](#repair-after-root-updates)
9. [Troubleshooting](#troubleshooting)
10. [File Locations Table](#file-locations-table)

---

## Prerequisites

Before installing Uprooted, make sure you have the following:

- **Operating system:** Windows 10 or later, or a Linux distribution with a graphical desktop
- **Root Communications desktop app** installed and **logged in at least once** --
  Uprooted patches HTML files inside Root's profile directory, which is only created
  after the first successful login.
- **.NET 10 SDK** -- only required if you are building from source. The prebuilt
  installer bundles all necessary artifacts. See [Build Guide](BUILD.md) for details.

**Important:** Uprooted does **not** require administrator or root privileges.
On Windows, all environment variables are set at user scope (`HKCU\Environment`).
On Linux, everything lives under `~/.local/` and `~/.config/`.

---

## Quick Install (Tauri Installer)

This is the recommended method for most users. The Tauri-based GUI installer handles
detection, file deployment, environment variable configuration, and HTML patching
automatically.

### Steps

1. **Download the installer** from the
   [latest release](https://github.com/watchthelight/uprooted/releases/latest).
   - Windows: `.msi` or `.exe` installer
   - Linux: `.deb` or `.AppImage` installer

2. **Close Root** if it is currently running. The installer will warn you if Root is
   still open.

3. **Run the installer.** It will:
   - Auto-detect Root's installation path
     - Windows: `%LOCALAPPDATA%\Root\current\Root.exe`
     - Linux: checks `~/Applications/Root.AppImage`, `~/Downloads/Root.AppImage`,
       `~/.local/bin/Root.AppImage`, `/opt/Root.AppImage`, and `PATH`
   - Deploy five files to the Uprooted install directory (see
     [File Locations Table](#file-locations-table)):
     - `uprooted_profiler.dll` (Windows) or `libuprooted_profiler.so` (Linux)
     - `UprootedHook.dll`
     - `UprootedHook.deps.json`
     - `uprooted-preload.js`
     - `uprooted.css`
   - Set CLR profiler environment variables (user-scoped)
   - Patch Root's HTML files with `<script>` and `<link>` tags for the TypeScript layer

4. **Click Install** and wait for the process to complete.

5. **Restart Root.** Open Root normally -- Uprooted will load automatically via
   the CLR profiler hook.

6. **Verify:** Open Root's Settings page. You should see an "UPROOTED" section in the
   sidebar. See [Verification Steps](#verification-steps) for more details.

---

## PowerShell Install (Manual Windows)

Use this method if you prefer command-line installation, want to build from source,
or need to choose between injection methods.

### Usage

```powershell
# Default: Profiler method (recommended)
.\Install-Uprooted.ps1

# Alternative: Startup Hooks method
.\Install-Uprooted.ps1 -Method StartupHooks
```

### Injection Methods

Uprooted supports two injection methods. Both accomplish the same result -- loading
`UprootedHook.dll` into the Root process -- but they work differently:

**Profiler method (default):**

- Uses a native CLR profiler DLL (`uprooted_profiler.dll`) for IL injection.
- The profiler injects IL that calls `Assembly.LoadFrom` + `Assembly.CreateInstance`
  to bootstrap the hook.
- Does **not** patch `Root.exe` -- no binary modification needed.
- Survives Root updates without re-patching the executable.
- Downside: the profiler environment variables are visible to all .NET applications
  on your system. The profiler DLL has a built-in process guard that only activates
  for `Root.exe`, so other apps are unaffected in practice.

**Startup Hooks method:**

- Uses the standard .NET `DOTNET_STARTUP_HOOKS` environment variable.
- Requires patching `Root.exe` to flip the embedded
  `System.StartupHookProvider.IsSupported` flag from `false` to `true` (Root ships
  with startup hooks disabled).
- Cleaner environment -- only one env var (`DOTNET_STARTUP_HOOKS`) is set.
- Downside: Root updates overwrite `Root.exe`, so you must re-run the installer
  after each update.

### What the Script Does (Step by Step)

1. **Verifies Root.exe exists** at `%LOCALAPPDATA%\Root\current\Root.exe`.

2. **Checks if Root is running.** If so, prompts to close it.

3. **Method-specific setup:**
   - **Profiler:** Verifies `tools\uprooted_profiler.dll` exists in the repo.
   - **StartupHooks:** Binary-patches `Root.exe` to enable startup hooks. Creates
     a backup at `Root.exe.uprooted.bak` before patching. If the exe is already
     patched, it skips this step.

4. **Builds UprootedHook.dll** by running `dotnet build hook/ -c Release`.
   Requires .NET 10 SDK.

5. **Copies files** to `%LOCALAPPDATA%\Root\uprooted\`:
   - `UprootedHook.dll`
   - `UprootedHook.deps.json`
   - `uprooted_profiler.dll` (Profiler method only)

6. **Sets persistent user-scoped environment variables:**

   **Profiler method sets:**
   ```
   CORECLR_ENABLE_PROFILING = 1
   CORECLR_PROFILER          = {D1A6F5A0-1234-4567-89AB-CDEF01234567}
   CORECLR_PROFILER_PATH     = %LOCALAPPDATA%\Root\uprooted\uprooted_profiler.dll
   DOTNET_ReadyToRun          = 0
   ```

   **StartupHooks method sets:**
   ```
   DOTNET_STARTUP_HOOKS = %LOCALAPPDATA%\Root\uprooted\UprootedHook.dll
   ```

   Each method cleans up environment variables from the other method to prevent
   conflicts.

7. **Done.** Launch Root to activate Uprooted.

### Note on Environment Variable Scope

The Profiler method sets `CORECLR_ENABLE_PROFILING=1` at user scope, which means
**every .NET application** you run will attempt to load the profiler DLL. The
profiler itself has a process guard (`Root.exe` only) and will immediately detach
from any non-Root process. This is harmless but worth knowing.

If this concerns you, use the StartupHooks method instead, which only sets
`DOTNET_STARTUP_HOOKS`. See [Troubleshooting](#troubleshooting) for more details.

---

## Linux Install

### Usage

```bash
# Auto-detect Root.AppImage location
./install-uprooted-linux.sh

# Specify Root path manually
./install-uprooted-linux.sh --root-path /path/to/Root.AppImage
```

### Build Dependencies

The Linux script builds from source, so you need:

- `gcc` -- to compile the native profiler shared library
- `dotnet` -- .NET 10 SDK to build the hook DLL
- `node` -- Node.js for the TypeScript build
- `pnpm` -- package manager for the TypeScript layer

On Ubuntu/Debian:
```bash
sudo apt install gcc nodejs
# Install .NET 10: https://dotnet.microsoft.com/download
# Install pnpm: npm install -g pnpm
```

### What the Script Does (Step by Step)

1. **Finds Root.AppImage.** Searches these locations in order:
   - `~/Applications/Root.AppImage`
   - `~/Downloads/Root.AppImage`
   - `~/.local/bin/Root.AppImage`
   - `/opt/Root.AppImage`
   - `~/.local/bin/Root`
   - `Root` in `$PATH`

   Use `--root-path` to override auto-detection.

2. **Checks build prerequisites** (`gcc`, `dotnet`, `node`, `pnpm`).

3. **Builds all artifacts from source:**
   - TypeScript layer: `pnpm install && pnpm build`
   - Hook DLL: `dotnet build hook/ -c Release`
   - Profiler shared library: `gcc -shared -fPIC -O2 -o libuprooted_profiler.so tools/uprooted_profiler_linux.c`

4. **Deploys files** to `~/.local/share/uprooted/`:
   - `libuprooted_profiler.so` (with execute permission)
   - `UprootedHook.dll`
   - `UprootedHook.deps.json`
   - `uprooted-preload.js`
   - `uprooted.css`

5. **Sets session-wide environment variables** by writing
   `~/.config/environment.d/uprooted.conf`:
   ```ini
   CORECLR_ENABLE_PROFILING=1
   CORECLR_PROFILER={D1A6F5A0-1234-4567-89AB-CDEF01234567}
   CORECLR_PROFILER_PATH=~/.local/share/uprooted/libuprooted_profiler.so
   DOTNET_ReadyToRun=0
   ```
   These take effect after your next login. The wrapper script (below) provides
   immediate use without re-logging.

6. **Creates a wrapper script** at `~/.local/share/uprooted/launch-root.sh` that
   exports the profiler environment variables and then executes Root. This is
   useful for immediate testing without logging out and back in.

7. **Creates a `.desktop` file** at `~/.local/share/applications/root-uprooted.desktop`
   so you can find "Root (Uprooted)" in your application menu.

8. **Patches HTML files** in Root's profile directory
   (`~/.local/share/Root Communications/Root/profile/default/`):
   - Looks for `index.html` in `WebRtcBundle/` and `RootApps/*/`
   - Injects `<script>` and `<link>` tags inside `<!-- uprooted:start -->` /
     `<!-- uprooted:end -->` markers before the `</head>` tag
   - Creates `.uprooted.bak` backup of each original file

### How to Launch After Install

You have three options:

1. **Log out and back in**, then launch Root normally (environment variables will
   be loaded by systemd `environment.d`).
2. **Run the wrapper script** directly:
   ```bash
   ~/.local/share/uprooted/launch-root.sh
   ```
3. **Use the application menu** entry "Root (Uprooted)".

---

## Arch Linux

An AUR-style PKGBUILD is available at `packaging/arch/PKGBUILD` in the repository.
This package installs the Tauri GUI installer from the `.deb` release artifact.

The PKGBUILD depends on:
- `cairo`, `desktop-file-utils`, `gdk-pixbuf2`, `glib2`, `gtk3`
- `hicolor-icon-theme`, `libsoup3`, `pango`, `webkit2gtk-4.1`

The version and checksum placeholders (`%%VERSION%%`, `%%SHA256%%`, `%%DEB_NAME%%`)
are filled in by the release CI pipeline. To build locally:

```bash
cd packaging/arch/
# Edit PKGBUILD to replace %%VERSION%%, %%DEB_NAME%%, and %%SHA256%% with actual values
makepkg -si
```

A proper AUR submission (`uprooted-bin`) is planned for a future release. For now,
use the standalone Linux install script or the Tauri installer `.AppImage`.

---

## Verification Steps

After installation, follow these steps to confirm Uprooted is working correctly.

### 1. Check the Settings Sidebar

1. Open Root Communications.
2. Navigate to **Settings** (gear icon).
3. Look for the **UPROOTED** section heading in the left sidebar, below Root's
   built-in settings categories.
4. You should see navigation items such as "Uprooted", "Themes", and "Plugins"
   under the UPROOTED heading.

If the sidebar items appear, both the C# hook layer and the sidebar injector are
working.

### 2. Check the Log File

Uprooted writes detailed logs to:
- **Windows:** `%LOCALAPPDATA%\Root Communications\Root\profile\default\uprooted-hook.log`
- **Linux:** `~/.local/share/Root Communications/Root/profile/default/uprooted-hook.log`

Open the log file after launching Root. A successful startup looks like this:

```
[HH:MM:SS.fff] [Startup] ========================================
[HH:MM:SS.fff] [Startup] === Uprooted Hook v0.2.2 Loaded ===
[HH:MM:SS.fff] [Startup] ========================================
[HH:MM:SS.fff] [Startup] Process: C:\Users\...\Root.exe
[HH:MM:SS.fff] [Startup] PID: 12345
[HH:MM:SS.fff] [Startup] .NET: 10.0.0
[HH:MM:SS.fff] [Startup] Phase 0: Verifying HTML patches...
[HH:MM:SS.fff] [HtmlPatch] Checking 2 HTML file(s) for patches
[HH:MM:SS.fff] [HtmlPatch] OK: WebRtcBundle/index.html
[HH:MM:SS.fff] [HtmlPatch] OK: RootApps/.../index.html
[HH:MM:SS.fff] [Startup] Phase 0 OK: 0 file(s) repaired
[HH:MM:SS.fff] [Startup] Phase 1: Waiting for Avalonia assemblies...
[HH:MM:SS.fff] [Startup] Phase 1 OK: Avalonia assemblies loaded
[HH:MM:SS.fff] [Startup] Phase 2: Waiting for Application.Current...
[HH:MM:SS.fff] [Startup] Phase 2 OK: Application.Current is set
[HH:MM:SS.fff] [Startup] Phase 3: Waiting for MainWindow...
[HH:MM:SS.fff] [Startup] Phase 3 OK: MainWindow = ...
[HH:MM:SS.fff] [Startup] Phase 3.5: Initializing theme engine
[HH:MM:SS.fff] [Startup] Phase 4: Starting settings page monitor
[HH:MM:SS.fff] [Startup] ========================================
[HH:MM:SS.fff] [Startup] === Uprooted Hook Ready ===
[HH:MM:SS.fff] [Startup] ========================================
```

All five phases should complete. If any phase shows "FAILED", see
[Troubleshooting](#troubleshooting).

### 3. Check TypeScript Features

Open Root's developer tools (if accessible) or look for Uprooted-specific UI
changes (theme overrides, plugin effects). If the C# hook is working but themes
and plugins are not, the HTML files may not be patched. Check the log for
`[HtmlPatch]` messages and see
[Missing TypeScript features](#missing-typescript-features) in Troubleshooting.

---

## Uninstallation

### Tauri Installer

If you installed via the Tauri GUI installer:

1. Open the installer application.
2. Click **Uninstall**.
3. The installer will:
   - Remove all deployed files from the Uprooted install directory
   - Remove all environment variables (profiler and startup hooks)
   - Strip injected tags from HTML files (or restore from backups)
   - On Linux: remove the wrapper script, `.desktop` file, and
     `environment.d/uprooted.conf`
4. Restart Root to confirm it runs without Uprooted.

### PowerShell (Windows)

Run the uninstall script:

```powershell
.\Uninstall-Uprooted.ps1
```

The script performs these steps:

1. **Prompts to close Root** if it is running.

2. **Removes all environment variables** (user-scoped):
   - `CORECLR_ENABLE_PROFILING`
   - `CORECLR_PROFILER`
   - `CORECLR_PROFILER_PATH`
   - `DOTNET_ReadyToRun`
   - `DOTNET_STARTUP_HOOKS`

3. **Restores Root.exe from backup** if it was binary-patched (StartupHooks method).
   Deletes the `.uprooted.bak` backup file.

4. **Deletes the install directory** at `%LOCALAPPDATA%\Root\uprooted\`.

5. **Optionally removes log and settings files:**
   - `uprooted-hook.log`
   - `uprooted-settings.json`

   The script will ask before deleting these. Choose "n" to keep your logs for
   debugging or your settings for a future reinstall.

### Linux

Run the uninstall script:

```bash
./uninstall-uprooted-linux.sh
```

The script performs these steps:

1. **Checks if Root is running** and exits if so (close Root first).

2. **Restores HTML files.** Looks for `.uprooted-backup` files and restores them.
   If no backups are found, strips Uprooted-injected lines (script tags, CSS links)
   from `index.html` files directly.

3. **Removes `~/.config/environment.d/uprooted.conf`** (session-wide env vars).

4. **Removes `~/.local/share/applications/root-uprooted.desktop`** (menu entry).

5. **Removes `~/.local/share/uprooted/`** (all deployed files, including the
   wrapper script).

### Manual Cleanup

If the uninstall scripts are not available or something was missed, here is
everything Uprooted touches:

**Windows:**
```
# Files
%LOCALAPPDATA%\Root\uprooted\                              (entire directory)

# Environment variables (user-scoped, HKCU\Environment)
CORECLR_ENABLE_PROFILING
CORECLR_PROFILER
CORECLR_PROFILER_PATH
DOTNET_ReadyToRun
DOTNET_STARTUP_HOOKS

# Optional data files
%LOCALAPPDATA%\Root Communications\Root\profile\default\uprooted-hook.log
%LOCALAPPDATA%\Root Communications\Root\profile\default\uprooted-settings.json

# Root.exe backup (if StartupHooks method was used)
%LOCALAPPDATA%\Root\current\Root.exe.uprooted.bak
```

To remove Windows environment variables manually:
```powershell
[System.Environment]::SetEnvironmentVariable("CORECLR_ENABLE_PROFILING", $null, "User")
[System.Environment]::SetEnvironmentVariable("CORECLR_PROFILER", $null, "User")
[System.Environment]::SetEnvironmentVariable("CORECLR_PROFILER_PATH", $null, "User")
[System.Environment]::SetEnvironmentVariable("DOTNET_ReadyToRun", $null, "User")
[System.Environment]::SetEnvironmentVariable("DOTNET_STARTUP_HOOKS", $null, "User")
```

**Linux:**
```
# Files
~/.local/share/uprooted/                                   (entire directory)
~/.config/environment.d/uprooted.conf                      (env vars)
~/.local/share/applications/root-uprooted.desktop           (menu entry)

# Optional data files
~/.local/share/Root Communications/Root/profile/default/uprooted-hook.log
~/.local/share/Root Communications/Root/profile/default/uprooted-settings.json
```

**Both platforms -- HTML file cleanup:**

If patched HTML files remain, they contain injected content between
`<!-- uprooted:start -->` and `<!-- uprooted:end -->` markers. You can:

- Delete the markers and everything between them manually, or
- Delete the `.uprooted.bak` backup files and let Root regenerate fresh HTML on
  the next launch, or
- Re-run the install script and then the uninstall script to clean up properly

---

## Repair (After Root Updates)

When Root Communications updates, it may overwrite its HTML files, removing the
Uprooted `<script>` and `<link>` tags. The C# hook itself is unaffected (it lives
outside Root's install directory), but the TypeScript layer will not load until the
HTML patches are restored.

### Automatic Repair

Uprooted has a built-in self-healing mechanism:

- **Phase 0 (startup):** Every time Root launches, the hook verifies all HTML files
  still contain the Uprooted markers. If any file is missing patches, it repairs
  them automatically before Avalonia even starts.

- **FileSystemWatcher (runtime):** After startup, the hook monitors `WebRtcBundle/`
  and `RootApps/*/` directories for changes to `index.html`. If Root overwrites an
  HTML file at runtime, the watcher detects it and re-patches within seconds.

This means in most cases, you do not need to do anything after a Root update.
Just restart Root and the hook will repair the patches itself.

### Manual Repair

If automatic repair does not trigger (e.g. the hook DLL was also updated), you have
these options:

**Tauri installer:**
1. Open the installer.
2. Click **Repair**. This strips any old injection, re-deploys files, and re-patches
   HTML fresh.

**PowerShell (Windows):**
```powershell
# Re-run the install script (it is idempotent)
.\Install-Uprooted.ps1
```

**Linux:**
```bash
# Re-run the install script
./install-uprooted-linux.sh
```

### StartupHooks Method: Root.exe Re-patching

If you used the StartupHooks injection method, a Root update will also overwrite
`Root.exe`, disabling startup hooks again. Re-run the installer to re-patch:

```powershell
.\Install-Uprooted.ps1 -Method StartupHooks
```

The Profiler method does not have this problem because it does not modify `Root.exe`.

---

## Troubleshooting

### Root freezes on startup

**Cause:** The `ContentControl.Content` property was directly modified, which
triggers a layout cycle freeze in Avalonia.

**Fix:** This should not happen with the current version of Uprooted (it uses a Grid
overlay instead of modifying Content). If it does occur:

1. Kill Root from Task Manager or `pkill Root`.
2. Uninstall Uprooted to remove the hook.
3. Report the issue with your `uprooted-hook.log` file.

### Missing UPROOTED sidebar in Settings

**Possible causes:**

- **Avalonia version mismatch:** If Root updated to a new Avalonia version, the
  reflection cache may fail to resolve types. Check the log for:
  ```
  [Startup] Type resolution failed, aborting
  ```
  or
  ```
  [Startup] Phase 1 FAILED: Avalonia assemblies not found after 30s
  ```

- **Hook not loading:** Verify environment variables are set:
  ```powershell
  # Windows (PowerShell)
  [System.Environment]::GetEnvironmentVariable("CORECLR_ENABLE_PROFILING", "User")
  # Should output: 1
  ```
  ```bash
  # Linux
  cat ~/.config/environment.d/uprooted.conf
  ```

- **Wrong profiler path:** The `CORECLR_PROFILER_PATH` must point to the actual
  profiler DLL/SO file. Verify the file exists at the path shown in the environment
  variable.

### Missing TypeScript features

Themes, plugins, and other browser-side features require the HTML files to be
patched with Uprooted's `<script>` and `<link>` tags.

**Check if HTML files are patched:**

Open one of the target HTML files and look for `<!-- uprooted:start -->`:
- Windows: `%LOCALAPPDATA%\Root Communications\Root\profile\default\WebRtcBundle\index.html`
- Linux: `~/.local/share/Root Communications/Root/profile/default/WebRtcBundle/index.html`

If the markers are missing:
1. Make sure Root has been launched at least once (to create the profile directory).
2. Re-run the install script or use the Tauri installer's Repair button.
3. Check the log for `[HtmlPatch]` errors.

### Environment variable leakage

The Profiler method sets `CORECLR_ENABLE_PROFILING=1` at user scope. This means
every .NET application you launch will attempt to load the profiler. The profiler
has a process guard that immediately detaches from any process that is not
`Root.exe`, so there is no functional impact.

However, if this concerns you:

- **Switch to the StartupHooks method** (`.\Install-Uprooted.ps1 -Method StartupHooks`),
  which only sets `DOTNET_STARTUP_HOOKS` and does not trigger profiler loading in
  other apps.
- **On Linux, use the wrapper script** (`~/.local/share/uprooted/launch-root.sh`)
  instead of session-wide env vars, and remove `~/.config/environment.d/uprooted.conf`.

### Hook loads but no log file

The log file is written to the Root profile directory. If the directory does not
exist, the logger creates it. If the log file is still missing:

- Verify the profile directory exists (launch Root once first).
- Check file permissions on the profile directory.
- On Linux, make sure the environment variables are actually set in your session
  (`env | grep CORECLR`).

### Log file location

The log file is at:
- **Windows:** `%LOCALAPPDATA%\Root Communications\Root\profile\default\uprooted-hook.log`
- **Linux:** `~/.local/share/Root Communications/Root/profile/default/uprooted-hook.log`

The log uses the format `[HH:MM:SS.fff] [Category] Message`. Key categories:

| Category | What it covers |
|----------|----------------|
| `Startup` | Hook initialization, startup phases 0--4 |
| `HtmlPatch` | HTML file verification, patching, and FileSystemWatcher events |
| `Injector` | Sidebar injection into the Avalonia settings page |
| `Entry` | Profiler entry point (ModuleInitializer or constructor) |
| `Recon` | Style reconnaissance for matching native Avalonia UI |

---

## File Locations Table

All paths Uprooted uses on each platform.

| File | Windows | Linux |
|------|---------|-------|
| **Hook DLL** | `%LOCALAPPDATA%\Root\uprooted\UprootedHook.dll` | `~/.local/share/uprooted/UprootedHook.dll` |
| **Hook deps** | `%LOCALAPPDATA%\Root\uprooted\UprootedHook.deps.json` | `~/.local/share/uprooted/UprootedHook.deps.json` |
| **Profiler DLL/SO** | `%LOCALAPPDATA%\Root\uprooted\uprooted_profiler.dll` | `~/.local/share/uprooted/libuprooted_profiler.so` |
| **TypeScript bundle** | `%LOCALAPPDATA%\Root\uprooted\uprooted-preload.js` | `~/.local/share/uprooted/uprooted-preload.js` |
| **Theme CSS** | `%LOCALAPPDATA%\Root\uprooted\uprooted.css` | `~/.local/share/uprooted/uprooted.css` |
| **Settings file** | `%LOCALAPPDATA%\Root Communications\Root\profile\default\uprooted-settings.json` | `~/.local/share/Root Communications/Root/profile/default/uprooted-settings.json` |
| **Log file** | `%LOCALAPPDATA%\Root Communications\Root\profile\default\uprooted-hook.log` | `~/.local/share/Root Communications/Root/profile/default/uprooted-hook.log` |
| **Root HTML files** | `%LOCALAPPDATA%\Root Communications\Root\profile\default\WebRtcBundle\index.html` and `RootApps\*\index.html` | `~/.local/share/Root Communications/Root/profile/default/WebRtcBundle/index.html` and `RootApps/*/index.html` |
| **Root executable** | `%LOCALAPPDATA%\Root\current\Root.exe` | `~/Applications/Root.AppImage` (varies) |
| **Env var config** | User registry (`HKCU\Environment`) | `~/.config/environment.d/uprooted.conf` |
| **Wrapper script** | N/A | `~/.local/share/uprooted/launch-root.sh` |
| **Desktop entry** | N/A | `~/.local/share/applications/root-uprooted.desktop` |
| **Root.exe backup** | `%LOCALAPPDATA%\Root\current\Root.exe.uprooted.bak` (StartupHooks only) | N/A |

---

See [Installer Reference](INSTALLER.md) for technical details on how the Tauri
installer works internally. See [Build Guide](BUILD.md) for building all layers
from source.
