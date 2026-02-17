# Installer Reference

> **Related docs:**
> [Architecture](ARCHITECTURE.md) |
> [Installation Guide](INSTALLATION.md) |
> [Build Guide](BUILD.md) |
> [Hook Reference](HOOK_REFERENCE.md)

---

## Overview

The Uprooted Installer is a Tauri 2 desktop application that automates every aspect of
Uprooted lifecycle management: detection of the Root Communications installation, full
install/uninstall/repair of the mod framework, and theme configuration. It ships as a
single executable with all required artifacts compiled directly into the binary via
`include_bytes!()`, meaning there are no external downloads, no temporary extractors,
and no network dependencies at runtime.

**Stack:**

| Layer    | Technology                        | Location                         |
|----------|-----------------------------------|----------------------------------|
| Backend  | Rust (Tauri 2 commands)           | `installer/src-tauri/src/`       |
| Frontend | TypeScript + vanilla DOM          | `installer/src/`                 |
| Bundler  | Vite 6                            | `installer/vite.config.ts`       |
| Build    | `tauri-build` + `cargo tauri`     | `installer/src-tauri/build.rs`   |

The window is 680x520, non-resizable, borderless (custom titlebar), transparent, and
centered on screen. The app identifier is `sh.uprooted.installer`.

---

## Five Operations

The installer exposes five high-level operations, each composed from lower-level
subsystems described in the sections that follow.

### 1. Detect

Scan the system for a Root Communications installation. Checks the executable path,
profile directory, HTML target files, deployed hook files, and environment variable
state. Returns a comprehensive `DetectionResult` with per-component status. This runs
automatically on page load and again after every install/uninstall/repair action.

### 2. Install

Full installation sequence, executed in three ordered steps:

1. **Deploy files** -- extract all embedded artifacts to the uprooted directory.
2. **Set environment variables** -- write CLR profiler env vars to the registry
   (Windows) or `environment.d` + wrapper script (Linux).
3. **Patch HTML** -- inject `<script>` and `<link>` tags into Root's HTML files.

If any step fails, the operation halts and returns a `PatchResult` with the error
message and the list of files patched so far.

See `main.rs:38-59`.

### 3. Uninstall

Reverse the installation in the opposite order:

1. **Remove environment variables** -- delete CLR profiler env vars.
2. **Restore HTML** -- strip injected tags (in-place stripping preferred; backup
   restore as fallback).
3. **Remove files** -- delete the entire uprooted directory.

See `main.rs:62-85`.

### 4. Repair

Fix a broken or partial installation without a full uninstall/reinstall cycle:

1. **Re-deploy files** -- overwrite all artifacts (fixes corrupted or missing files).
2. **Re-set environment variables** -- rewrite all env vars.
3. **Re-patch HTML** -- strip existing patches, then re-inject fresh ones.

This is the recommended action when the installer detects a partial state (e.g., hook
files present but HTML not patched, or env vars missing). See `main.rs:88-109`.

### 5. Configure

Manage settings and themes from the installer without launching Root. The installer can
read/write `uprooted-settings.json` in the profile directory and apply theme selections
by updating the `plugins.themes.config.theme` key. See `main.rs:112-139`.

---

## Rust Backend

### Source: `installer/src-tauri/src/main.rs`

The `main()` function (`main.rs:156-176`) initializes the Tauri application with the
`tauri-plugin-shell` plugin and registers all 13 Tauri commands via
`tauri::generate_handler![]`.

The `#![cfg_attr(not(debug_assertions), windows_subsystem = "windows")]` attribute at
line 2 suppresses the console window in release builds on Windows.

### Tauri Commands

All commands are synchronous Rust functions exposed to the frontend via `#[tauri::command]`.
The frontend invokes them through the Tauri IPC bridge (see TypeScript Frontend section).

