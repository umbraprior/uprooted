# How Uprooted Works: From Zero to Injected UI

A complete walkthrough of how we reverse-engineered Root Communications desktop app and built a client mod framework that injects custom UI into it at runtime. Every step, from opening the binary for the first time to seeing "UPROOTED" in the settings sidebar.

> **Related docs:**
> [Index](INDEX.md) | [Architecture](ARCHITECTURE.md) | [Hook Reference](HOOK_REFERENCE.md) | [TypeScript Reference](TYPESCRIPT_REFERENCE.md) | [CLR Profiler](CLR_PROFILER.md)

---

## Table of Contents

1. [What We're Looking At](#1-what-were-looking-at)
2. [Cracking Open the Binary](#2-cracking-open-the-binary)
3. [Finding the Embedded Browser](#3-finding-the-embedded-browser)
4. [The Source Map Breakthrough](#4-the-source-map-breakthrough)
5. [Mapping the Bridge Protocol](#5-mapping-the-bridge-protocol)
6. [Extracting Tokens from Memory](#6-extracting-tokens-from-memory)
7. [Reverse-Engineering the gRPC API](#7-reverse-engineering-the-grpc-api)
8. [Understanding Root's Theme System](#8-understanding-roots-theme-system)
9. [First Attempt: Binary Patching (Legacy)](#9-first-attempt-binary-patching-legacy)
10. [The Real Approach: CLR Profiler Injection](#10-the-real-approach-clr-profiler-injection)
11. [Building the Managed Hook](#11-building-the-managed-hook)
12. [Waiting for Avalonia to Load](#12-waiting-for-avalonia-to-load)
13. [Walking the Visual Tree](#13-walking-the-visual-tree)
14. [Injecting the Sidebar Section](#14-injecting-the-sidebar-section)
15. [Building Content Pages](#15-building-content-pages)
16. [Phase 3.5: The Theme Engine](#16-phase-35-the-theme-engine)
17. [The TypeScript Layer: Browser-Side Plugins](#17-the-typescript-layer-browser-side-plugins)
18. [Bridge Proxies: Intercepting IPC](#18-bridge-proxies-intercepting-ipc)
19. [Putting It All Together: The Install Flow](#19-putting-it-all-together-the-install-flow)
20. [What Breaks and Why](#20-what-breaks-and-why)

---

## 1. What We're Looking At

Root Communications is a desktop chat/community app. Think Discord, but smaller. The desktop client is a single 617 MB executable:

```
C:\Users\<user>\AppData\Local\Root\current\Root.exe
```

It stores profile data, cached web content, and embedded browser resources at:

```
C:\Users\<user>\AppData\Local\Root Communications\Root\profile\default\
```

That profile directory contains 3,646 files (1.18 GB) including HTML pages, JavaScript bundles, CSS, images, and DotNetBrowser's Chromium cache.

Our goal: inject custom UI (settings pages, sidebar sections, plugin system) into this app without modifying the binary, without breaking anything, and without Root even knowing we're there.

---

## 2. Cracking Open the Binary

### What Root.exe actually is

Root.exe is a **self-contained .NET 10 application** using **Avalonia UI 11.3.10** (a cross-platform WPF-like framework). "Self-contained" means the entire .NET runtime, all managed assemblies, and all native dependencies are bundled into that single 617 MB file. At launch, the runtime extracts assemblies to memory rather than to disk.

We confirmed this by:

- Checking the PE headers and finding .NET metadata
- Scanning the binary for assembly manifest strings (`Avalonia.Controls`, `Avalonia.Base`, etc.)
- Finding the `.runtimeconfig.json` embedded in the binary: `"framework": { "name": "Microsoft.NETCore.App", "version": "10.0.0" }`
- Identifying 10 companion DLLs in the install directory, including `uiohook.dll` (keyboard/mouse hook library, 709 KB)

### Code signing

```
Subject:    CN="Root Communications, Inc."
Issuer:     CN=Microsoft ID Verified CS AOC CA 02
Thumbprint: 79DF21E0E0C3FFD923977F943788B751BA5290CB
```

All executables and DLLs are Authenticode-signed. But this doesn't matter for our purposes because .NET does not verify Authenticode at P/Invoke load time — and we're injecting via the CLR profiler API, not by replacing DLLs.

---

## 3. Finding the Embedded Browser

### DotNetBrowser discovery

Root doesn't use a standard WebView2 or CEF for its web content. It uses **DotNetBrowser**, a commercial Chromium-based browser control for .NET. We found this by exploring the profile directory:

```
...\profile\default\DotNetBrowser\
...\profile\default\DotNetBrowser\RootApps\Bundle\Host\index.html
```

The host `index.html` is a bare iframe container with no CSP (Content Security Policy) and no sandbox attributes. Inside it, Root loads **7 sub-applications** as React/Vite bundles in iframes:

| Sub-App | Framework | Description |
|---------|-----------|-------------|
| Hexatris | React/Vite | Multiplayer game |
| Polls | React/Vite | Community polls |
| Suggestions | React/Vite | Suggestion board |
| Minecraft Easy Setup | React/Vite | Server management |
| Task Tracker | React/Vite | Task management |
| Stickerwall | Alpine.js + PixiJS | Sticker board |
| Raid Planner | React/Vite | Raid scheduling (80k+ lines) |

Plus a **WebRTC bundle** for voice and video:

```
...\profile\default\WebRtcBundle\
  rootapp-desktop-webrtc.js       (4.2 MB, 13,930 lines)
  rootapp-desktop-webrtc.js.map   (13.5 MB)  <-- THE JACKPOT
```

### The IPC architecture

Root.exe communicates with the embedded Chromium via:

1. **Proprietary binary IPC** on a dynamic localhost port (e.g., `localhost:49212`) — 4 loopback connections
2. **JavaScript bridge objects** injected into the browser's `window` scope:
   - `window.__rootSdkBridgeWebToNative` — sub-app bridge (send protobuf, search users, etc.)
   - `window.__rootSdkBridgeNativeToWeb` — sub-app inbound bridge
   - `window.__nativeToWebRtc` — host-to-WebRTC bridge (42 methods)
   - `window.__webRtcToNative` — WebRTC-to-host bridge (28 methods)

We probed the DotNetBrowser IPC port (TCP connection, HTTP, WebSocket upgrade, Chrome DevTools Protocol) but it uses a proprietary binary protocol. CDP endpoints (`/json/version`, `/json/list`) returned nothing — DevTools is disabled.

---

## 4. The Source Map Breakthrough

This is where everything opened up.

### Finding the source map

In the WebRTC bundle directory, alongside the 4.2 MB minified JavaScript file, sat a 13.5 MB source map:

```
rootapp-desktop-webrtc.js.map    14,181,758 bytes
```

This file maps every character in the minified bundle back to the original TypeScript source. It was shipped to every user's machine as part of the profile data. No obfuscation, no encryption, no integrity check.

### Extracting the source

We wrote `source_map_extract.py` to parse the source map and dump all original files to disk. The map contained **802 source files**:

```python
# source_map_extract.py (simplified)
import json, os

with open("rootapp-desktop-webrtc.js.map") as f:
    sm = json.load(f)

for i, (path, content) in enumerate(zip(sm["sources"], sm["sourcesContent"])):
    # Normalize webpack paths: remove "webpack://", collapse "../"
    clean = path.replace("webpack://", "").lstrip("./")
    out = os.path.join("src_dump", clean)
    os.makedirs(os.path.dirname(out), exist_ok=True)
    with open(out, "w", encoding="utf-8") as f:
        f.write(content or "")
```

We extracted 328 security-relevant files (first-party code + key node_modules). The result:

```
src_dump/Libs/JS/rootapp-desktop-webrtc/
├── src/                           # Original TypeScript
│   ├── services/
│   │   └── nativeToWebRtcBridge.ts    # 65 bridge commands
│   ├── types/
│   │   └── baseWebRtcBridge.ts        # Full bridge interfaces
│   ├── redux/
│   │   └── bridgeMiddleware.ts        # Redux middleware
│   └── mocks/
│       └── mockBridge.ts              # Mock with HARDCODED TOKEN
└── node_modules/@rootplatform/apiclient-internal/
    └── dist/grpc_client/base/webapi/
        ├── services/              # 27 gRPC service client stubs
        ├── requests/              # Request message definitions
        ├── responses/             # Response message definitions
        └── permission.js          # 22 channel + 14 community permission enums
```

We now had the complete original TypeScript source for the WebRTC layer, full gRPC client definitions for all 27 backend services (163 methods), and every protobuf message schema. The permission module contains 21 channel and 14 community permission enums.

---

## 5. Mapping the Bridge Protocol

### What the bridges expose

The extracted `baseWebRtcBridge.ts` revealed two interfaces with 70 combined methods:

**`INativeToWebRtc`** (41 methods) — .NET host controlling the browser:
```typescript
initialize(params: InitializeParams): void     // Start WebRTC session
kick(userId: string): void                     // Kick user from call
updateMyPermission(perms: Permissions): void   // Permission escalation
receiveRawPacket(buffer: ArrayBuffer): void    // Inject signaling packets
updateProfile(profile: UserProfile): void      // Spoof user profile
setTheme(theme: ThemeConfig): void             // Set UI theme
// ... 35 more methods
```

**`IWebRtcToNative`** (29 methods) — browser notifying .NET host:
```typescript
kickPeer(userId: string): void                 // Kick any user
setAdminMute(deviceId: string, muted: boolean): void
setAdminDeafen(deviceId: string, deaf: boolean): void
setSpeaking(speaking: boolean, deviceId: string, userId: string): void
// ... 25 more methods
```

Zero authorization checks on any bridge method. Any code running in the DotNetBrowser context has full bridge access.

### The mock bridge (hardcoded credentials)

The extracted `mockBridge.ts` (and the production bundle at line 13929) contained a hardcoded Bearer token appearing **4 times**:

```javascript
token = new tM("AChObn-FjgGkgtSzdaVi0wAsrDJSU4oSpE_mAVP-Hid2s1K_...");
baseUrl = "https://localhost:3005";
```

Plus hardcoded UUIDs:
```javascript
communityId = "ACiNMcK2gwKa7z5MDT_ScQ";
channelId   = "ACiNMcK3iQSUPvZBKshscQ";
deviceId    = "ACysMlJTihKkT-YBU_4eJw";
userId      = "AChObn-FjgGkgtSzdaVi0w";
```

The mock bridge activates when the user-agent doesn't contain `"rootplatform"` — meaning if you load the WebRTC bundle in a normal browser, it hands you a ready-to-use authenticated session.

---

## 6. Extracting Tokens from Memory

### Token format

We reverse-engineered the Bearer token structure from the source map and binary analysis:

```
128 bytes, base64url-encoded to ~172 characters:
  Bytes  0-15:   userId UUID   (16 bytes)
  Bytes 16-31:   deviceId UUID (16 bytes)
  Bytes 32-127:  Cryptographic signature (96 bytes)
```

The signature prevents forgery, but the token has no expiry and no binding to IP or device fingerprint. Steal it, and you have full API access until the user explicitly refreshes.

### Memory scanning

Root stores the token in plaintext in process memory. We built `memory_scan.py` using Windows APIs:

```python
import ctypes
from ctypes import wintypes

# Open Root.exe process
kernel32 = ctypes.WinDLL("kernel32")
handle = kernel32.OpenProcess(0x0010 | 0x0020, False, pid)  # QUERY + READ

# Walk committed memory regions
mbi = MEMORY_BASIC_INFORMATION()
addr = 0
while kernel32.VirtualQueryEx(handle, addr, ctypes.byref(mbi), ctypes.sizeof(mbi)):
    if mbi.State == 0x1000 and mbi.Protect in (0x02, 0x04, 0x20, 0x40):
        # Read region and search for base64url tokens (168-180 chars)
        buf = (ctypes.c_char * mbi.RegionSize)()
        kernel32.ReadProcessMemory(handle, addr, buf, mbi.RegionSize, None)
        # regex search for [A-Za-z0-9_-]{168,180}
        # decode, validate 128-byte length, parse userId UUID
    addr += mbi.RegionSize
```

This found tokens at addresses like `0x20bc04ad0d6`. We validated each extracted token by making a `GetSelf` gRPC call to the production API — if it returned a username and email, the token was live.

### Other extraction methods attempted

| Method | Result |
|--------|--------|
| DPAPI decryption of AuthToken file | Failed — couldn't determine entropy value |
| DotNetBrowser Chromium storage (LevelDB, SQLite) | No tokens found — stored server-side via DPAPI |
| Named pipe interception | Works — captures token during bridge `initialize()` call |
| Process memory scan | Works — finds plaintext tokens reliably |

---

## 7. Reverse-Engineering the gRPC API

### Protocol discovery

From the extracted source, we learned Root's backend uses **gRPC-web over HTTPS**:

```
POST https://api.rootapp.com/root.v2.<ServiceName>/<Method>
Content-Type: application/grpc-web+proto
Authorization: Bearer <token>
Body: [5-byte gRPC frame header] + [protobuf binary]
```

The gRPC frame header is: `0x00` (no compression) + 4-byte big-endian length.

### UUID encoding

The trickiest part was UUID encoding. Root uses a non-standard approach:

```
Protobuf message UUID {
  fixed64 high64 = 1;  // First 8 bytes of UUID, big-endian
  fixed64 low64  = 2;  // Last 8 bytes of UUID, big-endian
}
```

We built `grpc_lib.py` with encoding/decoding primitives:

```python
def uuid_pb(uuid_str):
    """Encode UUID as two fixed64 fields (big-endian byte order)."""
    b = uuid.UUID(uuid_str).bytes  # 16 bytes, big-endian
    high = struct.pack("<q", int.from_bytes(b[0:8], "big"))   # to little-endian wire
    low  = struct.pack("<q", int.from_bytes(b[8:16], "big"))
    return f_fixed64(1, high) + f_fixed64(2, low)
```

### What we found

27 services, 163 methods defined in the source. Active testing against production revealed 32 actually implemented endpoints across 8 services. The rest return gRPC status 12 (UNIMPLEMENTED).

---

## 8. Understanding Root's Theme System

### Dual theme system

Root uses two separate theme systems simultaneously:

**1. CSS variables (Chromium side)**

25 CSS custom properties per theme, applied via `data-theme` attribute:

```css
/* Dark theme (extracted from binary at offset 0x12AE9676) */
:root[data-theme="dark"] {
  --rootsdk-brand-primary: #3B6AF8;
  --rootsdk-brand-secondary: #A8FF5D;
  --rootsdk-background-primary: #0D1521;
  --rootsdk-background-secondary: #111D2E;
  --rootsdk-text-primary: #F2F2F2;
  /* ... 20 more variables */
}
```

**2. AXAML resources (Avalonia/.NET side)**

Compiled Avalonia XAML themes embedded in the binary:

```
Light.axaml:    Offset 0x19EED01F  (UTF-16LE, ~28.5 KB)
Dark.axaml:     Offset 0x19EF3FB0  (UTF-16LE, ~21.5 KB)
PureDark.axaml: Offset 0x19EF93D8  (UTF-16LE, ~196 B)
```

The key discovery: **native Avalonia AXAML contains no color brush resources**. All UI colors come from CSS variables injected into the Chromium webview. The native side is styled with hardcoded hex colors in the code, not theme resources.

This means to theme the native Avalonia UI, we'd need to patch binary color values — but to theme the web UI, we just override CSS variables. Much easier.

---

## 9. First Attempt: Binary Patching (Legacy)

Before we built the hook system, we tried direct binary patching. The legacy scripts in `legacy/` show this approach:

### How it worked

`apply_full_theme.py` ran 8 patch phases:

1. **Phase A**: Replace CSS hex color strings in the binary (outside Fluent theme blocks)
2. **Phase B**: Patch BAML (Binary XAML) color resources in compiled assemblies
3. **Phase C**: Replace ARGB/BGRA byte patterns for native colors
4. **Phase D**: Remove CSS `var()` wrappers (bypass server-side theme override)
5. **Phase E**: Patch JavaScript bundle hex colors (theme objects, inline styles)
6. **Phase F**: Replace Canvas `rgb()`/`rgba()` colors (WebRTC overlays)
7. **Phase G**: Inject CSS into all `*.css` files
8. **Phase H**: Patch IL BGRA constants (Avalonia sidebar/nav colors)

### The process

```
1. Kill Root.exe
2. Restore from backup (Root_APP_BACKUP_20260212_172827)
3. Apply all 8 phases via regex + binary search-replace
4. Relaunch Root
5. Theme the DWM title bar via DwmSetWindowAttribute API
```

### Why we abandoned it

- **Destructive**: Modifies Root.exe and supporting files directly
- **Fragile**: Breaks on every Root update (binary layout changes)
- **Static**: No runtime behavior — can't respond to user actions
- **Theme-only**: Can't add custom UI, plugins, or bridge interception
- **Risky**: No clean rollback if something goes wrong mid-patch

We needed a better approach.

---

## 10. The Real Approach: CLR Profiler Injection

### The idea

.NET provides a **CLR Profiler API** — a documented interface for performance profilers to hook into the runtime. A profiler DLL gets loaded before any managed code runs and receives callbacks for events like method compilation, module loading, and garbage collection.

The key insight: the profiler can **modify IL (Intermediate Language) bytecode** before it's JIT-compiled. We can inject our own code into any method in the app.

### Building the profiler

We wrote `uprooted_profiler.dll` in C, implementing the `ICorProfilerCallback` COM interface. The profiler goes through a precise sequence: register via environment variables, guard against non-Root processes, wait for a suitable module with proper TypeRef metadata, create cross-module MemberRef tokens for `Assembly.LoadFrom` and `Assembly.CreateInstance`, then prepend 26 bytes of IL into the first available method body. That IL loads our managed DLL inside a try/catch wrapper so Root continues normally if anything fails.

When the runtime JIT-compiles the targeted method, our injection runs first, loading `UprootedHook.dll` and creating an instance of `UprootedHook.Entry`. Root's original method body runs immediately after, completely unaware.

For the full implementation -- environment variable setup, process guard logic, module selection criteria, metadata token creation, IL byte sequences, and the `SetILFunctionBody` call -- see [CLR Profiler Reference](CLR_PROFILER.md).

### The result

When Root.exe launches:
1. .NET runtime loads `uprooted_profiler.dll` as a profiler
2. Profiler waits for a suitable module to load
3. Profiler injects IL into the first available method
4. When that method is called, our managed DLL loads
5. Our managed code starts a background thread
6. Root continues loading normally -- the user sees nothing

---

## 11. Building the Managed Hook

`UprootedHook.dll` is a .NET 10 class library with dual entry mechanisms:

```csharp
public class Entry
{
    private static int _initialized = 0;

    [ModuleInitializer]
    internal static void ModuleInit()
    {
        if (Interlocked.CompareExchange(ref _initialized, 1, 0) == 0)
            StartupHook.Initialize();
    }

    public Entry()
    {
        if (Interlocked.CompareExchange(ref _initialized, 1, 0) == 0)
            StartupHook.Initialize();
    }
}
```

`[ModuleInitializer]` runs when the assembly is loaded (before `CreateInstance` returns). The constructor is a fallback in case `ModuleInitializer` doesn't fire. `Interlocked.CompareExchange` ensures we only initialize once, even if both paths trigger.

The entry point performs a process name guard (bail out if we're not in Root.exe), then spawns a background thread named `"Uprooted-Injector"`. We must never block Root's startup -- that would cause a visible freeze or crash. The background thread runs through the phased startup sequence described in the next section.

For the complete process guard implementation and thread management details, see [Hook Reference](HOOK_REFERENCE.md).

---

## 12. Waiting for Avalonia to Load

Root's Avalonia UI doesn't exist yet when our code runs. The profiler injects us very early -- before Avalonia assemblies are even loaded. We need to wait through multiple phases, each with its own timeout and failure mode.

### Phase 0: Verify HTML patches

Before we even think about Avalonia, the hook runs a filesystem-only check. `HtmlPatchVerifier` scans all target HTML files in Root's profile directory to confirm our `<script>` and `<link>` tags are still present. Root's auto-updater can silently overwrite these files, stripping our injections. If any patches are missing, Phase 0 re-applies them in place.

The verifier then starts a `FileSystemWatcher` on the profile directory, held alive by a static reference for the lifetime of the process. If Root overwrites an HTML file while the app is running, the watcher detects it and re-patches within seconds -- a self-healing mechanism that prevents the TypeScript layer from silently disappearing.

Phase 0 is non-fatal: if it fails (missing directories, permission errors), the hook logs the error and continues into the Avalonia phases. The native sidebar will still work; only the browser-side plugins would be affected.

### Phase 1: Wait for Avalonia assemblies (30s timeout)

Poll every 250ms for the `Avalonia.Controls` assembly to appear in the AppDomain. Single-file .NET 10 apps load assemblies lazily from memory, so we have to wait for Root to actually reference Avalonia types.

### Phase 2: Resolve all Avalonia types via reflection

This is the critical step. Because Root is a single-file app, `Type.GetType()` with assembly-qualified names does not work -- assemblies aren't on disk. We solve this by scanning all loaded assemblies:

```csharp
var avaloniaTypes = new Dictionary<string, Type>();

foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
{
    if (!asm.GetName().Name?.StartsWith("Avalonia") == true)
        continue;

    foreach (var type in asm.GetTypes())
        avaloniaTypes[type.FullName!] = type;
}

// Now we can look up any Avalonia type by name:
Type textBlockType = avaloniaTypes["Avalonia.Controls.TextBlock"];
```

This gives us ~80 cached types, their properties, and their methods. All Avalonia control creation for the rest of the hook goes through this reflection cache. Direct `typeof()` is never used for Avalonia types.

Key gotcha: `DispatcherPriority` is a struct, not an enum, in Avalonia 11+. We handle this with a fallback chain trying static properties, static fields, `Enum.Parse`, and `Activator.CreateInstance` in sequence.

### Phase 3: Wait for Application.Current (30s timeout)

Avalonia's `Application.Current` is set after the app instance is created. We poll until it becomes non-null.

### Phase 4: Wait for MainWindow (60s timeout)

The main window is created after `Application.Current`. We navigate through `Application.Current` -> `ApplicationLifetime` -> `MainWindow` via reflection, polling until the window reference materializes.

Once we have the `MainWindow`, we start the sidebar monitoring timer and proceed to Phase 3.5 (theme engine initialization).

For the full phase implementation with code, timeouts, and error handling, see [Hook Reference](HOOK_REFERENCE.md).

---

## 13. Walking the Visual Tree

Root's settings page has no stable selectors -- no element IDs, no CSS classes, no automation IDs. We can't just call `FindControl("settingsSidebar")`. We have to discover the layout by its **structure**.

`VisualTreeWalker.FindSettingsLayout()` runs a 6-step algorithm: depth-first search for a `TextBlock` with text `"APP SETTINGS"`, walk up to find a `StackPanel` with >= 8 children (the nav container), walk up again to find a `Grid` with >= 2 column definitions (the layout grid), identify the content area in the adjacent column, locate the `ListBox` inside the nav container, and find the back button by searching for a `TextBlock` containing `"<"`.

The result is a `SettingsLayout` object with references to the nav container, content panel, ListBox, back button, and layout grid -- everything we need to inject our own section.

For the complete traversal algorithm, column detection logic, and the `SettingsLayout` data structure, see [Hook Reference](HOOK_REFERENCE.md).

---

## 14. Injecting the Sidebar Section

`SidebarInjector` uses a `Timer` that fires every 200ms. Each tick dispatches to the UI thread and performs a lightweight alive check -- search for the `"APP SETTINGS"` TextBlock. If found and we haven't injected yet, proceed. If not found (settings page closed), clean up.

When the settings page opens, we inject four things: an `"UPROOTED"` section header matching Root's `"APP SETTINGS"` style, three clickable nav items ("Uprooted", "Plugins", "Themes") with hover and active states, a version text inside Root's existing grey version box, and re-ordering of Root's elements so our section sits between the settings items and the version/sign-out controls.

The most important pattern is the **overlay approach**: we never replace `ContentControl.Content` (that causes a UI freeze via `OnDetachedFromVisualTreeCore` deadlock). Instead, we add our content page as a sibling in the layout Grid with matching `Grid.Column`/`Grid.Row` and an opaque background, covering Root's content via z-order. When the user clicks a Root sidebar item, we remove our overlay.

Cleanup is equally critical. The back button gets a `PointerPressed` handler that calls `CleanupInjection()` before Root's handler fires. Everything we inject is tagged with `Control.Tag = "uprooted-injected"` so we can find and remove our own controls without touching Root's.

For the injection sequence, event handler wiring, cleanup logic, and the overlay pattern implementation, see [Hook Reference](HOOK_REFERENCE.md).

---

## 15. Building Content Pages

### The challenge

We're building native Avalonia UI through pure reflection. No XAML, no designer, no compile-time types. Every control is created like this:

```csharp
// Create a TextBlock
var tb = Activator.CreateInstance(textBlockType);
textProperty.SetValue(tb, "Hello World");
fontSizeProperty.SetValue(tb, 14.0);
foregroundProperty.SetValue(tb, ParseBrush("#fff2f2f2"));
```

It's verbose, but it works and it's the only option when you can't reference Avalonia assemblies at compile time.

### What the pages look like

**Uprooted page**: Framework info card with version, description, and status indicators. Matches Root's card styling exactly (background `#0f1923`, corner radius 12, border `#19ffffff` thickness 0.5, inner padding 24px).

**Plugins page**: Lists registered plugins with name, version, author, and enabled/disabled badges. Currently display-only.

**Themes page**: Shows available themes with color preview swatches, name, description, and "ACTIVE" badge on the current theme. Currently display-only.

All pages are wrapped in a `ScrollViewer` and use the same spacing and typography as Root's native settings pages. The goal is that a user can't tell where Root's UI ends and ours begins.

For the page builder code and Root style matching details, see [Hook Reference](HOOK_REFERENCE.md).

---

## 16. Phase 3.5: The Theme Engine

### Why CSS wasn't enough

The TypeScript layer can retheme Root's web UI by overriding `--rootsdk-*` CSS variables (see [Section 8](#8-understanding-roots-theme-system)). But Root's desktop app has native Avalonia controls that CSS can't reach: the title bar, the sidebar navigation, window chrome, and various overlay panels. A CSS-only approach left these elements stuck in Root's default dark blue, breaking the visual coherence of any custom theme.

We needed to theme the native Avalonia layer from C# at runtime.

### The approach: resource dictionary injection

The discovery from Section 8 -- that Root's AXAML themes contain no color brush resources and instead use hardcoded hex colors in code -- meant we couldn't just swap a resource dictionary and call it done. The theme engine takes a two-pronged approach:

1. **Direct resource override**: Root's `Application.Styles[0].Resources` contains the active `SimpleTheme` resources (`ThemeAccentColor`, `ThemeAccentBrush`, etc.). The theme engine saves the original values, then writes replacement colors directly into this dictionary. These resources aren't overridden by `MergedDictionaries`, so direct writes are the only way.

2. **Injected ResourceDictionary**: For standard Fluent theme keys that Root doesn't override, the engine creates a new `ResourceDictionary` and adds it to `Application.Resources.MergedDictionaries`. This covers controls styled by Avalonia's built-in theme rather than Root's custom resources.

3. **Title bar**: On Windows 11, the engine calls `DwmSetWindowAttribute(DWMWA_CAPTION_COLOR)` via P/Invoke to match the title bar to the active theme's background color.

The engine maintains a persistent map from any theme replacement color back to Root's original, so switching themes or reverting doesn't lose track of what the original colors were -- even after multiple theme changes.

For the full `ThemeEngine` implementation, resource key lists, color mapping logic, and revert behavior, see [Hook Reference](HOOK_REFERENCE.md).

---

## 17. The TypeScript Layer: Browser-Side Plugins

The C# hook handles native Avalonia UI. But Root also has an entire web-based UI running in the embedded Chromium. For that, we have a TypeScript injection layer.

### How it gets in

The `patcher.ts` script (run via `pnpm install-root`) modifies Root's profile HTML files:

```html
<!-- Before: -->
<head>
  <!-- Root's existing tags -->
</head>

<!-- After: -->
<head>
  <!-- Root's existing tags -->
  <script><!-- uprooted -->window.__UPROOTED_SETTINGS__={...};</script>
  <script src="file:///C:/.../uprooted-preload.js"><!-- uprooted --></script>
  <link rel="stylesheet" href="file:///C:/.../uprooted.css"><!-- uprooted -->
</head>
```

Target files include `WebRtcBundle/index.html` (voice/video) and all 7 sub-app `RootApps/*/index.html` files. The `<!-- uprooted -->` comment is our marker for detection and cleanup.

Phase 0 of the hook (see [Section 12](#12-waiting-for-avalonia-to-load)) ensures these patches survive Root's auto-updater.

### The preload script

`preload.ts` runs before Root's own JavaScript bundles. It reads settings from `window.__UPROOTED_SETTINGS__` (inlined by the patcher), installs bridge proxies, creates a `PluginLoader`, registers built-in plugins, and starts all enabled plugins.

### The plugin system

Plugins implement a simple interface:

```typescript
interface UprootedPlugin {
  name: string;
  description: string;
  version: string;
  authors: Author[];

  start?(): void | Promise<void>;
  stop?(): void | Promise<void>;

  patches?: Patch[];    // Bridge method intercepts
  css?: string;         // CSS injected while active
}
```

Built-in plugins include **themes** (CSS variable overrides) and **settings-panel** (DOM-based settings injection in the web UI).

### Settings persistence

Root runs Chromium with `--incognito`, wiping `localStorage` on every launch. We persist settings to a JSON file at `%LOCALAPPDATA%\Root Communications\Root\profile\default\uprooted-settings.json` and inline them into the HTML at patch time.

For the full TypeScript architecture, plugin API, preload sequence, and settings system, see [TypeScript Reference](TYPESCRIPT_REFERENCE.md).

---

## 18. Bridge Proxies: Intercepting IPC

### The mechanism

Root assigns bridge objects to `window.__nativeToWebRtc` and `window.__webRtcToNative`. We wrap them with ES6 Proxies before Root's code can use them. The proxy intercepts every method call, emits a `BridgeEvent` through the plugin loader, and lets plugins inspect, modify, or cancel the call before it reaches Root.

### Deferred installation

Root may assign bridge globals after our preload runs. We handle this with `Object.defineProperty`:

```typescript
let _nativeToWebRtc: any = window.__nativeToWebRtc;

Object.defineProperty(window, "__nativeToWebRtc", {
  get: () => _nativeToWebRtc,
  set: (value) => {
    _nativeToWebRtc = createBridgeProxy(value, "nativeToWebRtc");
  },
  configurable: true,
});
```

When Root assigns `window.__nativeToWebRtc = realBridge`, our setter fires, wraps it in a Proxy, and stores the wrapped version. All subsequent access gets the proxied bridge.

### What plugins can do with this

```typescript
// Example: Log every theme change
patches: [{
  bridge: "nativeToWebRtc",
  method: "setTheme",
  before(args) {
    console.log("[Uprooted] Theme changing to:", args[0]);
  },
}]

// Example: Replace kick behavior
patches: [{
  bridge: "webRtcToNative",
  method: "kickPeer",
  replace(userId) {
    console.log("[Uprooted] Blocked kick attempt on:", userId);
    return undefined;  // Silently swallow the kick
  },
}]
```

For the full proxy implementation and plugin patch API, see [TypeScript Reference](TYPESCRIPT_REFERENCE.md).

---

## 19. Putting It All Together: The Install Flow

### What the installer does

The install process has three layers: build the TypeScript bundle (`pnpm build`), build the C# hook (`dotnet build hook/ -c Release`), deploy files and set environment variables, then patch HTML files. Environment variables are user-scoped and persist across reboots. `DOTNET_ReadyToRun=0` is critical -- it forces JIT compilation so our profiler gets a chance to modify IL.

For installer implementation details (Tauri/Rust detection, file deployment, environment variable management), see [Installer Reference](INSTALLER.md). For end-user install/uninstall instructions, see [Installation Guide](INSTALLATION.md).

### The boot sequence

After installation, every time Root.exe launches:

```
1.  Windows starts Root.exe
2.  .NET runtime sees CORECLR_ENABLE_PROFILING=1
3.  Runtime loads uprooted_profiler.dll as CLR profiler
4.  Profiler checks process name -> "Root"
5.  Profiler waits for a suitable module to load
6.  Profiler injects IL into first available method
7.  Injected IL calls Assembly.LoadFrom("UprootedHook.dll")
8.  UprootedHook.dll loads -> Entry.ModuleInit() fires
9.  StartupHook.Initialize() -> background thread starts
10. Phase 0: HtmlPatchVerifier checks/repairs HTML patches + starts FileSystemWatcher
11. Phase 1: Wait for Avalonia assemblies
12. Phase 2: Resolve all types via reflection
13. Phase 3: Wait for Application.Current
14. Phase 3.5: Initialize theme engine, apply saved theme
15. Phase 4: Wait for MainWindow
16. SidebarInjector.StartMonitoring() -> 200ms timer begins
17. Root finishes loading, user sees the app

     ... user opens Settings ...

18. Timer tick finds "APP SETTINGS" TextBlock
19. VisualTreeWalker discovers layout structure
20. SidebarInjector injects UPROOTED section
21. User sees "Uprooted", "Plugins", "Themes" in sidebar

     ... meanwhile, in Chromium ...

22. DotNetBrowser loads WebRtcBundle/index.html
23. <script> tag loads uprooted-preload.js before Root's bundles
24. Bridge proxies installed on window.__nativeToWebRtc
25. Theme plugin starts, overrides CSS variables
26. Web UI is themed + bridge calls are interceptable
```

### Uninstall

Uninstall reverses everything: remove environment variables, restore HTML files from `.uprooted.bak` backups, delete installed DLLs, clean up settings. Root.exe is never modified. Uninstall leaves zero traces.

---

## 20. What Breaks and Why

### Things that will break on Root updates

| Change | Impact |
|--------|--------|
| Root renames "APP SETTINGS" text | Visual tree walker can't find anchor -> no injection |
| Root changes settings Grid layout | Content area detection fails -> pages render in wrong position |
| Root changes CSS variable names | Theme engine targets wrong variables -> themes don't apply |
| Root moves to AOT compilation | Profiler IL injection doesn't work -> hook never loads |
| Root moves HTML to different path | Patcher can't find files -> TypeScript layer not injected |
| Root adds HTML integrity checks | Patcher modifications detected -> app refuses to load pages |
| Root switches from DotNetBrowser | Bridge globals don't exist -> proxy installation fails |
| Root renames bridge globals | Proxy targets wrong property names -> interception fails |
| Root uses Object.freeze on bridges | Proxy can't wrap frozen objects -> interception fails |

### Known limitations

1. **C# settings can't load JSON** -- `System.Text.Json` throws `MissingMethodException` in the profiler-injected context. Settings use INI-based persistence instead.
2. **Theme switching is display-only** -- the Themes page shows themes but clicking them does nothing yet.
3. **Plugin management is display-only** -- can't toggle plugins from the native UI yet.
4. **Environment variables are user-scoped** -- the profiler env vars affect ALL .NET apps. We guard with a process name check, but the vars are still set globally.
5. **No DevTools** -- Root's Chromium has no remote debugging port. TypeScript errors show as a red banner; there's no console or inspector.

---

## Summary

We went from a closed 617 MB binary to a fully injected mod framework by:

1. **Analyzing the binary** -- discovered .NET 10 / Avalonia / DotNetBrowser
2. **Finding the source map** -- 802 original TypeScript files with full gRPC schemas
3. **Reverse-engineering the protocol** -- understood bridge IPC + gRPC-web + protobuf encoding
4. **Scanning process memory** -- extracted live authentication tokens
5. **Trying binary patching** -- worked but too fragile and destructive
6. **Building a CLR profiler** -- native C DLL that injects IL before any managed code
7. **Writing a managed hook** -- C# reflection-based Avalonia UI injection
8. **Building a theme engine** -- native Avalonia resource dictionary injection + DWM title bar
9. **Creating browser-side plugins** -- TypeScript ES6 Proxy bridge interception + CSS theme engine
10. **Packaging it all** -- Tauri/Rust installer, env vars, HTML patching, clean uninstall

Two independent injection layers. Zero binary modifications. Full cleanup on uninstall. The user sees "UPROOTED" in their settings sidebar, and Root doesn't know we're there.
