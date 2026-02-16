using System.Runtime.CompilerServices;
using Uprooted;

namespace UprootedHook;

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