| #  | Command               | Signature                                     | Description                                                |
|----|-----------------------|-----------------------------------------------|------------------------------------------------------------|
| 1  | `detect_root`         | `() -> DetectionResult`                       | Full system scan: Root exe, profile, HTML files, hook status, env vars. |
| 2  | `check_hook_status`   | `() -> HookStatus`                            | Per-file and per-env-var deployment status check.          |
| 3  | `check_root_running`  | `() -> bool`                                  | Returns `true` if any Root process is currently running.   |
| 4  | `kill_root`           | `() -> u32`                                   | Terminates all Root processes. Returns count of killed processes. |
| 5  | `install_uprooted`    | `() -> PatchResult`                           | Three-step install: deploy files, set env vars, patch HTML. |
| 6  | `uninstall_uprooted`  | `() -> PatchResult`                           | Three-step uninstall: remove env vars, restore HTML, remove files. |
| 7  | `repair_uprooted`     | `() -> PatchResult`                           | Re-deploy files, re-set env vars, strip and re-patch HTML. |
| 8  | `load_settings`       | `() -> UprootedSettings`                      | Read settings from `uprooted-settings.json` (or return defaults). |
| 9  | `save_settings`       | `(settings: UprootedSettings) -> Result<(), String>` | Write settings to disk as pretty-printed JSON.     |
| 10 | `list_themes`         | `() -> Vec<ThemeDefinition>`                  | Return all built-in themes (parsed from embedded `themes.json`). |
| 11 | `apply_theme`         | `(name: String) -> Result<(), String>`        | Set the active theme in settings and persist to disk.      |
| 12 | `get_uprooted_version`| `() -> String`                                | Return the installer version from `CARGO_PKG_VERSION`.     |
| 13 | `open_profile_dir`    | `() -> Result<(), String>`                    | Open the Root profile directory in the system file manager. |

### Data Structures

Three key structs are serialized across the IPC boundary:

- **`DetectionResult`** (`detection.rs:8-16`) -- `root_found`, `root_path`,
  `profile_dir`, `html_files`, `is_installed`, and a nested `hook_status`.
- **`HookStatus`** (`hook.rs:22-37`) -- per-file booleans (`profiler_dll`, `hook_dll`,
  `hook_deps`, `preload_js`, `theme_css`), per-env-var booleans
  (`env_enable_profiling`, `env_profiler_guid`, `env_profiler_path`,
  `env_ready_to_run`), and two aggregate flags (`files_ok`, `env_ok`).
- **`PatchResult`** (`patcher.rs:14-19`) -- `success`, `message`, `files_patched`.

---

## Detection

### Source: `installer/src-tauri/src/detection.rs`

The detection module locates the Root Communications installation and determines the
current state of Uprooted deployment.

### Root Executable Path

**Windows** (`detection.rs:19-26`):
```
%LOCALAPPDATA%\Root\current\Root.exe
```
The installer checks if this single path exists.

**Linux** (`detection.rs:45-68`):
Searches an ordered list of candidate paths for the AppImage or extracted binary:
1. `~/Applications/Root.AppImage`
2. `~/Downloads/Root.AppImage`
3. `~/.local/bin/Root.AppImage`
4. `/opt/Root.AppImage`
5. `/usr/bin/Root.AppImage`
6. `~/.local/bin/Root` (extracted AppImage fallback)

Returns the first path that exists, or falls back to `~/Applications/Root.AppImage`.

### Profile Directory

**Windows** (`detection.rs:19-26`):
```
%LOCALAPPDATA%\Root Communications\Root\profile\default
```

**Linux** (`detection.rs:29-33`):
```
~/.local/share/Root Communications/Root/profile/default
```

### HTML Target Discovery

`find_target_html_files()` (`detection.rs:70-96`) scans the profile directory for
patchable HTML files in two locations:

1. `profile/default/WebRtcBundle/index.html` -- the main WebRTC UI bundle.
2. `profile/default/RootApps/*/index.html` -- every sub-app directory under RootApps.

Only files that actually exist on disk are returned.

### Installation State

