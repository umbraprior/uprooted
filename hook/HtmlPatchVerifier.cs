namespace Uprooted;

/// <summary>
/// Self-healing HTML patch verifier.
/// Phase 0: checks HTML files at startup and re-patches if markers are missing.
/// Runtime: FileSystemWatcher detects overwrites (e.g. Root auto-update) and re-patches.
/// </summary>
internal class HtmlPatchVerifier : IDisposable
{
    private const string MarkerStart = "<!-- uprooted:start -->";
    private const string MarkerEnd = "<!-- uprooted:end -->";
    private const string LegacyMarker = "<!-- uprooted -->";
    private const string PreloadMarker = "uprooted-preload";
    private const string BackupSuffix = ".uprooted.bak";

    private readonly string _profileDir;
    private readonly string _uprootedDir;
    private readonly List<FileSystemWatcher> _watchers = new();
    private int _patchGuard; // Interlocked guard to prevent concurrent patches
    private DateTime _lastPatchTime = DateTime.MinValue;
    private static readonly TimeSpan PatchCooldown = TimeSpan.FromSeconds(2);
    private Timer? _debounceTimer;
    private readonly object _debounceLock = new();

    internal HtmlPatchVerifier()
    {
        _profileDir = PlatformPaths.GetProfileDir();
        _uprootedDir = PlatformPaths.GetUprootedDir();
    }

    /// <summary>
    /// Phase 0: verify all HTML files are patched. Returns number of files repaired.
    /// </summary>
    internal int VerifyAndRepair()
    {
        var repaired = 0;
        var htmlFiles = FindTargetHtmlFiles();

        if (htmlFiles.Count == 0)
        {
            Logger.Log("HtmlPatch", "No target HTML files found in profile directory");
            return 0;
        }

        Logger.Log("HtmlPatch", $"Checking {htmlFiles.Count} HTML file(s) for patches");

        foreach (var file in htmlFiles)
        {
            try
            {
                var content = File.ReadAllText(file);
                if (IsPatched(content))
                {
                    Logger.Log("HtmlPatch", $"OK: {GetRelativeName(file)}");
                    continue;
                }

                Logger.Log("HtmlPatch", $"MISSING patches in {GetRelativeName(file)}, repairing...");
                if (PatchFile(file, content))
                {
                    repaired++;
                    Logger.Log("HtmlPatch", $"Repaired: {GetRelativeName(file)}");
                }
            }
            catch (Exception ex)
            {
                Logger.Log("HtmlPatch", $"Error checking {GetRelativeName(file)}: {ex.Message}");
            }
        }

        return repaired;
    }

    /// <summary>
    /// Start FileSystemWatchers on WebRtcBundle/ and RootApps/ to detect overwrites.
    /// </summary>
    internal void StartWatching()
    {
        try
        {
            var webRtcDir = Path.Combine(_profileDir, "WebRtcBundle");
            if (Directory.Exists(webRtcDir))
                AddWatcher(webRtcDir);

            var rootAppsDir = Path.Combine(_profileDir, "RootApps");
            if (Directory.Exists(rootAppsDir))
            {
                foreach (var appDir in Directory.GetDirectories(rootAppsDir))
                    AddWatcher(appDir);
            }

            Logger.Log("HtmlPatch", $"FileSystemWatcher active on {_watchers.Count} director(ies)");
        }
        catch (Exception ex)
        {
            Logger.Log("HtmlPatch", $"Failed to start watchers: {ex.Message}");
        }
    }

    public void Dispose()
    {
        _debounceTimer?.Dispose();
        foreach (var w in _watchers)
        {
            w.EnableRaisingEvents = false;
            w.Dispose();
        }
        _watchers.Clear();
    }

