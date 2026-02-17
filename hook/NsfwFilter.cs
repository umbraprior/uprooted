namespace Uprooted;

/// <summary>
/// Orchestrates the NSFW content filter lifecycle:
/// 1. Waits for DotNetBrowser BrowserView in the Avalonia visual tree
/// 2. Gets IBrowser -> IFrame references via DotNetBrowserReflection
/// 3. Injects config + filter JavaScript into the main browser frame
/// 4. Periodically re-checks injection (handles page navigation)
///
/// All Google Vision API calls happen in JavaScript (not C#) because
/// --disable-web-security eliminates CORS restrictions in DotNetBrowser.
/// </summary>
internal class NsfwFilter : IDisposable
{
    private const int ReinjectionIntervalMs = 30_000; // 30s re-check
    private const string ConfigGlobalName = "__UPROOTED_NSFW_CONFIG__";
    private const string ActiveGuardName = "__UPROOTED_NSFW_ACTIVE__";

    private readonly AvaloniaReflection _avaloniaReflection;
    private readonly DotNetBrowserReflection _browserReflection;
    private readonly UprootedSettings _settings;
    private readonly object _mainWindow;

    private Timer? _reinjectionTimer;
    private object? _lastBrowserView;
    private object? _lastFrame;
    private string? _filterScript; // Cached JS file contents
    private bool _disposed;

    internal NsfwFilter(AvaloniaReflection avaloniaReflection,
        DotNetBrowserReflection browserReflection,
        UprootedSettings settings,
        object mainWindow)
    {
        _avaloniaReflection = avaloniaReflection;
        _browserReflection = browserReflection;
        _settings = settings;
        _mainWindow = mainWindow;
    }

    /// <summary>
    /// Initialize the filter: find BrowserView, inject config + script.
    /// Call from a background thread (Phase 5).
    /// </summary>
    internal bool Initialize()
    {
        try
        {
            // Load the JS filter script from disk
            _filterScript = LoadFilterScript();
            if (_filterScript == null)
            {
                Logger.Log("NsfwFilter", "Filter script not found, aborting");
                return false;
            }

            // Find BrowserView in the visual tree (must run on UI thread)
            bool injected = false;
            _avaloniaReflection.RunOnUIThread(() =>
            {
                injected = TryInject();
            });

            // Wait a moment for the UI thread dispatch to complete
            Thread.Sleep(2000);

            // Start periodic re-injection timer
            _reinjectionTimer = new Timer(OnReinjectionTick, null, ReinjectionIntervalMs, ReinjectionIntervalMs);
            Logger.Log("NsfwFilter", "Re-injection timer started (30s interval)");

            return true;
        }
        catch (Exception ex)
        {
            Logger.Log("NsfwFilter", $"Initialize error: {ex}");
            return false;
        }
    }

    private bool TryInject()
    {
        try
        {
            // Step 1: Find BrowserView in visual tree
            var browserView = _browserReflection.FindBrowserView(_avaloniaReflection, _mainWindow);
            if (browserView == null)
            {
                Logger.Log("NsfwFilter", "BrowserView not found in visual tree");
                return false;
            }
            _lastBrowserView = browserView;
            Logger.Log("NsfwFilter", $"BrowserView found: {browserView.GetType().FullName}");

            // Step 2: Get IBrowser from BrowserView
            var browser = _browserReflection.GetBrowser(browserView);
            if (browser == null)
            {
                Logger.Log("NsfwFilter", "IBrowser not available from BrowserView");
                return false;
            }
            Logger.Log("NsfwFilter", $"IBrowser acquired: {browser.GetType().FullName}");

            // Step 3: Get MainFrame from IBrowser
            var frame = _browserReflection.GetMainFrame(browser);
            if (frame == null)
            {
                Logger.Log("NsfwFilter", "MainFrame not available from IBrowser");
                return false;
            }
            _lastFrame = frame;
            Logger.Log("NsfwFilter", $"MainFrame acquired: {frame.GetType().FullName}");

            // Step 4: Inject config
            var configJson = BuildConfigJson();
            var configScript = $"window.{ConfigGlobalName}={configJson};";
            if (!_browserReflection.ExecuteJavaScript(frame, configScript))
            {
                Logger.Log("NsfwFilter", "Failed to inject config");
                return false;
            }
            Logger.Log("NsfwFilter", "Config injected");

            // Step 5: Inject filter script
            if (!_browserReflection.ExecuteJavaScript(frame, _filterScript!))
            {
                Logger.Log("NsfwFilter", "Failed to inject filter script");
                return false;
            }
            Logger.Log("NsfwFilter", "Filter script injected successfully");

            return true;
        }
        catch (Exception ex)
        {
            Logger.Log("NsfwFilter", $"TryInject error: {ex.Message}");
            return false;
        }
    }

