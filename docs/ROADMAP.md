# Roadmap

Project direction, known issues, and planned features for Uprooted.

> **Related docs:** [Index](INDEX.md) | [Architecture](ARCHITECTURE.md) | [Contributing Technical](CONTRIBUTING_TECHNICAL.md)

---

## Known Issues

Issues are organized by severity. Critical issues block core functionality; high issues risk breakage on upstream updates; medium issues affect reliability or developer experience.

### Critical

**System.Text.Json broken in profiler context**
`System.Text.Json` causes `MissingMethodException` when used inside the CLR profiler-injected hook. This prevents JSON deserialization in the C# layer, so the hook cannot read `uprooted-settings.json`. Native Avalonia UI (themes page, plugins page) returns hardcoded defaults only and cannot reflect actual user configuration. Settings persistence from the C# side is currently disabled.
- Files: `hook/UprootedSettings.cs`
- Workaround in progress: manual INI-style parsing (partially implemented)

**Environment variables affect all .NET apps**
CLR profiler environment variables (`CORECLR_ENABLE_PROFILING`, `CORECLR_PROFILER`, etc.) are user-scoped and persistent. They apply to every .NET process the user launches, not just Root. The profiler has a process name guard that returns `E_FAIL` for non-Root processes, but other .NET apps still incur a slight startup overhead from the profiler loading and unloading.
- Files: `installer/src-tauri/src/hook.rs`, install scripts
- Mitigation: Process name guard in `tools/uprooted_profiler.c`

### High

**AvaloniaReflection brittleness**
The reflection cache (`hook/AvaloniaReflection.cs`, ~815 lines) assumes specific Avalonia type names, property names, and method signatures. Any Avalonia version update that renames or removes types will break all UI injection. The entire C# layer becomes non-functional if the reflection cache cannot resolve its targets.
- Files: `hook/AvaloniaReflection.cs`
- Recommendation: Add Avalonia version detection and per-feature graceful degradation

**Settings page text-based detection fragile**
The sidebar injector locates the settings page by searching the visual tree for the exact text "APP SETTINGS". If Root renames this label, native UI injection silently fails with no error visible to the user.
- Files: `hook/VisualTreeWalker.cs`, `hook/SidebarInjector.cs`
- Recommendation: Add structural fallback detection that does not depend on text anchors

### Medium

**Settings panel DOM discovery fragile**
Browser-side settings panel injection uses TreeWalker text matching and multiple fallback strategies (flex-row detection, grid detection, sibling size detection). If Root changes both the primary selectors and all fallbacks, injection fails silently. No distinction is made between "not on settings page" and "on settings page but structure unrecognized."
- Files: `src/plugins/settings-panel/panel.ts`

**XHR redirect to about:blank**
Sentry XHR blocking redirects intercepted requests to `about:blank`, which causes browser warnings and leaves the XMLHttpRequest in an undefined state. Technically succeeds but is semantically incorrect.
- Files: `src/plugins/sentry-blocker/index.ts`

**MutationObserver debounce timing**
Settings panel injection uses an 80ms debounce on MutationObserver callbacks. Rapid DOM mutations during Root's page load can still trigger repeated injection attempts, causing visible lag.
- Files: `src/plugins/settings-panel/panel.ts`

**`after` patch handler not implemented**
The `after` callback is defined in the `Patch` type interface but the plugin loader does not invoke it at runtime. Plugins defining `after` handlers get no behavior -- the handler is silently ignored.
- Files: `src/types/plugin.ts`, `src/core/pluginLoader.ts`