`check_is_installed()` (`detection.rs:98-107`) reads each discovered HTML file and
checks whether it contains any uprooted injection markers (delegating to
`patcher::is_patched()`). Returns `true` if at least one file is patched.

### Full Detection Flow

`detect()` (`detection.rs:109-127`) composes all the above:
1. Resolve Root executable path.
2. Resolve profile directory.
3. Discover HTML targets.
4. Check if any HTML file is patched.
5. Check hook file and env var status.
6. Return the complete `DetectionResult`.

---

## HTML Patcher

### Source: `installer/src-tauri/src/patcher.rs`

The patcher injects Uprooted's `<script>` and `<link>` tags into Root's HTML files
so that the TypeScript layer loads when Root renders its Chromium webviews.

### Markers and Injected Content

The patcher delimits injected content with HTML comment markers (`patcher.rs:8-12`):
`MARKER_START` = `<!-- uprooted:start -->`, `MARKER_END` = `<!-- uprooted:end -->`.
A `LEGACY_MARKER` (`<!-- uprooted -->`) is recognized for detection of older installs
from the bash installer. Backups use the suffix `.uprooted.bak`.

Three elements are injected before the `</head>` tag (`patcher.rs:43-50`):

```html
<!-- uprooted:start -->
    <script>window.__UPROOTED_SETTINGS__={...};</script>
    <script src="file:///path/to/uprooted-preload.js"></script>
    <link rel="stylesheet" href="file:///path/to/uprooted.css">
<!-- uprooted:end -->
```

1. **Settings inline script** -- serializes `UprootedSettings` as JSON into a global
   variable so the TypeScript preload can read settings synchronously on load.
2. **Preload script** -- loads the main TypeScript bundle (plugin system, theme engine,
   bridge proxies) via `file:///` URL pointing to the deployed artifact.
3. **Theme stylesheet** -- loads CSS variables and base styles. Paths use forward
   slashes even on Windows (`patcher.rs:33-37`).

### Patch Detection

`is_patched()` (`patcher.rs:22-26`) returns `true` if the content contains any of:
- `MARKER_START` (`<!-- uprooted:start -->`)
- `LEGACY_MARKER` (`<!-- uprooted -->`)
- The string `uprooted-preload` (catches bare script tags from the bash installer)

### Install Flow

`install()` (`patcher.rs:28-109`):

1. Resolve script and CSS paths from the uprooted directory (forward-slash normalized).
2. Load current settings and serialize to JSON.
3. Build the injection string with markers + script + link tags.
4. Find all target HTML files via `detection::find_target_html_files()`.
5. For each file:
   a. Skip if already patched.
   b. Create a `.uprooted.bak` backup (only if one does not already exist).
   c. Inject the block before `</head>` via string replacement.
   d. Write the modified content back to disk.
6. Return `PatchResult` with the list of patched files.

### Uninstall Flow

`uninstall()` (`patcher.rs:111-161`):

1. Find all target HTML files.
2. For each patched file:
   a. **Preferred:** Strip the injection in-place using `strip_injection()`. This
      preserves Root's current HTML (important if Root has auto-updated since install).
      Delete the backup file after successful stripping.
   b. **Fallback:** If stripping produced no change (edge case), restore from the
      `.uprooted.bak` backup file, then delete the backup.

### Strip Injection

`strip_injection()` (`patcher.rs:165-201`) performs line-by-line filtering to remove
all traces of Uprooted injection:

- Lines between `MARKER_START` and `MARKER_END` (inclusive) are dropped.
- Lines containing `LEGACY_MARKER` are dropped.
- Bare `uprooted-preload` script tags (from the bash installer, which did not use
  markers) are dropped.
- Bare `uprooted.css` link tags are dropped.
- Bare `__UPROOTED_SETTINGS__` inline scripts are dropped.

This multi-strategy approach ensures clean removal regardless of which installer version
originally applied the patches.

### Repair Flow

`repair()` (`patcher.rs:203-225`):