    private void OnReinjectionTick(object? state)
    {
        if (_disposed) return;
        if (!_settings.NsfwFilterEnabled || string.IsNullOrEmpty(_settings.NsfwApiKey)) return;

        try
        {
            _avaloniaReflection.RunOnUIThread(() =>
            {
                try
                {
                    // Check if filter is still active in the frame
                    // If the page navigated, __UPROOTED_NSFW_ACTIVE__ will be gone
                    // We re-inject to handle this case
                    if (_lastFrame != null)
                    {
                        // Try to check if already active by re-injecting
                        // The script self-guards against double injection
                        var configJson = BuildConfigJson();
                        var configScript = $"window.{ConfigGlobalName}={configJson};";
                        _browserReflection.ExecuteJavaScript(_lastFrame, configScript);
                        if (_filterScript != null)
                            _browserReflection.ExecuteJavaScript(_lastFrame, _filterScript);
                    }
                    else
                    {
                        // Lost frame reference -- try full re-discovery
                        TryInject();
                    }
                }
                catch (Exception ex)
                {
                    Logger.Log("NsfwFilter", $"Re-injection error: {ex.Message}");
                    // Frame may be stale -- try full re-discovery next time
                    _lastFrame = null;
                }
            });
        }
        catch (Exception ex)
        {
            Logger.Log("NsfwFilter", $"OnReinjectionTick error: {ex.Message}");
        }
    }

    /// <summary>
    /// Re-inject config when settings change (e.g., user updates threshold from settings UI).
    /// </summary>
    internal void UpdateConfig()
    {
        if (_lastFrame == null) return;

        try
        {
            _avaloniaReflection.RunOnUIThread(() =>
            {
                try
                {
                    var configJson = BuildConfigJson();
                    var configScript = $"window.{ConfigGlobalName}={configJson};";
                    _browserReflection.ExecuteJavaScript(_lastFrame, configScript);

                    if (_settings.NsfwFilterEnabled && !string.IsNullOrEmpty(_settings.NsfwApiKey))
                    {
                        // Re-inject script (self-guard prevents double init, but picks up new config)
                        // Reset the active flag so the script re-reads config
                        _browserReflection.ExecuteJavaScript(_lastFrame, $"window.{ActiveGuardName}=false;");
                        if (_filterScript != null)
                            _browserReflection.ExecuteJavaScript(_lastFrame, _filterScript);
                    }

                    Logger.Log("NsfwFilter", "Config updated via settings change");
                }
                catch (Exception ex)
                {
                    Logger.Log("NsfwFilter", $"UpdateConfig error: {ex.Message}");
                }
            });
        }
        catch { }
    }

    private string BuildConfigJson()
    {
        // Manual JSON building -- no System.Text.Json allowed in profiler context
        var enabled = _settings.NsfwFilterEnabled ? "true" : "false";
        var apiKey = EscapeJsonString(_settings.NsfwApiKey);
        var threshold = _settings.NsfwThreshold.ToString(System.Globalization.CultureInfo.InvariantCulture);
        return $"{{\"enabled\":{enabled},\"apiKey\":{apiKey},\"threshold\":{threshold}}}";
    }

    private static string EscapeJsonString(string s)
    {
        if (string.IsNullOrEmpty(s)) return "\"\"";
        var sb = new System.Text.StringBuilder("\"", s.Length + 2);
        foreach (var c in s)
        {
            switch (c)
            {
                case '"':  sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                default:   sb.Append(c); break;
            }
        }
        sb.Append('"');
        return sb.ToString();
    }

    private static string? LoadFilterScript()
    {
        try
        {
            // Script is deployed alongside UprootedHook.dll
            var hookDir = Path.GetDirectoryName(typeof(NsfwFilter).Assembly.Location);
            if (hookDir != null)
            {
                var scriptPath = Path.Combine(hookDir, "nsfw-filter.js");
                if (File.Exists(scriptPath))
                {
                    Logger.Log("NsfwFilter", $"Loading filter script from: {scriptPath}");
                    return File.ReadAllText(scriptPath);
                }
            }

            // Fallback: try Uprooted assets directory
            var uprootedDir = PlatformPaths.GetUprootedDir();
            var fallbackPath = Path.Combine(uprootedDir, "nsfw-filter.js");
            if (File.Exists(fallbackPath))
            {
                Logger.Log("NsfwFilter", $"Loading filter script from fallback: {fallbackPath}");
                return File.ReadAllText(fallbackPath);
            }

            Logger.Log("NsfwFilter", "Filter script not found in hook dir or uprooted dir");
            return null;
        }
        catch (Exception ex)
        {
            Logger.Log("NsfwFilter", $"LoadFilterScript error: {ex.Message}");
            return null;
        }
    }

    public void Dispose()
    {
        _disposed = true;
        _reinjectionTimer?.Dispose();
        _reinjectionTimer = null;
    }
}
