using System.Reflection;
using System.Runtime.InteropServices;
using Uprooted;

namespace Uprooted;

public static class NativeEntry
{
    public static int Initialize(IntPtr args, int sizeBytes)
    {
        try
        {
            Logger.Log("NativeEntry", "========================================");
            Logger.Log("NativeEntry", "=== Called from native DLL proxy ===");
            Logger.Log("NativeEntry", "========================================");


            Logger.Log("NativeEntry", $"Executing assembly: {Assembly.GetExecutingAssembly().FullName}");
            Logger.Log("NativeEntry", $"CLR version: {Environment.Version}");
            Logger.Log("NativeEntry", $"Process: {Environment.ProcessPath}");
            Logger.Log("NativeEntry", $"AppDomain: {AppDomain.CurrentDomain.FriendlyName}");


            var asms = AppDomain.CurrentDomain.GetAssemblies();
            Logger.Log("NativeEntry", $"Total loaded assemblies: {asms.Length}");
            foreach (var asm in asms)
            {
                var name = asm.GetName().Name ?? "(null)";
                Logger.Log("NativeEntry", $"  Assembly: {name}");
            }


            bool hasAvalonia = false;
            foreach (var asm in asms)
            {
                var name = asm.GetName().Name ?? "";
                if (name.StartsWith("Avalonia", StringComparison.OrdinalIgnoreCase))
                {
                    hasAvalonia = true;
                    Logger.Log("NativeEntry", $"  *** AVALONIA FOUND: {name}");
                }
            }

            if (!hasAvalonia)
            {
                Logger.Log("NativeEntry", "*** NO AVALONIA ASSEMBLIES - likely in separate runtime!");
                Logger.Log("NativeEntry", "*** Need to use Root.exe's embedded runtime instead.");
            }


            StartupHook.Initialize();

            Logger.Log("NativeEntry", "Initialize returned successfully");
            return 0;
        }
        catch (Exception ex)
        {
            Logger.Log("NativeEntry", $"FATAL: {ex}");
            return 1;
        }
    }
}
