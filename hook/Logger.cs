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

    /// <summary>Returns the full path to the hook log file.</summary>
    internal static string GetLogPath() => LogPath;

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

    /// <summary>Log exception with full inner exception chain for debugging.</summary>
    internal static void LogException(string category, string context, Exception ex)
    {
        Log(category, $"{context}: {ex.GetType().Name}: {ex.Message}");
        var inner = ex.InnerException;
        int depth = 0;
        while (inner != null && depth < 5)
        {
            Log(category, $"  Inner[{depth}]: {inner.GetType().Name}: {inner.Message}");
            inner = inner.InnerException;
            depth++;
        }
        Log(category, $"  StackTrace: {ex.StackTrace}");
    }
}
