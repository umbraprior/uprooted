using System.Runtime.CompilerServices;
using Uprooted;

namespace UprootedHook;

/// <summary>
/// Entry point for profiler-based IL injection.
/// The profiler injects IL that calls Assembly.LoadFrom + Assembly.CreateInstance("UprootedHook.Entry"),
/// which triggers the ModuleInitializer and/or constructor to start the Uprooted injection.
/// </summary>
public class Entry
{
    private static int _initialized = 0;

    [ModuleInitializer]
    internal static void ModuleInit()
    {
        if (Interlocked.CompareExchange(ref _initialized, 1, 0) == 0)
        {
            Logger.Log("Entry", "=== ModuleInitializer triggered ===");
            StartupHook.Initialize();
        }
    }

    public Entry()
    {
        if (Interlocked.CompareExchange(ref _initialized, 1, 0) == 0)
        {
            Logger.Log("Entry", "=== Constructor triggered ===");
            StartupHook.Initialize();
        }
    }
}