    private void AddWatcher(string directory)
    {
        var watcher = new FileSystemWatcher(directory)
        {
            Filter = "index.html",
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.CreationTime,
            EnableRaisingEvents = true,
            IncludeSubdirectories = false
        };
        watcher.Changed += OnHtmlFileChanged;
        watcher.Created += OnHtmlFileChanged;
        _watchers.Add(watcher);
    }

    private void OnHtmlFileChanged(object sender, FileSystemEventArgs e)
    {
        // Debounce: wait 1s after last event before acting
        lock (_debounceLock)
        {
            _debounceTimer?.Dispose();
            _debounceTimer = new Timer(DebouncedPatchCheck, e.FullPath, 1000, Timeout.Infinite);
        }
    }

    private void DebouncedPatchCheck(object? state)
    {
        var filePath = (string)state!;

        // Cooldown: skip if we patched very recently (prevents self-triggering)
        if (DateTime.UtcNow - _lastPatchTime < PatchCooldown)
            return;

        // Single-entry guard
        if (Interlocked.CompareExchange(ref _patchGuard, 1, 0) != 0)
            return;

        try
        {
            if (!File.Exists(filePath))
                return;

            var content = File.ReadAllText(filePath);
            if (IsPatched(content))
                return;

            Logger.Log("HtmlPatch", $"Watcher: patches lost in {GetRelativeName(filePath)}, repairing...");
            if (PatchFile(filePath, content))
            {
                _lastPatchTime = DateTime.UtcNow;
                Logger.Log("HtmlPatch", $"Watcher: repaired {GetRelativeName(filePath)}");
            }
        }
        catch (Exception ex)
        {
            Logger.Log("HtmlPatch", $"Watcher error on {GetRelativeName(filePath)}: {ex.Message}");
        }
        finally
        {
            Interlocked.Exchange(ref _patchGuard, 0);
        }
    }

    private bool PatchFile(string filePath, string content)
    {
        // Strip any partial/old injection first
        var clean = StripExistingInjection(content);

        if (!clean.Contains("</head>"))
        {
            Logger.Log("HtmlPatch", $"No </head> tag found in {GetRelativeName(filePath)}");
            return false;
        }

        // Create backup if none exists
        var backupPath = filePath + BackupSuffix;
        if (!File.Exists(backupPath))
        {
            try { File.Copy(filePath, backupPath); }
            catch (Exception ex)
            {
                Logger.Log("HtmlPatch", $"Backup failed: {ex.Message}");
            }
        }

        // Build injection block
        var injection = BuildInjectionBlock();

        var patched = clean.Replace("</head>", injection + "\n  </head>");
        File.WriteAllText(filePath, patched);
        return true;
    }

    private string BuildInjectionBlock()
    {
        var preloadPath = Path.Combine(_uprootedDir, "uprooted-preload.js").Replace('\\', '/');
        var cssPath = Path.Combine(_uprootedDir, "uprooted.css").Replace('\\', '/');

        // Build settings JSON inline without System.Text.Json (forbidden in profiler context).
        // Read uprooted-settings.json raw if it exists, otherwise build minimal JSON from INI settings.
        var settingsJson = BuildSettingsJson();

        // Build NSFW config JSON for early injection (before Phase 5 completes)
        var nsfwConfigJson = BuildNsfwConfigJson();

        var filePrefix = OperatingSystem.IsWindows() ? "file:///" : "file://";

        return $"    {MarkerStart}\n" +
               $"    <script>window.__UPROOTED_SETTINGS__={settingsJson};</script>\n" +
               $"    <script>window.__UPROOTED_NSFW_CONFIG__={nsfwConfigJson};</script>\n" +
               $"    <script src=\"{filePrefix}{preloadPath}\"></script>\n" +
               $"    <link rel=\"stylesheet\" href=\"{filePrefix}{cssPath}\">\n" +
               $"    {MarkerEnd}";
    }

