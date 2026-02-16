using Uprooted;
internal class StartupHook
{
    public static void Initialize()
    {
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
            Logger.Log("Startup", "=== Uprooted Hook v0.1.92 Loaded ===");
            Logger.Log("Startup", "========================================");
            Logger.Log("Startup", $"Process: {Environment.ProcessPath}");
            Logger.Log("Startup", $"PID: {Environment.ProcessId}");
            Logger.Log("Startup", $".NET: {Environment.Version}");
            Logger.Log("Startup", "Phase 1: Waiting for Avalonia assemblies...");
            if (!WaitForAvaloniaAssemblies(TimeSpan.FromSeconds(30)))
            {
                Logger.Log("Startup", "Phase 1 FAILED: Avalonia assemblies not found after 30s");
                return;
            }
            Logger.Log("Startup", "Phase 1 OK: Avalonia assemblies loaded");
            var resolver = new AvaloniaReflection();
            if (!resolver.Resolve())
            {
                Logger.Log("Startup", "Type resolution failed, aborting");
                return;
            }
            Logger.Log("Startup", "Phase 2: Waiting for Application.Current...");
            if (!WaitFor(() => resolver.GetAppCurrent() != null, TimeSpan.FromSeconds(30)))
            {
                Logger.Log("Startup", "Phase 2 FAILED: Application.Current not available after 30s");
                return;
            }
            Logger.Log("Startup", "Phase 2 OK: Application.Current is set");
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
            Logger.Log("Startup", "Phase 3.5: Initializing theme engine");
            var themeEngine = new ThemeEngine(resolver);
            var savedSettings = UprootedSettings.Load();
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
                }
                catch (Exception ex)
                {
                    Logger.Log("Startup", "Theme init error: " + ex.Message);
                }
            });
            Logger.Log("Startup", "Phase 4: Starting settings page monitor");
            var injector = new SidebarInjector(resolver, mainWindow!, themeEngine);
            injector.StartMonitoring();
            Logger.Log("Startup", "========================================");
            Logger.Log("Startup", "=== Uprooted Hook Ready ===");
            Logger.Log("Startup", "========================================");
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