1. For each patched HTML file, strip the existing injection in-place.
2. Update the backup file to the clean state (so backups reflect current Root HTML).
3. Call `install()` to re-inject fresh patches.

This is different from uninstall+install because it preserves the backup chain and
avoids deleting/re-deploying hook files unnecessarily.

---

## Hook Deployment

### Source: `installer/src-tauri/src/hook.rs`

The hook module handles deploying artifact files to disk, configuring CLR profiler
environment variables, and managing Root processes.

### Uprooted Directory

All artifacts are deployed to a single directory:

| Platform | Path                                  |
|----------|---------------------------------------|
| Windows  | `%LOCALAPPDATA%\Root\uprooted\`       |
| Linux    | `~/.local/share/uprooted/`            |

See `get_uprooted_dir()` at `hook.rs:43-53`.

### Deployed Files

`deploy_files()` (`hook.rs:65-93`) extracts five embedded artifacts:

| File                       | Source Constant           | Purpose                                  |
|----------------------------|---------------------------|------------------------------------------|
| `uprooted_profiler.dll`   | `embedded::PROFILER`      | CLR profiler native DLL (Windows)        |
| `libuprooted_profiler.so` | `embedded::PROFILER`      | CLR profiler native library (Linux)      |
| `UprootedHook.dll`        | `embedded::HOOK_DLL`      | C# .NET hook assembly (managed code)     |
| `UprootedHook.deps.json`  | `embedded::HOOK_DEPS_JSON`| .NET dependency manifest for the hook    |
| `uprooted-preload.js`     | `embedded::PRELOAD_JS`    | TypeScript bundle (compiled to JS)       |
| `uprooted.css`            | `embedded::THEME_CSS`     | Base theme stylesheet                    |

On Linux, the profiler `.so` file is set to `0o755` (executable) after deployment
(`hook.rs:84-90`).

The directory is created with `fs::create_dir_all()` if it does not exist.

### Environment Variables

The CLR profiler requires four environment variables to be set for `Root.exe` to load
the hook on startup:

| Variable                    | Value                                    | Purpose                          |
|-----------------------------|------------------------------------------|----------------------------------|
| `CORECLR_ENABLE_PROFILING`  | `1`                                      | Enable CLR profiling             |
| `CORECLR_PROFILER`          | `{D1A6F5A0-1234-4567-89AB-CDEF01234567}` | GUID identifying the profiler   |
| `CORECLR_PROFILER_PATH`     | Full path to profiler DLL/SO             | Where the runtime loads the profiler from |
| `DOTNET_ReadyToRun`         | `0`                                      | Disable R2R to ensure JIT hooks work |

Additionally, the legacy `DOTNET_STARTUP_HOOKS` variable is deleted if present
(`hook.rs:124`).

#### Windows Implementation

`set_env_vars()` (`hook.rs:99-128`):
- Opens `HKEY_CURRENT_USER\Environment` via the `winreg` crate.
- Writes all four env vars as REG_SZ values (user-scoped, not system-wide).
- Deletes `DOTNET_STARTUP_HOOKS` if it exists (legacy cleanup).
- Calls `broadcast_env_change()` to notify running processes.

`remove_env_vars()` (`hook.rs:132-144`):
- Opens `HKEY_CURRENT_USER\Environment` with `KEY_WRITE` access.
- Deletes all five env var names (the four active plus the legacy one).
- Calls `broadcast_env_change()`.

`broadcast_env_change()` (`hook.rs:176-198`):
- Sends `WM_SETTINGCHANGE` (0x001A) to `HWND_BROADCAST` (0xFFFF) with the
  `"Environment"` string as the lParam.
- Uses `SendMessageTimeoutW` with `SMTO_ABORTIFHUNG` and a 5-second timeout.
- This causes Explorer, shells, and other applications to re-read environment
  variables from the registry, so Root picks up the changes on next launch without
  requiring a reboot or re-login.

`check_env_vars()` (`hook.rs:148-173`):
- Reads all four env vars from `HKCU\Environment` and validates their values.
- Returns a tuple of four booleans: (enable, guid, path, r2r).

#### Linux Implementation

`set_env_vars()` (`hook.rs:209-260`) uses three complementary mechanisms:

1. **`~/.config/environment.d/uprooted.conf`** (`hook.rs:216-230`):
   A systemd user session environment file. Variables set here are picked up after
   re-login, equivalent to the Windows registry approach. This is the primary mechanism.

2. **Wrapper script `launch-root.sh`** (`hook.rs:232-254`):
   Written to the uprooted directory. Exports all env vars then `exec`s the Root
   binary. This works immediately from a terminal without re-login. Set to `0o755`.

3. **`.desktop` file** (`hook.rs:284-316`):
   `create_desktop_file()` writes `~/.local/share/applications/root-uprooted.desktop`
   with the `Name` "Root (Uprooted)" and `Exec` pointing to the wrapper script.
   This creates an application menu entry for launching Root with mods enabled.

`remove_env_vars()` (`hook.rs:263-282`):
- Deletes `~/.config/environment.d/uprooted.conf`.
- Deletes the `launch-root.sh` wrapper script.
- Deletes `~/.local/share/applications/root-uprooted.desktop`.

`check_env_vars()` (`hook.rs:320-339`):
- Reads `environment.d/uprooted.conf`; falls back to `launch-root.sh`.
- Checks for substring presence of each expected env var assignment.

### File Removal

`remove_files()` (`hook.rs:344-351`) deletes the entire uprooted directory with
`fs::remove_dir_all()`. This is called during uninstall after HTML has been restored.

### Hook Status Check

`check_hook_status()` (`hook.rs:354-381`) checks:
- Existence of all five deployed files.
- Correctness of all four environment variables (via `check_env_vars()`).
- Sets `files_ok = true` only if all five files exist.
- Sets `env_ok = true` only if enable + guid + path are all correct.

### Process Management

**`check_root_running()`** (`hook.rs:386-400`): On Windows, uses `find_root_pids()`
(toolhelp snapshot via `CreateToolhelp32Snapshot` + `Process32FirstW`/`Process32NextW`,
collecting PIDs matching `"Root.exe"` case-insensitively; `hook.rs:437-471`). On Linux,
runs `pgrep -x Root`.

**`kill_root_processes()`** (`hook.rs:403-433`): On Windows, calls `OpenProcess` with
`PROCESS_TERMINATE` then `TerminateProcess` for each PID. On Linux, runs `pkill -x
Root`. Returns the count of terminated processes.

---

## Settings

### Source: `installer/src-tauri/src/settings.rs`

The settings module provides read/write access to Uprooted's configuration file from
the installer. The same file is read by the TypeScript runtime inside Root.

### Settings File Location

```
{profile_dir}/uprooted-settings.json
```

Resolved via `detection::get_profile_dir()`. See `settings.rs:30-32`.

### Data Structure

`UprootedSettings` (`settings.rs:14-18`) contains three fields: `enabled` (master
toggle), `plugins` (a `HashMap<String, PluginSettings>` where each plugin has an
`enabled` flag and a `config` map of arbitrary JSON values), and `custom_css` (user
custom CSS string). Fields are serialized with `camelCase` naming via
`#[serde(rename_all = "camelCase")]` for JavaScript compatibility.

