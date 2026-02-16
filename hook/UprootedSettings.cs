namespace Uprooted;
internal class UprootedSettings
{
    public bool Enabled { get; set; } = true;
    public string Version { get; set; } = "0.9.15";
    public string ActiveTheme { get; set; } = "default-dark";
    public Dictionary<string, bool> Plugins { get; set; } = new();
    public string CustomCss { get; set; } = "";
    public string CustomAccent { get; set; } = "#3B6AF8";
    public string CustomBackground { get; set; } = "#0D1521";
    private static string? _settingsPath;
    private static string GetSettingsPath()
    {
        if (_settingsPath != null) return _settingsPath;
        try
        {
            var profileDir = PlatformPaths.GetProfileDir();
            _settingsPath = Path.Combine(profileDir, "uprooted-settings.ini");
        }
        catch
        {
            _settingsPath = "uprooted-settings.ini";
        }
        return _settingsPath;
    }
    internal static UprootedSettings Load()
    {
        var settings = new UprootedSettings();
        var path = GetSettingsPath();
        if (!File.Exists(path)) return settings;
        try
        {
            foreach (var line in File.ReadAllLines(path))
            {
                var eq = line.IndexOf('=');
                if (eq < 0) continue;
                var key = line[..eq].Trim();
                var val = line[(eq + 1)..].Trim();
                switch (key)
                {
                    case "ActiveTheme": settings.ActiveTheme = val; break;
                    case "Enabled": settings.Enabled = val == "true"; break;
                    case "CustomCss": settings.CustomCss = val; break;
                    case "CustomAccent": settings.CustomAccent = val; break;
                    case "CustomBackground": settings.CustomBackground = val; break;
                    case var k when k.StartsWith("Plugin."):
                        var pluginName = k["Plugin.".Length..];
                        settings.Plugins[pluginName] = val == "true";
                        break;
                }
            }
            Logger.Log("Settings", $"Loaded settings from {path}: ActiveTheme={settings.ActiveTheme}");
        }
        catch (Exception ex)
        {
            Logger.Log("Settings", $"Failed to load settings: {ex.Message}");
        }
        return settings;
    }
    internal void Save()
    {
        try
        {
            var path = GetSettingsPath();
            var lines = new List<string>
            {
                "ActiveTheme=" + ActiveTheme,
                "Enabled=" + (Enabled ? "true" : "false"),
                "Version=" + Version,
                "CustomCss=" + CustomCss,
                "CustomAccent=" + CustomAccent,
                "CustomBackground=" + CustomBackground
            };
            foreach (var (name, enabled) in Plugins)
            {
                lines.Add($"Plugin.{name}={( enabled ? "true" : "false" )}");
            }
            File.WriteAllText(path, string.Join("\n", lines));
            Logger.Log("Settings", "Saved settings to " + path + ": ActiveTheme=" + ActiveTheme);
        }
        catch (Exception ex)
        {
            Logger.Log("Settings", "Failed to save settings: " + ex.Message);
        }
    }
}