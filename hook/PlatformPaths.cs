namespace Uprooted;

internal static class PlatformPaths
{
    internal static string GetProfileDir()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(localAppData, "Root Communications", "Root", "profile", "default");
    }
}
