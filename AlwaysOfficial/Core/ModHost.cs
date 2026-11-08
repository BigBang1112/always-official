namespace AlwaysOfficial.Core;

internal static class ModHost
{
    private static nint instanceMutex;

    public static bool Boot(nint module)
    {
        // One instance per process, e.g. when both the ModLoader and the standalone proxy load a copy.
        var mutex = Win32.CreateMutex(0, false, $"Local\\{ModInfo.Name}_{Win32.GetCurrentProcessId()}");
        if (mutex == 0)
        {
            return false;
        }

        if (System.Runtime.InteropServices.Marshal.GetLastPInvokeError() == Win32.ERROR_ALREADY_EXISTS)
        {
            Win32.CloseHandle(mutex);
            return false;
        }

        // Held for the process lifetime; only the instance that created the mutex gets here.
        instanceMutex = mutex;

        Log.Init(ModInfo.Name);
        Log.Write($"{ModInfo.DisplayName} {ModInfo.Version}: {Win32.ModulePath(0)}");

        return Mod.Install();
    }
}