    private string BuildSettingsJson()
    {
        // Try reading uprooted-settings.json raw (written by Tauri installer)
        var jsonPath = Path.Combine(_profileDir, "uprooted-settings.json");
        if (File.Exists(jsonPath))
        {
            try
            {
                var raw = File.ReadAllText(jsonPath).Trim();
                // Sanity check: must look like JSON
                if (raw.StartsWith("{") && raw.EndsWith("}"))
                    return raw;
            }
            catch { }
        }

        // Fallback: build minimal JSON from INI settings (no System.Text.Json!)
        var settings = UprootedSettings.Load();
        return "{" +
               $"\"enabled\":{(settings.Enabled ? "true" : "false")}," +
               $"\"customCss\":{EscapeJsonString(settings.CustomCss)}," +
               "\"plugins\":{}" +
               "}";
    }

    private string BuildNsfwConfigJson()
    {
        var settings = UprootedSettings.Load();
        var enabled = settings.NsfwFilterEnabled ? "true" : "false";
        var apiKey = EscapeJsonString(settings.NsfwApiKey);
        var threshold = settings.NsfwThreshold.ToString(System.Globalization.CultureInfo.InvariantCulture);
        return $"{{\"enabled\":{enabled},\"apiKey\":{apiKey},\"threshold\":{threshold}}}";
    }

    /// <summary>
    /// Manual JSON string escaping -- avoids System.Text.Json dependency.
    /// </summary>
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

    /// <summary>
    /// Strip any existing uprooted injection (marker-based or bare tags).
    /// </summary>
    private static string StripExistingInjection(string content)
    {
        var lines = content.Split('\n');
        var result = new List<string>(lines.Length);
        var insideBlock = false;

        foreach (var line in lines)
        {
            if (line.Contains(MarkerStart))
            {
                insideBlock = true;
                continue;
            }
            if (line.Contains(MarkerEnd))
            {
                insideBlock = false;
                continue;
            }
            if (insideBlock)
                continue;

            // Strip legacy marker lines
            if (line.Contains(LegacyMarker))
                continue;

            // Strip bare uprooted tags (bash installer without markers)
            if (line.Contains("uprooted-preload") && (line.Contains("<script") || line.Contains("</script")))
                continue;
            if (line.Contains("uprooted.css") && line.Contains("<link"))
                continue;
            if (line.Contains("__UPROOTED_SETTINGS__") && line.Contains("<script"))
                continue;
            if (line.Contains("__UPROOTED_NSFW_CONFIG__") && line.Contains("<script"))
                continue;

            result.Add(line);
        }

        return string.Join('\n', result);
    }

    private static bool IsPatched(string content)
    {
        return content.Contains(MarkerStart)
            || content.Contains(LegacyMarker)
            || content.Contains(PreloadMarker);
    }

    private List<string> FindTargetHtmlFiles()
    {
        var targets = new List<string>();

        var webRtcIndex = Path.Combine(_profileDir, "WebRtcBundle", "index.html");
        if (File.Exists(webRtcIndex))
            targets.Add(webRtcIndex);

        var rootAppsDir = Path.Combine(_profileDir, "RootApps");
        if (Directory.Exists(rootAppsDir))
        {
            foreach (var appDir in Directory.GetDirectories(rootAppsDir))
            {
                var indexPath = Path.Combine(appDir, "index.html");
                if (File.Exists(indexPath))
                    targets.Add(indexPath);
            }
        }

        // Also check DotNetBrowser Host/Bundle context where chat renders
        var hostBundleDir = Path.Combine(_profileDir, "HostBundle");
        if (Directory.Exists(hostBundleDir))
        {
            var hostIndex = Path.Combine(hostBundleDir, "index.html");
            if (File.Exists(hostIndex))
                targets.Add(hostIndex);
        }

        return targets;
    }

    private string GetRelativeName(string fullPath)
    {
        if (fullPath.StartsWith(_profileDir))
            return fullPath[(_profileDir.Length + 1)..].Replace('\\', '/');
        return Path.GetFileName(fullPath);
    }
}
