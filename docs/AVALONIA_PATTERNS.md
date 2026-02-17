# Avalonia Patterns

> **Related docs:** [Hook Reference](HOOK_REFERENCE.md) | [Architecture](ARCHITECTURE.md) | [Theme Engine Deep Dive](THEME_ENGINE_DEEP_DIVE.md) | [.NET Runtime](DOTNET_RUNTIME.md)

---

## Table of Contents

1. [Overview](#overview)
2. [Property System](#property-system)
3. [Visual Tree](#visual-tree)
4. [Styling System](#styling-system)
5. [Threading Model](#threading-model)
6. [Control Hierarchy](#control-hierarchy)
7. [Layout System](#layout-system)
8. [Advanced Patterns](#advanced-patterns)
9. [Common Reflection Patterns](#common-reflection-patterns)
10. [Pitfalls](#pitfalls)

---

## Overview

Uprooted injects into Root Communications' desktop application, which is built on
.NET 10 with Avalonia UI. Root ships as a trimmed single-file binary, so Uprooted
cannot reference Avalonia assemblies at compile time. Every interaction with the UI
framework happens through runtime reflection.

This document explains Avalonia concepts through Uprooted's reflection-only lens.
If you are contributing to the C# hook layer, you need to understand both the
Avalonia concept and the reflection pattern used to reach it.

**Why this matters:**

- Controls are instantiated via `Activator.CreateInstance` on runtime-discovered types.
- Properties are set through cached `PropertyInfo` or `MethodInfo` handles.
- Events are wired via `Expression.Lambda` because delegate types are unknown at compile time.
- Mistakes produce silent failures, cryptic exceptions, or UI freezes.

The central class is `AvaloniaReflection` (`hook/AvaloniaReflection.cs`, ~1943
lines). It resolves roughly 50 Avalonia types and 55 member handles during startup,
then exposes them through typed wrapper methods.

---

## Property System

Avalonia has its own property system, similar to WPF's dependency properties. Some
properties cannot be accessed through normal CLR setters in Root's trimmed binary.

### Property Types

- **StyledProperty** -- Supports styling, binding, inheritance. Most common.
  Examples: `Control.MarginProperty`, `TextBlock.FontSizeProperty`.
- **DirectProperty** -- Wraps a CLR backing field. No styling. Faster.
  Example: `TextBox.TextProperty`.
- **AttachedProperty** -- Defined by one type, set on another via static methods.
  Examples: `Grid.ColumnProperty`, `Canvas.LeftProperty`.

All derive from `AvaloniaProperty`.

### Pattern 1: CLR PropertyInfo (When Available)

Most properties retain their CLR wrappers. Uprooted caches `PropertyInfo` handles
at startup (`hook/AvaloniaReflection.cs:310-314`):

```csharp
_textBlockText = TextBlockType?.GetProperty("Text", pub);
_textBlockFontSize = TextBlockType?.GetProperty("FontSize", pub);
```

Then uses `PropertyInfo.SetValue` (`hook/AvaloniaReflection.cs:654-656`):

```csharp
var tb = Activator.CreateInstance(TextBlockType);
_textBlockText?.SetValue(tb, text);
_textBlockFontSize?.SetValue(tb, fontSize);
```

### Pattern 2: Avalonia SetValue (When CLR Is Trimmed)

TextBox has trimmed CLR setters. Uprooted resolves the static `AvaloniaProperty`
fields as `FieldInfo` (`hook/AvaloniaReflection.cs:343-346`):

```csharp
_textBoxTextProperty = TextBoxType?.GetField("TextProperty", staticPub);
_textBoxWatermarkProperty = TextBoxType?.GetField("WatermarkProperty", staticPub);
```

Then invokes `SetValue(AvaloniaProperty, object, BindingPriority)` via the
`SetAvaloniaProperty` helper (`hook/AvaloniaReflection.cs:770-803`):

```csharp
var avProp = avaloniaPropertyField.GetValue(null);   // Static field -> AvaloniaProperty
_setValueWithPriority.Invoke(control, new[] { avProp, value, _bindingPriorityStyle });
```

### Pattern 3: Attached Properties via Static Methods

Grid.Column and Grid.Row are accessed through static methods
(`hook/AvaloniaReflection.cs:334-337, 1081-1094`):

```csharp
_gridSetColumn = GridType?.GetMethod("SetColumn", stat);
// Usage:
_gridSetColumn?.Invoke(null, new[] { control, (object)column });
```

### ClearValue

Removes a local value override so bindings or styles can reassert. Essential for
theme revert (`hook/AvaloniaReflection.cs:1178-1218`):

```csharp
var field = control.GetType().GetField(propertyFieldName,
    BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy);
var avaloniaProperty = field.GetValue(null);
// Find and invoke ClearValue(AvaloniaProperty) non-generic overload
```

---

## Visual Tree

Avalonia uses a visual tree to represent the rendered UI hierarchy. Uprooted
traverses this tree to find controls, inject new ones, and modify existing ones.

### Visual vs. Logical Children

- **Visual tree** -- The actual rendered hierarchy, including template internals
  (borders, content presenters, scrollbar tracks).
- **Logical tree** -- The developer-authored hierarchy. Popup controls appear as
  logical children but may not share the visual tree with the owning window.

Uprooted primarily uses the visual tree, since it needs template-generated elements.

### GetVisualChildren

Accessed via `VisualExtensions.GetVisualChildren(Visual)`, a static extension
(`hook/AvaloniaReflection.cs:294-295, 606-621`):

```csharp
_getVisualChildren = VisualExtensionsType?.GetMethods(stat)
    .FirstOrDefault(m => m.Name == "GetVisualChildren" && m.GetParameters().Length == 1);

// Usage: invoke as static, enumerate IEnumerable result
result = _getVisualChildren.Invoke(null, new[] { visual });
```

### Depth-First Traversal

`VisualTreeWalker` provides stack-based DFS (`hook/VisualTreeWalker.cs:35-49`):

```csharp
public IEnumerable<object> DescendantsDepthFirst(object root)
{
    var stack = new Stack<object>();
    foreach (var child in _r.GetVisualChildren(root))
        stack.Push(child);
    while (stack.Count > 0)
    {
        var node = stack.Pop();
        yield return node;
        foreach (var child in _r.GetVisualChildren(node))
            stack.Push(child);
    }
}
```

Used for: finding "APP SETTINGS" text, locating ListBox/NavContainer/content area,
searching by text or type, and checking for already-injected controls via tags.

### Settings Layout Discovery

`FindSettingsLayout` (`hook/VisualTreeWalker.cs:79-181`) demonstrates a complete
discovery workflow using text anchors and upward walks:

1. Find "APP SETTINGS" TextBlock via DFS text search
2. Walk up to find the NavContainer StackPanel (identified by having 8+ children)
3. Walk further up to find the Grid containing both sidebar and content columns
4. Within those containers, locate ListBox, version box, save bar, back button

The NavContainer heuristic (`hook/VisualTreeWalker.cs:374-381`):

```csharp
if (typeName == "StackPanel" && childCount >= 8)
    return current;  // This is the nav container
```

### Parent Navigation

`GetParent` tries `VisualParent` first, then `Parent`
(`hook/AvaloniaReflection.cs:1398-1409`):

```csharp
var vpProp = type.GetProperty("VisualParent");
if (vpProp != null) return vpProp.GetValue(node);
return type.GetProperty("Parent")?.GetValue(node);
```

---

## Styling System

Uprooted's ThemeEngine manipulates Avalonia's styling system to apply color themes
across the entire native UI.

### Resource Dictionaries

Key-value stores for reusable values (colors, brushes). They exist at multiple levels:

- `Application.Resources` -- App-wide
- `Application.Styles[n].Resources` -- Per-style (Root's theme colors live in `Styles[0]`)
- `Control.Resources` -- Per-control

Accessing style resources (`hook/AvaloniaReflection.cs:1588-1616`):

```csharp
var stylesProp = app.GetType().GetProperty("Styles");
// Enumerate to Styles[0], get its Resources property
```

### MergedDictionaries

Uprooted injects a custom `ResourceDictionary` into `Application.Resources.MergedDictionaries`
to override FluentTheme keys (`hook/AvaloniaReflection.cs:1642-1696`):

```csharp
var dict = Activator.CreateInstance(ResourceDictionaryType);
// Add entries via IDictionary indexer reflection:
var indexer = dict.GetType().GetProperty("Item", ...);
indexer.SetValue(dict, value, new object[] { key });
// Merge into app resources:
GetMergedDictionaries(appResources)?.Add(dict);
```

Root's theme colors (ThemeAccentColor, ThemeAccentBrush) live in
`Styles[0].Resources` and are NOT overridden by MergedDictionaries. The ThemeEngine
writes directly into `Styles[0].Resources` for those keys.

### BindingPriority

Determines which value wins when multiple sources set the same property. From
highest to lowest: Animation, LocalValue, StyleTrigger, Style, Template.

The ThemeEngine uses Style priority (lower than LocalValue) so hover/pressed
triggers can temporarily override themed values
(`hook/AvaloniaReflection.cs:1266-1307`):

```csharp
_setValueWithPriority.Invoke(control,
    new[] { avaloniaProperty, value, _bindingPriorityStyle });
```

`BindingPriority` is resolved once by scanning assemblies for
`Avalonia.Data.BindingPriority` (`hook/AvaloniaReflection.cs:807-820`).

---

## Threading Model

Avalonia uses a single-threaded UI model. All visual tree modifications must happen
on the UI thread.

### Dispatcher

Accessed via `Dispatcher.UIThread` static property. The `Post` method is resolved
with three fallbacks (`hook/AvaloniaReflection.cs:262-289`):

1. `Post(Action, DispatcherPriority)` -- 2-param (Avalonia 11+)
2. `Post(Action)` -- 1-param
3. `InvokeAsync(Action)` -- older API

### DispatcherPriority Is a Struct

**CRITICAL**: In Avalonia 11+, `DispatcherPriority` is a **struct** with static
properties (`Normal`, `Background`, `Render`), not an enum. `RunOnUIThread`
handles this with a four-level fallback (`hook/AvaloniaReflection.cs:553-604`):

```csharp
var priorityType = _dispatcherPost.GetParameters()[1].ParameterType;

// 1. Try static property: DispatcherPriority.Normal
var normalProp = priorityType.GetProperty("Normal", BindingFlags.Public | BindingFlags.Static);
// 2. Try static field
// 3. Try Enum.Parse (if it IS an enum in some version)
// 4. Last resort: Activator.CreateInstance for default struct value
```

### Threading in SidebarInjector

A `System.Threading.Timer` fires every 200ms, marshaling to the UI thread via
`RunOnUIThread`. An `Interlocked.CompareExchange` guard prevents re-entrant
injection (`hook/SidebarInjector.cs:90-112`):

```csharp
if (Interlocked.CompareExchange(ref _injecting, 1, 0) != 0) return;
_r.RunOnUIThread(() => {
    try { CheckAndInject(); }
    finally { Interlocked.Exchange(ref _injecting, 0); }
});
```

---

## Control Hierarchy

### Key Types

| Type | Purpose in Uprooted |
|------|---------------------|
| `Control` | Base type: Tag, IsVisible, Margin, Cursor |
| `Panel` | Container base with Children collection |
| `StackPanel` | Vertical/horizontal layout with Spacing -- most common container |
| `Grid` | Row/column layout -- 2-column plugin cards, sidebar structure |
| `Border` | Single-child decorator: Background, CornerRadius, BorderThickness |
| `TextBlock` | Text display: FontSize, FontWeight, Foreground, TextWrapping |
| `TextBox` | Text input: Watermark, MaxLength (CLR setters trimmed) |
| `ScrollViewer` | Scrollable wrapper for NavContainer and content pages |
| `ContentControl` | Root's content area (never modify Content directly) |
| `Canvas` | Absolute positioning via Left/Top attached properties |
| `Window` | Top-level: Clipboard access, OverlayLayer |

### Creating Controls

All use `Activator.CreateInstance` on cached types. Examples:

**TextBlock** (`hook/AvaloniaReflection.cs:650-667`):
```csharp
var tb = Activator.CreateInstance(TextBlockType);
_textBlockText?.SetValue(tb, text);
_textBlockFontSize?.SetValue(tb, fontSize);
```

**StackPanel** (`hook/AvaloniaReflection.cs:669-685`):
```csharp
var sp = Activator.CreateInstance(StackPanelType);
var orientation = Enum.Parse(OrientationType, vertical ? "Vertical" : "Horizontal");
_stackPanelOrientation?.SetValue(sp, orientation);
```

**Border** (`hook/AvaloniaReflection.cs:687-709`):
```csharp
var border = Activator.CreateInstance(BorderType);
var cr = Activator.CreateInstance(CornerRadiusType, cornerRadius);
_borderCornerRadius?.SetValue(border, cr);
```

### Type Checking

Since everything is `object`, use `IsAssignableFrom`
(`hook/AvaloniaReflection.cs:1133-1137`):

```csharp
public bool IsTextBlock(object? obj)
    => obj != null && TextBlockType?.IsAssignableFrom(obj.GetType()) == true;
public bool IsPanel(object? obj)
    => obj != null && PanelType?.IsAssignableFrom(obj.GetType()) == true;
```

---

## Layout System

Avalonia uses a two-pass layout (Measure then Arrange). Uprooted does not call
these directly -- it sets layout properties and lets Avalonia handle the passes.

### Margin and Padding

Both are `Thickness` structs (`hook/AvaloniaReflection.cs:879-893`):

```csharp
var thickness = Activator.CreateInstance(ThicknessType, left, top, right, bottom);
_controlMargin?.SetValue(control, thickness);       // Margin: cached PropertyInfo
control.GetType().GetProperty("Padding")?.SetValue(  // Padding: runtime search
    control, thickness);                              // (lives on different base classes)
```

### Alignment

Enums parsed from strings (`hook/AvaloniaReflection.cs:981-1001`):

```csharp
var val = Enum.Parse(HorizontalAlignmentType, alignment);  // "Left", "Center", "Right", "Stretch"
control.GetType().GetProperty("HorizontalAlignment")?.SetValue(control, val);
```

### Width, Height, and Bounds

Explicit sizing (`hook/AvaloniaReflection.cs:1543-1553`):

```csharp
control.GetType().GetProperty("Width")?.SetValue(control, width);
```

Reading computed Bounds after layout (`hook/AvaloniaReflection.cs:1451-1470`):

```csharp
var bounds = _layoutableBounds.GetValue(control);  // Rect struct
var x = (double)(bounds.GetType().GetProperty("X")?.GetValue(bounds) ?? 0.0);
// ... same for Y, Width, Height
```

### Grid Layout

Column definitions use GridLength + GridUnitType
(`hook/AvaloniaReflection.cs:1107-1129`):

```csharp
var starUnit = Enum.Parse(GridUnitTypeEnum, "Star");
var gridLength = Activator.CreateInstance(GridLengthType, starWidth, starUnit);
var colDef = Activator.CreateInstance(ColumnDefinitionType);
ColumnDefinitionType.GetProperty("Width")?.SetValue(colDef, gridLength);
```

Used in ContentPages for 2-column plugin card rows
(`hook/ContentPages.cs:476-494`):

```csharp
var rowGrid = r.CreateGrid();
r.AddGridColumn(rowGrid, 1.0);
r.AddGridColumn(rowGrid, 1.0);
r.SetGridColumn(visible[i], 0);
r.SetGridColumn(visible[i + 1], 1);
```

### Panel Children

`Panel.Children` returns `IList`. All manipulation goes through it
(`hook/AvaloniaReflection.cs:1043-1074`):

```csharp
public IList? GetChildren(object? panel) => _panelChildren?.GetValue(panel) as IList;
public void AddChild(object? panel, object? child) => GetChildren(panel)?.Add(child);
public void RemoveChild(object? panel, object? child) => GetChildren(panel)?.Remove(child);
```

---

## Advanced Patterns

### WindowImpl.s_instances for Window Discovery

Avalonia popups live in separate Window/PopupRoot objects outside the main window's
tree. The public `Windows` list does not include popup roots.

Uprooted accesses the internal `WindowImpl.s_instances` static field, then reads
each instance's `Owner` property to get the `TopLevel`
(`hook/AvaloniaReflection.cs:524-551`):

```csharp
_windowImplSInstances = type.GetField("s_instances",
    BindingFlags.NonPublic | BindingFlags.Static);
_windowImplOwner = type.GetProperty("Owner",
    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
```

This is an internal API. Fallback: `GetMainWindow()`.

### TranslatePoint for Coordinate Mapping

Maps coordinates between controls for overlay positioning. Handles both instance
(Avalonia 11+) and static extension forms
(`hook/AvaloniaReflection.cs:1477-1520`):

```csharp
var point = Activator.CreateInstance(PointType, x, y);
if (_translatePoint.IsStatic)
    result = _translatePoint.Invoke(null, new[] { from, point, to });
else
    result = _translatePoint.Invoke(from, new[] { point, to });
// Result is Nullable<Point> -- unwrap via HasValue/Value reflection
```

Resolution uses three fallback strategies: type hierarchy walk, VisualExtensions
check, then brute-force scan of all Avalonia types
(`hook/AvaloniaReflection.cs:362-403`).

### Expression.Lambda for Event Subscription

Avalonia routed events use custom delegate types unknown at compile time. Uprooted
builds correctly-typed delegates via expression trees
(`hook/AvaloniaReflection.cs:1141-1170`):

```csharp
var handlerType = eventInfo.EventHandlerType;
var paramTypes = handlerType.GetMethod("Invoke").GetParameters()
    .Select(p => p.ParameterType).ToArray();
var p0 = Expression.Parameter(paramTypes[0], "sender");
var p1 = Expression.Parameter(paramTypes[1], "e");
var lambda = Expression.Lambda(handlerType,
    Expression.Invoke(Expression.Constant(callback)), p0, p1);
eventInfo.AddEventHandler(control, lambda.Compile());
```

For pointer events needing coordinates, `SubscribePointerEvent`
(`hook/AvaloniaReflection.cs:1846-1901`) builds an expression that calls
`e.GetPosition(sender)` and extracts `X`/`Y`.

### OverlayLayer for Popups

A Canvas above the normal visual tree. Used for filter dropdowns and info lightboxes
(`hook/AvaloniaReflection.cs:1417-1446`):

```csharp
var overlay = _overlayGetOverlayLayer.Invoke(null, new[] { mainWindow });
_canvasSetLeft?.Invoke(null, new[] { control, (object)left });
_canvasSetTop?.Invoke(null, new[] { control, (object)top });
GetChildren(overlay)?.Add(child);
```

Typical workflow (see `hook/ContentPages.cs:665-767`):
1. Get OverlayLayer from main window
2. Create transparent backdrop for click-to-dismiss
3. Position dropdown using TranslatePoint + GetBounds
4. Add backdrop + panel to overlay
5. On dismiss, remove both

### Application.Current

Entry point for the running application. Chain: `Application.Current` ->
`ApplicationLifetime` -> `MainWindow` (`hook/AvaloniaReflection.cs:430-438`):

```csharp
public object? GetAppCurrent() => _appCurrent?.GetValue(null);
public object? GetMainWindow() {
    var app = GetAppCurrent();
    var lifetime = _appLifetime?.GetValue(app);
    return _lifetimeMainWindow?.GetValue(lifetime);
}
```

---

## Common Reflection Patterns

Quick reference for patterns that appear throughout the codebase.

### Type Lookup

Scan loaded assemblies, never use `Type.GetType`
(`hook/AvaloniaReflection.cs:148-166`):

```csharp
foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
{
    if (!asm.GetName().Name?.StartsWith("Avalonia") == true) continue;
    foreach (var type in asm.GetTypes())
        typeMap[type.FullName] = type;
}
Type? Find(string fullName) => typeMap.TryGetValue(fullName, out var t) ? t : null;
```

### Property Access

**Cached** (frequent use): resolve once at startup, call `SetValue`/`GetValue` many times.
**Runtime** (rare use): `control.GetType().GetProperty("Height")?.SetValue(control, 36.0)`.

### Struct/Enum Creation

**Enums**: `Enum.Parse(OrientationType, "Vertical")`
**Structs**: `Activator.CreateInstance(ThicknessType, left, top, right, bottom)`

### Brush Creation

Root's trimmed binary strips the `SolidColorBrush(Color)` constructor. Use
parameterless ctor + Color setter (`hook/AvaloniaReflection.cs:861-875`):

```csharp
var color = _colorParse.Invoke(null, new object[] { hex });
var brush = Activator.CreateInstance(SolidColorBrushType);
SolidColorBrushType.GetProperty("Color")?.SetValue(brush, color);
```

### Control Tagging

`Control.Tag` marks injected controls for identification and cleanup:

| Tag | Purpose |
|-----|---------|
| `uprooted-injected` | Sidebar container (duplicate injection guard) |
| `uprooted-nav-{page}` | Individual nav items |
| `uprooted-highlight-{page}` | Nav highlight borders |
| `uprooted-content` | Content pages |
| `uprooted-no-recolor` | Excluded from theme engine tree walks |

---

## Pitfalls

Real bugs encountered during development. Each is a rule in `ARCHITECTURE.md`.

### Never Use Type.GetType() for Avalonia Types

Returns null in Root's single-file binary. Assembly-qualified names do not resolve
when assemblies are bundled. Use `AvaloniaReflection`'s cached types instead.

### Never Modify ContentControl.Content Directly

Setting `Content` triggers a detach walk on the old content. If that tree was
modified by injection, Avalonia crashes or freezes. Instead, hide Root's children
with `SetIsVisible(false)` and add content alongside them
(`hook/SidebarInjector.cs:545-559`).

### Never Use System.Text.Json in Hook

The CLR profiler injection context causes `MissingMethodException`. The JSON
serializer relies on code generation unavailable at profiler load time. Use
INI-based settings (`UprootedSettings.cs`).

### Never Use EventInfo.AddEventHandler with Wrong Delegate Type

Avalonia routed events need `EventHandler<PointerPressedEventArgs>` etc., not
plain `EventHandler`. Use `Expression.Lambda` to build the exact delegate type.

### DispatcherPriority Is a Struct, Not an Enum

In Avalonia 11+, `Enum.Parse` fails on `DispatcherPriority`. Try static property
access first, then field, then enum parse, then default struct value
(`hook/AvaloniaReflection.cs:570-598`).

### Brush Constructors May Be Trimmed

`SolidColorBrush(Color)` is stripped. Use parameterless constructor + `Color`
property setter.

### CornerRadius Requires Explicit Construction

For non-uniform corners, use the four-parameter constructor
(`hook/ContentPages.cs:1615-1616`):

```csharp
var cr = Activator.CreateInstance(r.CornerRadiusType, 5.0, 5.0, 0.0, 0.0);
```

### ScrollBarVisibility Requires Dynamic Enum Parse

The enum lives in `Avalonia.Controls.Primitives`, not a pre-cached type. Resolve
via `assembly.GetType()` (`hook/SidebarInjector.cs:658-661`).

### Never Use localStorage in TypeScript

Root runs Chromium with `--incognito`. `localStorage` is not persisted.

---

*Last updated: 2026-02-16*
