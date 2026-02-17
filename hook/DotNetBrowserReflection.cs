using System.Reflection;

namespace Uprooted;

/// <summary>
/// Cached reflection handles for DotNetBrowser types, properties, and methods.
/// Follows AvaloniaReflection patterns: assembly scanning, type caching, nullable returns.
/// Used by NsfwFilter to execute JavaScript in the main browser frame.
/// </summary>
internal class DotNetBrowserReflection
{
    // DotNetBrowser types
    public Type? BrowserViewType { get; private set; }
    public Type? IBrowserType { get; private set; }
    public Type? IFrameType { get; private set; }

    // Cached property/method handles
    private PropertyInfo? _browserViewBrowser;   // BrowserView.Browser -> IBrowser
    private PropertyInfo? _browserMainFrame;     // IBrowser.MainFrame -> IFrame
    private MethodInfo? _frameExecuteJavaScript;  // IFrame.ExecuteJavaScript(string) -> object

    public bool IsResolved { get; private set; }

    public bool Resolve()
    {
        try
        {
            ResolveTypes();
            ResolveMembers();
            IsResolved = BrowserViewType != null && IBrowserType != null && IFrameType != null;
            Logger.Log("DotNetBrowser", $"Resolved: {IsResolved} " +
                $"(BrowserView={BrowserViewType != null}, IBrowser={IBrowserType != null}, " +
                $"IFrame={IFrameType != null})");
            return IsResolved;
        }
        catch (Exception ex)
        {
            Logger.Log("DotNetBrowser", $"Resolve failed: {ex}");
            return false;
        }
    }

    private void ResolveTypes()
    {
        var typeMap = new Dictionary<string, Type>();

        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            try
            {
                var name = asm.GetName().Name ?? "";
                if (!name.StartsWith("DotNetBrowser", StringComparison.OrdinalIgnoreCase)) continue;

                foreach (var type in asm.GetTypes())
                {
                    var fn = type.FullName;
                    if (fn != null) typeMap[fn] = type;
                }
            }
            catch { }
        }

        Logger.Log("DotNetBrowser", $"Scanned DotNetBrowser assemblies, found {typeMap.Count} types");

        Type? Find(string fullName) => typeMap.TryGetValue(fullName, out var t) ? t : null;

        // BrowserView -- the Avalonia control
        BrowserViewType = Find("DotNetBrowser.AvaloniaUi.BrowserView");
        // Fallback: search by name suffix
        BrowserViewType ??= typeMap.Values.FirstOrDefault(t =>
            t.Name == "BrowserView" && !t.IsAbstract && !t.IsInterface);

        // IBrowser -- browser instance
        IBrowserType = Find("DotNetBrowser.Browser.IBrowser");
        IBrowserType ??= typeMap.Values.FirstOrDefault(t =>
            t.Name == "IBrowser" && t.IsInterface);

        // IFrame -- frame for JS execution
        IFrameType = Find("DotNetBrowser.Frame.IFrame");
        IFrameType ??= typeMap.Values.FirstOrDefault(t =>
            t.Name == "IFrame" && t.IsInterface);

        Logger.Log("DotNetBrowser", $"  BrowserView: {(BrowserViewType != null ? BrowserViewType.FullName : "MISSING")}");
        Logger.Log("DotNetBrowser", $"  IBrowser: {(IBrowserType != null ? IBrowserType.FullName : "MISSING")}");
        Logger.Log("DotNetBrowser", $"  IFrame: {(IFrameType != null ? IFrameType.FullName : "MISSING")}");
    }

    private void ResolveMembers()
    {
        var pub = BindingFlags.Public | BindingFlags.Instance;

        // BrowserView.Browser property
        _browserViewBrowser = BrowserViewType?.GetProperty("Browser", pub);

        // IBrowser.MainFrame property
        _browserMainFrame = IBrowserType?.GetProperty("MainFrame", pub);

        // IFrame.ExecuteJavaScript(string) method
        // Try the simple string overload first
        if (IFrameType != null)
        {
            _frameExecuteJavaScript = IFrameType.GetMethods(pub)
                .FirstOrDefault(m => m.Name == "ExecuteJavaScript" &&
                    m.GetParameters().Length == 1 &&
                    m.GetParameters()[0].ParameterType == typeof(string));

            // Fallback: any single-param overload
            _frameExecuteJavaScript ??= IFrameType.GetMethods(pub)
                .FirstOrDefault(m => m.Name == "ExecuteJavaScript" &&
                    m.GetParameters().Length == 1);
        }

        Logger.Log("DotNetBrowser", $"  BrowserView.Browser: {(_browserViewBrowser != null ? "OK" : "MISSING")}");
        Logger.Log("DotNetBrowser", $"  IBrowser.MainFrame: {(_browserMainFrame != null ? "OK" : "MISSING")}");
        Logger.Log("DotNetBrowser", $"  IFrame.ExecuteJavaScript: {(_frameExecuteJavaScript != null ? "OK" : "MISSING")}");
    }

    /// <summary>
    /// Walk the Avalonia visual tree to find the first DotNetBrowser BrowserView control.
    /// </summary>
    public object? FindBrowserView(AvaloniaReflection r, object mainWindow)
    {
        if (BrowserViewType == null) return null;
        return FindInTree(r, mainWindow, BrowserViewType, 0);
    }

    private object? FindInTree(AvaloniaReflection r, object visual, Type targetType, int depth)
    {
        if (depth > 50) return null;

        if (targetType.IsAssignableFrom(visual.GetType()))
            return visual;

        foreach (var child in r.GetVisualChildren(visual))
        {
            var found = FindInTree(r, child, targetType, depth + 1);
            if (found != null) return found;
        }

        return null;
    }

    /// <summary>
    /// Get the IBrowser instance from a BrowserView control.
    /// </summary>
    public object? GetBrowser(object browserView)
    {
        if (_browserViewBrowser == null) return null;
        try { return _browserViewBrowser.GetValue(browserView); }
        catch (Exception ex)
        {
            Logger.Log("DotNetBrowser", $"GetBrowser error: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Get the main IFrame from an IBrowser instance.
    /// </summary>
    public object? GetMainFrame(object browser)
    {
        if (_browserMainFrame == null) return null;
        try { return _browserMainFrame.GetValue(browser); }
        catch (Exception ex)
        {
            Logger.Log("DotNetBrowser", $"GetMainFrame error: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Execute JavaScript in the given IFrame. Returns true if execution succeeded.
    /// </summary>
    public bool ExecuteJavaScript(object frame, string script)
    {
        if (_frameExecuteJavaScript == null) return false;
        try
        {
            _frameExecuteJavaScript.Invoke(frame, new object[] { script });
            return true;
        }
        catch (Exception ex)
        {
            Logger.Log("DotNetBrowser", $"ExecuteJavaScript error: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Check if DotNetBrowser assemblies are loaded in the current AppDomain.
    /// </summary>
    public static bool AreDotNetBrowserAssembliesLoaded()
    {
        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            var name = asm.GetName().Name ?? "";
            if (name.Equals("DotNetBrowser.Chromium", StringComparison.OrdinalIgnoreCase) ||
                name.Equals("DotNetBrowser", StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }
}