### Load

`load_settings()` (`settings.rs:34-44`):
- If the settings file exists and parses successfully, return the deserialized struct.
- Otherwise, return `UprootedSettings::default()` (enabled=true, no plugins, no CSS).

### Save

`save_settings()` (`settings.rs:46-54`):
- Creates parent directories if needed.
- Serializes to pretty-printed JSON via `serde_json::to_string_pretty()`.
- Writes to disk.

### Theme Application

The `apply_theme` command in `main.rs:127-139` demonstrates how settings are used:
1. Load current settings.
2. Get or create the `"themes"` plugin entry.
3. Set `config["theme"]` to the theme name.
4. Save settings back to disk.

---

## Themes

### Source: `installer/src-tauri/src/themes.rs`

The themes module provides access to the built-in theme definitions.

### Theme Definition

`ThemeDefinition` (`themes.rs:13-20`) contains: `name` (internal identifier),
`display_name`, `description`, `author`, `variables` (a `HashMap<String, String>` of
CSS custom properties), and `preview_colors` (a `PreviewColors` struct with
`background`, `text`, `accent`, `border` color strings for UI preview cards).

### Theme Source

`get_builtin_themes()` (`themes.rs:22-25`) loads themes from:
```
installer/src/plugins/themes/themes.json
```

This file is compiled into the binary at build time via `include_str!()` (not
`include_bytes!()` -- it is parsed as a JSON string). The relative path
`"../../../src/plugins/themes/themes.json"` resolves from the Rust source directory
to the frontend source tree.

