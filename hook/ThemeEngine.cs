using System.Collections;
using System.Linq;
using System.Runtime.InteropServices;
namespace Uprooted;
internal class ThemeEngine
{
    private readonly AvaloniaReflection _r;
    private object? _injectedDict;
    private string? _activeThemeName;
    private readonly Dictionary<string, object?> _savedOriginals = new();
    private readonly HashSet<string> _addedKeys = new();
    private readonly Dictionary<string, string> _rootOriginals = new(StringComparer.OrdinalIgnoreCase);
    public string? ActiveThemeName => _activeThemeName;
    public ThemeEngine(AvaloniaReflection r)
    {
        _r = r;
        foreach (var (_, themeMap) in TreeColorMaps)
        {
            foreach (var (rootOrig, replacement) in themeMap)
            {
                if (!_rootOriginals.ContainsKey(replacement))
                    _rootOriginals[replacement] = rootOrig;
            }
        }
        Logger.Log("Theme", "Root originals map initialized: " + _rootOriginals.Count + " entries from " + TreeColorMaps.Count + " preset themes");
    }
    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref uint value, int size);
    private const int DWMWA_CAPTION_COLOR = 35;
    private const string DefaultDarkBg = "#0D1521";
    private void UpdateTitleBarColor(string hexColor)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return;
        try
        {
            var hwnd = GetMainWindowHandle();
            if (hwnd == IntPtr.Zero) return;
            var hex = hexColor.TrimStart('#');
            if (hex.Length == 8) hex = hex[2..];
            if (hex.Length != 6) return;
            byte r = Convert.ToByte(hex[0..2], 16);
            byte g = Convert.ToByte(hex[2..4], 16);
            byte b = Convert.ToByte(hex[4..6], 16);
            uint colorRef = (uint)(r | (g << 8) | (b << 16));
            DwmSetWindowAttribute(hwnd, DWMWA_CAPTION_COLOR, ref colorRef, sizeof(uint));
            Logger.Log("Theme", "Title bar color set to " + hexColor);
        }
        catch (Exception ex)
        {
            Logger.Log("Theme", "Title bar color error: " + ex.Message);
        }
    }
    private IntPtr GetMainWindowHandle()
    {
        var mainWindow = _r.GetMainWindow();
        if (mainWindow == null) return IntPtr.Zero;
        try
        {
            var method = mainWindow.GetType().GetMethod("TryGetPlatformHandle");
            if (method != null)
            {
                var platformHandle = method.Invoke(mainWindow, null);
                if (platformHandle != null)
                {
                    var handleProp = platformHandle.GetType().GetProperty("Handle");
                    if (handleProp != null)
                        return (IntPtr)handleProp.GetValue(platformHandle)!;
                }
            }
        }
        catch { }
        return IntPtr.Zero;
    }
    public bool ApplyTheme(string name)
    {
        var themeName = name.ToLower().Trim();
        if (!Themes.TryGetValue(themeName, out var palette))
        {
            Logger.Log("Theme", "Unknown theme: " + name);
            return false;
        }
        TreeColorMaps.TryGetValue(themeName, out var treeMap);
        return ApplyThemeInternal(themeName, palette, treeMap);
    }
    public bool ApplyCustomTheme(string accentHex, string bgHex)
    {
        if (!ColorUtils.IsValidHex(accentHex) || !ColorUtils.IsValidHex(bgHex))
        {
            Logger.Log("Theme", "Invalid custom colors: accent=" + accentHex + " bg=" + bgHex);
            return false;
        }
        var palette = GenerateCustomTheme(accentHex, bgHex);
        var treeMap = GenerateCustomTreeColorMap(accentHex, bgHex);
        _customPalette = palette;
        _customAccent = accentHex;
        _customBg = bgHex;
        return ApplyThemeInternal("custom", palette, treeMap);
    }
    private Dictionary<string, string>? _customPalette;
    private string? _customAccent;
    private string? _customBg;
    private long _lastLiveUpdateTick;
    private Dictionary<string, object>? _liveBrushCache;
    public void UpdateCustomThemeLive(string accentHex, string bgHex)
    {
        if (!ColorUtils.IsValidHex(accentHex) || !ColorUtils.IsValidHex(bgHex))
            return;
        if (_activeThemeName != "custom" || _injectedDict == null)
        {
            Logger.Log("Theme", "Live preview: bootstrapping full custom apply (accent=" + accentHex + " bg=" + bgHex + ")");
            ApplyCustomTheme(accentHex, bgHex);
            _lastLiveUpdateTick = Environment.TickCount64;
            return;
        }
        long now = Environment.TickCount64;
        if (now - _lastLiveUpdateTick < 16) return;
        _lastLiveUpdateTick = now;
        var oldRawBg = _customBg;
        var oldRawAccent = _customAccent;
        var palette = GenerateCustomTheme(accentHex, bgHex);
        _customPalette = palette;
        _customAccent = accentHex;
        _customBg = bgHex;
        var styleRes = _r.GetStyleResources(0);
        if (styleRes != null)
        {
            foreach (var (key, hex) in palette)
            {
                try
                {
                    bool isBrush = key.Contains("Brush") || key.EndsWith("Fill");
                    if (isBrush)
                    {
                        var brush = _r.CreateBrush(hex);
                        if (brush != null) _r.AddResource(styleRes, key, brush);
                    }
                    else
                    {
                        var color = _r.ParseColor(hex);
                        if (color != null) _r.AddResource(styleRes, key, color);
                    }
                }
                catch { }
            }
        }
        if (_injectedDict != null)
        {
            foreach (var (key, hex) in palette)
            {
                try
                {
                    bool isBrush = key.Contains("Brush") || key.EndsWith("Fill");
                    if (isBrush)
                    {
                        var brush = _r.CreateBrush(hex);
                        if (brush != null) _r.AddResource(_injectedDict, key, brush);
                    }
                    else
                    {
                        var color = _r.ParseColor(hex);
                        if (color != null)
                        {
                            _r.AddResource(_injectedDict, key, color);
                            var brush = _r.CreateBrush(hex);
                            if (brush != null) _r.AddResource(_injectedDict, key + "Brush", brush);
                        }
                    }
                }
                catch { }
            }
        }
        var previousMap = _activeColorMap;
        var treeMap = GenerateCustomTreeColorMap(accentHex, bgHex);
        if (treeMap != null)
        {
            var combinedMap = new Dictionary<string, string>(treeMap, StringComparer.OrdinalIgnoreCase);
            if (previousMap != null)
            {
                foreach (var (origColor, prevReplacement) in previousMap)
                {
                    if (treeMap.TryGetValue(origColor, out var newReplacement))
                    {
                        if (!combinedMap.ContainsKey(prevReplacement) &&
                            !string.Equals(prevReplacement, newReplacement, StringComparison.OrdinalIgnoreCase))
                        {
                            combinedMap[prevReplacement] = newReplacement;
                        }
                    }
                }
            }
            foreach (var (staleReplacement, rootOrig) in _rootOriginals)
            {
                if (combinedMap.ContainsKey(staleReplacement)) continue;
                if (treeMap.TryGetValue(rootOrig, out var newReplacement))
                {
                    if (!string.Equals(staleReplacement, newReplacement, StringComparison.OrdinalIgnoreCase))
                        combinedMap[staleReplacement] = newReplacement;
                }
            }
            foreach (var (rootOrig, replacement) in treeMap)
            {
                if (!_rootOriginals.ContainsKey(replacement))
                    _rootOriginals[replacement] = rootOrig;
            }
            var oldAccent = NormalizeArgb(ContentPages.AccentGreen);
            var oldCardBg = NormalizeArgb(ContentPages.CardBg);
            var oldTextWhite = NormalizeArgb(ContentPages.TextWhite);
            var oldTextMuted = NormalizeArgb(ContentPages.TextMuted);
            var oldTextDim = NormalizeArgb(ContentPages.TextDim);
            var oldInactiveBorder = NormalizeArgb(ColorUtils.Lighten(ContentPages.CardBg, 12));
            var oldCardHover = NormalizeArgb(ColorUtils.Lighten(ContentPages.CardBg, 8));
            ContentPages.UpdateLiveColors(accentHex, bgHex, palette);
            var newAccent = NormalizeArgb(ContentPages.AccentGreen);
            var newCardBg = NormalizeArgb(ContentPages.CardBg);
            var newTextWhite = NormalizeArgb(ContentPages.TextWhite);
            var newTextMuted = NormalizeArgb(ContentPages.TextMuted);
            var newTextDim = NormalizeArgb(ContentPages.TextDim);
            var newInactiveBorder = NormalizeArgb(ColorUtils.Lighten(ContentPages.CardBg, 12));
            var newCardHover = NormalizeArgb(ColorUtils.Lighten(ContentPages.CardBg, 8));
            AddIfChanged(combinedMap, oldAccent, newAccent);
            AddIfChanged(combinedMap, oldCardBg, newCardBg);
            AddIfChanged(combinedMap, oldTextWhite, newTextWhite);
            AddIfChanged(combinedMap, oldTextMuted, newTextMuted);
            AddIfChanged(combinedMap, oldTextDim, newTextDim);
            AddIfChanged(combinedMap, oldInactiveBorder, newInactiveBorder);
            AddIfChanged(combinedMap, oldCardHover, newCardHover);
            if (oldRawBg != null)
                AddIfChanged(combinedMap, NormalizeArgb(oldRawBg), NormalizeArgb(bgHex));
            if (oldRawAccent != null)
                AddIfChanged(combinedMap, NormalizeArgb(oldRawAccent), NormalizeArgb(accentHex));
            _activeColorMap = combinedMap;
            _reverseColorMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var (orig, repl) in combinedMap)
            {
                if (!_reverseColorMap.ContainsKey(repl))
                    _reverseColorMap[repl] = orig;
            }
        }
        try
        {
            _liveBrushCache = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            var sw = System.Diagnostics.Stopwatch.StartNew();
            int liveRecolored = WalkAllWindows();
            sw.Stop();
            _liveBrushCache = null;
            if (liveRecolored > 0)
                Logger.Log("Theme", "Live walk: " + liveRecolored + " recolored in " + sw.ElapsedMilliseconds + "ms");
        }
        catch { _liveBrushCache = null; }
        if (palette.TryGetValue("SolidBackgroundFillColorBase", out var titleBarBg))
            UpdateTitleBarColor(titleBarBg);
    }
    private bool ApplyThemeInternal(string themeName,
        Dictionary<string, string> palette,
        Dictionary<string, string>? treeColorMap)
    {
        Logger.Log("Theme", "Applying theme: " + themeName + " (" + palette.Count + " resource overrides)");
        var previousColorMap = _activeColorMap;
        var previousThemeName = _activeThemeName;
        RevertTheme();
        var styleRes = _r.GetStyleResources(0);
        int styleOverrides = 0;
        if (styleRes != null)
        {
            Logger.Log("Theme", "Injecting into Styles[0].Resources...");
            foreach (var (key, hex) in palette)
            {
                try
                {
                    if (!_savedOriginals.ContainsKey(key) && !_addedKeys.Contains(key))
                    {
                        try
                        {
                            var original = _r.GetResource(styleRes, key);
                            if (original != null)
                                _savedOriginals[key] = original;
                            else
                                _addedKeys.Add(key);
                        }
                        catch
                        {
                            _addedKeys.Add(key);
                        }
                    }
                    bool isBrush = key.Contains("Brush") || key.EndsWith("Fill");
                    if (isBrush)
                    {
                        var brush = _r.CreateBrush(hex);
                        if (brush != null)
                        {
                            _r.AddResource(styleRes, key, brush);
                            styleOverrides++;
                        }
                    }
                    else
                    {
                        var color = _r.ParseColor(hex);
                        if (color != null)
                        {
                            _r.AddResource(styleRes, key, color);
                            styleOverrides++;
                        }
                    }
                }
                catch (Exception ex)
                {
                    Logger.Log("Theme", "  Style override failed for " + key + ": " + ex.Message);
                }
            }
            Logger.Log("Theme", "Styles[0].Resources: " + styleOverrides + " overrides applied, " + _savedOriginals.Count + " originals saved");
        }
        else
        {
            Logger.Log("Theme", "WARNING: Could not get Styles[0].Resources");
        }
        var resources = _r.GetAppResources();
        var mergedDicts = resources != null ? _r.GetMergedDictionaries(resources) : null;
        int mergedAdded = 0;
        if (mergedDicts != null)
        {
            var dict = _r.CreateResourceDictionary();
            if (dict != null)
            {
                foreach (var (key, hex) in palette)
                {
                    try
                    {
                        bool isBrush = key.Contains("Brush") || key.EndsWith("Fill");
                        if (isBrush)
                        {
                            var brush = _r.CreateBrush(hex);
                            if (brush != null)
                            {
                                _r.AddResource(dict, key, brush);
                                mergedAdded++;
                            }
                        }
                        else
                        {
                            var color = _r.ParseColor(hex);
                            if (color != null)
                            {
                                _r.AddResource(dict, key, color);
                                mergedAdded++;
                                var brush = _r.CreateBrush(hex);
                                if (brush != null)
                                {
                                    _r.AddResource(dict, key + "Brush", brush);
                                    mergedAdded++;
                                }
                            }
                        }
                    }
                    catch { }
                }
                mergedDicts.Add(dict);
                _injectedDict = dict;
            }
        }
        _activeThemeName = themeName;
        Logger.Log("Theme", "Theme applied: " + styleOverrides + " style overrides + " + mergedAdded + " merged dict entries");
        if (treeColorMap != null)
        {
            var combinedMap = new Dictionary<string, string>(treeColorMap, StringComparer.OrdinalIgnoreCase);
            int crossMapped = 0;
            foreach (var (rootOrig, replacement) in treeColorMap)
            {
                if (!_rootOriginals.ContainsKey(replacement))
                    _rootOriginals[replacement] = rootOrig;
            }
            if (previousColorMap != null && previousThemeName != null)
            {
                foreach (var (origColor, prevReplacement) in previousColorMap)
                {
                    if (treeColorMap.TryGetValue(origColor, out var newReplacement))
                    {
                        if (!combinedMap.ContainsKey(prevReplacement) &&
                            !string.Equals(prevReplacement, newReplacement, StringComparison.OrdinalIgnoreCase))
                        {
                            combinedMap[prevReplacement] = newReplacement;
                            crossMapped++;
                        }
                    }
                }
                if (crossMapped > 0)
                    Logger.Log("Theme", "Cross-mapped " + crossMapped + " colors from " + previousThemeName + " -> " + themeName);
            }
            int staleMapped = 0;
            foreach (var (staleReplacement, rootOrig) in _rootOriginals)
            {
                if (combinedMap.ContainsKey(staleReplacement)) continue;
                if (treeColorMap.TryGetValue(rootOrig, out var newReplacement))
                {
                    if (!string.Equals(staleReplacement, newReplacement, StringComparison.OrdinalIgnoreCase))
                    {
                        combinedMap[staleReplacement] = newReplacement;
                        staleMapped++;
                    }
                }
            }
            if (staleMapped > 0)
                Logger.Log("Theme", "Stale-mapped " + staleMapped + " colors from _rootOriginals");
            _activeColorMap = combinedMap;
            _reverseColorMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var (orig, repl) in combinedMap)
            {
                if (!_reverseColorMap.ContainsKey(repl))
                    _reverseColorMap[repl] = orig;
            }
            Logger.Log("Theme", "Color map loaded: " + combinedMap.Count + " mappings (" + treeColorMap.Count + " base + " + crossMapped + " cross-mapped)");
        }
        try
        {
            int initial = WalkAllWindows();
            Logger.Log("Theme", "Immediate walk: " + initial + " recolored");
        }
        catch { }
        ScheduleVisualTreeWalks();
        InstallLayoutInterceptor();
        var auditMap = _activeColorMap;
        var auditReverse = _reverseColorMap;
        var auditName = themeName;
        System.Threading.ThreadPool.QueueUserWorkItem(_ =>
        {
            Thread.Sleep(1500);
            if (_activeThemeName != auditName) return;
            _r.RunOnUIThread(() =>
            {
                try { RunColorAudit(auditMap, auditReverse, auditName); }
                catch (Exception ex) { Logger.Log("Theme", "Audit error: " + ex.Message); }
            });
        });
        if (palette.TryGetValue("SolidBackgroundFillColorBase", out var titleBarBg))
            UpdateTitleBarColor(titleBarBg);
        return true;
    }
    private System.Threading.Timer? _walkTimer;
    private int _walkCount;
    private bool _layoutInterceptorInstalled;
    private long _lastLayoutWalkTick;
    private void ScheduleVisualTreeWalks()
    {
        _walkCount = 0;
        _walkTimer?.Dispose();
        _walkTimer = new System.Threading.Timer(_ =>
        {
            _walkCount++;
            _r.RunOnUIThread(() =>
            {
                try
                {
                    int recolored = WalkAllWindows();
                    if (recolored > 0)
                        Logger.Log("Theme", "Walk #" + _walkCount + ": " + recolored + " recolored");
                }
                catch (Exception ex)
                {
                    Logger.Log("Theme", "Walk error: " + ex.Message);
                }
            });
        }, null, 200, 500);
    }
    private void InstallLayoutInterceptor()
    {
        if (_layoutInterceptorInstalled) return;
        var mainWindow = _r.GetMainWindow();
        if (mainWindow == null) return;
        try
        {
            _r.SubscribeEvent(mainWindow, "LayoutUpdated", () =>
            {
                if (_activeColorMap == null) return;
                long now = Environment.TickCount64;
                if (now - _lastLayoutWalkTick < 80) return;
                _lastLayoutWalkTick = now;
                try
                {
                    int recolored = WalkAllWindows();
                    if (recolored > 0)
                        Logger.Log("Theme", "Layout intercept: " + recolored + " recolored");
                }
                catch { }
            });
            _layoutInterceptorInstalled = true;
            Logger.Log("Theme", "Layout interceptor installed on MainWindow");
        }
        catch (Exception ex)
        {
            Logger.Log("Theme", "Layout interceptor install failed: " + ex.Message);
        }
    }
    private void ScheduleRapidFollowUp()
    {
        System.Threading.ThreadPool.QueueUserWorkItem(_ =>
        {
            foreach (var delayMs in new[] { 200, 500, 1000 })
            {
                Thread.Sleep(delayMs);
                if (_activeColorMap == null) return;
                _r.RunOnUIThread(() =>
                {
                    try
                    {
                        int recolored = WalkAllWindows();
                        if (recolored > 0)
                            Logger.Log("Theme", "Rapid follow-up (+" + delayMs + "ms): " + recolored + " recolored");
                    }
                    catch { }
                });
            }
        });
    }
    private int ComputeTreeFingerprint(object mainWindow)
    {
        int hash = 0;
        int count = 0;
        try
        {
            foreach (var c1 in _r.GetVisualChildren(mainWindow))
            {
                hash = hash * 31 + c1.GetType().Name.GetHashCode();
                foreach (var c2 in _r.GetVisualChildren(c1))
                {
                    count++;
                    foreach (var c3 in _r.GetVisualChildren(c2))
                    {
                        count++;
                        foreach (var c4 in _r.GetVisualChildren(c3))
                            count++;
                    }
                }
            }
        }
        catch { }
        return hash ^ (count * 997);
    }
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
    private void RunColorAudit(Dictionary<string, string>? activeMap, Dictionary<string, string>? reverseMap, string themeName)
    {
        if (activeMap == null) return;
        var staleColors = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var unmappedColors = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        int totalProps = 0;
        int matchedProps = 0;
        var originals = new HashSet<string>(activeMap.Keys, StringComparer.OrdinalIgnoreCase);
        foreach (var topLevel in _r.GetAllTopLevels())
        {
            AuditNode(topLevel, 0, activeMap, reverseMap, originals, staleColors, unmappedColors, ref totalProps, ref matchedProps);
        }
        Logger.Log("Theme", $"=== COLOR AUDIT ({themeName}) after 1.5s ===");
        Logger.Log("Theme", $"  Total color props scanned: {totalProps}");
        Logger.Log("Theme", $"  Still matching original (need recolor): {matchedProps}");
        if (staleColors.Count > 0)
        {
            int staleTotal = 0;
            foreach (var v in staleColors.Values) staleTotal += v;
            var topStale = staleColors.OrderByDescending(kv => kv.Value).Take(10);
            Logger.Log("Theme", $"  --- STALE (original Root colors still present, {staleTotal} total) ---");
            foreach (var (key, freq) in topStale)
                Logger.Log("Theme", $"    [{freq}x] {key}");
        }
        else
        {
            Logger.Log("Theme", "  No stale original colors found (good!)");
        }
        Logger.Log("Theme", $"=== END AUDIT ===");
    }
    private void AuditNode(object visual, int depth,
        Dictionary<string, string> activeMap, Dictionary<string, string>? reverseMap,
        HashSet<string> originals,
        Dictionary<string, int> staleColors,
        Dictionary<string, int> unmappedColors,
        ref int totalProps, ref int matchedProps)
    {
        if (depth > 50) return;
        if (_r.GetTag(visual) == "uprooted-no-recolor") return;
        try
        {
            foreach (var propName in new[] { "Background", "Foreground", "BorderBrush" })
            {
                var prop = visual.GetType().GetProperty(propName);
                if (prop == null) continue;
                try
                {
                    var brush = prop.GetValue(visual);
                    if (brush == null) continue;
                    var colorStr = GetBrushColorString(brush);
                    if (colorStr == null) continue;
                    totalProps++;
                    if (originals.Contains(colorStr))
                    {
                        matchedProps++;
                        var key = propName + ":" + colorStr + " on " + visual.GetType().Name;
                        staleColors.TryGetValue(key, out int ex);
                        staleColors[key] = ex + 1;
                    }
                }
                catch { }
            }
        }
        catch { }
        foreach (var child in _r.GetVisualChildren(visual))
        {
            AuditNode(child, depth + 1, activeMap, reverseMap, originals, staleColors, unmappedColors, ref totalProps, ref matchedProps);
        }
    }
    public void WalkVisualTreeNow()
    {
        if (_activeThemeName == null || _activeColorMap == null) return;
        int recolored = WalkAllWindows();
        if (recolored > 0)
            Logger.Log("Theme", "Manual walk: " + recolored + " recolored");
    }
    public void ScheduleWalkBurst()
    {
        if (_activeThemeName == null || _activeColorMap == null) return;
        _r.RunOnUIThread(() =>
        {
            try { WalkVisualTreeNow(); }
            catch { }
        });
        ScheduleRapidFollowUp();
    }
    private static readonly Dictionary<string, Dictionary<string, string>> TreeColorMaps = new()
    {
        ["crimson"] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["#ff3b6af8"] = "#ffc42b1c",
            ["#ff4a78f9"] = "#ffd94a3d",
            ["#ff2e59d1"] = "#ffa32417",
            ["#ff2148af"] = "#ff821d12",
            ["#ff5b88ff"] = "#ffe06b60",
            ["#ff3366ff"] = "#ffda4b3e",
            ["#663b6af8"] = "#66c42b1c",
            ["#333b6af8"] = "#33c42b1c",
            ["#193b6af8"] = "#19c42b1c",
            ["#ff0f1923"] = "#ff2a1818",
            ["#ff0d1521"] = "#ff241414",
            ["#ff07101b"] = "#ff1a0e0e",
            ["#ff090e13"] = "#ff1e1010",
            ["#ff0a1a2e"] = "#ff241212",
            ["#ff101c2e"] = "#ff2a1616",
            ["#ff121a26"] = "#ff2c1818",
            ["#ff141e2b"] = "#ff2e1a1a",
            ["#ff282828"] = "#ff302020",
            ["#ff4f5c6f"] = "#ff6f5050",
            ["#ff242c36"] = "#ff402828",
            ["#ff1a2230"] = "#ff341e1e",
            ["#ff505050"] = "#ff504040",
            ["#a3f2f2f2"] = "#a3f0dada",
            ["#66f2f2f2"] = "#66f0dada",
            ["#ffdedede"] = "#fff0dada",
            ["#fff2f2f2"] = "#fff8eaea",
        },
        ["loki"] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["#ff3b6af8"] = "#ff2a5a40",
            ["#ff4a78f9"] = "#ff3d7050",
            ["#ff2e59d1"] = "#ff1e402f",
            ["#ff2148af"] = "#ff112318",
            ["#ff5b88ff"] = "#ff508a62",
            ["#ff3366ff"] = "#ff3e7151",
            ["#663b6af8"] = "#662a5a40",
            ["#333b6af8"] = "#332a5a40",
            ["#193b6af8"] = "#192a5a40",
            ["#ff0f1923"] = "#ff171c17",
            ["#ff0d1521"] = "#ff0f1210",
            ["#ff07101b"] = "#ff0a0d0a",
            ["#ff090e13"] = "#ff0c0f0c",
            ["#ff0a1a2e"] = "#ff0d100d",
            ["#ff101c2e"] = "#ff151a15",
            ["#ff121a26"] = "#ff131813",
            ["#ff141e2b"] = "#ff1a1f1a",
            ["#ff282828"] = "#ff1e231e",
            ["#ff4f5c6f"] = "#ff4a5a42",
            ["#ff242c36"] = "#ff3d4a35",
            ["#ff1a2230"] = "#ff2a3528",
            ["#ff505050"] = "#ff3e4b36",
            ["#a3f2f2f2"] = "#a3f0ece0",
            ["#66f2f2f2"] = "#66f0ece0",
            ["#ffdedede"] = "#ffe0d8c8",
            ["#fff2f2f2"] = "#fff0ece0",
        },
    };
    private Dictionary<string, string>? _activeColorMap;
    private Dictionary<string, string>? _reverseColorMap;
    private int WalkAndRecolor(object visual, int depth, Dictionary<string, int>? colorCounts = null)
    {
        if (_activeColorMap == null) return 0;
        var pending = new List<(object control, System.Reflection.PropertyInfo prop, string replacement)>();
        CollectColorChanges(visual, 0, pending, colorCounts);
        int count = 0;
        foreach (var (control, prop, replacement) in pending)
        {
            try
            {
                object? newBrush = null;
                if (_liveBrushCache != null)
                {
                    if (!_liveBrushCache.TryGetValue(replacement, out newBrush))
                    {
                        newBrush = _r.CreateBrush(replacement);
                        if (newBrush != null)
                            _liveBrushCache[replacement] = newBrush;
                    }
                }
                else
                {
                    newBrush = _r.CreateBrush(replacement);
                }
                if (newBrush == null) continue;
                if (_liveBrushCache != null)
                {
                    prop.SetValue(control, newBrush);
                }
                else
                {
                    var fieldName = AvaloniaReflection.PropertyToFieldName(prop.Name);
                    if (!_r.SetValueStylePriority(control, fieldName, newBrush))
                        prop.SetValue(control, newBrush);
                }
                count++;
            }
            catch { }
        }
        return count;
    }
    private void CollectColorChanges(object visual, int depth,
        List<(object, System.Reflection.PropertyInfo, string)> pending,
        Dictionary<string, int>? colorCounts)
    {
        if (depth > 50 || _activeColorMap == null) return;
        var tag = _r.GetTag(visual);
        if (tag == "uprooted-no-recolor") return;
        try
        {
            foreach (var propName in new[] { "Background", "Foreground", "BorderBrush", "Fill" })
            {
                var prop = visual.GetType().GetProperty(propName);
                if (prop == null) continue;
                try
                {
                    var brush = prop.GetValue(visual);
                    if (brush == null) continue;
                    var colorStr = GetBrushColorString(brush);
                    if (colorStr == null) continue;
                    if (colorCounts != null)
                    {
                        var key = propName + ":" + colorStr;
                        colorCounts.TryGetValue(key, out int existing);
                        colorCounts[key] = existing + 1;
                    }
                    if (_activeColorMap.TryGetValue(colorStr, out var replacement))
                    {
                        pending.Add((visual, prop, replacement));
                    }
                }
                catch { }
            }
        }
        catch { }
        foreach (var child in _r.GetVisualChildren(visual))
        {
            CollectColorChanges(child, depth + 1, pending, colorCounts);
        }
    }
    private static string NormalizeArgb(string hex)
    {
        var h = hex.TrimStart('#');
        if (h.Length == 6) h = "FF" + h;
        return "#" + h.ToUpperInvariant();
    }
    private static void AddIfChanged(Dictionary<string, string> map, string oldColor, string newColor)
    {
        if (string.Equals(oldColor, newColor, StringComparison.OrdinalIgnoreCase)) return;
        if (!map.ContainsKey(oldColor))
            map[oldColor] = newColor;
    }
    private string? GetBrushColorString(object brush)
    {
        try
        {
            var colorProp = brush.GetType().GetProperty("Color");
            if (colorProp == null) return null;
            var color = colorProp.GetValue(brush);
            return color?.ToString();
        }
        catch { return null; }
    }
    private int WalkAndRestore(object visual, int depth)
    {
        if (depth > 50 || _reverseColorMap == null) return 0;
        if (_r.GetTag(visual) == "uprooted-no-recolor") return 0;
        int count = 0;
        try
        {
            foreach (var propName in new[] { "Background", "Foreground", "BorderBrush", "Fill" })
            {
                var prop = visual.GetType().GetProperty(propName);
                if (prop == null) continue;
                try
                {
                    var brush = prop.GetValue(visual);
                    if (brush == null) continue;
                    var colorStr = GetBrushColorString(brush);
                    if (colorStr == null) continue;
                    if (_reverseColorMap.TryGetValue(colorStr, out var original))
                    {
                        var fieldName = AvaloniaReflection.PropertyToFieldName(propName);
                        _r.ClearValueSilent(visual, fieldName);
                        var newBrush = prop.GetValue(visual);
                        var newColor = newBrush != null ? GetBrushColorString(newBrush) : null;
                        if (newColor == null ||
                            string.Equals(newColor, colorStr, StringComparison.OrdinalIgnoreCase))
                        {
                            var originalBrush = _r.CreateBrush(original);
                            if (originalBrush != null)
                                _r.SetValueStylePriority(visual, fieldName, originalBrush);
                        }
                        count++;
                    }
                }
                catch { }
            }
        }
        catch { }
        foreach (var child in _r.GetVisualChildren(visual))
        {
            count += WalkAndRestore(child, depth + 1);
        }
        return count;
    }
    public void RevertTheme()
    {
        _walkTimer?.Dispose();
        _walkTimer = null;
        var savedActiveMap = _activeColorMap;
        _activeColorMap = null;
        var styleRes = _r.GetStyleResources(0);
        if (styleRes != null)
        {
            if (_savedOriginals.Count > 0)
            {
                int restored = 0;
                foreach (var (key, original) in _savedOriginals)
                {
                    try
                    {
                        _r.AddResource(styleRes, key, original);
                        restored++;
                    }
                    catch (Exception ex)
                    {
                        Logger.Log("Theme", "  Restore failed for " + key + ": " + ex.Message);
                    }
                }
                Logger.Log("Theme", "Restored " + restored + " original resources in Styles[0]");
            }
            if (_addedKeys.Count > 0)
            {
                int removed = 0;
                foreach (var key in _addedKeys)
                {
                    try
                    {
                        if (_r.RemoveResource(styleRes, key))
                            removed++;
                    }
                    catch { }
                }
                Logger.Log("Theme", "Removed " + removed + "/" + _addedKeys.Count + " added keys from Styles[0]");
            }
        }
        if (_injectedDict != null)
        {
            try
            {
                var resources = _r.GetAppResources();
                var mergedDicts = _r.GetMergedDictionaries(resources);
                if (mergedDicts != null)
                {
                    mergedDicts.Remove(_injectedDict);
                    Logger.Log("Theme", "Removed MergedDictionary, count now " + mergedDicts.Count);
                }
            }
            catch (Exception ex)
            {
                Logger.Log("Theme", "RevertTheme MergedDict error: " + ex.Message);
            }
            _injectedDict = null;
        }
        var purgeColors = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (savedActiveMap != null)
        {
            foreach (var (orig, repl) in savedActiveMap)
            {
                purgeColors.Add(orig);
                purgeColors.Add(repl);
            }
        }
        foreach (var (replacement, rootOrig) in _rootOriginals)
        {
            purgeColors.Add(replacement);
            purgeColors.Add(rootOrig);
        }
        if (_reverseColorMap != null)
        {
            foreach (var (repl, orig) in _reverseColorMap)
            {
                purgeColors.Add(repl);
                purgeColors.Add(orig);
            }
        }
        try
        {
            _purgeNullFallbacks = 0;
            _purgeOrphans = 0;
            _purgeOrphanColors = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            int purged = 0;
            foreach (var topLevel in _r.GetAllTopLevels())
            {
                try { purged += PurgeKnownColors(topLevel, 0, purgeColors); }
                catch { }
            }
            Logger.Log("Theme", "Targeted purge: " + purged + " cleared, " +
                _purgeNullFallbacks + " null-fallbacks, " +
                _purgeOrphans + " orphan props (" + purgeColors.Count + " known colors)");
            if (_purgeOrphanColors.Count > 0)
            {
                var topOrphans = _purgeOrphanColors
                    .OrderByDescending(kv => kv.Value)
                    .Take(15);
                Logger.Log("Theme", "--- ORPHAN COLORS (not in known set, left untouched) ---");
                foreach (var (key, freq) in topOrphans)
                    Logger.Log("Theme", $"  [{freq}x] {key}");
            }
            _purgeOrphanColors = null;
        }
        catch (Exception ex)
        {
            Logger.Log("Theme", "Targeted purge error: " + ex.Message);
        }
        _savedOriginals.Clear();
        _addedKeys.Clear();
        UpdateTitleBarColor(DefaultDarkBg);
        _activeThemeName = null;
        _reverseColorMap = null;
    }
    private void ScheduleRevertFollowUps(Dictionary<string, string> reverseMap)
    {
        System.Threading.ThreadPool.QueueUserWorkItem(_ =>
        {
            foreach (var delayMs in new[] { 500, 1500, 3000 })
            {
                Thread.Sleep(delayMs);
                if (_activeThemeName != null) return;
                _r.RunOnUIThread(() =>
                {
                    try
                    {
                        int restored = 0;
                        foreach (var topLevel in _r.GetAllTopLevels())
                        {
                            try { restored += WalkAndRestoreWithMap(topLevel, 0, reverseMap); }
                            catch { }
                        }
                        if (restored > 0)
                            Logger.Log("Theme", "Revert follow-up (+" + delayMs + "ms): " + restored + " restored");
                    }
                    catch { }
                });
            }
        });
    }
    private int _purgeNullFallbacks;
    private int _purgeOrphans;
    private Dictionary<string, int>? _purgeOrphanColors;
    private int PurgeKnownColors(object visual, int depth, HashSet<string> knownColors)
    {
        if (depth > 50) return 0;
        if (_r.GetTag(visual) == "uprooted-no-recolor") return 0;
        int count = 0;
        try
        {
            foreach (var propName in new[] { "Background", "Foreground", "BorderBrush", "Fill" })
            {
                try
                {
                    var prop = visual.GetType().GetProperty(propName);
                    if (prop == null) continue;
                    var brush = prop.GetValue(visual);
                    if (brush == null) continue;
                    var colorStr = GetBrushColorString(brush);
                    if (colorStr == null) continue;
                    if (knownColors.Contains(colorStr))
                    {
                        var fieldName = AvaloniaReflection.PropertyToFieldName(propName);
                        if (_r.ClearValueSilent(visual, fieldName))
                        {
                            var newBrush = prop.GetValue(visual);
                            if (newBrush == null)
                            {
                                _purgeNullFallbacks++;
                                string restoreColor = colorStr;
                                if (_rootOriginals.TryGetValue(colorStr, out var rootOrig))
                                    restoreColor = rootOrig;
                                else if (_reverseColorMap != null && _reverseColorMap.TryGetValue(colorStr, out var revOrig))
                                    restoreColor = revOrig;
                                var restoreBrush = _r.CreateBrush(restoreColor);
                                if (restoreBrush != null)
                                    _r.SetValueStylePriority(visual, fieldName, restoreBrush);
                                if (_purgeNullFallbacks <= 10)
                                    Logger.Log("Theme", $"  PURGE NULL: {visual.GetType().Name}.{propName} was {colorStr} -> restored {restoreColor} (root orig)");
                            }
                            count++;
                        }
                    }
                    else
                    {
                        if (_purgeOrphanColors != null)
                        {
                            var key = propName + ":" + colorStr;
                            _purgeOrphanColors.TryGetValue(key, out int existing);
                            _purgeOrphanColors[key] = existing + 1;
                            _purgeOrphans++;
                        }
                    }
                }
                catch { }
            }
        }
        catch { }
        foreach (var child in _r.GetVisualChildren(visual))
        {
            count += PurgeKnownColors(child, depth + 1, knownColors);
        }
        return count;
    }
    private int WalkAndRestoreWithMap(object visual, int depth, Dictionary<string, string> reverseMap)
    {
        if (depth > 50) return 0;
        if (_r.GetTag(visual) == "uprooted-no-recolor") return 0;
        int count = 0;
        try
        {
            foreach (var propName in new[] { "Background", "Foreground", "BorderBrush", "Fill" })
            {
                var prop = visual.GetType().GetProperty(propName);
                if (prop == null) continue;
                try
                {
                    var brush = prop.GetValue(visual);
                    if (brush == null) continue;
                    var colorStr = GetBrushColorString(brush);
                    if (colorStr == null) continue;
                    if (reverseMap.TryGetValue(colorStr, out var original))
                    {
                        var fieldName = AvaloniaReflection.PropertyToFieldName(propName);
                        _r.ClearValueSilent(visual, fieldName);
                        var newBrush = prop.GetValue(visual);
                        var newColor = newBrush != null ? GetBrushColorString(newBrush) : null;
                        if (newColor == null ||
                            string.Equals(newColor, colorStr, StringComparison.OrdinalIgnoreCase))
                        {
                            var originalBrush = _r.CreateBrush(original);
                            if (originalBrush != null)
                                _r.SetValueStylePriority(visual, fieldName, originalBrush);
                        }
                        count++;
                    }
                }
                catch { }
            }
        }
        catch { }
        foreach (var child in _r.GetVisualChildren(visual))
        {
            count += WalkAndRestoreWithMap(child, depth + 1, reverseMap);
        }
        return count;
    }
    public void DumpVisualTreeColors()
    {
        var mainWindow = _r.GetMainWindow();
        if (mainWindow == null)
        {
            Logger.Log("Theme", "DumpVisualTreeColors: no MainWindow");
            return;
        }
        var colorCounts = new Dictionary<string, int>();
        var typeCounts = new Dictionary<string, int>();
        var nodeCounter = new int[] { 0 };
        var topLevels = _r.GetAllTopLevels();
        Logger.Log("Theme", "DumpVisualTreeColors: scanning " + topLevels.Count + " TopLevel instances");
        foreach (var tl in topLevels)
        {
            Logger.Log("Theme", "  TopLevel: " + tl.GetType().FullName);
            ScanColors(tl, colorCounts, 0, typeCounts, nodeCounter);
        }
        int totalNodes = nodeCounter[0];
        var sorted = colorCounts.OrderByDescending(kv => kv.Value).ToList();
        Logger.Log("Theme", "=== VISUAL TREE COLOR DUMP (" + sorted.Count + " unique combos, " + totalNodes + " total nodes) ===");
        int logged = 0;
        foreach (var (key, freq) in sorted)
        {
            if (logged >= 80) break;
            Logger.Log("Theme", "  [" + freq + "x] " + key);
            logged++;
        }
        var sortedTypes = typeCounts.OrderByDescending(kv => kv.Value).Take(20).ToList();
        Logger.Log("Theme", "--- TOP CONTROL TYPES ---");
        foreach (var (typeName, count) in sortedTypes)
            Logger.Log("Theme", "  [" + count + "x] " + typeName);
        Logger.Log("Theme", "--- BROWSER/WEB CONTROLS ---");
        var webControls = new List<string>();
        FindWebControls(mainWindow, webControls, 0);
        if (webControls.Count == 0)
            Logger.Log("Theme", "  (none found)");
        else
            foreach (var wc in webControls)
                Logger.Log("Theme", "  " + wc);
        Logger.Log("Theme", "=== END COLOR DUMP ===");
    }
    private void FindWebControls(object visual, List<string> results, int depth)
    {
        if (depth > 50) return;
        var fullName = visual.GetType().FullName ?? "";
        var name = visual.GetType().Name;
        if (name.Contains("Browser") || name.Contains("Web") || name.Contains("Chromium")
            || name.Contains("Cef") || fullName.Contains("DotNetBrowser"))
        {
            var size = "";
            try
            {
                var boundsProp = visual.GetType().GetProperty("Bounds");
                if (boundsProp != null)
                    size = " Bounds=" + boundsProp.GetValue(visual);
            }
            catch { }
            results.Add("depth=" + depth + " " + fullName + size);
        }
        foreach (var child in _r.GetVisualChildren(visual))
            FindWebControls(child, results, depth + 1);
    }
    private void ScanColors(object visual, Dictionary<string, int> colorCounts, int depth,
        Dictionary<string, int>? typeCounts = null, int[]? nodeCounter = null)
    {
        if (depth > 50) return;
        if (nodeCounter != null) nodeCounter[0]++;
        if (typeCounts != null)
        {
            var typeName = visual.GetType().Name;
            typeCounts.TryGetValue(typeName, out int tc);
            typeCounts[typeName] = tc + 1;
        }
        try
        {
            foreach (var propName in new[] { "Background", "Foreground", "BorderBrush", "Fill" })
            {
                var prop = visual.GetType().GetProperty(propName);
                if (prop == null) continue;
                try
                {
                    var brush = prop.GetValue(visual);
                    if (brush == null) continue;
                    var colorStr = GetBrushColorString(brush);
                    if (colorStr == null) continue;
                    var key = propName + ":" + colorStr;
                    colorCounts.TryGetValue(key, out int existing);
                    colorCounts[key] = existing + 1;
                }
                catch { }
            }
        }
        catch { }
        foreach (var child in _r.GetVisualChildren(visual))
            ScanColors(child, colorCounts, depth + 1, typeCounts, nodeCounter);
    }
    public void DumpVisualTreeStructure()
    {
        var mainWindow = _r.GetMainWindow();
        if (mainWindow == null) return;
        Logger.Log("Theme", "=== VISUAL TREE STRUCTURE ===");
        DumpNode(mainWindow, 0, 6);
        Logger.Log("Theme", "=== END STRUCTURE ===");
    }
    private void DumpNode(object visual, int depth, int maxDetailDepth)
    {
        if (depth > 20) return;
        var typeName = visual.GetType().Name;
        var fullTypeName = visual.GetType().FullName ?? typeName;
        var children = _r.GetVisualChildren(visual);
        var childCount = 0;
        foreach (var _ in children) childCount++;
        var indent = new string(' ', depth * 2);
        var bgStr = "";
        try
        {
            var bgProp = visual.GetType().GetProperty("Background");
            if (bgProp != null)
            {
                var brush = bgProp.GetValue(visual);
                if (brush != null)
                {
                    var color = GetBrushColorString(brush);
                    if (color != null) bgStr = " bg=" + color;
                }
            }
        }
        catch { }
        var displayName = fullTypeName.Contains("DotNet") || fullTypeName.Contains("Browser")
            || fullTypeName.Contains("Cef") || fullTypeName.Contains("Chromium")
            ? fullTypeName : typeName;
        Logger.Log("Theme", indent + displayName + " [" + childCount + "]" + bgStr);
        int shown = 0;
        foreach (var child in _r.GetVisualChildren(visual))
        {
            if (shown >= 8 && depth >= maxDetailDepth)
            {
                Logger.Log("Theme", indent + "  ... (" + (childCount - shown) + " more)");
                break;
            }
            DumpNode(child, depth + 1, maxDetailDepth);
            shown++;
        }
    }
    public void DumpResourceKeys()
    {
        Logger.Log("Theme", "=== RESOURCE KEY DUMP ===");
        var styleRes = _r.GetStyleResources(0);
        if (styleRes != null)
        {
            Logger.Log("Theme", "Styles[0].Resources type: " + styleRes.GetType().FullName);
            int count = 0;
            _r.EnumerateResources(styleRes, (k, v) =>
            {
                if (count < 60)
                    Logger.Log("Theme", "  [S0] [" + (v?.GetType().Name ?? "null") + "] " + k + " = " + v);
                count++;
            });
            Logger.Log("Theme", "Styles[0].Resources total: " + count);
        }
        var appRes = _r.GetAppResources();
        if (appRes != null)
        {
            int count = 0;
            _r.EnumerateResources(appRes, (k, v) =>
            {
                if (count < 30)
                    Logger.Log("Theme", "  [AR] [" + (v?.GetType().Name ?? "null") + "] " + k + " = " + v);
                count++;
            });
            Logger.Log("Theme", "Application.Resources total: " + count);
            var merged = _r.GetMergedDictionaries(appRes);
            if (merged != null)
            {
                Logger.Log("Theme", "MergedDictionaries count: " + merged.Count);
                for (int i = 0; i < merged.Count && i < 5; i++)
                {
                    var dict = merged[i];
                    int dcount = 0;
                    _r.EnumerateResources(dict, (k, v) =>
                    {
                        if (dcount < 15)
                            Logger.Log("Theme", "  [MD" + i + "] [" + (v?.GetType().Name ?? "null") + "] " + k + " = " + v);
                        dcount++;
                    });
                    Logger.Log("Theme", "  MergedDict[" + i + "] total: " + dcount);
                }
            }
        }
        Logger.Log("Theme", "=== END RESOURCE KEY DUMP ===");
    }
    public Dictionary<string, string>? GetPalette()
    {
        if (_activeThemeName == "custom" && _customPalette != null)
            return _customPalette;
        if (_activeThemeName != null && Themes.TryGetValue(_activeThemeName, out var p))
            return p;
        return null;
    }
    public string GetAccentColor()
    {
        if (_activeThemeName == "custom" && _customAccent != null)
            return _customAccent;
        if (_activeThemeName != null && Themes.TryGetValue(_activeThemeName, out var p))
        {
            if (p.TryGetValue("ThemeAccentColor", out var hex)) return hex;
            if (p.TryGetValue("SystemAccentColor", out hex)) return hex;
        }
        return "#3B6AF8";
    }
    public string GetBgPrimary()
    {
        if (_activeThemeName == "custom" && _customPalette != null)
        {
            if (_customPalette.TryGetValue("SolidBackgroundFillColorBase", out var hex)) return hex;
            if (_customBg != null) return _customBg;
        }
        if (_activeThemeName != null && Themes.TryGetValue(_activeThemeName, out var p))
        {
            if (p.TryGetValue("SolidBackgroundFillColorBase", out var hex)) return hex;
        }
        return "#0D1521";
    }
    private static Dictionary<string, string> GenerateCustomTheme(string accent, string bg)
    {
        var (ah, asat, al) = ColorUtils.RgbToHsl(accent);
        var (bh, bsat, bl) = ColorUtils.RgbToHsl(bg);
        double cappedAsat = Math.Min(asat, 0.88);
        double cappedAl = Math.Clamp(al, 0.02, 0.65);
        var accentLight1 = ColorUtils.HslToHex(ah, Math.Min(0.88, cappedAsat * 1.05), Math.Min(0.75, cappedAl + 0.12));
        var accentLight2 = ColorUtils.HslToHex(ah, Math.Min(0.82, cappedAsat * 1.0),  Math.Min(0.82, cappedAl + 0.22));
        var accentLight3 = ColorUtils.HslToHex(ah, Math.Min(0.75, cappedAsat * 0.85), Math.Min(0.88, cappedAl + 0.32));
        var accentDark1  = ColorUtils.HslToHex(ah, Math.Min(0.88, cappedAsat * 1.1),  Math.Max(0.01, cappedAl - 0.12));
        var accentDark2  = ColorUtils.HslToHex(ah, Math.Min(0.88, cappedAsat * 1.1),  Math.Max(0.005, cappedAl - 0.20));
        var accentDark3  = ColorUtils.HslToHex(ah, Math.Min(0.85, cappedAsat * 1.0),  Math.Max(0.002, cappedAl - 0.28));
        double bgHue = bh;
        double bgSat = Math.Clamp(bsat, 0.06, 0.35);
        double bgL = Math.Clamp(bl, 0.03, 0.18);
        var bgBase        = ColorUtils.HslToHex(bgHue, bgSat, bgL);
        var bgSecondary   = ColorUtils.HslToHex(bgHue, bgSat * 0.95, bgL + 0.035);
        var bgTertiary    = ColorUtils.HslToHex(bgHue, bgSat * 0.90, bgL + 0.07);
        var bgQuarternary = ColorUtils.HslToHex(bgHue, bgSat * 0.85, bgL + 0.11);
        var textColor = ColorUtils.DeriveTextColorTinted(bgBase, accent);
        var (th, ts, tl) = ColorUtils.RgbToHsl(textColor);
        var textPrimary = ColorUtils.HslToHex(th, ts, Math.Min(0.98, tl + 0.02));
        var textFaded78 = ColorUtils.WithAlphaFraction(textColor, 0.78);
        var textFaded55 = ColorUtils.WithAlphaFraction(textColor, 0.55);
        var textFaded36 = ColorUtils.WithAlphaFraction(textColor, 0.36);
        var cardFill = ColorUtils.WithAlpha(accent, 0x20);
        var highlightFg = cappedAl < 0.4 ? accentLight2 : accent;
        return new Dictionary<string, string>
        {
            ["ThemeAccentColor"]     = accent,
            ["ThemeAccentColor2"]    = accentLight1,
            ["ThemeAccentColor3"]    = accentDark1,
            ["ThemeAccentColor4"]    = accentDark2,
            ["ThemeAccentBrush"]     = accent,
            ["ThemeAccentBrush2"]    = accentLight1,
            ["ThemeAccentBrush3"]    = accentDark1,
            ["ThemeAccentBrush4"]    = accentDark2,
            ["ThemeForegroundLowColor"]  = textFaded55,
            ["ThemeForegroundLowBrush"]  = textFaded55,
            ["HighlightForegroundColor"] = highlightFg,
            ["HighlightForegroundBrush"] = highlightFg,
            ["ErrorColor"]               = "#FF4444",
            ["ErrorLowColor"]            = "#80FF4444",
            ["ErrorBrush"]               = "#FF4444",
            ["ErrorLowBrush"]            = "#80FF4444",
            ["DatePickerFlyoutPresenterHighlightFill"]  = accent,
            ["TimePickerFlyoutPresenterHighlightFill"]  = accent,
            ["SystemAccentColor"]       = accent,
            ["SystemAccentColorDark1"]  = accentDark1,
            ["SystemAccentColorDark2"]  = accentDark2,
            ["SystemAccentColorDark3"]  = accentDark3,
            ["SystemAccentColorLight1"] = accentLight1,
            ["SystemAccentColorLight2"] = accentLight2,
            ["SystemAccentColorLight3"] = accentLight3,
            ["TextFillColorPrimary"]    = ColorUtils.WithAlphaFraction(textPrimary, 1.0),
            ["TextFillColorSecondary"]  = textFaded78,
            ["TextFillColorTertiary"]   = textFaded55,
            ["TextFillColorDisabled"]   = textFaded36,
            ["ControlFillColorDefault"]   = ColorUtils.WithAlpha(accent, 0x1A),
            ["ControlFillColorSecondary"] = ColorUtils.WithAlpha(accent, 0x14),
            ["ControlFillColorTertiary"]  = ColorUtils.WithAlpha(accent, 0x0C),
            ["ControlFillColorDisabled"]  = "#06FFFFFF",
            ["SolidBackgroundFillColorBase"]        = bgBase,
            ["SolidBackgroundFillColorSecondary"]   = bgSecondary,
            ["SolidBackgroundFillColorTertiary"]    = bgTertiary,
            ["SolidBackgroundFillColorQuarternary"] = bgQuarternary,
            ["CardBackgroundFillColorDefault"]       = cardFill,
            ["CardBackgroundFillColorDefaultBrush"]  = cardFill,
            ["LayerFillColorDefault"]                = ColorUtils.WithAlpha(accent, 0x0C),
            ["LayerFillColorAlt"]                    = ColorUtils.WithAlpha(accent, 0x10),
            ["AccentFillColorDefaultBrush"]    = accent,
            ["AccentFillColorSecondaryBrush"]  = accentLight1,
            ["AccentFillColorTertiaryBrush"]   = accentDark1,
            ["AccentFillColorDisabledBrush"]   = ColorUtils.WithAlpha(accent, 0x5C),
            ["ControlStrokeColorDefault"]   = ColorUtils.WithAlpha(accent, 0x42),
            ["ControlStrokeColorSecondary"] = ColorUtils.WithAlpha(accent, 0x2C),
            ["CardStrokeColorDefault"]      = ColorUtils.WithAlpha(accent, 0x36),
            ["SurfaceStrokeColorDefault"]   = ColorUtils.WithAlpha(accent, 0x48),
            ["ButtonBackground"]                   = ColorUtils.WithAlpha(accent, 0x1A),
            ["ButtonBackgroundPointerOver"]         = ColorUtils.WithAlpha(accent, 0x2C),
            ["ButtonBackgroundPressed"]             = ColorUtils.WithAlpha(accent, 0x14),
            ["ButtonBackgroundDisabled"]            = "#08FFFFFF",
            ["ListBoxItemBackgroundPointerOver"]    = ColorUtils.WithAlpha(accent, 0x1A),
            ["ListBoxItemBackgroundPressed"]        = ColorUtils.WithAlpha(accent, 0x25),
            ["ListBoxItemBackgroundSelected"]       = ColorUtils.WithAlpha(accent, 0x2C),
            ["ListBoxItemBackgroundSelectedPointerOver"]  = ColorUtils.WithAlpha(accent, 0x35),
            ["ListBoxItemBackgroundSelectedPressed"]      = ColorUtils.WithAlpha(accent, 0x25),
            ["ToggleSwitchFillOn"]               = accent,
            ["ToggleSwitchFillOnPointerOver"]    = accentLight1,
            ["ToggleSwitchFillOnPressed"]        = accentDark1,
            ["ScrollBarThumbFill"]               = ColorUtils.WithAlpha(accent, 0x58),
            ["ScrollBarThumbFillPointerOver"]    = ColorUtils.WithAlpha(accent, 0x88),
            ["ScrollBarThumbFillPressed"]        = accent,
            ["TextControlBackgroundFocused"]     = ColorUtils.WithAlpha(accent, 0x25),
            ["TextControlBorderBrushFocused"]    = accent,
            ["TextSelectionHighlightColor"]      = ColorUtils.WithAlpha(accent, 0x60),
        };
    }
    private static Dictionary<string, string> GenerateCustomTreeColorMap(string accent, string bg)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var (ah, asat, al) = ColorUtils.RgbToHsl(accent);
        var (bh, bsat, bl) = ColorUtils.RgbToHsl(bg);
        double cappedAsat = Math.Min(asat, 0.88);
        double cappedAl = Math.Clamp(al, 0.02, 0.65);
        double bgHue = bh;
        double bgSat = Math.Clamp(bsat, 0.06, 0.35);
        double bgL = Math.Clamp(bl, 0.03, 0.18);
        double accentBorderSat = Math.Clamp(cappedAsat * 0.45, 0.08, 0.35);
        string Hsl(double h, double s, double l) =>
            "#FF" + ColorUtils.HslToHex(h, s, l).TrimStart('#');
        var (ar, ag, ab) = ColorUtils.ParseHex(accent);
        map["#ff3b6af8"] = $"#FF{ar:X2}{ag:X2}{ab:X2}";
        map["#ff4a78f9"] = Hsl(ah, Math.Min(0.88, cappedAsat * 1.05), Math.Min(0.75, cappedAl + 0.12));
        map["#ff2e59d1"] = Hsl(ah, Math.Min(0.88, cappedAsat * 1.1),  Math.Max(0.01, cappedAl - 0.12));
        map["#ff2148af"] = Hsl(ah, Math.Min(0.88, cappedAsat * 1.1),  Math.Max(0.005, cappedAl - 0.20));
        map["#ff5b88ff"] = Hsl(ah, Math.Min(0.82, cappedAsat * 1.0),  Math.Min(0.82, cappedAl + 0.22));
        map["#ff3366ff"] = Hsl(ah + 2, Math.Min(0.88, cappedAsat * 1.05), Math.Min(0.75, cappedAl + 0.12));
        map["#663b6af8"] = $"#66{ar:X2}{ag:X2}{ab:X2}";
        map["#333b6af8"] = $"#33{ar:X2}{ag:X2}{ab:X2}";
        map["#193b6af8"] = $"#19{ar:X2}{ag:X2}{ab:X2}";
        map["#ff0f1923"] = Hsl(bgHue, bgSat * 0.92, bgL + 0.035);
        map["#ff0d1521"] = Hsl(bgHue, bgSat, bgL);
        map["#ff07101b"] = Hsl(bgHue, bgSat * 1.05, Math.Max(0.02, bgL - 0.03));
        map["#ff090e13"] = Hsl(bgHue, bgSat * 1.02, Math.Max(0.02, bgL - 0.02));
        map["#ff0a1a2e"] = Hsl(bgHue, bgSat * 0.85, Math.Max(0.02, bgL - 0.01));
        map["#ff101c2e"] = Hsl(bgHue, bgSat * 0.97, bgL + 0.025);
        map["#ff121a26"] = Hsl(bgHue, bgSat * 0.95, bgL + 0.015);
        map["#ff141e2b"] = Hsl(bgHue, bgSat * 0.93, bgL + 0.05);
        map["#ff282828"] = Hsl(bgHue, bgSat * 0.55, bgL + 0.09);
        map["#ff4f5c6f"] = Hsl(bgHue, bgSat * 0.65, bgL + 0.22);
        map["#ff242c36"] = Hsl(ah, accentBorderSat, bgL + 0.12);
        map["#ff1a2230"] = Hsl(ah, accentBorderSat, bgL + 0.08);
        map["#ff505050"] = Hsl(ah, accentBorderSat * 0.75, bgL + 0.15);
        var textBase = ColorUtils.DeriveTextColorTinted(Hsl(bgHue, bgSat, bgL), accent);
        var (tr, tg, tb) = ColorUtils.ParseHex(textBase);
        map["#a3f2f2f2"] = $"#A3{tr:X2}{tg:X2}{tb:X2}";
        map["#66f2f2f2"] = $"#66{tr:X2}{tg:X2}{tb:X2}";
        map["#ffdedede"] = $"#FF{tr:X2}{tg:X2}{tb:X2}";
        var textBright = ColorUtils.DeriveTextColorTinted(Hsl(bgHue, bgSat, bgL), accent);
        var (tbh, tbs, tbl) = ColorUtils.RgbToHsl(textBright);
        var textBrightAdjusted = ColorUtils.HslToHex(tbh, tbs, Math.Min(0.98, tbl + 0.02));
        var (tr2, tg2, tb2) = ColorUtils.ParseHex(textBrightAdjusted);
        map["#fff2f2f2"] = $"#FF{tr2:X2}{tg2:X2}{tb2:X2}";
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var keys = new List<string>(map.Keys);
        foreach (var key in keys)
        {
            var val = map[key];
            while (seen.Contains(val))
            {
                var hex = val.TrimStart('#');
                if (hex.Length == 8)
                {
                    byte a = Convert.ToByte(hex[0..2], 16);
                    byte r = Convert.ToByte(hex[2..4], 16);
                    byte g = Math.Min((byte)255, (byte)(Convert.ToByte(hex[4..6], 16) + 1));
                    byte b = Convert.ToByte(hex[6..8], 16);
                    val = $"#{a:X2}{r:X2}{g:X2}{b:X2}";
                    map[key] = val;
                }
                else break;
            }
            seen.Add(val);
        }
        return map;
    }
    private static readonly Dictionary<string, Dictionary<string, string>> Themes = new()
    {
        ["crimson"] = new Dictionary<string, string>
        {
            ["ThemeAccentColor"]     = "#C42B1C",
            ["ThemeAccentColor2"]    = "#D94A3D",
            ["ThemeAccentColor3"]    = "#A32417",
            ["ThemeAccentColor4"]    = "#821D12",
            ["ThemeAccentBrush"]     = "#C42B1C",
            ["ThemeAccentBrush2"]    = "#D94A3D",
            ["ThemeAccentBrush3"]    = "#A32417",
            ["ThemeAccentBrush4"]    = "#821D12",
            ["ThemeForegroundLowColor"]  = "#8CF0EAEA",
            ["ThemeForegroundLowBrush"]  = "#8CF0EAEA",
            ["HighlightForegroundColor"] = "#C42B1C",
            ["HighlightForegroundBrush"] = "#C42B1C",
            ["ErrorColor"]               = "#FF4444",
            ["ErrorLowColor"]            = "#80FF4444",
            ["ErrorBrush"]               = "#FF4444",
            ["ErrorLowBrush"]            = "#80FF4444",
            ["DatePickerFlyoutPresenterHighlightFill"]  = "#C42B1C",
            ["TimePickerFlyoutPresenterHighlightFill"]  = "#C42B1C",
            ["SystemAccentColor"]       = "#C42B1C",
            ["SystemAccentColorDark1"]  = "#A32417",
            ["SystemAccentColorDark2"]  = "#821D12",
            ["SystemAccentColorDark3"]  = "#61150E",
            ["SystemAccentColorLight1"] = "#D94A3D",
            ["SystemAccentColorLight2"] = "#E06B60",
            ["SystemAccentColorLight3"] = "#E88D84",
            ["TextFillColorPrimary"]    = "#FFF0EAEA",
            ["TextFillColorSecondary"]  = "#C8F0EAEA",
            ["TextFillColorTertiary"]   = "#8CF0EAEA",
            ["TextFillColorDisabled"]   = "#5CF0EAEA",
            ["ControlFillColorDefault"]   = "#15FFFFFF",
            ["ControlFillColorSecondary"] = "#10FFFFFF",
            ["ControlFillColorTertiary"]  = "#08FFFFFF",
            ["ControlFillColorDisabled"]  = "#06FFFFFF",
            ["SolidBackgroundFillColorBase"]        = "#241414",
            ["SolidBackgroundFillColorSecondary"]   = "#2C1818",
            ["SolidBackgroundFillColorTertiary"]    = "#341C1C",
            ["SolidBackgroundFillColorQuarternary"] = "#3C2020",
            ["CardBackgroundFillColorDefault"]       = "#20C42B1C",
            ["CardBackgroundFillColorDefaultBrush"]  = "#20C42B1C",
            ["LayerFillColorDefault"]                = "#08FFFFFF",
            ["LayerFillColorAlt"]                    = "#0AFFFFFF",
            ["AccentFillColorDefaultBrush"]    = "#C42B1C",
            ["AccentFillColorSecondaryBrush"]  = "#D94A3D",
            ["AccentFillColorTertiaryBrush"]   = "#A32417",
            ["AccentFillColorDisabledBrush"]   = "#5CC42B1C",
            ["ControlStrokeColorDefault"]   = "#3DF0EAEA",
            ["ControlStrokeColorSecondary"] = "#25F0EAEA",
            ["CardStrokeColorDefault"]      = "#30F0EAEA",
            ["SurfaceStrokeColorDefault"]   = "#40C42B1C",
            ["ButtonBackground"]                   = "#15C42B1C",
            ["ButtonBackgroundPointerOver"]         = "#25C42B1C",
            ["ButtonBackgroundPressed"]             = "#10C42B1C",
            ["ButtonBackgroundDisabled"]            = "#08FFFFFF",
            ["ListBoxItemBackgroundPointerOver"]    = "#15C42B1C",
            ["ListBoxItemBackgroundPressed"]        = "#20C42B1C",
            ["ListBoxItemBackgroundSelected"]       = "#25C42B1C",
            ["ListBoxItemBackgroundSelectedPointerOver"]  = "#30C42B1C",
            ["ListBoxItemBackgroundSelectedPressed"]      = "#20C42B1C",
            ["ToggleSwitchFillOn"]               = "#C42B1C",
            ["ToggleSwitchFillOnPointerOver"]    = "#D94A3D",
            ["ToggleSwitchFillOnPressed"]        = "#A32417",
            ["ScrollBarThumbFill"]               = "#50C42B1C",
            ["ScrollBarThumbFillPointerOver"]    = "#80C42B1C",
            ["ScrollBarThumbFillPressed"]        = "#C42B1C",
            ["TextControlBackgroundFocused"]     = "#20C42B1C",
            ["TextControlBorderBrushFocused"]    = "#C42B1C",
            ["TextSelectionHighlightColor"]      = "#60C42B1C",
        },
        ["loki"] = new Dictionary<string, string>
        {
            ["ThemeAccentColor"]     = "#2A5A40",
            ["ThemeAccentColor2"]    = "#3D7050",
            ["ThemeAccentColor3"]    = "#1E402F",
            ["ThemeAccentColor4"]    = "#112318",
            ["ThemeAccentBrush"]     = "#2A5A40",
            ["ThemeAccentBrush2"]    = "#3D7050",
            ["ThemeAccentBrush3"]    = "#1E402F",
            ["ThemeAccentBrush4"]    = "#112318",
            ["ThemeForegroundLowColor"]  = "#8CF0ECE0",
            ["ThemeForegroundLowBrush"]  = "#8CF0ECE0",
            ["HighlightForegroundColor"] = "#D4A847",
            ["HighlightForegroundBrush"] = "#D4A847",
            ["ErrorColor"]               = "#FF4444",
            ["ErrorLowColor"]            = "#80FF4444",
            ["ErrorBrush"]               = "#FF4444",
            ["ErrorLowBrush"]            = "#80FF4444",
            ["DatePickerFlyoutPresenterHighlightFill"]  = "#2A5A40",
            ["TimePickerFlyoutPresenterHighlightFill"]  = "#2A5A40",
            ["SystemAccentColor"]       = "#2A5A40",
            ["SystemAccentColorDark1"]  = "#1E402F",
            ["SystemAccentColorDark2"]  = "#112318",
            ["SystemAccentColorDark3"]  = "#0D1A12",
            ["SystemAccentColorLight1"] = "#3D7050",
            ["SystemAccentColorLight2"] = "#508A62",
            ["SystemAccentColorLight3"] = "#6AA07A",
            ["TextFillColorPrimary"]    = "#FFF0ECE0",
            ["TextFillColorSecondary"]  = "#C8F0ECE0",
            ["TextFillColorTertiary"]   = "#8CF0ECE0",
            ["TextFillColorDisabled"]   = "#5CF0ECE0",
            ["ControlFillColorDefault"]   = "#15FFFFFF",
            ["ControlFillColorSecondary"] = "#10FFFFFF",
            ["ControlFillColorTertiary"]  = "#08FFFFFF",
            ["ControlFillColorDisabled"]  = "#06FFFFFF",
            ["SolidBackgroundFillColorBase"]        = "#0F1210",
            ["SolidBackgroundFillColorSecondary"]   = "#151A15",
            ["SolidBackgroundFillColorTertiary"]    = "#1A1F1A",
            ["SolidBackgroundFillColorQuarternary"] = "#202820",
            ["CardBackgroundFillColorDefault"]       = "#202A5A40",
            ["CardBackgroundFillColorDefaultBrush"]  = "#202A5A40",
            ["LayerFillColorDefault"]                = "#08FFFFFF",
            ["LayerFillColorAlt"]                    = "#0AFFFFFF",
            ["AccentFillColorDefaultBrush"]    = "#2A5A40",
            ["AccentFillColorSecondaryBrush"]  = "#3D7050",
            ["AccentFillColorTertiaryBrush"]   = "#1E402F",
            ["AccentFillColorDisabledBrush"]   = "#5C2A5A40",
            ["ControlStrokeColorDefault"]   = "#3DF0ECE0",
            ["ControlStrokeColorSecondary"] = "#25F0ECE0",
            ["CardStrokeColorDefault"]      = "#30F0ECE0",
            ["SurfaceStrokeColorDefault"]   = "#402A5A40",
            ["ButtonBackground"]                   = "#152A5A40",
            ["ButtonBackgroundPointerOver"]         = "#252A5A40",
            ["ButtonBackgroundPressed"]             = "#102A5A40",
            ["ButtonBackgroundDisabled"]            = "#08FFFFFF",
            ["ListBoxItemBackgroundPointerOver"]    = "#152A5A40",
            ["ListBoxItemBackgroundPressed"]        = "#202A5A40",
            ["ListBoxItemBackgroundSelected"]       = "#252A5A40",
            ["ListBoxItemBackgroundSelectedPointerOver"]  = "#302A5A40",
            ["ListBoxItemBackgroundSelectedPressed"]      = "#202A5A40",
            ["ToggleSwitchFillOn"]               = "#2A5A40",
            ["ToggleSwitchFillOnPointerOver"]    = "#3D7050",
            ["ToggleSwitchFillOnPressed"]        = "#1E402F",
            ["ScrollBarThumbFill"]               = "#502A5A40",
            ["ScrollBarThumbFillPointerOver"]    = "#802A5A40",
            ["ScrollBarThumbFillPressed"]        = "#2A5A40",
            ["TextControlBackgroundFocused"]     = "#202A5A40",
            ["TextControlBorderBrushFocused"]    = "#2A5A40",
            ["TextSelectionHighlightColor"]      = "#602A5A40",
        },
    };
}