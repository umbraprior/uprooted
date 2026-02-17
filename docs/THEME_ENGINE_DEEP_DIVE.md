# Theme Engine Deep Dive

> **Related docs:** [Hook Reference](HOOK_REFERENCE.md) | [Architecture](ARCHITECTURE.md) | [Avalonia Patterns](AVALONIA_PATTERNS.md) | [TypeScript Reference](TYPESCRIPT_REFERENCE.md)

For the overview, see [Hook Reference](HOOK_REFERENCE.md#theme-engine).

---

## Table of Contents

1. [Overview](#overview)
2. [Resource Dictionary Injection](#resource-dictionary-injection)
3. [Theme Palette Format](#theme-palette-format)
4. [Live Preview System](#live-preview-system)
5. [Tree Fingerprinting](#tree-fingerprinting)
6. [Color Audit Algorithm](#color-audit-algorithm)
7. [Custom Theme Generation](#custom-theme-generation)
8. [Revert Mechanics](#revert-mechanics)
9. [CSS Variable Bridge](#css-variable-bridge)
10. [Error Handling](#error-handling)
11. [Key Data Structures](#key-data-structures)
12. [Performance Considerations](#performance-considerations)

---

## Overview

**File:** `hook/ThemeEngine.cs` (2218 lines)

The theme engine is the largest single component in the Uprooted hook layer. It
transforms Root Communications' default dark-blue UI into arbitrary color schemes at
runtime, without patching any files on disk and without restarting the application.

Root is an Avalonia 11 desktop app. Avalonia distributes colors through two mechanisms:

1. **Resource dictionaries** -- named `Color` and `SolidColorBrush` values resolved at
   render time by controls that use `DynamicResource` bindings (e.g.,
   `{DynamicResource ThemeAccentBrush}`).
2. **Hardcoded ARGB values** -- controls created in C# or XAML with literal brush values
   that do not participate in the resource system (e.g., `Background="#0D1521"`).

The theme engine must defeat both mechanisms. For resource-bound controls, it overrides
the dictionaries so that existing `DynamicResource` bindings resolve to new colors.
For hardcoded controls, it walks the live visual tree and physically replaces brush
objects on each node.

### Role in the Dual-Layer Architecture

Uprooted has two layers: a C# hook (native Avalonia manipulation) and a TypeScript
injection (CSS/DOM manipulation inside the embedded Chromium browser). The theme engine
is a C# component that operates entirely on the native Avalonia side. It communicates
with the TypeScript layer indirectly -- when a theme is applied, `ContentPages` updates
its static color fields, and the TypeScript theme plugin reads CSS variables injected
into the browser DOM. See [CSS Variable Bridge](#css-variable-bridge) for details.

### Five-Phase Application

Theme application proceeds through five phases in `ApplyThemeInternal`
(`hook/ThemeEngine.cs:365-586`):

| Phase | What it does | Target |
|-------|-------------|--------|
| 1 | Override `Styles[0].Resources` | Root's custom theme keys |
| 2 | Inject `MergedDictionary` into `Application.Resources` | Standard FluentTheme keys |
| 3 | Build visual tree color maps with cross-mapping | Hardcoded ARGB controls |
| 4 | Immediate walk + continuous 500ms timer + `LayoutUpdated` hook | All visual tree nodes |
| 5 | Set DWM title bar color via Win32 API | Windows title bar |

---

## Resource Dictionary Injection

### Where Root Stores Its Colors

Root's Avalonia application uses two resource locations:

- **`Application.Styles[0].Resources`** -- Root's custom theme keys. This is where
  keys like `ThemeAccentColor`, `ThemeAccentBrush`, `ThemeForegroundLowColor`, and
  `HighlightForegroundColor` live. These are the keys that Root's own controls bind
  to via `DynamicResource`. They are NOT overridden by `MergedDictionaries` -- they
  must be written directly.

- **`Application.Resources.MergedDictionaries`** -- Standard Avalonia FluentTheme keys
  like `SystemAccentColor`, `TextFillColorPrimary`, `ControlFillColorDefault`, etc.
  Adding a `ResourceDictionary` here overrides these system-level defaults.

### Phase 1: Styles[0] Override Algorithm

(`hook/ThemeEngine.cs:379-439`)

For each key-value pair in the theme palette:

```
for each (key, hex) in palette:
    1. Save original value:
       - If key not yet saved AND not yet tracked as added:
         - Try to read current value via _r.GetResource(styleRes, key)
         - If value exists: save to _savedOriginals[key]
         - If value is null: add key to _addedKeys (for removal on revert)
    2. Determine type:
       - If key contains "Brush" OR ends with "Fill": create SolidColorBrush
       - Otherwise: create Color struct
    3. Write via _r.AddResource(styleRes, key, value)
```

The type detection heuristic (`hook/ThemeEngine.cs:408`) is critical. Avalonia's
resource system is strongly typed -- setting a `SolidColorBrush` where a `Color` is
expected (or vice versa) causes a silent binding failure. The naming convention is
reliable: Root follows the pattern where Brush keys contain `"Brush"` and Fill keys
end with `"Fill"`.

### Phase 2: MergedDictionary Injection

(`hook/ThemeEngine.cs:441-493`)

```
1. Get Application.Resources via _r.GetAppResources()
2. Get MergedDictionaries list via _r.GetMergedDictionaries(resources)
3. Create new ResourceDictionary via _r.CreateResourceDictionary()
4. For each (key, hex) in palette:
   a. If isBrush: create SolidColorBrush, add to dict
   b. If isColor: create Color, add to dict
      ALSO auto-generate: create SolidColorBrush, add as key + "Brush"
5. mergedDicts.Add(dict)
6. Store dict reference in _injectedDict for later removal
```

The auto-generation step (`hook/ThemeEngine.cs:474-479`) is important: when a Color
key like `SystemAccentColor` is added, a corresponding `SystemAccentColorBrush` is
also created. This covers controls that bind to the Brush variant of a Color key.

### Reflection Mechanics

All resource operations go through `AvaloniaReflection` because Uprooted cannot
reference Avalonia assemblies at compile time. The key methods:

- `_r.CreateResourceDictionary()` -- `Activator.CreateInstance` on the cached
  `ResourceDictionaryType`
- `_r.AddResource(dict, key, value)` -- Calls the indexer setter or `Add` method
  via reflection
- `_r.GetResource(dict, key)` -- Calls the indexer getter via reflection
- `_r.CreateBrush(hex)` -- Creates `SolidColorBrush` via parameterless constructor
  plus Color property setter (the `SolidColorBrush(Color)` constructor is trimmed)
- `_r.ParseColor(hex)` -- Calls `Color.Parse(string)` via cached `MethodInfo`

---

## Theme Palette Format

### Preset Theme Structure

Each preset theme is a `Dictionary<string, string>` mapping resource key names to hex
color strings. The two presets are defined in the static `Themes` dictionary
(`hook/ThemeEngine.cs:2029-2217`):

**Crimson** (`hook/ThemeEngine.cs:2031-2128`): 55 keys, accent `#C42B1C` (deep red)
**Loki** (`hook/ThemeEngine.cs:2130-2215`): 55 keys, accent `#2A5A40` (moss green)

### Key Categories

The 55+ keys fall into these categories:

| Category | Example Keys | Count |
|----------|-------------|-------|
| Root custom theme | `ThemeAccentColor`, `ThemeAccentBrush`, `ThemeForegroundLowColor` | 16 |
| System accent | `SystemAccentColor`, `SystemAccentColorDark1..3`, `SystemAccentColorLight1..3` | 7 |
| Text fill | `TextFillColorPrimary`, `TextFillColorSecondary`, `TextFillColorTertiary`, `TextFillColorDisabled` | 4 |
| Control fill | `ControlFillColorDefault`, `ControlFillColorSecondary`, `ControlFillColorTertiary`, `ControlFillColorDisabled` | 4 |
| Solid backgrounds | `SolidBackgroundFillColorBase`, `...Secondary`, `...Tertiary`, `...Quarternary` | 4 |
| Card/layer | `CardBackgroundFillColorDefault`, `LayerFillColorDefault`, `LayerFillColorAlt` | 4 |
| Accent fill brushes | `AccentFillColorDefaultBrush`, `...SecondaryBrush`, `...TertiaryBrush`, `...DisabledBrush` | 4 |
| Strokes | `ControlStrokeColorDefault`, `CardStrokeColorDefault`, `SurfaceStrokeColorDefault` | 4 |
| Buttons | `ButtonBackground`, `ButtonBackgroundPointerOver`, `ButtonBackgroundPressed`, `ButtonBackgroundDisabled` | 4 |
| ListBox | `ListBoxItemBackgroundPointerOver`, `...Pressed`, `...Selected`, `...SelectedPointerOver`, `...SelectedPressed` | 5 |
| ToggleSwitch | `ToggleSwitchFillOn`, `...PointerOver`, `...Pressed` | 3 |
| ScrollBar | `ScrollBarThumbFill`, `...PointerOver`, `...Pressed` | 3 |
| TextControl | `TextControlBackgroundFocused`, `TextControlBorderBrushFocused` | 2 |
| Selection | `TextSelectionHighlightColor` | 1 |

### Naming Convention for Type Detection

The engine determines whether to create a `SolidColorBrush` or a `Color` struct
based on the key name (`hook/ThemeEngine.cs:408`):

```csharp
bool isBrush = key.Contains("Brush") || key.EndsWith("Fill");
```

Keys like `ThemeAccentBrush` and `ToggleSwitchFillOn` become brushes. Keys like
`ThemeAccentColor` and `SystemAccentColor` become Color structs. For Color keys in
the MergedDictionary, a Brush variant is auto-generated.

### Tree Color Maps

Separate from the resource palette, each theme has a tree color map in the static
`TreeColorMaps` dictionary (`hook/ThemeEngine.cs:865-958`). These map hardcoded ARGB
color strings (as they appear in `Color.ToString()` output) to replacement colors.

Each map covers approximately 25 color mappings:

- Blue accent variants (primary through semi-transparent)
- Structural dark backgrounds (main, darker, near-black, panel, chat)
- Dark borders (primary, darker, gray)
- Semi-transparent text
- Solid text

**Critical constraint:** Every replacement value must be unique within its map. The
reverse map (replacement -> original) would lose data if two originals shared a
replacement. This is enforced by hand for presets and by algorithm for custom themes
(see [Custom Theme Generation](#custom-theme-generation)).

---

## Live Preview System

### Architecture

The live preview system (`UpdateCustomThemeLive`, `hook/ThemeEngine.cs:170-359`) enables
real-time theme updates as the user drags the color picker. It is fundamentally different
from a full `ApplyTheme` call: it skips `RevertTheme()`, skips audits, and updates
resources and the color map in-place rather than tearing down and rebuilding.

### Throttling

(`hook/ThemeEngine.cs:186-188`)

```csharp
long now = Environment.TickCount64;
if (now - _lastLiveUpdateTick < 16) return;
_lastLiveUpdateTick = now;
```

Updates are throttled to a maximum of once per 16ms (~60fps). This prevents the UI
thread from being overwhelmed during rapid color picker drags.

### Bootstrap Guard

(`hook/ThemeEngine.cs:177-183`)

If the custom theme is not yet fully active (no `_injectedDict` or `_activeThemeName`
is not `"custom"`), the method falls through to a full `ApplyCustomTheme()` call first.
This handles the case where the user opens the color picker before ever applying a
custom theme.

### Three-Phase Update

**Phase 1: Update Styles[0].Resources in-place** (`hook/ThemeEngine.cs:200-221`)

Regenerates the full palette from the new colors, then iterates every key and writes
the new value directly into `Styles[0].Resources`. No save/restore of originals --
the originals are already saved from the initial `ApplyCustomTheme` bootstrap.

**Phase 2: Replace MergedDictionary contents** (`hook/ThemeEngine.cs:224-249`)

Same iteration but writing into the existing `_injectedDict`. This is an in-place
update, not a remove-and-readd. The dictionary object stays in the
`MergedDictionaries` list.

**Phase 3: Immediate tree walk** (`hook/ThemeEngine.cs:341-354`)

```csharp
_liveBrushCache = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
var sw = System.Diagnostics.Stopwatch.StartNew();
int liveRecolored = WalkAllWindows();
sw.Stop();
_liveBrushCache = null;
```

A full visual tree walk runs synchronously on the UI thread. The `_liveBrushCache`
is active during this walk -- see [Performance Considerations](#performance-considerations).

### Cross-Mapping for Intermediate Colors

The most subtle part of live preview is the cross-mapping logic
(`hook/ThemeEngine.cs:252-339`). When the user drags the color picker, controls
already show colors from the *previous* live update, not Root's original colors. The
tree walker needs to find these intermediate colors and map them to the new colors.

The algorithm builds a `combinedMap` with three layers:

1. **Base map** -- the new theme's tree color map (Root original -> new replacement)
2. **Previous-map cross-mapping** (`hook/ThemeEngine.cs:261-274`) -- for each entry in
   the previous `_activeColorMap`, if the original has a new replacement, add
   `previousReplacement -> newReplacement`. This catches controls showing the
   previous live update's colors.
3. **Root-originals cross-mapping** (`hook/ThemeEngine.cs:278-286`) -- for each entry in
   `_rootOriginals`, if the stale replacement color has a new mapping, add
   `staleReplacement -> newReplacement`. This catches colors from themes applied
   two or more switches ago.

Additionally, Uprooted's own UI elements (`ContentPages` static colors) are tracked:

```csharp
var oldAccent = NormalizeArgb(ContentPages.AccentGreen);
// ... capture all old values ...
ContentPages.UpdateLiveColors(accentHex, bgHex, palette);
var newAccent = NormalizeArgb(ContentPages.AccentGreen);
// ... add old->new mappings for each ...
```

(`hook/ThemeEngine.cs:298-331`)

This ensures that Uprooted's own settings page UI updates in real time along with
Root's native UI.

### Undo/Revert During Preview

There is no explicit undo for live preview drags. The user can:

1. Type a different hex value in the text box to jump to any color.
2. Apply a preset theme to abandon the custom theme.
3. Close and reopen the settings page (which triggers a full page rebuild).

The live preview state is always consistent because each update regenerates the full
palette and tree map from the raw accent/background hex values.

---

## Tree Fingerprinting

### Purpose

The tree fingerprint (`ComputeTreeFingerprint`, `hook/ThemeEngine.cs:698-722`)
provides a lightweight structural hash of the visual tree. It detects when Root
navigates to a different view (channels, communities, settings, etc.), which causes
new controls to be created with Root's original colors.

### Algorithm

```
function ComputeTreeFingerprint(mainWindow):
    hash = 0
    count = 0
    for each level-1 child c1 of mainWindow:
        hash = hash * 31 + c1.GetType().Name.GetHashCode()
        for each level-2 child c2 of c1:
            count++
            for each level-3 child c3 of c2:
                count++
                for each level-4 child c4 of c3:
                    count++
    return hash XOR (count * 997)
```

The fingerprint walks exactly 4 levels deep. It hashes type names at level 1 (which
captures the major structural components like panels and content controls) and counts
nodes at levels 2-4 (which captures the density of the subtree). The XOR with
`count * 997` mixes the structural hash with the population count.

### Walk Triggers

The fingerprint is not the primary walk trigger. Instead, three mechanisms work in
concert:

1. **Continuous timer** (`ScheduleVisualTreeWalks`, `hook/ThemeEngine.cs:599-624`):
   fires every 500ms, first walk at 200ms after theme apply. This is the background
   safety net that catches any changes the other mechanisms miss.

2. **LayoutUpdated interceptor** (`InstallLayoutInterceptor`, `hook/ThemeEngine.cs:631-666`):
   hooks into `MainWindow.LayoutUpdated` via `SubscribeEvent`. This fires on every
   layout pass, which happens when Root navigates (switches channels, opens settings,
   etc.). Debounced to 80ms minimum interval.

3. **Rapid follow-up** (`ScheduleRapidFollowUp`, `hook/ThemeEngine.cs:672-692`):
   scheduled walks at +200ms, +500ms, +1000ms after a navigation event. This catches
   controls that load asynchronously after the initial navigation.

### All-Windows Walk

(`hook/ThemeEngine.cs:728-738`)

```csharp
private int WalkAllWindows()
{
    int total = 0;
    var topLevels = _r.GetAllTopLevels();
    foreach (var topLevel in topLevels)
    {
        try { total += WalkAndRecolor(topLevel, 0); }
        catch { }
    }
    return total;
}
```

This walks ALL TopLevel instances, not just `MainWindow`. Avalonia creates separate
`PopupRoot` windows for profile cards, context menus, and overlays.
`_r.GetAllTopLevels()` uses Avalonia's internal `WindowImpl.s_instances` list to
discover these.

---

## Color Audit Algorithm

### Purpose

The color audit (`RunColorAudit`, `hook/ThemeEngine.cs:745-826`) is a diagnostic pass
that runs 1.5 seconds after a theme is applied. Its job is to find colors that "escaped"
-- original Root colors that survived all the tree walks and still appear on controls.

### Scheduling

(`hook/ThemeEngine.cs:566-579`)

```csharp
System.Threading.ThreadPool.QueueUserWorkItem(_ =>
{
    Thread.Sleep(1500);
    if (_activeThemeName != auditName) return; // theme changed, skip
    _r.RunOnUIThread(() =>
    {
        try { RunColorAudit(auditMap, auditReverse, auditName); }
        catch (Exception ex) { Logger.Log("Theme", "Audit error: " + ex.Message); }
    });
});
```

The audit is scheduled on a background thread with a 1.5 second delay, then
marshaled to the UI thread for tree access. If the theme changed during the delay
(the user switched themes rapidly), the audit is abandoned.

### Walk Algorithm

(`hook/ThemeEngine.cs:783-826`)

```
function AuditNode(visual, depth, activeMap, reverseMap, originals, staleColors, unmappedColors):
    if depth > 50: return
    if visual.Tag == "uprooted-no-recolor": return

    for each propName in [Background, Foreground, BorderBrush]:
        brush = visual.propName
        colorStr = GetBrushColorString(brush)

        totalProps++

        if originals.Contains(colorStr):
            // This is an original Root color that SHOULD have been recolored
            matchedProps++
            staleColors[propName + ":" + colorStr + " on " + typeName]++

    for each child of visual:
        AuditNode(child, depth + 1, ...)
```

The `originals` set contains the *keys* of `_activeColorMap` -- these are the colors
that should have been replaced. Any control still showing one of these colors is a
"stale" entry.

### Output

The audit logs:
- Total color properties scanned
- Count of properties still showing original colors
- Top 10 stale colors by frequency, with property name, color value, and control type

Example log output:
```
=== COLOR AUDIT (crimson) after 1.5s ===
  Total color props scanned: 847
  Still matching original (need recolor): 3
  --- STALE (original Root colors still present, 3 total) ---
    [2x] Background:#FF0D1521 on Border
    [1x] Foreground:#FFF2F2F2 on TextBlock
=== END AUDIT ===
```

This data drives iterative improvement of the tree color maps.

---

## Custom Theme Generation

### Entry Point

(`hook/ThemeEngine.cs:135-149`)

```csharp
public bool ApplyCustomTheme(string accentHex, string bgHex)
{
    var palette = GenerateCustomTheme(accentHex, bgHex);
    var treeMap = GenerateCustomTreeColorMap(accentHex, bgHex);
    _customPalette = palette;
    _customAccent = accentHex;
    _customBg = bgHex;
    return ApplyThemeInternal("custom", palette, treeMap);
}
```

From two user-chosen colors (accent and background), the engine generates both a
55-key resource palette and a 25-entry tree color map.

### Palette Generation Algorithm

(`GenerateCustomTheme`, `hook/ThemeEngine.cs:1778-1920`)

**Step 1: HSL decomposition and capping**

```csharp
var (ah, asat, al) = ColorUtils.RgbToHsl(accent);
var (bh, bsat, bl) = ColorUtils.RgbToHsl(bg);

double cappedAsat = Math.Min(asat, 0.88);
double cappedAl = Math.Clamp(al, 0.02, 0.65);
```

The accent saturation is capped at 0.88 to prevent garish neon colors. Lightness is
clamped to [0.02, 0.65] -- the low bound of 0.02 allows very dark accents (near-black)
while the high bound of 0.65 prevents washed-out pastels.

**Step 2: Accent shade ladder**

(`hook/ThemeEngine.cs:1789-1794`)

Six variants are generated from the accent hue:

| Variant | Saturation | Lightness |
|---------|-----------|-----------|
| `accentLight1` | `min(0.88, cappedAsat * 1.05)` | `min(0.75, cappedAl + 0.12)` |
| `accentLight2` | `min(0.82, cappedAsat * 1.0)` | `min(0.82, cappedAl + 0.22)` |
| `accentLight3` | `min(0.75, cappedAsat * 0.85)` | `min(0.88, cappedAl + 0.32)` |
| `accentDark1` | `min(0.88, cappedAsat * 1.1)` | `max(0.01, cappedAl - 0.12)` |
| `accentDark2` | `min(0.88, cappedAsat * 1.1)` | `max(0.005, cappedAl - 0.20)` |
| `accentDark3` | `min(0.85, cappedAsat * 1.0)` | `max(0.002, cappedAl - 0.28)` |

Lighter variants decrease saturation slightly to avoid over-saturation at higher
lightness. Darker variants increase saturation slightly to maintain vibrancy at
lower lightness.

**Step 3: Background hierarchy**

(`hook/ThemeEngine.cs:1798-1806`)

```csharp
double bgHue = bh;
double bgSat = Math.Clamp(bsat, 0.06, 0.35);
double bgL = Math.Clamp(bl, 0.03, 0.18);

var bgBase        = ColorUtils.HslToHex(bgHue, bgSat, bgL);
var bgSecondary   = ColorUtils.HslToHex(bgHue, bgSat * 0.95, bgL + 0.035);
var bgTertiary    = ColorUtils.HslToHex(bgHue, bgSat * 0.90, bgL + 0.07);
var bgQuarternary = ColorUtils.HslToHex(bgHue, bgSat * 0.85, bgL + 0.11);
```

Backgrounds use the background color's own hue (not the accent hue), preserving the
user's intent. Saturation is clamped to [0.06, 0.35] so backgrounds are always subtly
tinted but never vivid. Lightness is clamped to [0.03, 0.18] to keep backgrounds dark.
Each level increases lightness by 3.5% and decreases saturation by 5%.

**Step 4: Hue-tinted text**

(`hook/ThemeEngine.cs:1809-1815`)

```csharp
var textColor = ColorUtils.DeriveTextColorTinted(bgBase, accent);
var (th, ts, tl) = ColorUtils.RgbToHsl(textColor);
var textPrimary = ColorUtils.HslToHex(th, ts, Math.Min(0.98, tl + 0.02));
```

`DeriveTextColorTinted` (from `hook/ColorUtils.cs:95-109`) creates text that carries
a subtle hue from the accent color. For dark backgrounds, this means near-white text
with 3-12% accent saturation. The primary text variant is nudged 2% brighter.

Alpha variants are generated at 78%, 55%, and 36% for secondary, tertiary, and
disabled text.

**Step 5: Derived values**

The remaining ~35 keys are derived from the accent and background colors using alpha
overlays and the shade ladder:

- Control fills: accent with alpha `0x1A` / `0x14` / `0x0C`
- Card fill: accent with alpha `0x20`
- Strokes: accent with alpha `0x42` / `0x2C` / `0x36` / `0x48`
- Buttons: accent with alpha `0x1A` (normal), `0x2C` (hover), `0x14` (pressed)
- ListBox items: accent with alpha `0x1A` through `0x35`
- ToggleSwitch: raw accent, `accentLight1`, `accentDark1`
- ScrollBar: accent with alpha `0x58` / `0x88` / full
- Highlight foreground: if accent lightness < 0.4, uses `accentLight2`; otherwise
  uses the raw accent

### Tree Color Map Generation

(`GenerateCustomTreeColorMap`, `hook/ThemeEngine.cs:1928-2022`)

This function maps Root's ~25 hardcoded ARGB colors to custom-derived equivalents.
The approach uses a dual-hue strategy:

- **Backgrounds** use the background hue (`bh`) with the clamped saturation/lightness
- **Borders** use the accent hue (`ah`) for a dual-tone effect
- **Accents** use the raw accent color and its shade ladder
- **Text** uses the hue-tinted text color

Example mapping for structural backgrounds:

```csharp
map["#ff0d1521"] = Hsl(bgHue, bgSat, bgL);                    // Main dark bg
map["#ff07101b"] = Hsl(bgHue, bgSat * 1.05, max(0.02, bgL - 0.03)); // Darkest bg
map["#ff101c2e"] = Hsl(bgHue, bgSat * 0.97, bgL + 0.025);     // Slightly lighter
```

Each replacement gets a unique lightness value to maintain visual hierarchy.

### Uniqueness Enforcement

(`hook/ThemeEngine.cs:1997-2019`)

After generating all mappings, a uniqueness pass ensures no two replacement values
collide:

```csharp
var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
foreach (var key in keys)
{
    var val = map[key];
    while (seen.Contains(val))
    {
        // Nudge green channel by +1
        byte g = Math.Min((byte)255, (byte)(Convert.ToByte(hex[4..6], 16) + 1));
        val = $"#{a:X2}{r:X2}{g:X2}{b:X2}";
        map[key] = val;
    }
    seen.Add(val);
}
```

This nudges the green channel by +1 until uniqueness is achieved. The visual
difference of a single green channel step is imperceptible, but it ensures the
reverse map (replacement -> original) is bijective.

---

## Revert Mechanics

### Overview

`RevertTheme()` (`hook/ThemeEngine.cs:1174-1320`) undoes all theme changes. The
algorithm is carefully ordered: resources must be restored BEFORE the visual tree
walk so that `ClearValue` causes `DynamicResource` bindings to resolve correctly.

### Step-by-Step Algorithm

**Step 0: Disable active theme**

```csharp
_walkTimer?.Dispose();
_walkTimer = null;
var savedActiveMap = _activeColorMap;
_activeColorMap = null;  // Disables layout interceptor
```

(`hook/ThemeEngine.cs:1176-1185`)

The walk timer is cancelled and `_activeColorMap` is set to null immediately. This
prevents the layout interceptor from re-applying theme colors during the revert
process. The active map is saved for the purge step.

**Step 1: Restore Styles[0].Resources**

(`hook/ThemeEngine.cs:1191-1227`)

Two substeps:

1. For each entry in `_savedOriginals`, write the original value back:
   ```csharp
   _r.AddResource(styleRes, key, original);
   ```

2. For each key in `_addedKeys` (keys that had no original), remove them:
   ```csharp
   _r.RemoveResource(styleRes, key);
   ```

**Step 2: Remove MergedDictionary**

(`hook/ThemeEngine.cs:1230-1247`)

```csharp
mergedDicts.Remove(_injectedDict);
_injectedDict = null;
```

**Step 3: Targeted purge via PurgeKnownColors**

(`hook/ThemeEngine.cs:1249-1310`)

This is the most sophisticated step. A naive approach would `ClearValue` on every
control in the tree, but that would destroy Root's structural backgrounds that were
never theme-related. Instead, the purge builds a known-color set and only clears
controls whose current color is in that set.

```
knownColors = union of:
    - All keys and values from savedActiveMap
    - All keys and values from _rootOriginals
    - All keys and values from _reverseColorMap
```

The `PurgeKnownColors` walker (`hook/ThemeEngine.cs:1365-1436`):

```
function PurgeKnownColors(visual, depth, knownColors):
    if visual.Tag == "uprooted-no-recolor": skip
    for each propName in [Background, Foreground, BorderBrush, Fill]:
        colorStr = GetBrushColorString(visual.propName)
        if knownColors.Contains(colorStr):
            ClearValue(visual, propName)
            if visual.propName is now null:
                // DynamicResource didn't reassert -- explicit fallback
                if _rootOriginals has colorStr:
                    restore = _rootOriginals[colorStr]
                else if _reverseColorMap has colorStr:
                    restore = _reverseColorMap[colorStr]
                SetValueStylePriority(visual, propName, restore)
        else:
            // Track as orphan for diagnostics
    recurse into children
```

The null-fallback logic is critical. When `ClearValue` removes a `LocalValue`
override, controls with `DynamicResource` bindings automatically re-resolve from
the now-restored resource dictionaries. But controls with hardcoded brushes (no
`DynamicResource` binding) go null after `ClearValue`. For these, the engine falls
back to `_rootOriginals` (the persistent map of all theme replacements back to Root's
true original colors) to restore the correct value.

**Step 4: Cleanup**

```csharp
_savedOriginals.Clear();
_addedKeys.Clear();
UpdateTitleBarColor(DefaultDarkBg);  // "#0D1521"
_activeThemeName = null;
_reverseColorMap = null;
```

### Revert Follow-Ups

(`ScheduleRevertFollowUps`, `hook/ThemeEngine.cs:1326-1353`)

After the immediate revert, follow-up walks are scheduled at +500ms, +1500ms, and
+3000ms. These catch controls that were created after the initial revert (e.g., lazy-
loaded views or async content). Each follow-up uses the saved reverse map and stops
if a new theme has been applied in the interim.

---

## CSS Variable Bridge

### How Native Themes Sync with TypeScript

The C# theme engine operates on Avalonia's native resource system and visual tree.
The TypeScript layer operates on the embedded Chromium browser's DOM and CSS. These
two worlds are bridged through `ContentPages.UpdateLiveColors()`.

When a theme is applied or updated live, `ContentPages` updates its static color
fields:

```csharp
// hook/ThemeEngine.cs:307
ContentPages.UpdateLiveColors(accentHex, bgHex, palette);
```

These static fields (`ContentPages.AccentGreen`, `ContentPages.CardBg`,
`ContentPages.TextWhite`, `ContentPages.TextMuted`, `ContentPages.TextDim`) are used
both by the C# side (for building Uprooted's settings pages) and are injected into
the HTML patches as part of `window.__UPROOTED_SETTINGS__`.

The TypeScript theme plugin reads these settings at startup and translates them into
CSS custom properties (e.g., `--uprooted-accent`, `--uprooted-bg-primary`). Native
Avalonia UI and browser DOM UI thus share the same color values through this indirect
bridge.

### Limitations

The bridge is not truly real-time. CSS variables in the browser are set at page load
from the settings object. During a live preview drag, the native Avalonia side updates
instantly, but the browser side does not update until the next page load or explicit
CSS variable update from the TypeScript layer. The primary theming target for the C#
engine is Root's Avalonia UI, not the embedded browser content.

---

## Error Handling

### Failure Modes and Recovery

The theme engine is designed to be resilient. Every tree walk, resource operation, and
reflection call is wrapped in try-catch blocks. The principle is: a failure in one
control should never prevent the rest of the tree from being themed.

**Resource creation failure**: If `_r.CreateBrush(hex)` or `_r.ParseColor(hex)` returns
null (malformed hex, reflection failure), the key is silently skipped
(`hook/ThemeEngine.cs:413-427`). The theme applies with a reduced palette.

**Walk failure**: Each `WalkAndRecolor` call in `WalkAllWindows` is individually
wrapped (`hook/ThemeEngine.cs:734-735`):
```csharp
try { total += WalkAndRecolor(topLevel, 0); }
catch { }
```
A crash in one window's tree does not prevent other windows from being walked.

**Property access failure**: Inside `CollectColorChanges`, each property read is
individually wrapped (`hook/ThemeEngine.cs:1042-1062`). Properties that throw
(e.g., trimmed getters, disposed controls) are skipped.

**Layout interceptor failure**: If the `LayoutUpdated` subscription fails
(`hook/ThemeEngine.cs:657-665`), the engine falls back to the 500ms timer. The
interceptor is an optimization, not a requirement.

**Revert failure**: If `RevertTheme()` partially fails (e.g., MergedDictionary removal
throws), the state cleanup still runs (`hook/ThemeEngine.cs:1312-1319`). The engine
may leave some visual artifacts, but it will not be in an inconsistent state for the
next theme apply.

**Audit failure**: The color audit runs on a background thread and swallows all
exceptions (`hook/ThemeEngine.cs:577`). A failed audit has zero impact on theme
functionality.

### The `uprooted-no-recolor` Tag

Controls tagged with `"uprooted-no-recolor"` are excluded from all tree walks:

- `CollectColorChanges` (`hook/ThemeEngine.cs:1033`)
- `AuditNode` (`hook/ThemeEngine.cs:791`)
- `PurgeKnownColors` (`hook/ThemeEngine.cs:1368`)
- `WalkAndRestore` (`hook/ThemeEngine.cs:1119`)

This tag is applied to Uprooted's own UI elements that manage their own colors (e.g.,
theme preview swatches on the Themes settings page). Without this tag, the tree walker
would fight with ContentPages over the colors of these elements.

---

## Key Data Structures

### Instance Fields

| Field | Type | Purpose |
|-------|------|---------|
| `_r` | `AvaloniaReflection` | Cached reflection handles for all Avalonia operations |
| `_injectedDict` | `object?` | Reference to our `ResourceDictionary` in `MergedDictionaries` |
| `_activeThemeName` | `string?` | Current theme name (`"crimson"`, `"loki"`, `"custom"`, or null) |
| `_savedOriginals` | `Dictionary<string, object?>` | Original resource values from `Styles[0]` for revert |
| `_addedKeys` | `HashSet<string>` | Keys we added to `Styles[0]` that had no original (remove on revert) |
| `_rootOriginals` | `Dictionary<string, string>` | Persistent map: any theme replacement -> Root's true original color |
| `_activeColorMap` | `Dictionary<string, string>?` | Current forward map: original ARGB -> replacement ARGB |
| `_reverseColorMap` | `Dictionary<string, string>?` | Current reverse map: replacement ARGB -> original ARGB |
| `_customPalette` | `Dictionary<string, string>?` | Full palette for the active custom theme |
| `_customAccent` | `string?` | Raw accent hex for the active custom theme |
| `_customBg` | `string?` | Raw background hex for the active custom theme |
| `_walkTimer` | `System.Threading.Timer?` | 500ms continuous walk timer |
| `_walkCount` | `int` | Walk iteration counter (for logging) |
| `_layoutInterceptorInstalled` | `bool` | Whether `LayoutUpdated` hook is active |
| `_lastLayoutWalkTick` | `long` | Debounce timestamp for layout interceptor |
| `_lastLiveUpdateTick` | `long` | Throttle timestamp for live preview |
| `_liveBrushCache` | `Dictionary<string, object>?` | Per-update brush cache during live preview walks |

### The `_rootOriginals` Map

(`hook/ThemeEngine.cs:32-34`)

```csharp
private readonly Dictionary<string, string> _rootOriginals =
    new(StringComparer.OrdinalIgnoreCase);
```

This is the most important data structure for multi-theme correctness. It is a
*persistent* map that accumulates across theme switches. The key is any replacement
color ever used by any theme; the value is the original Root color.

It is pre-populated in the constructor from all static `TreeColorMaps`
(`hook/ThemeEngine.cs:42-51`) and grows as custom themes are applied. It enables:

1. **Stale color recovery during theme switching** -- when switching from Crimson to
   Loki, controls may still show Crimson colors that were missed by the revert walk.
   `_rootOriginals` maps these Crimson colors back to Root's originals, which then
   map to Loki's colors in the combined map.

2. **Null-fallback during purge** -- when `ClearValue` leaves a property null during
   revert, `_rootOriginals` provides the correct Root original color.

### Static Dictionaries

| Dictionary | Type | Purpose |
|-----------|------|---------|
| `Themes` | `Dict<string, Dict<string, string>>` | Preset theme palettes (55 keys each) |
| `TreeColorMaps` | `Dict<string, Dict<string, string>>` | Preset tree color maps (~25 entries each) |

### Color Map Lifecycle

```
ApplyTheme("crimson"):
    1. _activeColorMap = combinedMap (base + cross-mapped + stale-mapped)
    2. _reverseColorMap = inverse of combinedMap
    3. _rootOriginals += crimson's replacement->original entries

ApplyTheme("loki"):
    1. RevertTheme() clears _activeColorMap, _reverseColorMap
    2. _rootOriginals still has crimson's entries
    3. _activeColorMap = combinedMap (loki base + crimson cross-map + stale-map)
    4. _reverseColorMap = inverse of combinedMap

RevertTheme():
    1. _activeColorMap = null
    2. _reverseColorMap = null (after purge completes)
    3. _rootOriginals persists (never cleared)
```

---

## Performance Considerations

### Tree Walk Cost

Each tree walk reads 4 properties (`Background`, `Foreground`, `BorderBrush`, `Fill`)
on every visual tree node via reflection. For a typical Root window with 500+ nodes,
the walk takes approximately 2-5ms.

The walk is split into two passes (`hook/ThemeEngine.cs:970-1023`):

**Pass 1 (Collect)**: Read-only traversal that builds a list of pending changes.
This avoids modifying the tree during traversal, which could cause iterator
invalidation or missed nodes.

**Pass 2 (Apply)**: Iterate the pending list and apply changes. This is safe because
the tree structure is not being traversed during modification.

### Brush Caching in Live Preview

(`hook/ThemeEngine.cs:986-996`)

During live preview, `_liveBrushCache` maps replacement hex strings to already-created
`SolidColorBrush` objects:

```csharp
if (_liveBrushCache != null)
{
    if (!_liveBrushCache.TryGetValue(replacement, out newBrush))
    {
        newBrush = _r.CreateBrush(replacement);
        if (newBrush != null)
            _liveBrushCache[replacement] = newBrush;
    }
}
```

Without this cache, the engine would create a new `SolidColorBrush` for every control
that shares a color. With ~25 unique replacement colors and 500+ nodes, this saves
hundreds of `Activator.CreateInstance` calls per live update.

The cache is scoped to a single update cycle (created before the walk, nulled after).
This prevents brush objects from accumulating across many rapid updates.

### Style Priority vs. LocalValue Priority

(`hook/ThemeEngine.cs:1003-1016`)

In normal walks, brushes are set at Avalonia's **Style priority** via
`_r.SetValueStylePriority()`. This is lower than `LocalValue` priority, which means:

- Hover and pressed style triggers (which use `LocalValue`) temporarily override our
  color, then our color reasserts when the trigger deactivates.
- This creates natural-feeling hover effects without any custom hover handling.

In live preview walks, brushes are set at **LocalValue priority** via direct
`prop.SetValue()`. This is necessary because Root's controls already have `LocalValue`
brushes; Style priority would not override them. The trade-off is that hover effects
during live preview are suppressed.

### Throttle and Debounce Summary

| Mechanism | Interval | Purpose |
|-----------|---------|---------|
| Live preview throttle | 16ms | Cap at ~60fps during color picker drag |
| Layout interceptor debounce | 80ms | Prevent rapid-fire walks during layout |
| Continuous walk timer | 500ms (first at 200ms) | Background safety net |
| Rapid follow-up walks | 200ms, 500ms, 1000ms | Catch async-loaded controls |
| Color audit delay | 1500ms | Allow tree to settle before diagnostic |
| Revert follow-ups | 500ms, 1500ms, 3000ms | Catch post-revert lazy loads |

### Reflection Cost Management

All Avalonia types, properties, and methods are resolved once during startup by
`AvaloniaReflection.Resolve()` and cached as `Type`, `PropertyInfo`, and `MethodInfo`
handles. The per-call cost of reflection in the tree walk is primarily
`PropertyInfo.GetValue()` and `PropertyInfo.SetValue()`, which are fast for
non-trimmed properties.

The tree walk also uses `visual.GetType().GetProperty(propName)` on each node
(`hook/ThemeEngine.cs:1039`), which is not cached. This is acceptable because:

1. The CLR caches type metadata internally, so repeated `GetProperty` calls on the
   same type are fast.
2. The walk checks only 4 fixed property names per node.
3. The overall walk time (2-5ms for 500+ nodes) fits well within the 16ms frame budget.

### DWM Title Bar Color

(`hook/ThemeEngine.cs:54-113`)

The Windows 11 title bar color is set via P/Invoke to `DwmSetWindowAttribute`. This
is called once per theme apply, not per walk. The HWND is obtained via
`TopLevel.TryGetPlatformHandle().Handle` through reflection. On non-Windows platforms,
the call is skipped via `RuntimeInformation.IsOSPlatform` check.

The hex-to-COLORREF conversion (`hook/ThemeEngine.cs:75-81`):

```csharp
byte r = Convert.ToByte(hex[0..2], 16);
byte g = Convert.ToByte(hex[2..4], 16);
byte b = Convert.ToByte(hex[4..6], 16);
uint colorRef = (uint)(r | (g << 8) | (b << 16));  // COLORREF = 0x00BBGGRR
```

Note the byte order reversal: COLORREF uses BGR, not RGB.

---

## Appendix: Diagnostic Methods

The theme engine includes several diagnostic methods that are not part of the normal
theme flow but are invaluable for development:

**`DumpVisualTreeColors()`** (`hook/ThemeEngine.cs:1496-1545`): Scans all TopLevel
instances and logs the top 80 color/property combinations by frequency. Also logs
the top 20 control types and searches for DotNetBrowser/WebView controls.

**`DumpVisualTreeStructure()`** (`hook/ThemeEngine.cs:1616-1671`): Logs the visual
tree to 6 levels of depth with type names, child counts, and background colors. Shows
full type names for DotNetBrowser controls.

**`DumpResourceKeys()`** (`hook/ThemeEngine.cs:1676-1729`): Dumps all keys from
`Styles[0].Resources` (up to 60), `Application.Resources` (up to 30), and each
`MergedDictionary` (up to 15 per dict). Shows key name, value type, and value.

These methods were used to discover Root's color scheme, identify which resource keys
affect which controls, and build the tree color maps. They can be called from the
Uprooted settings page via the theme engine instance.
