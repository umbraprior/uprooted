namespace Uprooted;
internal static class Logger
{
    private static readonly string LogPath;
    private static readonly object Lock = new();
    static Logger()
    {
        var profileDir = PlatformPaths.GetProfileDir();
        Directory.CreateDirectory(profileDir);
        LogPath = Path.Combine(profileDir, "uprooted-hook.log");
    }
    internal static void Log(string message)
    {
        try
        {
            lock (Lock)
            {
                File.AppendAllText(LogPath, $"[{DateTime.Now:HH:mm:ss.fff}] {message}\n");
            }
        }
        catch { }
    }
    internal static void Log(string category, string message) => Log($"[{category}] {message}");
}