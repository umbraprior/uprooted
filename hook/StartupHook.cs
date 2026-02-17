using Uprooted;

/// <summary>
/// .NET Startup Hook entry point for Uprooted.
/// Must be: internal class StartupHook (no namespace) with public static void Initialize().
/// Loaded via DOTNET_STARTUP_HOOKS env var before Root's Main() runs.
/// </summary>
internal class StartupHook
{
    // Static reference keeps FileSystemWatcher alive for process lifetime
    private static HtmlPatchVerifier? s_patchVerifier;

    public static void Initialize()
    {
        // Process guard: only inject into Root.exe
        var processName = Path.GetFileNameWithoutExtension(Environment.ProcessPath ?? "");
        if (!processName.Equals("Root", StringComparison.OrdinalIgnoreCase))
            return;

        var thread = new Thread(InjectorLoop)
        {
            IsBackground = true,
            Name = "Uprooted-Injector"
        };
        thread.Start();
    }

    private static void InjectorLoop()
    {
        try
        {
            Logger.Log("Startup", "========================================");
            Logger.Log("Startup", "=== Uprooted Hook v0.2.3 Loaded ===");
            Logger.Log("Startup", "========================================");
            Logger.Log("Startup", $"Process: {Environment.ProcessPath}");
            Logger.Log("Startup", $"PID: {Environment.ProcessId}");
            Logger.Log("Startup", $".NET: {Environment.Version}");
            Logger.Log("Startup", $"Log file: {Logger.GetLogPath()}");

            // Phase 0: Verify HTML patches (filesystem only -- no Avalonia needed)
            Logger.Log("Startup", "Phase 0: Verifying HTML patches...");
            try
            {
                var verifier = new HtmlPatchVerifier();
                var repaired = verifier.VerifyAndRepair();
                Logger.Log("Startup", $"Phase 0 OK: {repaired} file(s) repaired");
                verifier.StartWatching();
                s_patchVerifier = verifier; // prevent GC
            }
            catch (Exception ex)
            {
                Logger.Log("Startup", $"Phase 0 non-fatal error: {ex.Message}");
            }

            // Phase 1: Wait for Avalonia assemblies to load
            Logger.Log("Startup", "Phase 1: Waiting for Avalonia assemblies...");
            if (!WaitForAvaloniaAssemblies(TimeSpan.FromSeconds(30)))
            {
                Logger.Log("Startup", "Phase 1 FAILED: Avalonia assemblies not found after 30s");
                return;
            }
            Logger.Log("Startup", "Phase 1 OK: Avalonia assemblies loaded");

            // Resolve all Avalonia types via reflection
            var resolver = new AvaloniaReflection();
            if (!resolver.Resolve())
            {
                Logger.Log("Startup", "Type resolution failed, aborting");
                return;
            }

            // Phase 2: Wait for Application.Current to be set
            Logger.Log("Startup", "Phase 2: Waiting for Application.Current...");
            if (!WaitFor(() => resolver.GetAppCurrent() != null, TimeSpan.FromSeconds(30)))
            {
                Logger.Log("Startup", "Phase 2 FAILED: Application.Current not available after 30s");
                return;
            }
            Logger.Log("Startup", "Phase 2 OK: Application.Current is set");

            // Phase 3: Wait for MainWindow
            Logger.Log("Startup", "Phase 3: Waiting for MainWindow...");
            object? mainWindow = null;
            if (!WaitFor(() =>
            {
                mainWindow = resolver.GetMainWindow();
                return mainWindow != null;
            }, TimeSpan.FromSeconds(60)))
            {
                Logger.Log("Startup", "Phase 3 FAILED: MainWindow not available after 60s");
                return;
            }
            Logger.Log("Startup", $"Phase 3 OK: MainWindow = {mainWindow!.GetType().FullName}");

            // Phase 3.5: Initialize theme engine (actual theme apply deferred to UI thread)
            Logger.Log("Startup", "Phase 3.5: Initializing theme engine");
            var themeEngine = new ThemeEngine(resolver);
            var savedSettings = UprootedSettings.Load();

            // Apply saved theme on UI thread (ResourceDictionary requires Dispatcher access)
            resolver.RunOnUIThread(() =>
            {
                try
                {
                    if (savedSettings.ActiveTheme == "custom")
                    {
                        Logger.Log("Startup", "Applying saved custom theme: accent=" + savedSettings.CustomAccent + " bg=" + savedSettings.CustomBackground);
                        themeEngine.ApplyCustomTheme(savedSettings.CustomAccent, savedSettings.CustomBackground);
                    }
                    else if (savedSettings.ActiveTheme != "default-dark")
                    {
                        Logger.Log("Startup", "Applying saved theme: " + savedSettings.ActiveTheme);
                        themeEngine.ApplyTheme(savedSettings.ActiveTheme);
                    }
                    else
                    {
                        Logger.Log("Startup", "Using default theme (no override)");
                    }
                    // Diagnostics disabled (uncomment for debugging)
                    // var te = themeEngine;
                    // System.Threading.ThreadPool.QueueUserWorkItem(_ => {
                    //     Thread.Sleep(25000);
                    //     resolver.RunOnUIThread(() => {
                    //         try { te.DumpVisualTreeColors(); }
                    //         catch { }
                    //     });
                    // });
                }
                catch (Exception ex)
                {
                    Logger.Log("Startup", "Theme init error: " + ex.Message);
                }
            });

            // Phase 4: Start the settings page monitor
            Logger.Log("Startup", "Phase 4: Starting settings page monitor");
            var injector = new SidebarInjector(resolver, mainWindow!, themeEngine);
            injector.StartMonitoring();

            Logger.Log("Startup", "========================================");
            Logger.Log("Startup", "=== Uprooted Hook Ready ===");
            Logger.Log("Startup", "========================================");

            // Phase 5: NSFW content filter (non-blocking, background thread)
            var nsfwSettings = UprootedSettings.Load();
            if (nsfwSettings.NsfwFilterEnabled && !string.IsNullOrEmpty(nsfwSettings.NsfwApiKey))
            {
                var capturedWindow = mainWindow!;
                var capturedResolver = resolver;
                ThreadPool.QueueUserWorkItem(_ =>
                {
                    try
                    {
                        Logger.Log("Startup", "Phase 5: Starting NSFW content filter...");

                        // Wait for DotNetBrowser assemblies to load
                        if (!WaitFor(() => DotNetBrowserReflection.AreDotNetBrowserAssembliesLoaded(),
                            TimeSpan.FromSeconds(30)))
                        {
                            Logger.Log("Startup", "Phase 5: DotNetBrowser assemblies not found after 30s, skipping");
                            return;
                        }
                        Logger.Log("Startup", "Phase 5: DotNetBrowser assemblies loaded");

                        // Resolve DotNetBrowser types
                        var browserReflection = new DotNetBrowserReflection();
                        if (!browserReflection.Resolve())
                        {
                            Logger.Log("Startup", "Phase 5: DotNetBrowser type resolution failed, skipping");
                            return;
                        }

                        // Initialize NSFW filter
                        var nsfwFilter = new NsfwFilter(capturedResolver, browserReflection,
                            nsfwSettings, capturedWindow);
                        ContentPages.NsfwFilterInstance = nsfwFilter;

                        if (nsfwFilter.Initialize())
                            Logger.Log("Startup", "Phase 5 OK: NSFW content filter active");
                        else
                            Logger.Log("Startup", "Phase 5: NSFW filter init returned false (BrowserView may not be ready yet, will retry via timer)");
                    }
                    catch (Exception ex)
                    {
                        Logger.Log("Startup", $"Phase 5 error: {ex.Message}");
                    }
                });
            }
            else
            {
                Logger.Log("Startup", "Phase 5: NSFW filter disabled or no API key, skipping");
            }
        }
        catch (Exception ex)
        {
            Logger.Log("Startup", $"Fatal error in injector: {ex}");
        }
    }

    private static bool WaitForAvaloniaAssemblies(TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                var name = asm.GetName().Name ?? "";
                if (name.Equals("Avalonia.Controls", StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            Thread.Sleep(250);
        }
        return false;
    }

    private static bool WaitFor(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                if (condition()) return true;
            }
            catch { }
            Thread.Sleep(500);
        }
        return false;
    }
}