If parsing fails, an empty vector is returned.

---

## Embedded Artifacts

### Source: `installer/src-tauri/src/embedded.rs`

All deployment artifacts are embedded directly into the installer binary using Rust's
`include_bytes!()` macro. This makes the installer fully self-contained -- no network
access, no sidecar files, no extraction steps.

### Artifact List

| Constant          | File                                         | Platform  | Purpose                         |
|-------------------|----------------------------------------------|-----------|---------------------------------|
| `PROFILER`        | `artifacts/uprooted_profiler.dll`            | Windows   | CLR profiler native DLL         |
| `PROFILER`        | `artifacts/libuprooted_profiler.so`          | Linux     | CLR profiler native shared lib  |
| `HOOK_DLL`        | `artifacts/UprootedHook.dll`                 | Both      | C# .NET hook assembly           |
| `HOOK_DEPS_JSON`  | `artifacts/UprootedHook.deps.json`           | Both      | .NET dependency manifest        |
| `PRELOAD_JS`      | `artifacts/uprooted-preload.js`              | Both      | TypeScript bundle (compiled)    |
| `THEME_CSS`       | `artifacts/uprooted.css`                     | Both      | Base theme CSS                  |

The `PROFILER` constant uses `#[cfg(target_os)]` conditional compilation to include
the correct native binary for the build target (`embedded.rs:7-10`).

### Artifact Staging

The `installer/src-tauri/artifacts/` directory is populated by `scripts/build_installer.ps1`
before `cargo tauri build`: build the C# hook (`dotnet build hook/ -c Release`), build
the TS bundle (`pnpm build`), copy outputs to `artifacts/`, then run `cargo tauri build`.
Missing artifacts cause a compile error since `include_bytes!()` is evaluated at compile
time. See [Build Guide](BUILD.md) for the full pipeline.

---

## TypeScript Frontend

The frontend is a vanilla TypeScript application with no framework dependencies. It
renders directly into the DOM and communicates with the Rust backend exclusively through
Tauri's IPC `invoke()` bridge.

### Entry Point

**Source:** `installer/src/main.ts`

On `DOMContentLoaded` (`main.ts:59-64`):

1. **`initStarfield()`** -- start the animated background canvas.
2. **`setupTitlebar()`** -- bind minimize/close buttons to Tauri window controls
   (`main.ts:8-18`). Uses `getCurrentWindow()` from the global `__TAURI__` object.
3. **`setupNav()`** -- attach click handlers to `.nav-tab` elements for page switching
   (`main.ts:49-56`).
4. **`switchPage("installer")`** -- navigate to the default page.

### Page Routing

The app uses a simple client-side page router (`main.ts:21-47`):

- **Pages:** `"installer"` and `"themes"` (type `PageName`).
- Each page has an `init()` function that runs once when the page is first shown.
- Page visibility is toggled via `.active` CSS classes on `.page` elements.
- Navigation tabs (`.nav-tab`) are updated with active styling.

### Main Page (Installer)

**Source:** `installer/src/pages/main.ts`

This is the primary page, containing the status display, action buttons, and log output.