**Browser-side settings not persisted to disk**
Runtime changes made via the browser-side settings panel only update the in-memory `window.__UPROOTED_SETTINGS__` object. The browser layer (running in DotNetBrowser's incognito Chromium) has no file system access to write changes back. Settings changed at runtime take effect immediately but revert on Root restart. The installer/CLI can write settings to disk, so initial configuration persists -- only runtime UI changes are session-only.
- Files: `src/plugins/settings-panel/`, `src/core/settings.ts`

**Shallow settings merge**
Settings loading merges user settings with defaults using a shallow object spread. Nested objects (like `plugins`) get completely replaced rather than merged. If a user's saved settings contain only some plugin entries, other plugins lose their defaults.
- Files: `src/core/settings.ts`

---

## Short-term Goals

Next release. Focused on completing core functionality that is currently stubbed or broken.

### Fix C# settings persistence
Replace `System.Text.Json` usage with manual INI-style parsing in the hook layer. Partial implementation already exists in `hook/UprootedSettings.cs`. This unblocks the native Avalonia settings pages from reflecting actual user configuration.

### Implement theme click handlers in native UI
The native Avalonia Themes page shows themes with "ACTIVE" badges but has no click handlers. Add theme selection behavior so users can switch themes from the native settings UI.
- Files: `hook/ContentPages.cs`, `hook/ThemeEngine.cs`

### Add plugin toggle functionality in native UI
The native Plugins page lists discovered plugins but cannot toggle them on or off. Wire up toggle controls to the settings persistence layer.
- Files: `hook/ContentPages.cs`, `hook/UprootedSettings.cs`

### Implement `after` patch handler
The plugin loader currently ignores `after` callbacks defined in plugin patches. Implement post-execution invocation so plugins can observe return values and side effects.
- Files: `src/core/pluginLoader.ts`, `src/types/plugin.ts`

### Implement deep merge for settings
Replace the shallow spread merge in settings loading with a recursive merge that correctly combines nested objects (especially the `plugins` map).
- Files: `src/core/settings.ts`

---

## Medium-term Goals

Next 2-3 releases. Expanding the platform and improving the developer/user experience.

### Plugin marketplace / repository
A discoverable listing of community plugins. Could be a static registry (JSON manifest in a GitHub repo) or a simple web interface. Plugin authors would submit metadata; users would browse and install from within Uprooted.

### Auto-update mechanism
Detect new Uprooted versions and offer in-app updates. Needs to handle hook DLL replacement while Root is not running, HTML re-patching, and TypeScript bundle updates.

### Linux support improvements
The standalone bash installer (`install-uprooted-linux.sh`) covers basic installation. Improve detection reliability, test across distributions, and ensure the native profiler (`tools/uprooted_profiler_linux.c`) handles Linux-specific edge cases.

### Error recovery in plugin lifecycle
If `plugin.start()` rejects, the plugin is currently logged as errored but may be left in an inconsistent state. Add rollback logic and a `starting`/`stopping` guard set to prevent race conditions from rapid start/stop calls.
- Files: `src/core/pluginLoader.ts`

### Bridge version detection
Add negotiation between Uprooted and Root's bridge interface (`__nativeToWebRtc`, `__webRtcToNative`). Detect bridge version at runtime and warn or degrade gracefully if the interface has changed.

---

## Long-term Vision

Bigger-picture goals that define where the project is headed.

### Panel rearrangement / layout customization
Allow users to rearrange Root's UI panels: move the server/community list to the left or right side, reposition the notifications panel, adjust top/bottom placement of various UI elements. This would require deep DOM manipulation of Root's Avalonia-hosted Chromium layout and likely a dedicated layout engine plugin. The C# hook layer may also need modifications to support native panel repositioning.
- Originally suggested by MrEizy

### Full settings bidirectional sync
Bridge the gap between browser-side settings changes and disk persistence. Either expose a write channel from the browser layer through the C# hook (via the native bridge), or implement a periodic sync mechanism. Goal: runtime settings changes survive Root restarts.

### Version compatibility matrix
Maintain a documented mapping of which Uprooted versions support which Root versions. Add runtime version detection so Uprooted can warn users when running against an untested Root version.

### Community plugin ecosystem
Beyond the marketplace, build out tooling for plugin authors: a CLI scaffolding tool, automated testing harness against a mock Root environment, documentation generator from plugin metadata, and a contribution pipeline for vetted community plugins.

### Sentry blocker hardening
Expand coverage to handle additional transport mechanisms (WebSocket, image beacons) that future Sentry SDK versions may introduce. Consider intercepting at the `Sentry.init()` level for more comprehensive blocking.

---

## Security Hardening

Items derived from security research on Root Communications v0.9.86. Uprooted ships inside the same process and browser context as Root, so these findings directly affect how Uprooted handles tokens, validates input, delivers updates, and protects user privacy.

These items are tracked internally.

### Token handling improvements

Root stores bearer tokens in process memory (H17), on disk without DPAPI (H18, M5), and in sessionStorage as plaintext JSON (M10). Uprooted's bridge proxy intercepts calls that carry these tokens. Hardening work:

- **Avoid retaining tokens in Uprooted's own scope.** Bridge proxy handlers should forward tokens without caching them in closures or module-level variables. Audit `src/api/bridge.ts` to confirm no token values persist beyond the call boundary.
- **Scrub token values from log output.** Console logging in plugin lifecycle and bridge interception must never print raw bearer tokens. Add a log sanitizer that detects the 128-byte token format and replaces it with a truncated fingerprint.
- **Warn users about AuthToken file exposure.** The installer or settings panel should inform users that `Store\AuthToken` is readable without elevation and is not protected by DPAPI. Consider documenting how users can restrict ACLs on this file.

### Input validation enhancements

Several Root findings involve injection vectors (C5 open redirect via `restart(url)`, H1 `encodeURI()` parameter injection, H3 innerHTML XSS via DevBar, M11 URI scheme pass-through). Uprooted can mitigate some of these for its own users:

- **Validate URLs passed through Uprooted APIs.** Any plugin API that accepts a URL (theme CDN, plugin manifest, etc.) must reject `javascript:`, `data:`, `file:`, and `vbscript:` schemes. Implement a shared `validateUrl()` utility in `src/api/`.
- **Sanitize plugin-provided HTML.** Plugins that inject HTML into the settings panel or DOM overlays should pass through a sanitizer. Consider bundling a lightweight sanitizer (or a minimal allowlist-based approach) rather than relying on `textContent` alone.
- **Enforce scheme allowlists on bridge proxy.** If Uprooted adds bridge method interception for `restart()` or navigation calls, enforce an allowlist (`https:`, `http:`, `root:`) and block dangerous schemes before they reach the native layer.

### Update mechanism security

Uprooted plans an auto-update system (Medium-term Goals). Root's own update mechanism has known weaknesses (M7 unsigned manifests, M8 no certificate pinning). Uprooted must avoid repeating these:

- **Sign release manifests with an asymmetric key.** The update manifest (version list, download URLs, checksums) must be signed with a private key held offline. The client verifies the signature with an embedded public key before downloading anything.
- **Verify binary integrity after download.** Downloaded hook DLLs, TypeScript bundles, and profiler binaries must be checked against SHA-256 hashes listed in the signed manifest. Reject any file whose hash does not match.
- **Pin TLS certificates for the update channel.** If updates are served from a known domain, pin the expected certificate chain to prevent MITM substitution. At minimum, pin the root CA.
- **Fail closed on verification failure.** If signature verification, hash verification, or TLS pinning fails, the update must be rejected entirely. Never apply a partially-verified update.

### Privacy protections

Root leaks PII to Sentry (H4 `sendDefaultPii: true`, H5 TURN/ICE credentials, M16 session replay at 25% error rate) and exposes user data through various channels. Uprooted already blocks Sentry via the sentry-blocker plugin. Additional hardening:

- **Expand sentry-blocker transport coverage.** Monitor for Sentry SDK updates that introduce WebSocket or image beacon transports beyond the current fetch/XHR/sendBeacon interception. Consider intercepting at the `Sentry.init()` level for comprehensive blocking.
- **Block session replay recording.** Sentry's session replay (M16) captures video-like DOM recordings. If the sentry-blocker cannot suppress replay initialization, add a dedicated replay interceptor that stubs the recording API.
- **Strip PII from bridge traffic logging.** If Uprooted adds bridge call logging for debugging, ensure user IDs, device IDs, IP addresses, and TURN credentials are redacted before any output.
- **Document privacy posture.** Create a user-facing document explaining what Uprooted blocks (Sentry telemetry), what it does not block (Effects SDK WASM loading, update checks), and what data Uprooted itself collects (none, currently).

---

## Technical Debt

Items from the automated codebase analysis that are not yet tracked elsewhere in this roadmap. These range from type safety gaps to architectural concerns that will cause maintenance burden as the project grows.

These items are tracked internally.

### Settings persistence (System.Text.Json limitation)

Already tracked in Known Issues (Critical) and Short-term Goals. Additional debt items related to settings:

- **Settings file format migration path.** The INI-style workaround in `hook/UprootedSettings.cs` is a stopgap. Define a migration strategy for when System.Text.Json is eventually resolved (new .NET host, separate process, or alternative parser). The INI format should be forward-compatible with the eventual JSON format.
- **Shallow merge in browser-side settings.** Already tracked in Short-term Goals. The additional debt: ensure that the deep merge implementation handles arrays (plugin lists) correctly and does not duplicate entries on repeated merges.

### Code quality items

These items from CONCERNS.md are not yet on the roadmap:

- **Non-null assertion casts in sentry-blocker.** The `originalFetch!.call()` pattern in `src/plugins/sentry-blocker/index.ts` risks crashes if `stop()` is called while requests are in-flight. Add runtime null guards before using cached originals.
- **`any[]` typing for XMLHttpRequest parameters.** The rest parameters in `src/plugins/sentry-blocker/index.ts` lose type information. Type them as `[async?: boolean, username?: string, password?: string]` per the MDN XMLHttpRequest.open() spec.
- **MutationObserver circular triggering.** The settings panel observer watches the entire document body, including its own injected elements. Scope the observer to the sidebar subtree only, and add a guard to skip callbacks when the mutation source is a `data-uprooted` element.
- **Theme variable cleanup duplication.** `src/plugins/themes/index.ts` maintains two separate lists of CSS variable names for cleanup. Consolidate into a single source of truth computed once at module load.
- **Event listener memory leaks.** Sidebar click listeners and item handlers in `src/plugins/settings-panel/panel.ts` accumulate without cleanup on repeated settings page visits. Switch to event delegation on a stable parent element and clean up in `stopObserving()`.
- **Window global exposure.** `window.__UPROOTED_LOADER__` and `window.__UPROOTED_SETTINGS__` are accessible to any script in the page context. Freeze the settings object with `Object.freeze()` to prevent tampering, and consider restricting loader access to plugin management only.

### Linter and formatter configuration

No linter or formatter is configured for the TypeScript layer. Code style currently relies on TypeScript strict mode and manual review. Add:

- ESLint with a strict TypeScript ruleset (or Biome as a faster alternative)
- Prettier (or Biome formatting) for consistent formatting
- Pre-commit hooks via husky or lefthook to enforce on every commit
- CI check to reject unformatted code

---

## Testing Improvements

The TypeScript layer has zero test coverage. No test framework, no test files, no test scripts. The C# layer has a manual test harness and two unit test files. This section tracks the plan to close these gaps.

These items are tracked internally.

### Test framework setup

- **Install Vitest** as the TypeScript test runner. It has native ESM support (the codebase uses ES modules), fast in-memory execution, and minimal configuration. Add `test`, `test:watch`, and `test:coverage` scripts to `package.json`.
- **Adopt co-located test files** using the `.test.ts` suffix: `src/core/pluginLoader.test.ts`, `src/api/bridge.test.ts`, etc.
- **Set initial coverage targets**: 60% statements, 50% branches, 70% functions. These are starting targets, not ceilings.

### Unit test expansion targets

High-priority modules that need test coverage first, in order:

1. **PluginLoader** (`src/core/pluginLoader.ts`) -- Registration, start/stop lifecycle, patch installation, event emission, multi-plugin handler chaining, concurrent start/stop race conditions.
2. **Settings** (`src/core/settings.ts`) -- JSON parsing, merge with defaults, deep merge for nested plugin configs, error recovery from corrupted settings files, file path resolution.
3. **Bridge proxy** (`src/api/bridge.ts`) -- Proxy creation, event interception, call cancellation, return value replacement, deferred setup via `Object.defineProperty`, independent proxying of both bridge objects.
4. **Sentry blocker** (`src/plugins/sentry-blocker/index.ts`) -- Fetch blocking for `*.sentry.io`, XHR redirect behavior, sendBeacon interception, blocked count accuracy, concurrent request handling.
5. **Theme engine** (`src/plugins/themes/index.ts`) -- Color math functions (`darken()`, `lighten()`, `luminance()`), custom variable generation, CSS variable application and cleanup, invalid theme name handling.
6. **CSS injection** (`src/api/css.ts`) -- Style element creation, ID prefixing, targeted removal vs batch removal, isolation from non-Uprooted styles.
7. **DOM utilities** (`src/api/dom.ts`) -- `waitForElement()` immediate resolution, timeout rejection, observer cleanup, `nextFrame()` scheduling.

### Integration test needs

These scenarios require multiple modules working together and are harder to unit test in isolation:

- **Full plugin lifecycle**: Register a plugin, start it (patches install, CSS injects, lifecycle hooks fire), verify bridge interception works, stop it (patches uninstall, CSS removes, cleanup runs), verify no residual state.
- **Settings round-trip**: Load settings from a file, modify via the settings API, verify the in-memory state is correct and that re-loading produces the same result.
- **Preload initialization sequence**: Settings check, plugin registration, `startAll()` execution, error banner display on fatal errors, `window.__UPROOTED_VERSION__` availability.

### Manual test procedures to automate

These are currently verified by manual inspection with console logging. Converting them to automated checks would catch regressions earlier:

- **Sentry blocker count verification**: Start the blocker, simulate Sentry requests via fetch/XHR/sendBeacon, verify the blocked count matches expected values.
- **Theme application verification**: Apply a theme, verify that the expected CSS variables are set on `document.documentElement`, switch themes, verify old variables are removed and new ones applied.
- **Settings panel injection**: Mock a DOM structure resembling Root's settings page, trigger the MutationObserver, verify that Uprooted's sidebar items appear in the correct location.

### C# hook test expansion

The existing `tests/UprootedTests/` project covers ColorUtils and GradientBrush. Additional targets:

- **UprootedSettings INI parsing**: Test the INI-style parser with valid files, missing keys, malformed lines, and empty files. This can be tested without Root's runtime.
- **PlatformPaths resolution**: Test path resolution logic across different platform configurations. Mock environment variables and verify correct path construction.
- **Logger robustness**: Verify that the Logger class swallows its own exceptions and never crashes the host process, even when the log directory is missing or the disk is full.

---

## Version Compatibility Tracking

Uprooted injects into a closed-source application that updates independently. Tracking version compatibility is essential for diagnosing user issues and planning for breaking changes.

### Root version compatibility

| Uprooted Version | Root Versions Tested | Status | Notes |
|-------------------|---------------------|--------|-------|
| 0.1.92 (current) | v0.9.86 | Active | Primary development target |

Maintain this matrix as new Root versions are released. When a user reports a bug, the first diagnostic question should be which Root version they are running.

**Monitoring approach:**
- Check for Root updates weekly (installer.rootapp.com serves Velopack manifests)
- When a new Root version is detected, test core injection (HTML patching, profiler loading, bridge proxy, settings panel) before declaring compatibility
- Document any breaking changes discovered during testing

### .NET version requirements

Uprooted's C# hook layer targets .NET 10, matching Root's runtime. Key dependencies:

- **CLR profiler API**: The profiler DLL (`tools/uprooted_profiler.c`) uses the ICorProfilerCallback interface. This API is stable across .NET versions but the profiler GUID and COM interfaces must match the runtime version.
- **Reflection targets**: `hook/AvaloniaReflection.cs` resolves types by name from loaded assemblies. A .NET runtime version change could alter assembly loading order or available types.
- **If Root upgrades .NET version**: Rebuild the profiler DLL against the new CoreCLR headers. Test that the hook DLL loads correctly in the new runtime. Verify all reflection targets still resolve.

### Avalonia version dependencies

Root uses Avalonia 11.x for its native UI. The C# hook depends on specific Avalonia type names, property names, and method signatures:

- **Current target**: Avalonia 11 (specific minor version unknown, inferred from API surface)
- **Critical types**: `ContentControl`, `StackPanel`, `Grid`, `TextBlock`, `Border`, `ScrollViewer`, `Button`, `ToggleSwitch`, `ComboBox`, and approximately 70 others cached in `AvaloniaReflection.cs`
- **Known risk**: `DispatcherPriority` is a struct (not an enum) in Avalonia 11+. Code that treats it as an enum will fail silently.
- **Detection plan**: Add Avalonia assembly version detection at hook startup. Log the detected version. If the version differs from the tested version, log a warning and consider entering a degraded mode that skips native UI injection but leaves HTML patching and TypeScript injection intact.

### Breaking change monitoring

Proactive detection of changes that could break Uprooted:

- **Root HTML structure changes**: The `HtmlPatchVerifier` (Phase 0) already detects when HTML patches are missing and self-heals. Monitor its repair frequency -- if repairs happen on every launch, Root is overwriting patches on update and the install mechanism needs adjustment.
- **Root bridge API changes**: Add version negotiation for `__nativeToWebRtc` and `__webRtcToNative` (already in Medium-term Goals as "Bridge version detection"). Log the set of available bridge methods at startup and compare against the expected set.
- **Root settings page structure changes**: The sidebar injector searches for "APP SETTINGS" text. Add a secondary structural detection method that does not depend on text content, so injection survives label changes.
- **Avalonia API surface changes**: If Root ships a new Avalonia version, the reflection cache may fail to resolve types. Add per-type resolution failure handling so that individual features degrade rather than the entire hook failing.

---

## Feature Requests

Have an idea for Uprooted? We track feature requests as GitHub issues.

- **Suggest a feature:** Open a [feature request](https://github.com/watchthelight/uprooted/issues/new?template=feature-request.yml)
- **Report a bug:** Open a [bug report](https://github.com/watchthelight/uprooted/issues/new?template=bug-report.yml)
- **Discuss ideas:** Start a conversation in [GitHub Discussions](https://github.com/watchthelight/uprooted/discussions) (if enabled)

When suggesting a feature, include:
- A clear description of the behavior you want
- Why it would be useful (what problem does it solve?)
- Any technical considerations you are aware of
- Whether you would be willing to contribute an implementation

---

*Last updated: 2026-02-16*
