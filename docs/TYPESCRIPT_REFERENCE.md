# TypeScript Browser Injection Layer Reference

> **Related docs:** [Architecture](ARCHITECTURE.md) | [Hook Reference](HOOK_REFERENCE.md) | [Theme Engine Deep Dive](THEME_ENGINE_DEEP_DIVE.md) | [Root Environment](plugins/ROOT_ENVIRONMENT.md)
> - [Bridge Reference](plugins/BRIDGE_REFERENCE.md) -- Bridge proxy interception and method catalog
> - [Build Guide](BUILD.md) -- Build instructions for all components

---

## Table of Contents

1. [Overview](#overview)
2. [Core Runtime](#core-runtime)
3. [Plugin API](#plugin-api)
4. [Built-in Plugins](#built-in-plugins)
5. [Type Definitions](#type-definitions)
6. [Build System](#build-system)

---

## Overview

The TypeScript browser injection layer is Uprooted's client-side runtime. It runs inside
Root Communications' embedded DotNetBrowser Chromium instance and provides the plugin
system, theme engine, and bridge proxies that make client modification possible.

### How It Gets There

The C# hook (see [Hook Reference](HOOK_REFERENCE.md)) patches Root's profile HTML files
(`WebRtcBundle/index.html` and `RootApps/*/index.html`) by inserting tags before `</head>`:

1. A `<script>` block setting `window.__UPROOTED_SETTINGS__` with JSON settings
2. A `<script src="file:///...">` loading `dist/uprooted-preload.js` (the bundled IIFE)
3. A `<link rel="stylesheet">` loading `dist/uprooted.css` (combined plugin CSS)

Because these tags appear before Root's own bundle scripts, Uprooted's preload runs first,
allowing bridge proxies to be installed before Root's WebRTC code touches the globals.

### Relationship to the C# Hook Layer

| Concern | C# Hook | TypeScript Layer |
|---------|---------|-----------------|
| Injection target | Avalonia UI (.NET) | Chromium DOM (browser) |
| Communication | Reflection into Avalonia types | ES6 Proxy on `window` globals |
| Settings | INI file via `UprootedSettings.cs` | JSON file via `settings.ts`, inlined into HTML |
| Plugin system | None (monolithic) | Full lifecycle with patches, CSS, events |
| Theme engine | `ThemeEngine.cs` for native Avalonia | CSS variable overrides in the browser |

### Source Layout

```
src/
  core/
    preload.ts          Entry point -- initialization sequence
    pluginLoader.ts     Plugin lifecycle management
    patcher.ts          HTML patch install/uninstall (CLI tool, not browser)
    settings.ts         File-based settings I/O (CLI tool, not browser)
  api/
    bridge.ts           ES6 Proxy wrapper for Root's bridge globals
    css.ts              Runtime CSS injection/removal
    dom.ts              DOM query and mutation helpers
    native.ts           Native bridge call wrappers
    index.ts            Barrel export for plugin authors
  plugins/
    sentry-blocker/     Blocks Sentry telemetry
    themes/             CSS variable theme engine
    settings-panel/     In-app settings sidebar injection
  types/
    bridge.ts           INativeToWebRtc / IWebRtcToNative interfaces
    plugin.ts           UprootedPlugin, Patch, SettingField interfaces
    root.ts             Window augmentation (global type declarations)
    settings.ts         UprootedSettings / PluginSettings interfaces
```

Note: `patcher.ts` and `settings.ts` in `core/` are Node.js CLI scripts used by the
installer. They are **not** part of the browser bundle -- esbuild marks `node:fs` and
`node:path` as external.

---

## Core Runtime

### preload.ts -- Initialization

**File**: `src/core/preload.ts`

Single entry point for the TypeScript layer. Bundled as an IIFE that executes on load.

#### Initialization Sequence (line 22, `main()`)

1. **Read settings** (line 24): Reads `window.__UPROOTED_SETTINGS__`. If `enabled` is
   false, exits silently.
2. **Set version global** (line 34): Sets `window.__UPROOTED_VERSION__` from the
   compile-time constant `__UPROOTED_VERSION__` (injected by esbuild from `package.json`).
3. **Install bridge proxies** (line 37): Calls `installBridgeProxy()` -- wraps
   `window.__nativeToWebRtc` and `window.__webRtcToNative` with ES6 Proxies.
4. **Initialize plugin loader** (line 40): Creates `PluginLoader` with settings, exposes
   it on `window.__UPROOTED_LOADER__` (line 43).
5. **Wire loader into bridge** (line 46): Calls `setPluginLoader(loader)` so bridge proxy
   events reach the plugin system.
6. **Register built-in plugins** (lines 48-51): `sentry-blocker` first (wraps fetch
   earliest), then `themes`, then `settings-panel`.
7. **Inject custom CSS** (lines 54-56): If `settings.customCss` is non-empty.
8. **Start enabled plugins** (lines 59-61): Calls `loader.startAll()`.

#### Error Handling

On fatal exception, a red fixed-position error banner is injected into the DOM (lines
64-71). This is necessary because Root's Chromium has no accessible DevTools.

#### DOM Readiness

If `document.readyState === "loading"`, defers to `DOMContentLoaded`. Otherwise runs
immediately (line 77).

#### Global Surface

| Global | Type | Purpose |
|--------|------|---------|
| `window.__UPROOTED_SETTINGS__` | `UprootedSettings` | Inlined settings from JSON file |
| `window.__UPROOTED_VERSION__` | `string` | Version from `package.json` |
| `window.__UPROOTED_LOADER__` | `PluginLoader` | Plugin loader instance |

---

### pluginLoader.ts -- Plugin Lifecycle

**File**: `src/core/pluginLoader.ts`

#### Class: PluginLoader (line 20)

```typescript
export class PluginLoader {
  private plugins = new Map<string, UprootedPlugin>();
  private activePlugins = new Set<string>();
  private eventHandlers = new Map<string, EventHandler[]>();
  private settings: UprootedSettings;
  constructor(settings: UprootedSettings);
}
```

#### Registration: `register(plugin)` -- Line 31

Adds a plugin to the map. Does not start it. Skips duplicates with a warning.

#### Starting: `start(name)` -- Line 40

1. Install patches (line 51): Register bridge event handlers for each `Patch`.
2. Inject CSS (line 58): `injectCss("plugin-${name}", plugin.css)`.
3. Call `start()` hook (line 63): Awaited for async init.
4. Add to `activePlugins`.

#### Stopping: `stop(name)` -- Line 73

1. Call `stop()` hook (awaited).
2. Remove CSS via `removeCss("plugin-${name}")`.
3. Remove all bridge event handlers for this plugin.
4. Remove from `activePlugins`.

#### Batch Start: `startAll()` -- Line 96

Iterates registered plugins, starts those with `enabled !== false` (defaults to `true`).
Plugins start sequentially in registration order.

#### Event System: `emit(eventName, event)` -- Line 107

Called by the bridge proxy on every bridge method call. Dispatches to handlers registered
for `"eventName:methodName"`. If any handler sets `event.cancelled = true`, subsequent
handlers are skipped and the original call is suppressed.

#### BridgeEvent Interface (line 11)

```typescript
export interface BridgeEvent {
  method: string;       // Bridge method being called
  args: unknown[];      // Arguments (mutable -- plugins can modify)
  cancelled: boolean;   // Set true to suppress the original call
  returnValue?: unknown; // Return value when cancelled (from replace patches)
}
```

#### Patch Installation (line 118)

Converts `Patch` declarations to event handlers:
- `patch.replace`: Sets `event.returnValue` and cancels the original call.
- `patch.before`: Calls with args; returns `false` to cancel.
- Each handler tagged with `__plugin` for cleanup on stop.

Note: The `Patch` interface defines an `after` callback, but `installPatch` does not wire
it up yet. Planned for a future release.

---

### patcher.ts -- HTML Injection (CLI)

**File**: `src/core/patcher.ts` -- Node.js only, not in the browser bundle.

Injects or removes Uprooted's `<script>` and `<link>` tags from Root's HTML files. Used by
the installer. See [Hook Reference](HOOK_REFERENCE.md) for the runtime equivalent in C#.

#### Target Files

```
%LOCALAPPDATA%/Root Communications/Root/profile/default/
  WebRtcBundle/index.html
  RootApps/*/index.html
```

#### Injection Marker (line 18)

```typescript
const INJECTION_MARKER = "<!-- uprooted -->";
```

Used for idempotency (skip if present) and clean removal (strip marked lines).

#### `install(distDir)` -- Line 42

Backs up originals to `*.uprooted.bak`, injects settings + script + CSS tags before
`</head>`.

#### `uninstall()` -- Line 84

Restores from backup if available; otherwise strips lines containing the marker.

---

### settings.ts -- File-Based Settings I/O (CLI)

**File**: `src/core/settings.ts` -- Node.js only.

Root runs Chromium with `--incognito`, so localStorage is wiped every launch. Settings are
stored as JSON on disk and inlined into HTML by the patcher.

```typescript
// Settings file location (line 21):
// %LOCALAPPDATA%/Root Communications/Root/profile/default/uprooted-settings.json
```

- **`loadSettings()`** (line 23): Reads JSON, merges with `DEFAULT_SETTINGS`. Returns
  defaults if file missing or corrupt.
- **`saveSettings(settings)`** (line 35): Writes formatted JSON, creates dirs as needed.
- **`getSettingsPath()`** (line 44): Returns absolute path for the installer UI.

---

## Plugin API

The `src/api/` directory contains the runtime API available to plugins inside the browser.

### bridge.ts -- ES6 Proxy Bridge Interception

**File**: `src/api/bridge.ts`

Root's WebRTC layer communicates with .NET through two globals:

| Global | Direction | Purpose |
|--------|-----------|---------|
| `window.__nativeToWebRtc` | .NET --> Browser | C# host controlling WebRTC |
| `window.__webRtcToNative` | Browser --> .NET | JS notifying native host |

#### `installBridgeProxy()` -- Line 49

Handles two scenarios:

1. **Immediate** (lines 53-71): If globals exist, wrap them now.
2. **Deferred** (lines 76-102): Install `Object.defineProperty` setter traps so assignment
   by Root automatically wraps the value.

```typescript
// Deferred proxy example (src/api/bridge.ts:80-90):
let _ntw: INativeToWebRtc | undefined;
Object.defineProperty(window, "__nativeToWebRtc", {
  get: () => _ntw,
  set: (val: INativeToWebRtc) => {
    _ntw = createBridgeProxy(val, "bridge:nativeToWebRtc");
  },
  configurable: true,
});
```

#### `createBridgeProxy<T>(target, eventPrefix)` -- Line 21

ES6 Proxy with a `get` trap. For function properties, returns a wrapper that creates a
`BridgeEvent`, emits it through the plugin loader, and either returns the event's
`returnValue` (if cancelled) or calls the original function with potentially modified args.

#### `setPluginLoader(loader)` -- Line 17

Wires the loader into the bridge module. Called once during initialization.

#### Bridge Proxy Deferred Installation

The bridge globals (`window.__nativeToWebRtc` and `window.__webRtcToNative`) are not
present on the `window` object when Uprooted's preload script runs. Root's DotNetBrowser
host assigns them asynchronously -- the C# side calls `ExecuteJavaScript` to set the
globals after the Chromium frame has loaded and the WebRTC session is being initialized.
The exact timing depends on network conditions and the .NET host's initialization sequence,
so Uprooted cannot simply wait a fixed delay.

**Why immediate installation fails:** If `installBridgeProxy()` only checked for the
globals at call time and wrapped them, it would miss the assignment entirely. The globals
do not exist yet, so there is nothing to wrap. By the time Root assigns them, the plain
(unproxied) objects would be on `window` and all bridge calls would bypass the plugin
system.

**The deferred pattern** solves this with `Object.defineProperty` setter traps:

1. `installBridgeProxy()` first checks whether the globals already exist (lines 53-71).
   If Root has somehow already assigned them (rare but possible on cached pages), they are
   wrapped immediately with `createBridgeProxy()`.

2. For globals that do not yet exist, a property descriptor with a `get`/`set` pair is
   installed on `window` (lines 80-102). The `set` trap intercepts Root's future
   assignment and automatically wraps the assigned value with `createBridgeProxy()` before
   storing it in a closure variable. The `get` trap returns the proxied value from that
   same closure variable.

3. The property is marked `configurable: true` so that if Root's code itself tries to
   redefine the property (it does not currently, but defensive coding), the trap can be
   replaced without throwing.

**Timing guarantee:** Because Uprooted's `<script>` tag appears before Root's bundle
scripts in the HTML `<head>`, the `Object.defineProperty` traps are installed before any
Root code executes. When Root later assigns `window.__nativeToWebRtc = { ... }`, the
setter trap fires, the value is proxied transparently, and all subsequent reads through
`window.__nativeToWebRtc` return the proxied version. Root's own code never sees the raw
object after this point.

**Edge case -- double assignment:** If Root assigns the same global twice (observed during
reconnection flows), the setter fires again and re-wraps the new value. The old proxy is
discarded. The `pluginLoader` reference is module-scoped, so all proxies share the same
loader instance regardless of how many times the globals are reassigned.

---

### css.ts -- CSS Injection Utilities

**File**: `src/api/css.ts`

Manages `<style>` elements in `<head>`, prefixed with `uprooted-css-` (line 8).

- **`injectCss(id, css)`** (line 14): Creates or replaces a `<style>` element.
- **`removeCss(id)`** (line 30): Removes a style element by ID.
- **`removeAllCss()`** (line 39): Removes all `uprooted-css-*` style elements.

---

### dom.ts -- DOM Manipulation Helpers

**File**: `src/api/dom.ts`

- **`waitForElement<T>(selector, timeout?)`** (line 9): Returns a Promise that resolves
  when a matching element appears. Uses `MutationObserver`, default 10s timeout.
- **`observe(target, callback, options?)`** (line 44): `MutationObserver` wrapper returning
  a disconnect function.
- **`nextFrame()`** (line 57): Promise resolving on next `requestAnimationFrame`.

---

### native.ts -- Native Bridge Call Wrappers

**File**: `src/api/native.ts`

- **`getCurrentTheme()`** (line 11): Reads `data-theme` from `<html>`.
- **`setCssVariable(name, value)`** (line 19): Sets a CSS variable on `:root`.
- **`removeCssVariable(name)`** (line 26): Removes a CSS variable override.
- **`setCssVariables(vars)`** (line 33): Batch-sets CSS variables from a record.
- **`nativeLog(message)`** (line 42): Sends `[Uprooted] ${message}` through
  `window.__webRtcToNative.log()` to .NET logs.

---

### index.ts -- API Barrel Export

**File**: `src/api/index.ts`

```typescript
export * as css from "./css.js";
export * as dom from "./dom.js";
export * as native from "./native.js";
export * as bridge from "./bridge.js";
```

See [Plugin Getting Started](plugins/GETTING_STARTED.md) for writing plugins that use
this API.

---

## Built-in Plugins

Registered in `preload.ts` (lines 48-51). All follow the `UprootedPlugin` interface.

### sentry-blocker -- Telemetry Blocking

**File**: `src/plugins/sentry-blocker/index.ts`

Blocks Sentry error tracking. Root sends telemetry to Sentry with `sendDefaultPii: true`
(IP addresses), `replaysOnErrorSampleRate: 0.25` (DOM replays with mouse and inputs),
and leaks Bearer tokens in request breadcrumbs.

#### Why Fetch-Level Blocking?

Sentry initializes at module evaluation time in Root's bundle, before Uprooted runs.
Overriding `Sentry.init` is not viable. Instead, this plugin wraps the network APIs.

#### Intercepted APIs (`start()`, line 34)

1. **`window.fetch`** (lines 38-46): Returns a fake `Response(null, { status: 200 })` for
   Sentry URLs. Sentry sees success and does not retry.
2. **`XMLHttpRequest.prototype.open`** (lines 49-62): Redirects Sentry requests to
   `about:blank`.
3. **`navigator.sendBeacon`** (lines 65-73): Returns `true` without sending data.

URL detection via `isSentryUrl()` (line 23): checks if URL string contains `"sentry.io"`.
Handles `string`, `URL`, and `Request` input types.

#### Cleanup (`stop()`, line 78)

Restores all three original functions and logs total blocked count.

---

### themes -- CSS Variable Theme Engine

**File**: `src/plugins/themes/index.ts`

Root's color system uses CSS variables (`--rootsdk-*`). This plugin overrides them.

#### Theme Definitions

Loaded from `themes.json` at build time. Each theme has `name`, `display_name`, and a
`variables` record mapping CSS variable names to values.

#### Color Math (lines 48-77)

- `parseHex(hex)` / `toHex(r,g,b)` -- hex <-> RGB conversion
- `darken(hex, percent)` / `lighten(hex, percent)` -- shade adjustment
- `luminance(hex)` -- WCAG relative luminance

#### `generateCustomVariables(accent, bg)` -- Line 82

Derives 10 CSS variables from two colors. Uses luminance to detect dark/light background:

```typescript
export function generateCustomVariables(accent: string, bg: string): Record<string, string> {
  const isDark = luminance(bg) < 0.3;
  return {
    "--rootsdk-brand-primary": accent,
    "--rootsdk-brand-secondary": lighten(accent, 15),
    "--rootsdk-brand-tertiary": darken(accent, 15),
    "--rootsdk-background-primary": bg,
    "--rootsdk-background-secondary": lighten(bg, 8),
    "--rootsdk-background-tertiary": darken(bg, 8),
    "--rootsdk-input": darken(bg, 5),
    "--rootsdk-border": lighten(bg, 18),
    "--rootsdk-link": lighten(accent, 30),
    "--rootsdk-muted": isDark ? lighten(bg, 25) : darken(bg, 25),
  };
}
```

#### `start()` -- Line 116

Flushes all known variables, reads theme name from settings, applies the matching theme's
variables (or generates custom variables from accent + background colors).

#### `stop()` -- Line 140

Removes all known CSS variables, restoring Root's defaults.

#### CSS Variable Integration with Native Theme Engine

The TypeScript theme plugin and the C# `ThemeEngine` (see
[Theme Engine Deep Dive](THEME_ENGINE_DEEP_DIVE.md)) operate on different layers of the
same application. The browser theme plugin overrides `--rootsdk-*` CSS variables in
Chromium's DOM, while the C# engine overrides Avalonia `ResourceDictionary` entries for
native controls. Both must stay in sync so the native sidebar, title bar, and window chrome
match the web content area.

**Mapping between CSS variables and Avalonia resource keys:**

| CSS Variable (`--rootsdk-*`) | Avalonia Resource Key | Notes |
|------------------------------|-----------------------|-------|
| `--rootsdk-brand-primary` | `ThemeAccentColor` / `ThemeAccentBrush` | Primary accent color |
| `--rootsdk-brand-secondary` | `ThemeAccentColor2` / `ThemeAccentBrush2` | Lighter accent variant |
| `--rootsdk-brand-tertiary` | `ThemeAccentColor3` / `ThemeAccentBrush3` | Darker accent variant |
| `--rootsdk-background-primary` | `SolidBackgroundFillColorBase` | Main background fill |
| `--rootsdk-background-secondary` | `SolidBackgroundFillColorSecondary` | Secondary background |
| `--rootsdk-background-tertiary` | `SolidBackgroundFillColorTertiary` | Tertiary background |
| `--rootsdk-text-primary` | `TextFillColorPrimary` | Primary text fill |
| `--rootsdk-text-secondary` | `TextFillColorSecondary` | Secondary (dimmed) text |
| `--rootsdk-border` | `ControlStrokeColorDefault` | Border/stroke color |
| `--rootsdk-error` | `ErrorColor` / `ErrorBrush` | Error state color |

These are not wired automatically. The two layers derive their palettes independently
from the same pair of inputs (accent color + background color) stored in settings. The C#
side uses `GenerateCustomTheme()` in `ThemeEngine.cs` to compute ~60 Avalonia resource
overrides via HSL color math. The TypeScript side uses `generateCustomVariables()` in
`src/plugins/themes/index.ts` to compute 10 CSS variable overrides using its own
`darken`/`lighten`/`luminance` helpers.

**Propagation flow -- native to browser:**

1. User selects a theme in Uprooted's native settings page (Avalonia UI).
2. `ThemeEngine.ApplyTheme()` or `ApplyCustomTheme()` updates Avalonia resources.
3. `ContentPages.UpdateLiveColors()` saves the accent and background hex values into
   static fields that feed `window.__UPROOTED_SETTINGS__`.
4. On the next page load, the TypeScript preload reads these settings and the themes
   plugin calls `setCssVariables()` to apply matching CSS overrides.

**Propagation flow -- browser to native (planned, not yet implemented):**

Currently there is no live channel from the TypeScript layer back to the C# hook at
runtime. If a user were to change the theme from within the browser settings panel, the
change would only take effect in CSS. The native Avalonia controls would not update until
the next application restart, when the C# hook reads the updated settings file. A future
bridge method (`__webRtcToNative.uprootedThemeChanged`) is planned to close this gap.

**Why two separate engines?**

Root's desktop app renders in two completely different technologies. Native controls
(sidebar, title bar, settings chrome, overlays) are Avalonia XAML -- they do not respond
to CSS. Web content (chat, voice, communities) is Chromium HTML -- it does not respond to
Avalonia resource dictionaries. There is no shared color bus, so each layer needs its own
theme engine targeting its own rendering pipeline.

---

### settings-panel -- In-App Settings UI

Three files: `index.ts` (plugin entry), `panel.ts` (DOM discovery), `components.ts` (UI).

#### index.ts -- Plugin Entry

**File**: `src/plugins/settings-panel/index.ts`

On `start()`, retrieves `PluginLoader` from `window.__UPROOTED_LOADER__` and passes it to
`startObserving()`. On `stop()`, calls `stopObserving()`.

#### panel.ts -- Sidebar Discovery and Injection

**File**: `src/plugins/settings-panel/panel.ts`

Uses a Vencord-inspired MutationObserver approach to detect Root's settings page.

**`startObserving()`** (line 49): Sets up MutationObserver on `document.body` with 80ms
debounce. Calls `tryInject()` on every mutation.

**`tryInject()`** (line 106) -- Core injection logic:

1. Guard: If `[data-uprooted]` elements exist, skip.
2. Find `"APP SETTINGS"` text via `findByExactText()` -- confirms settings page is open.
3. Find `"Advanced"` text -- the last sidebar item, used as insertion point.
4. `findSettingsLayout()` (line 170): Walks up DOM to find a flex-row/grid ancestor with
   sidebar + content children.
5. `findItemElement()` (line 234): Walks up from "Advanced" text to its nav item parent.
6. `injectSidebarSection()` (line 284): Clones the header as "UPROOTED", creates nav items
   ("Uprooted", "Plugins", "Themes") by cloning the template, strips React attributes.
7. `injectVersionText()` (line 489): Appends version text near Root's version display.

#### DOM Discovery Algorithm (Detail)

Root does not expose semantic IDs or stable class names for its settings layout. Class
names are hashed by the build tool (e.g. `_abc12d`) and change between versions. The
TypeScript layer therefore discovers the DOM structure using text content matching and
layout geometry analysis, never relying on class names or IDs.

**Phase 1 -- Text anchors.** `findByExactText()` uses a `TreeWalker` with
`NodeFilter.SHOW_ELEMENT` to find leaf elements (no children) whose trimmed `textContent`
exactly matches a target string. The two anchors are `"APP SETTINGS"` (confirms the
settings page is open) and `"Advanced"` (locates the last sidebar item for insertion). If
either anchor is missing, `tryInject()` returns silently -- this is expected on non-settings
pages and fires on every MutationObserver callback.

**Phase 2 -- Layout discovery.** `findSettingsLayout()` walks up from the `"APP SETTINGS"`
element through up to 20 ancestors, checking each for `display: flex; flex-direction: row`
or `display: grid` with at least 2 children. When found, it identifies which child contains
the sidebar text and which sibling is the content panel (the first non-sidebar child with
`clientWidth > 50` and `clientHeight > 50`). A fallback path looks for any ancestor whose
sibling is wider than itself, catching unusual layout structures.

**Phase 3 -- Template extraction.** `findItemElement()` walks up from the `"Advanced"`
text leaf toward the sidebar container. It identifies the item-level element by checking
whether the current element's parent has 3 or more children with text content (indicating
sibling nav items). The matched element becomes the clone template for Uprooted's sidebar
items.

**Phase 4 -- Injection.** `injectSidebarSection()` clones the `"APP SETTINGS"` header and
the template item. For each Uprooted nav item ("Uprooted", "Plugins", "Themes"), it:
- Deep-clones the template
- Replaces the first text node via `replaceTextContent()` (TreeWalker on `SHOW_TEXT`)
- Strips active/selected CSS classes via regex (`/active|selected|current/i`)
- Removes React internal attributes (`__react*`, `data-reactid`) and duplicate IDs
- Attaches a click handler that hides Root's content panel and inserts a dynamically
  built Uprooted page as a sibling

**MutationObserver pattern.** The observer watches `document.body` with
`{ childList: true, subtree: true }`. Every mutation triggers an 80ms debounced call to
`tryInject()`. The debounce prevents thrashing during React re-renders, which typically
emit multiple rapid mutations. The guard check (`document.querySelector("[data-uprooted]")`)
makes repeat calls nearly free. When settings closes and Root removes the sidebar from the
DOM, the `injected` flag is reset on the next mutation, allowing re-injection when settings
reopens.

**Content swapping** -- `onUprootedItemClick()` (line 397): Hides Root's content panel,
builds the requested page, inserts it as a sibling. `onSidebarClick()` (line 464) restores
Root's panel when a Root sidebar item is clicked.

#### components.ts -- UI Components

**File**: `src/plugins/settings-panel/components.ts`

**Primitive components**:

| Function | Line | Purpose |
|----------|------|---------|
| `createToggle(checked, onChange)` | 20 | Checkbox toggle switch |
| `createSelect(options, selected, onChange)` | 41 | Dropdown select |
| `createTextarea(value, placeholder, onChange)` | 62 | Text input, 300ms debounce |
| `createRow(label, description, control)` | 83 | Settings row with label + control |
| `createSection(label)` | 111 | Section header |

**Page builders**:

- **`buildUprootedPage()`** (line 126): About page with version, description, links.
- **`buildPluginsPage(loader)`** (line 183): Lists plugins with toggle switches and status
  badges. Shows privacy notice for sentry-blocker. Accesses loader internals via `any` cast.
- **`buildThemesPage(loader)`** (line 297): Theme dropdown with live preview, theme cards
  with color swatches, custom theme color pickers, and custom CSS textarea.

---

## Type Definitions

### src/types/bridge.ts -- Bridge Interfaces

**File**: `src/types/bridge.ts`

#### Branded GUID Types (lines 1-2)

```typescript
export type UserGuid = string & { readonly __brand: "UserGuid" };
export type DeviceGuid = string & { readonly __brand: "DeviceGuid" };
```

Opaque branded types preventing accidental mixing of user/device IDs at compile time.

#### Supporting Types (lines 4-58)

```typescript
export type TileType = "camera" | "screen" | "audio";
export type Theme = "dark" | "light" | "pure-dark";
export type ScreenQualityMode = "motion" | "detail" | "auto";
export type Codec = string;
export type WebRtcPermission = Record<string, boolean>;

export interface Coordinates { x: number; y: number; }
export interface IUserResponse { userId: UserGuid; displayName: string; avatarUrl?: string; [key: string]: unknown; }
export interface VolumeBoosterSettings { enabled: boolean; gain: number; }
export interface WebRtcError { code: string; message: string; }
export interface IPacket { type: string; data: unknown; }
export interface UserMediaStreamConstraints { audio?: MediaTrackConstraints | boolean; video?: MediaTrackConstraints | boolean; }
export interface DisplayMediaStreamConstraints { audio?: MediaTrackConstraints | boolean; video?: MediaTrackConstraints | boolean; }

export interface InitializeDesktopWebRtcPayload {
  token: string; channelId: string; communityId: string;
  userId: UserGuid; deviceId: DeviceGuid; theme: Theme;
  [key: string]: unknown;
}
```

#### `IWebRtcToNative` (line 64) -- Browser to .NET

Methods called by JavaScript to notify the .NET host. Key methods:

| Method | Purpose |
|--------|---------|
| `initialized()` | WebRTC session ready |
| `disconnected()` | Session disconnected |
| `localMuteWasSet(isMuted)` | Mute state changed |
| `localDeafenWasSet(isDeafened)` | Deafen state changed |
| `localAudioStarted/Stopped/Failed()` | Audio lifecycle |
| `localVideoStarted/Stopped/Failed()` | Video lifecycle |
| `localScreenStarted/Stopped/Failed()` | Screen share lifecycle |
| `getUserProfile(userId)` | Fetch user profile (async) |
| `getUserProfiles(userIds)` | Fetch multiple profiles (async) |
| `setSpeaking(isSpeaking, deviceId, userId)` | Speaking indicator |
| `setHandRaised(isHandRaised, deviceId, userId)` | Hand raise state |
| `failed(error)` | Report WebRTC error |
| `setAdminMute/setAdminDeafen(deviceId, state)` | Admin controls |
| `kickPeer(userId)` | Kick a peer |
| `viewProfileMenu/viewContextMenu(userId, coords, ...)` | UI menus |
| `log(message)` | Send log to .NET |

#### `INativeToWebRtc` (line 105) -- .NET to Browser

Methods called by the C# host to control WebRTC. Key methods:

| Method | Purpose |
|--------|---------|
| `initialize(state)` | Initialize WebRTC with token, channel, user info |
| `disconnect()` | End the session |
| `setIsVideoOn/setIsScreenShareOn/setIsAudioOn(state)` | Toggle media |
| `updateVideoDeviceId/updateAudioInputDeviceId/...` | Change devices |
| `updateProfile(user)` | Update user profile |
| `setMute/setDeafen/setHandRaised(state)` | User state |
| `setTheme(theme)` | Change UI theme |
| `setNoiseGateThreshold/setDenoisePower(value)` | Audio processing |
| `setScreenQualityMode(mode)` | Screen share quality |
| `setPreferredCodecs(codecs)` | Codec preference |
| `setTileVolume/setOutputVolume/setInputVolume(value)` | Volume controls |
| `customizeVolumeBooster(settings)` | Volume booster config |
| `kick(userId)` | Kick a user |
| `receivePacket/receiveRawPacket(data)` | Data channel packets |
| `nativeLoopbackAudioStarted/receiveNativeLoopbackAudioData/...` | Loopback audio |

See [Bridge Reference](plugins/BRIDGE_REFERENCE.md) for the full method catalog with
parameter types and interception examples.

---

### src/types/plugin.ts -- Plugin Interfaces

**File**: `src/types/plugin.ts`

```typescript
export interface Author {
  name: string;
  id?: string;
}

export interface Patch {
  bridge: "nativeToWebRtc" | "webRtcToNative";
  method: string;
  before?(args: unknown[]): boolean | void | Promise<boolean | void>;
  after?(result: unknown, args: unknown[]): void | Promise<void>;
  replace?(...args: unknown[]): unknown | Promise<unknown>;
}

export type SettingField =
  | { type: "boolean"; default: boolean; description: string }
  | { type: "string"; default: string; description: string }
  | { type: "number"; default: number; description: string; min?: number; max?: number }
  | { type: "select"; default: string; description: string; options: string[] };

export interface SettingsDefinition {
  [key: string]: SettingField;
}

export interface UprootedPlugin {
  name: string;
  description: string;
  version: string;
  authors: Author[];
  start?(): void | Promise<void>;
  stop?(): void | Promise<void>;
  patches?: Patch[];
  css?: string;
  settings?: SettingsDefinition;
}
```

Patch priority: `replace` > `before` > `after`. A `replace` patch cancels the original
call and provides the return value. A `before` patch can cancel by returning `false`.

---

### src/types/root.ts -- Window Augmentation

**File**: `src/types/root.ts`

Extends the global `Window` with Root's runtime globals and Uprooted's injected properties:

```typescript
declare global {
  interface Window {
    // Root's bridge globals (set by DotNetBrowser)
    __nativeToWebRtc: INativeToWebRtc;
    __webRtcToNative: IWebRtcToNative;
    __mediaManager: IMediaManager;     // getDevices(kind?) -> Promise<string>
    __rootApiBaseUrl: string;
    __rootSdkBridgeWebToNative: Record<string, (...args: unknown[]) => unknown>;

    // Uprooted injections
    __UPROOTED_SETTINGS__: UprootedSettings;
    __UPROOTED_VERSION__: string;
    __UPROOTED_LOADER__: PluginLoader;
  }
}
```

---

### src/types/settings.ts -- Settings Interfaces

**File**: `src/types/settings.ts`

```typescript
export interface PluginSettings {
  enabled: boolean;
  config: Record<string, unknown>;
}

export interface UprootedSettings {
  enabled: boolean;                          // Global kill switch
  plugins: Record<string, PluginSettings>;   // Per-plugin settings by name
  customCss: string;                         // Global custom CSS
}

export const DEFAULT_SETTINGS: UprootedSettings = {
  enabled: true,
  plugins: {},    // Empty = all plugins default to enabled
  customCss: "",
};
```

When `plugins[name]` is not present, `PluginLoader` defaults `enabled` to `true`
(see `pluginLoader.ts` line 99).

---

## Build System

**File**: `scripts/build.ts`

Uses [esbuild](https://esbuild.github.io/) to produce two output files.

### esbuild Configuration (line 45)

```typescript
const ctx = await esbuild.context({
  entryPoints: [path.join(SRC, "core", "preload.ts")],
  bundle: true,
  format: "iife",
  globalName: "Uprooted",
  outfile: path.join(DIST, "uprooted-preload.js"),
  platform: "browser",
  target: "chrome120",
  sourcemap: true,
  define: {
    __UPROOTED_VERSION__: JSON.stringify(/* from package.json */),
  },
  external: ["node:fs", "node:path"],
});
```

| Option | Value | Rationale |
|--------|-------|-----------|
| `format` | `"iife"` | Self-executing, no module system needed |
| `globalName` | `"Uprooted"` | Exposes exports on `window.Uprooted` |
| `platform` | `"browser"` | Browser APIs, no Node.js polyfills |
| `target` | `"chrome120"` | DotNetBrowser's Chromium version |
| `external` | `["node:fs", "node:path"]` | CLI-only modules excluded from bundle |

### CSS Collection (line 18)

`collectPluginCss()` recursively walks `src/plugins/` for `.css` files and concatenates
them with source path comments.

### Watch Mode

```bash
pnpm build --watch    # ctx.watch() for incremental rebuild
```

CSS collection runs once at startup. New CSS files require a restart.

### Output

```
dist/
  uprooted-preload.js      # IIFE bundle
  uprooted-preload.js.map  # Source map
  uprooted.css              # Combined plugin CSS
```

Loaded by `file:///` URLs from injected HTML tags.

### Build Commands

```bash
pnpm build              # Production build
npx tsx scripts/build.ts # Direct invocation
```