**Initialization** (`main.ts:446-494`): Fetches the installer version, renders the
page template (header, status section, action buttons, log area), binds click handlers,
and runs initial detection.

**Detection** (`main.ts:54-138`): Calls `detectRoot()` and logs each component's
status (Root executable, profile, HTML targets, hook files, env vars, patch state).
Calls `analyzeScenario()` for smart recommendations, then updates the status display
and button states.

**Scenario Analysis** (`main.ts:140-184`): Examines the detection result and provides
contextual guidance -- "restart root to activate" (fully installed), "install root
communications first" (not found), "launch root once to generate profile" (no HTML
files), specific repair suggestions (partial state), or "ready to install" (clean).

**Root-Running Guard** (`main.ts:268-335`): Before any action, `ensureRootClosed()`
checks if Root is running. If so, shows a popup with "close root" (calls `killRoot()`,
waits 1.5s, verifies exit) or "cancel" (aborts the operation).

**Action Handlers** (`main.ts:339-423`): Each action follows the same pattern: guard
via `ensureRootClosed()`, set loading state, log progress, invoke the Tauri command,
log the result, and re-run detection to refresh the UI.

**Status Display** (`main.ts:188-223`): Five status rows with color-coded dots
(green/yellow/red) for Root executable, profile/HTML targets, hook files, env vars,
and HTML patch state. Yellow indicates partial deployment/configuration.

**Copy Logs** (`main.ts:427-442`): Extracts log lines as plain text and copies to
clipboard with a brief "copied" badge.

### Themes Page

**Source:** `installer/src/pages/themes.ts`

Provides a visual theme browser and one-click theme application.

**Initialization** (`themes.ts:103-132`):
1. Load all built-in themes via `listThemes()`.
2. Load current settings via `loadSettings()` to determine the active theme.
3. Select the active theme (or first available) and render.

**Theme Cards** (`themes.ts:13-31`):
Each theme is rendered as a card with:
- Color swatches (background, accent, text from `preview_colors`).
- Theme name, description, and author.
- "active" badge if currently selected.

**Theme Detail** (`themes.ts:33-64`):
When a theme is selected, a detail panel shows all CSS variables defined by the theme.
Each variable is displayed with its name, value, and a color chip preview.

**Theme Application** (`themes.ts:84-100`):
Clicking a theme card:
1. Updates the selected theme.
2. If different from the active theme, calls `applyTheme(name)` (Tauri command).
3. Updates `activeTheme` and re-renders.

Users must restart Root for theme changes to take effect.

### State Management

**Source:** `installer/src/lib/state.ts`

A minimal reactive state container: `State<T>` with `get()`, `set(next)`, and
`subscribe(fn)` (returns an unsubscribe function). Notifies all subscribers
synchronously on `set()`. Available as a utility, though the current pages use direct
DOM manipulation rather than reactive bindings.

### Tauri Bridge

**Source:** `installer/src/lib/tauri.ts`

Type-safe wrapper around Tauri's `invoke()` IPC mechanism. Accesses the global
`__TAURI__.core.invoke` function (available because `withGlobalTauri: true` is set in
`tauri.conf.json`).

**Exported Functions:**

| Function            | Tauri Command           | Return Type              |
|---------------------|-------------------------|--------------------------|
| `detectRoot()`      | `detect_root`           | `Promise<DetectionResult>` |
| `installUprooted()` | `install_uprooted`      | `Promise<PatchResult>`   |
| `uninstallUprooted()`| `uninstall_uprooted`   | `Promise<PatchResult>`   |
| `repairUprooted()`  | `repair_uprooted`       | `Promise<PatchResult>`   |
| `loadSettings()`    | `load_settings`         | `Promise<UprootedSettings>` |
| `saveSettings()`    | `save_settings`         | `Promise<void>`          |
| `listThemes()`      | `list_themes`           | `Promise<ThemeDefinition[]>` |
| `applyTheme()`      | `apply_theme`           | `Promise<void>`          |
| `getUprootedVersion()` | `get_uprooted_version` | `Promise<string>`      |
| `openProfileDir()`  | `open_profile_dir`      | `Promise<void>`          |
| `checkHookStatus()` | `check_hook_status`     | `Promise<HookStatus>`   |
| `checkRootRunning()`| `check_root_running`    | `Promise<boolean>`       |
| `killRoot()`        | `kill_root`             | `Promise<number>`        |

All TypeScript interfaces mirror the Rust serialized structs exactly (snake_case field
names preserved from Rust serialization).

### Starfield Background

**Source:** `installer/src/starfield.ts`

A cosmetic animated star field on a full-window `<canvas id="stars">` element.
`initStarfield()` (`starfield.ts:9-56`) generates `(width * height) / 8000` stars with
random positions, radii (0.3--1.5), base alpha (0.15--0.65), and drift speed. The
`draw()` loop runs via `requestAnimationFrame`, computing a sinusoidal flicker per star
and rendering each as a 1x1 or 2x2 blue-white pixel (`rgba(200, 210, 220, alpha)`).
Re-seeds on window resize.

---

## Build

### Rust Build Script

**Source:** `installer/src-tauri/build.rs`

The build script is minimal -- it calls `tauri_build::build()` which handles:
- Generating the Tauri context (window config, app metadata).
- Bundling the frontend dist into the binary.
- Platform-specific resource embedding.

### Vite Configuration

**Source:** `installer/vite.config.ts`

Key settings: `clearScreen: false` (preserves Tauri terminal output), `strictPort: true`
(fails if port 1420 is taken), `envPrefix: ["VITE_", "TAURI_"]` (exposes both prefixes
to frontend), `target: "chrome120"` (modern webview, no legacy support), `minify:
"esbuild"`, `sourcemap: true`.

### Tauri Configuration

**Source:** `installer/src-tauri/tauri.conf.json`

Identifier: `sh.uprooted.installer`. Frontend dist: `../dist`. Dev URL:
`http://localhost:1420`. `beforeBuildCommand: "pnpm build"` runs Vite before Tauri.
`withGlobalTauri: true` exposes `__TAURI__` on `window`. `decorations: false` (custom
titlebar) and `transparent: true` (starfield background shows through).

### Dependencies

**Rust** (`Cargo.toml`): `tauri` 2, `tauri-plugin-shell` 2, `serde`/`serde_json` 1,
`glob` 0.3, `opener` 0.7. Windows-only: `winreg` 0.55 (registry), `windows-sys` 0.59
(Win32 API for `SendMessageTimeoutW`, `TerminateProcess`, toolhelp snapshots).

**TypeScript** (`package.json`): `@tauri-apps/api` ^2.5.0, `@tauri-apps/cli` ^2.5.0,
`typescript` ^5.7.0, `vite` ^6.1.0.

### Build Commands

```bash
# Development (hot-reload frontend + Rust backend)
cd installer && pnpm tauri dev

# Production build (requires artifacts staged first)
cd installer && pnpm tauri build

# Full pipeline (builds hook, stages artifacts, builds installer)
powershell -File scripts/build_installer.ps1
```

The production sequence: `pnpm build` (Vite compiles TS to `dist/`), `tauri-build`
processes config and generates Rust glue, `cargo build --release` compiles the backend
with `include_bytes!()` pulling from `artifacts/`, and Tauri packages the final
executable. See [Build Guide](BUILD.md) for the full CI pipeline.

---

## Cross-References

- See [Hook Reference](HOOK_REFERENCE.md) for what the deployed hook does at runtime
  (5-phase startup, sidebar injection, Avalonia reflection).
- See [Installation Guide](INSTALLATION.md) for end-user install instructions.
- See [Architecture](ARCHITECTURE.md) for how the installer fits into the dual-layer
  injection model.
- The `uprooted-preload.js` deployed by the installer is compiled from the public
  repo's TypeScript source -- it bootstraps the plugin system, theme engine, and bridge
  proxies inside Root's Chromium webviews.
